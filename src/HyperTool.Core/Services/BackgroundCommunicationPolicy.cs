using HyperTool.Models;

namespace HyperTool.Services;

public static class BackgroundCommunicationPolicy
{
    public static bool IsBackgroundCommunicationEnabled(bool usbFeatureEnabled, bool backgroundCommunicationEnabled)
    {
        return usbFeatureEnabled && backgroundCommunicationEnabled;
    }

    public static bool IsAutomaticRefreshReason(RefreshReason reason)
    {
        return reason is RefreshReason.Background or RefreshReason.Startup;
    }

    public static bool IsRefreshAllowed(bool backgroundCommunicationEnabled, RefreshReason reason)
    {
        if (!IsAutomaticRefreshReason(reason))
        {
            return true;
        }

        return backgroundCommunicationEnabled;
    }
}
