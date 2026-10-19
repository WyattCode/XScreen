namespace XScreen.Core;

// Application defaults, not a claim about LG factory defaults. Only this immutable
// six-setting profile can reach the reset path; callers cannot supply a batch.
public sealed record AppDefault(byte Code, string Name, uint Value, string Display)
{
    public uint EncodedValue => Code == 0xF9 ? Value / 5 : Value;
}
public static class AppResetProfile
{
    public static IReadOnlyList<AppDefault> Settings { get; } = Array.AsReadOnly<AppDefault>([
        new(0x15, "画面模式", 45, "玩家 1"), new(0x14, "色温", 3, "暖色"),
        new(0xF9, "黑色稳定器", 50, "50"),
        new(0x87, "锐度", 50, "50"), new(0x62, "显示器音量", 50, "50"),
        new(0x10, "亮度", 100, "100")]);
}
public sealed record AppResetResult(MonitorSnapshot Before, MonitorSnapshot Snapshot, int WriteCount);

public sealed partial class MonitorController
{
    // Invoked only after the user confirms the six visible targets. No factory
    // reset opcode, automatic test, retry, rollback or corrective second pass.
    public async Task<AppResetResult> ResetToAppDefaultsAsync(MonitorDevice device, CancellationToken cancellation = default)
    {
        if (!device.Supported) throw new InvalidOperationException("此显示器型号尚未确认。");
        return await Serialized(() =>
        {
            cancellation.ThrowIfCancellationRequested();
            using var connection = transport.Open(device.Id);
            int writes = 0;
            var expected = new Dictionary<byte, uint>();
            MonitorSnapshot Capture(string phase, byte? first = null, bool allowCancellation = false)
            {
                var values = new List<MonitorValue>();
                IEnumerable<byte> codes = first is { } code ? new[] { code }.Concat(ReadCodes).Distinct() : ReadCodes;
                foreach (byte requested in codes)
                {
                    if (allowCancellation) cancellation.ThrowIfCancellationRequested();
                    MonitorValue value;
                    try { value = connection.Read(requested); }
                    catch (Exception ex) { throw new ReadbackException("重置状态未能确认；已完成的设置不会自动恢复。", null, writes > 0, phase, ex); }
                    var spec = ControlSpec.Writable.SingleOrDefault(s => s.Code == requested);
                    var osd = OsdProtocol.Controls.Where(c => OsdProtocol.Code(c) == requested).ToArray();
                    bool valid = value.Code == requested && value.Success && (spec == null || ValidRead(spec, value));
                    if (valid && osd.Length == 1) valid = OsdProtocol.Decode(osd[0], value).Recognized;
                    if (!valid) throw new ReadbackException("重置状态读取失败或参数未确认；后续重置已停止，已完成的设置不会自动恢复。", value, writes > 0, phase);
                    if (expected.TryGetValue(requested, out uint target) && value.Current != target)
                        throw new ReadbackException($"重置目标 {target} 与回读 {value.Current} 不一致；后续重置已停止，已完成的设置不会自动恢复。", value, writes > 0, phase);
                    values.Add(value);
                }
                return new(device, DateTimeOffset.Now, values.AsReadOnly());
            }
            void CheckCancellation()
            {
                if (!cancellation.IsCancellationRequested) return;
                if (writes > 0) throw new ReadbackException("重置已停止；已完成的设置不会自动恢复。", null, true, "reset-cancelled");
                cancellation.ThrowIfCancellationRequested();
            }
            var before = Capture("reset-before", allowCancellation: true); var snapshot = before;
            foreach (var setting in AppResetProfile.Settings)
            {
                CheckCancellation(); MonitorSafetyPolicy.EnsureWriteAllowed(setting.Code);
                var current = snapshot.Get(setting.Code)!;
                if (setting.Code is 0x10 or 0x62 or 0x87 or 0x12 or 0xF9 && setting.Value > current.Maximum)
                    throw new ReadbackException($"{setting.Name}当前范围不能应用目标 {setting.Value}；后续重置已停止。", current, writes > 0, "reset-range");
                expected.Add(setting.Code, setting.Value);
                if (current.Current == setting.Value) continue;
                // Once a write call begins, its verification must finish despite cancellation.
                writes++;
                try { connection.Write(setting.Code, setting.EncodedValue); }
                catch (Exception ex) { throw new ReadbackException($"{setting.Name}已尝试重置但结果未确认；后续重置已停止，不会自动恢复。", null, true, "reset-write", ex); }
                Thread.Sleep(200);
                // Target first; also check every earlier target to catch dependencies.
                snapshot = Capture("reset-verify", setting.Code);
            }
            CheckCancellation();
            snapshot = Capture("reset-final");
            return new AppResetResult(before, snapshot, writes);
        }, cancellation);
    }
}
