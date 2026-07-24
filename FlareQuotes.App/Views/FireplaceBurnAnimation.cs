using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace FlareQuotes.App.Views;

/// <summary>
/// Burns a fireplace summary card from the bottom upward, then performs the real
/// collection removal. The card is captured once and rendered in an overlay while
/// an irregular burn front consumes it into flame, embers, and drifting ash.
/// </summary>
internal static class FireplaceBurnAnimation
{
    private const double BurnDurationMs = 1450;
    private const double CollapseStartMs = 1260;
    private const double TotalDurationMs = 1660;
    private const double CollapseDurationMs = 230;
    private const double TopPadding = 58;
    private const double BottomPadding = 18;

    private static readonly HashSet<FrameworkElement> ActiveCards = [];

    public static void Burn(FrameworkElement card, Panel overlayHost, Action onComplete)
    {
        if (card is null || overlayHost is not Canvas overlayCanvas)
        {
            SafeComplete(onComplete);
            return;
        }

        if (ActiveCards.Contains(card))
            return;

        if (!SystemParameters.ClientAreaAnimation ||
            card.ActualWidth < 8 ||
            card.ActualHeight < 8)
        {
            SafeComplete(onComplete);
            return;
        }

        BurnCardVisual? burnVisual = null;
        var originalOpacity = card.Opacity;
        var originalHeight = card.Height;
        var originalHitTestVisible = card.IsHitTestVisible;
        var collapseDone = false;
        var visualDone = false;
        var finished = false;

        void RestoreCardState()
        {
            try
            {
                card.BeginAnimation(UIElement.OpacityProperty, null);
                card.BeginAnimation(FrameworkElement.HeightProperty, null);
                card.Opacity = originalOpacity;
                card.IsHitTestVisible = originalHitTestVisible;

                if (double.IsNaN(originalHeight))
                    card.ClearValue(FrameworkElement.HeightProperty);
                else
                    card.Height = originalHeight;
            }
            catch
            {
                // The card may already be detached after successful removal.
            }
        }

        void Finish()
        {
            if (finished || !collapseDone || !visualDone)
                return;

            finished = true;
            ActiveCards.Remove(card);

            try
            {
                burnVisual?.Stop();
                if (burnVisual is not null)
                    overlayCanvas.Children.Remove(burnVisual);
            }
            catch
            {
                // Cosmetic cleanup must never block removal.
            }

            SafeComplete(onComplete);
            RestoreCardState();
        }

        void BeginCollapse()
        {
            if (collapseDone)
                return;

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

                collapse.Completed += (_, _) =>
                {
                    collapseDone = true;
                    Finish();
                };

                card.BeginAnimation(
                    FrameworkElement.HeightProperty,
                    collapse,
                    HandoffBehavior.SnapshotAndReplace);
            }
            catch
            {
                collapseDone = true;
                Finish();
            }
        }

        try
        {
            ActiveCards.Add(card);

            card.UpdateLayout();
            var snapshot = CaptureCard(card);
            var topLeft = card.TransformToVisual(overlayCanvas).Transform(new Point(0, 0));
            var cardWidth = card.ActualWidth;
            var cardHeight = card.ActualHeight;

            card.IsHitTestVisible = false;
            card.Opacity = 0;

            burnVisual = new BurnCardVisual(
                snapshot,
                cardWidth,
                cardHeight,
                onCollapseStart: BeginCollapse,
                onVisualComplete: () =>
                {
                    visualDone = true;
                    Finish();
                })
            {
                Width = cardWidth,
                Height = cardHeight + TopPadding + BottomPadding,
                IsHitTestVisible = false
            };

            Canvas.SetLeft(burnVisual, topLeft.X);
            Canvas.SetTop(burnVisual, topLeft.Y - TopPadding);
            Panel.SetZIndex(burnVisual, 10000);
            overlayCanvas.Children.Add(burnVisual);
            burnVisual.Start();
        }
        catch
        {
            ActiveCards.Remove(card);

            try
            {
                burnVisual?.Stop();
                if (burnVisual is not null)
                    overlayCanvas.Children.Remove(burnVisual);
            }
            catch
            {
            }

            RestoreCardState();
            SafeComplete(onComplete);
        }
    }

    private static RenderTargetBitmap CaptureCard(FrameworkElement card)
    {
        var dpi = VisualTreeHelper.GetDpi(card);
        var pixelWidth = Math.Max(1, (int)Math.Ceiling(card.ActualWidth * dpi.DpiScaleX));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(card.ActualHeight * dpi.DpiScaleY));

        var bitmap = new RenderTargetBitmap(
            pixelWidth,
            pixelHeight,
            dpi.PixelsPerInchX,
            dpi.PixelsPerInchY,
            PixelFormats.Pbgra32);

        bitmap.Render(card);
        bitmap.Freeze();
        return bitmap;
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

    private sealed class BurnCardVisual : FrameworkElement
    {
        private const int FrontPointCount = 31;

        private static readonly Brush OuterFlameBrush = CreateOuterFlameBrush();
        private static readonly Brush InnerFlameBrush = CreateInnerFlameBrush();
        private static readonly Brush AshBrush = CreateSolidBrush(Color.FromRgb(160, 166, 171));
        private static readonly Brush DarkAshBrush = CreateSolidBrush(Color.FromRgb(88, 94, 99));
        private static readonly Brush SparkBrush = CreateSolidBrush(Color.FromRgb(255, 174, 58));
        private static readonly Brush HotSparkBrush = CreateSolidBrush(Color.FromRgb(255, 226, 132));
        private static readonly Brush CharBrush = CreateCharBrush();
        private static readonly Brush FrontGlowBrush = CreateFrontGlowBrush();
        private static readonly Pen EmberHaloPen = CreatePen(Color.FromArgb(92, 225, 55, 10), 7.0);
        private static readonly Pen EmberPen = CreatePen(Color.FromArgb(230, 255, 132, 28), 2.1);
        private static readonly Pen HotEdgePen = CreatePen(Color.FromArgb(220, 255, 218, 114), 0.9);

        private readonly ImageSource _snapshot;
        private readonly double _cardWidth;
        private readonly double _cardHeight;
        private readonly Action _onCollapseStart;
        private readonly Action _onVisualComplete;
        private readonly Stopwatch _stopwatch = new();
        private readonly double[] _frontNoise = new double[FrontPointCount];
        private readonly double[] _frontPhase = new double[FrontPointCount];
        private readonly List<FlameSeed> _flames = [];
        private readonly List<ParticleSeed> _particles = [];

        private bool _running;
        private bool _collapseStarted;
        private bool _completed;

        public BurnCardVisual(
            ImageSource snapshot,
            double cardWidth,
            double cardHeight,
            Action onCollapseStart,
            Action onVisualComplete)
        {
            _snapshot = snapshot;
            _cardWidth = cardWidth;
            _cardHeight = cardHeight;
            _onCollapseStart = onCollapseStart;
            _onVisualComplete = onVisualComplete;

            SnapsToDevicePixels = false;
            UseLayoutRounding = false;

            BuildSeeds();
        }

        public void Start()
        {
            if (_running)
                return;

            _running = true;
            _stopwatch.Restart();
            CompositionTarget.Rendering += OnRendering;
            InvalidateVisual();
        }

        public void Stop()
        {
            if (!_running)
                return;

            _running = false;
            CompositionTarget.Rendering -= OnRendering;
            _stopwatch.Stop();
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);

            var elapsedMs = Math.Max(0, _stopwatch.Elapsed.TotalMilliseconds);
            var seconds = elapsedMs / 1000.0;
            var burnProgress = Clamp01(elapsedMs / BurnDurationMs);
            var easedProgress = SmoothStep(burnProgress);
            var flameFade = Clamp01(elapsedMs / 130.0) *
                            Clamp01((TotalDurationMs - elapsedMs) / 280.0);

            var frontBaseY = TopPadding + (_cardHeight * (1.0 - easedProgress));
            var front = BuildFront(seconds, frontBaseY, burnProgress);
            var remainingClip = BuildRemainingGeometry(front);

            if (burnProgress < 1.0)
            {
                drawingContext.PushClip(remainingClip);
                drawingContext.DrawImage(
                    _snapshot,
                    new Rect(0, TopPadding, _cardWidth, _cardHeight));
                drawingContext.Pop();

                DrawCharredEdge(drawingContext, front, flameFade);
                DrawFlames(drawingContext, front, seconds, burnProgress, flameFade);
            }

            DrawParticles(drawingContext, elapsedMs, seconds);
        }

        private void OnRendering(object? sender, EventArgs e)
        {
            var elapsedMs = _stopwatch.Elapsed.TotalMilliseconds;

            if (!_collapseStarted && elapsedMs >= CollapseStartMs)
            {
                _collapseStarted = true;
                try
                {
                    _onCollapseStart();
                }
                catch
                {
                    // Outer guard will still complete removal.
                }
            }

            if (!_completed && elapsedMs >= TotalDurationMs)
            {
                _completed = true;
                Stop();

                try
                {
                    _onVisualComplete();
                }
                catch
                {
                    // Outer guard will still complete removal.
                }

                return;
            }

            InvalidateVisual();
        }

        private List<Point> BuildFront(double seconds, double baseY, double progress)
        {
            var points = new List<Point>(FrontPointCount);
            var endDamping = 1.0 - Math.Pow(Clamp01((progress - 0.88) / 0.12), 2.0);

            for (var index = 0; index < FrontPointCount; index++)
            {
                var ratio = index / (double)(FrontPointCount - 1);
                var x = ratio * _cardWidth;
                var flicker = Math.Sin((seconds * 7.2) + _frontPhase[index]) * 2.8;
                var secondary = Math.Sin((seconds * 12.6) + (_frontPhase[index] * 0.47)) * 1.35;
                var y = baseY + ((_frontNoise[index] + flicker + secondary) * endDamping);

                y = Math.Max(TopPadding - 2.0, Math.Min(TopPadding + _cardHeight + 7.0, y));
                points.Add(new Point(x, y));
            }

            return points;
        }

        private Geometry BuildRemainingGeometry(IReadOnlyList<Point> front)
        {
            var geometry = new StreamGeometry();

            using (var context = geometry.Open())
            {
                context.BeginFigure(new Point(0, TopPadding), isFilled: true, isClosed: true);
                context.LineTo(new Point(_cardWidth, TopPadding), isStroked: true, isSmoothJoin: false);

                for (var index = front.Count - 1; index >= 0; index--)
                    context.LineTo(front[index], isStroked: true, isSmoothJoin: true);
            }

            geometry.Freeze();
            return geometry;
        }

        private void DrawCharredEdge(DrawingContext drawingContext, IReadOnlyList<Point> front, double opacity)
        {
            var charBand = new StreamGeometry();

            using (var context = charBand.Open())
            {
                context.BeginFigure(front[0], isFilled: true, isClosed: true);

                for (var index = 1; index < front.Count; index++)
                    context.LineTo(front[index], isStroked: true, isSmoothJoin: true);

                for (var index = front.Count - 1; index >= 0; index--)
                {
                    var depth = 8.0 + (Math.Abs(_frontNoise[index]) * 0.45);
                    context.LineTo(
                        new Point(front[index].X, front[index].Y - depth),
                        isStroked: true,
                        isSmoothJoin: true);
                }
            }

            charBand.Freeze();

            drawingContext.PushOpacity(0.92 * opacity);
            drawingContext.DrawGeometry(CharBrush, null, charBand);
            drawingContext.Pop();

            var line = BuildFrontLine(front);

            var averageY = 0.0;
            foreach (var point in front)
                averageY += point.Y;
            averageY /= Math.Max(1, front.Count);

            drawingContext.PushOpacity(0.44 * opacity);
            drawingContext.DrawRectangle(
                FrontGlowBrush,
                null,
                new Rect(4, averageY - 15, Math.Max(1, _cardWidth - 8), 30));
            drawingContext.Pop();

            drawingContext.PushOpacity(opacity);
            drawingContext.DrawGeometry(null, EmberHaloPen, line);
            drawingContext.DrawGeometry(null, EmberPen, line);
            drawingContext.DrawGeometry(null, HotEdgePen, line);
            drawingContext.Pop();
        }

        private static Geometry BuildFrontLine(IReadOnlyList<Point> front)
        {
            var geometry = new StreamGeometry();

            using (var context = geometry.Open())
            {
                context.BeginFigure(front[0], isFilled: false, isClosed: false);

                for (var index = 1; index < front.Count; index++)
                    context.LineTo(front[index], isStroked: true, isSmoothJoin: true);
            }

            geometry.Freeze();
            return geometry;
        }

        private void DrawFlames(
            DrawingContext drawingContext,
            IReadOnlyList<Point> front,
            double seconds,
            double progress,
            double flameFade)
        {
            if (flameFade <= 0.001)
                return;

            var progressPulse = 0.88 + (0.12 * Math.Sin(progress * Math.PI));

            foreach (var seed in _flames)
            {
                var x = seed.XRatio * _cardWidth;
                var baseY = InterpolateFront(front, x);
                var pulse = 0.82 + (0.18 * Math.Sin((seconds * seed.Speed) + seed.Phase));
                var quickPulse = 0.92 + (0.08 * Math.Sin((seconds * seed.Speed * 2.1) + seed.Phase));
                var height = seed.Height * pulse * progressPulse;
                var width = seed.Width * quickPulse;
                var tipShift = seed.Lean +
                               (Math.Sin((seconds * seed.Speed * 0.73) + seed.Phase) * 5.2);

                var outer = BuildFlameGeometry(x, baseY + 1.5, width, height, tipShift);
                var inner = BuildFlameGeometry(
                    x + (tipShift * 0.16),
                    baseY + 1.0,
                    width * 0.47,
                    height * 0.63,
                    tipShift * 0.42);

                drawingContext.PushOpacity(flameFade * seed.Opacity);
                drawingContext.DrawGeometry(OuterFlameBrush, null, outer);
                drawingContext.Pop();

                drawingContext.PushOpacity(flameFade * Math.Min(1.0, seed.Opacity + 0.12));
                drawingContext.DrawGeometry(InnerFlameBrush, null, inner);
                drawingContext.Pop();
            }
        }

        private static Geometry BuildFlameGeometry(
            double centerX,
            double baseY,
            double width,
            double height,
            double tipShift)
        {
            var halfWidth = width / 2.0;
            var tip = new Point(centerX + tipShift, baseY - height);
            var geometry = new StreamGeometry();

            using (var context = geometry.Open())
            {
                context.BeginFigure(
                    new Point(centerX - halfWidth, baseY),
                    isFilled: true,
                    isClosed: true);

                context.BezierTo(
                    new Point(centerX - (halfWidth * 1.08), baseY - (height * 0.28)),
                    new Point(centerX - (halfWidth * 0.38), baseY - (height * 0.72)),
                    tip,
                    isStroked: true,
                    isSmoothJoin: true);

                context.BezierTo(
                    new Point(centerX + (halfWidth * 0.42), baseY - (height * 0.70)),
                    new Point(centerX + (halfWidth * 1.06), baseY - (height * 0.25)),
                    new Point(centerX + halfWidth, baseY),
                    isStroked: true,
                    isSmoothJoin: true);
            }

            geometry.Freeze();
            return geometry;
        }

        private void DrawParticles(DrawingContext drawingContext, double elapsedMs, double seconds)
        {
            foreach (var particle in _particles)
            {
                var ageMs = elapsedMs - particle.BirthMs;
                if (ageMs < 0 || ageMs > particle.LifetimeMs)
                    continue;

                var age = ageMs / particle.LifetimeMs;
                var fade = Math.Sin(age * Math.PI);
                if (fade <= 0.001)
                    continue;

                var originProgress = SmoothStep(Clamp01(particle.BirthMs / BurnDurationMs));
                var originY = TopPadding + (_cardHeight * (1.0 - originProgress));
                var turbulence = Math.Sin((seconds * particle.WobbleSpeed) + particle.Phase);
                var x = (particle.XRatio * _cardWidth) +
                        (particle.Drift * age) +
                        (turbulence * particle.Wobble);
                var y = originY -
                        (particle.Rise * age) +
                        (particle.Fall * age * age);

                if (particle.IsSpark)
                {
                    drawingContext.PushOpacity(fade * particle.Opacity);
                    drawingContext.DrawEllipse(
                        particle.IsHot ? HotSparkBrush : SparkBrush,
                        null,
                        new Point(x, y),
                        particle.Size * 0.46,
                        particle.Size * 1.25);
                    drawingContext.Pop();
                    continue;
                }

                var size = particle.Size * (0.72 + (age * 0.46));
                var angle = particle.Rotation + (particle.Spin * age);

                drawingContext.PushOpacity(fade * particle.Opacity);
                drawingContext.PushTransform(new RotateTransform(angle, x, y));
                drawingContext.DrawRoundedRectangle(
                    particle.IsDark ? DarkAshBrush : AshBrush,
                    null,
                    new Rect(x - (size / 2.0), y - (size * 0.32), size, size * 0.64),
                    size * 0.18,
                    size * 0.18);
                drawingContext.Pop();
                drawingContext.Pop();
            }
        }

        private void BuildSeeds()
        {
            var random = new Random(
                unchecked(((int)Math.Round(_cardWidth) * 397) ^
                          (int)Math.Round(_cardHeight) ^
                          0x5F3759DF));

            for (var index = 0; index < FrontPointCount; index++)
            {
                var coarse = (random.NextDouble() * 11.0) - 5.5;
                if (index % 7 == 0)
                    coarse -= 4.0 + (random.NextDouble() * 4.0);

                _frontNoise[index] = coarse;
                _frontPhase[index] = random.NextDouble() * Math.PI * 2.0;
            }

            var flameCount = Math.Clamp((int)Math.Round(_cardWidth / 29.0), 7, 13);

            for (var index = 0; index < flameCount; index++)
            {
                var slot = (index + 1.0) / (flameCount + 1.0);
                var jitter = ((random.NextDouble() - 0.5) * 0.055);

                _flames.Add(new FlameSeed(
                    XRatio: Clamp01(slot + jitter),
                    Width: 15.0 + (random.NextDouble() * 10.0),
                    Height: 30.0 + (random.NextDouble() * 20.0),
                    Phase: random.NextDouble() * Math.PI * 2.0,
                    Speed: 7.2 + (random.NextDouble() * 3.8),
                    Lean: (random.NextDouble() - 0.5) * 7.0,
                    Opacity: 0.78 + (random.NextDouble() * 0.18)));
            }

            const int particleCount = 86;

            for (var index = 0; index < particleCount; index++)
            {
                var isSpark = index < 24;
                var birth = 90.0 + (random.NextDouble() * (BurnDurationMs - 100.0));
                var lifetime = isSpark
                    ? 420.0 + (random.NextDouble() * 500.0)
                    : 620.0 + (random.NextDouble() * 760.0);

                _particles.Add(new ParticleSeed(
                    XRatio: 0.02 + (random.NextDouble() * 0.96),
                    BirthMs: birth,
                    LifetimeMs: lifetime,
                    Drift: (random.NextDouble() - 0.5) * (isSpark ? 34.0 : 52.0),
                    Rise: isSpark
                        ? 48.0 + (random.NextDouble() * 70.0)
                        : 28.0 + (random.NextDouble() * 62.0),
                    Fall: isSpark
                        ? 8.0 + (random.NextDouble() * 22.0)
                        : 18.0 + (random.NextDouble() * 48.0),
                    Size: isSpark
                        ? 1.2 + (random.NextDouble() * 1.8)
                        : 2.0 + (random.NextDouble() * 4.0),
                    Wobble: 2.0 + (random.NextDouble() * 8.0),
                    WobbleSpeed: 2.0 + (random.NextDouble() * 5.0),
                    Phase: random.NextDouble() * Math.PI * 2.0,
                    Rotation: random.NextDouble() * 180.0,
                    Spin: (random.NextDouble() - 0.5) * 310.0,
                    Opacity: isSpark
                        ? 0.65 + (random.NextDouble() * 0.30)
                        : 0.38 + (random.NextDouble() * 0.42),
                    IsSpark: isSpark,
                    IsHot: isSpark && random.NextDouble() > 0.48,
                    IsDark: !isSpark && random.NextDouble() > 0.57));
            }
        }

        private static double InterpolateFront(IReadOnlyList<Point> points, double x)
        {
            if (points.Count == 0)
                return TopPadding;

            if (x <= points[0].X)
                return points[0].Y;

            if (x >= points[^1].X)
                return points[^1].Y;

            for (var index = 1; index < points.Count; index++)
            {
                if (x > points[index].X)
                    continue;

                var left = points[index - 1];
                var right = points[index];
                var range = Math.Max(0.001, right.X - left.X);
                var amount = Clamp01((x - left.X) / range);
                return left.Y + ((right.Y - left.Y) * amount);
            }

            return points[^1].Y;
        }

        private static Brush CreateOuterFlameBrush()
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(0.5, 1.0),
                EndPoint = new Point(0.5, 0.0),
                GradientStops = new GradientStopCollection
                {
                    new(Color.FromArgb(250, 255, 218, 112), 0.00),
                    new(Color.FromArgb(248, 255, 139, 35), 0.28),
                    new(Color.FromArgb(225, 239, 64, 13), 0.62),
                    new(Color.FromArgb(105, 132, 22, 8), 0.86),
                    new(Colors.Transparent, 1.00)
                }
            };

            brush.Freeze();
            return brush;
        }

        private static Brush CreateInnerFlameBrush()
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(0.5, 1.0),
                EndPoint = new Point(0.5, 0.0),
                GradientStops = new GradientStopCollection
                {
                    new(Color.FromArgb(255, 255, 250, 210), 0.00),
                    new(Color.FromArgb(250, 255, 224, 112), 0.36),
                    new(Color.FromArgb(210, 255, 145, 36), 0.75),
                    new(Colors.Transparent, 1.00)
                }
            };

            brush.Freeze();
            return brush;
        }

        private static Brush CreateCharBrush()
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(0.5, 1.0),
                EndPoint = new Point(0.5, 0.0),
                GradientStops = new GradientStopCollection
                {
                    new(Color.FromArgb(230, 28, 17, 14), 0.00),
                    new(Color.FromArgb(205, 55, 27, 17), 0.32),
                    new(Color.FromArgb(150, 20, 18, 18), 0.72),
                    new(Colors.Transparent, 1.00)
                }
            };

            brush.Freeze();
            return brush;
        }

        private static Brush CreateFrontGlowBrush()
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(0.0, 0.5),
                EndPoint = new Point(1.0, 0.5),
                GradientStops = new GradientStopCollection
                {
                    new(Colors.Transparent, 0.00),
                    new(Color.FromArgb(68, 210, 44, 9), 0.05),
                    new(Color.FromArgb(100, 255, 109, 18), 0.28),
                    new(Color.FromArgb(82, 255, 175, 46), 0.52),
                    new(Color.FromArgb(98, 245, 76, 12), 0.76),
                    new(Color.FromArgb(60, 190, 34, 8), 0.95),
                    new(Colors.Transparent, 1.00)
                }
            };

            brush.Freeze();
            return brush;
        }

        private static Brush CreateSolidBrush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        private static Pen CreatePen(Color color, double thickness)
        {
            var pen = new Pen(CreateSolidBrush(color), thickness)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };

            pen.Freeze();
            return pen;
        }

        private static double SmoothStep(double value)
        {
            value = Clamp01(value);
            return value * value * (3.0 - (2.0 * value));
        }

        private static double Clamp01(double value) => Math.Max(0.0, Math.Min(1.0, value));

        private readonly record struct FlameSeed(
            double XRatio,
            double Width,
            double Height,
            double Phase,
            double Speed,
            double Lean,
            double Opacity);

        private readonly record struct ParticleSeed(
            double XRatio,
            double BirthMs,
            double LifetimeMs,
            double Drift,
            double Rise,
            double Fall,
            double Size,
            double Wobble,
            double WobbleSpeed,
            double Phase,
            double Rotation,
            double Spin,
            double Opacity,
            bool IsSpark,
            bool IsHot,
            bool IsDark);
    }
}
