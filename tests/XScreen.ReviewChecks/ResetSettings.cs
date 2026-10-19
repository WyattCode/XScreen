using System.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using XScreen.Core;

namespace XScreen.Review;

public sealed partial class ReviewApplication
{
    void SeedResetValues()
    {
        foreach (var pair in new (byte, uint)[] { (0x15, 30), (0x14, 9), (0x12, 40), (0xF9, 45), (0x87, 60), (0x62, 30), (0x10, 80) }) fake.SimulateExternalOsd(pair.Item1, pair.Item2);
    }
    async Task<(Task Task, ContentDialog Dialog)> OpenReset()
    {
        var task = (Task)Call(window!, "ShowResetDialogAsync")!; Call(window!, "Track", task);
        await Until(() => window!.GetType().GetField("resetView", Members)!.GetValue(window) != null);
        var dialog = (ContentDialog)Get(Get(window!, "resetView"), "dialog");
        await Until(() => dialog.IsLoaded); await Task.Delay(60); return (task, dialog);
    }
    static Button ConfirmReset(ContentDialog dialog) => Descendants(dialog).OfType<Button>().Single(b => b.Name == "PrimaryButton");
    async Task CaptureReset(ContentDialog dialog, string path)
    {
        var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(dialog);
        using var pixels = Windows.Storage.Streams.DataReader.FromBuffer(await bitmap.GetPixelsAsync()); byte[] data = new byte[pixels.UnconsumedBufferLength]; pixels.ReadBytes(data);
        using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, data); await encoder.FlushAsync();
        using var reader = new Windows.Storage.Streams.DataReader(stream.GetInputStreamAt(0)); await reader.LoadAsync((uint)stream.Size); byte[] png = new byte[reader.UnconsumedBufferLength]; reader.ReadBytes(png); await File.WriteAllBytesAsync(path, png);
    }
    async Task RunResetSettings()
    {
        try
        {
            await Until(() => fake.InitialReadEntered.IsSet); fake.InitialReadRelease.Set(); await Idle();
            var root = (Grid)Get(window!, "root"); var reset = (Button)Get(window!, "reset"); var pages = (SelectorBar)Get(window!, "pages");
            Check(reset.IsEnabled && Descendants(root).OfType<Button>().Count(b => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(b) == "重置设置") == 1,
                "One shared reset button serves both tabs and becomes available only after a valid snapshot");
            var rows = (IDictionary)Get(window!, "rows"); var brightness = rows[(byte)0x10]!;
            Call(brightness, "Request", 90d); await Idle(); int reads = fake.Reads, writes = fake.Writes.Count;
            var opened = await OpenReset();
            var texts = Descendants(opened.Dialog).OfType<TextBlock>().Select(t => t.Text).ToArray();
            Check(fake.Reads == reads && fake.Writes.Count == writes && texts.Contains("90")
                && AppResetProfile.Settings.All(s => texts.Contains(s.Name) && texts.Contains(s.Display)),
                "Opening reset shows six targets and cached current values without I/O");
            Check((string)opened.Dialog.Title == "重置六项设置" && texts.Contains("不重置（受限）") && texts.Contains("对比度")
                && texts.Count(t => t == "70") == 1,
                "Reset confirmation explicitly excludes contrast and never presents70 as a contrast reset target");
            Check(opened.Dialog.DefaultButton == ContentDialogButton.Close && !(bool)Call(window!, "CanAdjust", (byte)0x10)!
                && !reset.IsEnabled && !((Button)Get(window!, "refresh")).IsEnabled,
                "Cancel is the confirmation default; main and tray adjustments and refresh are blocked while reviewing");
            await (Task)Call(window!, "ShowResetDialogAsync")!;
            Check(fake.Reads == reads && fake.Writes.Count == writes, "Repeated reset opens do not create a second confirmation or access the monitor");
            await CaptureReset(opened.Dialog, ReviewProgram.ReportPath + ".dark.png");
            ((ComboBox)Get(window!, "appearance")).SelectedIndex = 1; await Task.Delay(70);
            Check(opened.Dialog.RequestedTheme == root.ActualTheme, "An open reset confirmation follows theme changes");
            await CaptureReset(opened.Dialog, ReviewProgram.ReportPath + ".light.png");
            opened.Dialog.Hide(); await opened.Task;
            Check(fake.Reads == reads && fake.Writes.Count == writes && reset.IsEnabled, "Cancelling reset changes nothing and restores ordinary controls");
            pages.SelectedItem = pages.Items[1]; SeedResetValues(); await (Task)Call(window!, "RefreshAsync", true)!;
            opened = await OpenReset(); fake.WriteEntered = new(); fake.WriteGate = new(); reads = fake.Reads; writes = fake.Writes.Count;
            ClickOsd(ConfirmReset(opened.Dialog)); await Until(() => fake.WriteEntered.IsSet);
            Check((bool)Get(window!, "resetting") && !reset.IsEnabled && !(bool)Call(window!, "CanAdjust", (byte)0x62)!,
                "Confirmed reset is single-flight and disables competing quick/tray operations");
            await (Task)Call(window!, "ShowResetDialogAsync")!; await (Task)Call(window!, "RefreshAsync", true)!;
            fake.WriteGate.Set(); await opened.Task; await Idle(); fake.WriteGate = null;
            Check(fake.Writes.Skip(writes).Select(w => w.Code).SequenceEqual(AppResetProfile.Settings.Select(s => s.Code))
                && fake.Writes.Count == writes + 6 && !fake.Writes.Skip(writes).Any(w => w.Code == 0x12) && ((TextBlock)Get(window!, "mode")).Text == "玩家 1"
                && (uint)Get(brightness, "Current") == 100 && !UndoEnabled(brightness),
                "Confirmation resets six writable settings, excludes contrast, synchronizes both tabs and clears old undo");
            Check(((InfoBar)Get(window!, "notice")).Title == "重置完成" && fake.LiveConnections == 0 && reset.IsEnabled,
                "Only complete verified reset reports success and releases its connection");
            reads = fake.Reads; writes = fake.Writes.Count; opened = await OpenReset(); ClickOsd(ConfirmReset(opened.Dialog)); await opened.Task;
            Check(fake.Reads == reads + 14 && fake.Writes.Count == writes && ((InfoBar)Get(window!, "notice")).Message.Contains("没有重复写入"),
                "Confirming an already-default profile verifies state without duplicate writes");
            Call(window!, "ResizeLogical", 840, 680); await Task.Delay(80);
            var position = reset.TransformToVisual(root).TransformPoint(new(0, 0));
            Check(position.X >= 0 && position.X + reset.ActualWidth <= root.ActualWidth && reset.ActualHeight >= 36,
                "The shared reset remains visible and usable at the minimum window size");
            SeedResetValues(); await (Task)Call(window!, "RefreshAsync", true)!; opened = await OpenReset();
            fake.IgnoreNextWrite = true; reads = fake.Reads; writes = fake.Writes.Count; ClickOsd(ConfirmReset(opened.Dialog)); await opened.Task;
            Check((bool)Get(window!, "halted") && !reset.IsEnabled && fake.Writes.Count == writes + 1 && fake.Reads == reads + 8,
                "The first mismatched target stops reset immediately, disables controls and never reports complete success");
            reads = fake.Reads; await (Task)Call(window!, "ShowResetDialogAsync")!; await (Task)Call(window!, "RefreshAsync", true)!;
            Check(fake.Reads == reads && fake.Writes.Count == writes + 1 && WindowsMonitorTransport.NativeReadAttempts == 0 && WindowsMonitorTransport.NativeWriteAttempts == 0,
                "An uncertain reset cannot be retried or recovered via refresh; every test uses fake transport only");
        }
        catch (Exception ex) { failures++; Environment.ExitCode = 1; results.Add(new { harnessError = ex.ToString() }); }
        finally { fake.InitialReadRelease.Set(); fake.WriteGate?.Set(); WriteReport(); await (Task)Call(window!, "ExitAsync")!; }
    }
    async Task RunExitReset()
    {
        bool exitStarted = false;
        try
        {
            await Until(() => fake.InitialReadEntered.IsSet); fake.InitialReadRelease.Set(); await Idle();
            SeedResetValues(); await (Task)Call(window!, "RefreshAsync", true)!;
            var opened = await OpenReset(); fake.WriteEntered = new(); fake.WriteGate = new();
            ClickOsd(ConfirmReset(opened.Dialog)); await Until(() => fake.WriteEntered.IsSet);
            bool closed = false;
            window!.Closed += (_, _) =>
            {
                closed = true;
                Check(fake.LiveConnections == 0 && fake.Writes.Count == 1 && fake.PostWriteReads == 7,
                    "Exit drains the first reset write's full readback and cancels every later target without rollback");
                Check(!(bool)Get(window!, "resetting") && WindowsMonitorTransport.NativeReadAttempts == 0 && WindowsMonitorTransport.NativeWriteAttempts == 0,
                    "Exit drains the complete reset UI operation with zero native device I/O"); WriteReport();
            };
            Call(window, "ExitAsync"); exitStarted = true; await Task.Delay(60);
            Check(!closed && (bool)Get(window!, "exiting"), "Exit waits for the attempted reset write rather than closing over pending verification");
            fake.WriteGate.Set();
        }
        catch (Exception ex) { failures++; Environment.ExitCode = 1; results.Add(new { harnessError = ex.ToString() }); }
        finally { fake.InitialReadRelease.Set(); fake.WriteGate?.Set(); if (!exitStarted) { WriteReport(); await (Task)Call(window!, "ExitAsync")!; } }
    }
    async Task RunExitResetReview()
    {
        bool exitStarted = false;
        try
        {
            await Until(() => fake.InitialReadEntered.IsSet); fake.InitialReadRelease.Set(); await Idle();
            var opened = await OpenReset(); int reads = fake.Reads; bool closed = false;
            window!.Closed += (_, _) =>
            {
                closed = true;
                Check(opened.Task.IsCompleted && !(bool)Get(window!, "resetDialogOpen") && fake.Reads == reads && fake.Writes.Count == 0,
                    "Exit dismisses the unconfirmed reset dialog and drains its task without any device I/O");
                Check(fake.LiveConnections == 0 && WindowsMonitorTransport.NativeReadAttempts == 0 && WindowsMonitorTransport.NativeWriteAttempts == 0,
                    "Exit during reset review closes cleanly using fake-only transport"); WriteReport();
            };
            Call(window, "ExitAsync"); exitStarted = true;
            await Until(() => closed);
        }
        catch (Exception ex) { failures++; Environment.ExitCode = 1; results.Add(new { harnessError = ex.ToString() }); }
        finally { fake.InitialReadRelease.Set(); if (!exitStarted) { WriteReport(); await (Task)Call(window!, "ExitAsync")!; } }
    }
}
