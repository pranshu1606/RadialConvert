using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RadialConvert.Conversion;

/// <summary>One choice in the radial menu, e.g. "PDF" produced by the Word engine.</summary>
/// <param name="Id">Stable key used to intersect options across several selected files.</param>
/// <param name="Label">Big text in the menu slice.</param>
/// <param name="OutputExtension">Extension of the result (".pdf"); for folder outputs the folder suffix.</param>
/// <param name="OutputIsFolder">True when the result is a folder of files (e.g. one PNG per slide).</param>
internal sealed record ConversionOption(
    string Id, string Label, string OutputExtension, IConverter Converter, bool OutputIsFolder = false);

internal interface IConverter
{
    string Name { get; }

    /// <summary>Whether the engine can run on this machine (e.g. Office installed).</summary>
    bool IsAvailable { get; }

    IEnumerable<ConversionOption> GetOptions(string inputExtension);

    /// <summary>Runs on a dedicated STA thread; throws on failure.</summary>
    void Convert(string inputPath, string outputPath, ConversionOption option);
}

internal static class OutputPaths
{
    /// <summary>"C:\a\report.docx" + ".pdf" → "C:\a\report.pdf", or "report (1).pdf" if that exists.</summary>
    public static string Next(string inputPath, ConversionOption option)
    {
        string dir = Path.GetDirectoryName(inputPath)!;
        string name = Path.GetFileNameWithoutExtension(inputPath);
        string suffix = option.OutputIsFolder ? option.OutputExtension : option.OutputExtension.ToLowerInvariant();

        for (int i = 0; ; i++)
        {
            string candidate = Path.Combine(dir, i == 0 ? name + suffix : $"{name} ({i}){suffix}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
                return candidate;
        }
    }
}

/// <summary>Knows every engine and decides which options to show for a selection.</summary>
internal sealed class ConversionRegistry
{
    readonly IReadOnlyList<IConverter> _converters;

    public ConversionRegistry(IEnumerable<IConverter> converters)
    {
        _converters = converters.Where(c => c.IsAvailable).ToList();
        Log.Info("Engines available: " + string.Join(", ", _converters.Select(c => c.Name)));
    }

    public static ConversionRegistry CreateDefault() => new(new IConverter[]
    {
        new WordConverter(),
        new ExcelConverter(),
        new PowerPointConverter(),
        new ImageConverter(),
    });

    /// <summary>Options valid for every selected file, first engine wins for duplicate ids.</summary>
    public IReadOnlyList<ConversionOption> GetOptions(IReadOnlyList<string> paths)
    {
        List<ConversionOption>? common = null;
        foreach (string path in paths)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            var forFile = _converters.SelectMany(c => c.GetOptions(ext)).DistinctBy(o => o.Id).ToList();
            common = common is null ? forFile : common.Where(o => forFile.Any(f => f.Id == o.Id)).ToList();
        }
        return common ?? new List<ConversionOption>();
    }

    public ConversionOption? Find(string path, string idOrExtension)
    {
        string key = idOrExtension.TrimStart('.').ToLowerInvariant();
        return GetOptions(new[] { path }).FirstOrDefault(o =>
            o.Id.Equals(key, StringComparison.OrdinalIgnoreCase) ||
            o.OutputExtension.TrimStart('.').Equals(key, StringComparison.OrdinalIgnoreCase));
    }
}
