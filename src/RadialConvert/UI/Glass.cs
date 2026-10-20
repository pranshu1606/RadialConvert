using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace RadialConvert.UI;

/// <summary>
/// A snapshot of the screen behind an overlay. Layered windows can't use the system acrylic,
/// so the "liquid glass" is faked from this: blurred, slightly magnified, tinted and rimmed.
/// The overlay is short-lived and the screen behind it doesn't move, so a still frame reads as real glass.
/// </summary>
internal sealed class GlassBackdrop
{
    GlassBackdrop(BitmapSource image, double luminance)
    {
        Image = image;
        Luminance = luminance;
    }

    public BitmapSource Image { get; }

    /// <summary>Average brightness 0..1, used to pick light or dark text.</summary>
    public double Luminance { get; }

    public bool IsDark => Luminance < 0.55;

    /// <summary>Copies a screen rectangle (physical pixels). Call before the overlay is shown.</summary>
    public static GlassBackdrop Capture(int x, int y, int width, int height)
    {
        try
        {
            using var bitmap = new System.Drawing.Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
            using (var g = System.Drawing.Graphics.FromImage(bitmap))
                g.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(width, height));

            IntPtr hBitmap = bitmap.GetHbitmap();
            try
            {
                var source = Imaging.CreateBitmapSourceFromHBitmap(hBitmap, IntPtr.Zero, Int32Rect.Empty,
                                                                  BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return new GlassBackdrop(source, MeasureLuminance(source));
            }
            finally
            {
                Native.DeleteObject(hBitmap);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Screen capture for glass failed", ex);
            return Synthetic(width, height, dark: true);
        }
    }

    /// <summary>A wallpaper-like stand-in, used for previews and if capture fails.</summary>
    public static GlassBackdrop Synthetic(double width, double height, bool dark)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var bg = dark
                ? new LinearGradientBrush(Color.FromRgb(0x10, 0x1E, 0x3C), Color.FromRgb(0x3B, 0x1F, 0x5C), 35)
                : new LinearGradientBrush(Color.FromRgb(0xE6, 0xEE, 0xFB), Color.FromRgb(0xF6, 0xE7, 0xF0), 35);
            dc.DrawRectangle(bg, null, new Rect(0, 0, width, height));
            dc.DrawEllipse(new SolidColorBrush(dark ? Color.FromRgb(0xFF, 0x7A, 0x45) : Color.FromRgb(0x5B, 0x8C, 0xFF)),
                           null, new Point(width * 0.28, height * 0.30), width * 0.20, width * 0.20);
            dc.DrawEllipse(new SolidColorBrush(dark ? Color.FromRgb(0x2D, 0xD4, 0xBF) : Color.FromRgb(0xFF, 0xB8, 0x3D)),
                           null, new Point(width * 0.78, height * 0.72), width * 0.17, width * 0.17);
            var ink = new SolidColorBrush(dark ? Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0xCC, 0x20, 0x20, 0x28));
            var typeface = new Typeface("Segoe UI");
            for (int row = 0; row < 7; row++)
            {
                var text = new FormattedText($"Quarterly report {2020 + row}.docx", System.Globalization.CultureInfo.InvariantCulture,
                                             FlowDirection.LeftToRight, typeface, 13, ink, 1.0);
                dc.DrawText(text, new Point(width * 0.08, height * 0.08 + row * height * 0.13));
            }
        }
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width * 2), (int)Math.Ceiling(height * 2), 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return new GlassBackdrop(bitmap, MeasureLuminance(bitmap));
    }

    static double MeasureLuminance(BitmapSource source)
    {
        var bgra = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int width = bgra.PixelWidth, height = bgra.PixelHeight, stride = width * 4;
        var pixels = new byte[stride * height];
        bgra.CopyPixels(pixels, stride, 0);

        double sum = 0;
        int count = 0;
        for (int y = 0; y < height; y += 4)
            for (int x = 0; x < width; x += 4)
            {
                int i = y * stride + x * 4;
                sum += (0.0722 * pixels[i] + 0.7152 * pixels[i + 1] + 0.2126 * pixels[i + 2]) / 255.0;
                count++;
            }
        return count > 0 ? sum / count : 0.5;
    }
}

/// <summary>Builds the layered "liquid glass" look for any shape.</summary>
internal static class Glass
{
    public static readonly Color Accent = Color.FromRgb(0x5B, 0x8C, 0xFF);
    public static readonly Color Accent2 = Color.FromRgb(0x8B, 0x5C, 0xF6);

    /// <summary>
    /// Glass in the shape of <paramref name="shape"/>, laid out in a <paramref name="width"/>×<paramref name="height"/>
    /// box that matches the window. The backdrop covers the window plus <paramref name="backdropMargin"/> on each side,
    /// so the blur has real pixels to sample at the edges.
    /// </summary>
    public static FrameworkElement Surface(Geometry shape, double width, double height, GlassBackdrop backdrop,
                                           double backdropMargin, double tint = 0, double blur = 16)
    {
        var grid = new Grid { Width = width, Height = height };

        // Soft drop shadow so the glass lifts off the desktop.
        grid.Children.Add(new System.Windows.Shapes.Path
        {
            Data = shape, Fill = Brushes.Black, Opacity = 0.30,
            Effect = new BlurEffect { Radius = 18 }, RenderTransform = new TranslateTransform(0, 5),
        });

        // The blurred, slightly magnified backdrop is what makes it read as a lens.
        var image = new Image
        {
            Source = backdrop.Image, Stretch = Stretch.Fill,
            Width = width + backdropMargin * 2, Height = height + backdropMargin * 2,
            Effect = new BlurEffect { Radius = blur, RenderingBias = RenderingBias.Quality },
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1.05, 1.05),
        };
        Canvas.SetLeft(image, -backdropMargin);
        Canvas.SetTop(image, -backdropMargin);
        grid.Children.Add(new Canvas { Width = width, Height = height, Clip = shape, Children = { image } });

        byte tintAlpha = (byte)Math.Clamp((backdrop.IsDark ? 0x22 : 0x55) + tint * 255, 0, 255);
        grid.Children.Add(Fill(shape, Frozen(new SolidColorBrush(Color.FromArgb(tintAlpha, 0xFF, 0xFF, 0xFF)))));

        // Brighter toward the edge, like light bending through a thick lens.
        var edge = new RadialGradientBrush
        {
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.70),
                new GradientStop(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF), 0.88),
                new GradientStop(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF), 1.00),
            },
        };
        grid.Children.Add(Fill(shape, Frozen(edge)));

        // Top sheen.
        var sheen = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0), EndPoint = new Point(0, 1),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0x48, 0xFF, 0xFF, 0xFF), 0.0),
                new GradientStop(Color.FromArgb(0x0C, 0xFF, 0xFF, 0xFF), 0.38),
                new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.55),
                new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.85),
                new GradientStop(Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF), 1.0),
            },
        };
        grid.Children.Add(Fill(shape, Frozen(sheen)));

        // Hairline for definition on light backgrounds, then the specular rim.
        grid.Children.Add(new System.Windows.Shapes.Path
        {
            Data = shape, Stroke = Frozen(new SolidColorBrush(Color.FromArgb(0x1E, 0, 0, 0))), StrokeThickness = 2.2,
        });
        grid.Children.Add(new System.Windows.Shapes.Path { Data = shape, Stroke = Rim(), StrokeThickness = 1.3 });
        return grid;
    }

    /// <summary>Specular rim: bright top-left, faint middle, softer bounce light bottom-right.</summary>
    public static Brush Rim() => Frozen(new LinearGradientBrush
    {
        StartPoint = new Point(0.1, 0), EndPoint = new Point(0.9, 1),
        GradientStops =
        {
            new GradientStop(Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF), 0.0),
            new GradientStop(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF), 0.30),
            new GradientStop(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF), 0.60),
            new GradientStop(Color.FromArgb(0xA0, 0xFF, 0xFF, 0xFF), 1.0),
        },
    });

    public static Brush AccentFill(byte alpha) => Frozen(new LinearGradientBrush(
        Color.FromArgb(alpha, Accent.R, Accent.G, Accent.B), Color.FromArgb(alpha, Accent2.R, Accent2.G, Accent2.B), 35));

    public static Brush TextBrush(bool darkBackdrop) =>
        Frozen(new SolidColorBrush(darkBackdrop ? Colors.White : Color.FromRgb(0x16, 0x17, 0x1C)));

    public static Brush MutedBrush(bool darkBackdrop) =>
        Frozen(new SolidColorBrush(darkBackdrop ? Color.FromArgb(0xC0, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0xA0, 0x16, 0x17, 0x1C)));

    /// <summary>A soft halo that keeps text readable on busy glass.</summary>
    public static Effect TextHalo(bool darkBackdrop) => new DropShadowEffect
    {
        Color = darkBackdrop ? Colors.Black : Colors.White, BlurRadius = 8, ShadowDepth = 0, Opacity = 0.45,
    };

    static System.Windows.Shapes.Path Fill(Geometry shape, Brush brush) => new() { Data = shape, Fill = brush };

    public static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
