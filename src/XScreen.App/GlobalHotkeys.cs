using System.Runtime.InteropServices;
using System.Text.Json;
using XScreen.Core;

namespace XScreen.App;

enum HotkeyAction { BrightnessUp, BrightnessDown, VolumeUp, VolumeDown, SharpnessUp, SharpnessDown }
sealed record HotkeyChord(uint Modifiers, uint Key)
{
    public bool Valid => (Modifiers & ~7u) == 0 && (Modifiers & 3) != 0 &&
        (Key is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A or >= 0x70 and <= 0x87 && Key != 0x7B || Key is >= 0x21 and <= 0x28 or 0x2D or 0x2E);
    public override string ToString() => string.Join(" + ", new[] { (Modifiers & 2) != 0 ? "Ctrl" : null,
        (Modifiers & 1) != 0 ? "Alt" : null, (Modifiers & 4) != 0 ? "Shift" : null, KeyName(Key) }.Where(s => s != null));
    static string KeyName(uint key) => key switch { 0x21 => "Page Up", 0x22 => "Page Down", 0x23 => "End", 0x24 => "Home", 0x25 => "←", 0x26 => "↑", 0x27 => "→", 0x28 => "↓", 0x2D => "Insert", 0x2E => "Delete", >= 0x70 and <= 0x87 => "F" + (key - 0x6F), _ => ((char)key).ToString() };
}
sealed record HotkeyBinding(HotkeyAction Action, HotkeyChord? Chord);
sealed record HotkeySettings(bool Enabled, HotkeyBinding[] Bindings)
{
    public static HotkeySettings Defaults() => new(true, Enum.GetValues<HotkeyAction>().Select((a, i) => new HotkeyBinding(a, new(7, new uint[] { 0x26, 0x28, 0x27, 0x25, 0x21, 0x22 }[i]))).ToArray());
    public string? Error => Bindings == null || Bindings.Length != 6 || Bindings.Any(b => b == null || !Enum.IsDefined(b.Action) || b.Chord is { Valid: false }) || Bindings.Select(b => b.Action).Distinct().Count() != 6
        ? "快捷键配置无效，请重新设置。" : Bindings.Where(b => b.Chord != null).GroupBy(b => b.Chord).Any(g => g.Count() > 1) ? "两个操作使用了相同的快捷键，请修改后保存。" : null;
    public static string Name(HotkeyAction action) => action switch { HotkeyAction.BrightnessUp => "亮度增加", HotkeyAction.BrightnessDown => "亮度降低", HotkeyAction.VolumeUp => "显示器音量增加", HotkeyAction.VolumeDown => "显示器音量降低", HotkeyAction.SharpnessUp => "锐度增加", _ => "锐度降低" };
    // Hotkeys act away from the window, so they use the coarse increment from the spec
// rather than a hard-coded number that could drift from the tray and the window.
    public static (byte Code, int Delta) Target(HotkeyAction action)
    {
        var (code, up) = action switch
        {
            HotkeyAction.BrightnessUp => ((byte)0x10, true), HotkeyAction.BrightnessDown => ((byte)0x10, false),
            HotkeyAction.VolumeUp => ((byte)0x62, true), HotkeyAction.VolumeDown => ((byte)0x62, false),
            HotkeyAction.SharpnessUp => ((byte)0x87, true), HotkeyAction.SharpnessDown => ((byte)0x87, false),
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };
        int coarse = (int)(ControlSpec.ByCode(code)?.CoarseStep ?? 10);
        return (code, up ? coarse : -coarse);
    }
}
static class HotkeyStore
{
    internal static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XScreen", "hotkeys.json");
    public static HotkeySettings Load(string path)
    {
        try
        {
            // File.Exists also returns false for directories and denied access. Only a
            // genuinely absent file is permission to enable the default shortcuts.
            if ((File.GetAttributes(path) & FileAttributes.Directory) != 0) throw new IOException("快捷键配置路径是目录。");
            var value = JsonSerializer.Deserialize<HotkeySettings>(File.ReadAllText(path)); if (value?.Error == null && value != null) return value;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return HotkeySettings.Defaults(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { AppLog.Write("hotkeys-load", ex.Message); }
        // An invalid existing file must not silently activate different shortcuts.
        return new(false, Enum.GetValues<HotkeyAction>().Select(a => new HotkeyBinding(a, null)).ToArray());
    }
    public static bool Save(string path, HotkeySettings settings)
    {
        if (settings.Error != null) return false;
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!); File.WriteAllText(temp, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true })); File.Move(temp, path, true); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Write("hotkeys-save", ex.Message); return false; }
        finally { try { File.Delete(temp); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } }
    }
}
interface IHotkeyRegistrar : IDisposable
{
    bool Register(int id, HotkeyChord chord);
    void Unregister(int id);
}
sealed class GlobalHotkeys(IHotkeyRegistrar registrar, Action<HotkeyAction> invoke) : IDisposable
{
    readonly Dictionary<int, HotkeyBinding> active = [];
    int nextId = 100;
    bool disposed;
    public string? LastError { get; private set; }
    public HotkeySettings Settings { get; private set; } = new(false, Enum.GetValues<HotkeyAction>().Select(a => new HotkeyBinding(a, null)).ToArray());
    public string? Start(HotkeySettings settings)
    {
        var error = TryApply(settings);
        if (error != null && settings.Error == null) Settings = settings with { Bindings = settings.Bindings.ToArray() };
        return error;
    }
    public string? TryApply(HotkeySettings settings, Func<HotkeySettings, bool>? save = null)
        => LastError = Apply(settings, save);
    string? Apply(HotkeySettings settings, Func<HotkeySettings, bool>? save)
    {
        if (disposed) return "快捷键服务已停止。";
        if (settings.Error is { } invalid) return invalid;
        settings = settings with { Bindings = settings.Bindings.ToArray() };
        Dictionary<int, HotkeyBinding> desired = []; List<int> acquired = [];
        try
        {
            foreach (var binding in settings.Enabled ? settings.Bindings.Where(b => b.Chord != null) : [])
            {
                var reused = active.FirstOrDefault(p => p.Value == binding);
                if (reused.Key != 0) { desired.Add(reused.Key, binding); continue; }
                // IDs are never reused: queued messages from removed bindings stay inert.
                if (++nextId > 0xBFFF) return "快捷键修改次数过多，请重新启动程序。";
                if (!registrar.Register(nextId, binding.Chord!)) return $"{HotkeySettings.Name(binding.Action)}：{binding.Chord} 已被占用或系统不允许注册，请换一组。";
                acquired.Add(nextId); desired.Add(nextId, binding);
            }
            if (save != null && !save(settings)) return "快捷键未能保存，原设置保持不变。请检查用户文件夹权限后重试。";
            foreach (var id in active.Keys.Except(desired.Keys).ToArray()) registrar.Unregister(id);
            active.Clear(); foreach (var pair in desired) active.Add(pair.Key, pair.Value);
            acquired.Clear(); Settings = settings; return null;
        }
        finally { foreach (int id in acquired) registrar.Unregister(id); }
    }
    public void Pause() { foreach (int id in active.Keys) registrar.Unregister(id); active.Clear(); }
    public void Dispatch(int id, uint modifiers, uint key)
    {
        if (!disposed && active.TryGetValue(id, out var binding) && binding.Chord == new HotkeyChord(modifiers, key)) invoke(binding.Action);
    }
    public void Dispose() { if (disposed) return; disposed = true; Pause(); registrar.Dispose(); }
}

// Separate subclass from the tray, so hiding the window or restarting Explorer does not affect hotkeys.
sealed class NativeHotkeyRegistrar : IHotkeyRegistrar
{
    readonly IntPtr hwnd;
    readonly SubclassProc callback;
    readonly HashSet<int> registered = [];
    bool disposed;
    public Action<int, uint, uint>? Pressed;
    public NativeHotkeyRegistrar(IntPtr handle)
    {
        hwnd = handle; callback = WindowProc;
        if (!SetWindowSubclass(hwnd, callback, 3, 0)) throw new InvalidOperationException("无法初始化全局快捷键。");
    }
    public bool Register(int id, HotkeyChord chord)
    {
        if (disposed || !RegisterHotKey(hwnd, id, chord.Modifiers | 0x4000, chord.Key)) return false; // MOD_NOREPEAT
        registered.Add(id); return true;
    }
    public void Unregister(int id) { if (registered.Remove(id)) UnregisterHotKey(hwnd, id); }
    IntPtr WindowProc(IntPtr h, uint message, IntPtr w, IntPtr l, nuint id, nuint data)
    {
        if (message == 0x312)
        {
            try { if (!disposed) Pressed?.Invoke(w.ToInt32(), (uint)((long)l & 0xffff), (uint)(((long)l >> 16) & 0xffff)); }
            catch (Exception ex) { AppLog.Write("hotkeys-dispatch", ex.ToString()); }
            return IntPtr.Zero;
        }
        return DefSubclassProc(h, message, w, l);
    }
    public void Dispose() { if (disposed) return; foreach (int id in registered.ToArray()) Unregister(id); disposed = true; RemoveWindowSubclass(hwnd, callback, 3); }
    delegate IntPtr SubclassProc(IntPtr hwnd, uint message, IntPtr w, IntPtr l, nuint id, nuint data);
    [DllImport("comctl32.dll")] static extern bool SetWindowSubclass(IntPtr hwnd, SubclassProc proc, nuint id, nuint data);
    [DllImport("comctl32.dll")] static extern bool RemoveWindowSubclass(IntPtr hwnd, SubclassProc proc, nuint id);
    [DllImport("comctl32.dll")] static extern IntPtr DefSubclassProc(IntPtr hwnd, uint message, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}
