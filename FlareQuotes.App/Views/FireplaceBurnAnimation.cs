using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FlareQuotes.App.Views;

/// <summary>
/// Plays a "linear-fireplace burner" burn-to-ash animation over a summary card, then
/// invokes <paramref name="onComplete"/> to perform the real removal. The animation is
/// entirely cosmetic and fully guarded: if anything goes wrong the card is removed
/// immediately so the quote logic is never blocked.
///
/// The flames are a per-pixel fire field (a heat grid mapped through a fire palette and
/// pushed into a WriteableBitmap each frame) — the same technique used in the design
/// preview, so the look matches. Burner "ports" are seeded at intervals so the flames
/// rise as spaced jets over a low ember glow, contained to the card's own width.
/// </summary>
internal static class FireplaceBurnAnimation
{
    private static readonly byte[][] Palette = BuildPalette();

    public static void Burn(FrameworkElement card, Panel overlayHost, Action onComplete)
    {
        var completed = false;
        void Finish()
        {
            if (completed)
                return;
            completed = true;
            try
            {
                onComplete?.Invoke();
            }
            catch { /* removal must never throw */ }
        }

        try
        {
            if (card is null || overlayHost is null || card.ActualWidth < 8 || card.ActualHeight < 8)
            {
                Finish();
                return;
            }

            double cardW = card.ActualWidth, cardH = card.ActualHeight;
            const double padTop = 60, padBot = 8, cell = 2.6;
            double cw = cardW, ch = cardH + padTop + padBot;

            int W = Math.Max(48, (int)Math.Round(cw / cell));
            int H = Math.Max(40, (int)Math.Round(ch / cell));

            Point topLeft = card.TransformToVisual(overlayHost).Transform(new Point(0, 0));

            var wb = new WriteableBitmap(W, H, 96, 96, PixelFormats.Pbgra32, null);
            var img = new Image
            {
                Source = wb,
                Width = cw,
                Height = ch,
                Stretch = Stretch.Fill,
                IsHitTestVisible = false,
                Effect = new BlurEffect { Radius = 1.4, KernelType = KernelType.Gaussian }
            };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.Linear);
            Canvas.SetLeft(img, topLeft.X);
            Canvas.SetTop(img, topLeft.Y - padTop);
            overlayHost.Children.Add(img);

            var fire = new byte[W * H];
            var pixels = new byte[W * H * 4];
            int stride = W * 4;

            const int MARGIN = 3, S = 31;
            int usable = Math.Max(1, W - MARGIN * 2);
            int port = Math.Max(7, (int)Math.Round(usable / 8.0));
            int portWidth = Math.Max(2, (int)Math.Round(port * 0.4));
            int cardTopCell = (int)Math.Round(padTop / ch * H);
            int cardBottomCell = (int)Math.Round((padTop + cardH) / ch * H);

            const double totalMs = 1550, fadeMs = 560, collapseAt = 1500;
            var rnd = new Random();
            var sw = Stopwatch.StartNew();
            bool collapsed = false;

            var timer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(1000.0 / 45)
            };

            timer.Tick += (_, _) =>
            {
                try
                {
                    double el = sw.Elapsed.TotalMilliseconds;
                    double p = Math.Min(1.0, el / totalMs);
                    int front = p < 1.0
                        ? (int)Math.Round(cardBottomCell - p * (cardBottomCell - (cardTopCell - 1)))
                        : -1;

                    Step(fire, W, H, front, S, MARGIN, port, portWidth, rnd);
                    RenderFire(fire, pixels, W * H);
                    wb.WritePixels(new Int32Rect(0, 0, W, H), pixels, stride, 0);

                    if (!collapsed && el >= collapseAt)
                    {
                        collapsed = true;
                        BeginCollapse(card, Finish);
                    }

                    if (el >= totalMs + fadeMs)
                    {
                        timer.Stop();
                        try
                        {
                            overlayHost.Children.Remove(img);
                        }
                        catch { }
                        Finish();
                    }
                }
                catch
                {
                    timer.Stop();
                    try
                    {
                        overlayHost.Children.Remove(img);
                    }
                    catch { }
                    Finish();
                }
            };

            timer.Start();
        }
        catch
        {
            Finish();
        }
    }

    private static void BeginCollapse(FrameworkElement card, Action onDone)
    {
        try
        {
            double h = card.ActualHeight;
            card.Height = h;
            var shrink = new DoubleAnimation(h, 0, TimeSpan.FromMilliseconds(330))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            shrink.Completed += (_, _) => onDone();
            card.BeginAnimation(FrameworkElement.HeightProperty, shrink);
            card.BeginAnimation(UIElement.OpacityProperty,
                                new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300)));
        }
        catch
        {
            onDone();
        }
    }

    private static void Step(byte[] fire, int W, int H, int front, int s, int margin, int port, int portWidth, Random rnd)
    {
        if (front >= 0)
        {
            for (int y = Math.Min(H - 1, front + 1); y < H; y++)
            {
                int yy = y * W;
                for (int x = 0; x < W; x++)
                    fire[yy + x] = 0;
            }
            for (int band = 0; band < 3; band++)
            {
                int yr = front + band;
                if (yr < 0 || yr >= H)
                    continue;
                int yy = yr * W;
                for (int x = 0; x < W; x++)
                {
                    if (x < margin || x >= W - margin)
                    {
                        fire[yy + x] = 0;
                        continue;
                    }
                    bool inPort = ((x - margin) % port) < portWidth;
                    int baseValue = inPort ? s : 6;
                    int v = baseValue - 3 + rnd.Next(inPort ? 9 : 4);
                    fire[yy + x] = (byte)(v < 0 ? 0 : (v > 36 ? 36 : v));
                }
            }
        }

        for (int y = 1; y < H; y++)
        {
            int yy = y * W;
            for (int x = 0; x < W; x++)
            {
                int from = yy + x;
                int vdecay = rnd.Next(3);
                int hoff = rnd.Next(3) - 1;
                int tx = x + hoff;
                if (tx < margin)
                    tx = margin;
                else if (tx >= W - margin)
                    tx = W - margin - 1;
                int to = (y - 1) * W + tx;
                int nv = fire[from] - vdecay;
                fire[to] = (byte)(nv < 0 ? 0 : nv);
            }
        }
    }

    private static void RenderFire(byte[] fire, byte[] pixels, int count)
    {
        for (int i = 0; i < count; i++)
        {
            byte[] c = Palette[fire[i]];
            int alpha = c[3];
            int j = i << 2;
            // Pbgra32 = premultiplied B,G,R,A
            pixels[j] = (byte)(c[2] * alpha / 255);
            pixels[j + 1] = (byte)(c[1] * alpha / 255);
            pixels[j + 2] = (byte)(c[0] * alpha / 255);
            pixels[j + 3] = (byte)alpha;
        }
    }

    private static byte[][] BuildPalette()
    {
        int[][] stops =
        {
            new[] { 0, 6, 6, 6, 0 },       new[] { 4, 48, 14, 6, 55 },     new[] { 8, 110, 30, 8, 140 },
            new[] { 12, 172, 54, 10, 200 }, new[] { 16, 224, 94, 18, 232 }, new[] { 20, 250, 132, 28, 246 },
            new[] { 24, 255, 164, 46, 255 }, new[] { 28, 255, 192, 78, 255 }, new[] { 31, 255, 214, 116, 255 },
            new[] { 34, 255, 234, 172, 255 }, new[] { 36, 255, 247, 218, 255 }
        };

        var pal = new byte[37][];
        for (int i = 0; i <= 36; i++)
        {
            int[] a = stops[0], b = stops[^1];
            for (int sIndex = 0; sIndex < stops.Length - 1; sIndex++)
            {
                if (i >= stops[sIndex][0] && i <= stops[sIndex + 1][0])
                {
                    a = stops[sIndex];
                    b = stops[sIndex + 1];
                    break;
                }
            }

            double t = (double)(i - a[0]) / Math.Max(1, b[0] - a[0]);
            pal[i] = new[]
            {
                (byte)Math.Round(a[1] + (b[1] - a[1]) * t),
                (byte)Math.Round(a[2] + (b[2] - a[2]) * t),
                (byte)Math.Round(a[3] + (b[3] - a[3]) * t),
                (byte)Math.Round(a[4] + (b[4] - a[4]) * t)
            };
        }

        return pal;
    }
}
