using System.Text.Json;
using XScreen.Core;
using XScreen.Probes;

int passed = 0;
void Check(string name, Action test) { test(); passed++; Console.WriteLine("PASS: " + name); }
static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
static void Reject(Action action) { try { action(); } catch { return; } throw new Exception("Expected rejection."); }
static ProbePlan Mute() => new(0x8D, "mute", _ => 1);
static ProbePlan Gamma() => new(0x72, "gamma", _ => 25600);
static ProbePlan Language() => new(0xCC, "language", _ => 2);
static ProbeSession Session(FakeMonitor fake, ProbeJournal journal, Action<ProbeJournal>? save = null) =>
    new(fake, journal, save ?? (_ => { }), () => { });

Check("Adaptive Sync and aspect ratio reject every target/recovery value", () =>
{
    foreach (byte code in new byte[] { 0xF5, 0xF8 })
    foreach (uint value in new uint[] { 0, 1, 2, 255, 65535 })
    { Reject(() => MonitorSafetyPolicy.EnsureWriteAllowed(code)); Reject(() => ProbeWritePolicy.EnsureAllowed(code, value)); }
});
Check("Disabled plan rejects the complete batch before I/O", () =>
{
    foreach (byte code in new byte[] { 0xF5, 0xF8 })
    {
        var fake = new FakeMonitor(); var journal = new ProbeJournal();
        Reject(() => Session(fake, journal).RunPlans([Mute(), new(code, "disabled", _ => 1)]));
        Assert(fake.Calls == 0, "A disabled later plan must prevent preceding I/O too.");
    }
});
Check("Research hardware entry is closed", () => Reject(MonitorSafetyPolicy.EnsureResearchHardwareAllowed));
Check("Target mismatch halts without automatic recovery or later tests", () =>
{
    var fake = new FakeMonitor { OnWrite = (_, _, _) => { } }; var journal = new ProbeJournal();
    Reject(() => Session(fake, journal).RunPlans([Mute(), Language()]));
    Assert(fake.Writes.SequenceEqual(new[] { ((byte)0x8D, 1u) }), "No restore or next write after mismatch.");
    Assert(journal.Steps.Count == 1 && journal.Steps[0].ParameterRestoreNeeded, "Pending mutation must remain explicit.");
    Assert(journal.Status == "halted-review-required" && journal.Error != null, "Failure must be terminal.");
});
Check("Failed target read stops immediately and does not issue a diagnostic read", () =>
{
    var fake = new FakeMonitor(); var journal = new ProbeJournal();
    fake.OnRead = (f, code) => f.Writes.Count > 0 ? new(code, false, 0, 0, 0, -1071241843) : f.Value(code);
    Reject(() => Session(fake, journal).RunPlans([Mute(), Language()]));
    Assert(fake.Reads == 2 && fake.Writes.Count == 1, "One original read and one failed read, no retries or restore.");
});
Check("Failure after a prior successful step still halts and clears the baseline-match flag", () =>
{
    var fake = new FakeMonitor(); var journal = new ProbeJournal();
    fake.OnWrite = (f, code, value) => { if (code != 0x72) f.Values[code] = value; };
    Reject(() => Session(fake, journal).RunPlans([Mute(), Gamma(), Language()]));
    Assert(fake.Writes.SequenceEqual(new[] { ((byte)0x8D, 1u), ((byte)0x8D, 2u), ((byte)0x72, 25600u) }), "No gamma restore or language write after failure.");
    Assert(!journal.ParameterReadbacksMatchBaseline && journal.Steps.Count == 2, "Earlier matches cannot hide a later fault.");
});
Check("Failed write may have mutated the device; no cleanup writes follow", () =>
{
    var fake = new FakeMonitor { OnWrite = (f, code, value) => { f.Values[code] = value; throw new IOException("write failed after mutation"); } };
    var journal = new ProbeJournal();
    Reject(() => Session(fake, journal).RunPlans([Mute(), Language()]));
    Assert(fake.Writes.Count == 1 && fake.Reads == 1 && journal.Steps[0].ParameterRestoreNeeded, "Stop after uncertain write.");
});
Check("Restore failure is attempted once, with no retry or next plan", () =>
{
    var fake = new FakeMonitor(); var journal = new ProbeJournal();
    fake.OnWrite = (f, code, value) => { if (f.Writes.Count == 2) throw new IOException("restore failure"); f.Values[code] = value; };
    Reject(() => Session(fake, journal).RunPlans([Mute(), Language()]));
    Assert(fake.Writes.Count == 2 && journal.Steps[0].TargetReadbackMatched && journal.Steps[0].ParameterRestoreNeeded, "No retry after restore failure.");
});
Check("Black stabilizer never falls back to an unscaled restore", () =>
{
    var fake = new FakeMonitor(); var journal = new ProbeJournal();
    fake.OnWrite = (f, code, value) => { if (value == 10) throw new IOException("encoded restore failed"); f.Values[code] = value * 5; };
    Reject(() => Session(fake, journal).RunProfile("black"));
    Assert(fake.Writes.SequenceEqual(new[] { ((byte)0xF9, 9u), ((byte)0xF9, 10u) }), "No raw 50 fallback or retry.");
});
Check("RGB failure cannot trigger a parent preset restore or later manual mode", () =>
{
    var fake = new FakeMonitor(); var journal = new ProbeJournal();
    fake.OnWrite = (f, code, value) => { if (code == 0x14) f.Values[code] = value; };
    Reject(() => Session(fake, journal).RunProfile("color"));
    Assert(fake.Writes.SequenceEqual(new[] { ((byte)0x14, 11u), ((byte)0x16, 49u) }), "No dependency finally-writes after child failure.");
    Assert(journal.Steps.All(s => s.ParameterRestoreNeeded), "Parent and child remain pending for manual review.");
});
Check("Unsuccessful baseline performs no writes", () =>
{
    var fake = new FakeMonitor { OnSnapshot = _ => [new(0x8D, false, 0, 0, 0, -1)] };
    Reject(() => Session(fake, new()).RunPlans([Mute()]));
    Assert(fake.Writes.Count == 0 && fake.Reads == 0, "No operation after failed baseline.");
});
Check("Snapshot failure after a restored parameter halts the next test", () =>
{
    var fake = new FakeMonitor(); var journal = new ProbeJournal();
    fake.OnSnapshot = f => f.Snapshots == 1 ? f.ValuesSnapshot() : [new(0x8D, false, 0, 0, 0, -1071241843)];
    Reject(() => Session(fake, journal).RunPlans([Mute(), Language()]));
    Assert(fake.Writes.Count == 2 && fake.Snapshots == 2, "No reconnect or next test.");
    Assert(!journal.ParameterReadbacksMatchBaseline && !journal.RestorationConfirmed, "No restored claim after lost link.");
});
Check("Cross-field drift halts even when the tested parameter reads its original value", () =>
{
    var fake = new FakeMonitor(); var journal = new ProbeJournal();
    fake.OnSnapshot = f => { if (f.Snapshots > 1) f.Values[0xCC] = 2; return f.ValuesSnapshot(); };
    Reject(() => Session(fake, journal).RunPlans([Mute(), Language()]));
    Assert(fake.Writes.Count == 2 && !journal.ParameterReadbacksMatchBaseline, "Other field drift must not be ignored.");
});
Check("Every readback matches yet complete recovery remains unverified", () =>
{
    var fake = new FakeMonitor(); var journal = new ProbeJournal();
    Session(fake, journal).RunPlans([Mute(), Language()]);
    Assert(journal.ParameterReadbacksMatchBaseline && journal.Steps.All(s => s.TargetReadbackMatched && !s.ParameterRestoreNeeded), "Parameter evidence should be retained.");
    Assert(!journal.RestorationConfirmed && !journal.DisplayUsabilityVerified && !journal.PowerCycleVerified && journal.RecoveryStatus == "unverified", "Readbacks cannot prove recovery.");
    Assert(journal.Status == "parameter-readbacks-match-recovery-unverified", "Status must qualify evidence.");
    using var json = JsonDocument.Parse(JsonSerializer.Serialize(journal));
    Assert(!json.RootElement.GetProperty("RestorationConfirmed").GetBoolean(), "Serialized results must not claim recovery.");
});
Check("Ordinary profile never includes disabled controls", () =>
{
    var fake = new FakeMonitor(); var journal = new ProbeJournal();
    Session(fake, journal).RunProfile("ordinary");
    Assert(fake.Writes.Count == 8 && fake.Writes.All(w => w.Item1 is not (0xF5 or 0xF8)), "Only four allowed ordinary steps.");
});
Check("Write-ahead journal failure prevents the first write", () =>
{
    var fake = new FakeMonitor(); var journal = new ProbeJournal();
    Reject(() => Session(fake, journal, j => { if (j.Steps.Count > 0) throw new IOException("disk full"); }).RunPlans([Mute()]));
    Assert(fake.Writes.Count == 0 && journal.Status == "halted-review-required", "WAL failure must stop before hardware write.");
});
Check("Persistence failure after mutation does not trigger restore or later writes", () =>
{
    var fake = new FakeMonitor(); var journal = new ProbeJournal();
    Reject(() => Session(fake, journal, j => { if (j.Events.Any(e => e.Phase == "target-write-returned")) throw new IOException("disk full"); }).RunPlans([Mute(), Language()]));
    Assert(fake.Writes.Count == 1 && fake.Reads == 1 && journal.Steps[0].ParameterRestoreNeeded, "Stop after post-write logging failure.");
});
Check("A faulted session and its journal cannot be resumed", () =>
{
    var fake = new FakeMonitor { OnWrite = (_, _, _) => throw new IOException("fault") }; var journal = new ProbeJournal();
    var session = Session(fake, journal); Reject(() => session.RunPlans([Mute()])); int calls = fake.Calls;
    Reject(() => session.RunPlans([Language()]));
    Reject(() => Session(fake, journal).RunPlans([Language()]));
    Assert(fake.Calls == calls, "Fault must remain latched across attempts to reuse a journal.");
});
Check("Unreadable equal snapshots and empty snapshots are never accepted", () =>
{
    ProbeValue[] failed = [new(0x8D, false, 0, 0, 0, -1)];
    Assert(!ProbeSession.SnapshotsMatch(failed, failed) && !ProbeSession.SnapshotsMatch([], []), "Matching failures are not positive evidence.");
});
Console.WriteLine($"PASS: {passed} offline safety regressions; fake I/O only, no native APIs or monitor transport references.");

sealed class FakeMonitor : IProbeMonitor
{
    public Dictionary<byte, uint> Values = new() { [0x8D] = 2, [0x72] = 30720, [0xF6] = 0, [0xCC] = 13,
        [0xF9] = 50, [0x14] = 3, [0x16] = 50, [0x18] = 50, [0x1A] = 50, [0x69] = 6500 };
    public List<(byte, uint)> Writes = [];
    public int Reads, Snapshots;
    public int Calls => Reads + Snapshots + Writes.Count;
    public Action<FakeMonitor, byte, uint>? OnWrite;
    public Func<FakeMonitor, byte, ProbeValue>? OnRead;
    public Func<FakeMonitor, List<ProbeValue>>? OnSnapshot;
    public ProbeValue Value(byte code) => new(code, true, 1, Values[code], 65535);
    public List<ProbeValue> ValuesSnapshot() => Values.Keys.Order().Select(Value).ToList();
    public ProbeValue Read(byte code) { Reads++; return OnRead?.Invoke(this, code) ?? Value(code); }
    public List<ProbeValue> Snapshot() { Snapshots++; return OnSnapshot?.Invoke(this) ?? ValuesSnapshot(); }
    public void Write(byte code, uint value)
    {
        Writes.Add((code, value));
        if (OnWrite != null) OnWrite(this, code, value); else Values[code] = code == 0xF9 ? value * 5 : value;
    }
}
