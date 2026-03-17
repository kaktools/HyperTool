namespace HyperTool.Services;

public sealed class HyperVTelemetryChannel : IAsyncDisposable, IDisposable
{
    private readonly PersistentHyperVConnection _connection;

    public HyperVTelemetryChannel(Guid? resourceMonitorServiceId = null)
    {
        _connection = new PersistentHyperVConnection(
            resourceMonitorServiceId ?? HyperVSocketUsbTunnelDefaults.ResourceMonitorServiceId,
            "telemetry-resource-monitor");
    }

    public Task SendTelemetryLineAsync(string payload, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return Task.CompletedTask;
        }

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
