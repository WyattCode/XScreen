using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;

namespace XScreen.App;

sealed partial class MainWindow
{
    // Null in every offline renderer/test unless an isolated test path is explicitly injected.
    internal string? WindowStatePath { get; set; }
    DispatcherQueueTimer? windowStateTimer;
    SavedWindowState? pendingWindowState;
    SavedWindowState? rememberedWindowState, resizeStart;
    WindowResizeObserver? resizeObserver;

    void QueueWindowStateSave()
    {
        if (exiting || resizeStart != null || !initialPlacementPrepared || WindowStatePath == null) return;
        var state = CaptureWindowState();
        if (state == null) return;
        pendingWindowState = state;
        if (windowStateTimer == null)
        {
            windowStateTimer = DispatcherQueue.CreateTimer();
            windowStateTimer.Interval = TimeSpan.FromMilliseconds(350); windowStateTimer.IsRepeating = false;
            windowStateTimer.Tick += (_, _) => FlushWindowState();
        }
        windowStateTimer.Stop(); windowStateTimer.Start();
    }
    SavedWindowState? CaptureRawWindowState()
    {
        if (!AppWindow.IsVisible) return null;
        var placement = new WindowPlacement { Length = (uint)Marshal.SizeOf<WindowPlacement>() };
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        if (!GetWindowPlacement(handle, ref placement) || placement.ShowCommand is 2 or 6 or 7) return null;
        double scale = Math.Max(1, GetDpiForWindow(handle) / 96d);
        var normal = placement.Normal;
        var state = new SavedWindowState(((double)normal.Right - normal.Left) / scale,
            ((double)normal.Bottom - normal.Top) / scale, placement.ShowCommand == 3);
        return WindowStateStore.Valid(state) ? state : null;
    }
    SavedWindowState? CaptureWindowState()
    {
        var raw = CaptureRawWindowState();
        return raw == null || rememberedWindowState == null ? raw
            : raw with { Width = rememberedWindowState.Width, Height = rememberedWindowState.Height };
    }
    void OnMoveSize(bool entering, bool sizing)
    {
        if (exiting || !initialPlacementPrepared) return;
        if (entering) { resizeStart = CaptureRawWindowState(); windowStateTimer?.Stop(); return; }
        var before = resizeStart; resizeStart = null;
        var after = CaptureRawWindowState();
        // Move-only, DPI and work-area changes must not replace the user's size.
        // Native gesture boundaries also distinguish a deliberate one-pixel resize.
        if (sizing && before != null && after != null && (Math.Abs(before.Width-after.Width) > .001 || Math.Abs(before.Height-after.Height) > .001))
            rememberedWindowState = after;
        QueueWindowStateSave();
    }
    void SaveWindowState()
    {
        if (WindowStatePath == null || !initialPlacementPrepared) return;
        // A minimized or tray-hidden window must not replace its last normal dimensions/state.
        pendingWindowState = CaptureWindowState() ?? pendingWindowState;
        windowStateTimer?.Stop(); FlushWindowState();
    }
    void FlushWindowState()
    {
        if (WindowStatePath != null && pendingWindowState is { } state)
        {
            if (WindowStateStore.Save(WindowStatePath, state)) pendingWindowState = null;
        }
    }
    [StructLayout(LayoutKind.Sequential)] struct PlacementPoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct PlacementRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct WindowPlacement
    {
        public uint Length, Flags, ShowCommand;
        public PlacementPoint MinPosition, MaxPosition;
        public PlacementRect Normal;
    }
    [DllImport("user32.dll")] static extern bool GetWindowPlacement(IntPtr hwnd, ref WindowPlacement placement);
}
