using VEIL.Application;
using VEIL.Domain;

namespace VEIL.UnitTests;

public class SelectorPlacementTests
{
    private static DisplayInfo D(string id, int x, int y, int w, int h, double scale = 1.0, bool primary = false) =>
        new(id, id, new WindowBounds(x, y, w, h), scale, primary);

    private static void AssertInside(WindowBounds inner, WindowBounds outer)
    {
        Assert.True(inner.X >= outer.X && inner.Y >= outer.Y, $"{inner} starts outside {outer}");
        Assert.True(inner.X + inner.Width <= outer.X + outer.Width && inner.Y + inner.Height <= outer.Y + outer.Height, $"{inner} ends outside {outer}");
    }

    [Fact]
    public void Full_hd_at_100_percent_is_the_design_size_centered_in_the_upper_third()
    {
        var p = SelectorPlacementCalculator.Compute([D("A", 0, 0, 1920, 1080, 1.0, true)], null, false);
        Assert.Equal(new WindowBounds(580, 200, 760, 480), p.Bounds); // identical to the live 100% run
        Assert.Equal(1.0, p.Scale);
    }

    [Theory]
    [InlineData(1.25, 3840, 2160)]
    [InlineData(1.5, 2560, 1440)]
    [InlineData(2.0, 3840, 2160)]
    [InlineData(2.5, 3840, 2160)]
    public void Size_scales_with_the_display_and_stays_centered(double scale, int w, int h)
    {
        var display = D("A", 0, 0, w, h, scale, true);
        var p = SelectorPlacementCalculator.Compute([display], null, false);
        Assert.Equal((int)Math.Round(760 * scale), p.Bounds.Width);
        Assert.Equal((int)Math.Round(480 * scale), p.Bounds.Height);
        Assert.Equal((w - p.Bounds.Width) / 2, p.Bounds.X);
        AssertInside(p.Bounds, display.Bounds);
    }

    [Fact]
    public void Same_physical_size_on_4k_200_percent_as_on_full_hd_100_percent() // 1520 px on a 3840 px screen == 760 px on 1920
    {
        var hd = SelectorPlacementCalculator.Compute([D("A", 0, 0, 1920, 1080, 1.0, true)], null, false);
        var uhd = SelectorPlacementCalculator.Compute([D("B", 0, 0, 3840, 2160, 2.0, true)], null, false);
        Assert.Equal((double)hd.Bounds.Width / 1920, (double)uhd.Bounds.Width / 3840, 3);
        Assert.Equal((double)hd.Bounds.Height / 1080, (double)uhd.Bounds.Height / 2160, 3);
    }

    [Fact]
    public void An_excessive_scale_is_clamped_to_90_percent_of_the_display_but_keeps_the_font_scale()
    {
        var display = D("A", 0, 0, 1920, 1080, 3.0, true);
        var p = SelectorPlacementCalculator.Compute([display], null, false);
        Assert.Equal(1728, p.Bounds.Width);
        Assert.Equal(972, p.Bounds.Height);
        Assert.Equal(3.0, p.Scale);
        AssertInside(p.Bounds, display.Bounds);
    }

    [Fact]
    public void Small_and_portrait_displays_are_respected()
    {
        var small = D("S", 0, 0, 800, 480, 1.0, true);
        AssertInside(SelectorPlacementCalculator.Compute([small], null, false).Bounds, small.Bounds);

        var portrait = D("P", 0, 0, 1080, 1920, 1.0, true);
        var p = SelectorPlacementCalculator.Compute([portrait], null, false);
        Assert.Equal(new WindowBounds(160, 480, 760, 480), p.Bounds);
    }

    [Fact]
    public void Primary_display_is_used_by_default_even_when_the_pointer_is_elsewhere()
    {
        var displays = new[] { D("Left", 0, 0, 1920, 1080, 1.0, true), D("Right", 1920, 0, 1920, 1080) };
        var p = SelectorPlacementCalculator.Compute(displays, (2500, 400), onPointerDisplay: false);
        Assert.Equal("Left", p.DisplayId);
    }

    [Fact]
    public void Pointer_display_is_used_when_configured()
    {
        var displays = new[] { D("Primary", 0, 0, 1920, 1080, 1.0, true), D("Right", 1920, 0, 1920, 1080) };
        var p = SelectorPlacementCalculator.Compute(displays, (2500, 400), onPointerDisplay: true);
        Assert.Equal("Right", p.DisplayId);
        Assert.Equal(1920 + 580, p.Bounds.X);
        AssertInside(p.Bounds, displays[1].Bounds);
    }

    [Fact]
    public void A_monitor_left_of_or_above_the_primary_has_negative_coordinates()
    {
        var displays = new[] { D("Primary", 0, 0, 1920, 1080, 1.0, true), D("Left", -1920, 0, 1920, 1080), D("Above", 0, -1080, 1920, 1080) };
        var left = SelectorPlacementCalculator.Compute(displays, (-500, 300), true);
        Assert.Equal("Left", left.DisplayId);
        Assert.Equal(-1920 + 580, left.Bounds.X);
        AssertInside(left.Bounds, displays[1].Bounds);

        var above = SelectorPlacementCalculator.Compute(displays, (100, -200), true);
        Assert.Equal("Above", above.DisplayId);
        Assert.Equal(-1080 + 200, above.Bounds.Y);
        AssertInside(above.Bounds, displays[2].Bounds);
    }

    [Fact]
    public void Mixed_dpi_uses_the_scale_of_the_chosen_display_not_the_primary()
    {
        var displays = new[] { D("Primary", 0, 0, 1920, 1080, 1.0, true), D("Hi", 1920, 0, 3840, 2160, 2.0) };
        var onPrimary = SelectorPlacementCalculator.Compute(displays, (2000, 100), false);
        var onHi = SelectorPlacementCalculator.Compute(displays, (2000, 100), true);
        Assert.Equal(760, onPrimary.Bounds.Width);
        Assert.Equal(1520, onHi.Bounds.Width);
        Assert.Equal(2.0, onHi.Scale);
        AssertInside(onHi.Bounds, displays[1].Bounds);
    }

    [Fact]
    public void A_pointer_outside_every_display_falls_back_to_the_primary()
    {
        var displays = new[] { D("Primary", 0, 0, 1920, 1080, 1.0, true), D("Right", 1920, 0, 1920, 1080) };
        Assert.Equal("Primary", SelectorPlacementCalculator.Compute(displays, (99999, 99999), true).DisplayId);
        Assert.Equal("Primary", SelectorPlacementCalculator.Compute(displays, null, true).DisplayId);
    }

    [Fact]
    public void Display_edges_belong_to_exactly_one_display()
    {
        var displays = new[] { D("Primary", 0, 0, 1920, 1080, 1.0, true), D("Right", 1920, 0, 1920, 1080) };
        Assert.Equal("Primary", SelectorPlacementCalculator.Compute(displays, (1919, 500), true).DisplayId);
        Assert.Equal("Right", SelectorPlacementCalculator.Compute(displays, (1920, 500), true).DisplayId);
    }

    [Fact]
    public void Without_a_flagged_primary_the_first_display_is_used() =>
        Assert.Equal("A", SelectorPlacementCalculator.Compute([D("A", 0, 0, 1920, 1080), D("B", 1920, 0, 1920, 1080)], null, false).DisplayId);

    [Fact]
    public void Invalid_scale_factor_falls_back_to_100_percent() =>
        Assert.Equal(760, SelectorPlacementCalculator.Compute([D("A", 0, 0, 1920, 1080, 0.0, true)], null, false).Bounds.Width);

    [Fact]
    public void Scale_override_wins_over_the_display_scale() =>
        Assert.Equal(1520, SelectorPlacementCalculator.Compute([D("A", 0, 0, 3840, 2160, 1.0, true)], null, false, scaleOverride: 2.0).Bounds.Width);

    [Fact]
    public void No_displays_is_an_error() =>
        Assert.Throws<InvalidOperationException>(() => SelectorPlacementCalculator.Compute([], null, false));

    [Fact]
    public void Controller_hands_the_placement_to_the_view()
    {
        // covered in SelectorControllerTests; here we only confirm the preference defaults to the primary display
        Assert.False(new FocusPreferences().SelectorOnPointerDisplay);
    }

    // ---- general window placement (Admin Settings) ------------------------------------------------------------------

    [Fact]
    public void Window_is_centred_at_its_design_size_on_a_full_hd_100_percent_display()
    {
        var (w, h, x, y) = SelectorPlacementCalculator.CenterWindow(D("A", 0, 0, 1920, 1080, 1.0, true), 1180, 780);
        Assert.Equal((1180.0, 780.0, 370, 150), (w, h, x, y));
    }

    [Fact]
    public void On_a_4k_200_percent_display_the_dip_size_is_unchanged_and_the_pixel_size_doubles()
    {
        var display = D("A", 0, 0, 3840, 2160, 2.0, true);
        var (w, h, x, y) = SelectorPlacementCalculator.CenterWindow(display, 1180, 780);
        Assert.Equal((1180.0, 780.0), (w, h));
        Assert.Equal((740, 300), (x, y)); // (3840 - 2360) / 2, (2160 - 1560) / 2
    }

    [Fact]
    public void A_window_too_big_for_a_small_scaled_display_shrinks_to_90_percent_of_it()
    {
        var display = D("A", 0, 0, 1366, 768, 1.25, true);
        var (w, h, x, y) = SelectorPlacementCalculator.CenterWindow(display, 1180, 780);
        Assert.True(w * 1.25 <= 1366 * 0.9 + 0.01 && h * 1.25 <= 768 * 0.9 + 0.01);
        Assert.True(x >= 0 && y >= 0);
        Assert.True(x + w * 1.25 <= 1366 && y + h * 1.25 <= 768);
    }

    [Fact]
    public void Window_on_a_secondary_or_negative_origin_display_stays_inside_that_display()
    {
        foreach (var d in new[] { D("Right", 1920, 0, 1920, 1080), D("Left", -1920, 0, 1920, 1080), D("Above", 0, -1080, 1920, 1080), D("Hi", 1920, -400, 3840, 2160, 2.0) })
        {
            var (w, h, x, y) = SelectorPlacementCalculator.CenterWindow(d, 1180, 780);
            var s = d.ScaleFactor;
            Assert.True(x >= d.Bounds.X && y >= d.Bounds.Y, d.Id);
            Assert.True(x + w * s <= d.Bounds.X + d.Bounds.Width + 0.5 && y + h * s <= d.Bounds.Y + d.Bounds.Height + 0.5, d.Id);
        }
    }

    [Fact]
    public void Invalid_scale_in_window_placement_falls_back_to_100_percent()
    {
        var (w, _, _, _) = SelectorPlacementCalculator.CenterWindow(D("A", 0, 0, 1920, 1080, 0.0, true), 1180, 780);
        Assert.Equal(1180.0, w);
    }

    [Fact]
    public void PickDisplay_follows_the_pointer_only_when_asked()
    {
        var displays = new[] { D("P", 0, 0, 1920, 1080, 1.0, true), D("S", 1920, 0, 1920, 1080) };
        Assert.Equal("S", SelectorPlacementCalculator.PickDisplay(displays, (2500, 300), true).Id);
        Assert.Equal("P", SelectorPlacementCalculator.PickDisplay(displays, (2500, 300), false).Id);
        Assert.Equal("P", SelectorPlacementCalculator.PickDisplay(displays, (-50, -50), true).Id); // outside every display
        Assert.Equal("P", SelectorPlacementCalculator.PickDisplay(displays, null, true).Id);
    }
}
