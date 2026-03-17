namespace HyperTool.Services;

public sealed class HyperVSocketConnectionOptions
{
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromMilliseconds(2200);
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromMilliseconds(6000);
    public int MaxConnectAttempts { get; init; } = 4;
    public TimeSpan InitialBackoff { get; init; } = TimeSpan.FromMilliseconds(120);
    public TimeSpan MaxBackoff { get; init; } = TimeSpan.FromSeconds(8);
    public TimeSpan NoBufferCircuitCooldown { get; init; } = TimeSpan.FromSeconds(12);
}
