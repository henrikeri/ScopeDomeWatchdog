using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ScopeDomeWatchdog.Tray;

internal static class WindowChromeHelper
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    public static void ApplyDarkTitleBar(Window window)
    {
        window.SourceInitialized += (_, _) => TrySetDarkTitleBar(window);
    }

    private static void TrySetDarkTitleBar(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var enabled = 1;
        var result = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int));
        if (result != 0)
        {
            DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkModeBefore20H1, ref enabled, sizeof(int));
        }

        // Windows 11+ supports caption and border color attributes.
        var borderColor = ColorRef(0x1E, 0x1E, 0x1E);
        DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref borderColor, sizeof(int));

        var captionColor = ColorRef(0x1E, 0x1E, 0x1E);
        DwmSetWindowAttribute(hwnd, DwmwaCaptionColor, ref captionColor, sizeof(int));

        var textColor = ColorRef(0xCC, 0xCC, 0xCC);
        DwmSetWindowAttribute(hwnd, DwmwaTextColor, ref textColor, sizeof(int));
    }

    private static int ColorRef(byte r, byte g, byte b)
    {
        return r | (g << 8) | (b << 16);
    }
}
