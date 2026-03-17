using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace HyperTool.Services;

public static class HyperVSocketConnectionMetrics
{
    private static long _openedConnections;
    private static long _closedConnections;
    private static long _activeConnections;
    private static long _failedConnects;
    private static long _reconnectAttempts;
    private static long _requestsCoalesced;
    private static long _backoffActivations;
    private static long _circuitBreakerOpen;
    private static readonly ConcurrentDictionary<string, long> PurposeOpenConnections = new(StringComparer.OrdinalIgnoreCase);

    public static void OnOpened(string purpose)
    {
        Interlocked.Increment(ref _openedConnections);
        Interlocked.Increment(ref _activeConnections);
        PurposeOpenConnections.AddOrUpdate(purpose, 1, (_, value) => value + 1);
    }

    public static void OnClosed(string purpose)
    {
        Interlocked.Increment(ref _closedConnections);
        Interlocked.Decrement(ref _activeConnections);
        PurposeOpenConnections.AddOrUpdate(purpose, 0, (_, value) => Math.Max(0, value - 1));
    }

    public static void OnFailedConnect()
    {
        Interlocked.Increment(ref _failedConnects);
    }

    public static void OnReconnectAttempt()
    {
        Interlocked.Increment(ref _reconnectAttempts);
    }

    public static void OnRequestCoalesced()
    {
        Interlocked.Increment(ref _requestsCoalesced);
    }

    public static void OnBackoffActivated()
    {
        Interlocked.Increment(ref _backoffActivations);
    }

    public static void OnCircuitBreakerOpen()
    {
        Interlocked.Increment(ref _circuitBreakerOpen);
    }

    public static HyperVSocketConnectionSnapshot GetSnapshot()
    {
        return new HyperVSocketConnectionSnapshot
        {
            OpenedConnections = Interlocked.Read(ref _openedConnections),
            ClosedConnections = Interlocked.Read(ref _closedConnections),
            ActiveConnections = Interlocked.Read(ref _activeConnections),
            FailedConnects = Interlocked.Read(ref _failedConnects),
            ReconnectAttempts = Interlocked.Read(ref _reconnectAttempts),
            RequestsCoalesced = Interlocked.Read(ref _requestsCoalesced),
            BackoffActivations = Interlocked.Read(ref _backoffActivations),
            CircuitBreakerOpen = Interlocked.Read(ref _circuitBreakerOpen),
            OpenConnectionsByPurpose = PurposeOpenConnections.ToDictionary(static entry => entry.Key, static entry => entry.Value, StringComparer.OrdinalIgnoreCase)
        };
    }
}

public sealed class HyperVSocketConnectionSnapshot
{
    public long OpenedConnections { get; init; }
    public long ClosedConnections { get; init; }
    public long ActiveConnections { get; init; }
    public long FailedConnects { get; init; }
    public long ReconnectAttempts { get; init; }
    public long RequestsCoalesced { get; init; }
    public long BackoffActivations { get; init; }
    public long CircuitBreakerOpen { get; init; }
    public IReadOnlyDictionary<string, long> OpenConnectionsByPurpose { get; init; } = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
}
