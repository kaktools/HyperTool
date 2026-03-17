using System.Text.Json;

namespace HyperTool.Services;

public sealed class HyperVEventChannel : IAsyncDisposable, IDisposable
{
    private readonly PersistentHyperVConnection _connection;

    public HyperVEventChannel(Guid? diagnosticsServiceId = null)
    {
        _connection = new PersistentHyperVConnection(
            diagnosticsServiceId ?? HyperVSocketUsbTunnelDefaults.DiagnosticsServiceId,
            "event-diagnostics");
    }

    public Task SendDiagnosticsAckAsync(HyperVSocketDiagnosticsAck ack, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ack);

        var payload = JsonSerializer.Serialize(ack);
        return _connection.SendLineAsync(payload, cancellationToken);
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
