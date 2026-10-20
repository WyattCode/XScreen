using System.Runtime.InteropServices;

namespace XScreen.App;

// No window, monitor transport or hardware APIs: native Shell calls are supplied
// by NativeWindow, and lifecycle/ownership can be exercised entirely offline.
interface INotificationIconPlatform
{
    int SmallIconPixels { get; }
    IntPtr Load(string path, int pixels);
    IntPtr Fallback(); // Shared system handle; never destroy it.
    bool Notify(uint message, ref NotificationIconData data);
    void Destroy(IntPtr image);
    bool SetRecoveryTimer(bool enabled);
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
struct NotificationIconData
{
    public uint Size; public IntPtr Window; public uint Id, Flags, Callback; public IntPtr Icon;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
    public uint State, StateMask;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
    public uint Version;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
    public uint InfoFlags; public Guid Guid; public IntPtr BalloonIcon;
}

sealed class NotificationIcon : IDisposable
{
    internal const uint MessageFlag = 0x1, IconFlag = 0x2, TipFlag = 0x4, ShowTipFlag = 0x80;
    readonly INotificationIconPlatform platform;
    readonly Action<string> log;
    readonly string path;
    NotificationIconData data;
    bool owned, disposed, updating, registrationAttempted, recoveryScheduled;
    int pixels;
    public bool Available { get; private set; }

    public NotificationIcon(INotificationIconPlatform platform, IntPtr window, uint callback, string path, Action<string> log)
    {
        this.platform = platform; this.path = path; this.log = log;
        data = new() { Size = (uint)Marshal.SizeOf<NotificationIconData>(), Window = window, Id = 1,
            Callback = callback, Flags = MessageFlag | IconFlag | TipFlag | ShowTipFlag,
            Tip = "XScreen", Info = "", InfoTitle = "" };
        Recreate();
    }

    public void Recreate()
    {
        if (disposed || updating) return;
        updating = true; Available = false;
        IntPtr next = IntPtr.Zero; bool releaseNext = false;
        try
        {
            int nextPixels = Math.Clamp(platform.SmallIconPixels, 16, 256);
            next = platform.Load(path, nextPixels); releaseNext = next != IntPtr.Zero;
            bool nextOwned = releaseNext;
            if (next == IntPtr.Zero && data.Icon != IntPtr.Zero)
            {
                next = data.Icon; nextOwned = owned; nextPixels = pixels;
                Report("Tray icon load failed; reusing the retained image.");
            }
            if (next == IntPtr.Zero) { next = platform.Fallback(); Report("Tray icon load failed; using shared fallback."); }
            if (next == IntPtr.Zero) { Report("No tray icon image is available."); return; }
            if (disposed) return;
            var candidate = data; candidate.Icon = next; candidate.Version = 0;
            registrationAttempted = true;
            // A duplicate TaskbarCreated may leave an existing registration.
            // Modify that registration before deciding the tray is unavailable.
            if (!platform.Notify(0, ref candidate) && !disposed && !platform.Notify(1, ref candidate))
            { Report("Shell tray add/modify failed."); return; }
            if (disposed) return;
            Adopt(candidate, nextOwned, nextPixels); releaseNext = false;
            NegotiateVersion();
        }
        catch (Exception ex) { Report("Tray registration failed: " + ex.Message); }
        finally { if (releaseNext) Release(next); updating = false; SyncRecoveryTimer(); }
    }

    public void RefreshForDisplay()
    {
        if (disposed || updating) return;
        if (!Available) { Recreate(); return; }
        updating = true;
        IntPtr next = IntPtr.Zero;
        try
        {
            int nextPixels = Math.Clamp(platform.SmallIconPixels, 16, 256);
            if (nextPixels == pixels) return;
            next = platform.Load(path, nextPixels);
            if (next == IntPtr.Zero) { Report("DPI icon load failed; retaining the current image."); return; }
            if (disposed) return;
            var candidate = data; candidate.Icon = next;
            bool added = false;
            if (!platform.Notify(1, ref candidate))
            {
                if (disposed) return;
                candidate.Version = 0;
                registrationAttempted = true;
                added = platform.Notify(0, ref candidate);
                if (!added) { Available = false; Report("DPI add/modify failed; retaining the image for recovery."); return; }
            }
            if (disposed) return;
            Adopt(candidate, true, nextPixels); next = IntPtr.Zero;
            if (added) NegotiateVersion();
        }
        catch (Exception ex) { Available = false; Report("DPI tray update failed: " + ex.Message); }
        finally { if (next != IntPtr.Zero) Release(next); updating = false; SyncRecoveryTimer(); }
    }

    void NegotiateVersion()
    {
        if (disposed) return;
        var candidate = data; candidate.Version = 4;
        bool version4 = false;
        try { version4 = platform.Notify(4, ref candidate); }
        catch (Exception ex) { Report("Shell version negotiation failed: " + ex.Message); }
        if (!disposed) data.Version = version4 ? 4u : 0u;
        if (!version4) Report("Shell version 4 unavailable; retaining legacy notifications.");
    }

    void Adopt(NotificationIconData candidate, bool nextOwned, int nextPixels)
    {
        var previous = data.Icon; bool previousOwned = owned;
        data = candidate; owned = nextOwned; pixels = nextPixels; Available = true;
        if (previousOwned && previous != candidate.Icon) Release(previous);
    }

    public void Recover() { if (!disposed && !Available) Recreate(); }

    void SyncRecoveryTimer()
    {
        bool needed = !disposed && !Available;
        if (needed == recoveryScheduled) return;
        try
        {
            if (platform.SetRecoveryTimer(needed)) recoveryScheduled = needed;
            else Report("Could not update the Shell recovery timer.");
        }
        catch (Exception ex) { Report("Shell recovery timer failed: " + ex.Message); }
    }

    void Release(IntPtr image)
    { try { platform.Destroy(image); } catch (Exception ex) { Report("Icon release failed: " + ex.Message); } }

    void Report(string message)
    { try { log(message); } catch { /* Diagnostics must not interrupt resource cleanup. */ } }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true; Available = false; SyncRecoveryTimer();
        try { if (registrationAttempted) platform.Notify(2, ref data); }
        catch (Exception ex) { Report("Shell tray delete failed: " + ex.Message); }
        finally
        {
            var previous = data.Icon; bool previousOwned = owned;
            owned = false; data.Icon = IntPtr.Zero;
            if (previousOwned) Release(previous);
        }
    }
}
