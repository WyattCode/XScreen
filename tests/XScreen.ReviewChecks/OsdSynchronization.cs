using System.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using XScreen.Core;

namespace XScreen.Review;

public sealed partial class ReviewApplication
{
    async Task RunOsdSynchronization()
    {
        try
        {
            await Until(() => fake.InitialReadEntered.IsSet); fake.InitialReadRelease.Set(); await Idle();
            var advanced = Get(window!, "advanced"); var editors = (IDictionary)Get(advanced, "editors");
            var rows = (IDictionary)Get(window!, "rows"); var brightness = rows[(byte)0x10]!;
            object Editor(OsdControl control) => editors[control]!;
            void Select(OsdControl control, uint value)
            {
                var choice = (ComboBox)Get(Editor(control), "choice");
                choice.SelectedItem = choice.Items.Cast<ComboBoxItem>().Single(i => (uint)i.Tag == value);
            }
            async Task Apply(OsdControl control)
            {
                if (control == OsdControl.Contrast) await (Task)Call(advanced, "ApplyAsync", control)!;
                else ClickOsd((Button)Get(Editor(control), "apply"));
                await Until(() => !(bool)Get(advanced, "Busy")); await Idle();
            }
            async Task Read()
            {
                ClickOsd((Button)Get(advanced, "read")); await Until(() => !(bool)Get(advanced, "Busy")); await Idle();
            }
            var pages = (SelectorBar)Get(window!, "pages"); pages.SelectedItem = pages.Items[1];
            Check(fake.Reads == 7 && fake.Writes.Count == 0, "Startup automatically loads one complete snapshot and page navigation does not add I/O");
            Call(brightness, "Request", 90d); await Idle();
            Check(UndoEnabled(brightness), "A completed direct adjustment offers its own current-mode undo");

            fake.SimulateExternalOsd(0x10, 65); fake.SimulateExternalOsd(0x62, 40); fake.SimulateExternalOsd(0x87, 60);
            fake.SimulateExternalOsd(0x15, 30); fake.SimulateExternalOsd(0x14, 9); fake.SimulateExternalOsd(0x12, 68); fake.SimulateExternalOsd(0xF9, 45);
            int reads = fake.Reads, writes = fake.Writes.Count; await Read();
            Check(fake.Reads == reads + 7 && fake.Writes.Count == writes, "Picture Refresh reads the full seven fields once without any writes");
            Check((uint)Get(brightness, "Current") == 65 && (uint)Get(rows[(byte)0x62]!, "Current") == 40
                && (uint)Get(rows[(byte)0x87]!, "Current") == 60 && ((TextBlock)Get(window!, "mode")).Text == "FPS"
                && ((TextBlock)Get(window!, "contrast")).Text == "68" && ((TextBlock)Get(window!, "black")).Text == "45",
                "Refreshing external mode/color changes synchronizes both panes and the summary from the same snapshot");
            Check(!UndoEnabled(brightness) && Get(brightness, "previous") == null, "An external snapshot discards stale undo from the former display state");

            Call(brightness, "Request", 64d); await Idle(); fake.SimulateModeSideEffects = true;
            Select(OsdControl.PictureMode, 46); await Apply(OsdControl.PictureMode);
            Check(((TextBlock)Get(window!, "mode")).Text == "玩家 2" && (uint)Get(brightness, "Current") == 60
                && ((TextBox)Get(Editor(OsdControl.Contrast), "number")).Text == "72" && !UndoEnabled(brightness),
                "Applying a picture mode synchronizes dependent values and invalidates undo from the former mode");

            Call(brightness, "Request", 59d); await Idle(); fake.SimulateColorSideEffects = true;
            Select(OsdControl.ColorTemperature, 3); writes = fake.Writes.Count; await Apply(OsdControl.ColorTemperature);
            Check(fake.Writes.Count == writes + 1 && fake.Writes.Last() == ((byte)0x14, 3u)
                && ((TextBlock)Get(window!, "mode")).Text == "鲜艳" && (uint)Get(brightness, "Current") == 55
                && (uint)Get(rows[(byte)0x62]!, "Current") == 20 && (uint)Get(rows[(byte)0x87]!, "Current") == 40
                && ((TextBox)Get(Editor(OsdControl.BlackStabilizer), "number")).Text == "60" && !UndoEnabled(brightness),
                "Color-preset side effects synchronize mode and all dependent fields without replaying targets or stale undo");
            fake.SimulateExternalOsd(0x10, 54); fake.SimulateExternalOsd(0x15, 45); reads = fake.Reads; writes = fake.Writes.Count;
            await Apply(OsdControl.ColorTemperature);
            Check(fake.Reads == reads + 11 && fake.Writes.Count == writes && (uint)Get(brightness, "Current") == 54
                && ((TextBlock)Get(window!, "mode")).Text == "玩家 1", "A no-op color Apply still refreshes dependent state without a duplicate write");

            fake.SimulateExternalOsd(0x12, 101); await (Task)Call(window!, "RefreshAsync", true)!;
            Check(!((Button)Get(Editor(OsdControl.Contrast), "apply")).IsEnabled
                && ((TextBlock)Get(window!, "contrast")).Text.Contains("未确认") && !(bool)Get(window!, "halted"),
                "Invalid numeric replies are marked unconfirmed consistently in the editor and summary");
            fake.SimulateExternalOsd(0x12, 65); fake.ContrastMaximum = 70; await Read();
            var contrast = Editor(OsdControl.Contrast); var slider = (Slider)Get(contrast, "slider"); var number = (TextBox)Get(contrast, "number");
            Check(slider.Visibility == Visibility.Collapsed && number.Visibility == Visibility.Collapsed && number.Text == "65",
                "Contrast values still synchronize after mode changes while its editing controls remain hidden");
            number.Text = "80"; reads = fake.Reads; writes = fake.Writes.Count; await Apply(OsdControl.Contrast);
            Check(fake.Reads == reads && fake.Writes.Count == writes && !(bool)Get(window!, "halted")
                && !fake.Writes.Any(w => w.Code == 0x12), "Any hidden contrast draft is ignored before I/O rather than halting the session");
            fake.ContrastMaximum = 100; await Read();
            Check(slider.Maximum == 100 && number.Text == "65" && !((TextBlock)Get(advanced, "feedback")).Text.Contains("请输入"),
                "A later snapshot widens the control range and clears obsolete draft validation feedback");
            fake.FailNextReadCode = 0x62; reads = fake.Reads; writes = fake.Writes.Count; await Read();
            Check(fake.Reads == reads + 2 && fake.Writes.Count == writes && (bool)Get(window!, "halted")
                && !((Button)Get(Editor(OsdControl.PictureMode), "apply")).IsEnabled && !(bool)Get(brightness, "Available"),
                "A picture refresh stops at its first failed reply and invalidates both panes, never mixing old and new states");
            reads = fake.Reads; await Task.Delay(180);
            Check(fake.Reads == reads && fake.Writes.Count == writes, "Failed picture refresh does not trigger automatic retries or replay a draft");
            await (Task)Call(window!, "RefreshAsync", true)!;
            Check(!(bool)Get(window!, "halted") && fake.Reads == reads + 7 && fake.Writes.Count == writes
                && (uint)Get(brightness, "Current") == 54 && !UndoEnabled(brightness),
                "Explicit read-only checksum recovery reloads both panes without resurrecting settings or undo");
            Check(WindowsMonitorTransport.NativeReadAttempts == 0 && WindowsMonitorTransport.NativeWriteAttempts == 0,
                "Synchronization regressions use fake transport only, never the user's monitor");
        }
        catch (Exception ex) { failures++; Environment.ExitCode = 1; results.Add(new { harnessError = ex.ToString() }); }
        finally { fake.InitialReadRelease.Set(); WriteReport(); await (Task)Call(window!, "ExitAsync")!; }
    }
}
