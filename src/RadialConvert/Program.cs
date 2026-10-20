using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using RadialConvert.Conversion;

namespace RadialConvert;

internal static class Program
{
    /// <summary>
    /// RadialConvert.exe                         → tray app (tap Ctrl on a selected file)
    /// RadialConvert.exe --list &lt;file&gt;            → print available conversions
    /// RadialConvert.exe --convert &lt;file&gt; &lt;id&gt;   → convert without UI (e.g. id "pdf")
    /// </summary>
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length > 0)
            return RunCommandLine(args);

        using var singleInstance = new Mutex(true, @"Local\RadialConvert.SingleInstance", out bool isFirst);
        if (!isFirst) return 0;

        var app = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
        app.DispatcherUnhandledException += (_, e) =>
        {
            Log.Error("Unhandled UI exception", e.Exception);
            e.Handled = true;
        };

        Log.Info("Starting");
        using var tray = new TrayApp(ConversionRegistry.CreateDefault());
        app.Run();
        Log.Info("Exiting");
        return 0;
    }

    static int RunCommandLine(string[] args)
    {
        AttachConsole(-1); // WinExe: print into the console that launched us, if any
        var registry = ConversionRegistry.CreateDefault();

        switch (args[0])
        {
            case "--list" when args.Length == 2:
            {
                string file = Path.GetFullPath(args[1]);
                foreach (var o in registry.GetOptions(new[] { file }))
                    Console.WriteLine($"{o.Id,-12} {o.Label,-12} via {o.Converter.Name}");
                return 0;
            }
            case "--convert" when args.Length == 3:
            {
                string file = Path.GetFullPath(args[1]);
                var option = registry.Find(file, args[2]);
                if (option is null)
                {
                    Console.Error.WriteLine($"No conversion '{args[2]}' for {Path.GetExtension(file)}");
                    return 2;
                }
                var result = ConversionService.ConvertOne(file, option);
                if (result.Succeeded)
                {
                    Console.WriteLine(result.OutputPath);
                    return 0;
                }
                Console.Error.WriteLine(result.Error!.Message);
                return 1;
            }
            // Developer helpers: what the Ctrl tap would see, and what the menu looks like.
            case "--selection":
            {
                var files = Shell.ExplorerSelection.GetSelectedFiles();
                Log.Info($"Selection: [{string.Join(" | ", files)}]");
                foreach (string f in files) Console.WriteLine(f);
                return files.Count > 0 ? 0 : 3;
            }
            // --preview-menu <file> <out.png> [selected index] [light|dark]
            case "--preview-menu" when args.Length >= 3:
            {
                string file = Path.GetFullPath(args[1]);
                var options = registry.GetOptions(new[] { file });
                var menu = new UI.RadialMenuWindow(new[] { file }, options, holdMode: true,
                                                   new UI.Native.POINT(), PreviewBackdrop(args, 4, 244 + 36, 244 + 36));
                if (args.Length > 3 && int.TryParse(args[3], out int index) && index >= 0)
                    menu.PreviewSelect(index);
                menu.RenderPreview(Path.GetFullPath(args[2]));
                return 0;
            }
            // --preview-confirm <file> <id> <out.png> [light|dark]
            case "--preview-confirm" when args.Length >= 4:
            {
                string file = Path.GetFullPath(args[1]);
                var option = registry.Find(file, args[2]);
                if (option is null) return 2;
                new UI.ConfirmWindow(new[] { file }, option, new UI.Native.POINT(), PreviewBackdrop(args, 4, 348 + 36, 176 + 36))
                    .RenderPreview(Path.GetFullPath(args[3]));
                return 0;
            }
            default:
                Console.Error.WriteLine("Usage: RadialConvert [--list <file> | --convert <file> <id> | --selection | " +
                                        "--preview-menu <file> <out.png> [index] [light|dark] | --preview-confirm <file> <id> <out.png> [light|dark]]");
                return 2;
        }
    }

    static UI.GlassBackdrop PreviewBackdrop(string[] args, int themeArg, double width, double height) =>
        UI.GlassBackdrop.Synthetic(width, height, dark: !(args.Length > themeArg && args[themeArg] == "light"));

    [DllImport("kernel32.dll")]
    static extern bool AttachConsole(int processId);
}
