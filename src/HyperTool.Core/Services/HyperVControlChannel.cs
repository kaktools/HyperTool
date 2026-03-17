using HyperTool.Models;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace HyperTool.Services;

public sealed class HyperVControlChannel : IAsyncDisposable, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly PersistentHyperVConnection _hostIdentityConnection;
    private readonly PersistentHyperVConnection _sharedCatalogConnection;
    private readonly SemaphoreSlim _hostIdentityCoalesceGate = new(1, 1);
    private readonly SemaphoreSlim _sharedCatalogCoalesceGate = new(1, 1);
    private Task<HostIdentityInfo?>? _inflightHostIdentity;
    private Task<IReadOnlyList<HostSharedFolderDefinition>>? _inflightSharedCatalog;
    private HostIdentityInfo? _cachedHostIdentity;
    private DateTimeOffset _hostIdentityCacheUntilUtc = DateTimeOffset.MinValue;

    public HyperVControlChannel(Guid? hostIdentityServiceId = null, Guid? sharedCatalogServiceId = null)
    {
        _hostIdentityConnection = new PersistentHyperVConnection(
            hostIdentityServiceId ?? HyperVSocketUsbTunnelDefaults.HostIdentityServiceId,
            "control-hostidentity");
        _sharedCatalogConnection = new PersistentHyperVConnection(
            sharedCatalogServiceId ?? HyperVSocketUsbTunnelDefaults.SharedFolderCatalogServiceId,
            "control-sharedcatalog");
    }

    public async Task<HostIdentityInfo?> FetchHostIdentityAsync(CancellationToken cancellationToken, bool forceRefresh = false)
    {
        if (!forceRefresh && _cachedHostIdentity is not null && DateTimeOffset.UtcNow <= _hostIdentityCacheUntilUtc)
        {
            return _cachedHostIdentity;
        }

        await _hostIdentityCoalesceGate.WaitAsync(cancellationToken);
        try
        {
            if (!forceRefresh && _cachedHostIdentity is not null && DateTimeOffset.UtcNow <= _hostIdentityCacheUntilUtc)
            {
                return _cachedHostIdentity;
            }

            if (_inflightHostIdentity is { IsCompleted: false } inflight)
            {
                HyperVSocketConnectionMetrics.OnRequestCoalesced();
                return await inflight;
            }

            _inflightHostIdentity = FetchHostIdentityCoreAsync(cancellationToken);
            return await _inflightHostIdentity;
        }
        finally
        {
            _hostIdentityCoalesceGate.Release();
        }
    }

    private async Task<HostIdentityInfo?> FetchHostIdentityCoreAsync(CancellationToken cancellationToken)
    {
        var payload = await _hostIdentityConnection.SendAndReceiveLineAsync("{}", cancellationToken);
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        var parsed = JsonSerializer.Deserialize<HostIdentityWirePayload>(payload, JsonOptions);
        if (parsed is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(parsed.HostName) && string.IsNullOrWhiteSpace(parsed.Fqdn))
        {
            return null;
        }

        _cachedHostIdentity = new HostIdentityInfo
        {
            HostName = parsed.HostName?.Trim() ?? string.Empty,
            Fqdn = parsed.Fqdn?.Trim() ?? string.Empty,
            Features = parsed.Features ?? new HostFeatureAvailability()
        };
        _hostIdentityCacheUntilUtc = DateTimeOffset.UtcNow.AddSeconds(45);
        return _cachedHostIdentity;
    }

    public async Task<IReadOnlyList<HostSharedFolderDefinition>> FetchSharedFolderCatalogAsync(CancellationToken cancellationToken)
    {
        await _sharedCatalogCoalesceGate.WaitAsync(cancellationToken);
        try
        {
            if (_inflightSharedCatalog is { IsCompleted: false } inflight)
            {
                HyperVSocketConnectionMetrics.OnRequestCoalesced();
                return await inflight;
            }

            _inflightSharedCatalog = FetchSharedFolderCatalogCoreAsync(cancellationToken);
            return await _inflightSharedCatalog;
        }
        finally
        {
            _sharedCatalogCoalesceGate.Release();
        }
    }

    private async Task<IReadOnlyList<HostSharedFolderDefinition>> FetchSharedFolderCatalogCoreAsync(CancellationToken cancellationToken)
    {
        var payload = await _sharedCatalogConnection.SendAndReceiveLineAsync("{}", cancellationToken);
        if (string.IsNullOrWhiteSpace(payload))
        {
            return [];
        }

        var catalog = JsonSerializer.Deserialize<List<HostSharedFolderDefinition>>(payload, JsonOptions) ?? [];
        return catalog
            .Where(static item => item is not null
                                  && !string.IsNullOrWhiteSpace(item.Id)
                                  && !string.IsNullOrWhiteSpace(item.ShareName))
            .Select(static item => new HostSharedFolderDefinition
            {
                Id = item.Id,
                Label = item.Label,
                LocalPath = item.LocalPath,
                ShareName = item.ShareName,
                Enabled = item.Enabled,
                ReadOnly = item.ReadOnly
            })
            .ToList();
    }

    public void Dispose()
    {
        _hostIdentityConnection.Dispose();
        _sharedCatalogConnection.Dispose();
        _hostIdentityCoalesceGate.Dispose();
        _sharedCatalogCoalesceGate.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private sealed class HostIdentityWirePayload
    {
        public string HostName { get; set; } = string.Empty;
        public string Fqdn { get; set; } = string.Empty;
        public HostFeatureAvailability? Features { get; set; }
    }
}
