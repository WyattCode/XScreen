using System.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using XScreen.App;
using XScreen.Core;

namespace XScreen.Review;

public sealed partial class ReviewApplication
{
    async Task RunHotkeys()
    {
        try
        {
            await Until(() => fake.InitialReadEntered.IsSet); fake.InitialReadRelease.Set(); await Idle();
            var defaults = HotkeySettings.Defaults();
            var reg = new FakeHotkeyRegistrar(); var called = new List<HotkeyAction>();
            using var service = new GlobalHotkeys(reg, called.Add);
            Check(defaults.Error == null && defaults.Bindings.All(b => b.Chord!.Modifiers == 7) && defaults.Bindings.Select(b => b.Chord).Distinct().Count() == 6, "Six valid distinct defaults require Ctrl Alt Shift");
            Check(service.TryApply(defaults) == null && reg.Active.Count == 6, "All six shortcuts register without any monitor I/O");
            void Fire(GlobalHotkeys host, FakeHotkeyRegistrar registrar, HotkeyAction action)
            {
                var chord = host.Settings.Bindings.Single(b => b.Action == action).Chord!;
                var id = registrar.Active.Single(p => p.Value == chord).Key; host.Dispatch(id, chord.Modifiers, chord.Key);
            }
            foreach (var a in Enum.GetValues<HotkeyAction>()) Fire(service, reg, a);
            Check(called.SequenceEqual(Enum.GetValues<HotkeyAction>()), "Registered ID plus exact chord dispatches its intended action");
            service.Dispatch(9999, 7, 0x26); service.Dispatch(reg.Active.Keys.First(), 0, 0x26); service.Dispatch(reg.Active.Keys.First(), 7, 0x41);
            Check(called.Count == 6, "Unknown IDs, wrong modifiers and wrong keys are rejected");
            var duplicate = defaults with { Bindings = defaults.Bindings.Select(b => b with { Chord = new(7, 0x41) }).ToArray() };
            Check(service.TryApply(duplicate) != null && reg.Active.Count == 6, "Duplicate shortcuts reject the whole draft and preserve registrations");
            var invalid = defaults with { Bindings = defaults.Bindings.Take(5).ToArray() };
            Check(service.TryApply(invalid) != null && service.TryApply(new(true, null!)) != null, "Truncated and null binding lists are rejected");
            Check(!new HotkeyChord(0, 0x41).Valid && !new HotkeyChord(8, 0x41).Valid && !new HotkeyChord(7, 0x7B).Valid && !new HotkeyChord(7, 0x10).Valid, "Bare keys, Win keys, F12 and modifier-only chords are rejected");
            var custom = defaults with { Bindings = defaults.Bindings.Select((b, i) => b with { Chord = new(3, (uint)(0x41 + i)) }).ToArray() };
            reg.FailKey = 0x43;
            Check(service.TryApply(custom) != null && reg.Active.Count == 6 && reg.Active.Values.All(c => c.Modifiers == 7), "OS conflict rolls back earlier acquisitions and retains every old shortcut");
            reg.FailKey = null;
            Check(service.TryApply(custom, _ => false) != null && service.Settings.Enabled == defaults.Enabled && service.Settings.Bindings.SequenceEqual(defaults.Bindings) && reg.Active.Count == 6 && reg.Active.Values.All(c => c.Modifiers == 7), "Persistence failure rolls back acquired shortcuts and leaves runtime settings unchanged");
            int staleId = reg.Active.Keys.First(); var staleChord = reg.Active[staleId];
            Check(service.TryApply(custom) == null && reg.Active.Count == 6, "Successful custom mapping releases every replaced shortcut");
            service.Dispatch(staleId, staleChord.Modifiers, staleChord.Key);
            Check(called.Count == 6, "A delayed message from a replaced shortcut cannot adjust anything");
            service.Pause(); Check(reg.Active.Count == 0, "Opening configuration releases global registrations for safe capture");
            Check(service.TryApply(custom) == null && reg.Active.Count == 6 && !reg.Active.ContainsKey(staleId), "Resuming uses fresh IDs rather than reviving stale messages");
            Check(service.TryApply(custom with { Enabled = false }) == null && reg.Active.Count == 0, "Global disable unregisters all six bindings");
            service.Dispose(); Check(service.TryApply(defaults) != null && reg.Disposed, "Disposal releases the registrar and cannot be restarted");
            string store = ReviewProgram.ReportPath + ".hotkeys.json";
            Check(HotkeyStore.Load(store).Bindings.SequenceEqual(defaults.Bindings), "A missing user-local file supplies documented defaults");
            Check(HotkeyStore.Save(store, custom) && HotkeyStore.Load(store).Bindings.SequenceEqual(custom.Bindings), "Custom shortcuts round-trip through isolated atomic storage");
            File.WriteAllText(store, "{broken");
            Check(!HotkeyStore.Load(store).Enabled && HotkeyStore.Load(store).Bindings.All(b => b.Chord == null), "Corrupt existing preferences disable hotkeys instead of silently assigning defaults");
            Check(!HotkeyStore.Save(store + "/blocked.json", defaults) && Directory.GetFiles(Path.GetDirectoryName(store)!, "*.tmp").Length == 0, "Storage failure leaves no temporary file");

            var main = (XScreen.App.MainWindow)window!;
            main.HotkeyPath = store; File.Delete(store);
            var appRegistrar = new FakeHotkeyRegistrar(); main.InitializeHotkeys(appRegistrar);
            var appHotkeys = (GlobalHotkeys)Get(main, "hotkeys");
            var root = (Grid)Get(main, "root"); int reads = fake.Reads; int before = fake.Writes.Count;
            Task settingsTask = (Task)Call(main, "ShowHotkeySettingsAsync")!;
            await Until(() => (object?)Get(main, "hotkeyView") != null);
            var view = (HotkeySettingsView)Get(main, "hotkeyView"); await Task.Delay(200);
            Check(appRegistrar.Active.Count == 0 && (bool)Get(main, "hotkeyDialogOpen") && fake.Reads == reads && fake.Writes.Count == before, "Settings opens from cached data, suspends bindings and performs zero monitor I/O");
            main.OnGlobalHotkey(HotkeyAction.VolumeUp);
            Check(fake.Writes.Count == before, "Direct late dispatch cannot adjust while configuration owns focus");
            view.Capture(HotkeyAction.VolumeUp, 0x41, 3);
            Check(view.Draft().Bindings.Single(b => b.Action == HotkeyAction.VolumeUp).Chord == new HotkeyChord(3, 0x41), "Read-only capture fields accept a supported custom combination");
            view.Capture(HotkeyAction.VolumeUp, 0x7B, 7);
            Check(view.Draft().Bindings.Single(b => b.Action == HotkeyAction.VolumeUp).Chord == new HotkeyChord(3, 0x41) && ((InfoBar)Get(view, "feedback")).IsOpen, "Invalid capture preserves the prior draft and shows an inline error");
            await (Task)Call(main, "Capture", ReviewProgram.ReportPath + ".settings-dark.png")!;
            // Capture the dialog itself because root captures deliberately exclude native popups.
            await CaptureReset((ContentDialog)Get(view, "dialog"), ReviewProgram.ReportPath + ".dialog-dark.png");
            var fields = ((IDictionary)Get(view, "fields")).Values.Cast<TextBox>().ToArray();
            Check(fields.Length == 6 && fields.All(f => f.ActualHeight >= 36 && f.ActualWidth >= 160), "All six native capture fields have usable dimensions");
            view.Dismiss(); await settingsTask;
            Check(!File.Exists(store) && appHotkeys.Settings.Bindings.SequenceEqual(defaults.Bindings) && appRegistrar.Active.Count == 6 && fake.Reads == reads && fake.Writes.Count == before,
                "Cancel discards edited shortcuts, resumes old bindings and does not write settings or monitor");
            // Test the same Save callback used by ContentDialog PrimaryButtonClick.
            settingsTask = (Task)Call(main, "ShowHotkeySettingsAsync")!; await Task.Delay(150); view = (HotkeySettingsView)Get(main, "hotkeyView");
            view.LoadDefaults();
            var dialog = (ContentDialog)Get(view, "dialog"); ((ComboBox)Get(main, "appearance")).SelectedIndex = 1;
            await Task.Delay(150); await CaptureReset(dialog, ReviewProgram.ReportPath + ".dialog-light.png");
            Check(view.Draft().Bindings.SequenceEqual(defaults.Bindings), "Restore defaults edits only the draft");
            var restore = Descendants(dialog).OfType<Button>().Single(b => b.Content as string == "恢复默认快捷键");
            var clearVolume = Descendants(dialog).OfType<Button>().Single(b => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(b) == "清除显示器音量降低快捷键");
            ClickOsd(clearVolume);
            Check(view.Draft().Bindings.Single(b => b.Action == HotkeyAction.VolumeDown).Chord == null && fake.Writes.Count == before, "Clear removes one draft binding without applying monitor settings");
            ClickOsd(restore);
            var scrollHost = Descendants((Grid)dialog.Content).OfType<ScrollViewer>().First();
            Check(scrollHost.ScrollableWidth < 1 && scrollHost.ScrollableHeight < 1 && view.Draft().Bindings.SequenceEqual(defaults.Bindings), "Normal settings layout fits six fields, defaults and help without clipping or scrolling");
            view.Capture(HotkeyAction.VolumeUp, 0x26, 7); ClickOsd(ConfirmReset(dialog)); await Task.Delay(70);
            Check(!settingsTask.IsCompleted && ((InfoBar)Get(view, "feedback")).IsOpen && !File.Exists(store), "Saving a duplicate stays in the dialog and never persists the invalid draft");
            var errorBar = (InfoBar)Get(view, "feedback");
            var errorBounds = errorBar.TransformToVisual(dialog).TransformPoint(new(0, 0));
            Check(errorBar.ActualHeight >= 36 && errorBounds.Y >= 0 && errorBounds.Y + errorBar.ActualHeight <= dialog.ActualHeight && errorBounds.Y < scrollHost.TransformToVisual(dialog).TransformPoint(new(0, 0)).Y,
                "Validation errors stay above the scroll viewport and cannot be clipped at its bottom");
            view.Capture(HotkeyAction.VolumeUp, 0x41, 3); ClickOsd(ConfirmReset(dialog)); await settingsTask;
            Check(HotkeyStore.Load(store).Bindings.Single(b => b.Action == HotkeyAction.VolumeUp).Chord == new HotkeyChord(3, 0x41)
                && appHotkeys.Settings.Bindings.Single(b => b.Action == HotkeyAction.VolumeUp).Chord == new HotkeyChord(3, 0x41)
                && appRegistrar.Active.Count == 6 && fake.Reads == reads && fake.Writes.Count == before, "Saving a valid draft persists custom binding and activates it without monitor I/O");

            // A real native message/subclass/registration round on ONLY the fake-transport window.
            using (var nativeReg = new NativeHotkeyRegistrar(WinRT.Interop.WindowNative.GetWindowHandle(main)))
            using (var nativeService = new GlobalHotkeys(nativeReg, called.Add))
            {
                nativeReg.Pressed = nativeService.Dispatch;
                var nativeSettings = new HotkeySettings(true, Enum.GetValues<HotkeyAction>().Select(a => new HotkeyBinding(a, a == HotkeyAction.SharpnessUp ? new(7, 0x86) : null)).ToArray());
                string? error = nativeService.TryApply(nativeSettings);
                Check(error == null, "Native RegisterHotKey accepts the isolated test combination on the fake window", error);
                int count = called.Count;
                PostMessage(WinRT.Interop.WindowNative.GetWindowHandle(main), 0x312, (IntPtr)101, (IntPtr)((0x86 << 16) | 7)); await Task.Delay(60);
                Check(called.Count == count + 1 && called.Last() == HotkeyAction.SharpnessUp && fake.Writes.Count == before, "WM_HOTKEY is decoded by the delivered native subclass without monitor writes");
                nativeService.Pause(); PostMessage(WinRT.Interop.WindowNative.GetWindowHandle(main), 0x312, (IntPtr)101, (IntPtr)((0x86 << 16) | 7)); await Task.Delay(60);
                Check(called.Count == count + 1, "Native stale messages after unregister are ignored");
            }

            var rows = (IDictionary)Get(main, "rows"); main.AppWindow.Hide();
            foreach (var action in Enum.GetValues<HotkeyAction>())
            {
                var (code, delta) = HotkeySettings.Target(action); var row = rows[code]!;
                uint old = (uint)Get(row, "Desired"); uint expected = (uint)Math.Clamp((int)old + delta, 0, 100);
                int writes = fake.Writes.Count; Fire(appHotkeys, appRegistrar, action); await Idle();
                Check((uint)Get(row, "Current") == expected && fake.Writes.Count == writes + (expected == old ? 0 : 1) && !main.AppWindow.IsVisible, "Hidden main window dispatches " + action + " with step10 through normal readback");
            }
            int gatedWrites = fake.Writes.Count, gatedReads = fake.Reads;
            foreach (string flag in new[] { "refreshing", "resetDialogOpen", "resetting", "hotkeyDialogOpen", "exiting" })
            {
                main.GetType().GetField(flag, Members)!.SetValue(main, true);
                foreach (var action in Enum.GetValues<HotkeyAction>()) Fire(appHotkeys, appRegistrar, action);
                main.GetType().GetField(flag, Members)!.SetValue(main, false);
            }
            var chosen = main.GetType().GetField("selected", Members)!.GetValue(main); main.GetType().GetField("selected", Members)!.SetValue(main, null);
            foreach (var action in Enum.GetValues<HotkeyAction>()) Fire(appHotkeys, appRegistrar, action);
            main.GetType().GetField("selected", Members)!.SetValue(main, chosen);
            await Task.Delay(80);
            Check(fake.Writes.Count == gatedWrites && fake.Reads == gatedReads, "Read, reset review, reset batch, configuration, exit and disconnected gates drop hotkeys without I/O or deferred replay");
            var volume = rows[(byte)0x62]!; int rapidStart = fake.Writes.Count;
            for (int i = 0; i < 4; i++) Fire(appHotkeys, appRegistrar, HotkeyAction.VolumeUp);
            Check((uint)Get(volume, "Desired") == 70, "Rapid presses accumulate on desired value rather than jumping back to confirmed value");
            await Idle(); Check((uint)Get(volume, "Current") == 70 && fake.Writes.Count - rapidStart <= 2, "Rapid presses coalesce first and latest targets in the serialized pipeline");
            for (int i = 0; i < 6; i++) Fire(appHotkeys, appRegistrar, HotkeyAction.VolumeUp); await Idle();
            before = fake.Writes.Count; Fire(appHotkeys, appRegistrar, HotkeyAction.VolumeUp); await Task.Delay(50);
            Check((uint)Get(volume, "Current") == 100 && fake.Writes.Count == before, "Upper boundary remains100 and generates no redundant write");
            for (int i = 0; i < 12; i++) Fire(appHotkeys, appRegistrar, HotkeyAction.VolumeDown); await Idle();
            before = fake.Writes.Count; Fire(appHotkeys, appRegistrar, HotkeyAction.VolumeDown); await Task.Delay(50);
            Check((uint)Get(volume, "Current") == 0 && fake.Writes.Count == before, "Lower boundary remains0 without unsigned wraparound");
            Call(main, "ShowWindow");
            var valueEditor = (Microsoft.UI.Xaml.Controls.Flyout)Get(volume, "editor");
            valueEditor.ShowAt((Button)Get(volume, "valueButton")); await Task.Delay(120); before = fake.Writes.Count;
            Fire(appHotkeys, appRegistrar, HotkeyAction.VolumeUp); await Task.Delay(60);
            Check(fake.Writes.Count == before, "A shortcut does not overwrite the target channel's open numeric editor"); valueEditor.Hide();
            fake.FailNextBrightnessRead = true; Fire(appHotkeys, appRegistrar, HotkeyAction.BrightnessDown); await Idle();
            before = fake.Writes.Count; reads = fake.Reads;
            foreach (var action in Enum.GetValues<HotkeyAction>()) Fire(appHotkeys, appRegistrar, action);
            await Task.Delay(100);
            Check((bool)Get(main, "halted") && fake.Writes.Count == before && fake.Reads == reads, "A halted session ignores every global hotkey without retrying or auto-reading");
            before = fake.Writes.Count; await (Task)Call(main, "ExitAsync")!;
            main.OnGlobalHotkey((HotkeyAction)999);
            Check(appRegistrar.Active.Count == 0 && appRegistrar.Disposed && fake.Writes.Count == before, "Shutdown releases all shortcuts and invalid actions remain inert");
            Check(WindowsMonitorTransport.NativeReadAttempts == 0 && WindowsMonitorTransport.NativeWriteAttempts == 0, "Entire global-hotkey regression has zero real monitor reads and writes");
        }
        catch (Exception ex) { failures++; Environment.ExitCode = 1; results.Add(new { harnessError = ex.ToString() }); }
        finally { fake.InitialReadRelease.Set(); WriteReport(); await (Task)Call(window!, "ExitAsync")!; }
    }
}

sealed class FakeHotkeyRegistrar : IHotkeyRegistrar
{
    public readonly Dictionary<int, HotkeyChord> Active = [];
    public uint? FailKey;
    public bool Disposed;
    public bool Register(int id, HotkeyChord chord) { if (Disposed || chord.Key == FailKey || Active.Values.Contains(chord)) return false; Active.Add(id, chord); return true; }
    public void Unregister(int id) => Active.Remove(id);
    public void Dispose() { Disposed = true; Active.Clear(); }
}
