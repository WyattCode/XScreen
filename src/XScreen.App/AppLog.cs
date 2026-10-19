using System.Text.Json;

namespace XScreen.App;

// A process owns one canonical JSONL file. The legacy aggregate is compatibility output only.
sealed class SessionLog(string directory)
{
    readonly object gate = new();
    readonly string session = DateTimeOffset.Now.ToString("yyyyMMddTHHmmssfff") + "-" + Environment.ProcessId + "-" + Guid.NewGuid().ToString("N")[..8];
    long sequence;
    public string SessionPath => Path.Combine(directory, "sessions", session + ".jsonl");
    public void Write(string mode, string category, string message)
    {
        lock (gate)
        {
            var at = DateTimeOffset.Now;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SessionPath)!);
                File.AppendAllText(SessionPath, JsonSerializer.Serialize(new { at, session, pid = Environment.ProcessId, sequence = ++sequence, mode, category, message }) + "\n");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            try
            {
                Directory.CreateDirectory(directory);
                File.AppendAllText(Path.Combine(directory, "app.log"), $"{at:O} [{category}] {message}\n");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}

static class AppLog
{
    public static string Folder => Path.Combine(AppContext.BaseDirectory, "runtime-data");
    static readonly SessionLog log = new(Folder);
    public static string Mode { get; set; } = "entry";
    public static void Write(string category, string message) => log.Write(Mode, category, message);
}
