using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LumaClip.Core;

/// <summary>
/// WPF's Border clips children to a rectangle even when CornerRadius is set.
/// This variant applies a real antialiased rounded geometry, preventing image
/// thumbnails from producing square or jagged pixels at card corners.
/// </summary>
public sealed class RoundedClipBorder : Border
{
    protected override Geometry GetLayoutClip(System.Windows.Size layoutSlotSize)
    {
        var radius = Math.Max(0, CornerRadius.TopLeft);
        if (!ClipToBounds || radius <= 0)
            return base.GetLayoutClip(layoutSlotSize);

        var rect = new Rect(0, 0, Math.Max(0, ActualWidth), Math.Max(0, ActualHeight));
        return new RectangleGeometry(rect, radius, radius);
    }
}
