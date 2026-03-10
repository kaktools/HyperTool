namespace HyperTool.Models;

public sealed class HostFeatureAvailability
{
    public bool UsbSharingEnabled { get; set; } = true;

    public bool SharedFoldersEnabled { get; set; } = true;

    public List<UsbDeviceMetadataEntry> UsbDeviceMetadata { get; set; } = [];

    public List<UsbDeviceHostDescriptionEntry> UsbDeviceDescriptions { get; set; } = [];
}

public sealed class UsbDeviceHostDescriptionEntry
{
    public string DeviceKey { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;
}

public sealed class HostIdentityInfo
{
    public string HostName { get; set; } = string.Empty;

    public string Fqdn { get; set; } = string.Empty;

    public HostFeatureAvailability Features { get; set; } = new();
}