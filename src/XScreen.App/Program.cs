using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using XScreen.Core;

namespace XScreen.App;

static class Program
{
    internal const string WindowTitle = "XScreen";
    static Mutex? instance;
    public static string? RenderDirectory { get; private set; }
    public static string? PassiveCapture { get; private set; }
    public static bool ShowTray { get; private set; }
    public static string? TrayCapture { get; private set; }
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--render-only") RenderDirectory = Path.GetFullPath(args[1]);
        else if (args.Length == 2 && args[0] == "--capture-once") PassiveCapture = Path.GetFullPath(args[1]);
        else if (args.Length is 1 or 2 && args[0] == "--show-tray-menu") { ShowTray = true; if (args.Length == 2) TrayCapture = Path.GetFullPath(args[1]); }
        else if (args.Length != 0) throw new ArgumentException("Unknown arguments. No automatic hardware test mode exists.");
        if (RenderDirectory == null)
        {
            instance = new Mutex(true, @"Local\XScreen.UltraGear", out bool first);
            if (!first) { AppLog.Mode = "instance-forward"; AppLog.Write("entry", "Forwarding an interface request to the existing instance; no monitor transport constructed."); NativeWindow.BringExistingToFront(ShowTray); return; }
        }
        AppLog.Mode = RenderDirectory == null ? "live" : "offline-render";
        AppLog.Write("entry", RenderDirectory == null ? "Starting XScreen 0.3.31 LIVE; startup performs reads only." : "Starting isolated offline rendering; WindowsMonitorTransport will not be constructed.");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => AppLog.Write("fatal", e.ExceptionObject.ToString() ?? "Unknown failure");
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(parameters =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new ScreenApplication();
        });
        GC.KeepAlive(instance);
    }
}
public sealed partial class ScreenApplication : Application
{
    MainWindow? window;
    public ScreenApplication() { InitializeComponent(); UnhandledException += (_, e) => AppLog.Write("ui-unhandled", e.Exception.ToString()); }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        bool offline = Program.RenderDirectory != null;
        window = new MainWindow(offline ? new RenderTransport() : new WindowsMonitorTransport(), offline);
        window.PrepareInitialPlacement();
        window.Activate(); window.AppWindow.Show();
    }
}
// Explicit offline renderer only. Never a fallback and never constructed in the live window.
sealed class RenderTransport : IMonitorTransport
{
    public bool FailNextBrightnessRead;
    public bool FailWrites;
    public int WriteAttempts;
    public readonly List<(byte Code, uint Value)> Writes = [];
    readonly Dictionary<byte, uint> values = new() { [0x10] = 68, [0x62] = 24, [0x87] = 50, [0x12] = 70, [0xF9] = 50, [0x15] = 45, [0x14] = 3 };
    public IReadOnlyList<MonitorDevice> Enumerate() => [new("render-only", "OFFLINE SAMPLE", "sample", true)];
    public IMonitorConnection Open(string id) => new OfflineSample(this);
    sealed class OfflineSample(RenderTransport owner) : IMonitorConnection
    {
        public MonitorValue Read(byte code)
        {
            if (code == 0x10 && owner.FailNextBrightnessRead) { owner.FailNextBrightnessRead = false; return new(code, false, 0, 0, unchecked((int)0xC026258B)); }
            lock (owner.values) return new(code, true, owner.values.GetValueOrDefault(code), 100);
        }
        public void Write(byte code, uint value)
        {
            Interlocked.Increment(ref owner.WriteAttempts);
            if (owner.FailWrites) throw new InvalidOperationException("离线替身模拟写入失败；没有访问显示器。");
            lock (owner.values) { owner.values[code] = code == 0xF9 ? value * 5 : value; owner.Writes.Add((code, value)); }
        }
        public void Dispose() { }
    }
}
