using System.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using XScreen.Core;

namespace XScreen.Review;

public sealed partial class ReviewApplication
{
    async Task RunOsdComparison()
    {
        Task? opening = null;
        bool exitingViaRead = false;
        try
        {
            await Until(() => fake.InitialReadEntered.IsSet); fake.InitialReadRelease.Set(); await Idle();
            int startupReads = fake.Reads;
            var mainRoot = (Grid)Get(window!, "root");
            var more = Descendants(mainRoot).OfType<Button>().Single(b => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(b) == "更多操作");
            Check(!((MenuFlyout)more.Flyout).Items.OfType<MenuFlyoutItem>().Any(i => i.Text.Contains("只读")), "The diagnostic read-only comparison is absent from the everyday menu");
            opening = (Task)Call(window!, "ShowOsdComparisonAsync")!;
            await Until(() => window!.GetType().GetField("osdComparison", Members)!.GetValue(window) != null); await Task.Delay(120);
            var view = Get(window!, "osdComparison"); var dialog = (ContentDialog)Get(view, "dialog");
            var read = (Button)Get(view, "capture"); var baseline = (Button)Get(view, "baseline"); var model = (OsdComparison)Get(view, "state");
            var message = (TextBlock)Get(view, "message");
            void Invoke(Button b) => ((IInvokeProvider)new ButtonAutomationPeer(b).GetPattern(PatternInterface.Invoke)).Invoke();
            Check((bool)Get(view, "open") && fake.Reads == startupReads && fake.Writes.Count == 0 && !baseline.IsEnabled,
                "Opening the dialog performs zero I/O and cannot baseline an unread sample");
            await (Task)Call(window!, "ShowOsdComparisonAsync")!;
            Check(!opening.IsCompleted && fake.Reads == startupReads, "Repeated open requests retain one dialog without reading");
            fake.OsdReadEntered = new(); fake.OsdReadGate = new(); Invoke(read); await Until(() => fake.OsdReadEntered.IsSet);
            Check(!read.IsEnabled && !baseline.IsEnabled, "Read and baseline actions disable while the explicit capture is pending");
            await (Task)Call(view, "ReadNowAsync")!;
            Call(view, "SaveBaseline"); dialog.Hide(); await Task.Delay(80);
            Check((bool)Get(view, "reading") && (bool)Get(view, "open") && fake.Writes.Count == 0,
                "Duplicate callbacks, baseline and close cannot interrupt a pending capture or introduce writes");
            fake.OsdReadGate.Set(); await Until(() => !(bool)Get(view, "reading")); await Idle();
            fake.OsdReadGate = null;
            Check(fake.Reads == startupReads + 4 && model.Current?.Values.Count == 4 && model.Baseline == null && baseline.IsEnabled,
                "One explicit click collects all four codes once and leaves baseline selection to the user");
            int captured = fake.Reads; Invoke(baseline); await Task.Delay(60);
            Check(model.Baseline != null && fake.Reads == captured && fake.Writes.Count == 0,
                "Saving a baseline records existing values without any hardware I/O");
            fake.SimulateExternalOsd(0x15, 30); fake.SimulateExternalOsd(0x14, 9); fake.SimulateExternalOsd(0x12, 69); fake.SimulateExternalOsd(0xF9, 45);
            ((TextBox)Get(view, "note")).Text = "离线模拟：玩家1→FPS，暖色→冷色；非实机验证";
            Invoke(read); await Until(() => !(bool)Get(view, "reading") && fake.Reads == captured + 4); await Idle();
            Check(model.Differences.Count == 4 && model.Differences.All(d => d.RawChanged)
                && OsdProtocol.Decode(model.Current!)[0].Display == "FPS" && OsdProtocol.Decode(model.Current!)[1].Display == "冷色",
                "External manual edits produce four decoded differences while preserving the original baseline");
            Check(message.Text.Contains("不会自动恢复") && fake.Writes.Count == 0,
                "The comparison does not claim complete recovery and contains no restore path");
            // Capture the actual dialog content, including its light/dark layout and values.
            async Task Screenshot(string path)
            {
                var bitmap = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap(); await bitmap.RenderAsync(dialog);
                using var pixels = Windows.Storage.Streams.DataReader.FromBuffer(await bitmap.GetPixelsAsync()); byte[] data = new byte[pixels.UnconsumedBufferLength]; pixels.ReadBytes(data);
                using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, stream);
                encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, data); await encoder.FlushAsync();
                using var reader = new Windows.Storage.Streams.DataReader(stream.GetInputStreamAt(0)); await reader.LoadAsync((uint)stream.Size); byte[] png = new byte[reader.UnconsumedBufferLength]; reader.ReadBytes(png); await File.WriteAllBytesAsync(path, png);
            }
            await Screenshot(ReviewProgram.ReportPath + ".dark.png");
            dialog.RequestedTheme = ElementTheme.Light; await Task.Delay(100);
            // Palette brushes follow the main selector in the production path.
            ((ComboBox)Get(window!, "appearance")).SelectedIndex = 1; await Task.Delay(100);
            Check(dialog.RequestedTheme == mainRoot.ActualTheme, "An already-open comparison follows subsequent theme changes");
            await Screenshot(ReviewProgram.ReportPath + ".light.png");
            Check(((FrameworkElement)dialog.Content).ActualWidth > 0 && ((FrameworkElement)dialog.Content).ActualHeight > 0
                && read.ActualHeight >= 36 && baseline.ActualHeight >= 36, "Comparison actions remain usable in both themes");
            dialog.Hide(); await opening; opening = (Task)Call(window!, "ShowOsdComparisonAsync")!; await Task.Delay(100);
            Check(model.Baseline != null && fake.Reads == captured + 4, "Closing and reopening preserves the baseline without automatic reads");
            fake.FailNextReadCode = 0x14; int beforeFailure = fake.Reads; Invoke(read); await Until(() => !(bool)Get(view, "reading")); await Idle();
            Check(fake.Reads == beforeFailure + 2 && fake.Writes.Count == 0 && model.Current == null && model.Baseline != null
                && model.Differences.Count == 0 && (bool)Get(window!, "halted"), "The first failed field halts subsequent reads, invalidates the current result and pauses control");
            int pausedReads = fake.Reads; Invoke(read); await Until(() => !(bool)Get(view, "reading"));
            Check(fake.Reads == pausedReads && message.Text.Contains("本次对照无效") && fake.Writes.Count == 0,
                "A click while paused does not retry hardware or reuse old differences");
            dialog.Hide(); await opening; opening = null; await (Task)Call(view, "ReadNowAsync")!;
            Check(fake.Reads == pausedReads && WindowsMonitorTransport.NativeReadAttempts == 0 && WindowsMonitorTransport.NativeWriteAttempts == 0,
                "Late callbacks after close perform zero real or fake I/O");
            await (Task)Call(window!, "RefreshAsync", true)!;
            opening = (Task)Call(window!, "ShowOsdComparisonAsync")!; await Task.Delay(80);
            fake.OsdReadEntered = new(); fake.OsdReadGate = new(); Invoke(read); await Until(() => fake.OsdReadEntered.IsSet);
            bool closed = false;
            window!.Closed += (_, _) =>
            {
                closed = true;
                Check(fake.LiveConnections == 0 && !(bool)Get(view, "reading") && model.Current == null,
                    "Exit drains the complete read/error UI operation before disposing the window");
                Check(fake.Writes.Count == 0 && WindowsMonitorTransport.NativeReadAttempts == 0 && WindowsMonitorTransport.NativeWriteAttempts == 0,
                    "Exit during comparison produces no setting or recovery write");
                WriteReport();
            };
            Call(window, "ExitAsync"); exitingViaRead = true; await Task.Delay(80);
            Check(!closed && (bool)Get(window, "exiting"), "Exit waits while a native-style fake read is still pending");
            fake.OsdReadGate.Set();
        }
        catch (Exception ex) { failures++; Environment.ExitCode = 1; results.Add(new { harnessError = ex.ToString() }); }
        finally { fake.InitialReadRelease.Set(); fake.OsdReadGate?.Set(); if (!exitingViaRead) { WriteReport(); await (Task)Call(window!, "ExitAsync")!; } }
    }
}
