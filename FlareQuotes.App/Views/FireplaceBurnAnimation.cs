using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace FlareQuotes.App.Views;

/// <summary>
/// Plays the restrained fireplace-card removal animation, then invokes
/// <paramref name="onComplete"/> to perform the real removal.
///
/// The effect is deliberately lightweight: a short vector flame band follows a
/// bottom-to-top opacity wipe, then the card closes its layout space. It does not
/// use frame timers, per-pixel rendering, bitmap allocation, or quote-processing state.
/// </summary>
internal static class FireplaceBurnAnimation
{
    private const double BurnDurationMs = 900;
    private const double CollapseDurationMs = 160;

    private static readonly HashSet<FrameworkElement> ActiveCards = [];
    private static readonly Geometry FlameGeometry = CreateFlameGeometry();
    private static readonly Brush FlameBrush = CreateFlameBrush();

    public static void Burn(FrameworkElement card, Panel overlayHost, Action onComplete)
    {
        if (card is null || overlayHost is null)
        {
            SafeComplete(onComplete);
            return;
        }

        // A repeated click while the first animation is running must not start a
        // second effect or execute the removal command twice.
        if (ActiveCards.Contains(card))
            return;

        if (!SystemParameters.ClientAreaAnimation ||
            card.ActualWidth < 8 ||
            card.ActualHeight < 8 ||
            overlayHost is not Canvas overlayCanvas)
        {
            SafeComplete(onComplete);
            return;
        }

        var originalOpacityMask = card.OpacityMask;
        var originalOpacity = card.Opacity;
        var originalHeight = card.Height;
        var originalHitTestVisible = card.IsHitTestVisible;

        Canvas? burnBand = null;
        var finished = false;

        void RestoreDetachedCardState()
        {
            try
            {
                card.BeginAnimation(UIElement.OpacityProperty, null);
                card.BeginAnimation(FrameworkElement.HeightProperty, null);
                card.OpacityMask = originalOpacityMask;
                card.Opacity = originalOpacity;
                card.IsHitTestVisible = originalHitTestVisible;

                if (double.IsNaN(originalHeight))
                    card.ClearValue(FrameworkElement.HeightProperty);
                else
                    card.Height = originalHeight;
            }
            catch
            {
                // The card may already be detached after the collection removal.
            }
        }

        void Finish()
        {
            if (finished)
                return;

            finished = true;
            ActiveCards.Remove(card);

            try
            {
                if (burnBand is not null)
                    overlayCanvas.Children.Remove(burnBand);
            }
            catch
            {
                // Cosmetic cleanup must never block removal.
            }

            // The collection change is synchronous. Restore afterward only so a
            // failed command cannot leave a still-visible card collapsed or disabled.
            SafeComplete(onComplete);
            RestoreDetachedCardState();
        }

        try
        {
            ActiveCards.Add(card);
            card.IsHitTestVisible = false;

            var cardWidth = card.ActualWidth;
            var cardHeight = card.ActualHeight;
            var topLeft = card.TransformToVisual(overlayCanvas).Transform(new Point(0, 0));

            var mask = CreateBurnMask(
                out var transparentStop,
                out var featherStop,
                out var visibleStop);

            card.OpacityMask = mask;

            var burnDuration = TimeSpan.FromMilliseconds(BurnDurationMs);
            AnimateStop(transparentStop, 0.0, 0.92, burnDuration);
            AnimateStop(featherStop, 0.0, 0.965, burnDuration);
            AnimateStop(visibleStop, 0.001, 1.0, burnDuration);

            burnBand = CreateBurnBand(cardWidth);
            Canvas.SetLeft(burnBand, topLeft.X);
            Canvas.SetTop(burnBand, topLeft.Y + cardHeight - 19);
            overlayCanvas.Children.Add(burnBand);

            var rise = new DoubleAnimation
            {
                From = topLeft.Y + cardHeight - 19,
                To = topLeft.Y - 22,
                Duration = burnDuration,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                FillBehavior = FillBehavior.HoldEnd
            };

            rise.Completed += (_, _) =>
            {
                try
                {
                    var measuredHeight = Math.Max(0, card.ActualHeight);
                    card.Height = measuredHeight;

                    var collapse = new DoubleAnimation
                    {
                        From = measuredHeight,
                        To = 0,
                        Duration = TimeSpan.FromMilliseconds(CollapseDurationMs),
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
                        FillBehavior = FillBehavior.HoldEnd
                    };

                    collapse.Completed += (_, _) => Finish();

                    card.BeginAnimation(
                        UIElement.OpacityProperty,
                        new DoubleAnimation
                        {
                            From = card.Opacity,
                            To = 0,
                            Duration = TimeSpan.FromMilliseconds(CollapseDurationMs * 0.8),
                            FillBehavior = FillBehavior.HoldEnd
                        },
                        HandoffBehavior.SnapshotAndReplace);

                    if (burnBand is not null)
                    {
                        burnBand.BeginAnimation(
                            UIElement.OpacityProperty,
                            new DoubleAnimation
                            {
                                From = burnBand.Opacity,
                                To = 0,
                                Duration = TimeSpan.FromMilliseconds(CollapseDurationMs),
                                FillBehavior = FillBehavior.HoldEnd
                            },
                            HandoffBehavior.SnapshotAndReplace);
                    }

                    card.BeginAnimation(
                        FrameworkElement.HeightProperty,
                        collapse,
                        HandoffBehavior.SnapshotAndReplace);
                }
                catch
                {
                    Finish();
                }
            };

            burnBand.BeginAnimation(
                Canvas.TopProperty,
                rise,
                HandoffBehavior.SnapshotAndReplace);
        }
        catch
        {
            Finish();
        }
    }

    private static LinearGradientBrush CreateBurnMask(
        out GradientStop transparentStop,
        out GradientStop featherStop,
        out GradientStop visibleStop)
    {
        transparentStop = new GradientStop(Colors.Transparent, 0.0);
        featherStop = new GradientStop(Color.FromArgb(72, 255, 255, 255), 0.0);
        visibleStop = new GradientStop(Colors.White, 0.001);

        return new LinearGradientBrush
        {
            StartPoint = new Point(0.5, 1.0),
            EndPoint = new Point(0.5, 0.0),
            MappingMode = BrushMappingMode.RelativeToBoundingBox,
            GradientStops = new GradientStopCollection
            {
                transparentStop,
                featherStop,
                visibleStop
            }
        };
    }

    private static void AnimateStop(GradientStop stop, double from, double to, TimeSpan duration)
    {
        stop.BeginAnimation(
            GradientStop.OffsetProperty,
            new DoubleAnimation
            {
                From = from,
                To = to,
                Duration = duration,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                FillBehavior = FillBehavior.HoldEnd
            },
            HandoffBehavior.SnapshotAndReplace);
    }

    private static Canvas CreateBurnBand(double width)
    {
        var band = new Canvas
        {
            Width = width,
            Height = 34,
            Opacity = 0.64,
            IsHitTestVisible = false,
            ClipToBounds = false
        };

        var glowWidth = Math.Max(1, width - 12);

        var glow = new Border
        {
            Width = glowWidth,
            Height = 8,
            CornerRadius = new CornerRadius(4),
            Background = CreateGlowBrush(),
            Effect = new BlurEffect
            {
                Radius = 5,
                KernelType = KernelType.Gaussian
            },
            IsHitTestVisible = false
        };

        Canvas.SetLeft(glow, 6);
        Canvas.SetTop(glow, 23);
        band.Children.Add(glow);

        var emberLine = new Rectangle
        {
            Width = glowWidth,
            Height = 1.5,
            RadiusX = 0.75,
            RadiusY = 0.75,
            Fill = CreateEmberLineBrush(),
            Opacity = 0.72,
            IsHitTestVisible = false
        };

        Canvas.SetLeft(emberLine, 6);
        Canvas.SetTop(emberLine, 26);
        band.Children.Add(emberLine);

        var flameCount = Math.Clamp((int)Math.Round(width / 31.0), 6, 11);
        var spacing = width / (flameCount + 1);
        var heights = new[] { 13.0, 17.0, 12.0, 18.0, 14.0, 16.0, 11.0, 17.0, 13.0, 15.0, 12.0 };
        var durations = new[] { 760.0, 690.0, 820.0, 730.0, 790.0, 680.0, 840.0, 710.0, 770.0, 700.0, 810.0 };

        for (var index = 0; index < flameCount; index++)
        {
            var flameHeight = heights[index % heights.Length];
            var flameWidth = Math.Max(6.5, flameHeight * 0.43);
            var duration = TimeSpan.FromMilliseconds(durations[index % durations.Length]);

            var scale = new ScaleTransform(1.0, 0.82);
            var rotate = new RotateTransform(index % 2 == 0 ? -0.7 : 0.7);
            var transforms = new TransformGroup();
            transforms.Children.Add(scale);
            transforms.Children.Add(rotate);

            var flame = new Path
            {
                Data = FlameGeometry,
                Fill = FlameBrush,
                Width = flameWidth,
                Height = flameHeight,
                Stretch = Stretch.Fill,
                Opacity = 0.52,
                RenderTransformOrigin = new Point(0.5, 1.0),
                RenderTransform = transforms,
                IsHitTestVisible = false
            };

            Canvas.SetLeft(flame, (spacing * (index + 1)) - (flameWidth / 2));
            Canvas.SetTop(flame, 27 - flameHeight);
            band.Children.Add(flame);

            scale.BeginAnimation(
                ScaleTransform.ScaleYProperty,
                new DoubleAnimation
                {
                    From = 0.76,
                    To = 1.0,
                    Duration = duration,
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    BeginTime = TimeSpan.FromMilliseconds(index * 43),
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                });

            rotate.BeginAnimation(
                RotateTransform.AngleProperty,
                new DoubleAnimation
                {
                    From = index % 2 == 0 ? -1.2 : 1.2,
                    To = index % 2 == 0 ? 1.0 : -1.0,
                    Duration = TimeSpan.FromMilliseconds(duration.TotalMilliseconds * 1.12),
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    BeginTime = TimeSpan.FromMilliseconds(index * 31),
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                });

            flame.BeginAnimation(
                UIElement.OpacityProperty,
                new DoubleAnimation
                {
                    From = 0.40,
                    To = 0.58,
                    Duration = TimeSpan.FromMilliseconds(duration.TotalMilliseconds * 0.94),
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    BeginTime = TimeSpan.FromMilliseconds(index * 37),
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                });
        }

        return band;
    }

    private static Brush CreateGlowBrush()
    {
        var brush = new RadialGradientBrush
        {
            Center = new Point(0.5, 0.5),
            GradientOrigin = new Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5,
            GradientStops = new GradientStopCollection
            {
                new(Color.FromArgb(88, 255, 116, 24), 0.0),
                new(Color.FromArgb(36, 223, 48, 10), 0.58),
                new(Colors.Transparent, 1.0)
            }
        };

        brush.Freeze();
        return brush;
    }

    private static Brush CreateEmberLineBrush()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0.0, 0.5),
            EndPoint = new Point(1.0, 0.5),
            GradientStops = new GradientStopCollection
            {
                new(Colors.Transparent, 0.0),
                new(Color.FromArgb(170, 238, 72, 14), 0.08),
                new(Color.FromArgb(205, 255, 180, 58), 0.32),
                new(Color.FromArgb(180, 255, 101, 20), 0.58),
                new(Color.FromArgb(195, 255, 166, 48), 0.82),
                new(Colors.Transparent, 1.0)
            }
        };

        brush.Freeze();
        return brush;
    }

    private static Brush CreateFlameBrush()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0.5, 1.0),
            EndPoint = new Point(0.5, 0.0),
            GradientStops = new GradientStopCollection
            {
                new(Color.FromArgb(220, 255, 202, 92), 0.0),
                new(Color.FromArgb(190, 255, 116, 28), 0.48),
                new(Color.FromArgb(92, 214, 48, 12), 0.78),
                new(Colors.Transparent, 1.0)
            }
        };

        brush.Freeze();
        return brush;
    }

    private static Geometry CreateFlameGeometry()
    {
        var geometry = Geometry.Parse(
            "M 8,24 C 4,21 3,17 5.4,12.8 C 6.8,10.4 8.2,8.2 8,4 " +
            "C 11.8,7.5 14,11.2 13.2,15.2 C 12.5,19.1 10.7,22.1 8,24 Z");

        geometry.Freeze();
        return geometry;
    }

    private static void SafeComplete(Action onComplete)
    {
        try
        {
            onComplete?.Invoke();
        }
        catch
        {
            // Removal must never break the quote workflow.
        }
    }
}
