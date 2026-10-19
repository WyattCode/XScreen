using System.Runtime.InteropServices;

namespace XScreen.App;

// Observe only this window's native move/size loop; no global hook, input or display API.
sealed class WindowResizeObserver : IDisposable
{
    readonly IntPtr handle;
    readonly SubclassProc callback;
    readonly Action<bool, bool> changed;
    bool disposed, sizing;
    public WindowResizeObserver(IntPtr hwnd, Action<bool, bool> onMoveSize)
    {
        handle = hwnd; changed = onMoveSize; callback = WindowProc;
        if (!SetWindowSubclass(handle, callback, 2, 0)) throw new InvalidOperationException("无法监听窗口缩放。");
    }
    IntPtr WindowProc(IntPtr h, uint message, IntPtr w, IntPtr l, nuint id, nuint data)
    {
        try
        {
            if (!disposed)
            {
                if (message == 0x231) { sizing = false; changed(true, false); }
                else if (message == 0x214) sizing = true;
                else if (message == 0x232) { changed(false, sizing); sizing = false; }
            }
        }
        catch (Exception ex) { AppLog.Write("window-resize-observer", ex.ToString()); }
        return DefSubclassProc(h, message, w, l);
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        RemoveWindowSubclass(handle, callback, 2);
    }
    delegate IntPtr SubclassProc(IntPtr h, uint message, IntPtr w, IntPtr l, nuint id, nuint data);
    [DllImport("comctl32.dll")] static extern bool SetWindowSubclass(IntPtr h, SubclassProc callback, nuint id, nuint data);
    [DllImport("comctl32.dll")] static extern bool RemoveWindowSubclass(IntPtr h, SubclassProc callback, nuint id);
    [DllImport("comctl32.dll")] static extern IntPtr DefSubclassProc(IntPtr h, uint message, IntPtr w, IntPtr l);
}
