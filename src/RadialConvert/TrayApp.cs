using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;
using RadialConvert.Conversion;
using RadialConvert.Input;
using RadialConvert.Shell;
using RadialConvert.UI;

namespace RadialConvert;

/// <summary>Tray icon + glue: hold Ctrl → read selection → radial menu → release → confirm → convert → notify.</summary>
internal sealed class TrayApp : IDisposable
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunValue = "RadialConvert";
    const string IdleTip = "Radial Convert — select a file, hold Ctrl and drag";

    readonly ConversionRegistry _registry;
    readonly ConversionService _service = new();
    readonly CtrlHoldDetector _detector;
    readonly NotifyIcon _icon;
    readonly ToolStripMenuItem _enabledItem, _startupItem;
    RadialMenuWindow? _menu;
    ConfirmWindow? _confirm;
    string? _lastOutput;

    public TrayApp(ConversionRegistry registry)
    {
        _registry = registry;
        _detector = new CtrlHoldDetector();
        _detector.HoldStarted += OnHoldStarted;
        _detector.HoldReleased += OnHoldReleased;
        _detector.HoldCancelled += () => CloseMenu();

        _enabledItem = new ToolStripMenuItem("Enabled (hold Ctrl)", null, (_, _) => ToggleEnabled()) { Checked = true };
        _startupItem = new ToolStripMenuItem("Start with Windows", null, (_, _) => ToggleStartup()) { Checked = IsStartupEnabled() };

        var menu = new ContextMenuStrip();
        menu.Items.Add(_enabledItem);
        menu.Items.Add("Convert a file…", null, (_, _) => PickFileAndShowMenu());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_startupItem);
        menu.Items.Add("Open log", null, (_, _) => OpenPath(Log.FilePath));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => System.Windows.Application.Current.Shutdown());

        _icon = new NotifyIcon
        {
            Icon = AppIcon.TrayIcon(),
            Text = IdleTip,
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.BalloonTipClicked += (_, _) => { if (_lastOutput is not null) RevealInExplorer(_lastOutput); };
        _icon.DoubleClick += (_, _) => PickFileAndShowMenu();

        Notify("Radial Convert is running", "Select a file, hold Ctrl, drag toward a format and let go.", ToolTipIcon.Info);
    }

    void OnHoldStarted()
    {
        if (_menu is not null || _confirm is not null) return;

        IReadOnlyList<string> files = ExplorerSelection.GetSelectedFiles();
        if (files.Count == 0) return;
        ShowMenu(files, holdMode: true);
    }

    async void OnHoldReleased()
    {
        RadialMenuWindow? menu = _menu;
        if (menu is null) return;
        ConversionOption? choice = menu.Selected;
        CloseMenu();
        if (choice is not null)
            await ConfirmAsync(menu.Files, choice, menu.CenterOnScreen);
    }

    void CloseMenu()
    {
        _menu?.CloseAnimated();
        _menu = null;
    }

    void ShowMenu(IReadOnlyList<string> files, bool holdMode)
    {
        IReadOnlyList<ConversionOption> options = _registry.GetOptions(files);
        if (options.Count == 0)
        {
            string exts = string.Join(", ", files.Select(f => Path.GetExtension(f)).Distinct());
            Notify("No conversions available", $"Nothing to convert {exts} into yet.", ToolTipIcon.Info);
            return;
        }

        var menu = new RadialMenuWindow(files, options, holdMode);
        menu.Chosen += option => { CloseMenu(); _ = ConfirmAsync(files, option, menu.CenterOnScreen); };
        menu.Closed += (_, _) => { if (_menu == menu) _menu = null; };
        _menu = menu;
        menu.Show();
    }

    async System.Threading.Tasks.Task ConfirmAsync(IReadOnlyList<string> files, ConversionOption option, Native.POINT anchor)
    {
        // Let the menu fade out first, so the card's glass doesn't capture it.
        await System.Threading.Tasks.Task.Delay(160);
        if (_confirm is not null) return;

        var confirm = new ConfirmWindow(files, option, anchor);
        confirm.Confirmed += () => _ = ConvertAsync(files, option);
        confirm.Closed += (_, _) => { if (_confirm == confirm) _confirm = null; };
        _confirm = confirm;
        confirm.Show();
    }

    async System.Threading.Tasks.Task ConvertAsync(IReadOnlyList<string> files, ConversionOption option)
    {
        string what = files.Count == 1 ? Path.GetFileName(files[0]) : $"{files.Count} files";
        _icon.Text = Truncate($"Converting {what} to {option.Label}…", 63);

        var results = await _service.ConvertAsync(files, option);

        _icon.Text = IdleTip;
        var ok = results.Where(r => r.Succeeded).ToList();
        var failed = results.Where(r => !r.Succeeded).ToList();
        _lastOutput = ok.LastOrDefault()?.OutputPath;

        if (failed.Count == 0)
            Notify($"Converted to {option.Label}",
                   ok.Count == 1 ? $"{Path.GetFileName(ok[0].OutputPath)} — click to show it" : $"{ok.Count} files converted — click to show",
                   ToolTipIcon.Info);
        else
            Notify($"{failed.Count} of {results.Count} conversions failed",
                   $"{Path.GetFileName(failed[0].InputPath)}: {failed[0].Error!.Message}", ToolTipIcon.Error);
    }

    void PickFileAndShowMenu()
    {
        using var dialog = new System.Windows.Forms.OpenFileDialog { Title = "Choose files to convert", Multiselect = true };
        if (dialog.ShowDialog() == DialogResult.OK && dialog.FileNames.Length > 0)
            ShowMenu(dialog.FileNames, holdMode: false);
    }

    void ToggleEnabled()
    {
        _enabledItem.Checked = !_enabledItem.Checked;
        _detector.Enabled = _enabledItem.Checked;
    }

    static bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(RunValue) is not null;
    }

    void ToggleStartup()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (_startupItem.Checked)
            key.DeleteValue(RunValue, throwOnMissingValue: false);
        else
            key.SetValue(RunValue, $"\"{Environment.ProcessPath}\"");
        _startupItem.Checked = IsStartupEnabled();
    }

    void Notify(string title, string text, ToolTipIcon icon) =>
        _icon.ShowBalloonTip(4000, title, string.IsNullOrEmpty(text) ? " " : text, icon);

    static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    static void RevealInExplorer(string path) =>
        Process.Start("explorer.exe", $"/select,\"{path}\"");

    static void OpenPath(string path)
    {
        if (File.Exists(path))
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    public void Dispose()
    {
        _detector.Dispose();
        _icon.Visible = false;
        _icon.Dispose();
    }
}
