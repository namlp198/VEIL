using VEIL.Domain;
using VEIL.Platform.Abstractions;

namespace VEIL.Application;

/// <summary>
/// Chooses the display and the physical-pixel rectangle for the selector (UI/UX 4: primary display by default,
/// optionally the display containing the pointer). The design size is in device-independent pixels (96 dpi) and is
/// scaled by the target display's scale factor, so it is the same physical size on a 4K 200% display as on a 1080p 100% one.
/// </summary>
public static class SelectorPlacementCalculator
{
    public const int DesignWidth = 760;
    public const int DesignHeight = 480;
    private const double MaxFractionOfDisplay = 0.9;

    public static SelectorPlacement Compute(
        IReadOnlyList<DisplayInfo> displays,
        (int X, int Y)? pointer,
        bool onPointerDisplay,
        double? scaleOverride = null)
    {
        if (displays.Count == 0) throw new InvalidOperationException("No displays are available for the selector.");

        var display = PickDisplay(displays, pointer, onPointerDisplay);

        var scale = scaleOverride is > 0 ? scaleOverride.Value : (display.ScaleFactor > 0 ? display.ScaleFactor : 1.0);
        var b = display.Bounds;
        var width = (int)Math.Round(DesignWidth * scale);
        var height = (int)Math.Round(DesignHeight * scale);

        // Never larger than the display (small or portrait screens, very high scale factors).
        width = Math.Min(width, (int)(b.Width * MaxFractionOfDisplay));
        height = Math.Min(height, (int)(b.Height * MaxFractionOfDisplay));

        var x = b.X + (b.Width - width) / 2;
        var y = b.Y + (b.Height - height) / 3;
        return new SelectorPlacement(new WindowBounds(x, y, width, height), scale, display.Id);
    }

    /// <summary>The display under the pointer when asked (and when it is on one), otherwise the primary display.</summary>
    public static DisplayInfo PickDisplay(IReadOnlyList<DisplayInfo> displays, (int X, int Y)? pointer, bool onPointerDisplay)
    {
        if (displays.Count == 0) throw new InvalidOperationException("No displays are available.");
        var display = onPointerDisplay && pointer is { } p ? displays.FirstOrDefault(d => Contains(d.Bounds, p.X, p.Y)) : null;
        return display ?? displays.FirstOrDefault(d => d.IsPrimary) ?? displays[0];
    }

    /// <summary>
    /// Centres a window of a design size (device-independent pixels) on <paramref name="display"/>, scaled by that display's
    /// scale factor and never larger than <paramref name="maxFraction"/> of it. Returns the size in DIPs (what the UI
    /// toolkit wants) and the top-left corner in physical pixels.
    /// </summary>
    public static (double WidthDip, double HeightDip, int X, int Y) CenterWindow(DisplayInfo display, double designWidthDip, double designHeightDip, double maxFraction = 0.9)
    {
        var scale = display.ScaleFactor > 0 ? display.ScaleFactor : 1.0;
        var b = display.Bounds;
        var wPx = Math.Min(designWidthDip * scale, b.Width * maxFraction);
        var hPx = Math.Min(designHeightDip * scale, b.Height * maxFraction);
        return (wPx / scale, hPx / scale, b.X + (int)Math.Round((b.Width - wPx) / 2), b.Y + (int)Math.Round((b.Height - hPx) / 2));
    }

    private static bool Contains(WindowBounds b, int x, int y) =>
        x >= b.X && x < b.X + b.Width && y >= b.Y && y < b.Y + b.Height;
}
