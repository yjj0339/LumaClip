using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using LumaClip.Core;
using LumaClip.Models;
using LumaClip.Services;
using Forms = System.Windows.Forms;

namespace LumaClip;

public partial class SettingsWindow : Window
{
    readonly AppServices _services;
    public SettingsWindow(AppServices services)
    {
        InitializeComponent();
        _services = services;
        Loaded += SettingsWindow_Loaded;
        SourceInitialized += (_, _) => {
            if (!services.Settings.Current.ReducedTransparency)
                WindowsIntegration.TryApplyBackdrop(this, ThemeManager.IsDark, true);
        };
    }

    async void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var s = _services.Settings.Current;
        ThemeCombo.SelectedIndex = s.Theme switch { AppTheme.Light => 1, AppTheme.Dark => 2, _ => 0 };
        StartupCheck.IsChecked = s.StartWithWindows; MinimizedCheck.IsChecked = s.LaunchMinimized;
        PauseCheck.IsChecked = s.PauseCapture; PrivacyCheck.IsChecked = s.PrivacyMode; SensitiveCheck.IsChecked = s.HideSensitive;
        ReducedMotionCheck.IsChecked = s.ReducedMotion; ReducedTransparencyCheck.IsChecked = s.ReducedTransparency;
        MainHotkeyBox.Text = s.MainHotkey; FloatHotkeyBox.Text = s.FloatHotkey;
        ExcludedAppsBox.Text = string.Join(Environment.NewLine, s.ExcludedApps);
        FloatTopCheck.IsChecked = s.FloatAlwaysOnTop; FloatAutoCollapseCheck.IsChecked = s.FloatAutoCollapse;
        FloatClickThroughCheck.IsChecked = s.FloatClickThrough; FloatOpacitySlider.Value = s.FloatOpacity; FloatCountSlider.Value = s.FloatItemCount;
        FloatStyleCombo.SelectedIndex = s.FloatCompactStyle == FloatingCompactStyle.Island ? 1 : 0;
        DataRootBox.Text = s.DataRoot; MaxItemsBox.Text = s.MaxHistoryItems.ToString(); RetentionBox.Text = s.RetentionDays.ToString();
        await UpdateStorageAsync();
    }

    async Task UpdateStorageAsync()
    {
        var stats = await _services.Backup.GetStatsAsync();
        StorageTotalText.Text = StorageStats.FormatBytes(stats.TotalBytes);
        StorageDetailText.Text = $"数据库 {StorageStats.FormatBytes(stats.DatabaseBytes)}  ·  原图 {StorageStats.FormatBytes(stats.ImagesBytes)}  ·  缩略图 {StorageStats.FormatBytes(stats.ThumbnailsBytes)}\n{stats.ActiveItems} 条历史，{stats.DeletedItems} 条位于回收站";
    }

    void Section_Click(object sender, RoutedEventArgs e)
    {
        GeneralSection.Visibility = PrivacySection.Visibility = FloatingSection.Visibility = StorageSection.Visibility = Visibility.Collapsed;
        foreach (var nav in new[] { GeneralNav, PrivacyNav, FloatingNav, StorageNav })
            nav.Background = System.Windows.Media.Brushes.Transparent;
        if (sender is Button button) button.SetResourceReference(BackgroundProperty, "SelectedBrush");
        FrameworkElement activeSection;
        switch ((sender as Button)?.Tag?.ToString()) {
            case "privacy": activeSection = PrivacySection; break;
            case "floating": activeSection = FloatingSection; break;
            case "storage": activeSection = StorageSection; break;
            default: activeSection = GeneralSection; break;
        }
        activeSection.Visibility = Visibility.Visible;
        AnimateSection(activeSection);
    }

    void AnimateSection(FrameworkElement section)
    {
        if (_services.Settings.Current.ReducedMotion || WindowsIntegration.IsReducedMotion()) return;
        section.Opacity = 0;
        var translate = new TranslateTransform(0, 10);
        section.RenderTransform = translate;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        section.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
        translate.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(240)) { EasingFunction = ease });
    }

    async void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "全部历史将移入回收站，可以在清空回收站前恢复。是否继续？", "清除历史",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes) {
            await _services.Database.ClearHistoryAsync();
            await UpdateStorageAsync();
        }
    }

    async void Backup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog {
            Title = "保存 LumaClip 备份", Filter = "LumaClip 备份 (*.lcbak)|*.lcbak",
            FileName = $"LumaClip-Backup-{DateTime.Now:yyyyMMdd-HHmmss}.lcbak",
            InitialDirectory = Path.Combine(_services.Settings.DataRoot, "Backups")
        };
        if (dialog.ShowDialog(this) != true) return;
        try {
            var path = await _services.Backup.BackupAsync(dialog.FileName);
            MessageBox.Show(this, $"备份已创建：\n{path}", "备份完成", MessageBoxButton.OK, MessageBoxImage.Information);
        } catch (Exception ex) { MessageBox.Show(this, ex.Message, "备份失败", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    async void Restore_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "选择 LumaClip 备份", Filter = "LumaClip 备份 (*.lcbak)|*.lcbak" };
        if (dialog.ShowDialog(this) != true) return;
        if (MessageBox.Show(this, "恢复会替换当前数据库和 LumaClip 图片副本。建议先创建备份。是否继续？", "恢复备份",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try {
            await _services.Database.CheckpointAsync();
            await _services.Backup.RestoreAsync(dialog.FileName);
            MessageBox.Show(this, "恢复完成。LumaClip 将重新启动以载入数据。", "恢复完成", MessageBoxButton.OK, MessageBoxImage.Information);
            ((App)System.Windows.Application.Current).Restart();
        } catch (Exception ex) { MessageBox.Show(this, ex.Message, "恢复失败", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    async void ChangeDataRoot_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog { Description = "选择 LumaClip 数据保存目录", UseDescriptionForTitle = true, SelectedPath = _services.Settings.DataRoot };
        if (dialog.ShowDialog() != Forms.DialogResult.OK) return;
        if (MessageBox.Show(this, "LumaClip 将复制现有数据库、原图和缩略图到新目录，然后重新启动。", "迁移数据",
            MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK) return;
        try {
            await _services.Database.CheckpointAsync();
            await _services.Database.CopyDatabaseToAsync(Path.Combine(dialog.SelectedPath, "lumaclip.db"));
            await _services.Settings.MoveDataRootAsync(dialog.SelectedPath);
            ((App)System.Windows.Application.Current).Restart();
        } catch (Exception ex) { MessageBox.Show(this, ex.Message, "迁移失败", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        var s = _services.Settings.Current;
        s.Theme = ThemeCombo.SelectedIndex switch { 1 => AppTheme.Light, 2 => AppTheme.Dark, _ => AppTheme.System };
        s.LaunchMinimized = MinimizedCheck.IsChecked == true;
        s.PauseCapture = PauseCheck.IsChecked == true; s.PrivacyMode = PrivacyCheck.IsChecked == true; s.HideSensitive = SensitiveCheck.IsChecked == true;
        s.ReducedMotion = ReducedMotionCheck.IsChecked == true; s.ReducedTransparency = ReducedTransparencyCheck.IsChecked == true;
        s.MainHotkey = MainHotkeyBox.Text.Trim(); s.FloatHotkey = FloatHotkeyBox.Text.Trim();
        s.ExcludedApps = ExcludedAppsBox.Text.Split(["\r\n", "\n", ","], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        s.FloatAlwaysOnTop = FloatTopCheck.IsChecked == true; s.FloatAutoCollapse = FloatAutoCollapseCheck.IsChecked == true;
        s.FloatClickThrough = FloatClickThroughCheck.IsChecked == true; s.FloatOpacity = FloatOpacitySlider.Value; s.FloatItemCount = (int)FloatCountSlider.Value;
        s.FloatCompactStyle = FloatStyleCombo.SelectedIndex == 1 ? FloatingCompactStyle.Island : FloatingCompactStyle.Orb;
        if (int.TryParse(MaxItemsBox.Text, out var max)) s.MaxHistoryItems = Math.Clamp(max, 100, 1_000_000);
        if (int.TryParse(RetentionBox.Text, out var days)) s.RetentionDays = Math.Clamp(days, 0, 36500);
        try { _services.Settings.SetStartup(StartupCheck.IsChecked == true); _services.Settings.Save(); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "保存启动项失败", MessageBoxButton.OK, MessageBoxImage.Warning); }
        DialogResult = true;
    }
    void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
