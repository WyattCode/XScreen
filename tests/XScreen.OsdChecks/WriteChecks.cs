using XScreen.Core;

static class WriteChecks
{
    public static async Task Run(Action<bool, string> check, MonitorDevice device)
    {
        async Task<Exception?> Error(Func<Task> f) { try { await f(); return null; } catch (Exception ex) { return ex; } }
        var startup = new Device(); var startupController = new MonitorController(startup);
        var snapshot = await startupController.ReadAsync(device);
        check(snapshot.Values.Count == 7 && snapshot.Get(0x14)?.Current == 3
            && startup.Reads.SequenceEqual(MonitorController.ReadCodes) && startup.Writes.Count == 0 && startup.Live == 0,
            "Automatic startup snapshot reads all seven visible fields exactly once, including color, and never writes");
        startup.Values[0x14] = 9; var refreshed = await startupController.ReadAsync(device);
        check(refreshed.Get(0x14)?.Current == 9 && startup.Reads.Count == 14 && startup.Writes.Count == 0,
            "One explicit refresh updates color without a second capture or any mutation");
        startup.Values[0x14] = 4; snapshot = await startupController.ReadAsync(device);
        check(!OsdProtocol.Decode(OsdControl.ColorTemperature, snapshot.Get(0x14)!).Recognized && !startupController.Halted && startup.Writes.Count == 0,
            "Unknown enum replies remain unknown, with no invented preset, write or communication-fault latch");
        foreach (byte fail in MonitorController.ReadCodes)
        {
            var failed = new Device { FailBefore = fail }; var service = new MonitorController(failed);
            var error = await Error(() => service.ReadAsync(device)); int count = failed.Reads.Count;
            await Error(() => service.ReadAsync(device));
            check(error is ReadbackException { WriteAttempted: false, Phase: "snapshot" } && service.Halted
                && count == Array.IndexOf(MonitorController.ReadCodes, fail) + 1 && failed.Reads.Count == count
                && failed.Writes.Count == 0 && failed.Live == 0,
                "Startup fails at the first failed reply, closes and never retries or reaches later fields: " + fail.ToString("X2"));
        }
        var wrongReply = new Device { WrongReplyAt = 0x14 };
        check(await Error(() => new MonitorController(wrongReply).ReadAsync(device)) is ReadbackException
            && wrongReply.Writes.Count == 0 && wrongReply.Live == 0, "Startup rejects a color reply with a different opcode");
        foreach (var spec in ControlSpec.Writable)
        foreach (bool afterWrite in new[] { false, true })
        {
            var wrong = new Device { QuickWrongReplyAt = spec.Code, QuickWrongAfterWrite = afterWrite };
            var controller = new MonitorController(wrong);
            uint target = spec.Code == 0x10 ? 90u : spec.Code == 0x62 ? 40u : 60u;
            var error = await Error(() => controller.WriteAsync(device, spec, target));
            int reads = wrong.Reads.Count, writes = wrong.Writes.Count;
            await Error(() => controller.WriteAsync(device, spec, target));
            check(error is ReadbackException detail && detail.WriteAttempted == afterWrite && controller.Halted && !controller.CanReadAgain
                && reads == (afterWrite ? 2 : 1) && writes == (afterWrite ? 1 : 0)
                && wrong.Reads.Count == reads && wrong.Writes.Count == writes && wrong.Live == 0,
                "Quick control refuses a numerically valid reply for another opcode, without retry or restore: " + spec.Code.ToString("X2") + "/" + (afterWrite ? "after" : "before"));
        }
        foreach (uint target in new uint[] { 0, 66, 70, 76, 100 })
        {
            var blocked = new Device(); var service = new MonitorController(blocked);
            var ex = await Error(() => service.WriteOsdAsync(device, OsdControl.Contrast, target));
            check(ex is InvalidOperationException && blocked.Reads.Count == 0 && blocked.Writes.Count == 0 && !service.Halted,
                "Restricted contrast rejects before I/O and cannot fault the healthy session: " + target);
        }
        check(!OsdProtocol.IsValidEncodedWrite(0x12, 0) && !OsdProtocol.IsValidEncodedWrite(0x12, 70)
            && !OsdProtocol.IsValidEncodedWrite(0x12, 100), "The native encoded-write admission also excludes every contrast target");
        foreach (var control in OsdProtocol.WritableControls)
        {
            uint target = control switch { OsdControl.PictureMode => 30, OsdControl.ColorTemperature => 9, OsdControl.Contrast => 69, _ => 45 };
            var fake = new Device(); var service = new MonitorController(fake); var command = OsdProtocol.DescribeCandidate(control, target);
            var result = await service.WriteOsdAsync(device, control, target);
            check(result.WasWritten && result.Actual == target && result.EncodedTarget == command.EncodedValue
                && fake.Writes.SequenceEqual(new[] { (command.Code, command.EncodedValue) }) && result.Snapshot.Values.Count == 7 && fake.Live == 0,
                "Explicit single-item apply writes encoded value once and refreshes seven fields: " + control);
            int before = fake.Reads.Count; var same = await service.WriteOsdAsync(device, control, target);
            check(!same.WasWritten && fake.Writes.Count == 1 && fake.Reads.Count == before + 11 && same.Snapshot.Values.Count == 7, "Same-value apply still synchronizes dependent state without a duplicate write: " + control);
            check(!fake.Reads.Any(c => c is 0xF5 or 0xF8 or 0xB1), "No dangerous control read or write is added: " + control);
        }
        var sideEffects = new Device { ChangeDependencies = true };
        var changed = await new MonitorController(sideEffects).WriteOsdAsync(device, OsdControl.PictureMode, 30);
        check(changed.Snapshot.Get(0x10)?.Current == 60 && changed.Snapshot.Get(0x12)?.Current == 72 && changed.Snapshot.Get(0x14)?.Current == 9,
            "Mode side effects are returned as actual state without replaying former dependent values");
        foreach (var control in OsdProtocol.WritableControls)
        {
            var failed = new Device { IgnoreWrite = true }; var service = new MonitorController(failed);
            uint target = control == OsdControl.PictureMode ? 30u : control == OsdControl.ColorTemperature ? 9u : control == OsdControl.Contrast ? 69u : 45u;
            var ex = await Error(() => service.WriteOsdAsync(device, control, target));
            check(ex is ReadbackException { WriteAttempted: true, Phase: "read-after-write" } && service.Halted && !service.CanReadAgain
                && failed.Writes.Count == 1 && failed.Reads.Count == 5 && failed.Live == 0,
                "Ignored apply stops at its first readback, without restore or later field reads: " + control);
            int reads = failed.Reads.Count;
            await Error(() => service.WriteOsdAsync(device, OsdControl.PictureMode, 46)); await Error(() => service.ReadOsdComparisonAsync(device));
            check(failed.Reads.Count == reads && failed.Writes.Count == 1, "All later work is latched after uncertain OSD apply: " + control);
        }
        foreach (byte fail in new byte[] { 0x15, 0x14, 0x12, 0xF9 })
        {
            var fake = new Device { FailBefore = fail }; var controller = new MonitorController(fake);
            var ex = await Error(() => controller.WriteOsdAsync(device, OsdControl.PictureMode, 30));
            check(ex is ReadbackException { WriteAttempted: false } && fake.Writes.Count == 0 && fake.Live == 0,
                "Pre-read failure skips mutation and disposes handles at " + fail.ToString("X2"));
        }
        foreach (byte fail in new byte[] { 0x15, 0x14, 0x10, 0x62, 0x87, 0x12, 0xF9 })
        {
            var fake = new Device { FailAfter = fail }; var controller = new MonitorController(fake);
            var ex = await Error(() => controller.WriteOsdAsync(device, OsdControl.PictureMode, 30));
            check(ex is ReadbackException { WriteAttempted: true } && fake.Writes.Count == 1 && !controller.CanReadAgain && fake.Live == 0,
                "Any dependent post-read fault remains uncertain, with no recovery write: " + fail.ToString("X2"));
        }
        var throws = new Device { ThrowOnWrite = true }; var throwing = new MonitorController(throws);
        check(await Error(() => throwing.WriteOsdAsync(device, OsdControl.BlackStabilizer, 45)) is ReadbackException { Phase: "write", WriteAttempted: true }
            && throws.Writes.Count == 1 && throws.Reads.Count == 4 && throws.Live == 0, "Native write failure causes no readback retry or fallback encoding");
        using (var cancel = new CancellationTokenSource())
        {
            var fake = new Device { OnRead = code => { if (code == 0xF9) cancel.Cancel(); } };
            check(await Error(() => new MonitorController(fake).WriteOsdAsync(device, OsdControl.BlackStabilizer, 45, cancel.Token)) is OperationCanceledException
                && fake.Writes.Count == 0 && fake.Live == 0, "Cancellation after the pre-read still prevents mutation");
        }
        using (var cancel = new CancellationTokenSource())
        {
            var fake = new Device { OnWrite = () => cancel.Cancel() };
            var result = await new MonitorController(fake).WriteOsdAsync(device, OsdControl.BlackStabilizer, 45, cancel.Token);
            check(result.Actual == 45 && fake.Reads.Count == 11 && fake.Writes.Count == 1 && fake.Live == 0,
                "Cancellation after mutation cannot skip the complete mandatory readback");
        }
        foreach (var (control, target) in new[] { (OsdControl.PictureMode, 255u), (OsdControl.ColorTemperature, 4u), (OsdControl.Contrast, 101u), (OsdControl.BlackStabilizer, 49u), ((OsdControl)0xF8, 1u), ((OsdControl)0xB1, 1u) })
        {
            var fake = new Device(); var error = await Error(() => new MonitorController(fake).WriteOsdAsync(device, control, target));
            check(error != null && fake.Reads.Count == 0 && fake.Writes.Count == 0 && fake.Live == 0, "Invalid target/control is rejected before I/O: " + control + "/" + target);
        }
        check(OsdProtocol.IsValidEncodedWrite(0xF9, 9) && !OsdProtocol.IsValidEncodedWrite(0xF9, 45)
            && OsdProtocol.IsValidEncodedWrite(0x14, 65502) && !OsdProtocol.IsValidEncodedWrite(0x14, 4)
            && !OsdProtocol.IsValidEncodedWrite(0xF8, 1) && !OsdProtocol.IsValidEncodedWrite(0xB1, 0x2F00),
            "The native transport guard permits exactly encoded authorized domains, excluding dangerous controls");
    }

    sealed class Device : IMonitorTransport
    {
        public readonly Dictionary<byte, uint> Values = new() { [0x10] = 100, [0x62] = 30, [0x87] = 50, [0x15] = 45, [0x14] = 3, [0x12] = 70, [0xF9] = 50 };
        public readonly List<byte> Reads = [];
        public readonly List<(byte, uint)> Writes = [];
        public int Live;
        public byte? FailBefore, FailAfter, WrongReplyAt;
        public byte? QuickWrongReplyAt;
        public bool QuickWrongAfterWrite;
        public bool IgnoreWrite, ThrowOnWrite, ChangeDependencies;
        public Action<byte>? OnRead; public Action? OnWrite;
        public IReadOnlyList<MonitorDevice> Enumerate() => throw new Exception("No extra enumeration");
        public IMonitorConnection Open(string id) { Live++; return new Connection(this); }
        sealed class Connection(Device owner) : IMonitorConnection
        {
            public MonitorValue Read(byte code)
            {
                owner.Reads.Add(code); owner.OnRead?.Invoke(code);
                if (owner.Writes.Count == 0 ? owner.FailBefore == code : owner.FailAfter == code) return new(code, false, 0, 0, unchecked((int)0xC026258B));
                if (owner.WrongReplyAt == code) return new(0xFE, true, 3, 65535);
                if (owner.QuickWrongReplyAt == code && (owner.Writes.Count > 0) == owner.QuickWrongAfterWrite)
                    return new(0xFE, true, owner.Values[code], 100);
                return new(code, true, owner.Values[code], code == 0x14 ? 65535u : code is 0x15 or 0xF9 ? 255u : 100u);
            }
            public void Write(byte code, uint value)
            {
                owner.Writes.Add((code, value)); owner.OnWrite?.Invoke();
                if (owner.ThrowOnWrite) throw new IOException("fake write may have mutated");
                if (owner.IgnoreWrite) return;
                owner.Values[code] = code == 0xF9 ? value * 5 : value;
                if (owner.ChangeDependencies && code == 0x15) { owner.Values[0x10] = 60; owner.Values[0x12] = 72; owner.Values[0x14] = 9; }
            }
            public void Dispose() => owner.Live--;
        }
    }
}
