using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using LumaClip.Models;

namespace LumaClip.Core;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var visible = value is true;
        if (Invert) visible = !visible;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class ImagePathConverter : IMultiValueConverter
{
    // Stream loading keeps previews reliable for non-ASCII paths and releases the
    // image file immediately. Original image is preferred; thumbnail is fallback.
    public object? Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var decodeWidth = parameter is string s && int.TryParse(s, out var width) ? width : 1600;
        foreach (var path in values.OfType<string>().Where(File.Exists)) {
            try {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                var frame = decoder.Frames.FirstOrDefault();
                if (frame is null) continue;
                BitmapSource source = frame;
                if (frame.PixelWidth > decodeWidth) {
                    var scale = (double)decodeWidth / frame.PixelWidth;
                    source = new TransformedBitmap(frame, new System.Windows.Media.ScaleTransform(scale, scale));
                }
                source.Freeze();
                return source;
            } catch { /* Keep the inspector usable when one stored file is damaged. */ }
        }
        return null;
    }
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => targetTypes.Select(_ => Binding.DoNothing).ToArray();
}

public sealed class KindEqualsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var text = parameter?.ToString() ?? "";
        var invert = text.StartsWith('!');
        if (invert) text = text[1..];
        var matches = value is ClipKind kind && Enum.TryParse<ClipKind>(text, out var expected) && kind == expected;
        if (invert) matches = !matches;
        return matches ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var visible = value is not null;
        if (Invert) visible = !visible;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class ShelfItemWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double width || double.IsNaN(width)) return 300d;
        // Leave room for both item margins and the vertical scrollbar so two
        // tiles do not round onto separate rows at common Windows DPI scales.
        return Math.Max(260d, (width - 44d) / 2d);
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class EmptyStringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.IsNullOrEmpty(value as string) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
