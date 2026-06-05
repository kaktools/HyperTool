namespace HyperTool.Models;

public enum RefreshReason
{
    Unknown = 0,
    Manual = 1,
    Connect = 2,
    Disconnect = 3,
    PushNotification = 4,
    Background = 5,
    Startup = 6
}
