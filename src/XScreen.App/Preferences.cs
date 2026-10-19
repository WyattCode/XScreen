using System.Text.Json;

namespace XScreen.App;

sealed record SavedCombination(string Name, string DeviceId, uint Brightness, uint Volume);
sealed class Preferences
{
    public bool Hotkeys { get; set; }
    public List<SavedCombination> Combinations { get; set; } = [];
    static string FilePath => Path.Combine(AppLog.Folder, "preferences.json");
    public static Preferences Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new();
            var preferences = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(FilePath)) ?? new();
            preferences.Combinations = (preferences.Combinations ?? []).Where(c => c.Brightness <= 100 && c.Volume <= 100).Take(6).ToList();
            return preferences;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        { AppLog.Write("preferences-load", ex.Message); return new(); }
    }
    public void Save()
    {
        Directory.CreateDirectory(AppLog.Folder);
        File.WriteAllText(FilePath + ".tmp", JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(FilePath + ".tmp", FilePath, true);
    }
}
