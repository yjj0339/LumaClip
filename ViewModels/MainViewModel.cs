using System.Collections.ObjectModel;
using System.Windows.Threading;
using LumaClip.Core;
using LumaClip.Models;
using LumaClip.Services;

namespace LumaClip.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    readonly AppServices _services;
    readonly DispatcherTimer _searchTimer;
    string _searchText = "", _filter = "all", _status = "正在监听剪贴板", _title = "全部记录";
    ClipboardItem? _selected;
    bool _recycleBin, _loading;

    public ObservableCollection<ClipboardItem> Items { get; } = [];
    public ClipboardItem? SelectedItem { get => _selected; set => Set(ref _selected, value); }
    public string SearchText {
        get => _searchText;
        set { if (Set(ref _searchText, value)) { _searchTimer.Stop(); _searchTimer.Start(); } }
    }
    public string Filter { get => _filter; private set => Set(ref _filter, value); }
    public string Status { get => _status; set => Set(ref _status, value); }
    public string Title { get => _title; set => Set(ref _title, value); }
    public bool IsRecycleBin { get => _recycleBin; set => Set(ref _recycleBin, value); }
    public bool IsLoading { get => _loading; set => Set(ref _loading, value); }
    public int ItemCount => Items.Count;

    public MainViewModel(AppServices services)
    {
        _services = services;
        _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        _searchTimer.Tick += async (_, _) => { _searchTimer.Stop(); await RefreshAsync(); };
    }

    public async Task RefreshAsync(bool keepSelection = true)
    {
        if (IsLoading) return;
        IsLoading = true;
        var selectedId = keepSelection ? SelectedItem?.Id : null;
        try {
            var rows = await _services.Database.QueryAsync(SearchText, Filter, IsRecycleBin);
            Items.Clear();
            foreach (var row in rows) {
                row.IsSensitive = _services.Settings.Current.HideSensitive && ClipboardMonitor.IsSensitiveText(row.Text);
                Items.Add(row);
            }
            SelectedItem = selectedId.HasValue ? Items.FirstOrDefault(x => x.Id == selectedId) : Items.FirstOrDefault();
            Status = _services.Settings.Current.PrivacyMode ? "隐私模式 · 不记录新内容" :
                     _services.Settings.Current.PauseCapture ? "监听已暂停" : $"正在监听 · {Items.Count} 条可见记录";
            Raise(nameof(ItemCount));
        } finally { IsLoading = false; }
    }

    public async Task SelectFilterAsync(string filter, string title, bool recycle = false)
    {
        Filter = filter; Title = title; IsRecycleBin = recycle; SearchText = "";
        await RefreshAsync(false);
    }

    public async Task AddCapturedAsync(ClipboardItem item)
    {
        item.IsSensitive = _services.Settings.Current.HideSensitive && ClipboardMonitor.IsSensitiveText(item.Text);
        var saved = await _services.Database.UpsertAsync(item);
        await _services.Database.TrimAsync(_services.Settings.Current.MaxHistoryItems, _services.Settings.Current.RetentionDays);
        if (!IsRecycleBin && (Filter == "all" || Matches(Filter, saved))) {
            var existing = Items.FirstOrDefault(x => x.Id == saved.Id);
            if (existing is not null) Items.Remove(existing);
            Items.Insert(0, saved);
            SelectedItem ??= saved;
            while (Items.Count > 500) Items.RemoveAt(Items.Count - 1);
            Raise(nameof(ItemCount));
        }
    }

    static bool Matches(string filter, ClipboardItem item) => filter switch {
        "text" => item.Kind is ClipKind.Text or ClipKind.Link or ClipKind.Code,
        "image" => item.Kind == ClipKind.Image,
        "link" => item.Kind == ClipKind.Link,
        "files" => item.Kind is ClipKind.File or ClipKind.Folder or ClipKind.MixedFiles,
        "favorite" => item.IsFavorite, "pinned" => item.IsPinned, _ => true
    };

    public async Task ToggleFavoriteAsync(ClipboardItem item) {
        item.IsFavorite = !item.IsFavorite;
        await _services.Database.SetFavoriteAsync(item.Id, item.IsFavorite);
        if (Filter == "favorite" && !item.IsFavorite) Items.Remove(item);
    }
    public async Task TogglePinnedAsync(ClipboardItem item) {
        item.IsPinned = !item.IsPinned;
        await _services.Database.SetPinnedAsync(item.Id, item.IsPinned);
        await RefreshAsync();
    }
    public async Task DeleteOrRestoreAsync(ClipboardItem item) {
        if (IsRecycleBin) await _services.Database.RestoreAsync(item.Id);
        else await _services.Database.MoveToTrashAsync(item.Id);
        Items.Remove(item); SelectedItem = Items.FirstOrDefault(); Raise(nameof(ItemCount));
    }
    public async Task SaveMetadataAsync(ClipboardItem item) => await _services.Database.UpdateMetadataAsync(item.Id, item.Tags, item.Note);
}
