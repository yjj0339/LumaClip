namespace LumaClip.Services;

public sealed class AppServices : IDisposable
{
    public SettingsService Settings { get; }
    public SafeLogger Logger { get; }
    public DatabaseService Database { get; }
    public ClipboardMonitor Clipboard { get; }
    public BackupService Backup { get; }

    public AppServices()
    {
        Settings = new SettingsService();
        Logger = new SafeLogger(Settings.DataRoot);
        Database = new DatabaseService(Settings.DatabasePath);
        Clipboard = new ClipboardMonitor(Settings, Logger);
        Backup = new BackupService(Settings, Database);
    }
    public async Task InitializeAsync()
    {
        await Database.InitializeAsync();
        await Database.TrimAsync(Settings.Current.MaxHistoryItems, Settings.Current.RetentionDays);
    }
    public void Dispose()
    {
        Clipboard.Dispose();
        Database.Dispose();
    }
}
