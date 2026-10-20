using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace RadialConvert.Input;

/// <summary>
/// Watches global keyboard and mouse input for Ctrl being held on its own.
/// <list type="bullet">
/// <item><see cref="HoldStarted"/>: Ctrl has been down for <see cref="HoldDelay"/> with nothing else pressed.</item>
/// <item><see cref="HoldReleased"/>: Ctrl came up after a hold started (pick the highlighted option).</item>
/// <item><see cref="HoldCancelled"/>: another key, click or scroll happened during the hold (it was a shortcut).</item>
/// </list>
/// Only the Ctrl state is tracked; no keystrokes are recorded.
/// </summary>
internal sealed class CtrlHoldDetector : IDisposable
{
    const int WH_KEYBOARD_LL = 13, WH_MOUSE_LL = 14;
    const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
    const int WM_LBUTTONDOWN = 0x0201, WM_RBUTTONDOWN = 0x0204, WM_MBUTTONDOWN = 0x0207,
              WM_MOUSEWHEEL = 0x020A, WM_XBUTTONDOWN = 0x020B, WM_MOUSEHWHEEL = 0x020E;
    const int VK_CONTROL = 0x11, VK_LCONTROL = 0xA2, VK_RCONTROL = 0xA3;
    const uint LLKHF_INJECTED = 0x10;

    readonly LowLevelProc _keyboardProc, _mouseProc; // fields keep the delegates alive for the hooks
    readonly DispatcherTimer _holdTimer;
    IntPtr _keyboardHook, _mouseHook;
    bool _ctrlDown, _candidate, _holding;

    public CtrlHoldDetector()
    {
        // Hooks and timer both run on this (UI) thread, so no locking is needed.
        _holdTimer = new DispatcherTimer { Interval = HoldDelay };
        _holdTimer.Tick += OnHoldTimer;

        _keyboardProc = KeyboardProc;
        _mouseProc = MouseProc;
        IntPtr module = GetModuleHandle(null);
        _keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, module, 0);
        if (_keyboardHook == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not install keyboard hook");

        _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, module, 0);
        if (_mouseHook == IntPtr.Zero)
            Log.Error($"Could not install mouse hook (error {Marshal.GetLastWin32Error()}); Ctrl+click may open the menu");
    }

    public event Action? HoldStarted;
    public event Action? HoldReleased;
    public event Action? HoldCancelled;

    public bool Enabled { get; set; } = true;

    public TimeSpan HoldDelay { get; } = TimeSpan.FromMilliseconds(300);

    void OnHoldTimer(object? sender, EventArgs e)
    {
        _holdTimer.Stop();
        if (_ctrlDown && _candidate && Enabled)
        {
            _holding = true;
            HoldStarted?.Invoke();
        }
    }

    void Interrupt()
    {
        _candidate = false;
        _holdTimer.Stop();
        if (_holding)
        {
            _holding = false;
            Post(HoldCancelled);
        }
    }

    IntPtr KeyboardProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            bool injected = (info.flags & LLKHF_INJECTED) != 0;
            bool isCtrl = info.vkCode is VK_LCONTROL or VK_RCONTROL or VK_CONTROL;
            int msg = (int)wParam;

            if (!injected)
            {
                if (msg is WM_KEYDOWN or WM_SYSKEYDOWN)
                {
                    if (!isCtrl)
                        Interrupt();
                    else if (!_ctrlDown) // ignore auto-repeat while held
                    {
                        _ctrlDown = true;
                        _candidate = true;
                        _holdTimer.Start();
                    }
                }
                else if (msg is WM_KEYUP or WM_SYSKEYUP && isCtrl)
                {
                    _holdTimer.Stop();
                    if (_holding)
                        Post(HoldReleased);
                    _ctrlDown = _candidate = _holding = false;
                }
            }
        }
        return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _ctrlDown && (int)wParam is WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN
                                                  or WM_XBUTTONDOWN or WM_MOUSEWHEEL or WM_MOUSEHWHEEL)
            Interrupt();
        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    /// <summary>Raise after the hook returns, so slow handlers never delay system input.</summary>
    void Post(Action? handler)
    {
        if (handler is not null)
            Dispatcher.CurrentDispatcher.BeginInvoke(handler);
    }

    public void Dispose()
    {
        _holdTimer.Stop();
        if (_keyboardHook != IntPtr.Zero) UnhookWindowsHookEx(_keyboardHook);
        if (_mouseHook != IntPtr.Zero) UnhookWindowsHookEx(_mouseHook);
        _keyboardHook = _mouseHook = IntPtr.Zero;
    }

    delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    struct KBDLLHOOKSTRUCT
    {
        public int vkCode;
        public int scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr GetModuleHandle(string? lpModuleName);
}
