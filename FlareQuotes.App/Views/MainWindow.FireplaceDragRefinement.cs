using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using FlareQuotes.App.ViewModels;
using FlareQuotes.Core.Models;

namespace FlareQuotes.App.Views;

public partial class MainWindow
{
    private FrameworkElement? _refinedFireplaceDragCard;
    private FireplaceQuoteDraft? _refinedFireplaceDragItem;
    private ScrollViewer? _refinedFireplaceScrollViewer;
    private Point _refinedFireplaceDragStart;
    private Point _refinedFireplacePointerOffset;
    private bool _refinedFireplaceIsDragging;
    private int _refinedFireplaceSourceIndex = -1;
    private int _refinedFireplaceProposedIndex = -1;
    private AdornerLayer? _refinedFireplaceAdornerLayer;
    private FireplaceCardDragAdorner? _refinedFireplaceAdorner;

    static MainWindow()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            Mouse.PreviewMouseDownEvent,
            new MouseButtonEventHandler(RefinedFireplaceDrag_PreviewMouseDown),
            true);
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            Mouse.PreviewMouseMoveEvent,
            new MouseEventHandler(RefinedFireplaceDrag_PreviewMouseMove),
            true);
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            Mouse.PreviewMouseUpEvent,
            new MouseButtonEventHandler(RefinedFireplaceDrag_PreviewMouseUp),
            true);
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            Mouse.LostMouseCaptureEvent,
            new MouseEventHandler(RefinedFireplaceDrag_LostMouseCapture),
            true);
    }

    private static void RefinedFireplaceDrag_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not MainWindow window || e.ChangedButton != MouseButton.Left ||
            e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        var handle = FindRefinedFireplaceDragHandle(source);
        if (handle is null)
            return;

        var card = FindRefinedFireplaceCard(handle);
        if (card?.DataContext is not FireplaceQuoteDraft fireplace ||
            window.DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var sourceIndex = viewModel.Fireplaces.IndexOf(fireplace);
        if (sourceIndex < 0)
            return;

        window.CancelRefinedFireplaceDrag();
        window._refinedFireplaceDragCard = card;
        window._refinedFireplaceDragItem = fireplace;
        window._refinedFireplaceScrollViewer = FindRefinedAncestor<ScrollViewer>(card);
        window._refinedFireplaceDragStart = e.GetPosition(window);
        window._refinedFireplacePointerOffset = e.GetPosition(card);
        window._refinedFireplaceSourceIndex = sourceIndex;
        window._refinedFireplaceProposedIndex = sourceIndex;

        Mouse.Capture(window, CaptureMode.SubTree);
        e.Handled = true;
    }

    private static void RefinedFireplaceDrag_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not MainWindow window || window._refinedFireplaceDragCard is null ||
            window._refinedFireplaceDragItem is null)
        {
            return;
        }

        e.Handled = true;

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            window.CancelRefinedFireplaceDrag();
            return;
        }

        var current = e.GetPosition(window);
        var movedEnough =
            Math.Abs(current.X - window._refinedFireplaceDragStart.X) >= SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(current.Y - window._refinedFireplaceDragStart.Y) >= SystemParameters.MinimumVerticalDragDistance;

        if (!window._refinedFireplaceIsDragging && !movedEnough)
            return;

        if (!window._refinedFireplaceIsDragging)
            window.BeginRefinedFireplaceDrag();

        window.AutoScrollRefinedFireplaceList(e);
        window.UpdateRefinedFireplaceAdorner(e.GetPosition(window.WindowFrame));
        window.UpdateRefinedFireplaceInsertion(current);
    }

    private static void RefinedFireplaceDrag_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not MainWindow window || e.ChangedButton != MouseButton.Left ||
            window._refinedFireplaceDragCard is null)
        {
            return;
        }

        e.Handled = true;

        if (window._refinedFireplaceIsDragging)
            window.CommitRefinedFireplaceDrop();
        else
            window.CancelRefinedFireplaceDrag();
    }

    private static void RefinedFireplaceDrag_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (sender is MainWindow window && window._refinedFireplaceDragCard is not null &&
            !ReferenceEquals(Mouse.Captured, window))
        {
            window.CancelRefinedFireplaceDrag();
        }
    }

    private void BeginRefinedFireplaceDrag()
    {
        if (_refinedFireplaceDragCard is null)
            return;

        _refinedFireplaceIsDragging = true;

        var snapshot = CaptureRefinedFireplaceCard(_refinedFireplaceDragCard);
        _refinedFireplaceAdornerLayer = AdornerLayer.GetAdornerLayer(WindowFrame) ??
                                        AdornerLayer.GetAdornerLayer(_refinedFireplaceDragCard);

        if (_refinedFireplaceAdornerLayer is not null && snapshot is not null)
        {
            _refinedFireplaceAdorner = new FireplaceCardDragAdorner(
                WindowFrame,
                snapshot,
                _refinedFireplaceDragCard.ActualWidth,
                _refinedFireplaceDragCard.ActualHeight);
            _refinedFireplaceAdornerLayer.Add(_refinedFireplaceAdorner);
        }

        _refinedFireplaceDragCard.Opacity = 0.06;
        Panel.SetZIndex(_refinedFireplaceDragCard, -1);
    }

    private void UpdateRefinedFireplaceInsertion(Point current)
    {
        if (_refinedFireplaceDragCard is null || _refinedFireplaceDragItem is null ||
            DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var sourceIndex = viewModel.Fireplaces.IndexOf(_refinedFireplaceDragItem);
        if (sourceIndex < 0)
        {
            CancelRefinedFireplaceDrag();
            return;
        }

        _refinedFireplaceSourceIndex = sourceIndex;

        var cards = GetRefinedFireplaceCards(viewModel);
        if (cards.Count <= 1)
            return;

        var dragCenterY = current.Y - _refinedFireplacePointerOffset.Y +
                          _refinedFireplaceDragCard.ActualHeight / 2;
        var proposedIndex = 0;

        foreach (var entry in cards)
        {
            if (ReferenceEquals(entry.Item, _refinedFireplaceDragItem))
                continue;

            if (dragCenterY > entry.BaseBounds.Top + entry.BaseBounds.Height / 2)
                proposedIndex++;
        }

        proposedIndex = Math.Clamp(proposedIndex, 0, viewModel.Fireplaces.Count - 1);
        if (proposedIndex == _refinedFireplaceProposedIndex)
            return;

        _refinedFireplaceProposedIndex = proposedIndex;
        AnimateRefinedFireplaceGap(cards, sourceIndex, proposedIndex);
    }

    private List<RefinedFireplaceCardEntry> GetRefinedFireplaceCards(MainViewModel viewModel)
    {
        var entries = new List<RefinedFireplaceCardEntry>();

        foreach (var card in FindSelectedChipElements("FireplaceSummaryCard"))
        {
            if (card.DataContext is not FireplaceQuoteDraft item)
                continue;

            var index = viewModel.Fireplaces.IndexOf(item);
            if (index < 0)
                continue;

            var bounds = GetBoundsRelativeToWindow(card);
            if (bounds.IsEmpty)
                continue;

            var translate = EnsureSelectedChipTranslateTransform(card);
            bounds.Offset(0, -translate.Y);
            entries.Add(new RefinedFireplaceCardEntry(card, item, index, bounds));
        }

        return entries.OrderBy(entry => entry.Index).ToList();
    }

    private void AnimateRefinedFireplaceGap(
        IReadOnlyList<RefinedFireplaceCardEntry> cards,
        int sourceIndex,
        int proposedIndex)
    {
        var slotHeight = GetRefinedFireplaceSlotHeight();
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        foreach (var entry in cards)
        {
            if (ReferenceEquals(entry.Item, _refinedFireplaceDragItem))
                continue;

            var targetOffset = 0d;

            if (proposedIndex < sourceIndex && entry.Index >= proposedIndex && entry.Index < sourceIndex)
                targetOffset = slotHeight;
            else if (proposedIndex > sourceIndex && entry.Index > sourceIndex && entry.Index <= proposedIndex)
                targetOffset = -slotHeight;

            var translate = EnsureSelectedChipTranslateTransform(entry.Card);
            translate.BeginAnimation(
                TranslateTransform.YProperty,
                new DoubleAnimation
                {
                    To = targetOffset,
                    Duration = TimeSpan.FromMilliseconds(155),
                    EasingFunction = ease,
                    FillBehavior = FillBehavior.HoldEnd
                },
                HandoffBehavior.SnapshotAndReplace);
        }
    }

    private double GetRefinedFireplaceSlotHeight()
    {
        if (_refinedFireplaceDragCard is null)
            return 0;

        var container = FindRefinedAncestor<ButtonBase>(_refinedFireplaceDragCard) as FrameworkElement;
        var marginHeight = container is null ? 10 : container.Margin.Top + container.Margin.Bottom;
        return Math.Max(1, _refinedFireplaceDragCard.ActualHeight + marginHeight);
    }

    private void AutoScrollRefinedFireplaceList(MouseEventArgs e)
    {
        var viewer = _refinedFireplaceScrollViewer;
        if (viewer is null || viewer.ScrollableHeight <= 0)
            return;

        var point = e.GetPosition(viewer);
        const double edge = 42;
        const double step = 18;

        if (point.Y < edge)
            viewer.ScrollToVerticalOffset(Math.Max(0, viewer.VerticalOffset - step));
        else if (point.Y > viewer.ActualHeight - edge)
            viewer.ScrollToVerticalOffset(Math.Min(viewer.ScrollableHeight, viewer.VerticalOffset + step));
    }

    private void UpdateRefinedFireplaceAdorner(Point pointerInRoot)
    {
        _refinedFireplaceAdorner?.SetPosition(
            pointerInRoot.X - _refinedFireplacePointerOffset.X,
            pointerInRoot.Y - _refinedFireplacePointerOffset.Y);
    }

    private void CommitRefinedFireplaceDrop()
    {
        if (_refinedFireplaceDragItem is null || DataContext is not MainViewModel viewModel)
        {
            CancelRefinedFireplaceDrag();
            return;
        }

        var source = _refinedFireplaceDragItem;
        var sourceIndex = viewModel.Fireplaces.IndexOf(source);
        var proposedIndex = Math.Clamp(_refinedFireplaceProposedIndex, 0, viewModel.Fireplaces.Count - 1);

        ResetRefinedFireplacePreview();

        if (sourceIndex >= 0 && sourceIndex != proposedIndex)
        {
            var target = viewModel.Fireplaces[proposedIndex];
            viewModel.MoveFireplace(source, target);
        }

        FinishRefinedFireplaceDrag();
    }

    private void CancelRefinedFireplaceDrag()
    {
        ResetRefinedFireplacePreview();
        FinishRefinedFireplaceDrag();
    }

    private void ResetRefinedFireplacePreview()
    {
        if (DataContext is MainViewModel viewModel)
        {
            foreach (var entry in GetRefinedFireplaceCards(viewModel))
            {
                var translate = EnsureSelectedChipTranslateTransform(entry.Card);
                translate.BeginAnimation(TranslateTransform.YProperty, null);
                translate.Y = 0;
                entry.Card.Opacity = 1;
                Panel.SetZIndex(entry.Card, 0);
            }
        }

        if (_refinedFireplaceAdorner is not null && _refinedFireplaceAdornerLayer is not null)
            _refinedFireplaceAdornerLayer.Remove(_refinedFireplaceAdorner);

        _refinedFireplaceAdorner = null;
        _refinedFireplaceAdornerLayer = null;
    }

    private void FinishRefinedFireplaceDrag()
    {
        var releaseCapture = ReferenceEquals(Mouse.Captured, this);

        _refinedFireplaceDragCard = null;
        _refinedFireplaceDragItem = null;
        _refinedFireplaceScrollViewer = null;
        _refinedFireplaceIsDragging = false;
        _refinedFireplaceSourceIndex = -1;
        _refinedFireplaceProposedIndex = -1;

        if (releaseCapture)
            Mouse.Capture(null);
    }

    private static FrameworkElement? FindRefinedFireplaceDragHandle(DependencyObject source)
    {
        DependencyObject? current = source;

        while (current is not null)
        {
            if (current is FrameworkElement element)
            {
                if (string.Equals(element.Tag?.ToString(), "FireplaceSummaryCard", StringComparison.OrdinalIgnoreCase))
                    return null;

                if (element.Cursor == Cursors.SizeNS &&
                    string.Equals(element.ToolTip?.ToString(), "Click and drag to reorder this fireplace",
                                  StringComparison.Ordinal))
                {
                    return element;
                }
            }

            current = GetSafeParent(current);
        }

        return null;
    }

    private static FrameworkElement? FindRefinedFireplaceCard(DependencyObject source)
    {
        DependencyObject? current = source;

        while (current is not null)
        {
            if (current is FrameworkElement element &&
                string.Equals(element.Tag?.ToString(), "FireplaceSummaryCard", StringComparison.OrdinalIgnoreCase))
            {
                return element;
            }

            current = GetSafeParent(current);
        }

        return null;
    }

    private static T? FindRefinedAncestor<T>(DependencyObject source)
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

    private static ImageSource? CaptureRefinedFireplaceCard(FrameworkElement source)
    {
        if (source.ActualWidth <= 0 || source.ActualHeight <= 0)
            return null;

        try
        {
            var dpi = VisualTreeHelper.GetDpi(source);
            var pixelWidth = Math.Max(1, (int)Math.Ceiling(source.ActualWidth * dpi.DpiScaleX));
            var pixelHeight = Math.Max(1, (int)Math.Ceiling(source.ActualHeight * dpi.DpiScaleY));
            var bitmap = new RenderTargetBitmap(
                pixelWidth,
                pixelHeight,
                96 * dpi.DpiScaleX,
                96 * dpi.DpiScaleY,
                PixelFormats.Pbgra32);
            bitmap.Render(source);
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private sealed record RefinedFireplaceCardEntry(
        FrameworkElement Card,
        FireplaceQuoteDraft Item,
        int Index,
        Rect BaseBounds);

    private sealed class FireplaceCardDragAdorner : Adorner
    {
        private readonly VisualCollection _visuals;
        private readonly Border _preview;
        private double _left;
        private double _top;

        public FireplaceCardDragAdorner(
            UIElement adornedElement,
            ImageSource snapshot,
            double width,
            double height) : base(adornedElement)
        {
            IsHitTestVisible = false;
            _preview = new Border
            {
                Width = width,
                Height = height,
                CornerRadius = new CornerRadius(11),
                BorderBrush = new SolidColorBrush(Color.FromRgb(153, 204, 0)),
                BorderThickness = new Thickness(1.25),
                Background = new ImageBrush(snapshot) { Stretch = Stretch.Fill },
                Opacity = 0.97,
                IsHitTestVisible = false,
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 24,
                    ShadowDepth = 7,
                    Direction = 270,
                    Opacity = 0.34
                }
            };
            _visuals = new VisualCollection(this) { _preview };
        }

        public void SetPosition(double left, double top)
        {
            _left = left;
            _top = top;
            InvalidateArrange();
        }

        protected override int VisualChildrenCount => _visuals.Count;

        protected override Visual GetVisualChild(int index) => _visuals[index];

        protected override Size MeasureOverride(Size constraint)
        {
            _preview.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return AdornedElement.RenderSize;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            _preview.Arrange(new Rect(_left, _top, _preview.DesiredSize.Width, _preview.DesiredSize.Height));
            return finalSize;
        }
    }
}
