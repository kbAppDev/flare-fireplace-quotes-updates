using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;

namespace FlareQuotes.App.Views;

/// <summary>Native rounded frames and a restrained backdrop without fading window contents.</summary>
public static class WindowAppearance
{
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmWindowCornerPreference = 33;
    private const int DwmSystemBackdropType = 38;
    private static readonly DependencyProperty AttachedProperty = DependencyProperty.RegisterAttached(
        "Attached", typeof(bool), typeof(WindowAppearance), new PropertyMetadata(false));
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(WindowAppearance), new PropertyMetadata(false, (owner, args) =>
        {
            if (args.NewValue is true && owner is Window window)
                Attach(window, 54);
        }));
    public static bool GetEnabled(DependencyObject element) => (bool)element.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject element, bool enabled) => element.SetValue(EnabledProperty, enabled);

    public static void Attach(Window window, double captionHeight = 52)
    {
        if ((bool)window.GetValue(AttachedProperty))
            return;
        window.SetValue(AttachedProperty, true);
        WindowChrome.SetWindowChrome(window, new WindowChrome
        {
            CaptionHeight = captionHeight,
            ResizeBorderThickness = window.ResizeMode is ResizeMode.NoResize or ResizeMode.CanMinimize
                                        ? new Thickness(0) : new Thickness(6),
            GlassFrameThickness = new Thickness(-1),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false
        });
        window.SourceInitialized += (_, _) => UpdateTheme(window, UsesDarkTheme(window));
        window.StateChanged += (_, _) => UpdateTheme(window, UsesDarkTheme(window));
        AttachKeyboardHelp(window);
    }

    public static void UpdateTheme(Window window, bool dark)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var hasBackdrop = false;
        if (handle != IntPtr.Zero && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            var darkMode = dark ? 1 : 0;
            DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref darkMode, sizeof(int));
            var corners = 2; // DWMWCP_ROUND: the compositor clips the complete HWND, including child content.
            DwmSetWindowAttribute(handle, DwmWindowCornerPreference, ref corners, sizeof(int));
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621) && !SystemParameters.HighContrast)
            {
                var backdrop = 3; // Desktop Acrylic, with an opaque fallback provided by Windows.
                hasBackdrop = DwmSetWindowAttribute(handle, DwmSystemBackdropType,
                    ref backdrop, sizeof(int)) >= 0;
                if (hasBackdrop && HwndSource.FromHwnd(handle) is { CompositionTarget: { } target })
                    target.BackgroundColor = Colors.Transparent;
            }
        }

        // Only background paint is translucent. Cards, inputs and text keep their full opacity.
        var tint = dark ? Color.FromRgb(11, 16, 23) : Color.FromRgb(238, 242, 246);
        tint.A = hasBackdrop ? (byte)218 : (byte)255;
        window.Resources["WindowGlassTintBrush"] = new SolidColorBrush(tint);
        var titleTint = dark ? Color.FromRgb(8, 12, 18) : Color.FromRgb(251, 252, 254);
        titleTint.A = hasBackdrop ? (byte)222 : (byte)255;
        window.Resources["WindowGlassTitleBrush"] = new SolidColorBrush(titleTint);
    }

    private static bool UsesDarkTheme(FrameworkElement element) =>
        element.TryFindResource("FlareTextBrush") is SolidColorBrush brush && brush.Color.R > 128;

#if FLARE_UI_SNAPSHOTS
    internal static object VerifyNativeAttributes(Window window)
    {
        var handle = new WindowInteropHelper(window).EnsureHandle();
        var roundSupported = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);
        var acrylicSupported = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621) &&
                               !SystemParameters.HighContrast;
        var corner = 0;
        var backdrop = 0;
        var cornerResult = DwmGetWindowAttribute(handle, DwmWindowCornerPreference, ref corner, sizeof(int));
        var backdropResult = DwmGetWindowAttribute(handle, DwmSystemBackdropType, ref backdrop, sizeof(int));
        if (roundSupported && (cornerResult < 0 || corner != 2))
            throw new InvalidOperationException("The native window has not requested rounded corners.");
        if (acrylicSupported && (backdropResult < 0 || backdrop != 3))
            throw new InvalidOperationException("The native window has not requested the Acrylic backdrop.");

        return new
        {
            Window = window.GetType().Name,
            OperatingSystem = Environment.OSVersion.Version.ToString(),
            CornerAttributeResult = cornerResult,
            CornerPreference = corner,
            RoundedPreferenceVerified = roundSupported && cornerResult >= 0 && corner == 2,
            BackdropAttributeResult = backdropResult,
            BackdropType = backdrop,
            AcrylicPreferenceVerified = acrylicSupported && backdropResult >= 0 && backdrop == 3,
            SolidFallback = !acrylicSupported,
            HighContrast = SystemParameters.HighContrast,
            WindowState = window.WindowState.ToString(),
            AllowsTransparency = window.AllowsTransparency,
            WindowOpacity = window.Opacity,
            // DWM attributes are preferences; the compositor can suppress visual rounding in these states.
            RoundingPolicy = "Windows controls final clipping, including maximized, snapped and virtual-machine windows."
        };
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
#endif

    private static void AttachKeyboardHelp(Window window)
    {
        var keyboardNavigation = false;
        ToolTip? focusedTip = null;
        var delay = new DispatcherTimer(DispatcherPriority.Background, window.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(450)
        };
        var duration = new DispatcherTimer(DispatcherPriority.Background, window.Dispatcher)
        {
            Interval = TimeSpan.FromSeconds(10)
        };
        FrameworkElement? pendingTarget = null;
        void CloseHelp()
        {
            delay.Stop();
            duration.Stop();
            pendingTarget = null;
            if (focusedTip is not null)
                focusedTip.IsOpen = false;
            focusedTip = null;
        }
        delay.Tick += (_, _) =>
        {
            delay.Stop();
            if (pendingTarget is not { IsKeyboardFocusWithin: true, ToolTip: { } content } target)
                return;
            focusedTip = content as ToolTip ?? new ToolTip { Content = content };
            focusedTip.PlacementTarget = target;
            focusedTip.Placement = PlacementMode.Bottom;
            focusedTip.VerticalOffset = 6;
            focusedTip.IsOpen = true;
            duration.Start();
        };
        duration.Tick += (_, _) => CloseHelp();
        window.PreviewKeyDown += (_, args) =>
        {
            keyboardNavigation = args.Key == Key.Tab;
            CloseHelp();
        };
        window.PreviewMouseDown += (_, _) => { keyboardNavigation = false; CloseHelp(); };
        window.AddHandler(Keyboard.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler((_, args) =>
        {
            CloseHelp();
            if (keyboardNavigation && args.NewFocus is FrameworkElement { ToolTip: not null } element)
            {
                pendingTarget = element;
                delay.Start();
            }
        }), true);
        window.AddHandler(Keyboard.LostKeyboardFocusEvent,
            new KeyboardFocusChangedEventHandler((_, _) => CloseHelp()), true);
        window.Deactivated += (_, _) => CloseHelp();
        window.Closed += (_, _) => CloseHelp();
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
