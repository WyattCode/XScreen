using XScreen.Core;

static class ResetChecks
{
    public static async Task Run(Action<bool, string> check, MonitorDevice device)
    {
        async Task<Exception?> Error(Func<Task> action) { try { await action(); return null; } catch (Exception ex) { return ex; } }
        byte[] order = [0x15, 0x14, 0xF9, 0x87, 0x62, 0x10];
        check(AppResetProfile.Settings.Select(s => s.Code).SequenceEqual(order)
            && AppResetProfile.Settings.Select(s => s.EncodedValue).SequenceEqual(new uint[] { 45, 3, 10, 50, 50, 100 }),
            "The fixed reset profile covers exactly six supported targets, excluding restricted contrast");
        check(!AppResetProfile.Settings.Any(s => s.Code is 0x04 or 0x05 or 0x08 or 0xF5 or 0xF8 or 0xB1 or 0xD6),
            "Reset has no factory reset, power, aspect, Adaptive Sync or Dual Mode command");
        var already = new Device(); foreach (var s in AppResetProfile.Settings) already.Values[s.Code] = s.Value;
        var noOp = await new MonitorController(already).ResetToAppDefaultsAsync(device);
        check(noOp.WriteCount == 0 && already.Writes.Count == 0 && already.Reads.Count == 14 && already.Live == 0,
            "An already-default reset performs initial/final reads only and no duplicate writes");
        var fake = new Device(); var result = await new MonitorController(fake).ResetToAppDefaultsAsync(device);
        check(result.WriteCount == 6 && fake.Writes.Select(w => w.Code).SequenceEqual(order)
            && AppResetProfile.Settings.All(s => result.Snapshot.Get(s.Code)?.Current == s.Value) && fake.Live == 0,
            "One confirmed reset writes six targets and still reads all seven status fields");
        check(!fake.Writes.Any(w => w.Code == 0x12) && fake.Values[0x12] == 40,
            "Reset never writes contrast or forces it to70 even when the current value differs");
        check(fake.Reads.Count == 56 && fake.MaximumLive == 1 && !fake.Reads.Any(c => c is 0xF5 or 0xF8 or 0xB1),
            "Reset uses one serialized connection and a bounded seven-field check after each changed target");
        foreach (byte fail in order)
        {
            var ignored = new Device { IgnoreCode = fail }; var service = new MonitorController(ignored);
            var ex = await Error(() => service.ResetToAppDefaultsAsync(device)); int reads = ignored.Reads.Count, writes = ignored.Writes.Count;
            await Error(() => service.ResetToAppDefaultsAsync(device)); await Error(() => service.ReadAgainAsync(device));
            check(ex is ReadbackException { WriteAttempted: true } && service.Halted && !service.CanReadAgain
                && writes == Array.IndexOf(order, fail) + 1 && ignored.Writes.Count == writes && ignored.Reads.Count == reads && ignored.Live == 0,
                "An ignored reset target halts once, blocks later targets/retry/recovery and never rolls back: " + fail.ToString("X2"));
        }
        foreach (byte fail in MonitorController.ReadCodes)
        {
            var bad = new Device { FailReadCode = fail }; var service = new MonitorController(bad);
            var ex = await Error(() => service.ResetToAppDefaultsAsync(device));
            check(ex is ReadbackException { WriteAttempted: false } && bad.Writes.Count == 0 && bad.Live == 0
                && bad.Reads.Count == Array.IndexOf(MonitorController.ReadCodes, fail) + 1,
                "Initial reset read fails before mutation and stops immediately at " + fail.ToString("X2"));
        }
        var dependency = new Device { ColorChangesMode = true }; var dependent = new MonitorController(dependency);
        check(await Error(() => dependent.ResetToAppDefaultsAsync(device)) is ReadbackException { WriteAttempted: true, Actual.Code: 0x15 }
            && dependency.Writes.Count == 2 && dependent.Halted && !dependent.CanReadAgain,
            "A color change that invalidates an earlier reset target stops the batch without a corrective second pass");
        var throws = new Device { ThrowWriteCode = 0x14 }; var throwing = new MonitorController(throws);
        check(await Error(() => throwing.ResetToAppDefaultsAsync(device)) is ReadbackException { WriteAttempted: true, Phase: "reset-write" }
            && throws.Writes.Count == 2 && throwing.Halted && !throwing.CanReadAgain && throws.Live == 0,
            "A native-style exception after mutation never sends cleanup or later writes");
        var partial = new Device { FailReadCode = 0x62, FailAfterWrites = 1 }; var partialService = new MonitorController(partial);
        check(await Error(() => partialService.ResetToAppDefaultsAsync(device)) is ReadbackException { WriteAttempted: true, Actual.Error: unchecked((int)0xC026258B) }
            && partial.Writes.Count == 1 && !partialService.CanReadAgain && partial.Live == 0,
            "A checksum failure after an earlier reset mutation cannot be misclassified as a recoverable read-only fault");
        var restricted = new Device { RestrictBlackAfterColor = true }; var limited = new MonitorController(restricted);
        check(await Error(() => limited.ResetToAppDefaultsAsync(device)) is ReadbackException { WriteAttempted: true, Phase: "reset-range" }
            && restricted.Writes.Count == 2 && !limited.CanReadAgain,
            "A reduced range after mode/color changes stops the remaining reset targets");
        using (var cancellation = new CancellationTokenSource())
        {
            var cancelled = new Device(); cancellation.Cancel();
            check(await Error(() => new MonitorController(cancelled).ResetToAppDefaultsAsync(device, cancellation.Token)) is OperationCanceledException
                && cancelled.Reads.Count == 0 && cancelled.Writes.Count == 0 && cancelled.Live == 0, "A cancelled reset never opens hardware");
        }
        using (var cancellation = new CancellationTokenSource())
        {
            var cancelled = new Device { OnWrite = () => cancellation.Cancel() }; var service = new MonitorController(cancelled);
            check(await Error(() => service.ResetToAppDefaultsAsync(device, cancellation.Token)) is ReadbackException { Phase: "reset-cancelled", WriteAttempted: true }
                && cancelled.Writes.Count == 1 && cancelled.Reads.Count == 14 && service.Halted && !service.CanReadAgain && cancelled.Live == 0,
                "Exit during reset drains the attempted write's seven-field verification and cancels every later target");
        }
        var unsupported = new Device();
        check(await Error(() => new MonitorController(unsupported).ResetToAppDefaultsAsync(device with { Supported = false })) != null
            && unsupported.Reads.Count == 0 && unsupported.Writes.Count == 0, "Unsupported display reset rejects before I/O");
    }
    sealed class Device : IMonitorTransport
    {
        public Dictionary<byte, uint> Values = new() { [0x15] = 30, [0x14] = 9, [0x12] = 40, [0xF9] = 45, [0x87] = 60, [0x62] = 30, [0x10] = 80 };
        public List<byte> Reads = []; public List<(byte Code, uint Value)> Writes = [];
        public byte? IgnoreCode, ThrowWriteCode, FailReadCode;
        public bool ColorChangesMode, RestrictBlackAfterColor; public uint ContrastMaximum = 100; public int Live, MaximumLive, FailAfterWrites; public Action? OnWrite;
        public IReadOnlyList<MonitorDevice> Enumerate() => throw new Exception("Reset never enumerates another display");
        public IMonitorConnection Open(string id) { Live++; MaximumLive = Math.Max(Live, MaximumLive); return new Connection(this); }
        sealed class Connection(Device owner) : IMonitorConnection
        {
            public MonitorValue Read(byte code)
            {
                owner.Reads.Add(code);
                return owner.FailReadCode == code && owner.Writes.Count >= owner.FailAfterWrites ? new(code, false, 0, 0, unchecked((int)0xC026258B))
                    : new(code, true, owner.Values[code], code == 0xF9 && owner.RestrictBlackAfterColor && owner.Writes.Count >= 2 ? 40u : code == 0x12 ? owner.ContrastMaximum : code == 0x14 ? 65535u : code is 0x15 or 0xF9 ? 255u : 100u);
            }
            public void Write(byte code, uint value)
            {
                owner.Writes.Add((code, value)); owner.OnWrite?.Invoke();
                if (owner.IgnoreCode == code) return;
                owner.Values[code] = code == 0xF9 ? value * 5 : value;
                if (owner.ColorChangesMode && code == 0x14) owner.Values[0x15] = 65;
                if (owner.RestrictBlackAfterColor && code == 0x14) owner.Values[0xF9] = 40;
                if (owner.ThrowWriteCode == code) throw new IOException("Fake mutation before failure");
            }
            public void Dispose() => owner.Live--;
        }
    }
}
