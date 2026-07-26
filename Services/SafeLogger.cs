using System.Text;

namespace LumaClip.Services;

public sealed class SafeLogger
{
    readonly string _path;
    readonly object _gate = new();
    public SafeLogger(string dataRoot)
    {
        var dir = Path.Combine(dataRoot, "Logs");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, $"lumaclip-{DateTime.Now:yyyyMMdd}.log");
    }
    public void Info(string eventName, string details = "") => Write("INFO", eventName, details);
    public void Error(string eventName, Exception ex) => Write("ERROR", eventName, $"{ex.GetType().Name}: {ex.Message}");
    void Write(string level, string eventName, string details)
    {
        try {
            var safe = details.Replace("\r", " ").Replace("\n", " ");
            if (safe.Length > 300) safe = safe[..300];
            lock (_gate) File.AppendAllText(_path, $"{DateTime.Now:O} [{level}] {eventName} {safe}\n", Encoding.UTF8);
        } catch { }
    }
}
