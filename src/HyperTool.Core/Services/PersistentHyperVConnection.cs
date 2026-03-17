using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace HyperTool.Services;

public sealed class PersistentHyperVConnection : IAsyncDisposable, IDisposable
{
    private readonly Guid _serviceId;
    private readonly string _purpose;
    private readonly HyperVSocketConnectionOptions _options;
    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private readonly SemaphoreSlim _ioGate = new(1, 1);
    private readonly Random _random = new();
    private Socket? _socket;
    private NetworkStream? _stream;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private string _connectionId = string.Empty;
    private DateTimeOffset _openedAtUtc;
    private DateTimeOffset _circuitOpenUntilUtc = DateTimeOffset.MinValue;

    public PersistentHyperVConnection(Guid serviceId, string purpose, HyperVSocketConnectionOptions? options = null)
    {
        _serviceId = serviceId;
        _purpose = string.IsNullOrWhiteSpace(purpose) ? "hyperv" : purpose.Trim();
        _options = options ?? new HyperVSocketConnectionOptions();
    }

    public string ConnectionId => _connectionId;

    public bool IsConnected => _socket is { Connected: true };

    public async Task<string?> SendAndReceiveLineAsync(string requestLine, CancellationToken cancellationToken)
    {
        await _ioGate.WaitAsync(cancellationToken);
        try
        {
            for (var attempt = 1; attempt <= 2; attempt++)
            {
                await EnsureConnectedAsync(cancellationToken);

                try
                {
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    linkedCts.CancelAfter(_options.RequestTimeout);
                    var token = linkedCts.Token;

                    await _writer!.WriteLineAsync(requestLine.AsMemory(), token);
                    await _writer.FlushAsync(token);
                    var response = await _reader!.ReadLineAsync(token);
                    if (response is not null)
                    {
                        return response;
                    }

                    throw new IOException("Hyper-V connection closed by remote host.");
                }
                catch (Exception ex) when (attempt < 2 && IsConnectionFault(ex))
                {
                    FaultConnection(ex);
                }
            }

            return null;
        }
        finally
        {
            _ioGate.Release();
        }
    }

    public async Task SendLineAsync(string payload, CancellationToken cancellationToken)
    {
        await _ioGate.WaitAsync(cancellationToken);
        try
        {
            for (var attempt = 1; attempt <= 2; attempt++)
            {
                await EnsureConnectedAsync(cancellationToken);

                try
                {
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    linkedCts.CancelAfter(_options.RequestTimeout);
                    var token = linkedCts.Token;

                    await _writer!.WriteLineAsync(payload.AsMemory(), token);
                    await _writer.FlushAsync(token);
                    return;
                }
                catch (Exception ex) when (attempt < 2 && IsConnectionFault(ex))
                {
                    FaultConnection(ex);
                }
            }
        }
        finally
        {
            _ioGate.Release();
        }
    }

    public async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (IsConnected)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (now < _circuitOpenUntilUtc)
        {
            throw new SocketException((int)SocketError.NoBufferSpaceAvailable);
        }

        await _connectGate.WaitAsync(cancellationToken);
        try
        {
            if (IsConnected)
            {
                return;
            }

            Exception? lastError = null;
            for (var attempt = 1; attempt <= _options.MaxConnectAttempts; attempt++)
            {
                HyperVSocketConnectionMetrics.OnReconnectAttempt();
                try
                {
                    var socket = new Socket((AddressFamily)34, SocketType.Stream, (ProtocolType)1);
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    linkedCts.CancelAfter(_options.ConnectTimeout);
                    var endpoint = new HyperVSocketEndPoint(HyperVSocketUsbTunnelDefaults.VmIdParent, _serviceId);
                    linkedCts.Token.ThrowIfCancellationRequested();
                    var connectTask = Task.Run(() => socket.Connect(endpoint), CancellationToken.None);
                    await connectTask.WaitAsync(linkedCts.Token);
                    linkedCts.Token.ThrowIfCancellationRequested();

                    var stream = new NetworkStream(socket, ownsSocket: true);
                    var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 16 * 1024, leaveOpen: true);
                    var writer = new StreamWriter(stream, Encoding.UTF8, bufferSize: 16 * 1024, leaveOpen: true)
                    {
                        NewLine = "\n"
                    };

                    _socket = socket;
                    _stream = stream;
                    _reader = reader;
                    _writer = writer;
                    _connectionId = Guid.NewGuid().ToString("N");
                    _openedAtUtc = DateTimeOffset.UtcNow;
                    HyperVSocketConnectionMetrics.OnOpened(_purpose);
                    return;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    HyperVSocketConnectionMetrics.OnFailedConnect();

                    if (GetSocketException(ex) is { } socketException
                        && socketException.SocketErrorCode == SocketError.NoBufferSpaceAvailable)
                    {
                        _circuitOpenUntilUtc = DateTimeOffset.UtcNow + _options.NoBufferCircuitCooldown;
                        HyperVSocketConnectionMetrics.OnCircuitBreakerOpen();
                    }

                    if (attempt >= _options.MaxConnectAttempts || !IsTransientConnectFailure(ex))
                    {
                        break;
                    }

                    HyperVSocketConnectionMetrics.OnBackoffActivated();
                    await Task.Delay(ComputeBackoff(attempt), cancellationToken);
                }
            }

            throw new InvalidOperationException($"Failed to connect Hyper-V channel '{_purpose}'.", lastError);
        }
        finally
        {
            _connectGate.Release();
        }
    }

    private TimeSpan ComputeBackoff(int attempt)
    {
        var growthFactor = Math.Pow(2, Math.Max(0, attempt - 1));
        var backoffMs = Math.Min(_options.MaxBackoff.TotalMilliseconds, _options.InitialBackoff.TotalMilliseconds * growthFactor);
        var jitterMs = _random.Next(30, 220);
        return TimeSpan.FromMilliseconds(backoffMs + jitterMs);
    }

    private static bool IsConnectionFault(Exception exception)
    {
        return exception is IOException or ObjectDisposedException or OperationCanceledException or SocketException
               || exception.InnerException is SocketException;
    }

    private static bool IsTransientConnectFailure(Exception exception)
    {
        if (GetSocketException(exception) is not { } socketException)
        {
            return false;
        }

        return HyperVSocketTransientErrors.IsTransientConnectSocketError(socketException);
    }

    private static SocketException? GetSocketException(Exception exception)
    {
        var current = exception;
        while (current is not null)
        {
            if (current is SocketException socketException)
            {
                return socketException;
            }

            current = current.InnerException;
        }

        return null;
    }

    private void FaultConnection(Exception? cause)
    {
        _ = cause;
        CloseConnection();
    }

    private void CloseConnection()
    {
        var hadConnection = _socket is not null || _stream is not null || _reader is not null || _writer is not null;

        try
        {
            _writer?.Dispose();
        }
        catch
        {
        }

        try
        {
            _reader?.Dispose();
        }
        catch
        {
        }

        try
        {
            _stream?.Dispose();
        }
        catch
        {
        }

        try
        {
            _socket?.Dispose();
        }
        catch
        {
        }

        _writer = null;
        _reader = null;
        _stream = null;
        _socket = null;

        if (hadConnection)
        {
            HyperVSocketConnectionMetrics.OnClosed(_purpose);
        }
    }

    public void Dispose()
    {
        CloseConnection();
        _connectGate.Dispose();
        _ioGate.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
