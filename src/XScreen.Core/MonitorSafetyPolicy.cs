namespace XScreen.Core;

public static class MonitorSafetyPolicy
{
    public const string ContrastRestriction = "对比度当前受限，已禁用调节；仅显示读取值。";
    public static bool IsReadOnly(byte code) => code == 0x12;
    public static void EnsureWriteAllowed(byte code)
    {
        if (IsReadOnly(code)) throw new InvalidOperationException(ContrastRestriction);
        if (code is 0xF5 or 0xF8)
            throw new InvalidOperationException($"Control 0x{code:X2} is disabled following the black-screen incident, including recovery writes.");
    }

    // No argument, environment variable or journal can enable research hardware access.
    // Re-enabling requires an explicitly authorized code change after incident review.
    public static void EnsureResearchHardwareAllowed() =>
        throw new InvalidOperationException("Research hardware access is suspended. Offline checks only; historical journals must not be restored.");
}
