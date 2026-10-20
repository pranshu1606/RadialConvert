using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RadialConvert.Conversion;

/// <summary>Image format conversion with the Windows Imaging Component (no extra dependencies).
/// WEBP/HEIC inputs work when the matching Windows codec extension is installed.</summary>
internal sealed class ImageConverter : IConverter
{
    static readonly string[] Inputs =
        { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".ico", ".webp", ".heic", ".jxr" };

    static readonly (string Id, string Label, string Extension)[] Outputs =
    {
        ("png", "PNG", ".png"), ("jpg", "JPG", ".jpg"), ("bmp", "BMP", ".bmp"), ("tiff", "TIFF", ".tiff"), ("gif", "GIF", ".gif"),
    };

    public string Name => "Windows Imaging";

    public bool IsAvailable => true;

    public IEnumerable<ConversionOption> GetOptions(string ext)
    {
        if (!Inputs.Contains(ext)) yield break;
        string normalized = ext switch { ".jpeg" => ".jpg", ".tif" => ".tiff", _ => ext };
        foreach (var o in Outputs.Where(o => o.Extension != normalized))
            yield return new ConversionOption(o.Id, o.Label, o.Extension, this);
    }

    public void Convert(string inputPath, string outputPath, ConversionOption option)
    {
        BitmapSource source;
        using (var input = File.OpenRead(inputPath))
        {
            var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            source = decoder.Frames[0];
        }

        BitmapEncoder encoder = option.OutputExtension switch
        {
            ".png" => new PngBitmapEncoder(),
            ".jpg" => new JpegBitmapEncoder { QualityLevel = 92 },
            ".bmp" => new BmpBitmapEncoder(),
            ".tiff" => new TiffBitmapEncoder(),
            ".gif" => new GifBitmapEncoder(),
            _ => throw new NotSupportedException(option.OutputExtension),
        };

        // JPG and BMP have no transparency: flatten onto white rather than letting it turn black.
        if (option.OutputExtension is ".jpg" or ".bmp")
            source = FlattenOnWhite(source);

        encoder.Frames.Add(BitmapFrame.Create(source));
        using var output = File.Create(outputPath);
        encoder.Save(output);
    }

    static BitmapSource FlattenOnWhite(BitmapSource source)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var rect = new Rect(0, 0, source.PixelWidth, source.PixelHeight);
            dc.DrawRectangle(Brushes.White, null, rect);
            dc.DrawImage(source, rect);
        }
        var target = new RenderTargetBitmap(source.PixelWidth, source.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        return new FormatConvertedBitmap(target, PixelFormats.Bgr24, null, 0);
    }
}
