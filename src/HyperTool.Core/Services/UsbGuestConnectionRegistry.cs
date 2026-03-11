using System.Collections.Concurrent;

namespace HyperTool.Services;

public static class UsbGuestConnectionRegistry
{
    private sealed class GuestConnectionEntry
    {
        public string GuestComputerName { get; init; } = string.Empty;
        public DateTimeOffset LastSeenUtc { get; init; }
    }

    private static readonly ConcurrentDictionary<string, GuestConnectionEntry> ConnectedGuestsByDeviceKey = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, string> DeviceKeyByBusId = new(StringComparer.OrdinalIgnoreCase);

    public static void UpdateFromDiagnosticsAck(HyperVSocketDiagnosticsAck ack)
    {
        if (ack is null)
        {
            return;
        }

        var busId = (ack.BusId ?? string.Empty).Trim();
        var deviceKey = BuildDeviceIdentityKey(ack);
        var eventType = (ack.EventType ?? string.Empty).Trim();
        var guestComputerName = (ack.GuestComputerName ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(busId) && string.IsNullOrWhiteSpace(deviceKey))
        {
            return;
        }

        if (string.Equals(eventType, "usb-disconnected", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(deviceKey))
            {
                ConnectedGuestsByDeviceKey.TryRemove(deviceKey, out _);
            }

            if (!string.IsNullOrWhiteSpace(busId)
                && DeviceKeyByBusId.TryRemove(busId, out var mappedKey)
                && !string.IsNullOrWhiteSpace(mappedKey))
            {
                ConnectedGuestsByDeviceKey.TryRemove(mappedKey, out _);
            }

            return;
        }

        if ((string.Equals(eventType, "usb-connected", StringComparison.OrdinalIgnoreCase)
             || string.Equals(eventType, "usb-heartbeat", StringComparison.OrdinalIgnoreCase))
            && !string.IsNullOrWhiteSpace(guestComputerName)
            && !string.IsNullOrWhiteSpace(deviceKey))
        {
            ConnectedGuestsByDeviceKey[deviceKey] = new GuestConnectionEntry
            {
                GuestComputerName = guestComputerName,
                LastSeenUtc = DateTimeOffset.UtcNow
            };

            if (!string.IsNullOrWhiteSpace(busId))
            {
                DeviceKeyByBusId[busId] = deviceKey;
            }
        }
    }

    public static bool TryGetGuestComputerName(string? busId, out string guestComputerName)
    {
        guestComputerName = string.Empty;

        if (string.IsNullOrWhiteSpace(busId))
        {
            return false;
        }

        var normalizedBusId = busId.Trim();

        if (!DeviceKeyByBusId.TryGetValue(normalizedBusId, out var deviceKey)
            || string.IsNullOrWhiteSpace(deviceKey))
        {
            deviceKey = "busid:" + normalizedBusId;
        }

        if (!ConnectedGuestsByDeviceKey.TryGetValue(deviceKey, out var entry))
        {
            return false;
        }

        guestComputerName = entry.GuestComputerName;
        return true;
    }

    public static bool TryGetFreshGuestComputerName(string? busId, TimeSpan maxAge, out string guestComputerName)
    {
        guestComputerName = string.Empty;

        if (string.IsNullOrWhiteSpace(busId))
        {
            return false;
        }

        var normalizedBusId = busId.Trim();

        if (!DeviceKeyByBusId.TryGetValue(normalizedBusId, out var deviceKey)
            || string.IsNullOrWhiteSpace(deviceKey))
        {
            deviceKey = "busid:" + normalizedBusId;
        }

        if (!ConnectedGuestsByDeviceKey.TryGetValue(deviceKey, out var entry))
        {
            return false;
        }

        if ((DateTimeOffset.UtcNow - entry.LastSeenUtc) > maxAge)
        {
            return false;
        }

        guestComputerName = entry.GuestComputerName;
        return true;
    }

    private static string BuildDeviceIdentityKey(HyperVSocketDiagnosticsAck ack)
    {
        var hardwareId = NormalizeHardwareId(ack.HardwareId);
        if (!string.IsNullOrWhiteSpace(hardwareId))
        {
            return "hardware:" + hardwareId;
        }

        var persistedGuid = (ack.PersistedGuid ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(persistedGuid))
        {
            return "guid:" + persistedGuid;
        }

        var instanceId = (ack.InstanceId ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(instanceId))
        {
            return "instance:" + instanceId;
        }

        var busId = (ack.BusId ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(busId))
        {
            return "busid:" + busId;
        }

        return string.Empty;
    }

    private static string NormalizeHardwareId(string? hardwareId)
    {
        if (string.IsNullOrWhiteSpace(hardwareId))
        {
            return string.Empty;
        }

        return hardwareId.Trim().ToUpperInvariant();
    }
}
