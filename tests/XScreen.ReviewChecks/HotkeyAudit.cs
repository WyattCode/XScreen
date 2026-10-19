using System.Collections;
using Microsoft.UI.Xaml.Controls;
using XScreen.App;
using XScreen.Core;

namespace XScreen.Review;

public sealed partial class ReviewApplication
{
    async Task RunHotkeyAudit()
    {
        try
        {
            await Until(() => fake.InitialReadEntered.IsSet); fake.InitialReadRelease.Set(); await Idle();
            var main = (XScreen.App.MainWindow)window!;
            var defaults = HotkeySettings.Defaults();
            string directoryPath = ReviewProgram.ReportPath + ".directory"; Directory.CreateDirectory(directoryPath);
            Check(!HotkeyStore.Load(directoryPath).Enabled, "An existing directory at the preference path cannot silently enable default global shortcuts");
            var registrar = new FakeHotkeyRegistrar { FailKey = 0x26 };
            main.HotkeyPath = ReviewProgram.ReportPath + ".preferences.json";
            main.InitializeHotkeys(registrar);
            int writes = fake.Writes.Count, reads = fake.Reads;
            Check(registrar.Active.Count == 0 && ((InfoBar)Get(main, "notice")).IsOpen, "One startup OS conflict releases the entire shortcut group and reports the conflict");
            await (Task)Call(main, "RefreshAsync", true)!;
            Check(((InfoBar)Get(main, "notice")).IsOpen && ((InfoBar)Get(main, "notice")).Title.Contains("快捷键"), "A successful monitor refresh cannot hide an unresolved shortcut conflict");
            registrar.FailKey = null;
            var hotkeys = (GlobalHotkeys)Get(main, "hotkeys");
            Check(hotkeys.TryApply(defaults) == null && registrar.Active.Count == 6, "A released conflict can register all six shortcuts again");
            await (Task)Call(main, "RefreshAsync", true)!;
            Check(!((InfoBar)Get(main, "notice")).IsOpen, "A resolved shortcut conflict no longer leaves a stale warning after refresh");
            // Use only own fake-transport windows. No physical key input is generated.
            var probeWindow = new Microsoft.UI.Xaml.Window { Title = "XScreen offline hotkey registration" };
            try
            {
                using var ownerA = new NativeHotkeyRegistrar(WinRT.Interop.WindowNative.GetWindowHandle(main));
                using var ownerB = new NativeHotkeyRegistrar(WinRT.Interop.WindowNative.GetWindowHandle(probeWindow));
                var chord = new HotkeyChord(7, 0x86);
                Check(ownerA.Register(9001, chord) && !ownerB.Register(9002, chord), "Actual OS registration detects a combination already held by another window");
                ownerA.Dispose();
                Check(ownerB.Register(9002, chord), "Disposal releases the OS combination so a different window can acquire it");
                ownerB.Dispose();
                using var ownerC = new NativeHotkeyRegistrar(WinRT.Interop.WindowNative.GetWindowHandle(main));
                Check(ownerC.Register(9003, chord), "Repeated register, dispose and reacquire leaves no leaked system hotkeys");
            }
            finally { probeWindow.Close(); }
            void Fire(HotkeyAction action)
            {
                var chord = hotkeys.Settings.Bindings.Single(b => b.Action == action).Chord!;
                hotkeys.Dispatch(registrar.Active.Single(p => p.Value == chord).Key, chord.Modifiers, chord.Key);
            }
            var rows = (IDictionary)Get(main, "rows"); var volume = rows[(byte)0x62]!;
            var editor = (Flyout)Get(volume, "editor"); editor.ShowAt((Button)Get(volume, "valueButton")); await Task.Delay(80);
            window!.AppWindow.Hide(); await Task.Delay(80);
            Fire(HotkeyAction.VolumeUp); await Idle();
            Check((uint)Get(volume, "Current") == 40 && !main.AppWindow.IsVisible, "Hiding an open numeric editor cannot disable tray-hidden global adjustment");
            editor.Hide();
            Call(volume, "Request", 40d); await Idle();
            // Hold one fake write while dozens of real handler callbacks arrive.
            fake.WriteEntered = new(); fake.WriteGate = new();
            Fire(HotkeyAction.VolumeUp); await Until(() => fake.WriteEntered.IsSet);
            for (int i = 0; i < 50; i++) { Fire(HotkeyAction.VolumeUp); Fire(HotkeyAction.VolumeDown); }
            Fire(HotkeyAction.VolumeDown);
            Check((uint)Get(volume, "Desired") == 40, "Alternating key bursts preserve the final intended target while a prior write is pending");
            fake.WriteGate.Set(); await Idle();
            Check((uint)Get(volume, "Current") == 40 && fake.LiveConnections == 0, "Alternating key bursts drain serially and leave no open monitor connections");
            fake.WriteEntered = new(); fake.WriteGate = new();
            Fire(HotkeyAction.VolumeUp); await Until(() => fake.WriteEntered.IsSet);
            Fire(HotkeyAction.VolumeUp); Fire(HotkeyAction.SharpnessUp);
            var old = registrar.Active.ToArray(); int beforeExit = fake.Writes.Count;
            var exit = (Task)Call(main, "ExitAsync")!;
            Check(registrar.Disposed && registrar.Active.Count == 0 && !exit.IsCompleted, "Exit unregisters every shortcut immediately before waiting for the in-flight verification");
            foreach (var pair in old) hotkeys.Dispatch(pair.Key, pair.Value.Modifiers, pair.Value.Key);
            fake.WriteGate.Set(); await exit;
            Check(fake.Writes.Count == beforeExit + 1 && fake.Writes.Last() == ((byte)0x62, 50u) && fake.LiveConnections == 0,
                "Exit verifies the one in-flight setting but drops queued volume, sharpness and late hotkey messages");
            Check(WindowsMonitorTransport.NativeReadAttempts == 0 && WindowsMonitorTransport.NativeWriteAttempts == 0, "Audit uses fake monitors only and zero real monitor calls");
        }
        catch (Exception ex) { failures++; Environment.ExitCode = 1; results.Add(new { harnessError = ex.ToString() }); }
        finally { fake.WriteGate?.Set(); fake.InitialReadRelease.Set(); WriteReport(); await (Task)Call(window!, "ExitAsync")!; }
    }
}
