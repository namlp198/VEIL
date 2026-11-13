using System.Text.Json;

namespace VEIL.Domain.Theme;

/// <summary>
/// Design tokens (UI/UX 6). The single source of truth is <c>assets/theme/veil-tokens.json</c>, embedded in this assembly
/// and parsed with strict validation; the Avalonia resource dictionary and the Windows overlay/selector read from here.
/// </summary>
public sealed class VeilTheme
{
    /// <summary>Colour tokens that every consumer may rely on. A theme file missing any of them is rejected.</summary>
    public static readonly IReadOnlyList<string> RequiredColors =
    [
        "bg.canvas", "bg.base", "bg.surface", "bg.raised", "bg.panel",
        "border.subtle", "border.strong", "border.focus",
        "text.primary", "text.secondary", "text.muted", "text.link", "text.onAccent",
        "accent.primary", "accent.primaryHover", "accent.primaryActive", "accent.glow", "accent.lilac",
        "accent.cyan", "accent.cyanDim", "accent.magenta", "accent.blue",
        "status.success", "status.successBg", "status.warning", "status.warningBg",
        "status.error", "status.errorBg", "status.info", "status.infoBg", "status.neutralBg",
    ];

    private readonly Dictionary<string, ThemeColor> _colors;

    private VeilTheme(string name, string version, Dictionary<string, ThemeColor> colors,
        IReadOnlyDictionary<string, double> spacing, IReadOnlyDictionary<string, double> radius,
        IReadOnlyDictionary<string, double> fontSizesPt, string fontFamily, string monoFamily,
        IReadOnlyDictionary<string, double> motionMs, IReadOnlyDictionary<string, double> component)
    {
        Name = name; Version = version; _colors = colors; Spacing = spacing; Radius = radius;
        FontSizesPt = fontSizesPt; FontFamily = fontFamily; MonoFamily = monoFamily; MotionMs = motionMs; Component = component;
    }

    public string Name { get; }
    public string Version { get; }
    public IReadOnlyDictionary<string, ThemeColor> Colors => _colors;
    public IReadOnlyDictionary<string, double> Spacing { get; }
    public IReadOnlyDictionary<string, double> Radius { get; }
    public IReadOnlyDictionary<string, double> FontSizesPt { get; }
    public string FontFamily { get; }
    public string MonoFamily { get; }
    public IReadOnlyDictionary<string, double> MotionMs { get; }
    public IReadOnlyDictionary<string, double> Component { get; }

    public ThemeColor Color(string token) =>
        _colors.TryGetValue(token, out var c) ? c : throw new KeyNotFoundException($"Unknown colour token '{token}'.");

    private static readonly Lazy<VeilTheme> DarkTheme = new(LoadEmbedded);

    /// <summary>The built-in VEIL Dark theme.</summary>
    public static VeilTheme Dark => DarkTheme.Value;

    private static VeilTheme LoadEmbedded()
    {
        var asm = typeof(VeilTheme).Assembly;
        using var stream = asm.GetManifestResourceStream("veil-tokens.json")
            ?? throw new InvalidOperationException("Embedded theme tokens are missing.");
        using var reader = new StreamReader(stream);
        return FromJson(reader.ReadToEnd());
    }

    public static VeilTheme FromJson(string json)
    {
        if (json.Length > 64 * 1024) throw new FormatException("Theme file is too large.");
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Disallow });
        var root = doc.RootElement;

        var colors = new Dictionary<string, ThemeColor>(StringComparer.Ordinal);
        foreach (var p in Section(root, "colors").EnumerateObject())
        {
            if (!ThemeColor.TryParse(p.Value.GetString(), out var c))
                throw new FormatException($"Colour token '{p.Name}' is not a #RRGGBB value.");
            colors[p.Name] = c;
        }
        var missing = RequiredColors.Where(k => !colors.ContainsKey(k)).ToList();
        if (missing.Count > 0) throw new FormatException("Theme is missing colour tokens: " + string.Join(", ", missing));

        var font = Section(root, "font");
        return new VeilTheme(
            root.GetProperty("name").GetString() ?? "unnamed",
            root.GetProperty("version").GetString() ?? "0",
            colors,
            Numbers(Section(root, "spacing")),
            Numbers(Section(root, "radius")),
            Numbers(font.GetProperty("sizePt")),
            font.GetProperty("family").GetString() ?? "Segoe UI",
            font.GetProperty("mono").GetString() ?? "Consolas",
            Numbers(Section(root, "motion")),
            Numbers(Section(root, "component")));
    }

    private static JsonElement Section(JsonElement root, string name) =>
        root.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.Object ? e : throw new FormatException($"Theme section '{name}' is missing.");

    /// <summary>Keeps numeric members only (arrays, strings and booleans in a section are ignored here).</summary>
    private static Dictionary<string, double> Numbers(JsonElement section) =>
        section.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.Number).ToDictionary(p => p.Name, p => p.Value.GetDouble());
}
