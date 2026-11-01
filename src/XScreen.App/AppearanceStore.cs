using System.Text.Json;

namespace XScreen.App;

// The user's explicit appearance choice survives restarts. Index order matches the
// selector: 0 = UltraGear dark, 1 = UltraGear light, 2 = follow Windows.
sealed record SavedAppearance(int Selection);

static class AppearanceStore
{
    internal const int MaxSelection = 2;
    internal static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XScreen", "appearance.json");
    internal static bool Valid(SavedAppearance state) => state.Selection is >= 0 and <= MaxSelection;
    internal static SavedAppearance? Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var state = JsonSerializer.Deserialize<SavedAppearance>(File.ReadAllText(path));
            return state != null && Valid(state) ? state : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { AppLog.Write("appearance-load-failed", ex.Message); return null; }
    }
    internal static bool Save(string path, SavedAppearance state)
    {
        if (!Valid(state)) return false;
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            if (Load(path) == state) return true;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(state));
            File.Move(temporary, path, true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { AppLog.Write("appearance-save-failed", ex.Message); return false; }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}