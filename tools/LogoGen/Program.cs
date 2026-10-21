using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// Draws the Radial Convert logo and writes the app's icon assets:
//   logo.ico (16–256 px), logo-128.png (in-app), logo-512.png (README / store),
//   and, if a second folder is given, the setup wizard images (wizard-*.bmp at 100% and 200%).
// Usage: dotnet run --project tools/LogoGen -- <assets folder> [installer folder]

internal static class Program
{
    static readonly Color Blue = Color.FromRgb(0x3E, 0x7B, 0xFF);
    static readonly Color Violet = Color.FromRgb(0x8B, 0x5C, 0xF6);
    static readonly Color Pink = Color.FromRgb(0xD9, 0x4F, 0xE8);

    [STAThread]
    static int Main(string[] args)
    {
        string outDir = Path.GetFullPath(args.Length > 0 ? args[0] : "Assets");
        Directory.CreateDirectory(outDir);

        if (args.Length > 1)
        {
            string installerDir = Path.GetFullPath(args[1]);
            Directory.CreateDirectory(installerDir);
            foreach (int scale in new[] { 1, 2 })
            {
                string suffix = scale == 1 ? "" : "@2x";
                SaveBmp(WizardBanner(164 * scale, 314 * scale), Path.Combine(installerDir, $"wizard-large{suffix}.bmp"));
                SaveBmp(OnWhite(Render(55 * scale), 55 * scale), Path.Combine(installerDir, $"wizard-small{suffix}.bmp"));
            }
            Console.WriteLine($"Wrote wizard images to {installerDir}");
        }

        var icoSizes = new[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
        var frames = new List<(int Size, BitmapSource Image)>();
        foreach (int size in icoSizes)
            frames.Add((size, Render(size)));

        WriteIco(Path.Combine(outDir, "logo.ico"), frames);
        SavePng(Render(128), Path.Combine(outDir, "logo-128.png"));
        SavePng(Render(512), Path.Combine(outDir, "logo-512.png"));
        Console.WriteLine($"Wrote logo assets to {outDir}");
        return 0;
    }

    /// <summary>
    /// A gradient squircle holding a glass ring split into four arcs — the radial menu — with the top
    /// arc lit as the "selected" format and a dot at the centre where the cursor starts.
    /// </summary>
    static BitmapSource Render(int size)
    {
        double s = size;
        bool tiny = size <= 24;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            double inset = tiny ? 0 : s * 0.03;
            var tile = new Rect(inset, inset, s - inset * 2, s - inset * 2);
            double corner = tile.Width * 0.24;
            var tileGeometry = new RectangleGeometry(tile, corner, corner);

            var background = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0), EndPoint = new Point(1, 1),
                GradientStops = { new GradientStop(Blue, 0), new GradientStop(Violet, 0.6), new GradientStop(Pink, 1) },
            };
            dc.DrawGeometry(background, null, tileGeometry);

            // Glossy top, fading out by the middle.
            if (!tiny)
            {
                var gloss = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0), EndPoint = new Point(0, 1),
                    GradientStops =
                    {
                        new GradientStop(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF), 0),
                        new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.55),
                    },
                };
                dc.DrawGeometry(gloss, null, tileGeometry);
            }

            var center = new Point(s / 2, s / 2);
            double ringRadius = s * (tiny ? 0.29 : 0.285);
            double thickness = s * (tiny ? 0.15 : 0.115);
            const double gap = 40; // degrees between arcs, wide enough for the round caps

            for (int i = 0; i < 4; i++)
            {
                double mid = i * 90;
                bool selected = i == 0;
                var pen = new Pen(new SolidColorBrush(Color.FromArgb(selected ? (byte)0xFF : (byte)0x8C, 0xFF, 0xFF, 0xFF)), thickness)
                {
                    StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round,
                };
                dc.DrawGeometry(null, pen, Arc(center, ringRadius, mid - 45 + gap / 2, mid + 45 - gap / 2));
            }

            // Pointer dot: where the cursor starts, nudged toward the selected arc.
            dc.DrawEllipse(Brushes.White, null, new Point(s / 2, s / 2 - s * 0.03), s * (tiny ? 0.095 : 0.075), s * (tiny ? 0.095 : 0.075));

            if (!tiny)
            {
                // Hairline rim, like the glass in the app.
                var rim = new LinearGradientBrush(Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF), 60);
                dc.DrawGeometry(null, new Pen(rim, Math.Max(1, s / 128)), tileGeometry);
            }
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var straight = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        straight.Freeze();
        return straight;
    }

    /// <summary>Arc with 0° straight up, clockwise.</summary>
    static Geometry Arc(Point c, double r, double fromDeg, double toDeg)
    {
        Point At(double d) => new(c.X + r * Math.Sin(d * Math.PI / 180), c.Y - r * Math.Cos(d * Math.PI / 180));
        var figure = new PathFigure { StartPoint = At(fromDeg), IsClosed = false };
        figure.Segments.Add(new ArcSegment(At(toDeg), new Size(r, r), 0, toDeg - fromDeg > 180, SweepDirection.Clockwise, true));
        return new PathGeometry(new[] { figure });
    }

    /// <summary>Left-hand image on the setup's welcome/finish pages: brand gradient, logo, name.</summary>
    static BitmapSource WizardBanner(int width, int height)
    {
        double k = width / 164.0;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var background = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0), EndPoint = new Point(0.6, 1),
                GradientStops = { new GradientStop(Blue, 0), new GradientStop(Violet, 0.55), new GradientStop(Pink, 1) },
            };
            dc.DrawRectangle(background, null, new Rect(0, 0, width, height));

            // Faint echo of the ring behind the logo.
            var echo = new Pen(new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), 14 * k);
            dc.DrawEllipse(null, echo, new Point(width / 2.0, 108 * k), 72 * k, 72 * k);

            double logo = 84 * k;
            dc.DrawImage(Render((int)logo), new Rect((width - logo) / 2, 66 * k, logo, logo));

            var title = new FormattedText("Radial Convert", System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
                17 * k, Brushes.White, 1.0) { TextAlignment = TextAlignment.Center };
            dc.DrawText(title, new Point(width / 2.0, 176 * k));

            var tagline = new FormattedText("Hold Ctrl. Drag. Convert.", System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10.5 * k,
                new SolidColorBrush(Color.FromArgb(0xD0, 0xFF, 0xFF, 0xFF)), 1.0) { TextAlignment = TextAlignment.Center };
            dc.DrawText(tagline, new Point(width / 2.0, 202 * k));
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        return bitmap;
    }

    /// <summary>The setup's header image sits on white and BMPs have no transparency.</summary>
    static BitmapSource OnWhite(BitmapSource image, int size)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, size, size));
            dc.DrawImage(image, new Rect(0, 0, size, size));
        }
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        return bitmap;
    }

    static void SaveBmp(BitmapSource image, string path)
    {
        var encoder = new BmpBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(new FormatConvertedBitmap(image, PixelFormats.Bgr24, null, 0)));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    static void SavePng(BitmapSource image, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    /// <summary>ICO with classic 32-bit DIB entries up to 128 px (best compatibility) and PNG at 256 px.</summary>
    static void WriteIco(string path, List<(int Size, BitmapSource Image)> frames)
    {
        var payloads = new List<byte[]>();
        foreach (var (size, image) in frames)
            payloads.Add(size >= 256 ? EncodePng(image) : EncodeDib(image, size));

        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0);
        writer.Write((ushort)1); // icon
        writer.Write((ushort)frames.Count);

        int offset = 6 + 16 * frames.Count;
        for (int i = 0; i < frames.Count; i++)
        {
            int size = frames[i].Size;
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)0);   // palette
            writer.Write((byte)0);   // reserved
            writer.Write((ushort)1); // planes
            writer.Write((ushort)32);
            writer.Write(payloads[i].Length);
            writer.Write(offset);
            offset += payloads[i].Length;
        }
        foreach (byte[] payload in payloads)
            writer.Write(payload);
    }

    static byte[] EncodePng(BitmapSource image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    static byte[] EncodeDib(BitmapSource image, int size)
    {
        int stride = size * 4;
        var pixels = new byte[stride * size];
        image.CopyPixels(pixels, stride, 0);
        int maskStride = (size + 31) / 32 * 4;

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(40); w.Write(size); w.Write(size * 2); // BITMAPINFOHEADER; height covers XOR + AND masks
        w.Write((ushort)1); w.Write((ushort)32); w.Write(0);
        w.Write(stride * size + maskStride * size);
        w.Write(0); w.Write(0); w.Write(0); w.Write(0);
        for (int y = size - 1; y >= 0; y--) // bottom-up
            w.Write(pixels, y * stride, stride);
        w.Write(new byte[maskStride * size]); // alpha channel does the masking
        return ms.ToArray();
    }
}
