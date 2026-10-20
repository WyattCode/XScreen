using System.Runtime.InteropServices;
using Windows.Graphics;

namespace XScreen.App;

sealed class NativeWindow : IDisposable
{
    const uint TrayMessage = 0x8001;
    const nuint RecoveryTimer = 0x58534352;
    readonly IntPtr hwnd;
    readonly SubclassProc callback;
    readonly uint taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    NotificationIcon? icon;
    bool disposed;
    public bool TrayAvailable => icon?.Available == true;
    public event Action? OpenRequested;
    public event Action<PointInt32>? ContextMenuRequested;
    public NativeWindow(IntPtr handle, string iconPath)
    {
        hwnd = handle;
        callback = WindowProc;
        if (!SetWindowSubclass(hwnd, callback, 1, 0)) throw new InvalidOperationException("无法初始化托盘窗口。");
        try
        {
            icon = new(new TrayPlatform(hwnd), hwnd, TrayMessage, iconPath, message => AppLog.Write("tray", message));
            if (disposed) icon.Dispose(); // A native teardown may reenter initialization.
        }
        catch { disposed = true; KillTimer(hwnd, RecoveryTimer); RemoveWindowSubclass(hwnd, callback, 1); throw; }
    }
    IntPtr WindowProc(IntPtr h, uint message, IntPtr w, IntPtr l, nuint id, nuint data)
    {
        try
        {
            if (message == 0x0082) { Dispose(); return DefSubclassProc(h, message, w, l); } // WM_NCDESTROY
            if (disposed) return DefSubclassProc(h, message, w, l);
            if (message == TrayMessage)
            {
                int action = (int)((long)l & 0xffff);
                if (action is 0x202 or 0x203 or 0x400 or 0x401) OpenRequested?.Invoke();
                if (action is 0x205 or 0x7b) ContextMenuRequested?.Invoke(GetTrayAnchor());
                return IntPtr.Zero;
            }
            if (message == 0x8002) { OpenRequested?.Invoke(); return IntPtr.Zero; }
            if (message == 0x8003) { ContextMenuRequested?.Invoke(GetTrayAnchor()); return IntPtr.Zero; }
            if (taskbarCreated != 0 && message == taskbarCreated) icon?.Recreate();
            if (message is 0x02e0 or 0x001a or 0x007e) icon?.RefreshForDisplay();
            if (message == 0x0113 && (nuint)w == RecoveryTimer) { icon?.Recover(); return IntPtr.Zero; }
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
        try { icon?.Dispose(); }
        finally { KillTimer(hwnd, RecoveryTimer); RemoveWindowSubclass(hwnd, callback, 1); }
    }
    sealed class TrayPlatform(IntPtr owner) : INotificationIconPlatform
    {
        public int SmallIconPixels
        {
            get
            {
                var taskbar = FindWindow("Shell_TrayWnd", null);
                uint dpi = GetDpiForWindow(taskbar != IntPtr.Zero ? taskbar : owner);
                if (dpi == 0) dpi = 96;
                return Math.Max(GetSystemMetricsForDpi(49, dpi), GetSystemMetricsForDpi(50, dpi));
            }
        }
        public IntPtr Load(string path, int pixels) => LoadImage(IntPtr.Zero, path, 1, pixels, pixels, 0x10);
        public IntPtr Fallback() => LoadIcon(IntPtr.Zero, (IntPtr)32512);
        public bool Notify(uint message, ref NotificationIconData data) => Shell_NotifyIcon(message, ref data);
        public void Destroy(IntPtr image) => DestroyIcon(image);
        // Shell-only recovery. No monitor connection, refresh or setting action.
        public bool SetRecoveryTimer(bool enabled) => enabled
            ? SetTimer(owner, RecoveryTimer, 5000, IntPtr.Zero) != 0
            : KillTimer(owner, RecoveryTimer);
    }
    [StructLayout(LayoutKind.Sequential)] struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct NotifyIdentifier { public uint Size; public IntPtr Window; public uint Id; public Guid Guid; }
    delegate IntPtr SubclassProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, nuint id, nuint data);
    [DllImport("comctl32.dll")] static extern bool SetWindowSubclass(IntPtr hwnd, SubclassProc proc, nuint id, nuint data);
    [DllImport("comctl32.dll")] static extern bool RemoveWindowSubclass(IntPtr hwnd, SubclassProc proc, nuint id);
    [DllImport("comctl32.dll")] static extern IntPtr DefSubclassProc(IntPtr hwnd, uint message, IntPtr w, IntPtr l);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool Shell_NotifyIcon(uint message, ref NotificationIconData data);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int GetSystemMetricsForDpi(int index, uint dpi);
    [DllImport("user32.dll")] static extern nuint SetTimer(IntPtr hwnd, nuint id, uint milliseconds, IntPtr callback);
    [DllImport("user32.dll")] static extern bool KillTimer(IntPtr hwnd, nuint id);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr LoadImage(IntPtr instance, string path, uint type, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern IntPtr LoadIcon(IntPtr instance, IntPtr name);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
    [DllImport("shell32.dll")] static extern int Shell_NotifyIconGetRect(ref NotifyIdentifier identifier, out Rect rect);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string? className, string? title);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr w, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessage(string message);
}
