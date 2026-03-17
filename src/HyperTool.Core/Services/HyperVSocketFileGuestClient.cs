using HyperTool.Models;
using System.Collections.Concurrent;
using System.Text.Json;

namespace HyperTool.Services;

public sealed class HyperVSocketFileGuestClient
{
    private static readonly PersistentHyperVConnection SharedDefaultConnection = new(
        HyperVSocketUsbTunnelDefaults.FileServiceId,
        "control-file-service");

    private readonly PersistentHyperVConnection _connection;
    private readonly ConcurrentDictionary<string, Task<HostFileServiceResponse>> _inflightByKey = new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public HyperVSocketFileGuestClient(Guid? serviceId = null)
    {
        _connection = serviceId.HasValue
            ? new PersistentHyperVConnection(serviceId.Value, "control-file-service")
            : SharedDefaultConnection;
    }

    public Task<HostFileServiceResponse> PingAsync(CancellationToken cancellationToken)
    {
        return SendAsync(new HostFileServiceRequest
        {
            Operation = "ping"
        }, cancellationToken);
    }

    public Task<HostFileServiceResponse> ListSharesAsync(CancellationToken cancellationToken)
    {
        return SendAsync(new HostFileServiceRequest
        {
            Operation = "list-shares"
        }, cancellationToken);
    }

    public Task<HostFileServiceResponse> SendAsync(HostFileServiceRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.RequestId))
        {
            request.RequestId = Guid.NewGuid().ToString("N");
        }

        var key = BuildCoalesceKey(request);
        if (_inflightByKey.TryGetValue(key, out var existingTask) && !existingTask.IsCompleted)
        {
            HyperVSocketConnectionMetrics.OnRequestCoalesced();
            return existingTask;
        }

        var startedTask = SendCoreAsync(request, cancellationToken);
        if (!_inflightByKey.TryAdd(key, startedTask))
        {
            if (_inflightByKey.TryGetValue(key, out var inflightTask) && !inflightTask.IsCompleted)
            {
                HyperVSocketConnectionMetrics.OnRequestCoalesced();
                return inflightTask;
            }

            _inflightByKey[key] = startedTask;
        }

        SafeFireAndForget.Run(
            startedTask,
            onError: _ => { },
            operation: "file-request-coalesce-cleanup");

        _ = startedTask.ContinueWith(
            _ => _inflightByKey.TryRemove(key, out _),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        return startedTask;
    }

    private async Task<HostFileServiceResponse> SendCoreAsync(HostFileServiceRequest request, CancellationToken cancellationToken)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCts.CancelAfter(TimeSpan.FromMilliseconds(8000));

        var payload = JsonSerializer.Serialize(request, SerializerOptions);
        var responsePayload = await _connection.SendAndReceiveLineAsync(payload, linkedCts.Token);

        if (string.IsNullOrWhiteSpace(responsePayload))
        {
            throw new InvalidOperationException("Leere Antwort vom HyperTool File-Dienst.");
        }

        var response = JsonSerializer.Deserialize<HostFileServiceResponse>(responsePayload, SerializerOptions)
            ?? new HostFileServiceResponse();

        response.RequestId = string.IsNullOrWhiteSpace(response.RequestId)
            ? request.RequestId
            : response.RequestId;

        return response;
    }

    private static string BuildCoalesceKey(HostFileServiceRequest request)
    {
        var op = (request.Operation ?? string.Empty).Trim().ToLowerInvariant();
        var shareId = (request.ShareId ?? string.Empty).Trim().ToLowerInvariant();
        var relative = (request.RelativePath ?? string.Empty).Trim().ToLowerInvariant();
        var target = (request.TargetRelativePath ?? string.Empty).Trim().ToLowerInvariant();
        var offset = request.Offset;
        var length = request.Length;

        if (op is "metadata" or "list-directory" or "list-shares" or "ping")
        {
            return $"{op}|{shareId}|{relative}";
        }

        return $"{op}|{shareId}|{relative}|{target}|{offset}|{length}|{request.RequestId}";
    }
}
