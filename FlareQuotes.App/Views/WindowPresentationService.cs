using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FlareQuotes.App.Views;

/// <summary>
/// Applies supported Windows 11 presentation attributes without replacing native
/// resize, snap, shadow, accessibility, or system-menu behavior.
/// </summary>
internal static class WindowPresentationService
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeLegacy = 19;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaSystemBackdropType = 38;

    private const int DwmWindowCornerPreferenceRound = 2;
    private const int DwmSystemBackdropTypeMainWindow = 2;

    public static void Apply(Window window, bool useDark)
    {
        if (!OperatingSystem.IsWindows())
            return;

        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
            return;

        UpdateTheme(window, useDark);

        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            return;

        TrySetInt(handle, DwmwaWindowCornerPreference, DwmWindowCornerPreferenceRound);
        TrySetInt(handle, DwmwaSystemBackdropType, DwmSystemBackdropTypeMainWindow);

        // COLORREF is 0x00BBGGRR. This is a restrained graphite edge that
        // separates the app from the desktop without drawing a heavy outline.
        var graphiteBorder = ToColorRef(red: 38, green: 43, blue: 49);
        TrySetInt(handle, DwmwaBorderColor, graphiteBorder);
    }

    public static void UpdateTheme(Window window, bool useDark)
    {
        if (!OperatingSystem.IsWindows())
            return;

        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
            return;

        var enabled = useDark ? 1 : 0;
        if (!TrySetInt(handle, DwmwaUseImmersiveDarkMode, enabled))
            TrySetInt(handle, DwmwaUseImmersiveDarkModeLegacy, enabled);
    }

    private static bool TrySetInt(IntPtr handle, int attribute, int value)
    {
        try
        {
            return DwmSetWindowAttribute(handle, attribute, ref value, sizeof(int)) == 0;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    private static int ToColorRef(byte red, byte green, byte blue) =>
        red | (green << 8) | (blue << 16);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int dwAttribute,
        ref int pvAttribute,
        int cbAttribute);
}