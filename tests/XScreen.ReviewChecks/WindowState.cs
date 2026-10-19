using System.Reflection;
using System.Text.Json;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using XScreen.Core;

namespace XScreen.Review;

public sealed partial class ReviewApplication
{
    async Task RunWindowState()
    {
        try
        {
            await Until(() => fake.InitialReadEntered.IsSet); fake.InitialReadRelease.Set(); await Idle();
            int reads = fake.Reads;
            var type = window!.GetType();
            string path = (string)Get(window, "WindowStatePath");
            var store = type.Assembly.GetType("XScreen.App.WindowStateStore")!;
            var savedType = type.Assembly.GetType("XScreen.App.SavedWindowState")!;
            object State(double width, double height, bool max = false) => Activator.CreateInstance(savedType, width, height, max)!;
            object? Store(string name, params object[] args) => store.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args);
            JsonElement Saved() => JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(path));
            var presenter = (OverlappedPresenter)window.AppWindow.Presenter;
            presenter.Restore(); ResizeAsUser(window, new SizeInt32(1050, 740));
            await Task.Delay(500); Call(window, "SaveWindowState");
            var normal = Saved();
            Check(!normal.GetProperty("Maximized").GetBoolean() && normal.GetProperty("Width").GetDouble() > 0,
                "A user resize is persisted in logical outer dimensions");
            double width = normal.GetProperty("Width").GetDouble(), height = normal.GetProperty("Height").GetDouble();
            presenter.Maximize(); await Task.Delay(500); Call(window, "SaveWindowState");
            var maximized = Saved();
            Check(maximized.GetProperty("Maximized").GetBoolean()
                && Math.Abs(maximized.GetProperty("Width").GetDouble() - width) <= 1
                && Math.Abs(maximized.GetProperty("Height").GetDouble() - height) <= 1,
                "Maximize records the restored dimensions, never the full-screen size");
            presenter.Minimize(); await Task.Delay(100); Call(window, "SaveWindowState");
            Check(Saved().GetProperty("Maximized").GetBoolean() && File.ReadAllText(path) == maximized.GetRawText(),
                "Minimize does not overwrite the normal dimensions or maximized preference");
            Call(window,"ShowWindow"); await Task.Delay(80);
            Check(presenter.State == OverlappedPresenterState.Maximized,
                "Restoring a minimized formerly-maximized window works without a notification-area object");
            presenter.Restore(); await Task.Delay(80); var normalSize = window.AppWindow.Size;
            presenter.Minimize(); Call(window,"ShowWindow"); await Task.Delay(80);
            Check(presenter.State == OverlappedPresenterState.Restored && window.AppWindow.Size.Width == normalSize.Width
                && window.AppWindow.Size.Height == normalSize.Height, "Restoring a minimized normal window retains its size");
            presenter.Restore(); await Task.Delay(100); presenter.Maximize(); await Task.Delay(100); Call(window, "SaveWindowState");
            window.AppWindow.Hide(); Call(window, "SaveWindowState");
            Check(Saved().GetProperty("Maximized").GetBoolean(), "Tray-hidden geometry does not replace the saved window state");
            Call(window, "ShowWindow"); await Task.Delay(100);
            Check(presenter.State == OverlappedPresenterState.Maximized, "Showing a hidden maximized window preserves maximization");
            var native = type.Assembly.GetType("XScreen.App.NativeWindow")!;
            var command = native.GetMethod("ShowCommand", BindingFlags.NonPublic | BindingFlags.Static)!;
            Check((int)command.Invoke(null, [false])! == 5 && (int)command.Invoke(null, [true])! == 9,
                "Tray and second-instance native commands restore only minimized windows");
            Check(Store("Load", path) != null && (bool)Store("Save", path, State(1000, 720))!, "Window preferences round-trip through their version-independent store");

            Window? reopened = null;
            try
            {
                // A fresh real WinUI window, before first activation; no transport reads occur.
                reopened = (Window)Activator.CreateInstance(type, Members, null, [fake, true], null)!;
                type.GetProperty("WindowStatePath", Members)!.SetValue(reopened, path);
                Call(reopened, "PrepareInitialPlacement");
                var work = DisplayArea.GetFromWindowId(reopened.AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
                var expected = (RectInt32)Store("Bounds", work, GetWindowScale(reopened), State(1000, 720))!;
                Check(!reopened.AppWindow.IsVisible && reopened.AppWindow.Size.Width == expected.Width && reopened.AppWindow.Size.Height == expected.Height,
                    "A fresh window restores the saved size before first visibility");
                Check(Math.Abs(2 * (reopened.AppWindow.Position.X - work.X) + reopened.AppWindow.Size.Width - work.Width) <= 1,
                    "Restoring a saved size retains startup centering");
                reopened.AppWindow.Resize(new SizeInt32(1010, 730)); var size = reopened.AppWindow.Size; Call(reopened, "PrepareInitialPlacement");
                Check(reopened.AppWindow.Size.Width == size.Width && reopened.AppWindow.Size.Height == size.Height,
                    "Repeated placement does not undo a later user resize");
            }
            finally { if (reopened != null) { type.GetField("closeApproved", Members)!.SetValue(reopened, true); reopened.Close(); } }

            Check((bool)Store("Save", path, State(1000, 720, true))!, "A maximized preference can be saved independently of the version folder");
            reopened = null;
            try
            {
                reopened = (Window)Activator.CreateInstance(type, Members, null, [fake, true], null)!;
                type.GetProperty("WindowStatePath", Members)!.SetValue(reopened, path);
                Call(reopened, "PrepareInitialPlacement");
                Check(((OverlappedPresenter)reopened.AppWindow.Presenter).State == OverlappedPresenterState.Maximized,
                    "A fresh window restores saved maximization");
                // Restore down must reveal the saved normal size, not the maximized bounds.
                ((OverlappedPresenter)reopened.AppWindow.Presenter).Restore();
                var work = DisplayArea.GetFromWindowId(reopened.AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
                var expected = (RectInt32)Store("Bounds", work, GetWindowScale(reopened), State(1000, 720))!;
                Check(reopened.AppWindow.Size.Width == expected.Width && reopened.AppWindow.Size.Height == expected.Height,
                    "Restoring down after reopening recovers the saved normal size");
            }
            finally { if (reopened != null) { type.GetField("closeApproved", Members)!.SetValue(reopened, true); reopened.Close(); } }

            Store("Save",path,State(16000,16000));
            reopened = null;
            try
            {
                var isolated = new ReviewTransport(); isolated.InitialReadRelease.Set();
                reopened = (Window)Activator.CreateInstance(type,Members,null,[isolated,true],null)!;
                type.GetProperty("WindowStatePath",Members)!.SetValue(reopened,path);
                Call(reopened,"PrepareInitialPlacement"); reopened.Activate();
                await Until(() => (bool)Get(reopened,"ready") && !(bool)Get(reopened,"refreshing"));
                await Task.Delay(420); Call(reopened,"SaveWindowState");
                Check(Saved().GetProperty("Width").GetDouble() == 16000 && Saved().GetProperty("Height").GetDouble() == 16000,
                    "A startup work-area clamp never replaces the user's saved normal dimensions");
                ((OverlappedPresenter)reopened.AppWindow.Presenter).Maximize(); await Task.Delay(80); Call(reopened,"SaveWindowState");
                Check(Saved().GetProperty("Width").GetDouble() == 16000 && Saved().GetProperty("Maximized").GetBoolean(),
                    "Maximizing a temporarily clamped window updates its state without losing the original size");
                ((OverlappedPresenter)reopened.AppWindow.Presenter).Restore(); await Task.Delay(80);
                var clamped = reopened.AppWindow.Size;
                ResizeAsUser(reopened,new SizeInt32(clamped.Width-1,clamped.Height)); await Task.Delay(100); Call(reopened,"SaveWindowState");
                Check(Math.Abs(Saved().GetProperty("Width").GetDouble()-reopened.AppWindow.Size.Width/GetWindowScale(reopened)) <= .01,
                    "A one-pixel native user resize replaces a formerly clamped preference");
                double chosenWidth=Saved().GetProperty("Width").GetDouble(), chosenHeight=Saved().GetProperty("Height").GetDouble();
                reopened.AppWindow.Resize(new SizeInt32(950,720)); await Task.Delay(100); Call(reopened,"SaveWindowState");
                Check(Saved().GetProperty("Width").GetDouble()==chosenWidth && Saved().GetProperty("Height").GetDouble()==chosenHeight,
                    "A later automatic/programmatic size change does not overwrite the user's chosen dimensions");
                var gestureHandle=WinRT.Interop.WindowNative.GetWindowHandle(reopened);
                SendSizeMessage(gestureHandle,0x231,IntPtr.Zero,IntPtr.Zero);
                reopened.AppWindow.Move(new(reopened.AppWindow.Position.X+8,reopened.AppWindow.Position.Y+4));
                reopened.AppWindow.Resize(new SizeInt32(940,710));
                SendSizeMessage(gestureHandle,0x232,IntPtr.Zero,IntPtr.Zero); Call(reopened,"SaveWindowState");
                Check(Saved().GetProperty("Width").GetDouble()==chosenWidth,
                    "A move-only native gesture cannot turn an automatic geometry adjustment into a saved resize");
                ResizeAsUser(reopened,new SizeInt32(980,740));
                await Task.Delay(100); Call(reopened,"SaveWindowState");
                Check(Saved().GetProperty("Width").GetDouble() < 16000
                    && Math.Abs(Saved().GetProperty("Width").GetDouble()-reopened.AppWindow.Size.Width/GetWindowScale(reopened)) <= 1,
                    "A later deliberate resize replaces the old saved dimensions");
                Check(isolated.Writes.Count == 0 && isolated.Reads == 7, "Clamped restart performs one fake startup read and no settings writes");
            }
            finally { if (reopened != null) { type.GetField("closeApproved",Members)!.SetValue(reopened,true); reopened.Close(); } }

            bool fits = true;
            foreach (double scale in new[] { 1d, 1.25, 1.75, 2.5 })
            foreach (var work in new[] { new RectInt32(0,0,1920,1040), new RectInt32(-2560,-200,2560,1400), new RectInt32(0,40,800,560) })
            foreach (var saved in new[] { State(1000,720), State(12000,12000), State(200,100) })
            {
                var b = (RectInt32)Store("Bounds", work, scale, saved)!;
                fits &= b.Width > 0 && b.Height > 0 && b.X >= work.X && b.Y >= work.Y && b.X+b.Width <= work.X+work.Width && b.Y+b.Height <= work.Y+work.Height;
            }
            Check(fits, "Stored sizes fit small screens, negative monitor origins and DPI changes");
            Check(!(bool)Store("Save", path, State(double.NaN, 720))! && !(bool)Store("Save", path, State(0, 720))!
                && !(bool)Store("Save", path, State(20000, 720))!, "Invalid sizes are refused before file mutation");
            File.WriteAllText(path, "{broken"); Check(Store("Load", path) == null, "Malformed preferences fall back safely");
            File.WriteAllText(path, "{\"Width\":-1,\"Height\":720,\"Maximized\":true}");
            Check(Store("Load", path) == null, "Invalid stored dimensions cannot move or maximize the window");
            Check(!(bool)Store("Save", Path.Combine(path, "blocked.json"), State(1000,720))! && !(bool)Get(window, "halted"),
                "Preference I/O failure does not crash the UI or halt monitor control");
            Check((string)store.GetProperty("DefaultPath", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)! ==
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XScreen", "window-state.json"),
                "Preferences use one user-local location across application versions");
            Check(fake.Reads == reads && fake.Writes.Count == 0 && WindowsMonitorTransport.NativeReadAttempts == 0 && WindowsMonitorTransport.NativeWriteAttempts == 0,
                "Resize, maximize, hide, reopen and persistence tests perform zero additional monitor I/O");
        }
        catch (Exception ex) { failures++; Environment.ExitCode = 1; results.Add(new { harnessError = ex.ToString() }); }
        finally { fake.InitialReadRelease.Set(); WriteReport(); await (Task)Call(window!, "ExitAsync")!; }
    }
    static double GetWindowScale(Window target) => Math.Max(1, GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(target)) / 96d);
    static void ResizeAsUser(Window target, SizeInt32 size)
    {
        var handle=WinRT.Interop.WindowNative.GetWindowHandle(target);
        SendSizeMessage(handle,0x231,IntPtr.Zero,IntPtr.Zero);
        var rect=new SizingRect { Left=target.AppWindow.Position.X,Top=target.AppWindow.Position.Y,
            Right=target.AppWindow.Position.X+size.Width,Bottom=target.AppWindow.Position.Y+size.Height };
        IntPtr buffer=System.Runtime.InteropServices.Marshal.AllocHGlobal(System.Runtime.InteropServices.Marshal.SizeOf<SizingRect>());
        try
        {
            System.Runtime.InteropServices.Marshal.StructureToPtr(rect,buffer,false);
            SendSizeMessage(handle,0x214,(IntPtr)8,buffer);
            target.AppWindow.Resize(size);
        }
        finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(buffer); SendSizeMessage(handle,0x232,IntPtr.Zero,IntPtr.Zero); }
    }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct SizingRect { public int Left,Top,Right,Bottom; }
    [System.Runtime.InteropServices.DllImport("user32.dll",EntryPoint="SendMessageW",ExactSpelling=true)]
    static extern IntPtr SendSizeMessage(IntPtr handle,uint message,IntPtr w,IntPtr l);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);
}
