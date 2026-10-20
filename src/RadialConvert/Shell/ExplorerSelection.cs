using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace RadialConvert.Shell;

/// <summary>
/// Reads the files selected in the foreground File Explorer window (the active tab on Windows 11)
/// or on the Desktop, through the shell's own COM automation objects.
/// </summary>
internal static class ExplorerSelection
{
    static readonly Guid CLSID_ShellWindows = new("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
    static readonly Guid SID_STopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
    static readonly Guid IID_IShellBrowser = new("000214E2-0000-0000-C000-000000000046");
    static readonly Guid IID_IDispatch = new("00020400-0000-0000-C000-000000000046");

    const int SWC_DESKTOP = 8;
    const int SWFO_NEEDDISPATCH = 1;
    const int CSIDL_DESKTOP = 0;
    const uint SVGIO_BACKGROUND = 0;

    /// <summary>Returns selected file paths (folders are skipped), or an empty list when the
    /// foreground window is not Explorer/Desktop or nothing is selected.</summary>
    public static IReadOnlyList<string> GetSelectedFiles()
    {
        IntPtr foreground = GetForegroundWindow();
        string windowClass = GetClassName(foreground);
        try
        {
            return windowClass switch
            {
                "CabinetWClass" or "ExploreWClass" => FromExplorerWindow(foreground),
                "Progman" or "WorkerW" => FromDesktop(),
                _ => Array.Empty<string>(),
            };
        }
        catch (Exception ex)
        {
            Log.Error($"Reading selection from '{windowClass}' failed", ex);
            return Array.Empty<string>();
        }
    }

    static IReadOnlyList<string> FromExplorerWindow(IntPtr foreground)
    {
        // Windows 11 tabs share one top-level window; the first ShellTabWindowClass child is the active tab.
        IntPtr activeTab = FindWindowEx(foreground, IntPtr.Zero, "ShellTabWindowClass", null);

        dynamic shellWindows = CreateShellWindows();
        int count = shellWindows.Count;
        for (int i = 0; i < count; i++)
        {
            dynamic? window = shellWindows.Item(i);
            if (window is null) continue;
            try
            {
                if (new IntPtr((long)window.HWND) != foreground) continue;
                if (activeTab != IntPtr.Zero && GetBrowserWindow((object)window) != activeTab) continue;
                return ReadSelection(window.Document);
            }
            catch (Exception ex)
            {
                Log.Error("Skipping a shell window", ex);
            }
        }
        return Array.Empty<string>();
    }

    static IReadOnlyList<string> FromDesktop()
    {
        var shellWindows = (IShellWindows)CreateShellWindows();
        object location = CSIDL_DESKTOP;
        object? root = null;
        object desktop = shellWindows.FindWindowSW(ref location, ref root, SWC_DESKTOP, out _, SWFO_NEEDDISPATCH);

        IShellView view = GetShellBrowser(desktop).QueryActiveShellView();
        Guid iid = IID_IDispatch;
        object folderView = view.GetItemObject(SVGIO_BACKGROUND, ref iid);
        return ReadSelection(folderView);
    }

    static IReadOnlyList<string> ReadSelection(dynamic folderView)
    {
        var paths = new List<string>();
        dynamic items = folderView.SelectedItems();
        int count = items.Count;
        for (int i = 0; i < count; i++)
        {
            string? path = items.Item(i)?.Path;
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
                paths.Add(path);
        }
        return paths;
    }

    static object CreateShellWindows() =>
        Activator.CreateInstance(Type.GetTypeFromCLSID(CLSID_ShellWindows, throwOnError: true)!)!;

    static IShellBrowser GetShellBrowser(object shellWindow)
    {
        var provider = (IOleServiceProvider)shellWindow;
        Guid service = SID_STopLevelBrowser, iid = IID_IShellBrowser;
        return (IShellBrowser)provider.QueryService(ref service, ref iid);
    }

    static IntPtr GetBrowserWindow(object shellWindow) => GetShellBrowser(shellWindow).GetWindow();

    static string GetClassName(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        return GetClassNameW(hwnd, sb, sb.Capacity) > 0 ? sb.ToString() : string.Empty;
    }

    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)]
    static extern int GetClassNameW(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string? className, string? windowTitle);

    // --- Minimal COM declarations. Unused vtable slots are placeholders and are never called. ---

    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IOleServiceProvider
    {
        [return: MarshalAs(UnmanagedType.Interface)]
        object QueryService(ref Guid guidService, ref Guid riid);
    }

    [ComImport, Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellWindows
    {
        // IDispatch
        void _GetTypeInfoCount(); void _GetTypeInfo(); void _GetIDsOfNames(); void _Invoke();
        // IShellWindows
        void _get_Count(); void _Item(); void _NewEnum(); void _Register(); void _RegisterPending();
        void _Revoke(); void _OnNavigate(); void _OnActivated();

        [return: MarshalAs(UnmanagedType.IDispatch)]
        object FindWindowSW(ref object pvarLoc, ref object? pvarLocRoot, int swClass, out int phwnd, int swfwOptions);
    }

    [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellBrowser
    {
        IntPtr GetWindow();
        void _ContextSensitiveHelp(); void _InsertMenusSB(); void _SetMenuSB(); void _RemoveMenusSB();
        void _SetStatusTextSB(); void _EnableModelessSB(); void _TranslateAcceleratorSB(); void _BrowseObject();
        void _GetViewStateStream(); void _GetControlWindow(); void _SendControlMsg();
        IShellView QueryActiveShellView();
    }

    [ComImport, Guid("000214E3-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellView
    {
        IntPtr GetWindow();
        void _ContextSensitiveHelp(); void _TranslateAccelerator(); void _EnableModeless(); void _UIActivate();
        void _Refresh(); void _CreateViewWindow(); void _DestroyViewWindow(); void _GetCurrentInfo();
        void _AddPropertySheetPages(); void _SaveViewState(); void _SelectItem();

        [return: MarshalAs(UnmanagedType.IDispatch)]
        object GetItemObject(uint uItem, ref Guid riid);
    }
}
