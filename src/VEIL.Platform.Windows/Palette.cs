using System.Collections.Concurrent;
using VEIL.Domain.Theme;

namespace VEIL.Platform.Windows;

/// <summary>
/// GDI view of <see cref="VeilTheme"/>: COLORREFs and a small cache of solid brushes (a handful, kept for the life of
/// the process so painting never allocates per frame). Also creates DPI-scaled fonts from the theme's family and sizes.
/// </summary>
internal static class Palette
{
    public static VeilTheme Theme { get; } = VeilTheme.Dark;

    private static readonly ConcurrentDictionary<string, IntPtr> Brushes = new();

    public static uint Ref(string token) => Theme.Color(token).ToColorRef();

    public static IntPtr Brush(string token) =>
        Brushes.GetOrAdd(token, t => Native.CreateSolidBrush(Theme.Color(t).ToColorRef()));

    public static IntPtr Brush(ThemeColor color) =>
        Brushes.GetOrAdd(color.Hex, _ => Native.CreateSolidBrush(color.ToColorRef()));

    /// <summary>Creates a font; the caller owns it and must delete it. Height is negative pixels (character height).</summary>
    public static IntPtr Font(double points, double scale, int weight = 400)
    {
        var px = -(int)Math.Round(points * 96.0 / 72.0 * (scale > 0 ? scale : 1.0));
        var f = Native.CreateFont(px, 0, 0, 0, weight, 0, 0, 0, 1, 0, 0, 5, 0, Theme.FontFamily);
        return f != IntPtr.Zero ? f : Native.GetStockObject(Native.DEFAULT_GUI_FONT);
    }

    /// <summary>The theme's monospace face (Cascadia Mono, then Consolas): labels, keys and codes in the HUD style.</summary>
    public static IntPtr MonoFont(double points, double scale, int weight = 400)
    {
        var px = -(int)Math.Round(points * 96.0 / 72.0 * (scale > 0 ? scale : 1.0));
        var f = Native.CreateFont(px, 0, 0, 0, weight, 0, 0, 0, 1, 0, 0, 5, 0, Theme.MonoFamily);
        if (f == IntPtr.Zero || !FontInstalled(Theme.MonoFamily))
        {
            if (f != IntPtr.Zero) Native.DeleteObject(f);
            f = Native.CreateFont(px, 0, 0, 0, weight, 0, 0, 0, 1, 0, 0, 5, 0, "Consolas");
        }
        return f != IntPtr.Zero ? f : Native.GetStockObject(Native.DEFAULT_GUI_FONT);
    }

    private static readonly ConcurrentDictionary<string, bool> Installed = new();
    private static bool FontInstalled(string family) => Installed.GetOrAdd(family, f =>
    {
        try
        {
            foreach (var (root, path) in new[] { (Microsoft.Win32.Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts"),
                         (Microsoft.Win32.Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts") })
            {
                using var key = root.OpenSubKey(path);
                if (key is not null && key.GetValueNames().Any(n => n.StartsWith(f, StringComparison.OrdinalIgnoreCase))) return true;
            }
            return false;
        }
        catch { return true; }
    });

    public static int Px(double designPixels, double scale) => (int)Math.Round(designPixels * (scale > 0 ? scale : 1.0));
}
