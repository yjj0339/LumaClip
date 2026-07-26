using System.Text.Json;
using System.Text.Json.Serialization;
using LumaClip.Models;
using Microsoft.Win32;

namespace LumaClip.Services;

public sealed class SettingsService
{
    static readonly JsonSerializerOptions JsonOptions = new() {
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };
    readonly string _bootstrapDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LumaClip");
    readonly string _bootstrapPath;
    public AppSettings Current { get; private set; }
    public string DataRoot => Current.DataRoot;
    public string DatabasePath => Path.Combine(DataRoot, "lumaclip.db");
    public string ImagesPath => Path.Combine(DataRoot, "Images");
    public string ThumbnailsPath => Path.Combine(DataRoot, "Thumbnails");

    public SettingsService()
    {
        _bootstrapPath = Path.Combine(_bootstrapDir, "settings.json");
        Directory.CreateDirectory(_bootstrapDir);
        Current = Load();
        if (string.IsNullOrWhiteSpace(Current.DataRoot))
            Current.DataRoot = Path.Combine(_bootstrapDir, "Data");
        EnsureDirectories();
        Save();
    }

    AppSettings Load()
    {
        try {
            if (File.Exists(_bootstrapPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_bootstrapPath), JsonOptions) ?? new();
        } catch { }
        return new();
    }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(DataRoot);
        Directory.CreateDirectory(ImagesPath);
        Directory.CreateDirectory(ThumbnailsPath);
        Directory.CreateDirectory(Path.Combine(DataRoot, "Backups"));
    }

    public void Save()
    {
        Directory.CreateDirectory(_bootstrapDir);
        var temp = _bootstrapPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(Current, JsonOptions));
        File.Move(temp, _bootstrapPath, true);
    }

    public void SetStartup(bool enabled)
    {
        const string keyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        using var key = Registry.CurrentUser.OpenSubKey(keyPath, true);
        if (enabled)
            key?.SetValue("LumaClip", $"\"{Environment.ProcessPath}\" --minimized");
        else key?.DeleteValue("LumaClip", false);
        Current.StartWithWindows = enabled;
        Save();
    }

    public async Task MoveDataRootAsync(string newRoot)
    {
        newRoot = Path.GetFullPath(newRoot);
        if (string.Equals(newRoot.TrimEnd('\\'), DataRoot.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return;
        Directory.CreateDirectory(newRoot);
        foreach (var source in Directory.EnumerateFiles(DataRoot, "*", SearchOption.AllDirectories))
        {
            // Database files are copied through SQLite's backup API so that its
            // open handle and WAL file cannot cause a Windows sharing violation.
            var fileName = Path.GetFileName(source);
            if (fileName.Equals("lumaclip.db", StringComparison.OrdinalIgnoreCase) ||
                fileName.StartsWith("lumaclip.db-", StringComparison.OrdinalIgnoreCase))
                continue;
            var relative = Path.GetRelativePath(DataRoot, source);
            var destination = Path.Combine(newRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
            await input.CopyToAsync(output);
        }
        Current.DataRoot = newRoot;
        EnsureDirectories();
        Save();
    }
}
