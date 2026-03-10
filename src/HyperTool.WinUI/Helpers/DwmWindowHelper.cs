using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;
using Windows.Graphics;

namespace HyperTool.WinUI.Helpers;

internal static class DwmWindowHelper
{
    private const int DwmaWindowCornerPreference = 33;

    internal static void ApplyRoundedCorners(Window window)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            return;
        }

        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            if (hwnd == nint.Zero)
            {
                return;
            }

            var preference = DwmWindowCornerPreference.Round;
            _ = DwmSetWindowAttribute(
                hwnd,
                DwmaWindowCornerPreference,
                ref preference,
                Marshal.SizeOf<DwmWindowCornerPreference>());
        }
        catch
        {
        }
    }

    internal static void ApplyRoundedRegion(Window window, int width, int height, int radius)
    {
        if (width <= 0 || height <= 0 || radius <= 0)
        {
            return;
        }

        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            if (hwnd == nint.Zero)
            {
                return;
            }

            var diameter = radius * 2;
            var region = CreateRoundRectRgn(0, 0, width + 1, height + 1, diameter, diameter);
            if (region == nint.Zero)
            {
                return;
            }

            var result = SetWindowRgn(hwnd, region, true);
            if (result == 0)
            {
                _ = DeleteObject(region);
            }
        }
        catch
        {
        }
    }

    internal static SizeInt32 ScaleLogicalSizeForCurrentDpi(Window window, int logicalWidth, int logicalHeight)
    {
        var scale = 1d;
        nint hwnd = nint.Zero;
        nint monitor = nint.Zero;

        try
        {
            hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            if (hwnd != nint.Zero)
            {
                monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
                if (monitor != nint.Zero)
                {
                    if (GetScaleFactorForMonitor(monitor, out var monitorScaleFactor) == 0)
                    {
                        scale = Math.Clamp((int)monitorScaleFactor / 100d, 1d, 3d);
                    }
                    else
                    {
                        GetDpiForMonitor(monitor, MonitorDpiType.EffectiveDpi, out var dpiX, out _);
                        if (dpiX > 0)
                        {
                            scale = Math.Clamp(dpiX / 96d, 1d, 3d);
                        }
                    }
                }
            }
        }
        catch
        {
        }

        var scaledWidth = Math.Max(1, (int)Math.Round(logicalWidth * scale));
        var scaledHeight = Math.Max(1, (int)Math.Round(logicalHeight * scale));

        if (hwnd == nint.Zero)
        {
            return new SizeInt32(scaledWidth, scaledHeight);
        }

        try
        {
            if (monitor == nint.Zero)
            {
                monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
            }

            if (monitor != nint.Zero)
            {
                var monitorInfo = new MonitorInfoEx();
                monitorInfo.cbSize = Marshal.SizeOf<MonitorInfoEx>();
                if (GetMonitorInfo(monitor, ref monitorInfo))
                {
                    var workWidth = Math.Max(1, monitorInfo.rcWork.Right - monitorInfo.rcWork.Left);
                    var workHeight = Math.Max(1, monitorInfo.rcWork.Bottom - monitorInfo.rcWork.Top);

                    var minWidth = Math.Min(logicalWidth, 320);
                    var minHeight = Math.Min(logicalHeight, 220);

                    var clampedWidth = Math.Clamp(scaledWidth, minWidth, Math.Max(minWidth, workWidth - 16));
                    var clampedHeight = Math.Clamp(scaledHeight, minHeight, Math.Max(minHeight, workHeight - 16));

                    return new SizeInt32(clampedWidth, clampedHeight);
                }
            }
        }
        catch
        {
        }

        return new SizeInt32(scaledWidth, scaledHeight);
    }

    internal static void ResizeForCurrentDpi(Window window, int logicalWidth, int logicalHeight)
    {
        try
        {
            if (window.AppWindow is null)
            {
                return;
            }

            var scaledSize = ScaleLogicalSizeForCurrentDpi(window, logicalWidth, logicalHeight);
            window.AppWindow.Resize(scaledSize);
        }
        catch
        {
        }
    }

    internal static void ApplyContentCompensationForCurrentDpi(Window window, int logicalWidth, int logicalHeight)
    {
        _ = window;
        _ = logicalWidth;
        _ = logicalHeight;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        nint hwnd,
        int dwAttribute,
        ref DwmWindowCornerPreference pvAttribute,
        int cbAttribute);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern nint CreateRoundRectRgn(
        int left,
        int top,
        int right,
        int bottom,
        int widthEllipse,
        int heightEllipse);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowRgn(nint hWnd, nint hRgn, bool redraw);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, uint dwFlags);

    [DllImport("Shcore.dll")]
    private static extern int GetDpiForMonitor(
        nint hmonitor,
        MonitorDpiType dpiType,
        out uint dpiX,
        out uint dpiY);

    [DllImport("Shcore.dll")]
    private static extern int GetScaleFactorForMonitor(
        nint hMon,
        out DeviceScaleFactor pScale);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint hMonitor, ref MonitorInfoEx lpmi);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteObject(nint hObject);

    private enum DwmWindowCornerPreference
    {
        Default = 0,
        DoNotRound = 1,
        Round = 2,
        RoundSmall = 3
    }

    private const uint MonitorDefaultToNearest = 0x00000002;

    private enum MonitorDpiType
    {
        EffectiveDpi = 0,
        AngularDpi = 1,
        RawDpi = 2,
        Default = EffectiveDpi
    }

    private enum DeviceScaleFactor
    {
        Scale100Percent = 100,
        Scale120Percent = 120,
        Scale125Percent = 125,
        Scale140Percent = 140,
        Scale150Percent = 150,
        Scale160Percent = 160,
        Scale175Percent = 175,
        Scale180Percent = 180,
        Scale200Percent = 200,
        Scale225Percent = 225,
        Scale250Percent = 250,
        Scale300Percent = 300,
        Scale350Percent = 350,
        Scale400Percent = 400,
        Scale450Percent = 450,
        Scale500Percent = 500
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfoEx
    {
        public int cbSize;
        public Rect rcMonitor;
        public Rect rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }
}
