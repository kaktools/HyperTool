using System.Text.Json;
using Xunit;

namespace HyperTool.Guest.Tests;

public sealed class GuestConfigBackgroundCommunicationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public void Load_OldConfigWithoutBackgroundCommunicationEnabled_DefaultsToFalse()
    {
        var path = CreateTempFilePath();
        try
        {
            File.WriteAllText(path, BuildConfigJson(includeBackgroundCommunicationEnabled: false));

            var config = GuestConfigService.LoadOrCreate(path, out var created);

            Assert.False(created);
            Assert.NotNull(config.Usb);
            Assert.False(config.Usb.BackgroundCommunicationEnabled);
        }
        finally
        {
            SafeDelete(path);
        }
    }

    [Fact]
    public void Load_NewConfigWithBackgroundCommunicationEnabledTrue_ReadsTrue()
    {
        var path = CreateTempFilePath();
        try
        {
            File.WriteAllText(path, BuildConfigJson(includeBackgroundCommunicationEnabled: true, backgroundCommunicationEnabledValue: true));

            var config = GuestConfigService.LoadOrCreate(path, out var created);

            Assert.False(created);
            Assert.NotNull(config.Usb);
            Assert.True(config.Usb.BackgroundCommunicationEnabled);
        }
        finally
        {
            SafeDelete(path);
        }
    }

    [Fact]
    public void Load_NewConfigWithBackgroundCommunicationEnabledFalse_ReadsFalse()
    {
        var path = CreateTempFilePath();
        try
        {
            File.WriteAllText(path, BuildConfigJson(includeBackgroundCommunicationEnabled: true, backgroundCommunicationEnabledValue: false));

            var config = GuestConfigService.LoadOrCreate(path, out var created);

            Assert.False(created);
            Assert.NotNull(config.Usb);
            Assert.False(config.Usb.BackgroundCommunicationEnabled);
        }
        finally
        {
            SafeDelete(path);
        }
    }

    [Fact]
    public void LoadOldConfigThenSave_WritesBackgroundCommunicationEnabledFalse()
    {
        var path = CreateTempFilePath();
        try
        {
            File.WriteAllText(path, BuildConfigJson(includeBackgroundCommunicationEnabled: false));

            var config = GuestConfigService.LoadOrCreate(path, out _);
            GuestConfigService.Save(path, config);

            var savedJson = File.ReadAllText(path);
            using var document = JsonDocument.Parse(savedJson);

            var usbElement = document.RootElement.GetProperty("usb");
            var hasField = usbElement.TryGetProperty("backgroundCommunicationEnabled", out var backgroundElement);

            Assert.True(hasField);
            Assert.False(backgroundElement.GetBoolean());
        }
        finally
        {
            SafeDelete(path);
        }
    }

    private static string CreateTempFilePath()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "HyperTool.Guest.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(baseDir);
        return Path.Combine(baseDir, "HyperTool.Guest.json");
    }

    private static void SafeDelete(string path)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch
        {
        }
    }

    private static string BuildConfigJson(bool includeBackgroundCommunicationEnabled, bool backgroundCommunicationEnabledValue = false)
    {
        var root = new Dictionary<string, object?>
        {
            ["enabled"] = true,
            ["sharePath"] = "\\\\HOST\\HyperToolShare",
            ["driveLetter"] = "Z",
            ["persistent"] = true,
            ["pollIntervalSeconds"] = 15,
            ["credential"] = new Dictionary<string, object?>
            {
                ["username"] = string.Empty,
                ["password"] = string.Empty
            },
            ["autostart"] = new Dictionary<string, object?>
            {
                ["preferredMode"] = "run-registry",
                ["runValueName"] = "HyperTool.Guest",
                ["taskName"] = "HyperTool.Guest"
            },
            ["logging"] = new Dictionary<string, object?>
            {
                ["directoryPath"] = string.Empty,
                ["fileName"] = "hypertool-guest.log",
                ["echoToConsole"] = true
            },
            ["handshake"] = new Dictionary<string, object?>
            {
                ["enabled"] = true,
                ["filePath"] = string.Empty
            },
            ["usb"] = BuildUsbObject(includeBackgroundCommunicationEnabled, backgroundCommunicationEnabledValue),
            ["sharedFolders"] = new Dictionary<string, object?>
            {
                ["enabled"] = true,
                ["hostFeatureEnabled"] = true,
                ["baseDriveLetter"] = "Z",
                ["mappings"] = Array.Empty<object>()
            },
            ["fileService"] = new Dictionary<string, object?>
            {
                ["enabled"] = true,
                ["mappingMode"] = "hypertool-file",
                ["preferHyperVSocket"] = true
            },
            ["monitoring"] = new Dictionary<string, object?>
            {
                ["enabled"] = true,
                ["monitorIntervalMs"] = 1000
            },
            ["network"] = new Dictionary<string, object?>
            {
                ["enabled"] = true
            },
            ["ui"] = new Dictionary<string, object?>
            {
                ["theme"] = "dark",
                ["debugLoggingEnabled"] = false,
                ["startWithWindows"] = true,
                ["startMinimized"] = false,
                ["minimizeToTray"] = true,
                ["checkForUpdatesOnStartup"] = true
            }
        };

        return JsonSerializer.Serialize(root, JsonOptions);
    }

    private static Dictionary<string, object?> BuildUsbObject(bool includeBackgroundCommunicationEnabled, bool backgroundCommunicationEnabledValue)
    {
        var usb = new Dictionary<string, object?>
        {
            ["enabled"] = true,
            ["disconnectOnExit"] = true,
            ["hostAddress"] = string.Empty,
            ["hostName"] = string.Empty,
            ["hostFeatureEnabled"] = true,
            ["useHyperVSocket"] = true,
            ["hyperVSocketServiceId"] = HyperTool.Services.HyperVSocketUsbTunnelDefaults.ServiceIdString,
            ["autoConnectDeviceKeys"] = Array.Empty<object>(),
            ["usbConfigResetMigrationApplied"] = true,
            ["usbConfigResetMigrationInfoPending"] = false,
            ["hyperVOnlyCleanupMigrationApplied"] = true
        };

        if (includeBackgroundCommunicationEnabled)
        {
            usb["backgroundCommunicationEnabled"] = backgroundCommunicationEnabledValue;
        }

        return usb;
    }
}
