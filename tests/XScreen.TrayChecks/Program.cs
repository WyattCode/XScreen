using System.Runtime.InteropServices;
using System.Text.Json;
using XScreen.App;

int checks = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
var p = new FakeShell(); var logs = new List<string>();
var icon = new NotificationIcon(p, (IntPtr)123, 0x8001, "XScreen.Tray.ico", logs.Add);
Check(p.Events.Select(e => e.Message).SequenceEqual(new uint[] { 0, 4 }), "First add is immediately followed by version negotiation");
Check(p.Events.All(e => e.Data.Tip == "XScreen" && e.Data.Flags == 0x87), "Both Shell calls carry nonblank text and NIF_SHOWTIP for version 4");
Check(p.Events[0].Data.Version == 0 && p.Events[1].Data.Version == 4, "Version is set only for NIM_SETVERSION");
Check(p.Loads.Single().Pixels == 16 && p.Loads.Single().Path == "XScreen.Tray.ico", "At 100 percent the dedicated 16px frame is requested, never a fixed 32px image");
Check(p.Events.All(e => e.Data.Window == (IntPtr)123 && e.Data.Id == 1 && e.Data.Callback == 0x8001), "Registration retains callback identity and owning window");
Check(Marshal.SizeOf<NotificationIconData>() == (IntPtr.Size == 8 ? 976 : 956), "Native NOTIFYICONDATAW size matches the platform ABI");
Check(Marshal.OffsetOf<NotificationIconData>(nameof(NotificationIconData.Tip)).ToInt32() == (IntPtr.Size == 8 ? 40 : 24), "Tooltip is marshalled at the correct native UTF-16 offset");
var buffer = Marshal.AllocHGlobal(Marshal.SizeOf<NotificationIconData>());
try
{
    Marshal.StructureToPtr(p.Events[0].Data, buffer, false);
    Check(Marshal.PtrToStringUni(buffer + Marshal.OffsetOf<NotificationIconData>(nameof(NotificationIconData.Tip)).ToInt32()) == "XScreen",
        "The actual marshalled UTF-16 tooltip buffer contains XScreen rather than an empty string");
}
finally { Marshal.DestroyStructure<NotificationIconData>(buffer); Marshal.FreeHGlobal(buffer); }
foreach (int pixels in new[] { 20, 24, 28, 32, 40, 48, 56, 64 })
{
    int old = p.Current; p.Pixels = pixels; icon.RefreshForDisplay();
    Check(p.Loads.Last().Pixels == pixels && p.Events.Last().Message == 1 && p.Events.Last().Data.Tip == "XScreen" && (p.Events.Last().Data.Flags & 0x80) != 0,
        $"DPI refresh requests exact {pixels}px frame while preserving tooltip flags");
    Check(p.Destroyed.Count(h => h == (IntPtr)old) == 1, $"DPI refresh releases the replaced owned icon exactly once at {pixels}px");
}
int loads = p.Loads.Count, events = p.Events.Count;
icon.RefreshForDisplay();
Check(p.Loads.Count == loads && p.Events.Count == events, "Repeated settings messages at the same DPI do not reload or re-register");
int oldHandle = p.Current;
p.ModifyFails = true; p.AddFails = true; p.Pixels = 24; icon.RefreshForDisplay();
Check(!icon.Available && p.RecoveryEnabled && p.Current == oldHandle && p.Destroyed.Contains((IntPtr)p.LastLoaded) && !p.Destroyed.Contains((IntPtr)oldHandle), "Failed DPI modify/add retains its cached image and schedules registration recovery");
p.ModifyFails = false; p.AddFails = false; icon.Recover(); oldHandle = p.Current;
p.LoadFails = true; p.Pixels = 28; events = p.Events.Count; icon.RefreshForDisplay();
Check(p.Events.Count == events && !p.Destroyed.Contains((IntPtr)oldHandle), "Failed DPI load leaves the existing visible icon untouched");
p.LoadFails = false; icon.RefreshForDisplay();
for (int i = 0; i < 8; i++)
{
    events = p.Events.Count; icon.Recreate();
    Check(p.Events.Skip(events).Select(e => e.Message).SequenceEqual(new uint[] { 0, 4 }) && p.Events.Skip(events).All(e => e.Data.Flags == 0x87 && e.Data.Tip == "XScreen"),
        "Explorer restart renegotiates version and preserves standard tooltip " + i);
}
int retained = p.Current;
p.AddFails = true; p.ModifyFails = true; icon.Recreate();
Check(!icon.Available && p.Destroyed.Contains((IntPtr)p.LastLoaded) && !p.Destroyed.Contains((IntPtr)retained), "Failed re-add releases its candidate and retains the prior owned handle for disposal");
p.AddFails = false; p.ModifyFails = false; icon.Recreate();
Check(icon.Available && p.Destroyed.Contains((IntPtr)retained), "Later Shell recovery replaces and frees the retained handle");
events = p.Events.Count; icon.Dispose(); icon.Dispose(); icon.Recreate(); icon.RefreshForDisplay();
Check(p.Events.Skip(events).Count(e => e.Message == 2) == 1 && !icon.Available, "Dispose deletes once; delayed Explorer/DPI callbacks cannot resurrect an exited tray icon");
Check(p.Destroyed.Distinct().Count() == p.Destroyed.Count, "No owned HICON is destroyed twice across update and recovery paths");
Check(p.LiveOwned.Count == 0, "All allocated icon handles are released after exit");
var fallback = new FakeShell { LoadFails = true };
using (var f = new NotificationIcon(fallback, (IntPtr)123, 0x8001, "missing.ico", logs.Add))
    Check(f.Available && fallback.Current == 9000, "Missing icon uses an available shared fallback");
Check(!fallback.Destroyed.Contains((IntPtr)9000), "Shared system fallback is never destroyed");
var absent = new FakeShell { LoadFails = true, FallbackFails = true };
using (var f = new NotificationIcon(absent, (IntPtr)123, 0x8001, "missing.ico", logs.Add))
    Check(!f.Available && absent.Events.Count == 0, "No image means no invalid Shell registration");
var legacy = new FakeShell { VersionFails = true };
using (var f = new NotificationIcon(legacy, (IntPtr)123, 0x8001, "XScreen.Tray.ico", logs.Add))
    Check(f.Available && legacy.Events.Last().Data.Flags == 0x87, "Version negotiation failure keeps a usable legacy registration with tooltip text");
var edgeCases = RecoveryChecks.Evaluate();
if (args.Length >= 1)
{
    string edgeReport = Path.GetFullPath(args[0]) + ".recovery.json";
    if (File.Exists(edgeReport)) throw new IOException("Use a fresh recovery report path");
    Directory.CreateDirectory(Path.GetDirectoryName(edgeReport)!);
    File.WriteAllText(edgeReport, JsonSerializer.Serialize(edgeCases, new JsonSerializerOptions { WriteIndented = true }));
}
foreach (var result in edgeCases) Check(result.Passed, result.Name);
if (args.Length == 2) IconResources.Verify(args[1], Check);
if (args.Length >= 1)
{
    string report = Path.GetFullPath(args[0]);
    if (File.Exists(report)) throw new IOException("Use a fresh report path");
    Directory.CreateDirectory(Path.GetDirectoryName(report)!);
    File.WriteAllText(report, JsonSerializer.Serialize(new { checks, failures = 0, mode = "fake Shell registration; native icon file decoding only", nativeIconDecoding = args.Length == 2, applicationStarted = false, realShellWrites = 0, monitorReads = 0, monitorWrites = 0 }, new JsonSerializerOptions { WriteIndented = true }));
}
Console.WriteLine($"{checks} tray registration checks passed; real Shell/monitor calls 0/0.");

sealed class FakeShell : INotificationIconPlatform
{
    public int Pixels = 16, Current, LastLoaded;
    public int SmallIconPixels => Pixels;
    public bool LoadFails, FallbackFails, ModifyFails, AddFails, VersionFails;
    public uint? ThrowOnNotify;
    public bool RecoveryEnabled;
    public readonly List<bool> TimerChanges = [];
    public Action<uint>? AfterNotify;
    public readonly List<(string Path, int Pixels)> Loads = [];
    public readonly List<(uint Message, NotificationIconData Data)> Events = [];
    public readonly List<IntPtr> Destroyed = [];
    public readonly HashSet<IntPtr> LiveOwned = [];
    int next = 100;
    public IntPtr Load(string path, int pixels)
    {
        Loads.Add((path, pixels)); if (LoadFails) return IntPtr.Zero;
        LastLoaded = ++next; LiveOwned.Add((IntPtr)LastLoaded); return (IntPtr)LastLoaded;
    }
    public IntPtr Fallback() => FallbackFails ? IntPtr.Zero : (IntPtr)9000;
    public bool Notify(uint message, ref NotificationIconData data)
    {
        Events.Add((message, data));
        if (message == ThrowOnNotify) throw new InvalidOperationException("Injected Shell failure");
        if (message == 0 && AddFails || message == 1 && (ModifyFails || Current == 0) || message == 4 && VersionFails) return false;
        if (message is 0 or 1) Current = (int)data.Icon;
        if (message == 2) Current = 0;
        AfterNotify?.Invoke(message);
        return true;
    }
    public void Destroy(IntPtr image) { Destroyed.Add(image); LiveOwned.Remove(image); }
    public bool SetRecoveryTimer(bool enabled) { TimerChanges.Add(enabled); RecoveryEnabled = enabled; return true; }
}
