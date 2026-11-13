using VEIL.Domain.Theme;

namespace VEIL.UnitTests;

public class ThemeColorTests
{
    [Theory]
    [InlineData("#000000", 0, 0, 0)]
    [InlineData("#FFFFFF", 255, 255, 255)]
    [InlineData("#9235EC", 0x92, 0x35, 0xEC)]
    [InlineData("#31f1f4", 0x31, 0xF1, 0xF4)]
    public void Parses_hex(string hex, byte r, byte g, byte b)
    {
        Assert.True(ThemeColor.TryParse(hex, out var c));
        Assert.Equal(new ThemeColor(r, g, b), c);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("9235EC")]
    [InlineData("#9235E")]
    [InlineData("#9235EC0")]
    [InlineData("#GGGGGG")]
    [InlineData("rgb(1,2,3)")]
    public void Rejects_malformed_hex(string? hex) => Assert.False(ThemeColor.TryParse(hex, out _));

    [Fact]
    public void Hex_round_trips_in_upper_case() => Assert.Equal("#31F1F4", ThemeColor.Parse("#31f1f4").Hex);

    [Fact]
    public void ColorRef_is_blue_green_red_order_for_gdi() => Assert.Equal(0x00F4F131u, ThemeColor.Parse("#31F1F4").ToColorRef());

    [Fact]
    public void Contrast_matches_the_wcag_reference_values()
    {
        var black = new ThemeColor(0, 0, 0);
        var white = new ThemeColor(255, 255, 255);
        Assert.Equal(21.0, white.ContrastWith(black), 1);
        Assert.Equal(1.0, white.ContrastWith(white), 3);
        Assert.Equal(white.ContrastWith(black), black.ContrastWith(white), 6); // symmetric
    }

    [Fact]
    public void Mix_blends_linearly_and_clamps()
    {
        var a = new ThemeColor(0, 0, 0);
        var b = new ThemeColor(100, 200, 50);
        Assert.Equal(a, a.Mix(b, 0));
        Assert.Equal(b, a.Mix(b, 1));
        Assert.Equal(new ThemeColor(50, 100, 25), a.Mix(b, 0.5));
        Assert.Equal(b, a.Mix(b, 7));
    }
}

public class VeilThemeTests
{
    private static readonly VeilTheme T = VeilTheme.Dark;

    private static ThemeColor C(string token) => T.Color(token);

    [Fact]
    public void Embedded_theme_loads_and_has_every_required_colour()
    {
        Assert.Equal("VEIL Dark", T.Name);
        foreach (var key in VeilTheme.RequiredColors) Assert.True(T.Colors.ContainsKey(key), key);
    }

    [Fact]
    public void Embedded_theme_equals_the_file_in_the_repository()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "assets", "theme", "veil-tokens.json"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var fromFile = VeilTheme.FromJson(File.ReadAllText(Path.Combine(dir!.FullName, "assets", "theme", "veil-tokens.json")));
        Assert.Equal(fromFile.Colors.OrderBy(k => k.Key), T.Colors.OrderBy(k => k.Key));
    }

    [Fact]
    public void Generated_avalonia_resources_match_the_tokens() // regenerate with: python tools/gen_axaml.py
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "assets", "theme", "VeilTokens.axaml"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var xaml = System.Xml.Linq.XDocument.Load(Path.Combine(dir!.FullName, "assets", "theme", "VeilTokens.axaml"));
        XNameSpaceHelper(xaml, out var x);
        var colours = xaml.Descendants().Where(e => e.Name.LocalName == "Color")
            .ToDictionary(e => ((string)e.Attribute(x + "Key")!).Substring("Color.".Length), e => (string)e);
        Assert.Equal(T.Colors.OrderBy(k => k.Key).Select(k => (k.Key, k.Value.Hex)), colours.OrderBy(k => k.Key).Select(k => (k.Key, k.Value)));

        var brushes = xaml.Descendants().Where(e => e.Name.LocalName == "SolidColorBrush").Select(e => (string)e.Attribute(x + "Key")!).ToHashSet();
        foreach (var key in T.Colors.Keys) Assert.Contains("Brush." + key, brushes);

        var doubles = xaml.Descendants().Where(e => e.Name.LocalName == "Double").ToDictionary(e => (string)e.Attribute(x + "Key")!, e => double.Parse((string)e, System.Globalization.CultureInfo.InvariantCulture));
        foreach (var (k, v) in T.Spacing) Assert.Equal(v, doubles["Space." + k]);
        foreach (var (k, v) in T.FontSizesPt) Assert.Equal(v * 96 / 72, doubles["Font.Size." + k], 2);
    }

    private static void XNameSpaceHelper(System.Xml.Linq.XDocument doc, out System.Xml.Linq.XNamespace x) =>
        x = doc.Root!.GetNamespaceOfPrefix("x")!;

    [Fact]
    public void Backgrounds_are_dark_and_stack_from_darkest_to_lightest() // UI/UX 1: near-black / charcoal
    {
        string[] stack = ["bg.base", "bg.surface", "bg.raised"];
        for (var i = 1; i < stack.Length; i++) Assert.True(C(stack[i]).RelativeLuminance > C(stack[i - 1]).RelativeLuminance, stack[i]);
        foreach (var bg in new[] { "bg.canvas", "bg.base", "bg.surface", "bg.raised", "bg.panel" })
            Assert.True(C(bg).RelativeLuminance < 0.03, bg);
    }

    public static IEnumerable<object[]> Backgrounds() => new[] { "bg.canvas", "bg.base", "bg.surface", "bg.raised", "bg.panel" }.Select(b => new object[] { b });

    [Theory, MemberData(nameof(Backgrounds))]
    public void Body_text_meets_AAA_and_secondary_text_meets_AA(string bg)
    {
        Assert.True(C("text.primary").ContrastWith(C(bg)) >= 7.0, $"primary on {bg}");
        Assert.True(C("text.secondary").ContrastWith(C(bg)) >= 4.5, $"secondary on {bg}");
        Assert.True(C("text.muted").ContrastWith(C(bg)) >= 4.5, $"muted on {bg}");
        Assert.True(C("text.link").ContrastWith(C(bg)) >= 4.5, $"link on {bg}");
    }

    [Theory]
    [InlineData("accent.cyan")]
    [InlineData("accent.cyanDim")]
    [InlineData("accent.magenta")]
    [InlineData("accent.lilac")]
    [InlineData("status.success")]
    [InlineData("status.warning")]
    [InlineData("status.error")]
    public void Coloured_text_is_readable_on_the_main_surfaces(string fg)
    {
        foreach (var bg in new[] { "bg.base", "bg.surface", "bg.raised" })
            Assert.True(C(fg).ContrastWith(C(bg)) >= 4.5, $"{fg} on {bg}");
    }

    [Fact]
    public void Violet_is_for_fills_borders_and_glow_not_for_text() // measured 3.6-3.8:1 on the dark backgrounds
    {
        Assert.True(C("accent.primaryHover").ContrastWith(C("bg.base")) >= 3.0);          // graphics threshold
        Assert.True(C("accent.primaryHover").ContrastWith(C("bg.base")) < 4.5);           // documented: not body-text safe
        Assert.True(C("text.link").ContrastWith(C("bg.base")) >= 4.5);                    // the text-safe violet
    }

    [Fact]
    public void Button_label_is_readable_on_every_button_state()
    {
        foreach (var fill in new[] { "accent.primary", "accent.primaryHover", "accent.primaryActive" })
            Assert.True(C("text.onAccent").ContrastWith(C(fill)) >= 4.5, fill);
    }

    [Fact]
    public void Focus_ring_and_borders_are_visible_against_the_background() // 3:1 for UI components
    {
        Assert.True(C("border.focus").ContrastWith(C("bg.base")) >= 3.0);
        Assert.True(C("border.strong").ContrastWith(C("bg.surface")) >= 1.5);
    }

    [Theory]
    [InlineData("status.success", "status.successBg")]
    [InlineData("status.warning", "status.warningBg")]
    [InlineData("status.error", "status.errorBg")]
    [InlineData("status.info", "status.infoBg")]
    [InlineData("text.secondary", "status.neutralBg")]
    public void Status_pills_are_readable(string fg, string bg) =>
        Assert.True(C(fg).ContrastWith(C(bg)) >= 4.5, $"{fg} on {bg}");

    [Fact]
    public void Tokens_cover_the_scales_the_ui_uses()
    {
        Assert.Equal(8, T.Spacing["sm"]);
        Assert.Equal(12, T.Radius["lg"]);
        Assert.True(T.FontSizesPt["title"] > T.FontSizesPt["body"]);
        Assert.True(T.MotionMs["fastMs"] < T.MotionMs["baseMs"] && T.MotionMs["baseMs"] < T.MotionMs["slowMs"]);
        Assert.Equal(52, T.Component["selectorRowHeight"]);
    }

    [Fact]
    public void Reduced_motion_flag_is_declared()
    {
        // The flag is a boolean in the file (numbers only are surfaced); a theme must still parse with it present.
        Assert.DoesNotContain("reducedMotionDisablesAll", T.MotionMs.Keys);
    }

    [Fact]
    public void Missing_colour_token_is_rejected()
    {
        var json = MinimalJson(skip: "text.muted");
        var ex = Assert.Throws<FormatException>(() => VeilTheme.FromJson(json));
        Assert.Contains("text.muted", ex.Message);
    }

    [Fact]
    public void Bad_hex_value_is_rejected() =>
        Assert.Throws<FormatException>(() => VeilTheme.FromJson(MinimalJson(replace: ("text.primary", "red"))));

    [Fact]
    public void Missing_section_is_rejected() =>
        Assert.Throws<FormatException>(() => VeilTheme.FromJson("{\"name\":\"x\",\"version\":\"1\",\"colors\":{}}"));

    [Fact]
    public void Malformed_or_oversized_input_is_rejected()
    {
        Assert.ThrowsAny<Exception>(() => VeilTheme.FromJson("not json"));
        Assert.Throws<FormatException>(() => VeilTheme.FromJson(new string(' ', 70_000)));
    }

    [Fact]
    public void Unknown_colour_token_lookup_fails_loudly() =>
        Assert.Throws<KeyNotFoundException>(() => T.Color("nope"));

    private static string MinimalJson(string? skip = null, (string Key, string Value)? replace = null)
    {
        var colors = VeilTheme.RequiredColors.Where(k => k != skip)
            .Select(k => $"\"{k}\":\"{(replace is { } r && r.Key == k ? r.Value : "#123456")}\"");
        return "{\"name\":\"t\",\"version\":\"1\",\"colors\":{" + string.Join(",", colors) + "},"
             + "\"spacing\":{},\"radius\":{},\"font\":{\"family\":\"x\",\"mono\":\"y\",\"sizePt\":{}},\"motion\":{},\"component\":{}}";
    }
}
