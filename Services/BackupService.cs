using System.IO.Compression;
using LumaClip.Models;

namespace LumaClip.Services;

public sealed class BackupService(SettingsService settings, DatabaseService database)
{
    public async Task<string> BackupAsync(string destination)
    {
        await database.CheckpointAsync();
        if (Directory.Exists(destination))
            destination = Path.Combine(destination, $"LumaClip-Backup-{DateTime.Now:yyyyMMdd-HHmmss}.lcbak");
        if (!destination.EndsWith(".lcbak", StringComparison.OrdinalIgnoreCase)) destination += ".lcbak";
        if (File.Exists(destination)) File.Delete(destination);
        ZipFile.CreateFromDirectory(settings.DataRoot, destination, CompressionLevel.Optimal, false);
        return destination;
    }

    public async Task RestoreAsync(string archive)
    {
        if (!File.Exists(archive)) throw new FileNotFoundException("找不到备份文件。", archive);
        var temp = Path.Combine(Path.GetTempPath(), "LumaClipRestore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try {
            ZipFile.ExtractToDirectory(archive, temp);
            if (!File.Exists(Path.Combine(temp, "lumaclip.db"))) throw new InvalidDataException("不是有效的 LumaClip 备份。");
            foreach (var file in Directory.EnumerateFiles(temp, "*", SearchOption.AllDirectories)) {
                var relative = Path.GetRelativePath(temp, file);
                var target = Path.Combine(settings.DataRoot, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await using var input = File.OpenRead(file);
                await using var output = File.Create(target);
                await input.CopyToAsync(output);
            }
        } finally { try { Directory.Delete(temp, true); } catch { } }
    }

    public async Task<StorageStats> GetStatsAsync()
    {
        var counts = await database.CountAsync();
        long Size(string path) => Directory.Exists(path) ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Sum(f => { try { return new FileInfo(f).Length; } catch { return 0L; } }) : 0;
        var db = File.Exists(settings.DatabasePath) ? new FileInfo(settings.DatabasePath).Length : 0;
        return new(db, Size(settings.ImagesPath), Size(settings.ThumbnailsPath), counts.Active, counts.Deleted);
    }
}
