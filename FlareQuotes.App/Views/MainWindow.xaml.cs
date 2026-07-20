using System;
using FlareQuotes.Core.Services;
using FlareQuotes.Core.Models;
using FlareQuotes.Core.Paths;
using FlareQuotes.Core.Updates;
using System.Windows.Threading;
using System.Security.Cryptography;
using System.Reflection;
using System.Net.Http;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Web.WebView2.Core;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using FlareQuotes.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;
using System.Collections.Generic;
namespace FlareQuotes.App.Views;

public partial class MainWindow : Window
{
    private const string DarkThemeName = "dark";
    private const string LightThemeName = "light";
    private bool _isApplyingTheme;
    private WebView2? _pdfPreviewWebView;
    private bool _pdfPreviewInitializing;
    private bool _hasCheckedForStartupUpdates;
    private FrameworkElement? _selectedChipDragElement;
    private object? _selectedChipDragItem;
    private string? _selectedChipDragKind;
    private Point _selectedChipDragStart;
    private bool _selectedChipIsDragging;
    private FrameworkElement? _selectedChipDropTargetElement;
    private object? _selectedChipLastLiveReorderTarget;
    private DateTime _selectedChipLastLiveReorderUtc = DateTime.MinValue;
    private Popup? _selectedChipDragGhostPopup;
    private FrameworkElement? _selectedChipDragGhostRoot;

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowPresentationService.Apply(this, !string.Equals(LoadThemePreference(), LightThemeName, StringComparison.OrdinalIgnoreCase));
        AttachDropdownScrollResetHooks();
        SetAppVersionText();

        var viewModel = App.Services.GetRequiredService<MainViewModel>();
        viewModel.PropertyChanged += ViewModel_PropertyChanged;
        DataContext = viewModel;

        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;

        ApplySavedTheme();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
#if FLARE_UI_SNAPSHOTS
        if (await UiSnapshotCapture.TryCaptureAsync(this))
            return;
#endif
        await TryLoadPdfPreviewAsync();
        _ = Dispatcher.InvokeAsync(async () => await CheckForUpdatesOnStartupAsync(),
                                   DispatcherPriority.ApplicationIdle);
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
            viewModel.PropertyChanged -= ViewModel_PropertyChanged;

        try
        {
            _pdfPreviewWebView?.Dispose();
        }
        catch
        {
            // Preview cleanup must never block shutdown.
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.WorkflowStage) or nameof(MainViewModel.GeneratedPdfPath)
                or nameof(MainViewModel.GeneratedPdfUri))
        {
            _ = Dispatcher.InvokeAsync(async () => await TryLoadPdfPreviewAsync());
        }
    }

    private void ApplySavedTheme()
    {
        var theme = LoadThemePreference();
        var useDark = !string.Equals(theme, LightThemeName, StringComparison.OrdinalIgnoreCase);
        ApplyTheme(useDark);
        UpdateHeaderLogo(useDark);

        if (ThemeToggleButton != null)
        {
            ThemeToggleButton.IsChecked = useDark;
        }
        if (ThemeLabelText != null)
        {
            ThemeLabelText.Text = useDark ? "Dark Mode" : "Light Mode";
        }
    }

    // Custom Border-less Window Interactivity
    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            this.DragMove();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }
    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        this.WindowState = this.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
    private void DropdownPopup_Opened(object sender, EventArgs e)
    {
        // Whenever a feature/media popup opens, force its internal
        // scroll viewer back to the top so the next fireplace starts clean.
        if (sender is not System.Windows.Controls.Primitives.Popup popup)
            return;

        Dispatcher.BeginInvoke(
            new Action(() =>
                       {
                           var scrollViewer = FindFirstVisualChild<System.Windows.Controls.ScrollViewer>(popup.Child);
                           scrollViewer?.ScrollToHome();
                           scrollViewer?.ScrollToVerticalOffset(0);
                       }),
            System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    private static T? FindFirstVisualChild<T>(DependencyObject? parent)
        where T : DependencyObject
    {
        if (parent is null)
            return null;

        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);

            if (child is T typedChild)
                return typedChild;

            var nested = FindFirstVisualChild<T>(child);
            if (nested is not null)
                return nested;
        }

        return null;
    }

    private bool _dropdownScrollResetHooksAttached;

    private void AttachDropdownScrollResetHooks()
    {
        if (_dropdownScrollResetHooksAttached)
            return;

        _dropdownScrollResetHooksAttached = true;

        foreach (var popup in GetNamedPopups())
        {
            popup.Opened -= DropdownPopupScrollReset_Opened;
            popup.Opened += DropdownPopupScrollReset_Opened;

            popup.Closed -= DropdownPopupScrollReset_Closed;
            popup.Closed += DropdownPopupScrollReset_Closed;
        }

        QueueDropdownScrollReset();
    }

    private IEnumerable<System.Windows.Controls.Primitives.Popup> GetNamedPopups()
    {
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic;

        foreach (var field in GetType().GetFields(flags))
        {
            if (!typeof(System.Windows.Controls.Primitives.Popup).IsAssignableFrom(field.FieldType))
                continue;

            if (field.GetValue(this) is System.Windows.Controls.Primitives.Popup popup)
                yield return popup;
        }
    }

    private void DropdownPopupScrollReset_Opened(object? sender, EventArgs e)
    {
        QueueDropdownScrollReset();

        if (sender is System.Windows.Controls.Primitives.Popup popup)
            QueuePopupScrollReset(popup);
    }

    private void DropdownPopupScrollReset_Closed(object? sender, EventArgs e)
    {
        QueueDropdownScrollReset();

        if (sender is System.Windows.Controls.Primitives.Popup popup)
            QueuePopupScrollReset(popup);
    }

    private void QueueDropdownScrollReset()
    {
        Dispatcher.BeginInvoke(new Action(ResetDropdownScrollPositions),
                               System.Windows.Threading.DispatcherPriority.Loaded);
        Dispatcher.BeginInvoke(new Action(ResetDropdownScrollPositions),
                               System.Windows.Threading.DispatcherPriority.Render);
        Dispatcher.BeginInvoke(new Action(ResetDropdownScrollPositions),
                               System.Windows.Threading.DispatcherPriority.ContextIdle);
        Dispatcher.BeginInvoke(new Action(ResetDropdownScrollPositions),
                               System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    private void QueuePopupScrollReset(System.Windows.Controls.Primitives.Popup popup)
    {
        Dispatcher.BeginInvoke(new Action(() => ResetPopupScrollPosition(popup)),
                               System.Windows.Threading.DispatcherPriority.Loaded);
        Dispatcher.BeginInvoke(new Action(() => ResetPopupScrollPosition(popup)),
                               System.Windows.Threading.DispatcherPriority.Render);
        Dispatcher.BeginInvoke(new Action(() => ResetPopupScrollPosition(popup)),
                               System.Windows.Threading.DispatcherPriority.ContextIdle);
        Dispatcher.BeginInvoke(new Action(() => ResetPopupScrollPosition(popup)),
                               System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    private void ResetDropdownScrollPositions()
    {
        foreach (var popup in GetNamedPopups())
            ResetPopupScrollPosition(popup);
    }

    private static void ResetPopupScrollPosition(System.Windows.Controls.Primitives.Popup popup)
    {
        if (popup.Child is null)
            return;

        ResetScrollViewersIn(popup.Child);
        ResetItemsControlsIn(popup.Child);
    }

    private static void ResetScrollViewersIn(DependencyObject? parent)
    {
        if (parent is null)
            return;

        if (parent is System.Windows.Controls.ScrollViewer scrollViewer)
        {
            scrollViewer.ScrollToHome();
            scrollViewer.ScrollToTop();
            scrollViewer.ScrollToVerticalOffset(0);
        }

        var childCount = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);

        for (var i = 0; i < childCount; i++)
            ResetScrollViewersIn(System.Windows.Media.VisualTreeHelper.GetChild(parent, i));
    }

    private static void ResetItemsControlsIn(DependencyObject? parent)
    {
        if (parent is null)
            return;

        if (parent is System.Windows.Controls.ListBox listBox && listBox.Items.Count > 0)
            listBox.ScrollIntoView(listBox.Items[0]);

        var childCount = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);

        for (var i = 0; i < childCount; i++)
            ResetItemsControlsIn(System.Windows.Media.VisualTreeHelper.GetChild(parent, i));
    }
    private void DropdownToggle_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // force dropdown scroll reset before opening
        AttachDropdownScrollResetHooks();
        ResetDropdownScrollPositions();
        QueueDropdownScrollReset();
        TryAnimateButtonPress(e.OriginalSource as DependencyObject);

        if (DataContext is not MainViewModel viewModel)
            return;

        if (sender == FeatureDropdownButton && viewModel.IsFeatureDropdownOpen)
        {
            viewModel.IsFeatureDropdownOpen = false;
            e.Handled = true;
            return;
        }

        if (sender == ClassicMediaDropdownButton && viewModel.IsClassicMediaDropdownOpen)
        {
            viewModel.IsClassicMediaDropdownOpen = false;
            e.Handled = true;
            return;
        }

        if (sender == AdditionalClassicMediaDropdownButton && viewModel.IsAdditionalClassicMediaDropdownOpen)
        {
            viewModel.IsAdditionalClassicMediaDropdownOpen = false;
            e.Handled = true;
            return;
        }

        if (sender == PremiumMediaDropdownButton && viewModel.IsPremiumMediaDropdownOpen)
        {
            viewModel.IsPremiumMediaDropdownOpen = false;
            e.Handled = true;
            return;
        }

        if (sender == LeadTimeDropdownButton && viewModel.IsLeadTimeDropdownOpen)
        {
            viewModel.IsLeadTimeDropdownOpen = false;
            e.Handled = true;
            return;
        }
    }
    private void SelectedChip_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && FindAncestor<ButtonBase>(source) is not null)
            return;

        if (sender is not FrameworkElement element)
            return;

        var kind = element.Tag as string;
        var item = element.DataContext;

        if (!IsReorderableChip(kind, item))
            return;

        _selectedChipDragElement = element;
        _selectedChipDragItem = item;
        _selectedChipDragKind = kind;
        _selectedChipDragStart = e.GetPosition(this);
        _selectedChipIsDragging = false;

        element.CaptureMouse();
        e.Handled = true;
    }
    private void SelectedChip_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_selectedChipDragElement is null || _selectedChipDragItem is null ||
            string.IsNullOrWhiteSpace(_selectedChipDragKind))
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            EndSelectedChipDrag();
            return;
        }

        var current = e.GetPosition(this);
        var movedEnough =
            Math.Abs(current.X - _selectedChipDragStart.X) >= SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(current.Y - _selectedChipDragStart.Y) >= SystemParameters.MinimumVerticalDragDistance;

        if (!_selectedChipIsDragging && !movedEnough)
            return;

        if (!_selectedChipIsDragging)
        {
            _selectedChipIsDragging = true;
            _selectedChipLastLiveReorderTarget = null;
            _selectedChipLastLiveReorderUtc = DateTime.MinValue;
            ApplySelectedChipDragVisual(_selectedChipDragElement, true);
            ShowSelectedChipDragGhost(_selectedChipDragElement, current);
        }

        UpdateSelectedChipDragVisualPosition(current);
        UpdateSelectedChipDragGhost(current);

        var targetElement = FindSelectedChipElementNearPoint(current, _selectedChipDragKind, _selectedChipDragElement);
        if (targetElement is null || ReferenceEquals(targetElement, _selectedChipDragElement))
        {
            SetSelectedChipDropTarget(null);
            e.Handled = true;
            return;
        }

        if (targetElement.DataContext is not {} target || ReferenceEquals(target, _selectedChipDragItem))
        {
            SetSelectedChipDropTarget(null);
            e.Handled = true;
            return;
        }

        SetSelectedChipDropTarget(targetElement);

        if (ShouldReorderSelectedChip(current, targetElement) && ShouldLiveReorderSelectedChip(target) &&
            MoveSelectedChipToTarget(target))
        {
            _selectedChipLastLiveReorderTarget = target;
            _selectedChipLastLiveReorderUtc = DateTime.UtcNow;
        }

        e.Handled = true;
    }
    private void SelectedChip_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_selectedChipIsDragging)
            CommitSelectedChipDrop();

        EndSelectedChipDrag();
        e.Handled = true;
    }

    private void SelectedChip_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (ReferenceEquals(sender, _selectedChipDragElement))
            EndSelectedChipDrag();
    }
    private void EndSelectedChipDrag()
    {
        ClearSelectedChipDropTarget();
        CloseSelectedChipDragGhost();

        var element = _selectedChipDragElement;

        _selectedChipDragElement = null;
        _selectedChipDragItem = null;
        _selectedChipDragKind = null;
        _selectedChipIsDragging = false;
        _selectedChipLastLiveReorderTarget = null;
        _selectedChipLastLiveReorderUtc = DateTime.MinValue;

        if (element is null)
            return;

        ApplySelectedChipDragVisual(element, false);

        if (element.IsMouseCaptured)
            element.ReleaseMouseCapture();
    }
    private void CommitSelectedChipDrop()
    {
        if (_selectedChipDropTargetElement?.DataContext is not {} target ||
            ReferenceEquals(target, _selectedChipDragItem) ||
            ReferenceEquals(target, _selectedChipLastLiveReorderTarget))
        {
            return;
        }

        MoveSelectedChipToTarget(target);
    }

    private bool ShouldLiveReorderSelectedChip(object target)
    {
        if (ReferenceEquals(target, _selectedChipLastLiveReorderTarget))
            return false;

        // Small throttle to prevent WrapPanel measure/reflow from fighting the mouse.
        return DateTime.UtcNow - _selectedChipLastLiveReorderUtc >= TimeSpan.FromMilliseconds(115);
    }
    private bool MoveSelectedChipToTarget(object target)
    {
        if (_selectedChipDragItem is null || string.IsNullOrWhiteSpace(_selectedChipDragKind))
            return false;

        if (DataContext is not MainViewModel viewModel)
            return false;

        var before = CaptureSelectedChipBounds(_selectedChipDragKind);

        bool moved;

        if (_selectedChipDragKind == "SelectedFeatureChip" && _selectedChipDragItem is FeatureSelection sourceFeature &&
            target is FeatureSelection targetFeature)
        {
            moved = viewModel.MoveSelectedFeature(sourceFeature, targetFeature);
        }
        else if (_selectedChipDragKind == "SelectedPremiumMediaChip" &&
                 _selectedChipDragItem is MediaSelection sourcePremiumMedia &&
                 target is MediaSelection targetPremiumMedia)
        {
            moved = viewModel.MoveSelectedPremiumMedia(sourcePremiumMedia, targetPremiumMedia);
        }
        else if (_selectedChipDragKind == "SelectedAdditionalClassicMediaChip" &&
                 _selectedChipDragItem is MediaSelection sourceAdditionalClassic &&
                 target is MediaSelection targetAdditionalClassic)
        {
            moved = viewModel.MoveSelectedAdditionalClassicMedia(sourceAdditionalClassic, targetAdditionalClassic);
        }
        else
        {
            moved = false;
        }

        if (moved)
            AnimateSelectedChipReflow(_selectedChipDragKind, before);

        return moved;
    }

    private Dictionary<object, Rect> CaptureSelectedChipBounds(string kind)
    {
        var result = new Dictionary<object, Rect>();

        foreach (var element in FindSelectedChipElements(kind))
        {
            if (element.DataContext is null)
                continue;

            var bounds = GetBoundsRelativeToWindow(element);

            if (!bounds.IsEmpty && bounds.Width > 0 && bounds.Height > 0)
                result[element.DataContext] = bounds;
        }

        return result;
    }

    private void AnimateSelectedChipReflow(string kind, Dictionary<object, Rect> before)
    {
        Dispatcher.BeginInvoke(
            new Action(() =>
                       {
                           var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

                           foreach (var element in FindSelectedChipElements(kind))
                           {
                               if (element.DataContext is null)
                                   continue;

                               if (!before.TryGetValue(element.DataContext, out var oldBounds))
                                   continue;

                               var newBounds = GetBoundsRelativeToWindow(element);

                               if (newBounds.IsEmpty)
                                   continue;

                               var dx = oldBounds.Left - newBounds.Left;
                               var dy = oldBounds.Top - newBounds.Top;

                               if (Math.Abs(dx) < 0.5 && Math.Abs(dy) < 0.5)
                                   continue;

                               var translate = EnsureSelectedChipTranslateTransform(element);
                               translate.BeginAnimation(TranslateTransform.XProperty, null);
                               translate.BeginAnimation(TranslateTransform.YProperty, null);

                               translate.X += dx;
                               translate.Y += dy;

                               translate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation {
                                   To = 0, Duration = TimeSpan.FromMilliseconds(210), EasingFunction = ease
                               });

                               translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation {
                                   To = 0, Duration = TimeSpan.FromMilliseconds(210), EasingFunction = ease
                               });
                           }
                       }),
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private static double VerticalDistanceToRect(Point point, Rect rect)
    {
        if (point.Y < rect.Top)
            return rect.Top - point.Y;

        if (point.Y > rect.Bottom)
            return point.Y - rect.Bottom;

        return 0;
    }

    private void SetSelectedChipDropTarget(FrameworkElement? target)
    {
        if (ReferenceEquals(_selectedChipDropTargetElement, target))
            return;

        ClearSelectedChipDropTarget();

        _selectedChipDropTargetElement = target;

        if (_selectedChipDropTargetElement is not null)
            ApplySelectedChipDropTargetVisual(_selectedChipDropTargetElement, true);
    }

    private void ClearSelectedChipDropTarget()
    {
        if (_selectedChipDropTargetElement is null)
            return;

        ApplySelectedChipDropTargetVisual(_selectedChipDropTargetElement, false);
        _selectedChipDropTargetElement = null;
    }

    private static void ApplySelectedChipDropTargetVisual(FrameworkElement element, bool active)
    {
        if (active)
        {
            Panel.SetZIndex(element, 500);
            element.Opacity = 0.92;
            element.Effect = new DropShadowEffect { Color = Color.FromRgb(153, 204, 0), BlurRadius = 16,
                                                    ShadowDepth = 0, Opacity = 0.42 };
        }
        else
        {
            Panel.SetZIndex(element, 0);
            element.Opacity = 1.0;
            element.Effect = null;
        }
    }

    private static bool IsReorderableChip(string? kind, object? item)
    {
        return (kind == "SelectedFeatureChip" && item is FeatureSelection) ||
               (kind == "SelectedPremiumMediaChip" && item is MediaSelection) ||
               (kind == "SelectedAdditionalClassicMediaChip" && item is MediaSelection);
    }
    private static object? FindSelectedChipDataContext(DependencyObject? source, string kind)
    {
        var current = source;

        while (current is not null)
        {
            if (current is FrameworkElement element && element.Tag is string tag &&
                string.Equals(tag, kind, StringComparison.OrdinalIgnoreCase))
            {
                if (kind == "SelectedFeatureChip" && element.DataContext is FeatureSelection feature)
                    return feature;

                if (kind == "SelectedPremiumMediaChip" && element.DataContext is MediaSelection premiumMedia)
                    return premiumMedia;

                if (kind == "SelectedAdditionalClassicMediaChip" &&
                    element.DataContext is MediaSelection additionalClassicMedia)
                    return additionalClassicMedia;
            }

            current = GetSafeParent(current);
        }

        return null;
    }
    private static void ApplySelectedChipDragVisual(FrameworkElement element, bool active)
    {
        var translate = EnsureSelectedChipTranslateTransform(element);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        Panel.SetZIndex(element, active ? 250 : 0);

        if (active)
        {
            // This is the live placeholder. The floating ghost is the chip being moved.
            element.Opacity = 0.18;

            translate.BeginAnimation(
                TranslateTransform.XProperty,
                new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(90), EasingFunction = ease });

            translate.BeginAnimation(
                TranslateTransform.YProperty,
                new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(90), EasingFunction = ease });

            element.Effect = null;
        }
        else
        {
            element.Opacity = 1.0;

            translate.BeginAnimation(
                TranslateTransform.XProperty,
                new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(140), EasingFunction = ease });

            translate.BeginAnimation(
                TranslateTransform.YProperty,
                new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(140), EasingFunction = ease });

            element.Effect = null;
        }
    }
    private void UpdateSelectedChipDragVisualPosition(Point current)
    {
        if (_selectedChipDragElement is null)
            return;

        var translate = EnsureSelectedChipTranslateTransform(_selectedChipDragElement);

        // The floating ghost now shows the actual movement. Keep the source chip
        // parked in its slot so the WrapPanel does not jitter while dragging.
        translate.BeginAnimation(TranslateTransform.XProperty, null);
        translate.BeginAnimation(TranslateTransform.YProperty, null);
        translate.X = 0;
        translate.Y = -3;
    }

    private void ShowSelectedChipDragGhost(FrameworkElement sourceElement, Point current)
    {
        CloseSelectedChipDragGhost();

        var label = GetSelectedChipDragLabel(sourceElement.DataContext);
        var width = Math.Max(110, Math.Min(260, sourceElement.ActualWidth > 0 ? sourceElement.ActualWidth : 150));
        var height = Math.Max(32, sourceElement.ActualHeight > 0 ? sourceElement.ActualHeight : 34);

        var root = new Border { Width = width,
                                Height = height,
                                CornerRadius = new CornerRadius(14),
                                Padding = new Thickness(10, 5, 10, 5),
                                Background = new SolidColorBrush(Color.FromRgb(41, 54, 69)),
                                BorderBrush = new SolidColorBrush(Color.FromRgb(153, 204, 0)),
                                BorderThickness = new Thickness(1.25),
                                Opacity = 0.98,
                                IsHitTestVisible = false,
                                Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 26, ShadowDepth = 8,
                                                                Direction = 270, Opacity = 0.36 } };

        var stack =
            new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        stack.Children.Add(new System.Windows.Shapes.Ellipse { Width = 8, Height = 8,
                                                               Fill = new SolidColorBrush(Color.FromRgb(153, 204, 0)),
                                                               VerticalAlignment = VerticalAlignment.Center,
                                                               Margin = new Thickness(0, 0, 8, 0) });

        stack.Children.Add(new TextBlock { Text = label, Foreground = new SolidColorBrush(Color.FromRgb(244, 247, 250)),
                                           FontWeight = FontWeights.SemiBold, FontSize = 12,
                                           TextTrimming = TextTrimming.CharacterEllipsis,
                                           VerticalAlignment = VerticalAlignment.Center });

        root.Child = stack;

        _selectedChipDragGhostRoot = root;
        _selectedChipDragGhostPopup = new Popup { PlacementTarget = this,    Placement = PlacementMode.Relative,
                                                  AllowsTransparency = true, IsHitTestVisible = false,
                                                  StaysOpen = true,          Child = root };

        UpdateSelectedChipDragGhost(current);
        _selectedChipDragGhostPopup.IsOpen = true;
    }

    private void UpdateSelectedChipDragGhost(Point current)
    {
        if (_selectedChipDragGhostPopup is null)
            return;

        var xOffset = current.X + 12;
        var yOffset = current.Y - 18;

        // Keep the ghost inside the visible app window.
        if (_selectedChipDragGhostRoot is not null)
        {
            var maxX = Math.Max(0, ActualWidth - _selectedChipDragGhostRoot.ActualWidth - 18);
            var maxY = Math.Max(0, ActualHeight - _selectedChipDragGhostRoot.ActualHeight - 18);
            xOffset = Clamp(xOffset, 8, maxX);
            yOffset = Clamp(yOffset, 8, maxY);
        }

        _selectedChipDragGhostPopup.HorizontalOffset = xOffset;
        _selectedChipDragGhostPopup.VerticalOffset = yOffset;
    }

    private void CloseSelectedChipDragGhost()
    {
        if (_selectedChipDragGhostPopup is not null)
        {
            _selectedChipDragGhostPopup.IsOpen = false;
            _selectedChipDragGhostPopup.Child = null;
            _selectedChipDragGhostPopup = null;
        }

        _selectedChipDragGhostRoot = null;
    }

    private static string GetSelectedChipDragLabel(object? item)
    {
        if (item is null)
            return "Selected item";

        var type = item.GetType();

        foreach (var propertyName in new[] { "DisplayName", "Label", "Name", "Key" })
        {
            var property = type.GetProperty(propertyName);
            var value = property?.GetValue(item)?.ToString();

            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return item.ToString() ?? "Selected item";
    }
    private FrameworkElement? FindSelectedChipElementNearPoint(Point point, string kind,
                                                               FrameworkElement draggedElement)
    {
        var candidates = FindSelectedChipElements(kind)
                             .Where(element => !ReferenceEquals(element, draggedElement))
                             .Select(element => new { Element = element, Bounds = GetBoundsRelativeToWindow(element) })
                             .Where(x => x.Bounds.Width > 0 && x.Bounds.Height > 0)
                             .ToList();

        if (candidates.Count == 0)
            return null;

        var rowBand = candidates
                          .Select(x => new { x.Element, x.Bounds, Center = GetRectCenter(x.Bounds),
                                             VerticalDistance = VerticalDistanceToRect(point, x.Bounds) })
                          .OrderBy(x => x.VerticalDistance)
                          .ThenBy(x => Math.Abs(point.X - x.Center.X))
                          .ToList();

        var bestVerticalDistance = rowBand.First().VerticalDistance;
        var sameRow = rowBand
                          .Where(x => x.VerticalDistance <=
                                      Math.Max(bestVerticalDistance + 4, Math.Max(18, x.Bounds.Height * 0.55)))
                          .OrderBy(x => x.Bounds.Left)
                          .ToList();

        if (sameRow.Count == 0)
            return rowBand.First().Element;

        var before =
            sameRow.Where(x => point.X <= x.Center.X).OrderBy(x => Math.Abs(point.X - x.Center.X)).FirstOrDefault();

        if (before is not null)
            return before.Element;

        return sameRow.OrderBy(x => Math.Abs(point.X - x.Center.X)).First().Element;
    }

    private IEnumerable<FrameworkElement> FindSelectedChipElements(string kind)
    {
        return FindVisualDescendants<FrameworkElement>(this).Where(
            element => element.Tag is string tag && string.Equals(tag, kind, StringComparison.OrdinalIgnoreCase) &&
                       IsReorderableChip(kind, element.DataContext));
    }

    private Rect GetBoundsRelativeToWindow(FrameworkElement element)
    {
        try
        {
            var transform = element.TransformToVisual(this);
            return transform.TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        }
        catch
        {
            return Rect.Empty;
        }
    }
    private static bool ShouldReorderSelectedChip(Point point, FrameworkElement targetElement)
    {
        var window = Window.GetWindow(targetElement) as MainWindow;
        var bounds = window?.GetBoundsRelativeToWindow(targetElement) ?? Rect.Empty;

        if (bounds.IsEmpty)
            return true;

        var center = GetRectCenter(bounds);

        var inHorizontalRowBand = point.Y >= bounds.Top - Math.Max(14, bounds.Height * 0.35) &&
                                  point.Y <= bounds.Bottom + Math.Max(14, bounds.Height * 0.35);

        if (inHorizontalRowBand && Math.Abs(point.X - center.X) >= Math.Min(10, bounds.Width * 0.18))
            return true;

        var inVerticalColumnBand = point.X >= bounds.Left - Math.Max(22, bounds.Width * 0.25) &&
                                   point.X <= bounds.Right + Math.Max(22, bounds.Width * 0.25);

        if (inVerticalColumnBand && Math.Abs(point.Y - center.Y) >= Math.Min(10, bounds.Height * 0.22))
            return true;

        return InflateRect(bounds, 6, 6).Contains(point);
    }

    private static IEnumerable<T> FindVisualDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);

        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);

            if (child is T typed)
                yield return typed;

            foreach (var descendant in FindVisualDescendants<T>(child))
                yield return descendant;
        }
    }

    private static Point GetRectCenter(Rect rect) => new(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2);

    private static double DistanceToRectCenter(Point point, Rect rect)
    {
        var center = GetRectCenter(rect);
        var dx = point.X - center.X;
        var dy = point.Y - center.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static Rect InflateRect(Rect rect, double x, double y)
    {
        rect.Inflate(x, y);
        return rect;
    }

    private static double Clamp(double value, double minimum, double maximum) => Math.Min(maximum,
                                                                                          Math.Max(minimum, value));

    private static TranslateTransform EnsureSelectedChipTranslateTransform(FrameworkElement element)
    {
        if (element.RenderTransform is TranslateTransform directTranslate)
            return directTranslate;

        if (element.RenderTransform is TransformGroup group)
        {
            var existing = group.Children.OfType<TranslateTransform>().FirstOrDefault();

            if (existing is not null)
                return existing;

            var appended = new TranslateTransform();
            group.Children.Add(appended);
            return appended;
        }

        var translate = new TranslateTransform();
        element.RenderTransform = translate;
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        return translate;
    }

    private void Root_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source)
            return;

        TryAnimateButtonPress(source);

        if (IsInsideAnyDropdown(source))
            return;

        CloseAllDropdowns();
    }
    private bool IsInsideAnyDropdown(DependencyObject source)
    {
        return IsInside(source, FeatureDropdownButton) || IsInside(source, FeatureDropdownPopup?.Child) ||
               IsInside(source, ClassicMediaDropdownButton) || IsInside(source, ClassicMediaDropdownPopup?.Child) ||
               IsInside(source, AdditionalClassicMediaDropdownButton) ||
               IsInside(source, AdditionalClassicMediaDropdownPopup?.Child) ||
               IsInside(source, PremiumMediaDropdownButton) || IsInside(source, PremiumMediaDropdownPopup?.Child) ||
               IsInside(source, LeadTimeDropdownButton) || IsInside(source, LeadTimeDropdownPopup?.Child);
    }
    private void CloseAllDropdowns()
    {
        if (DataContext is not MainViewModel viewModel)
            return;

        viewModel.IsFeatureDropdownOpen = false;
        viewModel.IsClassicMediaDropdownOpen = false;
        viewModel.IsAdditionalClassicMediaDropdownOpen = false;
        viewModel.IsPremiumMediaDropdownOpen = false;
        viewModel.IsLeadTimeDropdownOpen = false;
    }

    private static bool IsInside(DependencyObject? source, DependencyObject? target)
    {
        if (source is null || target is null)
            return false;

        DependencyObject? current = source;

        while (current is not null)
        {
            if (ReferenceEquals(current, target))
                return true;

            current = GetSafeParent(current);
        }

        return false;
    }

    private static DependencyObject? GetSafeParent(DependencyObject current)
    {
        try
        {
            var visualParent = VisualTreeHelper.GetParent(current);
            if (visualParent is not null)
                return visualParent;
        }
        catch
        {
            // Popup/logical elements may not be visual children.
        }

        try
        {
            return LogicalTreeHelper.GetParent(current);
        }
        catch
        {
            return null;
        }
    }

    private static T? FindAncestor<T>(DependencyObject? source)
        where T : DependencyObject
    {
        DependencyObject? current = source;

        while (current is not null)
        {
            if (current is T match)
                return match;

            current = GetSafeParent(current);
        }

        return null;
    }

    private void TryAnimateButtonPress(DependencyObject? source)
    {
        try
        {
            var button = FindAncestor<ButtonBase>(source);
            if (button is null)
                return;

            if (button.RenderTransform is not ScaleTransform scale)
            {
                scale = new ScaleTransform(1.0, 1.0);
                button.RenderTransform = scale;
                button.RenderTransformOrigin = new Point(0.5, 0.5);
            }

            var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };

            var shrinkX = new DoubleAnimation { To = 0.975, Duration = TimeSpan.FromMilliseconds(55),
                                                AutoReverse = true, EasingFunction = ease };

            var shrinkY = new DoubleAnimation { To = 0.975, Duration = TimeSpan.FromMilliseconds(55),
                                                AutoReverse = true, EasingFunction = ease };

            scale.BeginAnimation(ScaleTransform.ScaleXProperty, shrinkX);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, shrinkY);
        }
        catch
        {
            // Tactile feedback should never break quote workflow.
        }
    }

    private async Task TryLoadPdfPreviewAsync()
    {
        if (DataContext is not MainViewModel viewModel)
            return;

        if (viewModel.WorkflowStage != QuoteWorkflowStage.PdfPreview)
            return;

        var pdfUri = viewModel.GeneratedPdfUri;
        if (pdfUri is null || !File.Exists(pdfUri.LocalPath))
        {
            ShowPdfPreviewFallback("PDF preview will load after the quote PDF is generated.");
            return;
        }

        try
        {
            await EnsurePdfPreviewControlAsync();

            if (_pdfPreviewWebView is null)
            {
                ShowPdfPreviewFallback("Live preview is unavailable. Use Open Generated PDF.");
                return;
            }

            PdfPreviewFallback.Visibility = Visibility.Collapsed;
            _pdfPreviewWebView.Visibility = Visibility.Visible;
            _pdfPreviewWebView.Source = BuildPdfPreviewUri(pdfUri);
        }
        catch
        {
            ShowPdfPreviewFallback("Live preview could not load on this machine. Use Open Generated PDF.");
        }
    }

    private static Uri BuildPdfPreviewUri(Uri pdfUri)
    {
        // Hide the built-in Edge PDF toolbar so users cannot click the PDF full-screen button
        // and get stuck without a visible return path inside the embedded preview.
        var baseUri = pdfUri.AbsoluteUri.Split('#')[0];
        return new Uri(baseUri + "#toolbar=0&navpanes=0&scrollbar=1");
    }
    private async Task EnsurePdfPreviewControlAsync()
    {
        if (_pdfPreviewWebView is not null || _pdfPreviewInitializing)
            return;

        _pdfPreviewInitializing = true;

        try
        {
            var webView = new WebView2 { HorizontalAlignment = HorizontalAlignment.Stretch,
                                         VerticalAlignment = VerticalAlignment.Stretch };

            PdfPreviewHost.Content = webView;
            var webViewUserDataFolder = AppPaths.WebView2;

            var webViewEnvironment = await CoreWebView2Environment.CreateAsync(userDataFolder: webViewUserDataFolder);
            await webView.EnsureCoreWebView2Async(webViewEnvironment);

            if (webView.CoreWebView2 is not null)
            {
                webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            }

            _pdfPreviewWebView = webView;
        }
        finally
        {
            _pdfPreviewInitializing = false;
        }
    }

    private void ShowPdfPreviewFallback(string message)
    {
        try
        {
            PdfPreviewFallback.Visibility = Visibility.Visible;

            if (_pdfPreviewWebView is not null)
                _pdfPreviewWebView.Visibility = Visibility.Collapsed;
        }
        catch
        {
            // Preview fallback must never affect quote flow.
        }
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var settingsWindow =
                new SettingsWindow { Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };

            settingsWindow.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Settings could not be opened." + Environment.NewLine + Environment.NewLine + ex.Message,
                            "Settings Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ThemeToggleButton_Checked(object sender, RoutedEventArgs e)
    {
        if (_isApplyingTheme)
            return;
        ApplyTheme(true);
        UpdateHeaderLogo(true);
        SaveThemePreference(DarkThemeName);
        if (ThemeLabelText != null)
            ThemeLabelText.Text = "Dark Mode";
    }

    private void ThemeToggleButton_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_isApplyingTheme)
            return;
        ApplyTheme(false);
        UpdateHeaderLogo(false);
        SaveThemePreference(LightThemeName);
        if (ThemeLabelText != null)
            ThemeLabelText.Text = "Light Mode";
    }

    private static string SettingsPath => AppPaths.UiSettingsFile;

    private static string LoadThemePreference()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return DarkThemeName;
            if (new FileInfo(SettingsPath).Length > 16 * 1024)
                return DarkThemeName;
            using var stream = File.OpenRead(SettingsPath);
            var settings = JsonSerializer.Deserialize<UiSettings>(stream);
            return string.IsNullOrWhiteSpace(settings?.Theme) ? DarkThemeName : settings.Theme;
        }
        catch
        {
            return DarkThemeName;
        }
    }

    private static void SaveThemePreference(string theme)
    {
        try
        {
            var settings = new UiSettings { Theme = theme };
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            var directory = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var tempPath = SettingsPath + ".tmp";
            try
            {
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, SettingsPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }
        catch
        {
            // Theme persistence should never block the quote workflow.
        }
    }

    private void UpdateHeaderLogo(bool dark)
    {
        try
        {
            var asset = dark ? "/Assets/header_logo-dark.png" : "/Assets/header_logo-light.png";
            HeaderLogoImage.Source = new BitmapImage(new Uri(asset, UriKind.Relative));
        }
        catch
        {
            // Logo switching should never block app startup.
        }
    }
    private void ApplyTheme(bool dark)
    {
        WindowPresentationService.UpdateTheme(this, dark);
        try
        {
            _isApplyingTheme = true;
            var resources = Application.Current.Resources;

            if (dark)
            {
                SetGradientBrush(resources, "FlareDarkBrush", "#0B0D10", "#0B0D10");
                SetGradientBrush(resources, "GlassBackgroundBrush", "#0B0D10", "#0B0D10");
                SetBrush(resources, "WindowSurfaceBrush", "#0B0D10");
                SetBrush(resources, "WindowTitleBarBrush", "#090B0E");
                SetBrush(resources, "WindowHeaderBrush", "#0E1115");
                SetBrush(resources, "WindowWorkspaceBrush", "#090C10");
                SetBrush(resources, "SurfaceBrush", "#12161B");
                SetBrush(resources, "SurfaceRaisedBrush", "#171C22");
                SetBrush(resources, "SurfaceSubtleBrush", "#0F1318");
                SetBrush(resources, "DividerBrush", "#242A31");
                SetBrush(resources, "ControlBorderBrush", "#303841");
                SetBrush(resources, "ControlHoverBrush", "#20262D");
                SetBrush(resources, "ControlPressedBrush", "#262D35");
                SetBrush(resources, "FocusRingBrush", "#789B00");
                SetBrush(resources, "SuccessMutedBrush", "#B4C58C");
                SetBrush(resources, "WarningBrush", "#E2B04A");
                SetBrush(resources, "DangerBrush", "#FF7377");
                SetBrush(resources, "GlassHeaderBrush", "#0E1115");

                SetBrush(resources, "FlareCardBrush", "#12161B");
                SetBrush(resources, "FlareCardAltBrush", "#0F1318");
                resources["GlassCardBrush"] = resources["FlareCardBrush"];
                resources["GlassCardAltBrush"] = resources["FlareCardAltBrush"];

                SetBrush(resources, "FlareInputBrush", "#0D1014");
                SetBrush(resources, "GlassInputBrush", "#0D1014");
                SetBrush(resources, "FlareBorderBrush", "#2A3038");
                SetBrush(resources, "GlassBorderBrush", "#2A3038");

                SetBrush(resources, "FlareTextBrush", "#F5F3EE");
                SetBrush(resources, "FlareMutedTextBrush", "#A4A8AE");
                SetBrush(resources, "FlareAccentBrush", "#99CC00");
                SetBrush(resources, "FlareAccentTextBrush", "#141A00");

                SetBrush(resources, "FlareButtonBrush", "#171C22");
                SetBrush(resources, "FlareButtonBorderBrush", "#343C46");
                SetBrush(resources, "FlareChipBrush", "#181D23");
                SetBrush(resources, "FlareChipBorderBrush", "#323A44");
                SetBrush(resources, "GlassButtonHoverBrush", "#20262D");
                SetBrush(resources, "GlassPopupBrush", "#171C22");
            }
            else
            {
                SetGradientBrush(resources, "FlareDarkBrush", "#F3F6F9", "#F3F6F9");
                SetGradientBrush(resources, "GlassBackgroundBrush", "#F3F6F9", "#F3F6F9");
                SetBrush(resources, "WindowSurfaceBrush", "#F3F6F9");
                SetBrush(resources, "WindowTitleBarBrush", "#F9FBFD");
                SetBrush(resources, "WindowHeaderBrush", "#F5F8FB");
                SetBrush(resources, "WindowWorkspaceBrush", "#EDF2F6");
                SetBrush(resources, "SurfaceBrush", "#FFFFFF");
                SetBrush(resources, "SurfaceRaisedBrush", "#FFFFFF");
                SetBrush(resources, "SurfaceSubtleBrush", "#F5F8FB");
                SetBrush(resources, "DividerBrush", "#DDE4EA");
                SetBrush(resources, "ControlBorderBrush", "#C3CED8");
                SetBrush(resources, "ControlHoverBrush", "#E8EEF3");
                SetBrush(resources, "ControlPressedBrush", "#DDE6ED");
                SetBrush(resources, "FocusRingBrush", "#6F8A27");
                SetBrush(resources, "SuccessMutedBrush", "#587023");
                SetBrush(resources, "WarningBrush", "#8A5B00");
                SetBrush(resources, "DangerBrush", "#B42318");
                SetBrush(resources, "GlassHeaderBrush", "#F5F8FB");

                SetBrush(resources, "FlareCardBrush", "#FFFFFF");
                SetBrush(resources, "FlareCardAltBrush", "#F5F8FB");
                resources["GlassCardBrush"] = resources["FlareCardBrush"];
                resources["GlassCardAltBrush"] = resources["FlareCardAltBrush"];

                SetBrush(resources, "FlareInputBrush", "#F9FBFD");
                SetBrush(resources, "GlassInputBrush", "#F9FBFD");
                SetBrush(resources, "FlareBorderBrush", "#D5DEE6");
                SetBrush(resources, "GlassBorderBrush", "#D5DEE6");

                SetBrush(resources, "FlareTextBrush", "#17212B");
                SetBrush(resources, "FlareMutedTextBrush", "#5F6F7E");
                SetBrush(resources, "FlareAccentBrush", "#8FB82B");
                SetBrush(resources, "FlareAccentTextBrush", "#141A00");

                SetBrush(resources, "FlareButtonBrush", "#F8FAFC");
                SetBrush(resources, "FlareButtonBorderBrush", "#C3CED8");
                SetBrush(resources, "FlareChipBrush", "#EFF4F7");
                SetBrush(resources, "FlareChipBorderBrush", "#C9D4DD");
                SetBrush(resources, "GlassButtonHoverBrush", "#E8EEF3");
                SetBrush(resources, "GlassPopupBrush", "#FFFFFF");
            }

            ApplyDropdownThemeResources(dark);
        }
        finally
        {
            _isApplyingTheme = false;
        }
    }

    private static void SetGradientBrush(ResourceDictionary resources, string key, string startHexColor,
                                         string endHexColor)
    {
        var start = (Color)ColorConverter.ConvertFromString(startHexColor);
        var end = (Color)ColorConverter.ConvertFromString(endHexColor);
        resources[key] = new LinearGradientBrush(start, end, 45);
    }

    private static void SetBrush(ResourceDictionary resources, string key, string hexColor)
    {
        resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hexColor));
    }

    private sealed class UiSettings
    {
        public string Theme { get; set; } = DarkThemeName;
    }

    private static string SafeForUser(string message)
    {
        var value = (message ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
            value = value.Replace(userProfile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        return string.IsNullOrWhiteSpace(value) ? "Unexpected error." : value;
    }

    private async Task CheckForUpdatesOnStartupAsync()
    {
        if (_hasCheckedForStartupUpdates)
            return;

        _hasCheckedForStartupUpdates = true;

        try
        {
            var settingsService = App.Services.GetService<ISettingsService>();
            var updateService = App.Services.GetService<IUpdateService>();

            if (settingsService is null || updateService is null)
                return;

            var settings = await settingsService.LoadAsync();
            if (!settings.CheckUpdatesOnStartup)
                return;

            var currentVersion = GetCurrentAppVersion();
            var result = await updateService.CheckAsync(currentVersion);

            if (!result.UpdateAvailable || string.IsNullOrWhiteSpace(result.InstallerUrl))
                return;

            if (!IsRemoteVersionNewerThanCurrent(result.LatestVersion))

            {

                return;
            }

            var updateWindow = new UpdateAvailableWindow(result.LatestVersion, result.Notes) { Owner = this };

            if (updateWindow.ShowDialog() != true)
                return;

            await DownloadVerifyAndLaunchInstallerAsync(result.InstallerUrl, result.Sha256,
                                                         result.ExpectedSizeBytes, result.LatestVersion);
        }
        catch
        {
            // Startup update checks must never block the app from opening.
        }
    }

    private static string GetCurrentAppVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version is null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    private static async Task DownloadVerifyAndLaunchInstallerAsync(string installerUrl, string? expectedSha256,
                                                                    long expectedSizeBytes,
                                                                    string latestVersion)
    {
        if (!UpdateTrustPolicy.TryGetTrustedInstallerUri(installerUrl, latestVersion, out var uri))
        {
            MessageBox.Show("The update link is outside the trusted Flare GitHub release lane.", "Update Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (!UpdateTrustPolicy.IsValidSha256(expectedSha256) ||
            !UpdateTrustPolicy.IsValidInstallerSize(expectedSizeBytes))
        {
            MessageBox.Show("The update manifest is missing valid installer verification data.",
                            "Update Verification Required", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var normalizedSha256 = expectedSha256!.Trim().ToLowerInvariant();
        var updatesDir = AppPaths.Updates;
        var fileName = Path.GetFileName(uri.LocalPath);
        var installerPath = Path.Combine(updatesDir, SafeUpdateFileName(fileName));
        var downloadPath = installerPath + ".download";

        try
        {
            if (File.Exists(downloadPath))
                File.Delete(downloadPath);

            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            if (!UpdateTrustPolicy.IsTrustedDownloadResponseUri(response.RequestMessage?.RequestUri))
                throw new InvalidDataException("The installer download redirected to an untrusted host.");

            if (response.Content.Headers.ContentLength is long contentLength && contentLength != expectedSizeBytes)
                throw new InvalidDataException("The installer size does not match the update manifest.");

            var buffer = new byte[81920];
            long totalBytes = 0;

            await using (var remote = await response.Content.ReadAsStreamAsync())
            await using (var local = new FileStream(downloadPath, FileMode.CreateNew, FileAccess.Write,
                                                    FileShare.None, bufferSize: 81920,
                                                    FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                while (true)
                {
                    var count = await remote.ReadAsync(buffer.AsMemory());
                    if (count == 0)
                        break;

                    totalBytes += count;
                    if (totalBytes > expectedSizeBytes || totalBytes > UpdateTrustPolicy.MaxInstallerBytes)
                        throw new InvalidDataException("The installer download exceeded the expected size.");

                    await local.WriteAsync(buffer.AsMemory(0, count));
                }

                await local.FlushAsync();
            }

            if (totalBytes != expectedSizeBytes)
                throw new InvalidDataException("The installer download was incomplete.");

            var actualSha256 = ComputeSha256(downloadPath);
            if (!string.Equals(actualSha256, normalizedSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The installer failed SHA-256 verification.");

            File.Move(downloadPath, installerPath, overwrite: true);
            Process.Start(new ProcessStartInfo(installerPath) { UseShellExecute = true });
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            try
            {
                if (File.Exists(downloadPath))
                    File.Delete(downloadPath);
            }
            catch
            {
            }

            MessageBox.Show("The update could not be verified and was not installed. " + SafeForUser(ex.Message),
                            "Update Verification Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string ComputeSha256(string path)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }

    private static string SafeUpdateFileName(string value)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            value = value.Replace(c, '-');

        return string.IsNullOrWhiteSpace(value) ? "FlareFireplacesQuotesSetup.exe" : value.Trim();
    }

    private static bool IsRemoteVersionNewerThanCurrent(string? latestVersion)
    {
        var current = GetCurrentAppVersionForUpdateCheck();
        var latest = ParseAppVersionForUpdateCheck(latestVersion);

        if (latest is null || current is null)
            return false;

        return latest.CompareTo(current) > 0;
    }

    private static Version? GetCurrentAppVersionForUpdateCheck()
    {
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();

        var info = assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                       .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
                       .FirstOrDefault()
                       ?.InformationalVersion;

        return ParseAppVersionForUpdateCheck(info) ??
               ParseAppVersionForUpdateCheck(assembly.GetName().Version?.ToString());
    }

    private static Version? ParseAppVersionForUpdateCheck(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var match = System.Text.RegularExpressions.Regex.Match(value, @"\d+(?:\.\d+){0,3}");
        if (!match.Success)
            return null;

        var parts = match.Value.Split('.').Select(p => int.TryParse(p, out var n) ? n : 0).ToList();

        while (parts.Count < 4)
            parts.Add(0);

        return new Version(parts[0], parts[1], parts[2], parts[3]);
    }

    private void ApplyDropdownThemeResources(bool isDarkMode)
    {
        static SolidColorBrush Brush(byte r, byte g, byte b, byte a = 255)
        {
            var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            brush.Freeze();
            return brush;
        }

        void SetBrush(string key, SolidColorBrush brush)
        {
            Resources[key] = brush;
            if (Application.Current?.Resources != null)
            {
                Application.Current.Resources[key] = brush;
            }
        }

        if (isDarkMode)
        {
            SetBrush("DropdownPopupBrush", Brush(21, 31, 42));
            SetBrush("DropdownInputBrush", Brush(14, 22, 31));
            SetBrush("DropdownOptionBrush", Brush(24, 36, 49));
            SetBrush("DropdownOptionHoverBrush", Brush(32, 45, 58));
            SetBrush("DropdownOptionBorderBrush", Brush(49, 66, 82));
            SetBrush("DropdownTextBrush", Brush(244, 247, 250));
            SetBrush("DropdownMutedTextBrush", Brush(152, 166, 181));
        }
        else
        {
            SetBrush("DropdownPopupBrush", Brush(255, 255, 255));
            SetBrush("DropdownInputBrush", Brush(249, 251, 253));
            SetBrush("DropdownOptionBrush", Brush(245, 248, 251));
            SetBrush("DropdownOptionHoverBrush", Brush(232, 238, 243));
            SetBrush("DropdownOptionBorderBrush", Brush(195, 206, 216));
            SetBrush("DropdownTextBrush", Brush(23, 33, 43));
            SetBrush("DropdownMutedTextBrush", Brush(95, 111, 126));
        }
    }

    private void SetAppVersionText()
    {
        var version = typeof(MainWindow).Assembly.GetName().Version;

        if (version is null)
        {
            AppVersionText.Text = "v";
            return;
        }

        AppVersionText.Text = $"v{version.Major}.{version.Minor}.{version.Build}";
    }

    private void RecallLastQuoteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.ContextMenu is not null)
        {
            element.ContextMenu.PlacementTarget = element;
            element.ContextMenu.Placement = PlacementMode.Top;
            element.ContextMenu.IsOpen = true;
            e.Handled = true;
        }
    }

}
