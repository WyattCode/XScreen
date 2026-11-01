using System.Text.Json;
using System.Text.RegularExpressions;
using XScreen.Core;

if (args.Length != 2) throw new ArgumentException("Provide workspace and a fresh offline report path; no hardware mode exists.");
var root = Path.GetFullPath(args[0]); var report = Path.GetFullPath(args[1]);
if (File.Exists(report)) throw new IOException("Preserve prior evidence; choose a fresh path.");
var results = new List<object>(); int failures = 0;
void Check(bool pass, string name) { results.Add(new { name, passed = pass }); if (!pass) failures++; }
void Reject(Action f, string name) { bool rejected = false; try { f(); } catch { rejected = true; } Check(rejected, name); }
async Task RejectAsync(Func<Task> f, string name) { bool rejected = false; try { await f(); } catch { rejected = true; } Check(rejected, name); }
var device = new MonitorDevice("fake-a", "OFFLINE OSD", "sample", true);
try
{
    using var reference = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "tests/Fixtures/osd-reference.json")));
    var pictureTables = reference.RootElement.GetProperty("pictureTables").EnumerateArray().Select(t => t.EnumerateArray().Select(v => v.GetInt32()).ToArray()).ToArray();
    Check(pictureTables.Length == 2 && pictureTables[0].SequenceEqual(pictureTables[1]), "Both frozen shipped picture mode mapping tables agree");
    var modes = reference.RootElement.GetProperty("pictureModes").EnumerateArray().ToArray();
    Check(modes.Length == 13 && OsdProtocol.PictureModes.Count == 13, "All 13 model-specific picture modes are retained");
    foreach (var mode in modes)
    {
        uint raw = mode.GetProperty("rawValue").GetUInt32(); string name = mode.GetProperty("label").GetString()!;
        var decoded = OsdProtocol.Decode(OsdControl.PictureMode, new(0x15, true, raw, 255));
        var candidate = OsdProtocol.DescribeCandidate(OsdControl.PictureMode, raw);
        Check(decoded.Display == name && candidate.EncodedValue == raw && pictureTables[0][mode.GetProperty("internalIndex").GetInt32()] == raw
            && candidate.RequiresManualApply && !candidate.HardwareWriteVerified
            && OsdProtocol.PictureModes.Single(m => m.Raw == raw).SdrOnly == mode.GetProperty("sdrOnly").GetBoolean(),
            "Mode mapping and disabled offline candidate: " + name);
    }
    var colorRows = reference.RootElement.GetProperty("colorRows").EnumerateObject().ToDictionary(p => int.Parse(p.Name), p => p.Value.GetUInt32());
    int[] indices = [0, 1, 2, 3, 9];
    for (int i = 0; i < indices.Length; i++)
    {
        var option = OsdProtocol.ColorTemperatures[i]; var candidate = OsdProtocol.DescribeCandidate(OsdControl.ColorTemperature, option.Raw);
        Check(option.Raw == colorRows[indices[i]] && OsdProtocol.Decode(OsdControl.ColorTemperature, new(0x14, true, option.Raw, 65535)).Display == option.Label
            && candidate.EncodedValue == option.Raw && candidate.RequiresManualApply, "Color matches shipped normal table: " + option.Label);
    }
    Check(!OsdProtocol.Decode(OsdControl.ColorTemperature, new(0x14, true, 4, 65535)).Recognized, "Unknown color 4 is never mislabeled Medium");
    Check(!OsdProtocol.Decode(OsdControl.PictureMode, new(0x15, true, 255, 255)).Recognized, "Enumeration maximum is never treated as a supported option");
    Reject(() => OsdProtocol.DescribeCandidate(OsdControl.PictureMode, 255), "Mode unknown raw value cannot become a candidate");
    Reject(() => OsdProtocol.DescribeCandidate(OsdControl.ColorTemperature, 4), "Unknown color raw value is rejected");
    Reject(() => OsdProtocol.Code((OsdControl)0xF8), "Adaptive Sync is outside the new protocol surface");
    Reject(() => OsdProtocol.Code((OsdControl)0xB1), "Dual Mode is outside the new protocol surface");
    Reject(() => OsdProtocol.Decode(OsdControl.Contrast, new(0xF9, true, 50, 100)), "A reply for the wrong opcode cannot be relabeled");
    Check(OsdProtocol.Decode(OsdControl.BlackStabilizer, new(0xF9, true, 50, 255)).Recognized,
        "Actual startup F9=50/max255 is recognized without treating 255 as an editable limit");
    Check(!OsdProtocol.Decode(OsdControl.BlackStabilizer, new(0xF9, true, 105, 255)).Recognized
        && !OsdProtocol.Decode(OsdControl.BlackStabilizer, new(0xF9, true, 49, 255)).Recognized
        && !OsdProtocol.Decode(OsdControl.Contrast, new(0x12, true, 70, 255)).Recognized,
        "The F9 envelope exception never broadens UI values, steps or contrast bounds");
    foreach (uint value in new uint[] { 0, 1, 50, 99, 100 })
    {
        var candidate = OsdProtocol.DescribeCandidate(OsdControl.Contrast, value);
        Check(candidate is { Code: 0x12, RequiresManualApply: true } && candidate.ExpectedReadback == value && candidate.EncodedValue == value,
            "Contrast offline boundary and encoding " + value);
    }
    for (uint value = 0; value <= 100; value += 5)
    {
            var black = OsdProtocol.DescribeCandidate(OsdControl.BlackStabilizer, value);
            Check(black.Code == 0xF9 && black.EncodedValue == value / 5 && black.ExpectedReadback == value && black.RequiresManualApply,
                "Black stabilizer separate write/read units " + value);
    }
    foreach (uint value in new uint[] { 1, 9, 49, 99, 101, uint.MaxValue })
        Reject(() => OsdProtocol.DescribeCandidate(OsdControl.BlackStabilizer, value), "Invalid black target never rounds or overflows " + value);
    Reject(() => OsdProtocol.DescribeCandidate(OsdControl.Contrast, 101), "Contrast upper bound enforced");
    {
        var step = reference.RootElement.GetProperty("blackUnits");
        var target = OsdProtocol.DescribeCandidate(OsdControl.BlackStabilizer, step.GetProperty("Target").GetUInt32());
        var original = OsdProtocol.DescribeCandidate(OsdControl.BlackStabilizer, step.GetProperty("Original").GetUInt32());
        Check(target.EncodedValue == step.GetProperty("EncodedTarget").GetUInt32() && original.EncodedValue == step.GetProperty("EncodedOriginal").GetUInt32()
            && !target.HardwareWriteVerified, "Historical 9/45 and 10/50 are evidence of units, never a recovery claim");
    }
    var fake = new FakeOsd(); var controller = new MonitorController(fake);
    Check(fake.Calls.Count == 0, "Creating the comparison controller performs no I/O");
    var baseline = await controller.ReadOsdComparisonAsync(device);
    Check(fake.Calls.SequenceEqual(new[] { "open", "15", "14", "12", "F9", "close" }) && fake.Writes == 0,
        "One explicit capture reads exactly four permitted codes and closes, with zero writes");
    var state = new OsdComparison(); state.Accept(baseline); state.SaveBaseline();
    fake.Values[0x15] = 30; fake.Values[0x14] = 9; fake.Values[0x12] = 69; fake.Values[0xF9] = 45;
    state.Accept(await controller.ReadOsdComparisonAsync(device));
    Check(state.Differences.Count == 4 && state.Differences.All(d => d.RawChanged) && fake.Writes == 0, "Manual external changes are decoded and compared without writes");
    state.InvalidateCurrent(); Check(state.Current == null && state.Baseline != null && state.Differences.Count == 0, "Failure invalidates the current result and never presents stale differences");
    Reject(state.SaveBaseline, "A failed capture cannot overwrite the baseline");
    state.Accept(baseline with { Device = device with { Id = "fake-b" } });
    Check(state.Baseline == null, "A different physical identity cannot reuse the old baseline");
    var mutable = baseline.Values.ToList(); state.Accept(baseline with { Values = mutable }); state.SaveBaseline(); mutable[0] = new(0x15, true, 255, 255);
    Check(state.Baseline!.Get(0x15)!.Current == 45, "Saved samples are immutable copies of caller-owned lists");
    foreach (byte failed in new byte[] { 0x15, 0x14, 0x12, 0xF9 })
    {
        var faultFake = new FakeOsd { FailCode = failed }; var fault = new MonitorController(faultFake);
        await RejectAsync(() => fault.ReadOsdComparisonAsync(device), "Capture fails immediately at " + failed.ToString("X2"));
        int before = faultFake.Calls.Count;
        await RejectAsync(() => fault.ReadOsdComparisonAsync(device), "Fault prevents automatic later captures " + failed.ToString("X2"));
        Check(fault.Halted && faultFake.Calls.Count == before && faultFake.Calls.Last() == "close" && faultFake.Writes == 0,
            "Fault releases handles, issues no retry, recovery write or later code " + failed.ToString("X2"));
        var expected = new byte[] { 0x15, 0x14, 0x12, 0xF9 }.TakeWhile(c => c != failed).Append(failed).Select(c => c.ToString("X2"));
        Check(faultFake.Calls.Where(c => c.Length == 2).SequenceEqual(expected), "The first failed read stops the capture sequence " + failed.ToString("X2"));
    }
    var unsupported = new FakeOsd(); await RejectAsync(() => new MonitorController(unsupported).ReadOsdComparisonAsync(device with { Supported = false }), "Unsupported devices reject before I/O");
    Check(unsupported.Calls.Count == 0, "Device rejection adds no native work");
    using var cancel = new CancellationTokenSource(); cancel.Cancel();
    var cancelled = new FakeOsd(); await RejectAsync(() => new MonitorController(cancelled).ReadOsdComparisonAsync(device, cancel.Token), "Canceled capture rejects before open");
    Check(cancelled.Calls.Count == 0, "Cancellation does not open a connection");
    foreach (var control in OsdProtocol.Controls)
    {
        var blocked = new FakeOsd(); await RejectAsync(() => new MonitorController(blocked).WriteAsync(device, new(OsdProtocol.Code(control), "unverified", 1, 1), 1), "Production write allowlist still excludes " + control);
        Check(blocked.Calls.Count == 0, "Unverified write never reaches transport " + control);
    }
    Check(ControlSpec.Writable.Select(s => s.Code).SequenceEqual(new byte[] { 0x10, 0x62, 0x87 })
        && MonitorController.ReadCodes.SequenceEqual(new byte[] { 0x10, 0x62, 0x87, 0x12, 0xF9, 0x15, 0x14 }), "Startup snapshot includes color once and legacy write admission remains unchanged");
    Reject(MonitorSafetyPolicy.EnsureResearchHardwareAllowed, "Research hardware probes remain blocked");
    await WriteChecks.Run(Check, device);
    await ResetChecks.Run(Check, device);
}
catch (Exception ex) { failures++; results.Add(new { error = ex.ToString() }); }
Check(WindowsMonitorTransport.NativeReadAttempts == 0 && WindowsMonitorTransport.NativeWriteAttempts == 0, "All checks use fake transport only, native reads/writes zero");
Directory.CreateDirectory(Path.GetDirectoryName(report)!);
File.WriteAllText(report, JsonSerializer.Serialize(new { offline = true, failures, checks = results.Count,
    nativeReadAttempts = WindowsMonitorTransport.NativeReadAttempts, nativeWriteAttempts = WindowsMonitorTransport.NativeWriteAttempts, results }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"OSD checks: {results.Count}, failures {failures}, native 0/0; {report}"); return failures == 0 ? 0 : 1;

sealed class FakeOsd : IMonitorTransport
{
    public Dictionary<byte, uint> Values = new() { [0x15] = 45, [0x14] = 3, [0x12] = 70, [0xF9] = 50 };
    public List<string> Calls = []; public int Writes; public byte? FailCode;
    public IReadOnlyList<MonitorDevice> Enumerate() => throw new Exception("No enumeration during comparison");
    public IMonitorConnection Open(string id) { Calls.Add("open"); return new Connection(this); }
    sealed class Connection(FakeOsd f) : IMonitorConnection
    {
        public MonitorValue Read(byte code) { f.Calls.Add(code.ToString("X2")); return f.FailCode == code ? new(code, false, 0, 0, unchecked((int)0xC026258B)) : new(code, true, f.Values[code], code == 0x14 ? 65535u : code == 0x15 ? 255u : 100u); }
        public void Write(byte code, uint value) { f.Writes++; throw new Exception("No write path is allowed in this test"); }
        public void Dispose() => f.Calls.Add("close");
    }
}
