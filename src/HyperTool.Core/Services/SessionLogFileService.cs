using Serilog;
using Serilog.Events;
using System.Text;

namespace HyperTool.Services;

public static class SessionLogFileService
{
    public static string AppendFileNameSuffix(string fileName, string suffix)
    {
        var normalizedFileName = string.IsNullOrWhiteSpace(fileName)
            ? "hypertool.log"
            : fileName.Trim();

        var extension = Path.GetExtension(normalizedFileName);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".log";
        }

        var fileStem = Path.GetFileNameWithoutExtension(normalizedFileName);
        if (string.IsNullOrWhiteSpace(fileStem))
        {
            fileStem = "hypertool";
        }

        var normalizedSuffix = (suffix ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedSuffix))
        {
            return fileStem + extension;
        }

        var expectedSuffix = "-" + normalizedSuffix;
        if (fileStem.EndsWith(expectedSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return fileStem + extension;
        }

        return fileStem + expectedSuffix + extension;
    }

    public static string ResolveWritableDirectory(IEnumerable<string> directoryCandidates)
    {
        foreach (var candidate in directoryCandidates)
        {
            if (IsWritableDirectory(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Kein beschreibbares Logverzeichnis gefunden.");
    }

    public static string CreateSessionLogFilePath(string directoryPath, string fileName)
    {
        Directory.CreateDirectory(directoryPath);

        var normalizedFileName = string.IsNullOrWhiteSpace(fileName)
            ? "hypertool.log"
            : fileName.Trim();

        var extension = Path.GetExtension(normalizedFileName);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".log";
        }

        var fileStem = Path.GetFileNameWithoutExtension(normalizedFileName);
        if (string.IsNullOrWhiteSpace(fileStem))
        {
            fileStem = "hypertool";
        }

        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var basePath = Path.Combine(directoryPath, $"{fileStem}-{timestamp}");
        var candidatePath = basePath + extension;
        var suffix = 1;

        while (File.Exists(candidatePath))
        {
            candidatePath = $"{basePath}-{suffix:00}{extension}";
            suffix++;
        }

        return candidatePath;
    }

    public static void CleanupOldLogFiles(string directoryPath, TimeSpan maxAge)
    {
        try
        {
            if (!Directory.Exists(directoryPath))
            {
                return;
            }

            var cutoffUtc = DateTime.UtcNow - maxAge;
            foreach (var filePath in Directory.EnumerateFiles(directoryPath, "*", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(filePath) < cutoffUtc)
                    {
                        var attributes = File.GetAttributes(filePath);
                        if ((attributes & FileAttributes.ReadOnly) != 0)
                        {
                            File.SetAttributes(filePath, attributes & ~FileAttributes.ReadOnly);
                        }

                        File.Delete(filePath);
                    }
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
    }

    private static bool IsWritableDirectory(string directoryPath)
    {
        try
        {
            Directory.CreateDirectory(directoryPath);
            var probeFilePath = Path.Combine(directoryPath, $".write-test-{Guid.NewGuid():N}.tmp");
            using var probeStream = new FileStream(
                probeFilePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1,
                options: FileOptions.DeleteOnClose);

            return true;
        }
        catch
        {
            return false;
        }
    }
}

public static class HostLoggingService
{
    private static readonly TimeSpan LogRetentionPeriod = TimeSpan.FromDays(3);

    public static string Initialize(bool debugLoggingEnabled)
    {
        var logDirectoryCandidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HyperTool", "logs"),
            Path.Combine(AppContext.BaseDirectory, "logs"),
            Path.Combine(Path.GetTempPath(), "HyperTool", "logs")
        };

        var logsDirectory = SessionLogFileService.ResolveWritableDirectory(logDirectoryCandidates);
        SessionLogFileService.CleanupOldLogFiles(logsDirectory, LogRetentionPeriod);

        var baseLogFileName = debugLoggingEnabled
            ? SessionLogFileService.AppendFileNameSuffix("hypertool.log", "Debug")
            : "hypertool.log";

        var logFilePath = SessionLogFileService.CreateSessionLogFilePath(logsDirectory, baseLogFileName);
        var logger = new LoggerConfiguration()
            .MinimumLevel.Is(debugLoggingEnabled ? LogEventLevel.Debug : LogEventLevel.Information)
            .WriteTo.File(
                logFilePath,
                encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
                shared: true)
            .CreateLogger();

        var previousLogger = Log.Logger as IDisposable;
        Log.Logger = logger;
        previousLogger?.Dispose();

        return logFilePath;
    }
}