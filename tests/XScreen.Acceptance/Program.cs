using System.Text.Json;
using XScreen.Core;

if (args.Length > 0)
{
    // This entry point runs fake transport checks only. Hardware acceptance is suspended.
    MonitorSafetyPolicy.EnsureResearchHardwareAllowed();
    return;
}

var fake = new FakeTransport();
var service = new MonitorController(fake);
var supported = new MonitorDevice("fake", "Test", "test", true);
await MustReject(() => service.WriteAsync(supported, new ControlSpec(0xD6, "power", 1), 1));
await MustReject(() => service.WriteAsync(supported, new ControlSpec(0xF5, "aspect-ratio", 1), 1));
await MustReject(() => service.WriteAsync(supported, new ControlSpec(0xF8, "adaptive-sync", 1), 1));
await MustReject(() => service.WriteAsync(supported with { Supported = false }, ControlSpec.Brightness, 99));
await MustReject(() => service.WriteAsync(supported, ControlSpec.Sharpness, 45));
Assert(fake.Writes == 0, "Unsupported/invalid writes must not reach hardware");
fake.IgnoreWrite = true;
await MustReject(() => service.WriteAsync(supported, ControlSpec.Brightness, 99));
Assert(fake.Value == 100, "Ignored native write must remain unchanged and never report success");
fake.IgnoreWrite = false;
fake.Writes = 0;
Assert(service.Halted, "A mismatched readback must latch the controller");
await MustReject(() => service.ReadAsync(supported));
await MustReject(() => service.EnumerateAsync());
await MustReject(() => service.WriteAsync(supported, ControlSpec.Volume, 70));
Assert(fake.Writes == 0, "A halted session must block every later I/O operation");
service = new MonitorController(fake);
await Task.WhenAll(service.WriteAsync(supported, ControlSpec.Brightness, 99), service.WriteAsync(supported, ControlSpec.Brightness, 98));
Assert(fake.MaximumConnections == 1 && fake.Value == 98, "Overlapping requests must serialize write and readback");
using var cancel = new CancellationTokenSource(); cancel.Cancel();
int before = fake.Writes;
await MustReject(() => service.WriteAsync(supported, ControlSpec.Brightness, 97, cancel.Token));
Assert(fake.Writes == before, "Canceled queued request must not write");
Assert(fake.LiveConnections == 0, "All native sessions must be disposed");
var queuedFake = new FakeTransport { IgnoreWrite = true };
var queuedService = new MonitorController(queuedFake);
await MustReject(() => Task.WhenAll(queuedService.WriteAsync(supported, ControlSpec.Brightness, 99), queuedService.WriteAsync(supported, ControlSpec.Volume, 98)));
Assert(queuedFake.Writes == 1 && queuedService.Halted, "Queued changes must stop at the first mismatch, without restore writes");
var failedRead = new FakeTransport { FailAfterWrite = true };
var failedService = new MonitorController(failedRead);
await MustReject(() => failedService.WriteAsync(supported, ControlSpec.Brightness, 99));
Assert(failedService.Halted && failedRead.Reads == 2 && failedRead.Writes == 1, "One original read and one verification read only after failure");
var startupFake = new FakeTransport();
var startup = new MonitorController(startupFake);
await startup.EnumerateAsync(); await startup.ReadAsync(supported); await startup.ReadAsync(supported);
Assert(startupFake.Writes == 0, "Startup and refresh must never write");
const int checksumError = unchecked((int)0xC026258B);
var replayFake = new FakeTransport(); bool failPreRead = false;
replayFake.OnRead = (f, code) => code == 0x10 && failPreRead ? new(code, false, 0, 0, checksumError)
    : new(code, true, code == 0x10 ? f.Value : code == 0x87 ? 50u : 30u, 100);
var replay = new MonitorController(replayFake);
await replay.WriteAsync(supported, ControlSpec.Brightness, 45);
failPreRead = true;
try { await replay.WriteAsync(supported, ControlSpec.Brightness, 100); throw new Exception("Expected checksum failure"); }
catch (ReadbackException ex)
{
    Assert(ex.Actual?.Error == checksumError && !ex.WriteAttempted && ex.Phase == "read-before-write", "Failure must retain original error and pre-write phase");
}
Assert(replay.Halted && replay.CanReadAgain && replayFake.Writes == 1 && replayFake.Value == 45, "Rejected100 must not write or restore");
int failedReads = replayFake.Reads;
await MustReject(() => replay.ReadAsync(supported)); await MustReject(() => replay.WriteAsync(supported, ControlSpec.Volume, 20));
Assert(replayFake.Reads == failedReads && replayFake.Writes == 1, "No automatic activity while paused");
await MustReject(() => replay.ReadAgainAsync(supported));
Assert(replay.Halted && replay.CanReadAgain && replayFake.Reads == failedReads + 1 && replayFake.Writes == 1, "Failed manual read must stop immediately without settings writes");
failPreRead = false;
var refreshed = await replay.ReadAgainAsync(supported);
Assert(!replay.Halted && refreshed.Get(0x10)?.Current == 45 && replayFake.Writes == 1, "Successful manual state read must not replay the requested100");
await replay.WriteAsync(supported, ControlSpec.Brightness, 100);
Assert(replayFake.Value == 100 && replayFake.Writes == 2, "A new explicit request can follow validated manual read");

var uncertainFake = new FakeTransport();
uncertainFake.OnRead = (f, code) => f.Writes > 0 ? new(code, false, 0, 0, checksumError) : new(code, true, f.Value, 100);
var uncertain = new MonitorController(uncertainFake);
await MustReject(() => uncertain.WriteAsync(supported, ControlSpec.Brightness, 99));
int uncertainReads = uncertainFake.Reads;
Assert(uncertain.Halted && !uncertain.CanReadAgain, "The same checksum AFTER a write must remain strictly halted");
await MustReject(() => uncertain.ReadAgainAsync(supported));
Assert(uncertainFake.Reads == uncertainReads && uncertainFake.Writes == 1, "Uncertain write cannot be resumed by refresh or recovery reads");

var throwFake = new FakeTransport { ThrowAfterWrite = true }; var throwing = new MonitorController(throwFake);
await MustReject(() => throwing.WriteAsync(supported, ControlSpec.Brightness, 99));
await MustReject(() => throwing.ReadAgainAsync(supported));
Assert(throwFake.Value == 99 && throwFake.Writes == 1 && !throwing.CanReadAgain, "Native-style throw after mutation must never be classed as a read-only fault");
foreach (uint invalidMax in new uint[] { 0, 255 })
{
    var badRange = new FakeTransport { OnRead = (_, code) => new(code, true, 45, invalidMax) }; var range = new MonitorController(badRange);
    await MustReject(() => range.WriteAsync(supported, ControlSpec.Brightness, 100));
    Assert(range.Halted && !range.CanReadAgain && badRange.Writes == 0, "Range checks must not be weakened for a communication fix");
}
// Revalidation must invalidate requests that queued before it completed.
var queuedReadFake = new FakeTransport { OnRead = (_, code) => new(code, false, 0, 0, checksumError) };
var queuedRead = new MonitorController(queuedReadFake); await MustReject(() => queuedRead.WriteAsync(supported, ControlSpec.Brightness, 99));
using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
queuedReadFake.OnRead = (_, code) => { if (code == 0x10) { entered.Set(); if (!release.Wait(5000)) throw new TimeoutException(); } return new(code, true, 100, 100); };
var readTask = queuedRead.ReadAgainAsync(supported);
Assert(entered.Wait(5000), "Manual read entered");
var staleWrite = queuedRead.WriteAsync(supported, ControlSpec.Brightness, 98); release.Set();
await readTask; await MustReject(() => staleWrite);
Assert(queuedReadFake.Writes == 0, "Queued old-session changes must never become live after manual read");
Assert(WindowsMonitorTransport.NativeReadAttempts == 0 && WindowsMonitorTransport.NativeWriteAttempts == 0, "This entire suite must perform zero native monitor I/O");
// Cancellation before the mutation boundary must still stop a request whose read has started.
using var preReadCancel = new CancellationTokenSource();
var preReadFake = new FakeTransport { OnRead = (f, code) => { preReadCancel.Cancel(); return new(code, true, f.Value, 100); } };
var preReadController = new MonitorController(preReadFake);
await MustReject(() => preReadController.WriteAsync(supported, ControlSpec.Brightness, 90, preReadCancel.Token));
Assert(preReadFake.Writes == 0 && preReadFake.LiveConnections == 0 && !preReadController.Halted,
    "Cancellation during the pre-write read must dispose the connection, skip mutation, and preserve the healthy session");
// Once mutation starts, cancellation must not interrupt the one mandatory readback.
using var afterWriteCancel = new CancellationTokenSource();
var afterWriteFake = new FakeTransport { AfterWrite = () => afterWriteCancel.Cancel() };
var afterWriteController = new MonitorController(afterWriteFake);
var completed = await afterWriteController.WriteAsync(supported, ControlSpec.Brightness, 90, afterWriteCancel.Token);
Assert(completed.Actual == 90 && afterWriteFake.Writes == 1 && afterWriteFake.Reads == 2 && afterWriteFake.LiveConnections == 0 && !afterWriteController.Halted,
    "Cancellation after mutation must finish exactly one readback and release its connection");
Console.WriteLine("PASS: cancellation during pre-write read skips mutation; cancellation after write still completes verification (fake transport)");
Console.WriteLine("PASS: admission, ignored-write detection, serialization, cancellation, connection disposal (fake transport; no hardware claims)");
Console.WriteLine("PASS: terminal fault latch, queued changes blocked, no retry or automatic restore, startup and refresh write zero (fake transport)");
Console.WriteLine("PASS: exact100-to45-to100 checksum replay; rejected pre-write request sends nothing; manual state read does not replay settings");
Console.WriteLine("PASS: post-write checksum/native throw stays halted; invalid ranges still rejected; stale queued writes canceled; native I/O zero");

static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
static async Task MustReject(Func<Task> action) { bool rejected = false; try { await action(); } catch { rejected = true; } Assert(rejected, "Expected rejection"); }
sealed class FakeTransport : IMonitorTransport
{
    public bool IgnoreWrite;
    public bool FailAfterWrite;
    public bool ThrowAfterWrite;
    public Func<FakeTransport, byte, MonitorValue>? OnRead;
    public Action? AfterWrite;
    public uint Value = 100;
    public int Writes, Reads, LiveConnections, MaximumConnections;
    public IReadOnlyList<MonitorDevice> Enumerate() => [new("fake", "Test", "test", true)];
    public IMonitorConnection Open(string deviceId)
    {
        int count = Interlocked.Increment(ref LiveConnections);
        MaximumConnections = Math.Max(MaximumConnections, count);
        return new Connection(this);
    }
    sealed class Connection(FakeTransport owner) : IMonitorConnection
    {
        public MonitorValue Read(byte code) { owner.Reads++; return owner.OnRead?.Invoke(owner, code) ?? new(code, !(owner.FailAfterWrite && owner.Writes > 0), owner.Value, 100); }
        public void Write(byte code, uint value) { owner.Writes++; if (!owner.IgnoreWrite) owner.Value = value; owner.AfterWrite?.Invoke(); if (owner.ThrowAfterWrite) throw new IOException("Fake mutation before native-style failure"); }
        public void Dispose() => Interlocked.Decrement(ref owner.LiveConnections);
    }
}
