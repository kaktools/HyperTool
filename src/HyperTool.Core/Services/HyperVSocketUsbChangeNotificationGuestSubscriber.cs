using HyperTool.Models;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace HyperTool.Services;

/// <summary>
/// Guest-side subscriber that connects to the host's USB change-notification service
/// and invokes a callback whenever a <c>usb-share-changed</c> event is received.
/// Returns (without throwing) when the connection is closed by the host or when
/// <paramref name="cancellationToken"/> is cancelled.
/// </summary>
public sealed class HyperVSocketUsbChangeNotificationGuestSubscriber
{
    private static readonly JsonSerializerOptions PayloadJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly Guid _serviceId;

    public HyperVSocketUsbChangeNotificationGuestSubscriber(Guid? serviceId = null)
    {
        _serviceId = serviceId ?? HyperVSocketUsbTunnelDefaults.UsbChangeNotificationServiceId;
    }

    /// <summary>
    /// Connects to the host notification service and blocks until the connection drops
    /// or <paramref name="cancellationToken"/> is cancelled. For each
    /// <c>usb-share-changed</c> event line received, <paramref name="onUsbShareChanged"/>
    /// is invoked synchronously before resuming the read loop.
    /// <paramref name="onConnected"/> is invoked once right after the socket connects.
    /// </summary>
    public async Task SubscribeAsync(
        Action<UsbChangeNotificationEnvelope> onUsbShareChanged,
        CancellationToken cancellationToken,
        Action? onConnected = null)
    {
        using var socket = new Socket((AddressFamily)34, SocketType.Stream, (ProtocolType)1);

        cancellationToken.ThrowIfCancellationRequested();

        // Synchronous connect — consistent with other HyperV Socket guest clients.
        var endpoint = new HyperVSocketEndPoint(HyperVSocketUsbTunnelDefaults.VmIdParent, _serviceId);
        socket.Connect(endpoint);
        onConnected?.Invoke();

        await using var stream = new NetworkStream(socket, ownsSocket: true);
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 256,
            leaveOpen: false);

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break; // EOF — host closed the connection.
            }

            if (TryParseUsbShareChangedEvent(line, out var notification))
            {
                onUsbShareChanged(notification);
            }
        }
    }

    private static bool TryParseUsbShareChangedEvent(string line, out UsbChangeNotificationEnvelope notification)
    {
        notification = new UsbChangeNotificationEnvelope();

        try
        {
            var envelope = JsonSerializer.Deserialize<UsbChangeNotificationEnvelope>(line, PayloadJsonOptions);
            if (envelope?.Event is not null
                && envelope.Event.Equals("usb-share-changed", StringComparison.OrdinalIgnoreCase))
            {
                notification = new UsbChangeNotificationEnvelope
                {
                    Event = envelope.Event,
                    EventId = (envelope.EventId ?? string.Empty).Trim(),
                    HasCatalogSnapshot = envelope.HasCatalogSnapshot,
                    HostDevices = (envelope.HostDevices ?? [])
                        .Where(device => device is not null)
                        .ToList()
                };
                return true;
            }
        }
        catch
        {
            // Fall back to legacy payload detection.
        }

        if (line.Contains("usb-share-changed", StringComparison.OrdinalIgnoreCase))
        {
            notification = new UsbChangeNotificationEnvelope
            {
                Event = "usb-share-changed",
                EventId = string.Empty,
                HasCatalogSnapshot = false,
                HostDevices = []
            };
            return true;
        }

        return false;
    }
}
