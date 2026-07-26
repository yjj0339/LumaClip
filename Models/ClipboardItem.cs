using LumaClip.Core;

namespace LumaClip.Models;

public enum ClipKind { Text, Link, Code, Image, File, Folder, MixedFiles }

public sealed class ClipboardItem : ObservableObject
{
    long _id;
    bool _isFavorite, _isPinned, _isSensitive;
    string _tags = "", _note = "";
    public long Id { get => _id; set => Set(ref _id, value); }
    public ClipKind Kind { get; set; }
    public string Text { get; set; } = "";
    public string? ContentPath { get; set; }
    public string? ThumbnailPath { get; set; }
    public string Hash { get; set; } = "";
    public string SourceApp { get; set; } = "";
    public string SourceProcess { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime LastCopiedAt { get; set; }
    public int CopyCount { get; set; } = 1;
    public bool IsFavorite { get => _isFavorite; set { if (Set(ref _isFavorite, value)) Raise(nameof(FavoriteGlyph)); } }
    public bool IsPinned { get => _isPinned; set { if (Set(ref _isPinned, value)) Raise(nameof(PinnedGlyph)); } }
    public bool IsSensitive { get => _isSensitive; set { if (Set(ref _isSensitive, value)) Raise(nameof(DisplayText)); } }
    public string Tags { get => _tags; set => Set(ref _tags, value); }
    public string Note { get => _note; set => Set(ref _note, value); }
    public DateTime? DeletedAt { get; set; }

    public string KindLabel => Kind switch {
        ClipKind.Text => "文本", ClipKind.Link => "链接", ClipKind.Code => "代码",
        ClipKind.Image => "图片", ClipKind.File => "文件", ClipKind.Folder => "文件夹", _ => "多个文件"
    };
    public string KindGlyph => Kind switch {
        ClipKind.Text => "\uE8D2", ClipKind.Link => "\uE71B", ClipKind.Code => "\uE943",
        ClipKind.Image => "\uEB9F", ClipKind.File => "\uE7C3", ClipKind.Folder => "\uE8B7", _ => "\uE8B7"
    };
    public string DisplayText => IsSensitive ? "••••••  敏感内容已隐藏" :
        Kind == ClipKind.Image ? "高清图片" :
        string.IsNullOrWhiteSpace(Text) ? KindLabel : Text.Replace("\r", " ").Replace("\n", " ");
    public string FavoriteGlyph => IsFavorite ? "\uE735" : "\uE734";
    public string PinnedGlyph => IsPinned ? "\uE840" : "\uE718";
    public string TimeLabel {
        get {
            var span = DateTime.Now - LastCopiedAt;
            if (span.TotalMinutes < 1) return "刚刚";
            if (span.TotalHours < 1) return $"{(int)span.TotalMinutes} 分钟前";
            if (span.TotalDays < 1) return $"{(int)span.TotalHours} 小时前";
            if (span.TotalDays < 7) return $"{(int)span.TotalDays} 天前";
            return LastCopiedAt.ToString("yyyy-MM-dd");
        }
    }
}
