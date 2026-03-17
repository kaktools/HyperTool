using System.Net;
using System.Net.Sockets;

namespace HyperTool.Services;

public sealed class HyperVSocketUsbGuestProxy : IDisposable
{
    private readonly Guid _serviceId;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoopTask;

    public HyperVSocketUsbGuestProxy(Guid? serviceId = null)
    {
        _serviceId = serviceId ?? HyperVSocketUsbTunnelDefaults.ServiceId;
    }

    public bool IsRunning { get; private set; }

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        var listener = new TcpListener(IPAddress.Loopback, HyperVSocketUsbTunnelDefaults.UsbIpTcpPort)
        {
            Server =
            {
                ExclusiveAddressUse = true
            }
        };

        listener.Start(64);

        _listener = listener;
        _cts = new CancellationTokenSource();
        _acceptLoopTask = Task.Run(() => AcceptLoopAsync(_cts.Token));
        IsRunning = true;
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient? tcpClient = null;
            try
            {
                if (_listener is null)
                {
                    break;
                }

                tcpClient = await _listener.AcceptTcpClientAsync(cancellationToken);
                SafeFireAndForget.Run(HandleClientSafelyAsync(tcpClient, cancellationToken), operation: "usb-guest-proxy-client");
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                tcpClient?.Dispose();
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    private async Task HandleClientSafelyAsync(TcpClient tcpClient, CancellationToken cancellationToken)
    {
        try
        {
            await HandleClientAsync(tcpClient, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch
        {
            // Keep accept loop resilient: one failed tunnel must not fault the process.
        }
        finally
        {
            try
            {
                tcpClient.Dispose();
            }
            catch
            {
            }
        }
    }

    private async Task HandleClientAsync(TcpClient tcpClient, CancellationToken cancellationToken)
    {
        await using var guestTcpStream = tcpClient.GetStream();
        using var hyperVSocket = new Socket((AddressFamily)34, SocketType.Stream, (ProtocolType)1);

        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connectCts.CancelAfter(TimeSpan.FromMilliseconds(2500));
        connectCts.Token.ThrowIfCancellationRequested();
        await ConnectWithRetryAsync(hyperVSocket, new HyperVSocketEndPoint(HyperVSocketUsbTunnelDefaults.VmIdParent, _serviceId), connectCts.Token);

        await using var hyperVStream = new NetworkStream(hyperVSocket, ownsSocket: true);

        var toHostTask = guestTcpStream.CopyToAsync(hyperVStream, cancellationToken);
        var fromHostTask = hyperVStream.CopyToAsync(guestTcpStream, cancellationToken);

        await Task.WhenAll(toHostTask, fromHostTask);
    }

    private static async Task ConnectWithRetryAsync(Socket socket, EndPoint endpoint, CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;
        var delays = new[]
        {
            TimeSpan.FromMilliseconds(75),
            TimeSpan.FromMilliseconds(220)
        };

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var gateLease = await HyperVSocketClientConcurrencyGate.AcquireAsync(cancellationToken);
                var connectTask = Task.Run(() => socket.Connect(endpoint), CancellationToken.None);
                await connectTask.WaitAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                return;
            }
            catch (SocketException ex) when (attempt < maxAttempts && IsTransientConnectSocketError(ex))
            {
                await Task.Delay(delays[Math.Min(attempt - 1, delays.Length - 1)], cancellationToken);
            }
        }
    }

    private static bool IsTransientConnectSocketError(SocketException ex)
    {
        return ex.SocketErrorCode is SocketError.NoBufferSpaceAvailable
            or SocketError.TryAgain
            or SocketError.TimedOut
            or SocketError.ConnectionRefused
            or SocketError.NetworkDown
            or SocketError.NetworkUnreachable
            or SocketError.HostDown
            or SocketError.HostUnreachable;
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
            _listener?.Stop();
        }
        catch
        {
        }

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