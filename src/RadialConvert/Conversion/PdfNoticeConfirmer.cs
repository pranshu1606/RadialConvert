using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Windows.Automation;

namespace RadialConvert.Conversion;

/// <summary>
/// While Word opens a PDF it shows "Word will now convert your PDF to an editable Word document…"
/// and waits for OK, ignoring DisplayAlerts. This watches only the Word instance(s) the current
/// conversion started and presses OK on exactly that notice, then stops.
/// </summary>
internal sealed class PdfNoticeConfirmer : IDisposable
{
    const string NoticePrefix = "Word will now convert your PDF";

    readonly Func<IEnumerable<Process>> _ourProcesses;
    readonly CancellationTokenSource _stop = new();
    readonly Thread _thread;

    public PdfNoticeConfirmer(Func<IEnumerable<Process>> ourProcesses)
    {
        _ourProcesses = ourProcesses;
        _thread = new Thread(Watch) { IsBackground = true, Name = "PdfNoticeConfirmer" };
        _thread.SetApartmentState(ApartmentState.MTA); // UI Automation clients work best off STA
        _thread.Start();
    }

    void Watch()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                foreach (var process in _ourProcesses())
                    if (TryConfirm(process.Id))
                        return;
            }
            catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException)
            {
                // Windows come and go while Word starts up; try again next tick.
            }
            _stop.Token.WaitHandle.WaitOne(250);
        }
    }

    static bool TryConfirm(int processId)
    {
        var ofProcess = new PropertyCondition(AutomationElement.ProcessIdProperty, processId);
        foreach (AutomationElement window in AutomationElement.RootElement.FindAll(TreeScope.Children, ofProcess))
        {
            if (!HasNotice(window)) continue;

            var ok = window.FindFirst(TreeScope.Descendants, new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                new PropertyCondition(AutomationElement.NameProperty, "OK")));
            if (ok?.GetCurrentPattern(InvokePattern.Pattern) is InvokePattern invoke)
            {
                invoke.Invoke();
                Log.Info("Confirmed Word's PDF conversion notice");
                return true;
            }
        }
        return false;
    }

    static bool HasNotice(AutomationElement window)
    {
        var texts = window.FindAll(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text));
        foreach (AutomationElement text in texts)
            if (text.Current.Name.StartsWith(NoticePrefix, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    public void Dispose()
    {
        _stop.Cancel();
        _thread.Join(TimeSpan.FromSeconds(2));
    }
}
