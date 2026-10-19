using System.Runtime.InteropServices;
using Windows.Graphics;

namespace XScreen.App;

sealed class NativeWindow : IDisposable
{
    const uint TrayMessage = 0x8001;
    readonly IntPtr hwnd;
    readonly SubclassProc callback;
    readonly uint taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    NotifyData icon;
    bool disposed;
    bool ownedIcon;
    public bool TrayAvailable { get; private set; }
    public event Action? OpenRequested;
    public event Action<PointInt32>? ContextMenuRequested;
    public NativeWindow(IntPtr handle, string iconPath)
    {
        hwnd = handle;
        callback = WindowProc;
        if (!SetWindowSubclass(hwnd, callback, 1, 0)) throw new InvalidOperationException("无法初始化托盘窗口。");
        var image = LoadImage(IntPtr.Zero, iconPath, 1, 32, 32, 0x10);
        ownedIcon = image != IntPtr.Zero;
        if (!ownedIcon) image = LoadIcon(IntPtr.Zero, (IntPtr)32512);
        icon = new NotifyData { Size = (uint)Marshal.SizeOf<NotifyData>(), Window = hwnd, Id = 1,
            Flags = 1 | 2 | 4, Callback = TrayMessage, Icon = image, Tip = "XScreen · 显示器控制",
            Info = "", InfoTitle = "" };
        AddIcon();
    }
    void AddIcon()
    {
        TrayAvailable = Shell_NotifyIcon(0, ref icon);
        if (TrayAvailable) { icon.Version = 4; Shell_NotifyIcon(4, ref icon); }
        else AppLog.Write("tray", "Shell_NotifyIcon failed: " + Marshal.GetLastWin32Error());
    }
    IntPtr WindowProc(IntPtr h, uint message, IntPtr w, IntPtr l, nuint id, nuint data)
    {
        try
        {
            if (message == TrayMessage)
            {
                int action = (int)((long)l & 0xffff);
                if (action is 0x202 or 0x203 or 0x400 or 0x401) OpenRequested?.Invoke();
                if (action is 0x205 or 0x7b) ContextMenuRequested?.Invoke(GetTrayAnchor());
                return IntPtr.Zero;
            }
            if (message == 0x8002) { OpenRequested?.Invoke(); return IntPtr.Zero; }
            if (message == 0x8003) { ContextMenuRequested?.Invoke(GetTrayAnchor()); return IntPtr.Zero; }
            if (message == taskbarCreated) AddIcon();
        }
        catch (Exception ex) { AppLog.Write("window-message", ex.ToString()); }
        return DefSubclassProc(h, message, w, l);
    }
    public PointInt32 GetTrayAnchor()
    {
        var identifier = new NotifyIdentifier { Size = (uint)Marshal.SizeOf<NotifyIdentifier>(), Window = hwnd, Id = 1 };
        if (Shell_NotifyIconGetRect(ref identifier, out var rect) == 0) return new(rect.Right, rect.Top);
        GetCursorPos(out var point); return new(point.X, point.Y);
    }
    public void Show() => ShowHandle(hwnd);
    internal static void ShowHandle(IntPtr handle) { ShowWindow(handle, ShowCommand(IsIconic(handle))); SetForegroundWindow(handle); }
    // SW_SHOW preserves a maximized window; SW_RESTORE is only for a minimized one.
    internal static int ShowCommand(bool minimized) => minimized ? 9 : 5;
    public static void BringExistingToFront(bool showTrayMenu = false)
    {
        var h = FindWindow(null, Program.WindowTitle);
        // Compatibility while a pre-0.3.25 instance still owns the single-instance mutex.
        if (h == IntPtr.Zero) h = FindWindow(null, "XScreen · UltraGear 控制");
        if (h != IntPtr.Zero)
        {
            PostMessage(h, showTrayMenu ? 0x8003u : 0x8002u, IntPtr.Zero, IntPtr.Zero);
            if (!showTrayMenu) ShowHandle(h);
        }
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (TrayAvailable) Shell_NotifyIcon(2, ref icon);
        RemoveWindowSubclass(hwnd, callback, 1);
        if (ownedIcon) DestroyIcon(icon.Icon);
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct NotifyData
    {
        public uint Size; public IntPtr Window; public uint Id, Flags, Callback; public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags; public Guid Guid; public IntPtr BalloonIcon;
    }
    [StructLayout(LayoutKind.Sequential)] struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct NotifyIdentifier { public uint Size; public IntPtr Window; public uint Id; public Guid Guid; }
    delegate IntPtr SubclassProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, nuint id, nuint data);
    [DllImport("comctl32.dll")] static extern bool SetWindowSubclass(IntPtr hwnd, SubclassProc proc, nuint id, nuint data);
    [DllImport("comctl32.dll")] static extern bool RemoveWindowSubclass(IntPtr hwnd, SubclassProc proc, nuint id);
    [DllImport("comctl32.dll")] static extern IntPtr DefSubclassProc(IntPtr hwnd, uint message, IntPtr w, IntPtr l);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool Shell_NotifyIcon(uint message, ref NotifyData data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr LoadImage(IntPtr instance, string path, uint type, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern IntPtr LoadIcon(IntPtr instance, IntPtr name);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
    [DllImport("shell32.dll")] static extern int Shell_NotifyIconGetRect(ref NotifyIdentifier identifier, out Rect rect);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string? className, string title);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr w, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessage(string message);
}
