using System.Collections.Specialized;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LumaClip.Models;

namespace LumaClip.Services;

public sealed class ClipboardMonitor : IDisposable
{
    const int WM_CLIPBOARDUPDATE = 0x031D;
    readonly SettingsService _settings;
    readonly SafeLogger _logger;
    HwndSource? _source;
    IntPtr _handle;
    DateTime _suppressUntil;
    int _captureBusy;

    public event EventHandler<ClipboardItem>? Captured;
    public bool IsRunning { get; private set; }

    public ClipboardMonitor(SettingsService settings, SafeLogger logger) { _settings = settings; _logger = logger; }

    public void Attach(Window window)
    {
        _handle = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(_handle);
        _source.AddHook(WndProc);
        if (AddClipboardFormatListener(_handle)) {
            IsRunning = true;
            _logger.Info("clipboard_monitor_started");
        } else _logger.Info("clipboard_monitor_start_failed", Marshal.GetLastWin32Error().ToString());
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_CLIPBOARDUPDATE && DateTime.UtcNow >= _suppressUntil && !_settings.Current.PauseCapture && !_settings.Current.PrivacyMode)
            _ = CaptureWithRetryAsync();
        return IntPtr.Zero;
    }

    async Task CaptureWithRetryAsync()
    {
        if (Interlocked.Exchange(ref _captureBusy, 1) == 1) return;
        try {
            for (var attempt = 0; attempt < 5; attempt++) {
                try {
                    var item = Capture();
                    if (item is not null && !IsExcluded(item.SourceProcess)) Captured?.Invoke(this, item);
                    return;
                } catch (COMException) { await Task.Delay(25 * (attempt + 1)); }
                  catch (ExternalException) { await Task.Delay(25 * (attempt + 1)); }
                  catch (Exception ex) { _logger.Error("clipboard_capture_failed", ex); return; }
            }
            _logger.Info("clipboard_capture_retry_exhausted");
        } finally { Interlocked.Exchange(ref _captureBusy, 0); }
    }

    ClipboardItem? Capture()
    {
        var (app, process) = GetClipboardSource();
        var now = DateTime.Now;
        if (System.Windows.Clipboard.ContainsFileDropList()) {
            var files = System.Windows.Clipboard.GetFileDropList().Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (files.Length == 0) return null;
            var text = string.Join(Environment.NewLine, files);
            var kind = files.Length > 1 ? ClipKind.MixedFiles : Directory.Exists(files[0]) ? ClipKind.Folder : ClipKind.File;
            return NewItem(kind, text, Hash("files:" + text.ToUpperInvariant()), app, process, now, _settings.Current.HideSensitive && IsSensitiveText(text));
        }
        if (System.Windows.Clipboard.ContainsImage()) {
            var image = System.Windows.Clipboard.GetImage();
            if (image is null) return null;
            image.Freeze();
            using var buffer = new MemoryStream();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            encoder.Save(buffer);
            var bytes = buffer.ToArray();
            if (bytes.Length > _settings.Current.MaxImageMegabytes * 1024L * 1024L) {
                _logger.Info("image_skipped_size", bytes.Length.ToString());
                return null;
            }
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            var originalPath = Path.Combine(_settings.ImagesPath, hash + ".png");
            var thumbnailPath = Path.Combine(_settings.ThumbnailsPath, hash + ".jpg");
            if (!File.Exists(originalPath)) File.WriteAllBytes(originalPath, bytes);
            if (!File.Exists(thumbnailPath)) SaveThumbnail(image, thumbnailPath);
            var item = NewItem(ClipKind.Image, $"{image.PixelWidth} × {image.PixelHeight} · {StorageStats.FormatBytes(bytes.Length)}", hash, app, process, now, false);
            item.ContentPath = originalPath;
            item.ThumbnailPath = thumbnailPath;
            return item;
        }
        if (System.Windows.Clipboard.ContainsText()) {
            var text = System.Windows.Clipboard.GetText();
            if (string.IsNullOrEmpty(text)) return null;
            var kind = DetectKind(text);
            return NewItem(kind, text, Hash("text:" + text), app, process, now, _settings.Current.HideSensitive && IsSensitiveText(text));
        }
        return null;
    }

    static ClipboardItem NewItem(ClipKind kind, string text, string hash, string app, string process, DateTime now, bool sensitive) => new() {
        Kind = kind, Text = text, Hash = hash, SourceApp = app, SourceProcess = process,
        CreatedAt = now, LastCopiedAt = now, IsSensitive = sensitive
    };

    static void SaveThumbnail(BitmapSource source, string path)
    {
        var max = 360d;
        var scale = Math.Min(1, max / Math.Max(source.PixelWidth, source.PixelHeight));
        var thumb = scale < 1 ? new TransformedBitmap(source, new ScaleTransform(scale, scale)) : source;
        var encoder = new JpegBitmapEncoder { QualityLevel = 82 };
        encoder.Frames.Add(BitmapFrame.Create(thumb));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    bool IsExcluded(string processName) => _settings.Current.ExcludedApps.Any(x =>
        processName.Contains(x.Trim(), StringComparison.OrdinalIgnoreCase));

    static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    static ClipKind DetectKind(string text)
    {
        var trimmed = text.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && (uri.Scheme is "http" or "https" or "mailto")) return ClipKind.Link;
        if (trimmed.Contains('\n') && (Regex.IsMatch(trimmed, @"[{};]\s*$", RegexOptions.Multiline) ||
            Regex.IsMatch(trimmed, @"^\s*(using|import|function|class|def|const|let|var|SELECT|CREATE)\b", RegexOptions.IgnoreCase | RegexOptions.Multiline)))
            return ClipKind.Code;
        return ClipKind.Text;
    }

    public static bool IsSensitiveText(string text)
    {
        var trimmed = text.Trim();
        if (Regex.IsMatch(text, @"(?i)\b(password|passwd|api[_-]?key|secret|token|private[_-]?key|access[_-]?key)\s*[:=]")) return true;
        if (Regex.IsMatch(text, @"(?i)\b(?:api[_-]?)?key\s*[:=]\s*[A-Za-z0-9_./+=-]{8,}")) return true;
        if (Regex.IsMatch(text, @"(?i)(?<![A-Za-z0-9])sk-[A-Za-z0-9_-]{16,}")) return true;
        if (Regex.IsMatch(trimmed, @"^\d{4,8}$")) return true;
        if (Regex.IsMatch(text, @"(?i)(验证码|校验码|动态码|verification|one[- ]time|otp).{0,16}\b\d{4,8}\b")) return true;
        if (text.Contains("-----BEGIN PRIVATE KEY-----", StringComparison.Ordinal)) return true;
        foreach (Match match in Regex.Matches(text, @"(?<!\d)(?:\d[ -]?){13,19}(?!\d)")) {
            var digits = match.Value.Where(char.IsDigit).Select(c => c - '0').ToArray();
            if (digits.Length is >= 13 and <= 19 && PassesLuhn(digits)) return true;
        }
        return false;
    }

    static bool PassesLuhn(int[] digits)
    {
        var sum = 0;
        var doubleDigit = false;
        for (var i = digits.Length - 1; i >= 0; i--) {
            var value = digits[i];
            if (doubleDigit && (value *= 2) > 9) value -= 9;
            sum += value;
            doubleDigit = !doubleDigit;
        }
        return sum % 10 == 0;
    }

    static (string App, string Process) GetClipboardSource()
    {
        try {
            var owner = GetClipboardOwner();
            if (owner == IntPtr.Zero) return ("系统", "");
            GetWindowThreadProcessId(owner, out var pid);
            using var process = Process.GetProcessById((int)pid);
            var name = process.ProcessName;
            var app = string.IsNullOrWhiteSpace(process.MainWindowTitle) ? name : process.MainWindowTitle;
            if (app.Length > 80) app = app[..80];
            return (app, name);
        } catch { return ("未知应用", ""); }
    }

    public void CopyBack(ClipboardItem item)
    {
        _suppressUntil = DateTime.UtcNow.AddMilliseconds(900);
        for (var attempt = 0; attempt < 5; attempt++) {
            try {
                if (item.Kind == ClipKind.Image && item.ContentPath is not null && File.Exists(item.ContentPath)) {
                    using var stream = File.OpenRead(item.ContentPath);
                    var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    var frame = decoder.Frames[0]; frame.Freeze();
                    System.Windows.Clipboard.SetImage(frame);
                } else if (item.Kind is ClipKind.File or ClipKind.Folder or ClipKind.MixedFiles) {
                    var paths = item.Text.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
                    var list = new StringCollection();
                    foreach (var path in paths.Where(p => File.Exists(p) || Directory.Exists(p))) list.Add(path);
                    if (list.Count > 0) System.Windows.Clipboard.SetFileDropList(list);
                    else System.Windows.Clipboard.SetText(item.Text);
                } else System.Windows.Clipboard.SetText(item.Text);
                return;
            } catch (ExternalException) { Thread.Sleep(25 * (attempt + 1)); }
        }
        throw new InvalidOperationException("系统剪贴板正被其他应用占用，请稍后重试。");
    }

    public void Dispose()
    {
        if (_handle != IntPtr.Zero) RemoveClipboardFormatListener(_handle);
        _source?.RemoveHook(WndProc);
        IsRunning = false;
    }

    [DllImport("user32.dll", SetLastError = true)] static extern bool AddClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)] static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll")] static extern IntPtr GetClipboardOwner();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
}
