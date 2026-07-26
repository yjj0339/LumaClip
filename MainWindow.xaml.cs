using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using LumaClip.Core;
using LumaClip.Models;
using LumaClip.Services;
using LumaClip.ViewModels;
using Forms = System.Windows.Forms;

namespace LumaClip;

public partial class MainWindow : Window
{
    const int WM_HOTKEY = 0x0312;
    const int HOTKEY_MAIN = 1101;
    const int HOTKEY_FLOAT = 1102;
    readonly AppServices _services;
    readonly MainViewModel _vm;
    readonly Forms.NotifyIcon _tray;
    HwndSource? _hwndSource;
    FloatingWindow? _floating;
    bool _reallyExit;

    public MainWindow(AppServices services)
    {
        InitializeComponent();
        _services = services;
        _vm = new MainViewModel(services);
        DataContext = _vm;
        _vm.PropertyChanged += (_, args) => {
            if (args.PropertyName == nameof(MainViewModel.SelectedItem))
                Dispatcher.BeginInvoke(AnimateInspector);
        };
        _services.Clipboard.Captured += Clipboard_Captured;
        _tray = CreateTray();
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Closing += OnClosing;
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized) Hide(); };
    }

    Forms.NotifyIcon CreateTray()
    {
        var tray = new Forms.NotifyIcon {
            Icon = new System.Drawing.Icon(System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/LumaClip.ico"))!.Stream),
            Text = "LumaClip · 正在监听",
            Visible = true
        };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("打开 LumaClip", null, (_, _) => Dispatcher.Invoke(ShowMain));
        menu.Items.Add("显示/隐藏悬浮窗", null, (_, _) => Dispatcher.Invoke(ToggleFloating));
        menu.Items.Add(new Forms.ToolStripSeparator());
        var pause = new Forms.ToolStripMenuItem("暂停监听") { Checked = _services.Settings.Current.PauseCapture, CheckOnClick = true };
        pause.CheckedChanged += (_, _) => Dispatcher.Invoke(() => {
            _services.Settings.Current.PauseCapture = pause.Checked; _services.Settings.Save();
            _vm.Status = pause.Checked ? "监听已暂停" : "正在监听剪贴板";
            tray.Text = pause.Checked ? "LumaClip · 已暂停" : "LumaClip · 正在监听";
        });
        menu.Items.Add(pause);
        var privacy = new Forms.ToolStripMenuItem("隐私模式") { Checked = _services.Settings.Current.PrivacyMode, CheckOnClick = true };
        privacy.CheckedChanged += (_, _) => Dispatcher.Invoke(() => {
            _services.Settings.Current.PrivacyMode = privacy.Checked; _services.Settings.Save();
            _vm.Status = privacy.Checked ? "隐私模式 · 不记录新内容" : "正在监听剪贴板";
        });
        menu.Items.Add(privacy);
        menu.Items.Add("设置", null, (_, _) => Dispatcher.Invoke(OpenSettings));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("完全退出", null, (_, _) => Dispatcher.Invoke(ExitApplication));
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowMain);
        return tray;
    }

    void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (!_services.Settings.Current.ReducedTransparency)
            WindowsIntegration.TryApplyBackdrop(this, ThemeManager.IsDark);
        _hwndSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        _hwndSource.AddHook(WndProc);
        _services.Clipboard.Attach(this);
        RegisterHotkeys();
    }

    async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _vm.RefreshAsync(false);
        if (!(_services.Settings.Current.ReducedMotion || WindowsIntegration.IsReducedMotion())) {
            Opacity = 0;
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
    }

    void RegisterHotkeys()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var main = WindowsIntegration.ParseHotkey(_services.Settings.Current.MainHotkey);
        var floating = WindowsIntegration.ParseHotkey(_services.Settings.Current.FloatHotkey);
        if (!RegisterHotKey(hwnd, HOTKEY_MAIN, main.Modifiers | 0x4000, main.Key))
            _services.Logger.Info("hotkey_registration_failed", "main");
        if (!RegisterHotKey(hwnd, HOTKEY_FLOAT, floating.Modifiers | 0x4000, floating.Key))
            _services.Logger.Info("hotkey_registration_failed", "floating");
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY) {
            if (wParam.ToInt32() == HOTKEY_MAIN) ShowMain();
            else if (wParam.ToInt32() == HOTKEY_FLOAT) ToggleFloating();
            handled = true;
        }
        return IntPtr.Zero;
    }

    async void Clipboard_Captured(object? sender, ClipboardItem item)
    {
        await Dispatcher.InvokeAsync(async () => {
            await _vm.AddCapturedAsync(item);
            AnimateShelfInsert();
            _floating?.NotifyNewItem(item);
        });
    }

    public void ShowMain()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Topmost = true; Topmost = false;
        Focus();
        SearchBox.Focus();
    }

    public void ToggleFloating()
    {
        if (_floating is null) {
            _floating = new FloatingWindow(_services, _vm);
            _floating.Closed += (_, _) => _floating = null;
            _floating.Show();
        } else if (_floating.IsVisible) _floating.Hide();
        else { _floating.Show(); _floating.Activate(); }
    }

    async void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag }) return;
        foreach (var nav in new[] { AllNav, AllRecordsNav, TextNav, ImageNav, LinkNav, FilesNav, FavoriteNav, PinnedNav, TrashNav,
                                   AllChip, TextChip, ImageChip, LinkChip, FilesChip, FavoriteChip, PinnedChip })
            nav.Background = System.Windows.Media.Brushes.Transparent;
        ((Button)sender).SetResourceReference(BackgroundProperty, "SelectedBrush");
        var (filter, title, trash) = tag switch {
            "link" => ("link", "链接", false),
            "text" => ("text", "文本与代码", false), "image" => ("image", "图片与截图", false),
            "files" => ("files", "文件与文件夹", false), "favorite" => ("favorite", "收藏", false),
            "pinned" => ("pinned", "置顶", false), "trash" => ("all", "回收站", true),
            _ => ("all", "全部记录", false)
        };
        await _vm.SelectFilterAsync(filter, title, trash);
        AnimateList();
    }

    void AnimateList()
    {
        if (_services.Settings.Current.ReducedMotion || WindowsIntegration.IsReducedMotion()) return;
        var translate = new System.Windows.Media.TranslateTransform(0, 10);
        HistoryList.RenderTransform = translate;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        HistoryList.BeginAnimation(OpacityProperty, new DoubleAnimation(.35, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
        translate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
            new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(280)) { EasingFunction = ease });
    }

    void AnimateShelfInsert()
    {
        if (_services.Settings.Current.ReducedMotion || WindowsIntegration.IsReducedMotion()) return;
        HistoryList.BeginAnimation(OpacityProperty,
            new DoubleAnimation(.72, 1, TimeSpan.FromMilliseconds(240)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    void AnimateInspector()
    {
        if (_services.Settings.Current.ReducedMotion || WindowsIntegration.IsReducedMotion()) return;
        var scale = new System.Windows.Media.ScaleTransform(.985, .985);
        var translate = new System.Windows.Media.TranslateTransform(14, 0);
        var group = new System.Windows.Media.TransformGroup();
        group.Children.Add(scale);
        group.Children.Add(translate);
        InspectorPanel.RenderTransform = group;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        InspectorPanel.BeginAnimation(OpacityProperty,
            new DoubleAnimation(.55, 1, TimeSpan.FromMilliseconds(240)) { EasingFunction = ease });
        scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty,
            new DoubleAnimation(.985, 1, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease });
        scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty,
            new DoubleAnimation(.985, 1, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease });
        translate.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty,
            new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease });
    }

    async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await _vm.RefreshAsync();
        AnimateList();
    }

    async void CopySelected()
    {
        if (_vm.SelectedItem is not { } item) return;
        try {
            _services.Clipboard.CopyBack(item);
            await _services.Database.RecordReuseAsync(item.Id);
            _vm.Status = $"已复制 · {item.KindLabel}";
            await _vm.RefreshAsync();
        } catch (Exception ex) { MessageBox.Show(this, ex.Message, "无法复制", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    void Copy_Click(object sender, RoutedEventArgs e) => CopySelected();
    void HistoryList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => CopySelected();

    async void Favorite_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Button { Tag: ClipboardItem item }) await _vm.ToggleFavoriteAsync(item);
    }
    async void DetailFavorite_Click(object sender, RoutedEventArgs e) { if (_vm.SelectedItem is { } item) await _vm.ToggleFavoriteAsync(item); }
    async void Pin_Click(object sender, RoutedEventArgs e) { if (_vm.SelectedItem is { } item) await _vm.TogglePinnedAsync(item); }
    async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedItem is { } item) await _vm.DeleteOrRestoreAsync(item);
    }
    async void Metadata_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_vm.SelectedItem is { } item) await _vm.SaveMetadataAsync(item);
    }
    void Reveal_Click(object sender, RoutedEventArgs e) { if (_vm.SelectedItem is { } item) item.IsSensitive = false; }

    void OpenImage_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedItem is not { Kind: ClipKind.Image, ContentPath: { } path } || !File.Exists(path)) {
            MessageBox.Show(this, "原图文件已不存在，无法打开。", "图片不可用", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "无法打开图片", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    void ShowInExplorer_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedItem is not { } item) return;
        var path = item.Kind == ClipKind.Image ? item.ContentPath : item.Text.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(path)) return;
        try {
            if (File.Exists(path)) Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            else if (Directory.Exists(path)) Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
            else MessageBox.Show(this, "原始文件已被移动或删除。历史记录本身不会删除用户文件。", "找不到文件", MessageBoxButton.OK, MessageBoxImage.Information);
        } catch (Exception ex) { MessageBox.Show(this, ex.Message, "无法打开资源管理器", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    async void EmptyTrash_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "将永久删除回收站中的记录及 LumaClip 保存的图片副本。用户原始文件不会受影响。", "清空回收站",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        await _services.Database.EmptyTrashAsync();
        await _vm.RefreshAsync(false);
    }

    void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings();
    void OpenSettings()
    {
        var window = new SettingsWindow(_services) { Owner = this };
        if (window.ShowDialog() == true) {
            ThemeManager.Apply(_services.Settings.Current.Theme, _services.Settings.Current.ReducedTransparency);
            if (!_services.Settings.Current.ReducedTransparency)
                WindowsIntegration.TryApplyBackdrop(this, ThemeManager.IsDark);
            _floating?.ApplySettings();
            _ = _vm.RefreshAsync();
        }
    }
    void Floating_Click(object sender, RoutedEventArgs e) => ToggleFloating();

    void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_reallyExit) {
            e.Cancel = true; Hide();
            _tray.ShowBalloonTip(1200, "LumaClip 仍在运行", "可从托盘或 Ctrl+Shift+V 再次打开。", Forms.ToolTipIcon.Info);
        }
    }

    public void ExitApplication()
    {
        _reallyExit = true;
        _floating?.Close();
        _tray.Visible = false; _tray.Dispose();
        var hwnd = new WindowInteropHelper(this).Handle;
        UnregisterHotKey(hwnd, HOTKEY_MAIN); UnregisterHotKey(hwnd, HOTKEY_FLOAT);
        _hwndSource?.RemoveHook(WndProc);
        Close();
        System.Windows.Application.Current.Shutdown();
    }

    [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint virtualKey);
    [DllImport("user32.dll", SetLastError = true)] static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}
