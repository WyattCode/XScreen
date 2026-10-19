using System.Collections;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using Windows.System;
using XScreen.Core;

namespace XScreen.Review;

public sealed partial class ReviewApplication
{
    // Messages target this offline window only; no global keyboard or monitor settings are used.
    static void PostKey(Window target, VirtualKey key)
    {
        var handle = GetFocus();
        GetWindowThreadProcessId(handle, out uint owner);
        if (owner != Environment.ProcessId || GetAncestor(handle, 2) != WinRT.Interop.WindowNative.GetWindowHandle(target))
            throw new InvalidOperationException("Offline keyboard test does not own the focused input window.");
        PostMessage(handle, 0x100, (IntPtr)(int)key, (IntPtr)1);
        PostMessage(handle, 0x101, (IntPtr)(int)key, (IntPtr)unchecked((int)0xC0000001));
    }
    [DllImport("user32.dll")] static extern IntPtr GetFocus();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    async Task RunTrayAudit()
    {
        try
        {
            await Until(() => fake.InitialReadEntered.IsSet); fake.InitialReadRelease.Set(); await Idle();
            var point = new PointInt32(850, 650);
            window!.AppWindow.Hide(); Call(window, "ShowTrayMenu", point);
            var menu = (Window)Get(window, "trayMenu"); var menuRoot = (Grid)Get(menu, "root");
            await Until(() => menuRoot.IsLoaded); await Task.Delay(150);
            var actions = ((IEnumerable)Get(menu, "actions")).Cast<Button>().ToArray();
            var scroll = (ScrollViewer)Get(menu, "scroll");
            int previewKeys = 0;
            menuRoot.AddHandler(UIElement.PreviewKeyDownEvent, new Microsoft.UI.Xaml.Input.KeyEventHandler((_, _) => previewKeys++), true);
            int reads = fake.Reads, enumerations = fake.Enumerations;
            Check(!scroll.IsTabStop && menuRoot.TabFocusNavigation == Microsoft.UI.Xaml.Input.KeyboardNavigationMode.Cycle, "Normal tab navigation stays on menu actions instead of the scroll container");

            int positionChanges = 0;
            void PositionChanged(AppWindow sender, AppWindowChangedEventArgs args) { if (args.DidPositionChange) positionChanges++; }
            menu.AppWindow.Changed += PositionChanged;
            actions[4].Focus(FocusState.Keyboard);
            for (int i = 0; i < 12; i++) Call(window, "ShowTrayMenu", point);
            await Task.Delay(100); menu.AppWindow.Changed -= PositionChanged;
            Check(positionChanges == 0, "Repeated open requests never move an already-positioned visible menu through the raw tray anchor", new { positionChanges });
            Check(actions[4].FocusState != FocusState.Unfocused, "Repeated notifications preserve the current keyboard action");

            scroll.IsTabStop = true;
            bool scrollFocused = scroll.Focus(FocusState.Programmatic);
            Check(scrollFocused && actions.All(b => b.FocusState == FocusState.Unfocused), "Focus-recovery scenario starts outside every action button");
            Call(menu, "HandleKey", VirtualKey.Up);
            Check(actions[5].FocusState != FocusState.Unfocused, "Up from the scroll container selects the last available action");
            scroll.Focus(FocusState.Programmatic); Call(menu, "HandleKey", VirtualKey.Down);
            Check(actions[0].FocusState != FocusState.Unfocused, "Down from the scroll container selects the first available action");
            scroll.IsTabStop = false;

            // Force a short native viewport without changing any display or Windows settings.
            scroll.MaxHeight = 140; menuRoot.UpdateLayout(); await Task.Delay(100);
            Check(scroll.ScrollableHeight > 0, "Short-viewport scenario has actual native overflow");
            actions[0].Focus(FocusState.Keyboard); int keysBefore = previewKeys; PostKey(menu, VirtualKey.Down); await Task.Delay(150);
            Check(previewKeys > keysBefore, "Posted keyboard input reaches the native XAML preview route", new { keysBefore, previewKeys });
            Check(actions[1].FocusState != FocusState.Unfocused, "A routed Down key moves focus even when the ScrollViewer can consume scrolling keys", new { focused = Array.FindIndex(actions, b => b.FocusState != FocusState.Unfocused), scroll.VerticalOffset });
            PostKey(menu, VirtualKey.Escape); await Task.Delay(100);
            Check(!(bool)Get(menu, "IsOpen"), "A routed Escape dismisses the native popup from a scrolled viewport");

            Call(window, "ShowTrayMenu", point); await Task.Delay(100);
            scroll.ChangeView(null, scroll.ScrollableHeight, null, true); actions[5].Focus(FocusState.Keyboard); await Task.Delay(100);
            double oldOffset = scroll.VerticalOffset;
            Call(menu, "Dismiss"); Call(window, "ShowTrayMenu", point); await Task.Delay(150);
            Check(oldOffset > 0 && scroll.VerticalOffset < 1, "A fresh popup open resets the previous scroll position to reveal its heading", new { oldOffset, reopenedOffset = scroll.VerticalOffset });
            scroll.MaxHeight = double.PositiveInfinity; Call(menu, "Dismiss"); Call(window, "ShowTrayMenu", point); await Task.Delay(100);

            actions[5].Focus(FocusState.Keyboard); PostKey(menu, VirtualKey.Tab); await Task.Delay(150);
            Check(actions[0].FocusState != FocusState.Unfocused, "A routed Tab wraps from Exit to Open without focusing an unrelated surface");

            actions[0].Focus(FocusState.Keyboard); PostKey(menu, VirtualKey.Enter); await Task.Delay(150);
            Check(!(bool)Get(menu, "IsOpen") && window.AppWindow.IsVisible && fake.Writes.Count == 0, "Native Enter invokes Open once without changing a setting");
            window.AppWindow.Hide(); Call(window, "ShowTrayMenu", point); await Task.Delay(100);
            var invoke = (IInvokeProvider)new ButtonAutomationPeer(actions[3]).GetPattern(PatternInterface.Invoke);
            invoke.Invoke(); invoke.Invoke(); await Task.Delay(400); await Idle();
            Check(fake.Writes.Count == 1 && fake.Writes.Single() == ((byte)0x62, 40u), "Two queued native Click invocations cannot apply a dismissed menu action twice", new { writes = fake.Writes.Count });
            Call(menu, "InvokeAction", 3); await Task.Delay(150);
            Check(fake.Writes.Count == 1, "A late click after dismissal remains canceled");

            Call(window, "ShowTrayMenu", point); Call(menu, "HandleKey", VirtualKey.End); PostKey(menu, VirtualKey.Escape); await Task.Delay(100);
            Check(!(bool)Get(menu, "IsOpen") && fake.Writes.Count == 1, "Cancel from the exit row never invokes Exit or a setting");
            Check(fake.Enumerations == enumerations && fake.Reads == reads + 2 && WindowsMonitorTransport.NativeReadAttempts == 0 && WindowsMonitorTransport.NativeWriteAttempts == 0,
                "Popup navigation and cancellation add no reads; only the explicit fake-volume action performs its pre-read and readback", new { readsBefore = reads, readsAfter = fake.Reads, fake.Enumerations });

            // Only UI geometry is replaced; this does not alter any display's actual work area.
            var areaProperty = menu.GetType().GetProperty("WorkAreaProvider", Members)!;
            var regularArea = areaProperty.GetValue(menu);
            areaProperty.SetValue(menu, (Func<PointInt32, RectInt32>)(_ => new RectInt32(300, 200, 196, 280)));
            Call(window, "ShowTrayMenu", new PointInt32(490, 470)); await Task.Delay(200);
            Check(menu.AppWindow.Position.X >= 300 && menu.AppWindow.Position.Y >= 200 && menu.AppWindow.Position.X + menu.AppWindow.Size.Width <= 496 && menu.AppWindow.Position.Y + menu.AppWindow.Size.Height <= 480 && scroll.ScrollableHeight > 0,
                "An actual narrow native popup stays within the fake work area and scrolls its content", new { menuRoot.ActualWidth, menuRoot.ActualHeight, scroll.ScrollableHeight });
            var volumeTitle = ((Grid)actions[3].Content).Children.OfType<TextBlock>().First();
            Check(volumeTitle.IsTextTrimmed && volumeTitle.TextTrimming == TextTrimming.CharacterEllipsis && AutomationProperties.GetName(actions[3]).Contains("显示器音量增加"),
                "Narrow rows ellipsize text while preserving the complete accessible action name");
            Call(menu, "HandleKey", VirtualKey.End); await Task.Delay(200);
            var exitPosition = actions[5].TransformToVisual(scroll).TransformPoint(new(0, 0));
            Check(actions[5].FocusState != FocusState.Unfocused && exitPosition.Y >= -1 && exitPosition.Y + actions[5].ActualHeight <= scroll.ViewportHeight + 1,
                "End makes Exit fully visible in a constrained native viewport", new { y = exitPosition.Y, height = actions[5].ActualHeight, scroll.ViewportHeight });
            await (Task)Call(menu, "Capture", ReviewProgram.ReportPath + ".narrow.png")!;
            areaProperty.SetValue(menu, regularArea); Call(menu, "Dismiss"); Call(window, "ShowTrayMenu", point); await Task.Delay(100);

            areaProperty.SetValue(menu, (Func<PointInt32, RectInt32>)(_ => throw new InvalidOperationException("Offline UI work-area failure")));
            Call(menu, "Reposition");
            Check(window.AppWindow.IsVisible && window.GetType().GetField("trayMenu", Members)!.GetValue(window) == null && (bool)Get(menu, "disposed"),
                "A popup layout exception disposes only the bad popup and returns to the main window");
            Check(!(bool)Get(window, "halted") && ((InfoBar)Get(window, "notice")).Title == "托盘菜单暂不可用" && fake.Writes.Count == 1 && fake.Reads == reads + 2,
                "UI-only failure reports its own warning without pausing monitor control or issuing I/O");
            Call(menu, "InvokeAction", 2); Call(menu, "Dispose");
            Check(fake.Writes.Count == 1, "Late callbacks and repeated disposal of the failed popup are harmless");
            Call(window, "ShowTrayMenu", point); var replacement = (Window)Get(window, "trayMenu"); await Task.Delay(100);
            Check(!ReferenceEquals(menu, replacement) && (bool)Get(replacement, "IsOpen") && fake.Writes.Count == 1 && fake.Reads == reads + 2,
                "An explicit later open creates a clean popup without replaying any setting or re-reading hardware");
            Call(replacement, "InvokeAction", 3); await Until(() => fake.Writes.Count == 2); await Idle();
            Check(fake.Writes.Last() == ((byte)0x62, 50u) && fake.Reads == reads + 4 && WindowsMonitorTransport.NativeReadAttempts == 0 && WindowsMonitorTransport.NativeWriteAttempts == 0,
                "An explicit action after UI recovery still uses one fake write/readback pipeline with zero real monitor I/O");

            fake.NoDevices = true; await (Task)Call(window, "RefreshAsync", true)!;
            var mainRoot = (Grid)Get(window, "root");
            foreach (var theme in new[] { ElementTheme.Dark, ElementTheme.Light })
            {
                mainRoot.RequestedTheme = theme; await Task.Delay(100); Call(window, "ShowTrayMenu", point); await Task.Delay(100);
                replacement = (Window)Get(window, "trayMenu");
                var disabledButton = ((IEnumerable)Get(replacement, "actions")).Cast<Button>().ElementAt(3);
                var foreground = ((SolidColorBrush)Descendants(disabledButton).OfType<ContentPresenter>().First().Foreground).Color;
                var background = ((SolidColorBrush)((Border)Get(replacement, "surface")).Background).Color;
                double alpha = foreground.A / 255d * disabledButton.Opacity;
                double Luminance(double r, double g, double b)
                {
                    double Linear(double v) { v /= 255; return v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4); }
                    return .2126 * Linear(r) + .7152 * Linear(g) + .0722 * Linear(b);
                }
                double ink = Luminance(foreground.R * alpha + background.R * (1 - alpha), foreground.G * alpha + background.G * (1 - alpha), foreground.B * alpha + background.B * (1 - alpha));
                double paper = Luminance(background.R, background.G, background.B);
                double contrast = (Math.Max(ink, paper) + .05) / (Math.Min(ink, paper) + .05);
                Check(!disabledButton.IsEnabled && contrast >= 2.5, "Disabled action text stays legible without duplicate dimming in " + theme, new { contrast, alpha });
                await (Task)Call(replacement, "Capture", ReviewProgram.ReportPath + ".disabled-" + theme + ".png")!;
            }
        }
        catch (Exception ex) { failures++; Environment.ExitCode = 1; results.Add(new { harnessError = ex.ToString() }); }
        finally { fake.InitialReadRelease.Set(); WriteReport(); await (Task)Call(window!, "ExitAsync")!; }
    }
}
