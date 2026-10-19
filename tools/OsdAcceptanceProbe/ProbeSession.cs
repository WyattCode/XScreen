using XScreen.Core;

namespace XScreen.Probes;

public sealed record ProbeValue(byte Code, bool Success, uint Type, uint Current, uint Maximum, int? Error = null);
public sealed record ProbeEvent(DateTimeOffset At, string Phase, byte? Code = null, uint? Value = null, string? Error = null);
public sealed record ProbePlan(byte Code, string Name, Func<uint, uint> Target, bool Scaled = false);

public interface IProbeMonitor
{
    ProbeValue Read(byte code);
    void Write(byte code, uint value);
    List<ProbeValue> Snapshot();
}

public sealed class ProbeJournal
{
    public int SchemaVersion { get; } = 2;
    public DateTimeOffset UpdatedAt { get; set; }
    public string Identity { get; set; } = "";
    public string Profile { get; set; } = "";
    public string Status { get; set; } = "not-started";
    public List<ProbeValue> Baseline { get; set; } = [];
    public List<ProbeValue> Final { get; set; } = [];
    public List<ProbeStep> Steps { get; set; } = [];
    public List<ProbeEvent> Events { get; set; } = [];
    public bool ParameterReadbacksMatchBaseline { get; set; }
    // The probe cannot observe the screen or verify a power cycle. Never derive these from VCP equality.
    public string RecoveryStatus => "unverified";
    public bool RestorationConfirmed => false;
    public bool DisplayUsabilityVerified => false;
    public bool PowerCycleVerified => false;
    public string? Error { get; set; }
}

public sealed class ProbeStep
{
    public byte Code { get; set; }
    public string Name { get; set; } = "";
    public uint Original { get; set; }
    public uint Target { get; set; }
    public uint EncodedOriginal { get; set; }
    public uint EncodedTarget { get; set; }
    public bool TargetReadbackMatched { get; set; }
    public bool ParameterRestoreNeeded { get; set; } = true;
    public ProbeValue? Observed { get; set; }
    public ProbeValue? RestoreReadback { get; set; }
    public string? Error { get; set; }
}

public static class ProbeWritePolicy
{
    public static void EnsureAllowed(byte code, uint value)
    {
        MonitorSafetyPolicy.EnsureWriteAllowed(code);
        bool allowed = code switch
        {
            0x8D => value is 1 or 2,
            0x72 => value is 25600 or 30720 or 35840 or 40960,
            0xF6 => value <= 2,
            0xCC => value is 2 or 13,
            0x14 => value is 3 or 5 or 9 or 11 or 65502,
            0x69 => value is 4000 or 4400 or 4700 or 5000 or 5300 or 5600 or 5900 or 6200 or 6500 or 6800 or 7100 or 7400 or 7700 or 8000 or 8300 or 8600 or 8900 or 9300 or 10000,
            0x16 or 0x18 or 0x1A => value <= 100,
            0xF9 => value <= 20,
            _ => false
        };
        if (!allowed) throw new InvalidOperationException($"Unapproved probe value: 0x{code:X2}={value}.");
    }
}

// Independent of Windows/native APIs. Offline tests compile this exact file with fake I/O only.
public sealed class ProbeSession(IProbeMonitor monitor, ProbeJournal journal, Action<ProbeJournal> persist, Action pause)
{
    bool faulted;
    bool started;

    public void RunPlans(IEnumerable<ProbePlan> plans) => Run(() =>
    {
        var batch = plans.ToArray();
        // Reject disabled controls before any baseline read or preceding plan can run.
        foreach (var plan in batch) MonitorSafetyPolicy.EnsureWriteAllowed(plan.Code);
        Start();
        foreach (var plan in batch) { Step(plan); AssertBaseline(); }
    });

    public void RunProfile(string profile)
    {
        switch (profile)
        {
            case "ordinary":
                RunPlans([
                    new(0x8D, "mute", v => v == 1 ? 2u : 1u),
                    new(0x72, "gamma", v => v == 25600 ? 30720u : 25600u),
                    new(0xF6, "energy-saving", v => v == 0 ? 1u : 0u),
                    new(0xCC, "osd-language", v => v == 13 ? 2u : 13u)]);
                break;
            case "language": RunPlans([new(0xCC, "osd-language", v => v == 13 ? 2u : 13u)]); break;
            case "black": RunPlans([new(0xF9, "black-stabilizer-scaled", v => v >= 5 ? v - 5 : 5u, true)]); break;
            case "color":
                Run(() =>
                {
                    Start();
                    var custom = Begin(new(0x14, "custom-color-dependency", _ => 11));
                    Apply(custom);
                    foreach (byte channel in new byte[] { 0x16, 0x18, 0x1A })
                        Step(new(channel, "rgb-" + channel.ToString("X2"), v => v > 0 ? v - 1 : 1u));
                    RestoreParameter(custom);
                    AssertBaseline();
                    var manual = Begin(new(0x14, "manual-color-dependency", _ => 65502));
                    Apply(manual);
                    Step(new(0x69, "manual-kelvin", v => v == 6500 ? 6200u : 6500u));
                    RestoreParameter(manual);
                    AssertBaseline();
                });
                break;
            default: throw new ArgumentException("Unknown profile.", nameof(profile));
        }
    }

    void Run(Action action)
    {
        if (faulted || started || journal.Status != "not-started" || journal.Steps.Count != 0 || journal.Error != null)
            throw new InvalidOperationException("Session is terminal or has already started. Historical journals cannot resume hardware operations.");
        try
        {
            action();
            AssertBaseline();
            journal.Status = "parameter-readbacks-match-recovery-unverified";
            Save();
        }
        catch (Exception ex)
        {
            faulted = true;
            journal.ParameterReadbacksMatchBaseline = false;
            journal.Status = "halted-review-required";
            journal.Error = ex.ToString();
            journal.Events.Add(new(DateTimeOffset.Now, "halted", Error: ex.Message));
            // Never read, reconnect, restore, retry a write, or run the next plan after a fault.
            try { Save(); }
            catch (Exception saveError) { throw new AggregateException("Probe halted; journal persistence also failed.", ex, saveError); }
            throw;
        }
    }

    void Start()
    {
        started = true;
        journal.Baseline = ReadSnapshot();
        journal.Status = "baseline-read";
        Save();
    }

    ProbeStep Begin(ProbePlan plan)
    {
        var original = Read(plan.Code);
        uint target = plan.Target(original.Current);
        if (plan.Scaled && (plan.Code != 0xF9 || original.Current > 100 || original.Current % 5 != 0 || target > 100 || target % 5 != 0))
            throw new InvalidOperationException("Invalid black-stabilizer encoding.");
        uint encodedOriginal = plan.Scaled ? original.Current / 5 : original.Current;
        uint encodedTarget = plan.Scaled ? target / 5 : target;
        ProbeWritePolicy.EnsureAllowed(plan.Code, encodedOriginal);
        ProbeWritePolicy.EnsureAllowed(plan.Code, encodedTarget);
        var step = new ProbeStep { Code = plan.Code, Name = plan.Name, Original = original.Current, Target = target,
            EncodedOriginal = encodedOriginal, EncodedTarget = encodedTarget };
        journal.Steps.Add(step);
        journal.Status = "write-pending";
        Save(); // Durable intent before the first possible mutation.
        return step;
    }

    void Step(ProbePlan plan)
    {
        var step = Begin(plan);
        Apply(step);
        RestoreParameter(step); // Only reached after a successful target readback.
    }

    void Apply(ProbeStep step)
    {
        try
        {
            Write(step.Code, step.EncodedTarget, "target-write");
            step.Observed = Expect(step.Code, step.Target);
            step.TargetReadbackMatched = true;
            Save();
        }
        catch (Exception ex) { step.Error = ex.Message; throw; }
    }

    void RestoreParameter(ProbeStep step)
    {
        try
        {
            Write(step.Code, step.EncodedOriginal, "parameter-restore-write");
            step.RestoreReadback = Expect(step.Code, step.Original);
            step.ParameterRestoreNeeded = false;
            Save();
        }
        catch (Exception ex) { step.Error = ex.Message; throw; }
    }

    void Write(byte code, uint value, string phase)
    {
        ProbeWritePolicy.EnsureAllowed(code, value);
        journal.ParameterReadbacksMatchBaseline = false;
        journal.Events.Add(new(DateTimeOffset.Now, phase + "-pending", code, value));
        Save();
        monitor.Write(code, value);
        journal.Events.Add(new(DateTimeOffset.Now, phase + "-returned", code, value));
        Save();
    }

    ProbeValue Read(byte code)
    {
        var value = monitor.Read(code);
        journal.Events.Add(new(DateTimeOffset.Now, "read", code, value.Success ? value.Current : null, value.Success ? null : value.Error?.ToString()));
        if (!value.Success || value.Code != code) throw new InvalidOperationException($"Read failed for 0x{code:X2}; error={value.Error}. Halt for review.");
        return value;
    }

    ProbeValue Expect(byte code, uint target)
    {
        for (int i = 0; i < 8; i++)
        {
            pause();
            var value = Read(code); // A failed read stops immediately; only readable mismatches may be polled.
            if (value.Current == target) return value;
        }
        throw new InvalidOperationException($"0x{code:X2}: target {target} did not match readback. Halt for review.");
    }

    List<ProbeValue> ReadSnapshot()
    {
        var values = monitor.Snapshot();
        if (values.Count == 0 || values.Any(v => !v.Success) || values.Select(v => v.Code).Distinct().Count() != values.Count)
            throw new InvalidOperationException("Snapshot is incomplete or unreadable. Halt for review.");
        return values;
    }

    void AssertBaseline()
    {
        journal.Final = ReadSnapshot();
        journal.ParameterReadbacksMatchBaseline = SnapshotsMatch(journal.Baseline, journal.Final);
        Save();
        if (!journal.ParameterReadbacksMatchBaseline) throw new InvalidOperationException("Parameter snapshot differs from baseline. Halt for review.");
    }

    public static bool SnapshotsMatch(IReadOnlyList<ProbeValue> before, IReadOnlyList<ProbeValue> after) =>
        before.Count > 0 && before.Count == after.Count && before.All(v => v.Success) && after.All(v => v.Success) &&
        before.Select(v => v.Code).Distinct().Count() == before.Count && after.Select(v => v.Code).Distinct().Count() == after.Count &&
        before.OrderBy(v => v.Code).SequenceEqual(after.OrderBy(v => v.Code));

    void Save() { journal.UpdatedAt = DateTimeOffset.Now; persist(journal); }
}
