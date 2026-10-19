namespace XScreen.Core;

public sealed record ControlSpec(byte Code, string Name, uint Step)
{
    public static readonly ControlSpec Brightness = new(0x10, "亮度", 1);
    public static readonly ControlSpec Volume = new(0x62, "显示器音量", 1);
    public static readonly ControlSpec Sharpness = new(0x87, "锐度", 10);
    public static readonly ControlSpec[] Writable = [Brightness, Volume, Sharpness];
}
public sealed record MonitorDevice(string Id, string Name, string Display, bool Supported);
public sealed record MonitorValue(byte Code, bool Success, uint Current, uint Maximum, int? Error = null);
public sealed record MonitorSnapshot(MonitorDevice Device, DateTimeOffset At, IReadOnlyList<MonitorValue> Values)
{
    public MonitorValue? Get(byte code) => Values.FirstOrDefault(v => v.Code == code);
}
public sealed record WriteResult(uint Previous, uint Actual, DateTimeOffset At);
public sealed record OsdWriteResult(OsdControl Control, uint Previous, uint Actual, uint EncodedTarget,
    bool WasWritten, MonitorSnapshot Snapshot);
public sealed class ReadbackException(string message, MonitorValue? actual, bool writeAttempted = false, string phase = "read", Exception? inner = null) : Exception(message, inner)
{
    public MonitorValue? Actual { get; } = actual;
    public bool WriteAttempted { get; } = writeAttempted;
    public string Phase { get; } = phase;
    // Only an explicitly requested state read may recover this exact non-mutating fault.
    public bool CanReadAgain => !WriteAttempted && Actual is { Success: false, Error: unchecked((int)0xC026258B) };
}
public interface IMonitorConnection : IDisposable
{
    MonitorValue Read(byte code);
    void Write(byte code, uint value);
}
public interface IMonitorTransport
{
    IReadOnlyList<MonitorDevice> Enumerate();
    IMonitorConnection Open(string deviceId);
}
public sealed partial class MonitorController(IMonitorTransport transport)
{
    readonly SemaphoreSlim gate = new(1, 1);
    Exception? fault;
    long generation;
    public bool Halted => Volatile.Read(ref fault) != null;
    public string? Fault => Volatile.Read(ref fault)?.Message;
    public bool CanReadAgain => Volatile.Read(ref fault) is ReadbackException { CanReadAgain: true };
    // One startup/refresh snapshot includes every visible field, including color preset.
    // No polling, automatic retry, or writes are performed by this read path.
    public static readonly byte[] ReadCodes = [0x10, 0x62, 0x87, 0x12, 0xF9, 0x15, 0x14];
    public async Task<IReadOnlyList<MonitorDevice>> EnumerateAsync() => await Serialized(transport.Enumerate);
    public async Task<MonitorSnapshot> ReadAsync(MonitorDevice device) => await Serialized(() => ReadSnapshot(device));
    // The separate manual comparison captures only its four fields; no writes.
    public async Task<MonitorSnapshot> ReadOsdComparisonAsync(MonitorDevice device, CancellationToken cancellation = default)
    {
        if (!device.Supported) throw new InvalidOperationException("只能读取已识别的目标显示器。");
        return await Serialized(() =>
        {
            cancellation.ThrowIfCancellationRequested();
            using var connection = transport.Open(device.Id);
            var values = new List<MonitorValue>();
            foreach (var control in OsdProtocol.Controls)
            {
                cancellation.ThrowIfCancellationRequested();
                byte code = OsdProtocol.Code(control);
                var value = connection.Read(code);
                if (!value.Success) throw ReadFailure("osd-comparison-read", value);
                if (value.Code != code) throw new ReadbackException("读取回复地址不符；本次对照无效。", value, false, "osd-comparison-read");
                values.Add(value);
            }
            return new MonitorSnapshot(device, DateTimeOffset.Now, values.AsReadOnly());
        }, cancellation);
    }
    // Only the four explicitly authorized controls. Invoked once by a user's Apply action.
    // There are no automatic target/restore batches, presets, retries, or write-on-selection paths.
    public async Task<OsdWriteResult> WriteOsdAsync(MonitorDevice device, OsdControl control, uint target, CancellationToken cancellation = default)
    {
        var command = OsdProtocol.DescribeCandidate(control, target);
        MonitorSafetyPolicy.EnsureWriteAllowed(command.Code);
        if (!device.Supported) throw new InvalidOperationException("此显示器型号尚未确认。");
        return await Serialized(() =>
        {
            cancellation.ThrowIfCancellationRequested();
            using var connection = transport.Open(device.Id);
            var before = new List<MonitorValue>();
            foreach (var item in OsdProtocol.Controls)
            {
                cancellation.ThrowIfCancellationRequested();
                byte code = OsdProtocol.Code(item); var value = connection.Read(code);
                if (!value.Success || value.Code != code || !OsdProtocol.Decode(item, value).Recognized)
                    throw ReadFailure("read-before-write", value);
                before.Add(value);
            }
            var previous = before.Single(v => v.Code == command.Code);
            if (control is OsdControl.Contrast or OsdControl.BlackStabilizer && target > previous.Maximum)
                throw ReadFailure("read-before-write", previous);
            cancellation.ThrowIfCancellationRequested();
            bool written = previous.Current != target;
            if (written)
            {
                try { connection.Write(command.Code, command.EncodedValue); }
                catch (Exception ex) { throw new ReadbackException("已尝试写入，结果未确认；不会自动恢复。", null, true, "write", ex); }
            }
            // Cancellation after mutation cannot skip required verification or cause a restore write.
            if (written) Thread.Sleep(200);
            string phase = written ? "read-after-write" : "read-after-noop";
            var after = new List<MonitorValue>();
            foreach (byte code in new[] { command.Code }.Concat(ReadCodes).Distinct())
            {
                MonitorValue value;
                if (!written) cancellation.ThrowIfCancellationRequested();
                try { value = connection.Read(code); }
                catch (Exception ex) { throw new ReadbackException("状态读取失败。", null, written, phase, ex); }
                var oldSpec = ControlSpec.Writable.SingleOrDefault(s => s.Code == code);
                bool valid = value.Code == code && value.Success && (oldSpec == null || ValidRead(oldSpec, value));
                var osd = OsdProtocol.Controls.Where(c => OsdProtocol.Code(c) == code).ToArray();
                if (valid && osd.Length == 1) valid = OsdProtocol.Decode(osd[0], value).Recognized;
                if (!valid) throw new ReadbackException("读到失败或未确认的状态；后续操作已停止。", value, written, phase);
                if (code == command.Code && value.Current != command.ExpectedReadback)
                    throw new ReadbackException($"请求值 {target} 与回读 {value.Current} 不一致；不会自动恢复。", value, written, phase);
                after.Add(value);
            }
            var actual = after.Single(v => v.Code == command.Code);
            return new OsdWriteResult(control, previous.Current, actual.Current, command.EncodedValue, written,
                new(device, DateTimeOffset.Now, after.AsReadOnly()));
        }, cancellation);
    }
    MonitorSnapshot ReadSnapshot(MonitorDevice device)
    {
        using var connection = transport.Open(device.Id);
        var values = new List<MonitorValue>();
        foreach (byte code in ReadCodes)
        {
            var value = connection.Read(code); values.Add(value);
            var spec = ControlSpec.Writable.SingleOrDefault(s => s.Code == code);
            if (!value.Success || value.Code != code || spec != null && !ValidRead(spec, value))
                throw ReadFailure("snapshot", value);
        }
        return new MonitorSnapshot(device, DateTimeOffset.Now, values);
    }
    // Never called automatically. This operation contains reads only and cannot
    // resume an uncertain write. Pending requests from the old generation stay invalid.
    public async Task<MonitorSnapshot> ReadAgainAsync(MonitorDevice device)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!CanReadAgain || !device.Supported) throw new InvalidOperationException("此会话不能通过重新读取解除暂停。");
            try
            {
                var snapshot = await Task.Run(() => ReadSnapshot(device)).ConfigureAwait(false);
                Interlocked.Increment(ref generation); Volatile.Write(ref fault, null);
                return snapshot;
            }
            catch (Exception ex) { Volatile.Write(ref fault, ex); throw; }
        }
        finally { gate.Release(); }
    }
    static bool ValidRead(ControlSpec spec, MonitorValue value) => value.Code == spec.Code && value.Success && value.Maximum is > 0 and <= 100 && value.Current <= value.Maximum && value.Current % spec.Step == 0;
    static ReadbackException ReadFailure(string phase, MonitorValue value) => new(
        value.Success ? "读到的数值或范围无效；未发送本次调节。"
        : value.Error == unchecked((int)0xC026258B) ? "DDC/CI 读取校验失败（0xC026258B）；未发送本次调节。"
        : $"无法读取控制项（错误 0x{value.Error.GetValueOrDefault():X8}）；未发送本次调节。", value, false, phase);
    public async Task<WriteResult> WriteAsync(MonitorDevice device, ControlSpec control, uint target, CancellationToken cancellation = default)
    {
        MonitorSafetyPolicy.EnsureWriteAllowed(control.Code);
        // Never accept a caller-supplied opcode or extend the device allowlist via the UI.
        var spec = ControlSpec.Writable.SingleOrDefault(s => s.Code == control.Code)
            ?? throw new InvalidOperationException("此项目尚未通过写入验证。");
        if (!device.Supported) throw new InvalidOperationException("此显示器型号尚未通过控制验证。");
        if (target > 100 || target % spec.Step != 0) throw new ArgumentOutOfRangeException(nameof(target), "数值超出范围或步长不正确。");
        return await Serialized(() =>
        {
            cancellation.ThrowIfCancellationRequested();
            using var connection = transport.Open(device.Id);
            var previous = connection.Read(spec.Code);
            if (!ValidRead(spec, previous) || target > previous.Maximum)
                throw ReadFailure("read-before-write", previous);
            // Cancellation during a slow pre-read is still before the mutation boundary.
            // After Write starts, its mandatory verification must always finish.
            cancellation.ThrowIfCancellationRequested();
            if (previous.Current == target) return new WriteResult(previous.Current, target, DateTimeOffset.Now);
            try { connection.Write(spec.Code, target); }
            catch (Exception ex) { throw new ReadbackException("已尝试写入，但结果未确认。", null, true, "write", ex); }
            // Finish verification after any write even if a newer UI request cancels its predecessor.
            Thread.Sleep(200);
            MonitorValue actual;
            try { actual = connection.Read(spec.Code); }
            catch (Exception ex) { throw new ReadbackException("写入后读取失败，结果未确认。", null, true, "read-after-write", ex); }
            if (ValidRead(spec, actual) && actual.Current == target)
                return new WriteResult(previous.Current, actual.Current, DateTimeOffset.Now);
            throw new ReadbackException(actual.Success
                ? $"显示器未应用请求值 {target}，实际值为 {actual.Current}。"
                : $"已发送调节请求，但未能回读确认（错误 0x{actual.Error.GetValueOrDefault():X8}）。", actual, true, "read-after-write");
        }, cancellation);
    }
    async Task<T> Serialized<T>(Func<T> operation, CancellationToken cancellation = default)
    {
        long requestGeneration = Interlocked.Read(ref generation);
        await gate.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            if (Halted) throw new InvalidOperationException("当前会话已暂停，不会继续访问显示器。请检查显示器；确认正常后手动重新启动程序。");
            if (requestGeneration != Interlocked.Read(ref generation)) throw new OperationCanceledException("旧会话中的请求已取消。");
            cancellation.ThrowIfCancellationRequested();
            try { return await Task.Run(operation, CancellationToken.None).ConfigureAwait(false); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { Interlocked.CompareExchange(ref fault, ex, null); throw; }
        }
        finally { gate.Release(); }
    }
}
