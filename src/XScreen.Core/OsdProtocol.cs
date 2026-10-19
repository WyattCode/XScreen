namespace XScreen.Core;

public enum OsdControl { PictureMode, ColorTemperature, Contrast, BlackStabilizer }
public sealed record OsdOption(string Label, uint Raw, bool SdrOnly = false);
public sealed record OsdReading(OsdControl Control, string Name, MonitorValue Raw, string Display, bool Recognized);
// An offline encoding description, never a permission or an executable command.
public sealed record OsdWriteCandidate(byte Code, uint EncodedValue, uint ExpectedReadback)
{
    public bool HardwareWriteVerified => false;
    public bool RequiresManualApply => true;
}
public sealed record OsdDifference(OsdReading Before, OsdReading After)
{
    public bool RawChanged => Before.Raw != After.Raw;
}

public static class OsdProtocol
{
    // Shipped LG Switch mappings, narrowed to the 32GX870A manual + historical CLI list.
    public static IReadOnlyList<OsdOption> PictureModes { get; } = Array.AsReadOnly<OsdOption>([
        new("个性化画面", 65), new("玩家 1", 45), new("玩家 2", 46), new("FPS", 30), new("RTS", 31),
        new("鲜艳", 49), new("阅读", 1, true), new("HDR 效果", 39, true), new("sRGB", 15, true),
        new("DCI-P3", 25, true), new("色弱辅助", 6, true), new("校准 1", 18, true), new("校准 2", 19, true)]);
    // COLOR_TEMP_LIST normal table. The GSYNC2 table swaps the first two entries;
    // manual observation is still required to establish this model's mapping after reset.
    public static IReadOnlyList<OsdOption> ColorTemperatures { get; } = Array.AsReadOnly<OsdOption>([
        new("暖色", 3), new("中等", 5), new("冷色", 9), new("自定义", 11), new("手动", 65502)]);
    public static IReadOnlyList<OsdControl> Controls { get; } = Array.AsReadOnly(new[] {
        OsdControl.PictureMode, OsdControl.ColorTemperature, OsdControl.Contrast, OsdControl.BlackStabilizer });
    public static IReadOnlyList<OsdControl> WritableControls { get; } = Array.AsReadOnly(
        Controls.Where(c => !MonitorSafetyPolicy.IsReadOnly(Code(c))).ToArray());
    public static byte Code(OsdControl control) => control switch {
        OsdControl.PictureMode => 0x15, OsdControl.ColorTemperature => 0x14,
        OsdControl.Contrast => 0x12, OsdControl.BlackStabilizer => 0xF9,
        _ => throw new ArgumentOutOfRangeException(nameof(control)) };
    public static string Name(OsdControl control) => control switch {
        OsdControl.PictureMode => "画面模式", OsdControl.ColorTemperature => "色温",
        OsdControl.Contrast => "对比度", OsdControl.BlackStabilizer => "黑色稳定器",
        _ => throw new ArgumentOutOfRangeException(nameof(control)) };
    public static OsdReading Decode(OsdControl control, MonitorValue raw)
    {
        if (raw.Code != Code(control)) throw new ArgumentException("回复地址与控制项不符。", nameof(raw));
        if (!raw.Success) return new(control, Name(control), raw, "读取失败", false);
        var options = control switch { OsdControl.PictureMode => PictureModes,
            OsdControl.ColorTemperature => ColorTemperatures, _ => null };
        if (options != null)
        {
            var option = options.SingleOrDefault(o => o.Raw == raw.Current);
            return new(control, Name(control), raw, option?.Label ?? $"未知值 0x{raw.Current:X}", option != null);
        }
        // This model reports F9 maximum=255; its confirmed UI domain remains 0..100, step 5.
        // The reply envelope must never become the editable range or the encoded write domain.
        bool valid = raw.Maximum > 0 && raw.Maximum <= (control == OsdControl.BlackStabilizer ? 255u : 100u)
            && raw.Current <= 100 && raw.Current <= raw.Maximum
            && (control != OsdControl.BlackStabilizer || raw.Current % 5 == 0);
        return new(control, Name(control), raw, valid ? raw.Current.ToString() : $"未确认原始值 {raw.Current}", valid);
    }
    public static OsdWriteCandidate DescribeCandidate(OsdControl control, uint value)
    {
        byte code = Code(control);
        bool valid = control switch {
            OsdControl.PictureMode => PictureModes.Any(o => o.Raw == value),
            OsdControl.ColorTemperature => ColorTemperatures.Any(o => o.Raw == value),
            OsdControl.Contrast => value <= 100,
            OsdControl.BlackStabilizer => value <= 100 && value % 5 == 0,
            _ => false };
        if (!valid) throw new ArgumentOutOfRangeException(nameof(value), "不在已恢复的参数表中；不猜测、不截断。");
        return new(code, control == OsdControl.BlackStabilizer ? value / 5 : value, value);
    }
    public static bool IsValidEncodedWrite(byte code, uint value) => code switch {
        0x15 => PictureModes.Any(o => o.Raw == value),
        0x14 => ColorTemperatures.Any(o => o.Raw == value),
        0x12 => false,
        0xF9 => value <= 20,
        _ => false };
    public static IReadOnlyList<OsdReading> Decode(MonitorSnapshot snapshot) => Controls.Select(c =>
        Decode(c, snapshot.Get(Code(c)) ?? throw new ArgumentException("对照快照缺少控制项。", nameof(snapshot)))).ToArray();
}

// In-memory comparison only. Neither this type nor the codec has a transport or write path.
public sealed class OsdComparison
{
    public MonitorSnapshot? Baseline { get; private set; }
    public MonitorSnapshot? Current { get; private set; }
    public void Accept(MonitorSnapshot snapshot)
    {
        if (OsdProtocol.Decode(snapshot).Any(r => !r.Raw.Success)) throw new ArgumentException("失败快照不能作为当前对照。");
        if (Baseline?.Device.Id != snapshot.Device.Id) Baseline = null;
        Current = snapshot with { Values = Array.AsReadOnly(snapshot.Values.ToArray()) };
    }
    public void InvalidateCurrent() => Current = null;
    public void SaveBaseline() => Baseline = Current ?? throw new InvalidOperationException("请先读取状态。");
    public IReadOnlyList<OsdDifference> Differences => Baseline == null || Current == null ? []
        : OsdProtocol.Decode(Baseline).Zip(OsdProtocol.Decode(Current), (a, b) => new OsdDifference(a, b)).ToArray();
}
