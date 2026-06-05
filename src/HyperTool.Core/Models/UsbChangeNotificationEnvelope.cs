namespace HyperTool.Models;

public sealed class UsbChangeNotificationEnvelope
{
    public string Event { get; init; } = "usb-share-changed";

    public string EventId { get; init; } = string.Empty;

    public bool HasCatalogSnapshot { get; init; }

    public List<UsbIpDeviceInfo> HostDevices { get; init; } = [];
}
