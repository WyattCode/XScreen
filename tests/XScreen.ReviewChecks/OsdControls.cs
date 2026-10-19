using System.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using XScreen.Core;

namespace XScreen.Review;

public sealed partial class ReviewApplication
{
    async Task RunOsdFaultName()
    {
        try
        {
            await Until(() => fake.InitialReadEntered.IsSet); fake.InitialReadRelease.Set(); await Idle();
            var advanced = Get(window!, "advanced"); var editors = (IDictionary)Get(advanced, "editors");
            var color = editors[OsdControl.ColorTemperature]!;
            var pages = (SelectorBar)Get(window!, "pages"); pages.SelectedItem = pages.Items[1];
            // Replay the reported request and failing dependency, with fake-only invalid F9.
            fake.SimulateExternalOsd(0xF9, 49);
            var choice = (ComboBox)Get(color, "choice"); choice.SelectedItem = choice.Items.Cast<ComboBoxItem>().Single(i => (uint)i.Tag == 9);
            ClickOsd((Button)Get(color, "apply")); await Until(() => !(bool)Get(advanced, "Busy")); await Idle();
            var notice = (InfoBar)Get(window!, "notice");
            Check(notice.Title == "黑色稳定器状态未能确认" && !notice.Title.Contains("色温"), "A color Apply whose F9 pre-read fails names the failed dependency in the error banner");
            Check((bool)Get(window!, "halted") && fake.Reads == 11 && fake.Writes.Count == 0 && fake.LiveConnections == 0,
                "The dependency failure stops before mutation and releases the connection");
            await (Task)Call(window!, "RefreshAsync", true)!; await Task.Delay(180);
            Check(fake.Reads == 11 && fake.Writes.Count == 0 && WindowsMonitorTransport.NativeReadAttempts == 0 && WindowsMonitorTransport.NativeWriteAttempts == 0,
                "Automatic startup loading never becomes automatic fault recovery or a hardware test");
        }
        catch (Exception ex) { failures++; Environment.ExitCode = 1; results.Add(new { harnessError = ex.ToString() }); }
        finally { fake.InitialReadRelease.Set(); WriteReport(); await (Task)Call(window!, "ExitAsync")!; }
    }
    static void ClickOsd(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    async Task RunOsdControls()
    {
        try
        {
            await Until(() => fake.InitialReadEntered.IsSet); fake.InitialReadRelease.Set(); await Idle();
            var advanced = Get(window!, "advanced"); var editors = (IDictionary)Get(advanced, "editors");
            object Editor(OsdControl c) => editors[c]!;
            Button Apply(OsdControl c) => (Button)Get(Editor(c), "apply");
            ComboBox Choice(OsdControl c) => (ComboBox)Get(Editor(c), "choice");
            TextBox Number(OsdControl c) => (TextBox)Get(Editor(c), "number");
            void Select(OsdControl c, uint raw) { var combo = Choice(c); combo.SelectedItem = combo.Items.Cast<ComboBoxItem>().Single(i => (uint)i.Tag == raw); }
            async Task Applied(OsdControl c) { if (c == OsdControl.Contrast) await (Task)Call(advanced, "ApplyAsync", c)!; else ClickOsd(Apply(c)); await Until(() => !(bool)Get(advanced, "Busy")); await Idle(); }
            int initialReads = fake.Reads;
            var pages = (SelectorBar)Get(window!, "pages"); pages.SelectedItem = pages.Items[1]; await Task.Delay(100);
            Check(fake.Reads == initialReads && fake.Writes.Count == 0 && ((ScrollViewer)Get(window!, "advancedScroll")).Visibility == Visibility.Visible,
                "Switching to the formal picture pane performs no I/O");
            Check(initialReads == 7 && OsdProtocol.WritableControls.All(c => Apply(c).IsEnabled) && !Apply(OsdControl.Contrast).IsEnabled
                && ((ComboBoxItem)Choice(OsdControl.ColorTemperature).SelectedItem).Tag is uint startupColor && startupColor == 3,
                "One automatic startup snapshot populates all four controls including color, with zero writes");
            Check(Apply(OsdControl.Contrast).Visibility == Visibility.Collapsed
                && ((Slider)Get(Editor(OsdControl.Contrast), "slider")).Visibility == Visibility.Collapsed
                && Number(OsdControl.Contrast).Visibility == Visibility.Collapsed
                && ((TextBlock)Get(Editor(OsdControl.Contrast), "current")).Text.Contains("70"),
                "Contrast shows its current value only, with no visible slider, input or Apply entry");
            int beforeDirectGuardReads = fake.Reads; bool rejectedContrast = false;
            try { await (Task)Call(window!, "WriteOsdAsync", OsdControl.Contrast, 66u)!; } catch (InvalidOperationException) { rejectedContrast = true; }
            Check(rejectedContrast && fake.Reads == beforeDirectGuardReads && fake.Writes.Count == 0 && !(bool)Get(window!, "halted"),
                "A stale direct contrast request is refused by MainWindow before busy state, I/O or fault latching");
            pages.SelectedItem = pages.Items[0]; pages.SelectedItem = pages.Items[1]; await Task.Delay(180);
            Check(fake.Reads == initialReads && fake.Writes.Count == 0, "Repeated page visits and idle time neither poll nor retry the startup read");
            fake.SimulateExternalOsd(0x14, 4); await (Task)Call(window!, "RefreshAsync", true)!;
            Check(!Apply(OsdControl.ColorTemperature).IsEnabled && Apply(OsdControl.PictureMode).IsEnabled && !(bool)Get(window!, "halted")
                && ((TextBlock)Get(Editor(OsdControl.ColorTemperature), "current")).Text.Contains("0x4"),
                "An unknown color reply is shown honestly and only its editor disables; it never writes");
            fake.SimulateExternalOsd(0x14, 3); await (Task)Call(window!, "RefreshAsync", true)!;
            initialReads = fake.Reads;
            ClickOsd((Button)Get(advanced, "read")); await Until(() => !(bool)Get(advanced, "Busy")); await Idle();
            Check(fake.Reads == initialReads + 7 && OsdProtocol.WritableControls.All(c => Apply(c).IsEnabled) && !Apply(OsdControl.Contrast).IsEnabled && fake.Writes.Count == 0, "Picture refresh synchronizes all seven fields while contrast remains read-only");
            Check(((Button)Get(window!, "reset")).IsEnabled && ((Button)Get(window!, "refresh")).IsEnabled,
                "Picture refresh completion re-enables the shared reset and refresh actions");
            Select(OsdControl.PictureMode, 30); Number(OsdControl.Contrast).Text = "67"; Number(OsdControl.BlackStabilizer).Text = "45"; await Task.Delay(100);
            Check(fake.Writes.Count == 0 && fake.Reads == initialReads + 7, "Selection, typing and slider draft synchronization never submit a setting");
            fake.SimulateModeSideEffects = true; fake.WriteEntered = new(); fake.WriteGate = new(); ClickOsd(Apply(OsdControl.PictureMode)); await Until(() => fake.WriteEntered.IsSet);
            Check(OsdProtocol.Controls.All(c => !Apply(c).IsEnabled) && !((Button)Get(advanced, "read")).IsEnabled,
                "Only one Apply may be in flight and conflicting commands disable");
            Check(!(bool)Call(window!, "CanAdjust", (byte)0x10)! && !(bool)Call(window!, "CanAdjust", (byte)0x62)!,
                "Tray quick actions cannot enqueue old-mode adjustments during a picture apply");
            await (Task)Call(advanced, "ApplyAsync", OsdControl.Contrast)!; fake.WriteGate.Set(); await Until(() => !(bool)Get(advanced, "Busy")); await Idle();
            fake.WriteGate = null;
            Check(fake.Writes.SequenceEqual(new[] { ((byte)0x15, 30u) }) && ((TextBlock)Get(window!, "mode")).Text == "FPS", "Mode selection applies exactly once and is reflected in the main status");
            Check(Number(OsdControl.Contrast).Text == "72" && Number(OsdControl.BlackStabilizer).Text == "55"
                && ((ComboBoxItem)Choice(OsdControl.ColorTemperature).SelectedItem).Tag is uint raw && raw == 9,
                "Mode-dependent actual values replace unsent drafts without replaying them");
            Check(((Button)Get(window!, "reset")).IsEnabled, "Mode Apply completion notifies the header so shared reset does not remain disabled");
            var legacyRows = (IDictionary)Get(window!, "rows");
            Check((uint)Get(legacyRows[(byte)0x10]!, "Current") == 60, "Mode side effects also refresh the legacy brightness channel");
            Select(OsdControl.ColorTemperature, 3); await Applied(OsdControl.ColorTemperature);
            Check(fake.Writes.Last() == ((byte)0x14, 3u), "Color applies the selected raw preset only after Apply");
            Check(((Button)Get(window!, "reset")).IsEnabled, "Color Apply completion re-enables shared reset after full synchronization");
            int count = fake.Writes.Count;
            string beforeInvalidBlack = Number(OsdControl.BlackStabilizer).Text;
            Number(OsdControl.Contrast).Text = "150"; await Applied(OsdControl.Contrast);
            Number(OsdControl.BlackStabilizer).Text = "49"; await Applied(OsdControl.BlackStabilizer);
            Number(OsdControl.Contrast).Text = ""; await Applied(OsdControl.Contrast);
            Check(fake.Writes.Count == count && !(bool)Get(window!, "halted"), "Out-of-range, invalid-step and empty drafts reject inline without writes or session halt");
            Select(OsdControl.PictureMode,46);
            var blackEditor=Editor(OsdControl.BlackStabilizer);
            var keyHandler=blackEditor.GetType().GetMethod("HandleNumberKey",Members);
            if (keyHandler != null) keyHandler.Invoke(blackEditor,[Windows.System.VirtualKey.Escape]);
            else Call(blackEditor,"Cancel"); // Exact pre-0.3.27 Escape handler behavior.
            Check(Number(OsdControl.BlackStabilizer).Text==beforeInvalidBlack
                && ((TextBlock)Get(advanced,"feedback")).Text.Contains("黑色稳定器草稿已取消")
                && (uint)((ComboBoxItem)Choice(OsdControl.PictureMode).SelectedItem).Tag==46u
                && fake.Writes.Count==count && !(bool)Get(window!,"halted"),
                "Escape after validation cancels only that editor, clears stale feedback and never sends a setting");
            ClickOsd((Button)Get(advanced, "cancel")); await Task.Delay(60);
            Check(Number(OsdControl.Contrast).Text == "72" && Number(OsdControl.BlackStabilizer).Text == "55" && fake.Writes.Count == count,
                "Cancel reverts only drafts and never writes a former hardware value");
            Number(OsdControl.BlackStabilizer).Text = "45"; await Applied(OsdControl.BlackStabilizer);
            Check(fake.Writes.Last() == ((byte)0xF9, 9u) && !fake.Writes.Any(w => w.Code == 0x12) && Number(OsdControl.BlackStabilizer).Text == "45",
                "Black stabilizer writes9 and displays45; contrast is never written");
            count = fake.Writes.Count; await Applied(OsdControl.BlackStabilizer);
            Check(fake.Writes.Count == count && !(bool)Get(window!, "halted"), "Unchanged Apply never duplicates the setting");
            Check(((Button)Get(window!, "reset")).IsEnabled, "A no-op Apply also restores the shared reset action");
            ((ComboBox)Get(window!, "appearance")).SelectedIndex = 1; await Task.Delay(80);
            await (Task)Call(window!, "Capture", ReviewProgram.ReportPath + ".light.png")!;
            ((ComboBox)Get(window!, "appearance")).SelectedIndex = 0; await Task.Delay(80);
            await (Task)Call(window!, "Capture", ReviewProgram.ReportPath + ".dark.png")!;
            Call(window!, "ResizeLogical", 840, 680); await Task.Delay(120);
            Check(((ScrollViewer)Get(window!, "advancedScroll")).ScrollableHeight > 0 && OsdProtocol.Controls.All(c => ((Grid)Get(Editor(c), "View")).ActualWidth <= ((Grid)Get(window!, "root")).ActualWidth),
                "The four-control pane scrolls within the minimum window without horizontal overflow");
            int beforeRestricted = fake.Reads, beforeRestrictedWrites = fake.Writes.Count;
            foreach (string restrictedTarget in new[] { "66", "76" }) { Number(OsdControl.Contrast).Text = restrictedTarget; await (Task)Call(advanced, "ApplyAsync", OsdControl.Contrast)!; }
            Check(fake.Reads == beforeRestricted && fake.Writes.Count == beforeRestrictedWrites && !(bool)Get(window!, "halted"),
                "Stale66/76 contrast callbacks perform zero I/O and cannot pause working controls");
            int beforeFailure = fake.Reads; fake.IgnoreNextWrite = true; Number(OsdControl.BlackStabilizer).Text = "40"; await Applied(OsdControl.BlackStabilizer);
            Check((bool)Get(window!, "halted") && fake.Writes.Count == count + 1 && fake.Reads == beforeFailure + 5 && OsdProtocol.Controls.All(c => !Apply(c).IsEnabled),
                "An ignored setting stops at first readback, disables all four controls and never restores or continues");
            Check(OsdProtocol.Controls.All(c => ((TextBlock)Get(Editor(c), "current")).Text.Contains("未确认")),
                "A fault invalidates the current state explicitly rather than pretending startup never read it");
            Check(((Slider)Get(Editor(OsdControl.Contrast), "slider")).Visibility == Visibility.Collapsed
                && ((Slider)Get(Editor(OsdControl.BlackStabilizer), "slider")).Visibility == Visibility.Collapsed
                && Number(OsdControl.Contrast).Text.Length == 0 && Number(OsdControl.BlackStabilizer).Text.Length == 0,
                "A failed setting clears and hides numeric drafts so the unaccepted target never looks applied");
            Check(Choice(OsdControl.PictureMode).SelectedItem == null && Choice(OsdControl.ColorTemperature).SelectedItem == null
                && Choice(OsdControl.PictureMode).PlaceholderText.Contains("未确认") && Choice(OsdControl.ColorTemperature).PlaceholderText.Contains("未确认"),
                "Paused preset editors explain uncertain state instead of instructing a read that is not available");
            await (Task)Call(window!, "Capture", ReviewProgram.ReportPath + ".halted.png")!;
            int pausedReads = fake.Reads; await (Task)Call(advanced, "ApplyAsync", OsdControl.BlackStabilizer)!; await (Task)Call(window!, "RefreshAsync", true)!;
            Check(fake.Reads == pausedReads && fake.Writes.Count == count + 1 && WindowsMonitorTransport.NativeReadAttempts == 0 && WindowsMonitorTransport.NativeWriteAttempts == 0,
                "The uncertain session blocks late Apply and refresh; all regression I/O is fake");
        }
        catch (Exception ex) { failures++; Environment.ExitCode = 1; results.Add(new { harnessError = ex.ToString() }); }
        finally { fake.InitialReadRelease.Set(); fake.WriteGate?.Set(); WriteReport(); await (Task)Call(window!, "ExitAsync")!; }
    }

    async Task RunExitOsdApply()
    {
        bool exitStarted = false;
        try
        {
            await Until(() => fake.InitialReadEntered.IsSet); fake.InitialReadRelease.Set(); await Idle();
            var advanced = Get(window!, "advanced"); var editors = (IDictionary)Get(advanced, "editors");
            var editor = editors[OsdControl.PictureMode]!; var pages = (SelectorBar)Get(window!, "pages"); pages.SelectedItem = pages.Items[1];
            var choice = (ComboBox)Get(editor, "choice"); choice.SelectedItem = choice.Items.Cast<ComboBoxItem>().Single(i => (uint)i.Tag == 30);
            fake.WriteEntered = new(); fake.WriteGate = new(); ClickOsd((Button)Get(editor, "apply")); await Until(() => fake.WriteEntered.IsSet);
            bool closed = false;
            window!.Closed += (_, _) =>
            {
                closed = true;
                Check(fake.LiveConnections == 0 && fake.Writes.SequenceEqual(new[] { ((byte)0x15, 30u) }) && !(bool)Get(advanced, "Busy"), "Exit drains the OSD write, full readback and UI result before closing");
                Check(WindowsMonitorTransport.NativeReadAttempts == 0 && WindowsMonitorTransport.NativeWriteAttempts == 0, "OSD exit regression performs zero native monitor I/O"); WriteReport();
            };
            Call(window, "ExitAsync"); exitStarted = true; await Task.Delay(80);
            Check(!closed && (bool)Get(window, "exiting"), "Exit waits for an already-attempted OSD apply without restoring or canceling its verification"); fake.WriteGate.Set();
        }
        catch (Exception ex) { failures++; Environment.ExitCode = 1; results.Add(new { harnessError = ex.ToString() }); }
        finally { fake.InitialReadRelease.Set(); fake.WriteGate?.Set(); if (!exitStarted) { WriteReport(); await (Task)Call(window!, "ExitAsync")!; } }
    }
}
