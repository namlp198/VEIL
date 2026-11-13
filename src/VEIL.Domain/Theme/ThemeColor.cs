using System.Globalization;

namespace VEIL.Domain.Theme;

/// <summary>An opaque sRGB colour with the maths needed for accessibility checks (WCAG 2.x relative luminance).</summary>
public readonly record struct ThemeColor(byte R, byte G, byte B)
{
    public static bool TryParse(string? hex, out ThemeColor color)
    {
        color = default;
        if (hex is not { Length: 7 } || hex[0] != '#') return false;
        if (!byte.TryParse(hex.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)
            || !byte.TryParse(hex.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)
            || !byte.TryParse(hex.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b)) return false;
        color = new ThemeColor(r, g, b);
        return true;
    }

    public static ThemeColor Parse(string hex) =>
        TryParse(hex, out var c) ? c : throw new FormatException($"'{hex}' is not a #RRGGBB colour.");

    public string Hex => string.Create(CultureInfo.InvariantCulture, $"#{R:X2}{G:X2}{B:X2}");

    /// <summary>Win32 COLORREF (0x00BBGGRR) for GDI calls.</summary>
    public uint ToColorRef() => (uint)(R | (G << 8) | (B << 16));

    public double RelativeLuminance
    {
        get
        {
            static double Lin(byte v)
            {
                var c = v / 255.0;
                return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Lin(R) + 0.7152 * Lin(G) + 0.0722 * Lin(B);
        }
    }

    public double ContrastWith(ThemeColor other)
    {
        var a = RelativeLuminance;
        var b = other.RelativeLuminance;
        if (a < b) (a, b) = (b, a);
        return (a + 0.05) / (b + 0.05);
    }

    /// <summary>Linear blend towards <paramref name="other"/>; 0 returns this, 1 returns other.</summary>
    public ThemeColor Mix(ThemeColor other, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        byte M(byte x, byte y) => (byte)Math.Round(x + (y - x) * amount);
        return new ThemeColor(M(R, other.R), M(G, other.G), M(B, other.B));
    }

    public override string ToString() => Hex;
}
