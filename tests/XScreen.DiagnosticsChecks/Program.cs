using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using XScreen.App;
using XScreen.Core;

if (args.Length == 3 && args[0] == "--session-child")
{
    var childLog = new SessionLog(args[1]);
    for (int i = 0; i < 30; i++) childLog.Write(args[2], "fixture", "音量\n" + i);
    return;
}
if (args.Length != 1) throw new ArgumentException("Provide a fresh offline report path.");
string reportPath = Path.GetFullPath(args[0]), work = reportPath + ".files";
if (Directory.Exists(work)) throw new InvalidOperationException("Use a fresh evidence directory.");
Directory.CreateDirectory(work);
var checks = new List<object>(); int failures = 0;
void Check(bool ok, string name, object? detail = null) { checks.Add(new { name, passed = ok, detail }); if (!ok) failures++; }
var device = new MonitorDevice("fake-device", "fake", "fake", true);
const int checksum = unchecked((int)0xC026258B);
async Task<Exception?> Failure(Func<Task> action) { try { await action(); return null; } catch (Exception ex) { return ex; } }

var trace = new List<MonitorIoTrace>(); var fake = new Fake();
var controller = new MonitorController(new TracingMonitorTransport(fake, trace.Add, "offline-fixture"));
await controller.WriteAsync(device, ControlSpec.Volume, 53);
Check(trace.Select(t => t.Operation).SequenceEqual(new[] { "open", "read", "write", "read", "close" }), "Successful adjustment forwards exactly one pre-read, write, readback and close");
Check(trace.Where(t => t.Operation is "read" or "write").All(t => t.ConcurrentIoCalls == 1) && fake.Reads == 2 && fake.Writes == 1 && fake.Closes == 1, "Tracing adds no I/O or overlapping calls");
Check(trace[1].Reply?.Current == 30 && trace[2].Target == 53 && trace[3].Reply?.Current == 53 && !trace[1].WriteCallEntered && trace[3].WriteCallEntered, "Trace distinguishes write intent, write entry and returned readback values");
Check(trace.Select(t => t.Connection).Distinct().Count() == 1 && trace[0].DeviceKey != device.Id && trace[0].DeviceKey.Length == 16, "One transaction has one connection identity and a hashed device key");
Check(trace.Zip(trace.Skip(1)).All(p => p.First.Sequence < p.Second.Sequence) && trace.All(t => t.DurationMs >= 0 && t.Thread > 0) && trace[3].IdleBeforeMs >= 190, "Per-call timing preserves controller verification delay without inventing a new delay");
int traceStart = trace.Count; await controller.WriteAsync(device, ControlSpec.Volume, 53);
Check(trace.Skip(traceStart).Select(t => t.Operation).SequenceEqual(new[] { "open", "read", "close" }) && fake.Writes == 1, "A same-value request is recorded without falsely reporting a transport write");

trace.Clear(); fake = new Fake { Value = 53, FailReadAt = 1 };
controller = new MonitorController(new TracingMonitorTransport(fake, t => { trace.Add(t); Marshal.SetLastPInvokeError(0); }, "offline-fixture"));
var rejected = await Failure(() => controller.WriteAsync(device, ControlSpec.Volume, 44));
Check(rejected is ReadbackException { Phase: "read-before-write", WriteAttempted: false, Actual.Error: checksum } && controller.CanReadAgain, "Volume53-to44 failure retains checksum code and pre-write classification");
Check(fake.Reads == 1 && fake.Writes == 0 && fake.Closes == 1 && trace.All(t => !t.WriteCallEntered), "Pre-write checksum failure causes no write, retry or restore");
Check(trace.Single(t => t.Operation == "read").Reply is { Success: false, Error: checksum } && rejected is ReadbackException r && ReferenceEquals(r.Actual, trace.Single(t => t.Operation == "read").Reply), "An observer changing last-error state cannot corrupt the already captured native reply");
int pausedEvents = trace.Count; await Failure(() => controller.WriteAsync(device, ControlSpec.Volume, 45));
Check(trace.Count == pausedEvents && fake.Reads == 1 && fake.Writes == 0, "Fault-latched requests never reach even the tracing transport");

fake = new Fake(); controller = new MonitorController(new TracingMonitorTransport(fake, _ => throw new IOException("log unavailable"), "offline-fixture"));
await controller.WriteAsync(device, ControlSpec.Volume, 53);
Check(!controller.Halted && fake.Reads == 2 && fake.Writes == 1 && fake.Closes == 1, "A failing diagnostic observer cannot interrupt successful write verification");
fake = new Fake { FailReadAt = 1 }; controller = new MonitorController(new TracingMonitorTransport(fake, _ => throw new IOException("log unavailable"), "offline-fixture"));
var observerFailure = await Failure(() => controller.WriteAsync(device, ControlSpec.Volume, 44));
Check(observerFailure is ReadbackException { Actual.Error: checksum, WriteAttempted: false } && fake.Writes == 0 && fake.Closes == 1, "Logging failure does not mask the original read fault or prevent disposal");

trace.Clear(); fake = new Fake { ThrowWrite = true }; controller = new MonitorController(new TracingMonitorTransport(fake, trace.Add, "offline-fixture"));
var writeError = await Failure(() => controller.WriteAsync(device, ControlSpec.Volume, 53));
Check(writeError is ReadbackException { Phase: "write", WriteAttempted: true } && !controller.CanReadAgain && trace.Single(t => t.Operation == "write").ExceptionType == typeof(IOException).FullName, "A backend write exception remains an uncertain attempted write, with its original exception traced");
Check(fake.Reads == 1 && fake.Writes == 1 && fake.Closes == 1, "A thrown write is not retried or followed by a hidden recovery action");
trace.Clear(); fake = new Fake { ThrowReadAt = 2 }; controller = new MonitorController(new TracingMonitorTransport(fake, trace.Add, "offline-fixture"));
var postError = await Failure(() => controller.WriteAsync(device, ControlSpec.Volume, 53));
Check(postError is ReadbackException { Phase: "read-after-write", WriteAttempted: true } && trace.Last(t => t.Operation == "read") is { WriteCallEntered: true, ExceptionType: not null } && !controller.CanReadAgain, "A thrown post-write read retains the attempted-write boundary and terminal latch");

var concurrentTrace = new ConcurrentBag<MonitorIoTrace>();
using var readEntered = new CountdownEvent(2); using var release = new ManualResetEventSlim();
fake = new Fake { DuringRead = () => { readEntered.Signal(); if (!release.Wait(5000)) throw new TimeoutException(); } };
var direct = new TracingMonitorTransport(fake, concurrentTrace.Add, "offline-fixture");
using (var first = direct.Open("fixture-one")) using (var second = direct.Open("fixture-two"))
{
    var a = Task.Run(() => first.Read(0x62)); var b = Task.Run(() => second.Read(0x62));
    bool both = readEntered.Wait(5000); release.Set(); await Task.WhenAll(a, b);
    Check(both && concurrentTrace.Any(t => t.Operation == "read" && t.ConcurrentIoCalls == 2), "Passive trace exposes deliberately overlapping fake calls instead of hiding them behind a new lock");
    first.Dispose(); first.Dispose();
}
Check(fake.Reads == 2 && fake.Writes == 0 && fake.Closes == 2 && concurrentTrace.Select(t => t.Sequence).Distinct().Count() == concurrentTrace.Count, "Concurrent fixture trace has unique sequence identities and idempotent wrapper disposal");

string parallelDirectory = Path.Combine(work, "threads"); var parallelLog = new SessionLog(parallelDirectory);
await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Task.Run(() => { for (int j = 0; j < 25; j++) parallelLog.Write("offline-fixture", "fixture", $"音量\n{i}:{j}"); })));
var threadLines = File.ReadAllLines(parallelLog.SessionPath).Select(l => JsonDocument.Parse(l)).ToArray();
Check(threadLines.Length == 200 && threadLines.Select(l => l.RootElement.GetProperty("sequence").GetInt64()).SequenceEqual(Enumerable.Range(1, 200).Select(i => (long)i)), "Parallel log writers keep all200 JSON records intact and ordered");
Check(threadLines.All(l => l.RootElement.GetProperty("mode").GetString() == "offline-fixture" && l.RootElement.GetProperty("pid").GetInt32() == Environment.ProcessId && l.RootElement.GetProperty("message").GetString()!.StartsWith("音量\n")), "Canonical records preserve mode, process identity and escaped multiline text");
foreach (var doc in threadLines) doc.Dispose();

string lockedDirectory = Path.Combine(work, "aggregate-locked"); Directory.CreateDirectory(lockedDirectory);
string aggregate = Path.Combine(lockedDirectory, "app.log"); var independent = new SessionLog(lockedDirectory);
using (var locked = new FileStream(aggregate, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
{
    // Exact old failure behavior: a sharing violation was swallowed and the event disappeared.
    bool oldLost = false;
    try { File.AppendAllText(aggregate, "old event\n"); } catch (IOException) { oldLost = true; }
    independent.Write("offline-fixture", "checksum", "0xC026258B");
    Check(oldLost && File.ReadAllLines(independent.SessionPath).Length == 1, "When the shared legacy log is locked, the independent session still preserves the event");
}
string blockedDirectory = Path.Combine(work, "not-a-directory"); File.WriteAllText(blockedDirectory, "fixture");
new SessionLog(blockedDirectory).Write("offline-fixture", "fixture", "logging is unavailable");
Check(File.ReadAllText(blockedDirectory) == "fixture", "An unwritable log destination is nonfatal and does not alter the conflicting file");

string processesDirectory = Path.Combine(work, "processes");
Process Child(string mode)
{
    var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
    info.ArgumentList.Add("--session-child"); info.ArgumentList.Add(processesDirectory); info.ArgumentList.Add(mode);
    return Process.Start(info)!;
}
using var childA = Child("fixture-live"); using var childB = Child("fixture-offline");
bool endedA = childA.WaitForExit(10000), endedB = childB.WaitForExit(10000);
var processLogs = Directory.GetFiles(Path.Combine(processesDirectory, "sessions"), "*.jsonl");
var processDocs = processLogs.SelectMany(f => File.ReadAllLines(f)).Select(l => JsonDocument.Parse(l)).ToArray();
Check(endedA && endedB && childA.ExitCode == 0 && childB.ExitCode == 0 && processLogs.Length == 2 && processDocs.Length == 60 && processDocs.Select(d => d.RootElement.GetProperty("pid").GetInt32()).Distinct().Count() == 2, "Two real offline test processes retain separate complete session logs in a shared output folder");
foreach (var doc in processDocs) doc.Dispose();
Check(WindowsMonitorTransport.NativeReadAttempts == 0 && WindowsMonitorTransport.NativeWriteAttempts == 0, "All diagnostics checks use fake calls and file I/O only, with zero native monitor access");
File.WriteAllText(reportPath, JsonSerializer.Serialize(new { offline = true, failures, nativeReadAttempts = WindowsMonitorTransport.NativeReadAttempts, nativeWriteAttempts = WindowsMonitorTransport.NativeWriteAttempts, checks }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"{checks.Count} diagnostics checks; {failures} failures; native monitor I/O0/0.");
Environment.ExitCode = failures == 0 ? 0 : 1;

sealed class Fake : IMonitorTransport
{
    public uint Value = 30;
    public int Reads, Writes, Closes;
    public int FailReadAt, ThrowReadAt;
    public bool ThrowWrite;
    public Action? DuringRead;
    public IReadOnlyList<MonitorDevice> Enumerate() => [new("fake-device", "fake", "fake", true)];
    public IMonitorConnection Open(string id) => new Connection(this);
    sealed class Connection(Fake owner) : IMonitorConnection
    {
        public MonitorValue Read(byte code)
        {
            int n = Interlocked.Increment(ref owner.Reads); owner.DuringRead?.Invoke();
            if (n == owner.ThrowReadAt) throw new IOException("fixture read error");
            return n == owner.FailReadAt ? new(code, false, 0, 0, unchecked((int)0xC026258B)) : new(code, true, owner.Value, 100);
        }
        public void Write(byte code, uint value) { Interlocked.Increment(ref owner.Writes); owner.Value = value; if (owner.ThrowWrite) throw new IOException("fixture mutation before throw"); }
        public void Dispose() => Interlocked.Increment(ref owner.Closes);
    }
}
