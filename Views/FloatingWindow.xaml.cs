using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using LumaClip.Core;
using LumaClip.Models;
using LumaClip.Services;
using LumaClip.ViewModels;
using Forms = System.Windows.Forms;

namespace LumaClip;

public partial class FloatingWindow : Window
{
    readonly AppServices _services;
    readonly MainViewModel _mainVm;
    readonly ObservableCollection<ClipboardItem> _items = [];
    readonly DispatcherTimer _searchTimer;
    readonly DispatcherTimer _collapseTimer;
    double _expandedWidth = 390, _expandedHeight = 560;
    bool _collapsed;
    FloatingCompactStyle _compactStyle;

    public FloatingWindow(AppServices services, MainViewModel mainVm)
    {
        InitializeComponent();
        _services = services; _mainVm = mainVm;
        FloatList.ItemsSource = _items;
        var s = services.Settings.Current;
        Topmost = s.FloatAlwaysOnTop; Opacity = s.FloatOpacity;
        _expandedWidth = Math.Max(300, s.FloatWidth);
        _expandedHeight = Math.Max(350, s.FloatHeight);
        _compactStyle = s.FloatCompactStyle;
        Width = _expandedWidth; Height = _expandedHeight;
        if (!double.IsNaN(s.FloatLeft)) Left = s.FloatLeft;
        if (!double.IsNaN(s.FloatTop)) Top = s.FloatTop;
        _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) };
        _searchTimer.Tick += async (_, _) => { _searchTimer.Stop(); await RefreshAsync(); };
        _collapseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
        _collapseTimer.Tick += (_, _) => { _collapseTimer.Stop(); if (s.FloatAutoCollapse && !IsMouseOver) Collapse(); };
        MouseEnter += (_, _) => { _collapseTimer.Stop(); if (_collapsed && s.FloatAutoCollapse) Expand(); };
        MouseLeave += (_, _) => _collapseTimer.Start();
        Loaded += async (_, _) => {
            if (!s.ReducedTransparency)
                WindowsIntegration.TryApplyBackdrop(this, ThemeManager.IsDark, true);
            await RefreshAsync();
            UpdatePresentationControls();
            if (s.FloatCollapsed) Collapse(false);
            WindowsIntegration.SetClickThrough(this, s.FloatClickThrough);
        };
        LocationChanged += (_, _) => SaveBounds();
        SizeChanged += (_, _) => { if (!_collapsed) { _expandedWidth = Width; _expandedHeight = Height; SaveBounds(); } };
        Closed += (_, _) => SaveBounds();
    }

    async Task RefreshAsync()
    {
        var rows = await _services.Database.QueryAsync(FloatSearchBox.Text, "all", false, _services.Settings.Current.FloatItemCount);
        _items.Clear();
        foreach (var item in rows) {
            item.IsSensitive = _services.Settings.Current.HideSensitive && ClipboardMonitor.IsSensitiveText(item.Text);
            _items.Add(item);
        }
        var latest = _items.FirstOrDefault();
        IslandLatestText.Text = latest?.DisplayText ?? "剪贴板已就绪";
        IslandMetaText.Text = latest is null ? "本地保存 · 不上传" : $"{latest.SourceApp} · {latest.TimeLabel}";
        IslandCountText.Text = Math.Min(_items.Count, 99).ToString();
    }

    void SaveBounds()
    {
        var s = _services.Settings.Current;
        s.FloatLeft = Left; s.FloatTop = Top;
        if (!_collapsed) { s.FloatWidth = Width; s.FloatHeight = Height; }
        s.FloatCollapsed = _collapsed;
        _services.Settings.Save();
    }

    void DragHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        BeginWindowDrag(e);
    }

    void Orb_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount > 1) { Expand(); return; }
        BeginWindowDrag(e);
    }

    void Island_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount > 1) { Expand(); return; }
        BeginWindowDrag(e);
    }

    void BeginWindowDrag(MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed ||
            FindAncestor<System.Windows.Controls.Primitives.ButtonBase>(e.OriginalSource as DependencyObject) is not null) return;
        e.Handled = true;
        StopPositionAnimations();
        try { DragMove(); SnapToEdge(); } catch { }
    }

    static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null) {
            if (current is T match) return match;
            current = current is Visual or Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }
        return null;
    }

    void StopPositionAnimations()
    {
        var currentLeft = Left;
        var currentTop = Top;
        BeginAnimation(LeftProperty, null);
        BeginAnimation(TopProperty, null);
        Left = currentLeft;
        Top = currentTop;
    }

    void SnapToEdge()
    {
        var screen = Forms.Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
        var work = new Rect(screen.WorkingArea.Left / dpi.DpiScaleX, screen.WorkingArea.Top / dpi.DpiScaleY,
            screen.WorkingArea.Width / dpi.DpiScaleX, screen.WorkingArea.Height / dpi.DpiScaleY);
        const double threshold = 30;
        var targetLeft = Left;
        var targetTop = Top;
        if (Math.Abs(Left - work.Left) < threshold) targetLeft = work.Left + 5;
        if (Math.Abs(Left + Width - work.Right) < threshold) targetLeft = work.Right - Width - 5;
        if (Math.Abs(Top - work.Top) < threshold) targetTop = work.Top + 5;
        if (Math.Abs(Top + Height - work.Bottom) < threshold) targetTop = work.Bottom - Height - 5;
        targetLeft = Math.Clamp(targetLeft, work.Left, Math.Max(work.Left, work.Right - Width));
        targetTop = Math.Clamp(targetTop, work.Top, Math.Max(work.Top, work.Bottom - Height));
        if (_services.Settings.Current.ReducedMotion || WindowsIntegration.IsReducedMotion()) {
            Left = targetLeft; Top = targetTop; SaveBounds(); return;
        }
        StopPositionAnimations();
        var ease = new BackEase { Amplitude = .16, EasingMode = EasingMode.EaseOut };
        var leftAnimation = new DoubleAnimation(Left, targetLeft, TimeSpan.FromMilliseconds(300)) { EasingFunction = ease };
        var topAnimation = new DoubleAnimation(Top, targetTop, TimeSpan.FromMilliseconds(300)) { EasingFunction = ease };
        topAnimation.Completed += (_, _) => {
            BeginAnimation(LeftProperty, null);
            BeginAnimation(TopProperty, null);
            Left = targetLeft;
            Top = targetTop;
            SaveBounds();
        };
        BeginAnimation(LeftProperty, leftAnimation);
        BeginAnimation(TopProperty, topAnimation);
    }

    void Collapse_Click(object sender, RoutedEventArgs e) => Collapse();
    void Presentation_Click(object sender, RoutedEventArgs e)
    {
        _compactStyle = _compactStyle == FloatingCompactStyle.Orb
            ? FloatingCompactStyle.Island
            : FloatingCompactStyle.Orb;
        _services.Settings.Current.FloatCompactStyle = _compactStyle;
        _services.Settings.Save();
        UpdatePresentationControls();
        if (_compactStyle == FloatingCompactStyle.Island) Collapse();
    }

    void Collapse(bool animate = true)
    {
        if (_collapsed) return;
        _expandedWidth = Math.Max(300, ActualWidth);
        _expandedHeight = Math.Max(350, ActualHeight);
        _collapsed = true;
        ShowCompactPresentation(animate);
        SaveBounds();
    }

    void ShowCompactPresentation(bool animate)
    {
        ExpandedPanel.Visibility = Visibility.Collapsed;
        ResizeMode = ResizeMode.NoResize;
        if (_compactStyle == FloatingCompactStyle.Island) {
            OrbPanel.Visibility = Visibility.Collapsed;
            IslandPanel.Visibility = Visibility.Visible;
            MinWidth = 280; MinHeight = 64; Width = 340; Height = 78;
            if (animate) AnimateMaterialIn(IslandPanel, .96, 220);
        } else {
            IslandPanel.Visibility = Visibility.Collapsed;
            OrbPanel.Visibility = Visibility.Visible;
            MinWidth = MinHeight = 72; Width = Height = 82;
            if (animate) AnimateMaterialIn(OrbPanel, .9, 220);
        }
        Dispatcher.BeginInvoke(SnapToEdge, DispatcherPriority.Loaded);
    }

    void Expand()
    {
        if (!_collapsed) return;
        _collapsed = false;
        NewDot.Visibility = Visibility.Collapsed;
        IslandPulse.Visibility = Visibility.Collapsed;
        Width = Math.Max(300, _expandedWidth); Height = Math.Max(350, _expandedHeight);
        MinWidth = 300; MinHeight = 350; ResizeMode = ResizeMode.CanResizeWithGrip;
        OrbPanel.Visibility = Visibility.Collapsed;
        IslandPanel.Visibility = Visibility.Collapsed;
        ExpandedPanel.Visibility = Visibility.Visible;
        AnimateMaterialIn(ExpandedPanel, .94, 240);
        Dispatcher.BeginInvoke(SnapToEdge, DispatcherPriority.Loaded);
        SaveBounds();
    }

    void AnimateMaterialIn(FrameworkElement element, double fromScale, int milliseconds)
    {
        if (_services.Settings.Current.ReducedMotion || WindowsIntegration.IsReducedMotion()) {
            element.Opacity = 1;
            element.RenderTransform = Transform.Identity;
            return;
        }
        element.RenderTransformOrigin = new Point(.5, .5);
        var scale = new ScaleTransform(fromScale, fromScale);
        element.RenderTransform = scale;
        element.Opacity = 0;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        element.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(Math.Min(180, milliseconds))) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(fromScale, 1, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(fromScale, 1, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = ease });
    }

    void UpdatePresentationControls()
    {
        PresentationButton.ToolTip = _compactStyle == FloatingCompactStyle.Island
            ? "改用玻璃悬浮球"
            : "切换到灵动岛";
        CollapseButton.ToolTip = _compactStyle == FloatingCompactStyle.Island
            ? "收起为灵动岛"
            : "收起为悬浮球";
    }

    void IslandExpand_Click(object sender, RoutedEventArgs e) => Expand();
    void IslandBackToPanel_Click(object sender, RoutedEventArgs e)
    {
        _compactStyle = FloatingCompactStyle.Orb;
        _services.Settings.Current.FloatCompactStyle = _compactStyle;
        _services.Settings.Save();
        UpdatePresentationControls();
        Expand();
    }

    public void ApplySettings()
    {
        var s = _services.Settings.Current;
        Topmost = s.FloatAlwaysOnTop;
        Opacity = s.FloatOpacity;
        _compactStyle = s.FloatCompactStyle;
        UpdatePresentationControls();
        if (_collapsed) ShowCompactPresentation(false);
        WindowsIntegration.SetClickThrough(this, s.FloatClickThrough);
    }

    void Hide_Click(object sender, RoutedEventArgs e) => Hide();
    void Topmost_Click(object sender, RoutedEventArgs e) {
        Topmost = !Topmost; _services.Settings.Current.FloatAlwaysOnTop = Topmost; _services.Settings.Save();
    }
    void FloatSearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) { _searchTimer.Stop(); _searchTimer.Start(); }

    void FloatList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (FloatList.SelectedItem is not ClipboardItem item) return;
        PreviewText.Text = item.DisplayText;
        PreviewMeta.Text = $"{item.KindLabel} · {item.SourceApp}";
        if (!(_services.Settings.Current.ReducedMotion || WindowsIntegration.IsReducedMotion())) {
            PreviewText.BeginAnimation(OpacityProperty,
                new DoubleAnimation(.35, 1, TimeSpan.FromMilliseconds(180)) {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
            PreviewMeta.BeginAnimation(OpacityProperty,
                new DoubleAnimation(.35, 1, TimeSpan.FromMilliseconds(220)) {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
        }
    }
    async void FloatList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FloatList.SelectedItem is not ClipboardItem item) return;
        try {
            _services.Clipboard.CopyBack(item);
            await _services.Database.RecordReuseAsync(item.Id);
            PreviewMeta.Text = "已复制到系统剪贴板";
            await RefreshAsync();
        }
        catch (Exception ex) { PreviewMeta.Text = ex.Message; }
    }
    async void FloatFavorite_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is System.Windows.Controls.Button { Tag: ClipboardItem item }) await _mainVm.ToggleFavoriteAsync(item);
    }
    async void FloatPin_Click(object sender, RoutedEventArgs e)
    {
        if (FloatList.SelectedItem is ClipboardItem item) { await _mainVm.TogglePinnedAsync(item); await RefreshAsync(); }
    }
    async void FloatDelete_Click(object sender, RoutedEventArgs e)
    {
        if (FloatList.SelectedItem is ClipboardItem item) { await _services.Database.MoveToTrashAsync(item.Id); _items.Remove(item); }
    }

    public async void NotifyNewItem(ClipboardItem item)
    {
        await RefreshAsync();
        if (_collapsed) {
            if (_compactStyle == FloatingCompactStyle.Island) {
                IslandPulse.Visibility = Visibility.Visible;
                AnimateMaterialIn(IslandPanel, .985, 180);
            } else {
                NewDot.Visibility = Visibility.Visible;
                AnimateMaterialIn(OrbPanel, .94, 180);
            }
        }
        else if (!(_services.Settings.Current.ReducedMotion || WindowsIntegration.IsReducedMotion())) {
            ExpandedPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(.78, 1, TimeSpan.FromMilliseconds(240)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
    }
}
