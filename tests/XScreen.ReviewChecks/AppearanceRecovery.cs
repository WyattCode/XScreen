using System.Collections;
using System.Reflection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using XScreen.Core;

namespace XScreen.Review;

public sealed partial class ReviewApplication
{
    async Task RunAppearanceRecovery()
    {
        try
        {
            await Until(() => fake.InitialReadEntered.IsSet); fake.InitialReadRelease.Set(); await Idle();
            var root = (Grid)Get(window!, "root"); var appearance = (ComboBox)Get(window!, "appearance");
            int initialReads = fake.Reads;
            var titleBar = (TitleBar)Get(window!, "titleBar");
            async Task CheckTitleLayout(string theme)
            {
                var icon = Descendants(titleBar).OfType<Viewbox>().Single(e => e.Name == "PART_Icon");
                var title = Descendants(titleBar).OfType<TextBlock>().Single(e => e.Name == "PART_TitleText");
                // The icon bitmap decodes asynchronously after the initial monitor read completes.
                await Until(() => icon.ActualHeight > 0 && title.ActualHeight > 0);
                double Center(FrameworkElement e) => e.TransformToVisual(titleBar).TransformPoint(new(0, e.ActualHeight / 2)).Y;
                Check(icon.ActualHeight > 0 && title.ActualHeight > 0 && Math.Abs(Center(icon) - Center(title)) <= .5,
                    "Title icon and text share a vertical center in " + theme, new { iconCenter = Center(icon), titleCenter = Center(title) });
                double iconRight = icon.TransformToVisual(titleBar).TransformPoint(new(icon.ActualWidth, 0)).X;
                double titleLeft = title.TransformToVisual(titleBar).TransformPoint(new(0, 0)).X;
                Check(titleBar.Title == "XScreen" && title.Text == "XScreen" && Math.Abs(titleLeft - iconRight - 8) <= .5,
                    "The caption is only XScreen with an eight-pixel icon gap in " + theme, new { titleLeft, iconRight });
                Check(title.TextLineBounds == TextLineBounds.Tight, "Title alignment uses tight glyph bounds rather than extra line leading in " + theme);
                Check(window!.ExtendsContentIntoTitleBar && titleBar.ActualHeight >= 32 && titleBar.RequestedTheme == root.ActualTheme,
                    "WinUI title bar keeps native caption integration and matches the main theme in " + theme);
                await (Task)Call(window!, "CaptureTitleBar", ReviewProgram.ReportPath + ".title-" + theme + ".png")!;
            }
            await CheckTitleLayout("Dark");
            Check(!Descendants(root).OfType<TextBlock>().Any(e => e.Text.Contains("非 LG 官方软件") || e.Text.Contains("仅在你调节时写入")),
                "The removed footer sentence is absent from the rendered content");
            Check(ReviewProgram.PlacementPreparedBeforeShow, "Initial position is prepared before the first visible window");
            var work = DisplayArea.GetFromWindowId(window!.AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
            Check(Math.Abs(2 * (window.AppWindow.Position.X - work.X) + window.AppWindow.Size.Width - work.Width) <= 1 &&
                  Math.Abs(2 * (window.AppWindow.Position.Y - work.Y) + window.AppWindow.Size.Height - work.Height) <= 1,
                "The actual initial window is centered inside its monitor's usable work area",
                new { x = window.AppWindow.Position.X, y = window.AppWindow.Position.Y, width = window.AppWindow.Size.Width, height = window.AppWindow.Size.Height, workX = work.X, workY = work.Y, workWidth = work.Width, workHeight = work.Height });
            var boundsMethod = window.GetType().GetMethod("InitialBounds", BindingFlags.Static | BindingFlags.NonPublic);
            bool fits = boundsMethod != null;
            if (boundsMethod != null)
            foreach (double scale in new[] { 1d, 1.25, 1.75, 2.5 })
            foreach (var area in new[] { new RectInt32(0, 0, 1920, 1040), new RectInt32(-2560, -200, 2560, 1400), new RectInt32(0, 40, 800, 560) })
            foreach (bool offline in new[] { false, true })
            {
                var b = (RectInt32)boundsMethod.Invoke(null, [area, scale, offline])!;
                fits &= b.Width > 0 && b.Height > 0 && b.X >= area.X && b.Y >= area.Y && b.X + b.Width <= area.X + area.Width && b.Y + b.Height <= area.Y + area.Height &&
                    Math.Abs(2 * (b.X - area.X) + b.Width - area.Width) <= 1 && Math.Abs(2 * (b.Y - area.Y) + b.Height - area.Height) <= 1;
            }
            Check(fits, "Initial bounds stay centered and on-screen across small work areas, negative origins and 100-250 percent DPI");
            var choices = appearance.Items.Cast<object>().Select(o => o.ToString()).ToArray();
            int light = Array.IndexOf(choices, "UltraGear 浅色"), dark = Array.IndexOf(choices, "UltraGear 深色"), system = Array.IndexOf(choices, "跟随 Windows");
            Check(light >= 0 && dark >= 0 && system >= 0 && choices.Length == 3, "Appearance offers explicit Dark, Light and Follow Windows choices", new { choices });
            var point = new PointInt32(850, 650);
            if (light >= 0)
            {
                appearance.SelectedIndex = light; await Task.Delay(150);
                await CheckTitleLayout("Light");
                Check(root.RequestedTheme == ElementTheme.Light && root.ActualTheme == ElementTheme.Light && ((SolidColorBrush)root.Background).Color.R > 240 && ((Image)Get(window, "backdrop")).Visibility == Visibility.Collapsed,
                    "Selecting Light through the real selector updates the main surface and hides the dark artwork");
                await (Task)Call(window, "Capture", ReviewProgram.ReportPath + ".light-main.png")!;
                Call(window, "ShowTrayMenu", point); var themedMenu = (Window)Get(window, "trayMenu"); await Task.Delay(120);
                Check(((Grid)Get(themedMenu, "root")).ActualTheme == ElementTheme.Light && ((SolidColorBrush)((Border)Get(themedMenu, "surface")).Background).Color.R > 240,
                    "The tray follows the explicit Light selection");
                await (Task)Call(themedMenu, "Capture", ReviewProgram.ReportPath + ".light-tray.png")!; Call(themedMenu, "Dismiss");
            }
            if (system >= 0) { appearance.SelectedIndex = system; await Task.Delay(100); Check(root.RequestedTheme == ElementTheme.Default, "Follow Windows retains a distinct system theme selection"); }
            appearance.SelectedIndex = dark; await Task.Delay(100);
            Check(root.ActualTheme == ElementTheme.Dark && ((SolidColorBrush)root.Background).Color.R < 40, "Selecting Dark restores the graphite surface");
            Check(fake.Reads == initialReads && fake.Writes.Count == 0, "Appearance and initial placement issue no monitor I/O");
            var moved = new PointInt32(window.AppWindow.Position.X + 20, window.AppWindow.Position.Y + 15); window.AppWindow.Move(moved);
            Call(window, "ShowWindow");
            if (window.GetType().GetMethod("PrepareInitialPlacement", Members) != null) Call(window, "PrepareInitialPlacement");
            Check(window.AppWindow.Position.X == moved.X && window.AppWindow.Position.Y == moved.Y, "Restoring a window does not recenter over a user's later position");
            await CheckTitleLayout("Dark-restored");

            Call(window, "ShowTrayMenu", point); var menu = (Window)Get(window, "trayMenu"); await Task.Delay(100);
            var recovery = menu.GetType().GetField("recovery", Members)?.GetValue(menu) as Button;
            Check(recovery != null, "The tray provides a separate explicit state-read recovery action");
            if (recovery == null) return;
            Check(recovery.Visibility == Visibility.Collapsed, "Healthy sessions do not show an unnecessary recovery command");
            Call(menu, "Dismiss");
            var rows = (IDictionary)Get(window, "rows"); var volume = rows[(byte)0x62]!;
            Call(volume, "Request", 53d); await Idle();
            fake.FailNextReadCode = 0x62; Call(volume, "Request", 44d); await Idle();
            var controller = (MonitorController)Get(window, "controller");
            Check(controller.Halted && controller.CanReadAgain && fake.Writes.SequenceEqual(new[] { ((byte)0x62, 53u) }),
                "The reported volume 53-to-44 checksum failure is reproduced before mutation, with 44 never sent");
            Check(rows.Values.Cast<object>().All(r => !((Slider)Get(r, "Slider")).IsEnabled && (string)Get(r, "DisplayValue") == "—"),
                "A communication fault still pauses every adjustment and shows unknown values");
            Call(window, "ShowTrayMenu", point); await Task.Delay(100);
            var actions = ((IEnumerable)Get(menu, "actions")).Cast<Button>().ToArray();
            Check(recovery.Visibility == Visibility.Visible && recovery.IsEnabled && actions.Skip(1).Take(4).All(b => !b.IsEnabled) && ((TextBlock)Get(menu, "group")).Text.Contains("重新读取"),
                "A recoverable pre-write fault explains disabled tray adjustments and exposes an enabled read-only command");
            Call(menu, "HandleKey", Windows.System.VirtualKey.Home); Call(menu, "HandleKey", Windows.System.VirtualKey.Down);
            Check(recovery.FocusState != FocusState.Unfocused, "Arrow navigation reaches the visible recovery command in visual order");
            await (Task)Call(menu, "Capture", ReviewProgram.ReportPath + ".paused-tray.png")!; Call(menu, "Dismiss");
            await (Task)Call(window, "Capture", ReviewProgram.ReportPath + ".paused-main.png")!;
            Call(window, "ShowTrayMenu", point);
            int beforeFailedRead = fake.Reads; fake.FailNextReadCode = 0x62;
            ((IInvokeProvider)new ButtonAutomationPeer(recovery).GetPattern(PatternInterface.Invoke)).Invoke(); await Idle();
            Check(controller.CanReadAgain && fake.Reads == beforeFailedRead + 2 && fake.Writes.Count == 1,
                "A failed manual recovery stops at the first failed read without retrying or writing");
            Call(window, "ShowTrayMenu", point); await Task.Delay(80);
            int beforeRead = fake.Reads;
            var invoke = (IInvokeProvider)new ButtonAutomationPeer(recovery).GetPattern(PatternInterface.Invoke);
            invoke.Invoke(); invoke.Invoke(); await Idle(); await Task.Delay(100);
            Check(!controller.Halted && !(bool)Get(window, "halted") && fake.Reads == beforeRead + 7 && fake.Writes.Count == 1 && (uint)Get(volume, "Current") == 53,
                "Two queued recovery clicks perform one full state read, restore controls and never replay the rejected 44");
            Call(menu, "InvokeRecovery"); await Task.Delay(80);
            Check(fake.Reads == beforeRead + 7 && fake.Writes.Count == 1, "A late click from the dismissed recovery command performs no I/O");
            Call(window, "ShowTrayMenu", point); await Task.Delay(80);
            Check(recovery.Visibility == Visibility.Collapsed && actions.Skip(1).Take(4).All(b => b.IsEnabled), "A successful explicit read restores normal tray adjustments");
            Call(menu, "Dismiss"); fake.FailNextReadback = true; Call(volume, "Request", 54d); await Idle();
            Call(window, "ShowTrayMenu", point); await Task.Delay(80); int uncertainReads = fake.Reads;
            Call(menu, "InvokeRecovery"); await Task.Delay(80);
            Check(controller.Halted && !controller.CanReadAgain && recovery.Visibility == Visibility.Collapsed && fake.Reads == uncertainReads && fake.Writes.Count == 2,
                "A checksum failure after an attempted write remains latched and cannot use the recovery command");
            Check(WindowsMonitorTransport.NativeReadAttempts == 0 && WindowsMonitorTransport.NativeWriteAttempts == 0, "Appearance, centering and fault recovery regressions use zero real monitor I/O");
        }
        catch (Exception ex) { failures++; Environment.ExitCode = 1; results.Add(new { harnessError = ex.ToString() }); }
        finally { fake.InitialReadRelease.Set(); WriteReport(); await (Task)Call(window!, "ExitAsync")!; }
    }
}
