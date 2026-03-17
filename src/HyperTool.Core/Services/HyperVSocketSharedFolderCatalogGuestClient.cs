using HyperTool.Models;

namespace HyperTool.Services;

public sealed class HyperVSocketSharedFolderCatalogGuestClient
{
    private static readonly HyperVControlChannel SharedControlChannel = new();
    private readonly HyperVControlChannel _controlChannel;

    public HyperVSocketSharedFolderCatalogGuestClient(Guid? serviceId = null)
    {
        _controlChannel = serviceId.HasValue
            ? new HyperVControlChannel(sharedCatalogServiceId: serviceId)
            : SharedControlChannel;
    }

    public Task<IReadOnlyList<HostSharedFolderDefinition>> FetchCatalogAsync(CancellationToken cancellationToken)
    {
        return _controlChannel.FetchSharedFolderCatalogAsync(cancellationToken);
    }
}
