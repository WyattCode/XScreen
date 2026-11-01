using System.Text.Json;
using Windows.Graphics;

namespace XScreen.App;

// Logical outer dimensions survive DPI changes; coordinates are deliberately not restored.
sealed record SavedWindowState(double Width, double Height, bool Maximized);

static class WindowStateStore
{
    // Minimum size has to sit below the compact threshold, otherwise the compact layout can never
    // engage and that whole branch is unreachable. Compact needs about 820; 900 leaves slack so
    // the rail is never squeezed, and it stays under the 1000 threshold in MainWindow.
    internal const double MinimumWidth = 840, MinimumHeight = 900;
    internal static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XScreen", "window-state.json");
    internal static bool Valid(SavedWindowState state) => double.IsFinite(state.Width) && double.IsFinite(state.Height)
        && state.Width > 0 && state.Height > 0 && state.Width <= 16384 && state.Height <= 16384;
    internal static SavedWindowState? Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var state = JsonSerializer.Deserialize<SavedWindowState>(File.ReadAllText(path));
            return state != null && Valid(state) ? state : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { AppLog.Write("window-state-load-failed", ex.Message); return null; }
    }
    internal static bool Save(string path, SavedWindowState state)
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
        { AppLog.Write("window-state-save-failed", ex.Message); return false; }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
    internal static RectInt32 Bounds(RectInt32 work, double scale, SavedWindowState state)
    {
        if (!double.IsFinite(scale) || scale <= 0) scale = 1;
        int margin = Math.Min((int)Math.Ceiling(16 * scale), Math.Max(0, (Math.Min(work.Width, work.Height) - 1) / 2));
        int width = (int)Math.Min(Math.Round(Math.Max(840, state.Width) * scale), Math.Max(1, work.Width - 2 * margin));
        // The floor tracks the control column's own floor so a size saved by an older build
        // cannot restore a window too short for the rail.
        int height = (int)Math.Min(Math.Round(Math.Max(MinimumHeight, state.Height) * scale), Math.Max(1, work.Height - 2 * margin));
        return new(work.X + (work.Width - width) / 2, work.Y + (work.Height - height) / 2, width, height);
    }
}
