using System.Runtime.InteropServices;
using System.Management;
using System.Diagnostics;

namespace HyperTool.Services;

public sealed class SystemResourceSampler
{
    private readonly object _cpuCounterLock = new();
    private PerformanceCounter? _cpuUtilityCounter;
    private bool _cpuUtilityCounterPrimed;
    private ulong _previousIdle;
    private ulong _previousKernel;
    private ulong _previousUser;
    private bool _hasPrevious;

    public (double CpuPercent, double RamUsedGb, double RamTotalGb) Sample()
    {
        var cpuPercent = SampleCpuPercent();
        var (ramUsedGb, ramTotalGb) = SampleMemoryGb();
        return (cpuPercent, ramUsedGb, ramTotalGb);
    }

    private double SampleCpuPercent()
    {
        if (TrySampleCpuPercentFromProcessorUtilityCounter(out var utilityCpuPercent))
        {
            return utilityCpuPercent;
        }

        if (TrySampleCpuPercentFromWmi(out var wmiCpuPercent))
        {
            return wmiCpuPercent;
        }

        if (!GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
        {
            return 0;
        }

        var idle = FileTimeToUInt64(idleTime);
        var kernel = FileTimeToUInt64(kernelTime);
        var user = FileTimeToUInt64(userTime);

        if (_hasPrevious
            && (idle < _previousIdle || kernel < _previousKernel || user < _previousUser))
        {
            _previousIdle = idle;
            _previousKernel = kernel;
            _previousUser = user;
            return 0;
        }

        if (!_hasPrevious)
        {
            _previousIdle = idle;
            _previousKernel = kernel;
            _previousUser = user;
            _hasPrevious = true;
            return 0;
        }

        var idleDelta = idle - _previousIdle;
        var kernelDelta = kernel - _previousKernel;
        var userDelta = user - _previousUser;

        _previousIdle = idle;
        _previousKernel = kernel;
        _previousUser = user;

        var totalDelta = kernelDelta + userDelta;
        if (totalDelta == 0)
        {
            return 0;
        }

        if (idleDelta >= totalDelta)
        {
            return 0;
        }

        var busy = totalDelta - idleDelta;
        var percent = (double)busy / totalDelta * 100d;
        return Math.Clamp(percent, 0d, 100d);
    }

    private bool TrySampleCpuPercentFromProcessorUtilityCounter(out double cpuPercent)
    {
        cpuPercent = 0d;

        try
        {
            lock (_cpuCounterLock)
            {
                _cpuUtilityCounter ??= new PerformanceCounter("Processor Information", "% Processor Utility", "_Total", true);

                if (!_cpuUtilityCounterPrimed)
                {
                    _ = _cpuUtilityCounter.NextValue();
                    _cpuUtilityCounterPrimed = true;
                    return false;
                }

                var sampled = _cpuUtilityCounter.NextValue();
                if (float.IsNaN(sampled) || float.IsInfinity(sampled))
                {
                    return false;
                }

                cpuPercent = Math.Clamp(Math.Round(sampled, 2), 0d, 100d);
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private static bool TrySampleCpuPercentFromWmi(out double cpuPercent)
    {
        cpuPercent = 0d;

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "root\\CIMV2",
                "SELECT Name, PercentProcessorTime FROM Win32_PerfFormattedData_PerfOS_Processor WHERE Name = '_Total'");

            foreach (ManagementObject obj in searcher.Get())
            {
                var rawValue = obj["PercentProcessorTime"];
                if (rawValue is null)
                {
                    continue;
                }

                var parsed = Convert.ToDouble(rawValue);
                if (double.IsNaN(parsed) || double.IsInfinity(parsed))
                {
                    continue;
                }

                cpuPercent = Math.Clamp(parsed, 0d, 100d);
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private static (double RamUsedGb, double RamTotalGb) SampleMemoryGb()
    {
        var memoryStatus = new MemoryStatusEx();
        if (!GlobalMemoryStatusEx(memoryStatus) || memoryStatus.TotalPhys == 0)
        {
            return (0, 0);
        }

        var usedBytes = memoryStatus.TotalPhys - memoryStatus.AvailPhys;
        var gb = 1024d * 1024d * 1024d;
        return (usedBytes / gb, memoryStatus.TotalPhys / gb);
    }

    private static ulong FileTimeToUInt64(FileTime fileTime)
    {
        return ((ulong)fileTime.HighDateTime << 32) | (uint)fileTime.LowDateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public int LowDateTime;
        public int HighDateTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MemoryStatusEx
    {
        public uint Length = (uint)Marshal.SizeOf<MemoryStatusEx>();
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx memoryStatus);
}
