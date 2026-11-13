using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using AxamlCheck;

AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
var fails = 0;
void Check(string what, bool ok) { Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {what}"); if (!ok) fails++; }

Dispatcher.UIThread.Invoke(() =>
{
    var app = Application.Current!;
    object? Res(string key) => app.TryFindResource(key, app.ActualThemeVariant, out var v) ? v : null;

    Check("Color.bg.canvas = #080817", Res("Color.bg.canvas") is Color c && c == Color.Parse("#080817"));
    Check("Brush.accent.primary resolves to a SolidColorBrush #7A31D6", Res("Brush.accent.primary") is SolidColorBrush b && b.Color == Color.Parse("#7A31D6"));
    Check("Radius.pill = 999", Res("Radius.pill") is CornerRadius r && r.TopLeft == 999);
    Check("Space.lg = 16", Res("Space.lg") is double d && d == 16);
    Check("Font.Size.body = 14 px (10.5 pt)", Res("Font.Size.body") is double f && Math.Abs(f - 14) < 0.01);
    Check("Component.selectorRowHeight = 52", Res("Component.selectorRowHeight") is double h && h == 52);
    Check("Font.Family resolves", Res("Font.Family") is FontFamily);

    var pill = new Border { Classes = { "pill", "allowed" }, Child = new TextBlock { Text = "Allowed" } };
    var win = new Window { Content = pill };
    win.Show();
    Check("Border.pill.allowed gets the success background", pill.Background is ISolidColorBrush sb && sb.Color == Color.Parse("#0E2A1D"));
    Check("pill label gets the success foreground", ((TextBlock)pill.Child!).Foreground is ISolidColorBrush fb && fb.Color == Color.Parse("#35D07F"));
    Check("pill has the pill corner radius", pill.CornerRadius.TopLeft == 999);

    var panel = new Border { Classes = { "panel" } };
    win.Content = panel;
    Check("Border.panel uses surface background", panel.Background is ISolidColorBrush pb && pb.Color == Color.Parse("#100B1A"));

    var btn = new Button { Content = "START" };
    win.Content = btn;
    Check("Button uses primary fill and white label", btn.Background is ISolidColorBrush bb && bb.Color == Color.Parse("#7A31D6") && btn.Foreground is ISolidColorBrush bf && bf.Color == Color.Parse("#FFFFFF"));
    win.Close();
});
Console.WriteLine(fails == 0 ? "ALL PASSED" : $"{fails} FAILED");
return fails;
