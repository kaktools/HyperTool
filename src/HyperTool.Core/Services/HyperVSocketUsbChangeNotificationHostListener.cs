using HyperTool.Models;
using Microsoft.Win32;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace HyperTool.Services;

/// <summary>
/// Host-side listener that keeps long-lived subscriber connections open and pushes
/// a <c>usb-share-changed</c> event to all connected guest subscribers whenever
/// <see cref="BroadcastAsync"/> is called.
/// </summary>
public sealed class HyperVSocketUsbChangeNotificationHostListener : IDisposable
{
    private const int MaxConcurrentSubscribers = 64;
    private static readonly JsonSerializerOptions PayloadJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private sealed class SubscriberEntry
    {
        public Guid Id { get; } = Guid.NewGuid();
        public required Socket Socket { get; init; }
    }

    private readonly Guid _serviceId;
    private readonly Func<IReadOnlyList<UsbIpDeviceInfo>>? _usbDeviceSnapshotProvider;
    private readonly ConcurrentDictionary<Guid, SubscriberEntry> _subscribers = new();
    private readonly SemaphoreSlim _subscriberGate = new(MaxConcurrentSubscribers, MaxConcurrentSubscribers);
    private Socket? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoopTask;

    public HyperVSocketUsbChangeNotificationHostListener(
        Guid? serviceId = null,
        Func<IReadOnlyList<UsbIpDeviceInfo>>? usbDeviceSnapshotProvider = null)
    {
        _serviceId = serviceId ?? HyperVSocketUsbTunnelDefaults.UsbChangeNotificationServiceId;
        _usbDeviceSnapshotProvider = usbDeviceSnapshotProvider;
    }

    public bool IsRunning { get; private set; }

    public int SubscriberCount => _subscribers.Count;

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        TryRegisterServiceGuid();

        var listener = new Socket((AddressFamily)34, SocketType.Stream, (ProtocolType)1);
        listener.Bind(new HyperVSocketEndPoint(HyperVSocketUsbTunnelDefaults.VmIdWildcard, _serviceId));
        listener.Listen(16);

        _listener = listener;
        _cts = new CancellationTokenSource();
        _acceptLoopTask = Task.Run(() => AcceptLoopAsync(_cts.Token));
        IsRunning = true;
    }

    /// <summary>
    /// Sends <c>usb-share-changed</c> to all currently connected subscribers.
    /// Disconnected subscribers are pruned automatically.
    /// </summary>
    public async Task BroadcastAsync(CancellationToken cancellationToken = default)
    {
        if (_subscribers.IsEmpty)
        {
            return;
        }

        var payload = BuildUsbShareChangedPayload(_usbDeviceSnapshotProvider?.Invoke() ?? []);

        var toRemove = new List<Guid>();

        foreach (var (id, entry) in _subscribers)
        {
            try
            {
                await entry.Socket.SendAsync(payload, SocketFlags.None, cancellationToken);
            }
            catch
            {
                toRemove.Add(id);
            }
        }

        foreach (var id in toRemove)
        {
            if (_subscribers.TryRemove(id, out var dead))
            {
                try { dead.Socket.Dispose(); } catch { }
            }
        }
    }

    private static byte[] BuildUsbShareChangedPayload(IReadOnlyList<UsbIpDeviceInfo> hostDevices)
    {
        var envelope = new UsbChangeNotificationEnvelope
        {
            EventId = Guid.NewGuid().ToString("N"),
            HasCatalogSnapshot = true,
            HostDevices = hostDevices
                .Where(device => device is not null)
                .Select(device => new UsbIpDeviceInfo
                {
                    BusId = (device.BusId ?? string.Empty).Trim(),
                    Description = (device.Description ?? string.Empty).Trim(),
                    HardwareId = (device.HardwareId ?? string.Empty).Trim(),
                    HardwareIdentityKey = (device.HardwareIdentityKey ?? string.Empty).Trim(),
                    InstanceId = (device.InstanceId ?? string.Empty).Trim(),
                    PersistedGuid = (device.PersistedGuid ?? string.Empty).Trim(),
                    ClientIpAddress = (device.ClientIpAddress ?? string.Empty).Trim(),
                    AttachedGuestComputerName = (device.AttachedGuestComputerName ?? string.Empty).Trim(),
                    DeviceIdentityKey = (device.DeviceIdentityKey ?? string.Empty).Trim(),
                    CustomName = (device.CustomName ?? string.Empty).Trim(),
                    CustomComment = (device.CustomComment ?? string.Empty).Trim(),
                    IsAttachedByOtherGuest = device.IsAttachedByOtherGuest,
                    IsGuestConnectionBlocked = device.IsGuestConnectionBlocked,
                    IsAttachedInCurrentGuest = device.IsAttachedInCurrentGuest
                })
                .Where(device => !string.IsNullOrWhiteSpace(device.BusId))
                .ToList()
        };

        var json = JsonSerializer.Serialize(envelope, PayloadJsonOptions);
        return Encoding.UTF8.GetBytes(json + "\n");
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            Socket? socket = null;
            var gateEntered = false;
            try
            {
                if (_listener is null)
                {
                    break;
                }

                await _subscriberGate.WaitAsync(cancellationToken);
                gateEntered = true;
                socket = await _listener.AcceptAsync(cancellationToken);
                SafeFireAndForget.Run(HandleClientAsync(socket, cancellationToken), operation: "usb-change-subscriber-client");
            }
            catch (OperationCanceledException)
            {
                if (gateEntered)
                {
                    _subscriberGate.Release();
                }
                break;
            }
            catch
            {
                socket?.Dispose();
                if (gateEntered)
                {
                    _subscriberGate.Release();
                }
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    private async Task HandleClientAsync(Socket socket, CancellationToken cancellationToken)
    {
        var entry = new SubscriberEntry { Socket = socket };
        _subscribers[entry.Id] = entry;

        try
        {
            var initialPayload = BuildUsbShareChangedPayload(_usbDeviceSnapshotProvider?.Invoke() ?? []);
            await socket.SendAsync(initialPayload, SocketFlags.None, cancellationToken);
        }
        catch
        {
            _subscribers.TryRemove(entry.Id, out _);
            try { socket.Dispose(); } catch { }
            _subscriberGate.Release();
            return;
        }

        // The guest does not send any data on this channel. We just wait for the socket
        // to be closed (ReceiveAsync returning 0) so we can prune it from the subscriber list.
        var buffer = new byte[1];
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var received = await socket.ReceiveAsync(buffer, SocketFlags.None, cancellationToken);
                if (received == 0)
                {
                    break; // Graceful disconnect from guest.
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
        finally
        {
            _subscribers.TryRemove(entry.Id, out _);
            try { socket.Dispose(); } catch { }
            _subscriberGate.Release();
        }
    }

    private void TryRegisterServiceGuid()
    {
        try
        {
            const string rootPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Virtualization\GuestCommunicationServices";
            using var rootKey = Registry.LocalMachine.CreateSubKey(rootPath, writable: true);
            if (rootKey is null)
            {
                return;
            }

            using var serviceKey = rootKey.CreateSubKey(_serviceId.ToString("D"), writable: true);
            serviceKey?.SetValue("ElementName", "HyperTool Hyper-V Socket USB Change Notification", RegistryValueKind.String);
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        if (!IsRunning)
        {
            return;
        }

        IsRunning = false;

        try
        {
            _cts?.Cancel();
        }
        catch
        {
        }

        try
        {
            _listener?.Dispose();
        }
        catch
        {
        }

        foreach (var (_, entry) in _subscribers)
        {
            try { entry.Socket.Dispose(); } catch { }
        }

        _subscribers.Clear();

        try
        {
            _acceptLoopTask?.Wait(TimeSpan.FromMilliseconds(250));
        }
        catch
        {
        }

        _cts?.Dispose();
        _cts = null;
        _listener = null;
        _acceptLoopTask = null;
    }
}
