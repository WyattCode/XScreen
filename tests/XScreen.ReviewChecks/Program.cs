using System.Collections;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Windowing;
using XScreen.Core;

namespace XScreen.Review;

static class ReviewProgram
{
    public static string ReportPath = "";
    public static bool StartupExit;
    public static string CloseScenario = "";
    public static bool UiDetails;
    public static bool ExitWithEditor;
    public static bool TrayMenu, TraySteps, OsdComparison, OsdControls, ExitOsdApply, OsdFaultName, OsdSynchronization;
    public static bool Hotkeys, HotkeyAudit;
    public static bool ResetSettings, ExitReset, ExitResetReview, WindowState, UiAlignment;
    public static bool TrayAudit;
    public static bool TrayLifecycle;
    public static bool AppearanceRecovery, PlacementPreparedBeforeShow;
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length != 1 && !(args.Length == 2 && args[1] is "--hotkey-audit" or "--hotkeys" or "--ui-alignment" or "--window-state" or "--exit-during-startup" or "--close-during-startup" or "--close-during-write" or "--ui-details" or "--exit-with-editor" or "--tray-menu" or "--tray-audit" or "--tray-lifecycle" or "--appearance-recovery" or "--tray-steps" or "--osd-comparison" or "--osd-controls" or "--exit-osd-apply" or "--osd-fault-name" or "--osd-synchronization" or "--reset-settings" or "--exit-reset" or "--exit-reset-review")) throw new ArgumentException("Provide an offline report path and an optional regression scenario.");
        ReportPath = Path.GetFullPath(args[0]);
        HotkeyAudit = args.Length == 2 && args[1] == "--hotkey-audit";
        Hotkeys = args.Length == 2 && args[1] == "--hotkeys";
        WindowState = args.Length == 2 && args[1] == "--window-state";
        UiAlignment = args.Length == 2 && args[1] == "--ui-alignment";
        StartupExit = args.Length == 2 && args[1] == "--exit-during-startup";
        CloseScenario = args.Length == 2 && args[1].StartsWith("--close-") ? args[1] : "";
        UiDetails = args.Length == 2 && args[1] == "--ui-details";
        ExitWithEditor = args.Length == 2 && args[1] == "--exit-with-editor";
        TrayMenu = args.Length == 2 && args[1] == "--tray-menu";
        TraySteps = args.Length == 2 && args[1] == "--tray-steps";
        OsdComparison = args.Length == 2 && args[1] == "--osd-comparison";
        OsdControls = args.Length == 2 && args[1] == "--osd-controls"; ExitOsdApply = args.Length == 2 && args[1] == "--exit-osd-apply";
        OsdFaultName = args.Length == 2 && args[1] == "--osd-fault-name";
        OsdSynchronization = args.Length == 2 && args[1] == "--osd-synchronization";
        ExitResetReview = args.Length == 2 && args[1] == "--exit-reset-review";
        ResetSettings = args.Length == 2 && args[1] == "--reset-settings"; ExitReset = args.Length == 2 && args[1] == "--exit-reset";
        TrayAudit = args.Length == 2 && args[1] == "--tray-audit";
        TrayLifecycle = args.Length == 2 && args[1] == "--tray-lifecycle";
        AppearanceRecovery = args.Length == 2 && args[1] == "--appearance-recovery";
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(parameters =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new ReviewApplication();
        });
    }
}

public sealed partial class ReviewApplication : Application
{
    public ReviewApplication()
    {
        InitializeComponent();
        UnhandledException += (_, e) => File.WriteAllText(ReviewProgram.ReportPath + ".error", e.Exception.ToString());
    }
    Window? window;
    readonly ReviewTransport fake = new();
    readonly List<object> results = [];
    int failures;
    const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static object Get(object obj, string name) => obj.GetType().GetProperty(name, Members)?.GetValue(obj) ?? obj.GetType().GetField(name, Members)!.GetValue(obj)!;
    static object? Call(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, Members)!.Invoke(obj, args);
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Use the exact delivered UI assembly, but ONLY this fake transport and renderOnly=true.
        var windowType = typeof(XScreen.App.ScreenApplication).Assembly.GetType("XScreen.App.MainWindow")!;
        window = (Window)Activator.CreateInstance(windowType, Members, null, [fake, true], null)!;
        window.Title = "XScreen · 离线回归";
        if (ReviewProgram.WindowState)
            windowType.GetProperty("WindowStatePath", Members)!.SetValue(window, ReviewProgram.ReportPath + ".window-state.json");
        var placement = windowType.GetMethod("PrepareInitialPlacement", Members);
        placement?.Invoke(window, null);
        ReviewProgram.PlacementPreparedBeforeShow = placement != null && !window.AppWindow.IsVisible;
        window.Activate();
        window.DispatcherQueue.TryEnqueue(async () => { if (ReviewProgram.HotkeyAudit) await RunHotkeyAudit(); else if (ReviewProgram.Hotkeys) await RunHotkeys(); else if (ReviewProgram.UiAlignment) await RunUiAlignment(); else if (ReviewProgram.WindowState) await RunWindowState(); else if (ReviewProgram.ExitResetReview) await RunExitResetReview(); else if (ReviewProgram.ResetSettings) await RunResetSettings(); else if (ReviewProgram.ExitReset) await RunExitReset(); else if (ReviewProgram.OsdSynchronization) await RunOsdSynchronization(); else if (ReviewProgram.OsdFaultName) await RunOsdFaultName(); else if (ReviewProgram.ExitOsdApply) await RunExitOsdApply(); else if (ReviewProgram.OsdControls) await RunOsdControls(); else if (ReviewProgram.OsdComparison) await RunOsdComparison(); else if (ReviewProgram.TraySteps) await RunTraySteps(); else if (ReviewProgram.AppearanceRecovery) await RunAppearanceRecovery(); else if (ReviewProgram.TrayLifecycle) await RunTrayLifecycle(); else if (ReviewProgram.TrayAudit) await RunTrayAudit(); else if (ReviewProgram.TrayMenu) await RunTrayMenu(); else if (ReviewProgram.ExitWithEditor) await RunExitWithEditor(); else if (ReviewProgram.UiDetails) await RunUiDetails(); else if (ReviewProgram.CloseScenario.Length > 0) await RunSystemClose(); else if (ReviewProgram.StartupExit) await RunStartupExit(); else await Run(); });
    }
    void Check(bool passed, string name, object? details = null)
    {
        results.Add(new { name, passed, details });
        if (!passed) { failures++; Environment.ExitCode = 1; }
    }
    async Task Until(Func<bool> condition)
    {
        for (int i = 0; i < 500 && !condition(); i++) await Task.Delay(10);
        if (!condition()) throw new TimeoutException("Offline regression condition did not settle.");
    }
    Task Idle() => Until(() => (bool)Get(window!, "ready") && !(bool)Get(window!, "busy") && !(bool)Get(window!, "refreshing"));
    static string Feedback(object row) => ((TextBlock)Get(row, "Feedback")).Text;
    static bool UndoEnabled(object row) => ((Button)Get(row, "undo")).IsEnabled;
    void WriteReport()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ReviewProgram.ReportPath)!);
        File.WriteAllText(ReviewProgram.ReportPath, JsonSerializer.Serialize(new { offline = true, assembly = typeof(XScreen.App.ScreenApplication).Assembly.Location, failures, nativeReadAttempts = WindowsMonitorTransport.NativeReadAttempts, nativeWriteAttempts = WindowsMonitorTransport.NativeWriteAttempts, results }, new JsonSerializerOptions { WriteIndented = true, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals }));
    }
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
        { var child=VisualTreeHelper.GetChild(parent,i); yield return child; foreach(var nested in Descendants(child)) yield return nested; }
    }
    async Task RunTrayMenu()
    {
        bool exitingViaMenu = false;
        try
        {
            await Until(() => fake.InitialReadEntered.IsSet); fake.InitialReadRelease.Set(); await Idle();
            var root = (Grid)Get(window!, "root");
            var point = new Windows.Graphics.PointInt32(850, 650);
            int reads = fake.Reads, enumerations = fake.Enumerations;
            window!.AppWindow.Hide(); Call(window, "ShowTrayMenu", point);
            var menu = (Window)Get(window, "trayMenu"); var menuRoot = (Grid)Get(menu, "root");
            await Until(() => menuRoot.IsLoaded); await Task.Delay(200);
            var actions = ((IEnumerable)Get(menu, "actions")).Cast<Button>().ToArray();
            Check((bool)Get(menu, "IsOpen") && !window.AppWindow.IsVisible && fake.Reads == reads && fake.Enumerations == enumerations && fake.Writes.Count == 0,
                "Opening the native tray popup uses cached state and leaves the main window hidden");
            Check(!menu.AppWindow.IsShownInSwitchers && actions.Length == 6 && actions.All(b => b.IsEnabled), "Popup stays out of task switching and preserves all six actions");
            Check(actions.All(b => b.ActualHeight >= 36) && ((ScrollViewer)Get(menu, "scroll")).ScrollableHeight < 1, "Native menu rows have usable targets and fit the regular popup");
            double BottomGap() => menuRoot.ActualHeight - actions[5].TransformToVisual(menuRoot).TransformPoint(new(0, actions[5].ActualHeight)).Y;
            Check(BottomGap() >= 7 && BottomGap() <= 9, "Popup height fits its content with only normal padding below Exit", new { bottomGap = BottomGap(), menuRoot.ActualHeight });
            await (Task)Call(menu, "Capture", ReviewProgram.ReportPath + ".dark.png")!;
            double oneLineHeight = menuRoot.ActualHeight;
            Call(menu, "ShowAt", point, "未找到受支持的显示器\n远程会话状态不可用", root.ActualTheme); await Task.Delay(150);
            Check(menuRoot.ActualHeight > oneLineHeight && BottomGap() >= 7 && BottomGap() <= 9 && ((ScrollViewer)Get(menu, "scroll")).ScrollableHeight < 1,
                "Wrapped status expands the popup without clipping Exit or adding footer whitespace", new { oneLineHeight, menuRoot.ActualHeight, bottomGap = BottomGap() });
            Call(window, "ShowTrayMenu", point); await Task.Delay(100);

            // Placement math covers all screen edges, negative monitor origins and several DPI scales.
            var place = menu.GetType().GetMethod("Place", BindingFlags.Static | BindingFlags.NonPublic)!;
            bool inside = true;
            foreach (double scale in new[] { 1d, 1.25, 1.75, 2.5 })
            foreach (var work in new[] { new Windows.Graphics.RectInt32(0, 0, 1920, 1040), new Windows.Graphics.RectInt32(-1920, -600, 1920, 1080), new Windows.Graphics.RectInt32(0, 0, 640, 400) })
            foreach (var p in new[] { new Windows.Graphics.PointInt32(work.X, work.Y), new Windows.Graphics.PointInt32(work.X + work.Width, work.Y + work.Height) })
            {
                var rect = (Windows.Graphics.RectInt32)place.Invoke(null, [p, work, scale, 398d])!;
                inside &= rect.X >= work.X && rect.Y >= work.Y && rect.X + rect.Width <= work.X + work.Width && rect.Y + rect.Height <= work.Y + work.Height;
            }
            Check(inside, "Popup placement stays inside work areas across screen edges, negative origins and 100–250 percent DPI");

            Call(menu, "HandleKey", Windows.System.VirtualKey.End);
            Check(actions[5].FocusState != FocusState.Unfocused, "End focuses the last action");
            Call(menu, "HandleKey", Windows.System.VirtualKey.Home);
            Check(actions[0].FocusState != FocusState.Unfocused, "Home focuses the first action");
            Call(menu, "HandleKey", Windows.System.VirtualKey.Escape);
            Check(!(bool)Get(menu, "IsOpen") && !menu.AppWindow.IsVisible && fake.Writes.Count == 0, "Escape dismisses the popup without settings requests");
            Call(menu, "InvokeAction", 2); await Task.Delay(200);
            Check(fake.Writes.Count == 0, "A late action from a dismissed popup does nothing");

            Call(window, "ShowTrayMenu", point); Call(menu, "InvokeAction", 2); await Until(() => fake.Writes.Count == 1); await Idle();
            Check(fake.Writes.Single() == ((byte)0x10, 90u) && !(bool)Get(menu, "IsOpen"), "Brightness decrease uses the existing verified pipeline exactly once and dismisses the popup");
            Call(window, "ShowTrayMenu", point); Call(menu, "InvokeAction", 3); await Until(() => fake.Writes.Count == 2); await Idle();
            Check(fake.Writes.Last() == ((byte)0x62, 40u), "Monitor-volume action uses its own existing control channel");

            Call(window, "ShowTrayMenu", point);
            window.GetType().GetField("refreshing", Members)!.SetValue(window, true);
            Call(menu, "InvokeAction", 2); window.GetType().GetField("refreshing", Members)!.SetValue(window, false);
            await Task.Delay(200);
            Check(fake.Writes.Count == 2 && !(bool)Get(menu, "IsOpen"), "A stale enabled action rechecks current eligibility before dispatch");

            root.RequestedTheme = ElementTheme.Light; await Task.Delay(150); Call(window, "ShowTrayMenu", point); await Task.Delay(150);
            Check(menuRoot.ActualTheme == ElementTheme.Light && ((SolidColorBrush)((Border)Get(menu, "surface")).Background).Color.R > 240, "Popup follows the main window's light theme");
            await (Task)Call(menu, "Capture", ReviewProgram.ReportPath + ".light.png")!;
            Call(menu, "Dismiss"); root.RequestedTheme = ElementTheme.Dark; await Task.Delay(150);
            fake.NoDevices = true; await (Task)Call(window, "RefreshAsync", true)!; Call(window, "ShowTrayMenu", point); await Task.Delay(150);
            Check(actions.Skip(1).Take(4).All(b => !b.IsEnabled) && actions[0].IsEnabled && actions[5].IsEnabled && ((TextBlock)Get(menu, "group")).Text.Contains("不可用"), "Unavailable device disables adjustment actions while open and exit remain usable");
            await (Task)Call(menu, "Capture", ReviewProgram.ReportPath + ".unavailable.png")!;
            Call(menu, "HandleKey", Windows.System.VirtualKey.Home); Call(menu, "HandleKey", Windows.System.VirtualKey.Down);
            Check(actions[5].FocusState != FocusState.Unfocused, "Arrow navigation skips all unavailable adjustments");
            Call(menu, "InvokeAction", 1); await Task.Delay(200);
            Check(fake.Writes.Count == 2, "Unavailable adjustments cannot bypass disabled UI");

            Call(window, "ShowTrayMenu", point); PostMessage(WinRT.Interop.WindowNative.GetWindowHandle(menu), 0x112, (IntPtr)0xF060, IntPtr.Zero); await Until(() => !(bool)Get(menu, "IsOpen"));
            Check(!(bool)Get(window, "exiting"), "System close dismisses only the popup and keeps XScreen alive");
            Call(window, "ShowTrayMenu", point); window.Activate(); await Until(() => !(bool)Get(menu, "IsOpen"));
            Check(!menu.AppWindow.IsVisible, "Activating another window dismisses the popup");
            window.AppWindow.Hide(); Call(window, "ShowTrayMenu", point); Call(menu, "InvokeAction", 0);
            Check(window.AppWindow.IsVisible && !(bool)Get(menu, "IsOpen") && fake.Writes.Count == 2, "Open action restores the main window without changing settings");

            fake.NoDevices = false; await (Task)Call(window, "RefreshAsync", true)!;
            Call(window, "ShowTrayMenu", point); fake.FailNextBrightnessRead = true;
            Call(((IDictionary)Get(window, "rows"))[(byte)0x10]!, "Request", 80d); await Idle();
            Check(!(bool)Get(menu, "IsOpen") && (bool)Get(window, "halted") && fake.Writes.Count == 2, "A control fault dismisses an already-open popup and sends no subsequent setting");
            await (Task)Call(window, "RefreshAsync", true)!;

            fake.WriteEntered = new(); fake.WriteGate = new();
            Call(((IDictionary)Get(window, "rows"))[(byte)0x10]!, "Request", 98d); await Until(() => fake.WriteEntered.IsSet);
            Call(window, "ShowTrayMenu", point);
            bool closed = false;
            window.Closed += (_, _) =>
            {
                closed = true;
                Check(fake.LiveConnections == 0 && fake.Writes.Count == 3 && (bool)Get(menu, "disposed"), "Tray exit waits for in-flight verification, releases its connection and disposes the popup");
                Check(WindowsMonitorTransport.NativeReadAttempts == 0 && WindowsMonitorTransport.NativeWriteAttempts == 0, "Tray regression suite uses fake transport only and performs zero real monitor I/O");
                WriteReport();
            };
            Call(menu, "InvokeAction", 5); exitingViaMenu = true; await Task.Delay(100);
            Check(!closed && (bool)Get(window, "exiting") && !(bool)Get(menu, "IsOpen"), "Tray exit hides promptly while waiting for an in-flight fake write");
            fake.WriteGate.Set();
        }
        catch (Exception ex) { failures++; Environment.ExitCode = 1; results.Add(new { harnessError = ex.ToString() }); WriteReport(); }
        finally
        {
            fake.InitialReadRelease.Set(); fake.WriteGate?.Set();
            if (!exitingViaMenu) { WriteReport(); await (Task)Call(window!, "ExitAsync")!; }
        }
    }
    async Task RunExitWithEditor()
    {
        try
        {
            await Until(()=>fake.InitialReadEntered.IsSet);fake.InitialReadRelease.Set();await Idle();
            var row=((IDictionary)Get(window!,"rows"))[(byte)0x10]!;
            var editor=(Flyout)Get(row,"editor");bool opened=false;editor.Opened+=(_,_)=>opened=true;
            editor.ShowAt((Button)Get(row,"valueButton"));await Until(()=>opened);
            Check(opened,"Exit test starts with the native numeric flyout actually open");
            window!.Closed+=(_,_)=>
            {
                Check(fake.Writes.Count==0 && fake.LiveConnections==0,"Exiting with an open editor releases connections without applying its value");
                Check(WindowsMonitorTransport.NativeReadAttempts==0 && WindowsMonitorTransport.NativeWriteAttempts==0,"Open-editor exit never accesses a real monitor");WriteReport();
            };
            await (Task)Call(window,"ExitAsync")!;
        }
        catch(Exception ex){failures++;Environment.ExitCode=1;results.Add(new{harnessError=ex.ToString()});WriteReport();Exit();}
    }
    async Task RunUiDetails()
    {
        try
        {
            await Until(() => fake.InitialReadEntered.IsSet); fake.InitialReadRelease.Set(); await Idle();
            var root=(Grid)Get(window!,"root"); var presenter=(OverlappedPresenter)window!.AppWindow.Presenter;
            double scale=root.XamlRoot.RasterizationScale;
            window.AppWindow.Resize(new((int)Convert.ToDouble(presenter.PreferredMinimumWidth),(int)Convert.ToDouble(presenter.PreferredMinimumHeight)));
            await Task.Delay(250); root.UpdateLayout();
            var header=(Grid)((Grid)Get(window,"layout")).Children[0];
            var product=(FrameworkElement)VisualTreeHelper.GetParent((TextBlock)Get(window,"connection"));
            bool Inside(FrameworkElement e) { var p=e.TransformToVisual(root).TransformPoint(new(0,0)); return p.X>=-1 && p.Y>=-1 && p.X+e.ActualWidth<=root.ActualWidth+1 && p.Y+e.ActualHeight<=root.ActualHeight+1; }
            Check(Convert.ToDouble(presenter.PreferredMinimumWidth)>=Math.Ceiling(840*scale) && Inside(product) && Inside((Button)Get(window,"refresh")) && Inside((ComboBox)Get(window,"appearance")),
                "Minimum window size preserves header and footer at the current DPI",new{scale,root.ActualWidth,root.ActualHeight,minimumWidth=presenter.PreferredMinimumWidth,minimumHeight=presenter.PreferredMinimumHeight});
            await (Task)Call(window,"Capture",ReviewProgram.ReportPath+".minimum.png")!;
            var scroll=(ScrollViewer)Get(window,"scroll"); scroll.ChangeView(null,scroll.ScrollableHeight,null,true);await Task.Delay(150);
            var minimumRows=(IDictionary)Get(window,"rows");
            bool Reachable(object row)
            {
                var undo=(Button)Get(row,"undo");var position=undo.TransformToVisual(scroll).TransformPoint(new(0,0));
                return position.Y>=-1 && position.Y+undo.ActualHeight<=scroll.ActualHeight+1;
            }
            Check(minimumRows.Values.Cast<object>().All(Reachable),"All lower controls remain reachable by scrolling at minimum window size",new{scroll.ScrollableHeight,scroll.ViewportHeight,scroll.ExtentHeight});
            Call(window,"ResizeLogical",1220,900); await Task.Delay(150);
            root.RequestedTheme=ElementTheme.Light; await Task.Delay(200);
            Check(root.ActualTheme==ElementTheme.Light && ((SolidColorBrush)root.Background).Color.R>240,"Light appearance updates the native root surface");
            await (Task)Call(window,"Capture",ReviewProgram.ReportPath+".light.png")!;
            root.RequestedTheme=ElementTheme.Dark; await Task.Delay(150);
            var rows=(IDictionary)Get(window,"rows"); var brightness=rows[(byte)0x10]!;
            var button=(Button)Get(brightness,"valueButton"); var editor=(Flyout)Get(brightness,"editor"); var number=(NumberBox)Get(brightness,"number");
            bool open=false; editor.Opened+=(_,_)=>open=true;editor.Closed+=(_,_)=>open=false;
            foreach(string raw in new[]{"150","-1","abc","50.5","","4294967296"})
            {
                Call(brightness,"Request",50d); await Idle(); editor.ShowAt(button); await Until(()=>open);
                var input=Descendants(number).OfType<TextBox>().First(); input.Focus(FocusState.Programmatic); input.Text=raw;
                var apply=((StackPanel)editor.Content).Children.OfType<Button>().Single(); apply.Focus(FocusState.Programmatic);await Task.Delay(40);
                int start=fake.Writes.Count; bool accepted=(bool)Call(brightness,"ApplyNumber")!; await Idle();
                Check(!accepted && fake.Writes.Count==start && (uint)Get(brightness,"Current")==50,
                    "Typed input rejects "+raw+" without coercing it into a settings request",new{raw,accepted,number.Value,number.Text,writes=fake.Writes.Count-start});
                var error=brightness.GetType().GetField("inputError",Members)?.GetValue(brightness) as TextBlock;
                Check(error is {Visibility:Visibility.Visible} && error.Text.Contains("步长"),"Validation error for "+raw+" is visible inside the input flyout");
                editor.Hide();await Until(()=>!open);
                if(raw=="abc")
                {
                    editor.ShowAt(button);await Until(()=>open);
                    Check(Descendants(number).OfType<TextBox>().First().Text==number.NumberFormatter.FormatDouble(50) && number.Value==50 && fake.Writes.Count==start,
                        "Reopening an invalid editor resets its raw text to the current value without submitting");
                    editor.Hide();await Until(()=>!open);
                }
            }
            editor.ShowAt(button); await Until(()=>open); number.Value=60;
            int before=fake.Writes.Count;
            var applyButton=((StackPanel)editor.Content).Children.OfType<Button>().Single();
            ((IInvokeProvider)new ButtonAutomationPeer(applyButton).GetPattern(PatternInterface.Invoke)).Invoke();
            await Until(()=>!open);await Idle();
            Check((uint)Get(brightness,"Current")==60 && fake.Writes.Count==before+1,"The native apply button accepts a legal input exactly once and closes its flyout");
            editor.ShowAt(button);await Until(()=>open);Call(brightness,"SetEnabled",false);await Task.Delay(250);
            Check(!open && !number.IsEnabled,"Disabling a channel closes and disables its previously open numeric editor");
            var backgrounds=Descendants(button).Select(d=>d switch{ContentPresenter p=>p.Background,Border b=>b.Background,Panel p=>p.Background,_=>null}).OfType<SolidColorBrush>().Where(b=>b.Color.A>0).Select(b=>b.Color.ToString()).ToArray();
            Check(backgrounds.Length==0,"Disabled large values stay transparent instead of showing a rectangular button plate",new{backgrounds});
            await (Task)Call(window,"Capture",ReviewProgram.ReportPath+".disabled.png")!;
            Call(brightness,"SetEnabled",true);
            editor.ShowAt(button);await Until(()=>open);fake.FailNextBrightnessRead=true;
            Call(brightness,"Request",70d);await Idle();await Task.Delay(300);
            Check(!open && !number.IsEnabled && rows.Values.Cast<object>().All(r=>Feedback(r)=="当前值未确认"),
                "A fault closes the editor and its delayed close event cannot overwrite the unknown-state warning");
            Check(WindowsMonitorTransport.NativeReadAttempts==0 && WindowsMonitorTransport.NativeWriteAttempts==0,"UI detail checks never access a real monitor");
        }
        catch(Exception ex){failures++;Environment.ExitCode=1;results.Add(new{harnessError=ex.ToString()});}
        finally
        {
            fake.InitialReadRelease.Set();WriteReport();
            if(window!=null)
            {
                foreach(var row in ((IDictionary)Get(window,"rows")).Values.Cast<object>()) ((Flyout)Get(row,"editor")).Hide();
                await Task.Delay(200);await (Task)Call(window,"ExitAsync")!;
            }
            else Exit();
        }
    }
    async Task RunSystemClose()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        bool closed = false;
        try
        {
            await Until(() => fake.InitialReadEntered.IsSet);
            bool writing = ReviewProgram.CloseScenario == "--close-during-write";
            if (writing)
            {
                fake.InitialReadRelease.Set(); await Idle();
                fake.WriteEntered = entered; fake.WriteGate = release;
                var rows = (IDictionary)Get(window!, "rows");
                Call(rows[(byte)0x10]!, "Request", 75d); await Until(() => entered.IsSet);
            }
            window!.Closed += (_, _) =>
            {
                closed = true;
                Check(fake.LiveConnections == 0, "System close without a tray drains the active connection before destroying the window");
                Check(writing ? fake.Writes.Count == 1 && fake.PostWriteReads == 1 : fake.Writes.Count == 0,
                    "System close completes an attempted write's single readback; startup close sends no settings");
                Check(WindowsMonitorTransport.NativeReadAttempts == 0 && WindowsMonitorTransport.NativeWriteAttempts == 0, "System-close regression uses zero native monitor I/O");
                WriteReport(); release.Set(); fake.InitialReadRelease.Set();
            };
            Check(PostMessage(WinRT.Interop.WindowNative.GetWindowHandle(window), 0x0010, IntPtr.Zero, IntPtr.Zero), "Deliver a real WM_CLOSE only to the fake regression window");
            await Task.Delay(100);
            if (closed) return;
            Check((bool)Get(window, "exiting") && fake.LiveConnections == 1, "System close suspends window destruction while its I/O is unfinished");
            release.Set(); fake.InitialReadRelease.Set();
            await Until(() => closed);
        }
        catch (Exception ex) { failures++; Environment.ExitCode=1; results.Add(new { harnessError=ex.ToString() }); WriteReport(); Exit(); }
        finally { release.Set(); fake.InitialReadRelease.Set(); }
    }
    async Task RunStartupExit()
    {
        try
        {
            await Until(() => fake.InitialReadEntered.IsSet);
            window!.Closed += (_, _) =>
            {
                Check(fake.LiveConnections == 0 && fake.Writes.Count == 0, "Exit closes the window only after startup connection disposal, with no writes");
                Check(WindowsMonitorTransport.NativeReadAttempts == 0 && WindowsMonitorTransport.NativeWriteAttempts == 0, "Startup-exit regression uses no real monitor I/O");
                WriteReport();
            };
            var exit = (Task)Call(window, "ExitAsync")!;
            Check(!exit.IsCompleted && fake.LiveConnections == 1, "Exit waits while its startup read is still in flight");
            WriteReport(); fake.InitialReadRelease.Set(); await exit;
        }
        catch (Exception ex) { failures++; Environment.ExitCode = 1; results.Add(new { harnessError = ex.ToString() }); WriteReport(); Exit(); }
        finally { fake.InitialReadRelease.Set(); }
    }
    async Task Run()
    {
        try
        {
            await Until(() => fake.InitialReadEntered.IsSet);
            Check(((ICollection<Task>)Get(window!, "operations")).Count > 0,
                "Startup read is tracked so exit can drain its connection");
            fake.InitialReadRelease.Set();
            await Idle();
            Check(fake.LiveConnections == 0, "Startup read releases its fake connection before controls become idle");
            // No physical monitor is enumerated by this transport. Session checks are also injected.
            var session = window!.GetType().GetProperty("RemoteSessionCheck", Members);
            fake.NoDevices = true;
            if (session != null) session.SetValue(window, (Func<bool>)(() => true));
            await (Task)Call(window, "RefreshAsync", true)!;
            var notice = (InfoBar)Get(window, "notice");
            Check(session != null && ((TextBlock)Get(window, "connection")).Text.Contains("远程桌面") && notice.Message.Contains("本地登录") && !notice.Message.Contains("检查连接"),
                "An empty RDP display list explains the session limitation without suggesting a cable fault");
            if (session != null) session.SetValue(window, (Func<bool>)(() => false));
            await (Task)Call(window, "RefreshAsync", true)!;
            Check(((TextBlock)Get(window, "connection")).Text == "未找到受支持的显示器" && notice.Message.Contains("远程桌面") && notice.Message.Contains("本地登录") && notice.Message.Contains("检查连接"), "Missing-device guidance covers console-tagged remote control as well as local connections");
            fake.NoDevices = false;
            if (session != null) session.SetValue(window, (Func<bool>)(() => true));
            await (Task)Call(window, "RefreshAsync", true)!;
            Check(((TextBlock)Get(window, "connection")).Text == "已连接 · DDC/CI" && !notice.IsOpen && fake.Writes.Count == 0,
                "Remote-session guidance never replaces a supported display or generates settings writes");
            if (session != null) session.SetValue(window, (Func<bool>)(() => false));
            var rows = (IDictionary)Get(window!, "rows"); var brightness = rows[(byte)0x10]!; var volume = rows[(byte)0x62]!; var sharpness = rows[(byte)0x87]!;
            var brightnessSlider = (Slider)Get(brightness, "Slider");
            var initialSharpSlider = (Slider)Get(sharpness, "Slider");
            initialSharpSlider.Value += initialSharpSlider.SmallChange;
            Check(initialSharpSlider.SmallChange == 10 && initialSharpSlider.Value == 60 && (string)Get(sharpness, "DisplayValue") == "60",
                "A native small increment keeps sharpness slider, text and legal step aligned", new { initialSharpSlider.SmallChange, slider = initialSharpSlider.Value, text = Get(sharpness, "DisplayValue") });
            Call(sharpness, "CancelDrag");
            int sharpInputStart = fake.Writes.Count; initialSharpSlider.Value = 51;
            Check(initialSharpSlider.Value == 50 && (string)Get(sharpness, "DisplayValue") == "50" && !(bool)Get(sharpness, "Pending") && fake.Writes.Count == sharpInputStart,
                "A non-step programmatic range input snaps consistently without issuing a write");

            Call(brightness, "Request", 50d); Call(brightness, "BeginDrag");
            await Idle(); string confirmedFeedback = Feedback(brightness); int start = fake.Writes.Count;
            Call(brightness, "FinishDrag"); Call(brightness, "FinishDrag"); await Task.Delay(400);
            Check(!(bool)Get(brightness, "Pending") && Feedback(brightness) == confirmedFeedback && confirmedFeedback.StartsWith("参数回读相符") && fake.Writes.Count == start,
                "A settled unchanged drag preserves confirmation and sends no duplicate", new { confirmed = Get(brightness, "Current"), feedback = Feedback(brightness) });

            Call(volume, "Request", 31d); Call(brightness, "Request", 60d); Call(brightness, "BeginDrag");
            await Idle();
            confirmedFeedback = Feedback(brightness); start = fake.Writes.Count;
            Call(brightness, "FinishDrag"); await Task.Delay(400);
            Check(Feedback(brightness) == confirmedFeedback && !(bool)Get(brightness, "Pending") && fake.Writes.Count == start,
                "Queued confirmation finishing during an unchanged drag stays settled");

            bool undoBefore = UndoEnabled(brightness); confirmedFeedback = Feedback(brightness); start = fake.Writes.Count;
            Call(brightness, "BeginDrag"); brightnessSlider.Value = 80;
            Call(brightness, "CancelDrag"); await Task.Delay(400);
            Check(undoBefore && UndoEnabled(brightness) && !(bool)Get(brightness, "Pending") && Feedback(brightness) == confirmedFeedback && fake.Writes.Count == start && (uint)Get(brightness, "Desired") == 60,
                "Pointer cancellation restores undo and confirmed feedback without a write", new { undoBefore, undoAfter = UndoEnabled(brightness), feedback = Feedback(brightness) });
            var undoPeer = new ButtonAutomationPeer((Button)Get(brightness, "undo"));
            ((IInvokeProvider)undoPeer.GetPattern(PatternInterface.Invoke)).Invoke();
            await Until(() => fake.Writes.Count > start); await Idle();
            Check((uint)Get(brightness, "Current") == 50 && fake.Writes.Count == start + 1, "Restored native undo button actually submits its previous value");

            using (var entered = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            {
                fake.WriteEntered = entered; fake.WriteGate = release;
                start = fake.Writes.Count; Call(brightness, "Request", 70d);
                await Until(() => entered.IsSet);
                Call(brightness, "BeginDrag"); brightnessSlider.Value = 80; Call(brightness, "CancelDrag");
                Check((bool)Get(brightness, "Pending") && (uint)Get(brightness, "Desired") == 70 && !UndoEnabled(brightness) && Feedback(brightness) == "正在写入并回读…",
                    "Cancel during in-flight I/O retains its target and unconfirmed status");
                release.Set(); await Idle(); fake.WriteEntered = fake.WriteGate = null; await Task.Delay(400);
                Check((uint)Get(brightness, "Current") == 70 && !(bool)Get(brightness, "Pending") && UndoEnabled(brightness) && fake.Writes.Count == start + 1,
                    "Retained write finishes exactly once; canceled target80 is never sent");
            }
            using (var entered = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            {
                fake.WriteEntered = entered; fake.WriteGate = release;
                start = fake.Writes.Count; Call(brightness, "Request", 80d); await Until(() => entered.IsSet);
                Call(brightness, "BeginDrag"); brightnessSlider.Value = 100; Call(brightness, "CancelDrag"); Call(brightness, "Request", 90d);
                release.Set(); await Idle(); fake.WriteEntered = fake.WriteGate = null;
                Check((uint)Get(brightness, "Current") == 90 && !(bool)Get(brightness, "Pending") && fake.Writes.Count == start + 2 && fake.Writes.Skip(start).All(w => w.Value is 80 or 90),
                    "A new edit after cancellation survives the older readback; canceled100 is not sent");
            }

            using (var secondEntered = new ManualResetEventSlim())
            using (var secondRelease = new ManualResetEventSlim())
            {
                int ordinal = 0;
                fake.BeforeWrite = (_, _) => { if (Interlocked.Increment(ref ordinal) == 2) { secondEntered.Set(); if (!secondRelease.Wait(5000)) throw new TimeoutException(); } };
                start = fake.Writes.Count;
                Call(brightness, "Request", 50d); Call(brightness, "Request", 60d); Call(volume, "Request", 40d);
                await Until(() => secondEntered.IsSet);
                Call(brightness, "Request", 70d); secondRelease.Set(); await Idle(); fake.BeforeWrite = null;
                var order = fake.Writes.Skip(start).Select(w => $"{w.Code:X2}:{w.Value}").ToArray();
                Check(order.SequenceEqual(new[] { "10:50", "10:60", "62:40", "10:70" }),
                    "Continuous edits cannot overtake an already queued different channel", new { order });
            }
            using (var entered = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            {
                fake.WriteEntered = entered; fake.WriteGate = release; start = fake.Writes.Count;
                Call(brightness, "Request", 71d); await Until(() => entered.IsSet);
                double expected = 71;
                foreach (var key in Enumerable.Repeat("increase", 10).Concat(Enumerable.Repeat("decrease", 7)))
                {
                    var peer = new ButtonAutomationPeer((Button)Get(brightness, key));
                    ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke(); expected += key == "increase" ? 1 : -1;
                    await Until(() => (uint)Get(brightness, "Desired") == expected);
                }
                Check(expected == 74 && (string)Get(brightness, "DisplayValue") == "74" && rows.Values.Cast<object>().All(r => ((Slider)Get(r, "Slider")).IsEnabled),
                    "Rapid native plus and minus clicks accumulate while verification is pending");
                release.Set(); await Idle(); fake.WriteEntered = fake.WriteGate = null;
                Check(fake.Writes.Skip(start).Select(w => w.Value).SequenceEqual(new uint[] { 71, 74 }),
                    "A rapid mixed button burst sends only the first and latest targets");
            }
            start = fake.Writes.Count;
            Call(brightness, "BeginDrag"); brightnessSlider.Value = 50; Call(brightness, "FinishDrag");
            await Task.Delay(100); Call(brightness, "BeginDrag"); brightnessSlider.Value = 80;
            await Task.Delay(350);
            Call(brightness, "DebounceTick"); await Task.Delay(25);
            Check(fake.Writes.Count == start && (string)Get(brightness, "DisplayValue") == "80", "A second continuous drag cancels the earlier release debounce");
            Call(brightness, "FinishDrag"); await Until(() => fake.Writes.Count > start); await Idle();
            Check(fake.Writes.Skip(start).Select(w => w.Value).SequenceEqual(new uint[] { 80 }), "Continuous drag submits only the final released target");
            start = fake.Writes.Count;
            for (int i = 0; i < 20; i++) { Call(brightness, "BeginDrag"); brightnessSlider.Value = 20 + i; Call(brightness, "FinishDrag"); Call(brightness, "CancelDrag"); }
            await Task.Delay(400);
            Check(fake.Writes.Count == start && !(bool)Get(brightness, "Pending") && (uint)Get(brightness, "Desired") == 80 && UndoEnabled(brightness),
                "Twenty rapid drag-release-cancel cycles discard every deferred target");

            fake.SharpMaximum = 95; await (Task)Call(window!, "RefreshAsync", true)!;
            var sharpSlider = (Slider)Get(sharpness, "Slider"); var number = (NumberBox)Get(sharpness, "number");
            bool availableBefore = (bool)Get(sharpness, "Available");
            Check(availableBefore && sharpSlider.Maximum == 90 && number.Maximum == 90, "Readback maximum95 aligns slider and number editor to90");
            start = fake.Writes.Count;
            Call(sharpness, "Request", 100d); await Idle();
            Check((uint)Get(sharpness, "Current") == 90 && !(bool)Get(window!, "halted") && fake.Writes.Count == start + 1 && rows.Values.Cast<object>().All(r => ((Slider)Get(r, "Slider")).IsEnabled),
                "Upper-bound sharpness adjustment writes a legal90 and keeps every channel usable");
            start = fake.Writes.Count; Call(sharpness, "Request", 95d); Call(sharpness, "Adjust", 10d); sharpSlider.Value = 95; await Task.Delay(400); await Idle();
            Check((uint)Get(sharpness, "Current") == 90 && fake.Writes.Count == start && !(bool)Get(window!, "halted"), "Repeated upper-bound requests never generate95 or duplicate writes");
            var typedEditor=(Flyout)Get(sharpness,"editor");
            typedEditor.ShowAt((Button)Get(sharpness,"valueButton"));await Task.Delay(200);
            number.Value = 45;
            Check(!(bool)Call(sharpness, "ApplyNumber")! && fake.Writes.Count == start && !(bool)Get(window!, "halted") && Feedback(sharpness).StartsWith("请输入"), "Invalid typed step is an inline input error with zero writes and no session halt");
            number.Value = double.NaN;
            Check(!(bool)Call(sharpness, "ApplyNumber")! && fake.Writes.Count == start && !(bool)Get(window!, "halted"), "Empty numeric input never reaches transport or pauses the session");
            number.Value = 70; Check((bool)Call(sharpness, "ApplyNumber")!, "Valid number input is accepted"); await Idle();
            Check((uint)Get(sharpness, "Current") == 70 && fake.Writes.Count == start + 1 && fake.Writes.Where(w => w.Code == 0x87).All(w => w.Value % 10 == 0 && w.Value <= 95), "Valid typed sharpness reaches real UI write/readback pipeline with legal targets only");
            typedEditor.Hide();await Task.Delay(150);
            fake.SharpMaximum = 100; start = fake.Writes.Count; await (Task)Call(window!, "RefreshAsync", true)!;
            Check(sharpSlider.Maximum == 100 && number.Maximum == 100 && fake.Writes.Count == start, "Later range refresh updates both editors and stays read-only");
            Call(sharpness, "Request", 100d); await Idle(); Call(sharpness, "Request", 90d); await Idle();
            fake.SharpMaximum = 95; start = fake.Writes.Count; await (Task)Call(window!, "RefreshAsync", true)!;
            Check(!UndoEnabled(sharpness) && (uint)Get(sharpness, "Current") == 90 && fake.Writes.Count == start, "Range reduction disables an undo target outside the aligned range");

            fake.FailNextBrightnessRead = true; start = fake.Writes.Count;
            Call(brightness, "Request", 77d); Call(volume, "Request", 40d); await Idle(); await Task.Delay(400);
            var controller = (MonitorController)Get(window!, "controller");
            Check((bool)Get(window!, "halted") && controller.CanReadAgain && fake.Writes.Count == start && rows.Values.Cast<object>().All(r => !(bool)Get(r, "Pending") && !((Slider)Get(r, "Slider")).IsEnabled && (string)Get(r, "DisplayValue") == "—"),
                "Pre-write checksum clears queued edits and leaves all values unknown without writing");
            await (Task)Call(window!, "RefreshAsync", true)!;
            Check(!controller.Halted && !(bool)Get(window!, "halted") && fake.Writes.Count == start, "Manual state re-read resumes without replaying canceled settings");
            fake.FailNextReadback = true; start = fake.Writes.Count;
            Call(brightness, "Request", 78d); Call(volume, "Request", 40d); await Idle(); await Task.Delay(400);
            Check(controller.Halted && !controller.CanReadAgain && fake.Writes.Count == start + 1 && rows.Values.Cast<object>().All(r => !(bool)Get(r, "Pending") && !((Slider)Get(r, "Slider")).IsEnabled && Feedback(r) == "当前值未确认"),
                "Unconfirmed write still halts every control and never sends the queued volume target");
            Call(brightness, "DebounceTick"); Call(brightness, "CancelDrag"); Call(brightness, "DebounceTick"); await Task.Delay(350);
            Check(((IDictionary)Get(window!, "pendingWrites")).Count == 0 && ((ICollection)Get(window!, "commitOrder")).Count == 0 && fake.Writes.Count == start + 1 && controller.Halted && rows.Values.Cast<object>().All(r => Feedback(r) == "当前值未确认" && (string)Get(r, "DisplayValue") == "—"),
                "Faults empty both FIFO and coalesced targets; late timer and cancel callbacks send nothing");
            Check(WindowsMonitorTransport.NativeReadAttempts == 0 && WindowsMonitorTransport.NativeWriteAttempts == 0, "Entire native regression suite performs zero monitor I/O");
        }
        catch (Exception ex) { failures++; results.Add(new { harnessError = ex.ToString() }); Environment.ExitCode = 1; }
        finally
        {
            fake.InitialReadRelease.Set();
            WriteReport();
            if (window != null) await (Task)Call(window, "ExitAsync")!;
            else Exit();
        }
    }
}

sealed class ReviewTransport : IMonitorTransport
{
    readonly Dictionary<byte, uint> values = new() { [0x10] = 100, [0x62] = 30, [0x87] = 50, [0x12] = 70, [0xF9] = 50, [0x15] = 45, [0x14] = 3 };
    public uint SharpMaximum = 100, ContrastMaximum = 100;
    readonly List<(byte Code, uint Value)> writes = [];
    public IReadOnlyList<(byte Code, uint Value)> Writes { get { lock (values) return writes.ToArray(); } }
    public ManualResetEventSlim? WriteEntered, WriteGate, OsdReadEntered, OsdReadGate;
    public readonly ManualResetEventSlim InitialReadEntered = new(), InitialReadRelease = new();
    public Action<byte, uint>? BeforeWrite;
    public int LiveConnections;
    public int PostWriteReads;
    public int Reads, Enumerations;
    public bool NoDevices, SimulateModeSideEffects, SimulateColorSideEffects, IgnoreNextWrite;
    public bool FailNextBrightnessRead, FailNextReadback;
    public byte? FailNextReadCode;
    byte? failedReadbackCode;
    public void SimulateExternalOsd(byte code, uint value) { lock(values) values[code] = value; }
    public IReadOnlyList<MonitorDevice> Enumerate() { Interlocked.Increment(ref Enumerations); return NoDevices ? [] : [new("review-fake", "OFFLINE REVIEW", "fake", true)]; }
    public IMonitorConnection Open(string deviceId) { Interlocked.Increment(ref LiveConnections); return new Connection(this); }
    sealed class Connection(ReviewTransport owner) : IMonitorConnection
    {
        public MonitorValue Read(byte code)
        {
            Interlocked.Increment(ref owner.Reads);
            if (code == 0x15 && owner.OsdReadGate != null) { owner.OsdReadEntered?.Set(); if (!owner.OsdReadGate.Wait(5000)) throw new TimeoutException("Offline OSD read gate timed out."); }
            if (code == 0x10 && !owner.InitialReadRelease.IsSet)
            { owner.InitialReadEntered.Set(); if (!owner.InitialReadRelease.Wait(5000)) throw new TimeoutException("Offline startup read gate timed out."); }
            lock (owner.values)
            {
                if (owner.writes.Count > 0) owner.PostWriteReads++;
                if (code == 0x10 && owner.FailNextBrightnessRead || owner.failedReadbackCode == code || owner.FailNextReadCode == code)
                { owner.FailNextBrightnessRead = false; owner.failedReadbackCode = null; owner.FailNextReadCode = null; return new(code, false, 0, 0, unchecked((int)0xC026258B)); }
                return new(code, true, owner.values[code], code == 0x87 ? owner.SharpMaximum : code == 0x12 ? owner.ContrastMaximum : code == 0xF9 ? 255u : 100);
            }
        }
        public void Write(byte code, uint value)
        {
            owner.BeforeWrite?.Invoke(code, value);
            owner.WriteEntered?.Set();
            if (owner.WriteGate != null && !owner.WriteGate.Wait(5000)) throw new TimeoutException("Offline write gate timed out.");
            lock (owner.values)
            {
                owner.writes.Add((code, value));
                if (owner.IgnoreNextWrite) { owner.IgnoreNextWrite = false; return; }
                owner.values[code] = code == 0xF9 ? value * 5 : value;
                if (owner.SimulateModeSideEffects && code == 0x15) { owner.values[0x10] = 60; owner.values[0x12] = 72; owner.values[0x14] = 9; owner.values[0xF9] = 55; }
                if (owner.SimulateColorSideEffects && code == 0x14) { owner.values[0x10] = 55; owner.values[0x62] = 20; owner.values[0x87] = 40; owner.values[0x15] = 49; owner.values[0x12] = 65; owner.values[0xF9] = 60; }
                if (owner.FailNextReadback) { owner.FailNextReadback = false; owner.failedReadbackCode = code; }
            }
        }
        public void Dispose() => Interlocked.Decrement(ref owner.LiveConnections);
    }
}
