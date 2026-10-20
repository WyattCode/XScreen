using XScreen.App;

static class RecoveryChecks
{
    internal record Result(string Name, bool Passed, string? Error);
    public static List<Result> Evaluate()
    {
        var results = new List<Result>();
        void Case(string name, Func<bool> test)
        { try { results.Add(new(name, test(), null)); } catch (Exception ex) { results.Add(new(name, false, ex.Message)); } }
        Case("A later display notification recovers an initial add failure without requiring a new DPI", () =>
        {
            var p = new FakeShell { AddFails = true, ModifyFails = true };
            using var icon = new NotificationIcon(p, (IntPtr)1, 0x8001, "tray.ico", _ => { });
            p.AddFails = false; p.ModifyFails = false; icon.RefreshForDisplay();
            return icon.Available && p.Current != 0;
        });
        Case("Explorer recovery reuses an existing owned image when its source file is temporarily unreadable", () =>
        {
            var p = new FakeShell();
            using var icon = new NotificationIcon(p, (IntPtr)1, 0x8001, "tray.ico", _ => { });
            int retained = p.Current; p.LoadFails = true; icon.Recreate();
            return icon.Available && p.Current == retained && !p.Destroyed.Contains((IntPtr)retained);
        });
        Case("A thrown add operation cannot leak its newly allocated HICON", () =>
        {
            var p = new FakeShell { ThrowOnNotify = 0 }; NotificationIcon? icon = null;
            try { icon = new(p, (IntPtr)1, 0x8001, "tray.ico", _ => { }); }
            catch (InvalidOperationException) { }
            finally { icon?.Dispose(); }
            return p.LiveOwned.Count == 0;
        });
        Case("A thrown delete operation still releases the owned image and marks the service unavailable", () =>
        {
            var p = new FakeShell(); var icon = new NotificationIcon(p, (IntPtr)1, 0x8001, "tray.ico", _ => { });
            p.ThrowOnNotify = 2;
            try { icon.Dispose(); } catch (InvalidOperationException) { }
            return !icon.Available && p.LiveOwned.Count == 0;
        });
        Case("Duplicate add failure falls back to modifying the already registered icon", () =>
        {
            var p = new FakeShell(); using var icon = new NotificationIcon(p, (IntPtr)1, 0x8001, "tray.ico", _ => { });
            int old = p.Current; p.AddFails = true; icon.Recreate();
            return icon.Available && p.Current != old && p.Events.Last().Message == 4;
        });
        Case("Recovery timer remains scheduled across repeated failures and stops on success", () =>
        {
            var p = new FakeShell { AddFails = true, ModifyFails = true };
            using var icon = new NotificationIcon(p, (IntPtr)1, 0x8001, "tray.ico", _ => { });
            bool started = p.RecoveryEnabled && p.TimerChanges.SequenceEqual(new[] { true });
            for (int i = 0; i < 8; i++) icon.Recover();
            bool stable = p.RecoveryEnabled && p.TimerChanges.Count == 1 && p.LiveOwned.Count == 0;
            p.AddFails = false; p.ModifyFails = false; icon.Recover();
            int calls = p.Events.Count; icon.Recover();
            return started && stable && icon.Available && !p.RecoveryEnabled && p.TimerChanges.SequenceEqual(new[] { true, false }) && p.Events.Count == calls;
        });
        Case("Disposal cancels a pending recovery timer and ignores queued timer ticks", () =>
        {
            var p = new FakeShell { AddFails = true, ModifyFails = true };
            var icon = new NotificationIcon(p, (IntPtr)1, 0x8001, "tray.ico", _ => { });
            icon.Dispose(); int calls = p.Events.Count, loads = p.Loads.Count;
            p.AddFails = false; p.ModifyFails = false;
            for (int i = 0; i < 8; i++) icon.Recover();
            return !p.RecoveryEnabled && !icon.Available && p.Events.Count == calls && p.Loads.Count == loads && p.LiveOwned.Count == 0;
        });
        Case("Uncertain registration is still explicitly deleted during disposal", () =>
        {
            var p = new FakeShell(); var icon = new NotificationIcon(p, (IntPtr)1, 0x8001, "tray.ico", _ => { });
            p.AddFails = true; p.ModifyFails = true; icon.Recreate();
            bool unavailable = !icon.Available;
            icon.Dispose();
            return unavailable && p.Events.Count(e => e.Message == 2) == 1 && p.Current == 0 && p.LiveOwned.Count == 0;
        });
        Case("Reentrant exit while adding cannot resurrect the icon or leak either image", () =>
        {
            var p = new FakeShell(); var icon = new NotificationIcon(p, (IntPtr)1, 0x8001, "tray.ico", _ => { });
            p.AfterNotify = message => { if (message == 0) icon.Dispose(); };
            int before = p.Events.Count; icon.Recreate();
            return !icon.Available && p.Current == 0 && p.LiveOwned.Count == 0 && !p.RecoveryEnabled
                && p.Events.Skip(before).Select(e => e.Message).SequenceEqual(new uint[] { 0, 2 }) && p.Destroyed.Distinct().Count() == p.Destroyed.Count;
        });
        Case("Reentrant exit during DPI modification releases both pending and previous images", () =>
        {
            var p = new FakeShell(); var icon = new NotificationIcon(p, (IntPtr)1, 0x8001, "tray.ico", _ => { });
            p.Pixels = 24; p.AfterNotify = message => { if (message == 1) icon.Dispose(); };
            icon.RefreshForDisplay();
            return !icon.Available && p.Current == 0 && p.LiveOwned.Count == 0 && p.Destroyed.Distinct().Count() == p.Destroyed.Count;
        });
        Case("Nested recreate callbacks do not start a second registration operation", () =>
        {
            var p = new FakeShell(); using var icon = new NotificationIcon(p, (IntPtr)1, 0x8001, "tray.ico", _ => { });
            p.AfterNotify = message => { if (message == 0) icon.Recreate(); };
            int before = p.Events.Count; icon.Recreate();
            return icon.Available && p.LiveOwned.Count == 1 && p.Events.Skip(before).Select(e => e.Message).SequenceEqual(new uint[] { 0, 4 });
        });
        Case("Throwing version negotiation preserves a usable legacy registration", () =>
        {
            var p = new FakeShell { ThrowOnNotify = 4 };
            using var icon = new NotificationIcon(p, (IntPtr)1, 0x8001, "tray.ico", _ => { });
            return icon.Available && p.Current != 0 && p.LiveOwned.Count == 1 && !p.RecoveryEnabled;
        });
        Case("Throwing DPI modification releases its pending image and schedules recovery of the retained image", () =>
        {
            var p = new FakeShell(); using var icon = new NotificationIcon(p, (IntPtr)1, 0x8001, "tray.ico", _ => { });
            int retained = p.Current; p.Pixels = 24; p.ThrowOnNotify = 1; icon.RefreshForDisplay();
            return !icon.Available && p.RecoveryEnabled && p.Current == retained && p.LiveOwned.Count == 1 && !p.Destroyed.Contains((IntPtr)retained);
        });
        Case("A failing log sink cannot interrupt icon or recovery timer cleanup", () =>
        {
            var p = new FakeShell { ThrowOnNotify = 0 };
            var icon = new NotificationIcon(p, (IntPtr)1, 0x8001, "tray.ico", _ => throw new IOException("Unavailable log sink"));
            icon.Dispose();
            return !icon.Available && !p.RecoveryEnabled && p.LiveOwned.Count == 0;
        });
        Case("Recovery from a shared fallback never destroys the borrowed system handle", () =>
        {
            var p = new FakeShell { LoadFails = true }; var icon = new NotificationIcon(p, (IntPtr)1, 0x8001, "tray.ico", _ => { });
            p.LoadFails = false; icon.Recreate(); icon.Dispose();
            return p.LiveOwned.Count == 0 && !p.Destroyed.Contains((IntPtr)9000);
        });
        Case("DPI refresh recreates a registration lost without an Explorer notification", () =>
        {
            var p = new FakeShell(); using var icon = new NotificationIcon(p, (IntPtr)1, 0x8001, "tray.ico", _ => { });
            p.Current = 0; p.Pixels = 24; icon.RefreshForDisplay();
            return icon.Available && p.Current != 0 && p.Events.Last().Message == 4 && !p.RecoveryEnabled;
        });
        Case("Failed DPI modify and add mark the registration uncertain and start recovery", () =>
        {
            var p = new FakeShell(); using var icon = new NotificationIcon(p, (IntPtr)1, 0x8001, "tray.ico", _ => { });
            p.Pixels = 24; p.ModifyFails = true; p.AddFails = true; icon.RefreshForDisplay();
            return !icon.Available && p.RecoveryEnabled && p.LiveOwned.Count == 1;
        });
        return results;
    }
}
