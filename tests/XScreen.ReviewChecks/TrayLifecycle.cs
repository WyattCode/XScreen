using System.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;
using XScreen.Core;

namespace XScreen.Review;

public sealed partial class ReviewApplication
{
    async Task RunTrayLifecycle()
    {
        bool closed = false;
        try
        {
            await Until(() => fake.InitialReadEntered.IsSet); fake.InitialReadRelease.Set(); await Idle();
            var point = new PointInt32(850, 650);
            window!.AppWindow.Hide(); Call(window, "ShowTrayMenu", point);
            var menu = (Window)Get(window, "trayMenu"); var root = (Grid)Get(menu, "root");
            int popupCloses = 0; menu.Closed += (_, _) => popupCloses++;
            await Until(() => root.IsLoaded); await Task.Delay(150);
            var actions = ((IEnumerable)Get(menu, "actions")).Cast<Button>().ToArray();
            int reads = fake.Reads, enumerations = fake.Enumerations;
            // A fixed geometry provider belongs only to this fake-transport window.
            menu.GetType().GetProperty("WorkAreaProvider", Members)!.SetValue(menu,
                (Func<PointInt32, RectInt32>)(_ => new RectInt32(0, 0, 1920, 1040)));
            Call(menu, "Dismiss"); Call(window, "ShowTrayMenu", point); await Task.Delay(100);
            var original = menu.AppWindow.Position;
            actions[4].Focus(FocusState.Keyboard);
            for (int i = 0; i < 12; i++) Call(window, "ShowTrayMenu", new PointInt32(906 + i, 650));
            await Task.Delay(100);
            Check(menu.AppWindow.Position.X == original.X && menu.AppWindow.Position.Y == original.Y,
                "Duplicate open notifications with a changed anchor leave the visible popup in place",
                new { originalX = original.X, originalY = original.Y, afterX = menu.AppWindow.Position.X, afterY = menu.AppWindow.Position.Y });
            Check(actions[4].FocusState != FocusState.Unfocused,
                "Duplicate changed-anchor notifications preserve the current keyboard action");
            Call(menu, "Dismiss"); Call(window, "ShowTrayMenu", new PointInt32(906, 650)); await Task.Delay(100);
            Check(menu.AppWindow.Position.X == original.X + 56,
                "A fresh open uses the new anchor after the preceding popup has been dismissed");

            actions[3].Focus(FocusState.Keyboard);
            Check(actions[3].FocusState != FocusState.Unfocused, "Eligibility transition starts on a focused adjustment");
            window.GetType().GetField("refreshing", Members)!.SetValue(window, true);
            Call(window, "ShowTrayMenu", point); await Task.Delay(100);
            Check(actions.Skip(1).Take(4).All(b => !b.IsEnabled) && actions[0].FocusState != FocusState.Unfocused,
                "Disabling the focused adjustment moves focus to Open rather than Exit",
                new { focused = Array.FindIndex(actions, b => b.FocusState != FocusState.Unfocused) });
            actions[5].Focus(FocusState.Keyboard); Call(window, "ShowTrayMenu", point); await Task.Delay(80);
            Check(actions[5].FocusState != FocusState.Unfocused,
                "A deliberately focused Exit remains focused when eligibility has not changed");
            window.GetType().GetField("refreshing", Members)!.SetValue(window, false);
            Call(window, "ShowTrayMenu", point); await Task.Delay(80);
            Check(actions[5].FocusState != FocusState.Unfocused && actions.All(b => b.IsEnabled),
                "Re-enabling adjustments preserves the user's current enabled action");

            bool focusCycles = true;
            for (int i = 0; i < 8; i++)
            {
                window.AppWindow.Show(); window.Activate(); await Until(() => !(bool)Get(menu, "IsOpen"));
                focusCycles &= !menu.AppWindow.IsVisible;
                Call(menu, "InvokeAction", 1); Call(menu, "InvokeAction", 5);
                window.AppWindow.Hide(); Call(window, "ShowTrayMenu", point); await Task.Delay(50);
                focusCycles &= ReferenceEquals(menu, Get(window, "trayMenu")) && (bool)Get(menu, "IsOpen") && actions[0].FocusState != FocusState.Unfocused;
                actions[4].Focus(FocusState.Keyboard);
            }
            Check(focusCycles && !(bool)Get(window, "exiting"),
                "Eight focus transfers dismiss, cancel late actions and reopen the same popup with safe initial focus");
            Check(fake.Reads == reads && fake.Enumerations == enumerations && fake.Writes.Count == 0,
                "Opening, repositioning, eligibility updates and focus transfers perform no device I/O");

            Call(menu, "Dismiss"); Call(window, "ShowWindow");
            fake.WriteEntered = new(); fake.WriteGate = new();
            var row = ((IDictionary)Get(window, "rows"))[(byte)0x10]!;
            Call(row, "Request", 99d); await Until(() => fake.WriteEntered.IsSet);
            Check(fake.LiveConnections == 1 && fake.Writes.Count == 0 && fake.PostWriteReads == 0,
                "Exit-race scenario starts with a blocked fake write and an open fake connection");
            window.Closed += (_, _) =>
            {
                closed = true;
                Check(fake.Writes.Count == 1 && fake.PostWriteReads == 1 && fake.LiveConnections == 0,
                    "Final native close occurs only after the attempted fake write, readback and connection disposal",
                    new { writes = fake.Writes.Count, fake.PostWriteReads, fake.LiveConnections });
                Check((bool)Get(menu, "disposed") && !(bool)Get(menu, "IsOpen") && popupCloses == 1,
                    "Final close disposes the hidden popup exactly once", new { popupCloses });
                Check(WindowsMonitorTransport.NativeReadAttempts == 0 && WindowsMonitorTransport.NativeWriteAttempts == 0,
                    "Lifecycle regression never reads or writes a real monitor");
                WriteReport(); fake.WriteGate.Set();
                // The failing baseline may close before its asynchronous exit resumes.
                window.DispatcherQueue.TryEnqueue(() => Exit());
            };
            Call(window, "ShowTrayMenu", point); await Task.Delay(80);
            var invoke = (IInvokeProvider)new ButtonAutomationPeer(actions[5]).GetPattern(PatternInterface.Invoke);
            invoke.Invoke(); invoke.Invoke(); await Until(() => (bool)Get(window, "exiting"));
            Check(!(bool)Get(menu, "IsOpen") && !menu.AppWindow.IsVisible,
                "Two queued native Exit clicks dismiss the popup and start one draining exit");
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            PostMessage(hwnd, 0x112, (IntPtr)0xF060, IntPtr.Zero);
            PostMessage(hwnd, 0x112, (IntPtr)0xF060, IntPtr.Zero);
            await Task.Delay(150); if (closed) return;
            Check(!closed && fake.LiveConnections == 1 && fake.PostWriteReads == 0,
                "Repeated system Close cannot bypass the draining exit while the fake write is blocked");
            window.AppWindow.Hide();
            for (int i = 0; i < 8; i++)
            {
                Call(window, "ShowWindow"); Call(window, "ShowTrayMenu", point); Call(menu, "InvokeAction", 1);
                _ = Call(window, "ExitAsync");
            }
            await Task.Delay(80);
            Check(!window.AppWindow.IsVisible && !(bool)Get(menu, "IsOpen") && fake.Writes.Count == 0,
                "Late Open, popup, adjustment and Exit requests cannot reopen or restart a draining session");
            fake.WriteGate.Set(); await Until(() => closed);
        }
        catch (Exception ex)
        {
            fake.InitialReadRelease.Set(); fake.WriteGate?.Set();
            failures++; Environment.ExitCode = 1; results.Add(new { harnessError = ex.ToString() }); WriteReport(); Exit();
        }
    }
}
