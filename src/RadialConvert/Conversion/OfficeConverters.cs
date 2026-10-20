using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace RadialConvert.Conversion;

/// <summary>
/// Shared plumbing for driving installed Microsoft Office apps through late-bound COM automation.
/// Each conversion starts the app hidden, does the work, and quits it again unless the user
/// has their own documents open in that same instance.
/// </summary>
internal abstract class OfficeConverter : IConverter
{
    static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

    readonly string _progId, _processName;

    protected OfficeConverter(string progId, string processName)
    {
        _progId = progId;
        _processName = processName;
    }

    public abstract string Name { get; }

    public bool IsAvailable => Type.GetTypeFromProgID(_progId) is not null;

    public abstract IEnumerable<ConversionOption> GetOptions(string inputExtension);

    public void Convert(string inputPath, string outputPath, ConversionOption option)
    {
        _timedOut = false;
        _preexisting = ProcessIds();
        using var watchdog = new System.Threading.Timer(_ => KillStartedInstances(), null, Timeout, System.Threading.Timeout.InfiniteTimeSpan);
        try
        {
            ConvertCore(inputPath, outputPath, option);
        }
        catch (Exception ex) when (ex is COMException or InvalidComObjectException && _timedOut)
        {
            throw new TimeoutException($"{Name} did not finish within {Timeout.TotalMinutes:0} minutes (it may have been showing a dialog).", ex);
        }
    }

    protected abstract void ConvertCore(string inputPath, string outputPath, ConversionOption option);

    volatile bool _timedOut;
    HashSet<int> _preexisting = new();

    HashSet<int> ProcessIds() => System.Diagnostics.Process.GetProcessesByName(_processName).Select(p => p.Id).ToHashSet();

    /// <summary>A hidden Office instance stuck on a prompt never returns; kill only the instance this
    /// conversion started (never one the user already had open), which makes the pending COM call fail.</summary>
    void KillStartedInstances()
    {
        foreach (var process in StartedProcesses())
        {
            _timedOut = true;
            Log.Error($"{Name} timed out; killing process {process.Id}");
            try { process.Kill(); } catch { }
        }
    }

    /// <summary>Office processes that did not exist when this conversion began, i.e. ours.</summary>
    protected IEnumerable<System.Diagnostics.Process> StartedProcesses() =>
        System.Diagnostics.Process.GetProcessesByName(_processName).Where(p => !_preexisting.Contains(p.Id));

    protected dynamic StartApp() =>
        Activator.CreateInstance(Type.GetTypeFromProgID(_progId, throwOnError: true)!)!;

    protected static void Release(object? comObject)
    {
        if (comObject is not null && Marshal.IsComObject(comObject))
            Marshal.FinalReleaseComObject(comObject);
    }

    protected IEnumerable<ConversionOption> Options(string inputExtension, IReadOnlyCollection<string> inputs,
        params (string Id, string Label, string Extension)[] outputs)
    {
        if (!inputs.Contains(inputExtension)) yield break;
        foreach (var o in outputs)
            if (o.Extension != inputExtension)
                yield return new ConversionOption(o.Id, o.Label, o.Extension, this);
    }
}

internal sealed class WordConverter : OfficeConverter
{
    // WdSaveFormat values
    static readonly Dictionary<string, int> Formats = new()
    {
        [".pdf"] = 17, [".docx"] = 16, [".doc"] = 0, [".rtf"] = 6, [".odt"] = 23, [".txt"] = 2, [".html"] = 10,
    };

    static readonly string[] DocumentInputs = { ".doc", ".docx", ".docm", ".dotx", ".rtf", ".odt", ".txt" };
    static readonly string[] PdfInputs = { ".pdf" };
    static readonly string[] ImageInputs = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff" };

    public WordConverter() : base("Word.Application", "WINWORD") { }

    public override string Name => "Microsoft Word";

    public override IEnumerable<ConversionOption> GetOptions(string ext)
    {
        foreach (var o in Options(ext, DocumentInputs,
                     ("pdf", "PDF", ".pdf"), ("docx", "DOCX", ".docx"), ("txt", "TXT", ".txt"),
                     ("rtf", "RTF", ".rtf"), ("odt", "ODT", ".odt"), ("html", "HTML", ".html")))
            yield return o;

        // Word opens PDFs as editable documents ("PDF Reflow").
        foreach (var o in Options(ext, PdfInputs,
                     ("docx", "DOCX", ".docx"), ("txt", "TXT", ".txt"), ("rtf", "RTF", ".rtf")))
            yield return o;

        foreach (var o in Options(ext, ImageInputs, ("pdf", "PDF", ".pdf")))
            yield return o;
    }

    protected override void ConvertCore(string inputPath, string outputPath, ConversionOption option)
    {
        string inputExt = Path.GetExtension(inputPath).ToLowerInvariant();
        dynamic? app = null, doc = null;
        bool isPdf = inputExt == ".pdf";
        using var notice = isPdf ? new PdfNoticeConfirmer(StartedProcesses) : null;
        try
        {
            app = StartApp();
            app.DisplayAlerts = 0; // wdAlertsNone
            if (isPdf)
            {
                // Word's "will now convert your PDF" notice ignores DisplayAlerts and never appears
                // while Word is hidden, so run it minimized and let PdfNoticeConfirmer press OK.
                app.Visible = true;
                app.WindowState = 2; // wdWindowStateMinimize
            }
            else
            {
                app.Visible = false;
            }

            if (ImageInputs.Contains(inputExt))
                doc = CreateDocumentFromImage(app, inputPath);
            else
                doc = app.Documents.Open(FileName: inputPath, ConfirmConversions: false, ReadOnly: true,
                                         AddToRecentFiles: false, Visible: false);

            int format = Formats[option.OutputExtension];
            if (format == 2) // plain text: write UTF-8 instead of the ANSI code page
                doc.SaveAs2(FileName: outputPath, FileFormat: format, AddToRecentFiles: false, Encoding: 65001);
            else
                doc.SaveAs2(FileName: outputPath, FileFormat: format, AddToRecentFiles: false);
        }
        finally
        {
            try { doc?.Close(SaveChanges: 0); } catch { /* already gone */ }
            Release(doc);
            QuitIfIdle(app);
        }
    }

    static dynamic CreateDocumentFromImage(dynamic app, string imagePath)
    {
        dynamic doc = app.Documents.Add();
        dynamic setup = doc.PageSetup;
        setup.TopMargin = setup.BottomMargin = setup.LeftMargin = setup.RightMargin = 36f; // 0.5 inch

        float maxWidth = setup.PageWidth - setup.LeftMargin - setup.RightMargin;
        float maxHeight = setup.PageHeight - setup.TopMargin - setup.BottomMargin - 1f;

        dynamic picture = doc.InlineShapes.AddPicture(FileName: imagePath, LinkToFile: false, SaveWithDocument: true);
        picture.LockAspectRatio = -1; // msoTrue
        float scale = Math.Min(1f, Math.Min(maxWidth / (float)picture.Width, maxHeight / (float)picture.Height));
        if (scale < 1f)
            picture.Width = (float)picture.Width * scale;
        return doc;
    }

    void QuitIfIdle(dynamic? app)
    {
        if (app is null) return;
        try
        {
            if (app.Documents.Count == 0)
                app.Quit(SaveChanges: 0);
        }
        catch { /* user may have closed it */ }
        Release(app);
    }
}

internal sealed class ExcelConverter : OfficeConverter
{
    // XlFileFormat values; PDF goes through ExportAsFixedFormat instead.
    static readonly Dictionary<string, int> Formats = new()
    {
        [".xlsx"] = 51, [".xls"] = 56, [".csv"] = 62 /* xlCSVUTF8 */, [".ods"] = 60,
    };

    static readonly string[] Inputs = { ".xls", ".xlsx", ".xlsm", ".xlsb", ".csv", ".ods" };

    public ExcelConverter() : base("Excel.Application", "EXCEL") { }

    public override string Name => "Microsoft Excel";

    public override IEnumerable<ConversionOption> GetOptions(string ext) =>
        Options(ext, Inputs, ("pdf", "PDF", ".pdf"), ("xlsx", "XLSX", ".xlsx"), ("csv", "CSV", ".csv"),
                ("ods", "ODS", ".ods"), ("xls", "XLS", ".xls"));

    protected override void ConvertCore(string inputPath, string outputPath, ConversionOption option)
    {
        dynamic? app = null, workbook = null;
        try
        {
            app = StartApp();
            app.Visible = false;
            app.DisplayAlerts = false;
            app.ScreenUpdating = false;

            workbook = app.Workbooks.Open(Filename: inputPath, UpdateLinks: 0, ReadOnly: true, AddToMru: false);

            if (option.OutputExtension == ".pdf")
                workbook.ExportAsFixedFormat(Type: 0 /* xlTypePDF */, Filename: outputPath);
            else
                workbook.SaveAs(Filename: outputPath, FileFormat: Formats[option.OutputExtension]);
        }
        finally
        {
            try { workbook?.Close(SaveChanges: false); } catch { }
            Release(workbook);
            if (app is not null)
            {
                try { if (app.Workbooks.Count == 0) app.Quit(); } catch { }
                Release(app);
            }
        }
    }
}

internal sealed class PowerPointConverter : OfficeConverter
{
    // PpSaveAsFileType values
    static readonly Dictionary<string, int> Formats = new()
    {
        [".pdf"] = 32, [".pptx"] = 24, [".ppt"] = 1, [".odp"] = 35,
    };

    static readonly string[] Inputs = { ".ppt", ".pptx", ".pptm", ".ppsx", ".pps", ".odp" };

    public PowerPointConverter() : base("PowerPoint.Application", "POWERPNT") { }

    public override string Name => "Microsoft PowerPoint";

    public override IEnumerable<ConversionOption> GetOptions(string ext)
    {
        foreach (var o in Options(ext, Inputs, ("pdf", "PDF", ".pdf"), ("pptx", "PPTX", ".pptx"), ("odp", "ODP", ".odp")))
            yield return o;
        if (Inputs.Contains(ext))
        {
            yield return new ConversionOption("png-slides", "PNG slides", " - slides", this, OutputIsFolder: true);
            yield return new ConversionOption("jpg-slides", "JPG slides", " - slides", this, OutputIsFolder: true);
        }
    }

    protected override void ConvertCore(string inputPath, string outputPath, ConversionOption option)
    {
        dynamic? app = null, presentation = null;
        try
        {
            app = StartApp(); // PowerPoint refuses Visible=false; WithWindow:false keeps it hidden instead
            presentation = app.Presentations.Open(inputPath, -1 /* ReadOnly */, 0 /* Untitled */, 0 /* WithWindow */);

            if (option.OutputIsFolder)
            {
                string filter = option.Id.StartsWith("png") ? "PNG" : "JPG";
                Directory.CreateDirectory(outputPath);
                int count = presentation.Slides.Count;
                for (int i = 1; i <= count; i++)
                    presentation.Slides[i].Export(Path.Combine(outputPath, $"Slide {i}.{filter.ToLowerInvariant()}"), filter);
            }
            else
            {
                presentation.SaveAs(outputPath, Formats[option.OutputExtension]);
            }
        }
        finally
        {
            try { presentation?.Close(); } catch { }
            Release(presentation);
            if (app is not null)
            {
                // PowerPoint is single-instance: never quit while the user has presentations open.
                try { if (app.Presentations.Count == 0) app.Quit(); } catch { }
                Release(app);
            }
        }
    }
}
