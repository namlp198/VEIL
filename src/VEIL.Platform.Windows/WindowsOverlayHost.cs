using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices;
using VEIL.Domain;
using VEIL.Domain.Theme;
using VEIL.Platform.Abstractions;

namespace VEIL.Platform.Windows;

/// <summary>
/// Creates one borderless topmost overlay window per display on the message thread (ADR-002 candidate: overlay is
/// TOPMOST + NOACTIVATE; permitted windows are raised to TOPMOST after it, hidden ones are demoted to NOTOPMOST).
/// The canvas is painted from <see cref="VeilTheme"/>: a calm near-black field with a restrained violet HUD ring, the
/// wordmark, a focus indicator and the recovery shortcut, plus (UI/UX 3.1, 4.2) the clock, the focus timer, the active
/// workspace and a quiet system line. NFR-UX-001, FR-DISP-002/003.
///
/// Motion is deliberately small (UI/UX 5): the ring glow breathes on a four second cycle, one dot drifts around the ring,
/// and the overlay fades in over a fraction of a second. Nothing flashes or moves fast, and everything stops under
/// reduced motion. Painting is double-buffered and limited to the part of the screen that changed.
/// The window procedure only paints; every value it shows comes from <see cref="HudProvider"/>, which must be cheap and
/// non-blocking (ADR-009).
/// </summary>
public sealed class WindowsOverlayHost : IOverlayHost
{
    private const string ClassName = "VEIL.Overlay";
    private static readonly UIntPtr AnimTimer = new(1), ClockTimer = new(2), FadeTimer = new(3);
    // ~12 frames a second is plenty for a glow that takes four seconds per breath and a dot that takes thirty per turn.
    private const int AnimStepMs = 80, FadeStepMs = 16;

    private static readonly Native.WndProc Proc = OverlayProc;
    private static bool _registered;
    private static readonly ConcurrentDictionary<IntPtr, OverlayStyle> Styles = new();
    private static volatile Func<HudSnapshot>? _hud;
    private static readonly ConcurrentDictionary<IntPtr, byte> Primaries = new();
    private static Win32Thread? _sharedThread;
    private static volatile Action? _unlockRequested, _trayRequested;
    private readonly Win32Thread _thread;

    /// <summary>
    /// The small lock icon at the top right of the primary display was clicked. Raised on the command thread. It is the only
    /// thing on the overlay that reacts to the mouse.
    /// </summary>
    public Action? UnlockRequested { get => _unlockRequested; set => _unlockRequested = value; }

    /// <summary>The tray icon next to the lock icon was clicked: switch to tray mode. Raised on the command thread.</summary>
    public Action? TrayRequested { get => _trayRequested; set => _trayRequested = value; }

    /// <summary>Diagnostic: forces the text/graphics scale instead of the display's (exercises high-scale rendering).</summary>
    public double? ScaleOverride { get; set; }

    /// <summary>Where the overlay gets the clock, timer, workspace and so on. Called while painting: keep it fast.</summary>
    public Func<HudSnapshot>? HudProvider { get => _hud; set => _hud = value; }

    private sealed class OverlayStyle(double scale, bool animated, long born, IntPtr wordmark, IntPtr caption, IntPtr hint, IntPtr small, IntPtr clock)
    {
        public double Scale { get; } = scale;
        public bool Animated { get; } = animated;
        public long Born { get; } = born;
        public IntPtr Wordmark { get; } = wordmark;
        public IntPtr Caption { get; } = caption;
        public IntPtr Hint { get; } = hint;
        public IntPtr Small { get; } = small;
        public IntPtr Clock { get; } = clock;
        public IntPtr[] Fonts => [Wordmark, Caption, Hint, Small, Clock];
    }

    internal WindowsOverlayHost(Win32Thread thread) { _thread = thread; _sharedThread = thread; }

    public IOverlaySurface Create(DisplayInfo display) => _thread.Invoke(() =>
    {
        EnsureClass();
        var b = display.Bounds;
        var animated = Motion.Enabled;
        var hwnd = Native.CreateWindowEx(
            Native.WS_EX_TOPMOST | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | (animated ? Native.WS_EX_LAYERED : 0),
            ClassName, display.IsPrimary ? "VEIL Overlay (primary)" : "VEIL Overlay", Native.WS_POPUP,
            b.X, b.Y, b.Width, b.Height, IntPtr.Zero, IntPtr.Zero, Native.GetModuleHandle(null), IntPtr.Zero);
        if (hwnd == IntPtr.Zero)
            throw new InvalidOperationException($"CreateWindowEx failed for {display.Id} (error {Marshal.GetLastWin32Error()}).");

        // Text and graphics must stay legible on high-scale displays (NFR-UX-001).
        var scale = ScaleOverride is > 0 ? ScaleOverride.Value : display.ScaleFactor;
        var sizes = Palette.Theme.FontSizesPt;
        Styles[hwnd] = new OverlayStyle(scale, animated, Environment.TickCount64,
            Palette.Font(sizes["display"], scale, 600),
            Palette.Font(sizes["caption"], scale, 600),
            Palette.Font(sizes["body"], scale),
            Palette.Font(sizes["caption"], scale),
            Palette.Font(sizes["title"], scale));

        if (display.IsPrimary) Primaries[hwnd] = 1;
        if (animated) Native.SetLayeredWindowAttributes(hwnd, 0, 0, Native.LWA_ALPHA); // starts invisible, fades in
        Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, b.X, b.Y, b.Width, b.Height, Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
        Native.SetTimer(hwnd, ClockTimer, 1000, IntPtr.Zero);
        if (animated)
        {
            Native.SetTimer(hwnd, AnimTimer, AnimStepMs, IntPtr.Zero);
            Native.SetTimer(hwnd, FadeTimer, FadeStepMs, IntPtr.Zero);
        }
        return (IOverlaySurface)new Surface(_thread, display.Id, hwnd);
    });

    private void EnsureClass()
    {
        if (_registered) return;
        var cls = new Native.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<Native.WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(Proc),
            hInstance = Native.GetModuleHandle(null),
            hbrBackground = Palette.Brush("bg.canvas"),
            hCursor = Native.LoadCursor(IntPtr.Zero, new IntPtr(32512)), // without a class cursor the pointer is not drawn over the overlay
            lpszClassName = ClassName,
        };
        if (Native.RegisterClassEx(ref cls) == 0) throw new InvalidOperationException("Overlay window class registration failed.");
        _registered = true;
    }

    // ---- window procedure: paints and ticks, nothing else --------------------------------------------------------------

    private static IntPtr OverlayProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            switch (msg)
            {
                case Native.WM_ERASEBKGND:
                    return new IntPtr(1); // the buffered paint covers every pixel; erasing first would only flicker
                case Native.WM_PAINT:
                {
                    var hdc = Native.BeginPaint(hWnd, out var ps);
                    try { if (Styles.TryGetValue(hWnd, out var style)) PaintBuffered(hdc, hWnd, style, ps.rcPaint); }
                    finally { Native.EndPaint(hWnd, ref ps); }
                    return IntPtr.Zero;
                }
                case Native.WM_TIMER:
                    if (Styles.TryGetValue(hWnd, out var st)) OnTimer(hWnd, st, (UIntPtr)(nuint)wParam.ToInt64());
                    return IntPtr.Zero;
                case 0x0020: // WM_SETCURSOR: arrow everywhere, a hand over the lock icon
                {
                    var hand = false;
                    if (Primaries.ContainsKey(hWnd) && Styles.TryGetValue(hWnd, out var cs) && Native.GetCursorPos(out var cp))
                    {
                        Native.GetWindowRect(hWnd, out var wr);
                        Native.GetClientRect(hWnd, out var cr);
                        var x = cp.X - wr.Left; var y = cp.Y - wr.Top;
                        hand = InRect(UnlockIconRect(cr.Right - cr.Left, cs.Scale), x, y) || InRect(TrayIconRect(cr.Right - cr.Left, cs.Scale), x, y);
                    }
                    Native.SetCursor(Native.LoadCursor(IntPtr.Zero, new IntPtr(hand ? 32649 : 32512)));
                    return new IntPtr(1);
                }
                case Native.WM_LBUTTONUP:
                    if (Primaries.ContainsKey(hWnd) && Styles.TryGetValue(hWnd, out var ls))
                    {
                        Native.GetClientRect(hWnd, out var crc);
                        var px = (short)(lParam.ToInt64() & 0xFFFF);
                        var py = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
                        if (InRect(UnlockIconRect(crc.Right - crc.Left, ls.Scale), px, py)) _sharedThread?.Post(() => _unlockRequested?.Invoke());
                        else if (InRect(TrayIconRect(crc.Right - crc.Left, ls.Scale), px, py)) _sharedThread?.Post(() => _trayRequested?.Invoke());
                    }
                    return IntPtr.Zero;
            }
        }
        catch (Exception)
        {
            // A painting fault must never unwind into native code (it would end the process). The next paint tries again.
        }
        return Native.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private static void OnTimer(IntPtr hWnd, OverlayStyle st, UIntPtr id)
    {
        Native.GetClientRect(hWnd, out var rc);
        var g = Measure(rc.Right - rc.Left, rc.Bottom - rc.Top, st.Scale);
        if (id == AnimTimer)
        {
            var hud = g.Hud;
            Native.InvalidateRect(hWnd, ref hud, false);
        }
        else if (id == ClockTimer)
        {
            // Once a second: the clock, the workspace/notice lines and the system line. The centre also changes when a
            // timer counts down, and when nothing animates it is only redrawn here.
            foreach (var r in new[] { g.Clock, g.Info, g.Status, g.Hud })
            {
                var copy = r;
                Native.InvalidateRect(hWnd, ref copy, false);
            }
        }
        else if (id == FadeTimer)
        {
            var t = Math.Clamp((Environment.TickCount64 - st.Born) / Palette.Theme.MotionMs.GetValueOrDefault("slowMs", 400), 0, 1);
            var eased = 1 - Math.Pow(1 - t, 3); // ease-out
            Native.SetLayeredWindowAttributes(hWnd, 0, (byte)Math.Round(255 * eased), Native.LWA_ALPHA);
            if (t >= 1) Native.KillTimer(hWnd, FadeTimer);
        }
    }

    // ---- geometry shared by painting and invalidation --------------------------------------------------------------------

    /// <summary>The lock icon: a quiet circle in the top right corner (client coordinates).</summary>
    private static Native.RECT UnlockIconRect(int width, double s)
    {
        var size = Palette.Px(40, s);
        var margin = Palette.Px(24, s);
        return new Native.RECT { Left = width - margin - size, Top = margin, Right = width - margin, Bottom = margin + size };
    }

    /// <summary>The tray icon sits just left of the lock icon.</summary>
    private static Native.RECT TrayIconRect(int width, double s)
    {
        var lockIcon = UnlockIconRect(width, s);
        var size = lockIcon.Right - lockIcon.Left;
        var gap = Palette.Px(12, s);
        return new Native.RECT { Left = lockIcon.Left - gap - size, Top = lockIcon.Top, Right = lockIcon.Left - gap, Bottom = lockIcon.Bottom };
    }

    private static bool InRect(Native.RECT r, int x, int y) => x >= r.Left && x < r.Right && y >= r.Top && y < r.Bottom;

    private readonly record struct Geo(int Cx, int Cy, int R, int R2, Native.RECT Hud, Native.RECT Clock, Native.RECT Info, Native.RECT Status);

    private static Geo Measure(int w, int h, double s)
    {
        int cx = w / 2, cy = h / 2;
        var unit = Math.Min(w, h);
        var r = Math.Max(Palette.Px(72, s), (int)(unit * 0.13));
        var r2 = (int)(r * 1.45);
        var pad = r2 + Palette.Px(18, s);
        var infoTop = Math.Min(cy + 2 * r + Palette.Px(14, s), h - Palette.Px(150, s));
        return new Geo(cx, cy, r, r2,
            Hud: new() { Left = cx - pad, Top = cy - pad, Right = cx + pad, Bottom = cy + pad },
            Clock: new() { Left = cx - Palette.Px(240, s), Top = Palette.Px(18, s), Right = cx + Palette.Px(240, s), Bottom = Palette.Px(100, s) },
            Info: new() { Left = cx - Palette.Px(320, s), Top = infoTop, Right = cx + Palette.Px(320, s), Bottom = infoTop + Palette.Px(84, s) },
            Status: new() { Left = Palette.Px(20, s), Top = h - Palette.Px(56, s), Right = Palette.Px(420, s), Bottom = h - Palette.Px(20, s) });
    }

    // ---- painting ---------------------------------------------------------------------------------------------------------

    /// <summary>Draws only <paramref name="dirty"/> into an off-screen bitmap, then copies it over in one go.</summary>
    private static void PaintBuffered(IntPtr hdc, IntPtr hWnd, OverlayStyle st, Native.RECT dirty)
    {
        int bw = dirty.Right - dirty.Left, bh = dirty.Bottom - dirty.Top;
        if (bw <= 0 || bh <= 0) return;

        HudSnapshot hud;
        try { hud = _hud?.Invoke() ?? HudSnapshot.Empty; }
        catch { hud = HudSnapshot.Empty; }

        var mem = Native.CreateCompatibleDC(hdc);
        var bmp = mem == IntPtr.Zero ? IntPtr.Zero : Native.CreateCompatibleBitmap(hdc, bw, bh);
        if (bmp == IntPtr.Zero)
        {
            if (mem != IntPtr.Zero) Native.DeleteDC(mem);
            Native.FillRect(hdc, ref dirty, Palette.Brush("bg.canvas")); // out of GDI memory: draw directly rather than not at all
            PaintCanvas(hdc, hWnd, st, hud);
            return;
        }
        var old = Native.SelectObject(mem, bmp);
        try
        {
            Native.SetViewportOrgEx(mem, -dirty.Left, -dirty.Top, IntPtr.Zero);
            Native.FillRect(mem, ref dirty, Palette.Brush("bg.canvas"));
            PaintCanvas(mem, hWnd, st, hud);
            Native.SetViewportOrgEx(mem, 0, 0, IntPtr.Zero);
            Native.BitBlt(hdc, dirty.Left, dirty.Top, bw, bh, mem, 0, 0, Native.SRCCOPY);
        }
        finally
        {
            Native.SelectObject(mem, old);
            Native.DeleteObject(bmp);
            Native.DeleteDC(mem);
        }
    }

    private static string Spaced(string text) =>
        string.Join("   ", text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => string.Join(' ', w.ToCharArray())));

    private static void PaintCanvas(IntPtr hdc, IntPtr hWnd, OverlayStyle st, HudSnapshot hud)
    {
        Native.GetClientRect(hWnd, out var rc);
        int w = rc.Right - rc.Left, h = rc.Bottom - rc.Top;
        var g = Measure(w, h, st.Scale);
        int cx = g.Cx, cy = g.Cy, r = g.R, r2 = g.R2;
        var t = Palette.Theme;
        var s = st.Scale;
        Native.SetBkMode(hdc, 1);

        // Animation phase. Both values stay fixed when motion is off, so the picture is simply the calm resting state.
        var elapsedMs = Environment.TickCount64 - st.Born;
        var pulseMs = t.MotionMs.GetValueOrDefault("pulseMs", 4000);
        var breath = st.Animated ? 0.5 + 0.5 * Math.Sin(2 * Math.PI * elapsedMs / pulseMs - Math.PI / 2) : 0.5; // 0..1, starts low

        var canvas = t.Color("bg.canvas");
        var oldBrush = Native.SelectObject(hdc, Native.GetStockObject(Native.NULL_BRUSH));

        void Ring(int radius, int width, ThemeColor color, int style = Native.PS_SOLID)
        {
            var pen = Native.CreatePen(style, Math.Max(1, width), color.ToColorRef());
            var old = Native.SelectObject(hdc, pen);
            Native.Ellipse(hdc, cx - radius, cy - radius, cx + radius, cy + radius);
            Native.SelectObject(hdc, old);
            Native.DeleteObject(pen);
        }

        // soft outer glow: faint rings blended from the canvas towards the glow colour; the strength breathes a little
        var glow = t.Color("accent.glow");
        var glowStrength = 0.22 + 0.14 * breath;
        for (var i = 5; i >= 1; i--) Ring(r + Palette.Px(2 * i, s), 1, canvas.Mix(glow, glowStrength / i));
        Ring(r, Palette.Px(2, s), t.Color("accent.primaryHover").Mix(t.Color("accent.lilac"), 0.10 * breath));
        Ring(r - Palette.Px(6, s), 1, canvas.Mix(t.Color("accent.lilac"), 0.30 + 0.10 * breath));
        Ring(r2, 1, t.Color("border.strong"));
        Ring((int)(r * 2.0), 1, t.Color("border.subtle"), Native.PS_DOT);

        // tick marks on the middle ring; every quarter turn gets a longer cyan tick
        for (var deg = 0; deg < 360; deg += 10)
        {
            var major = deg % 90 == 0;
            var len = Palette.Px(major ? 12 : 6, s);
            var rad = deg * Math.PI / 180.0;
            var pen = Native.CreatePen(Native.PS_SOLID, major ? Math.Max(2, Palette.Px(2, s)) : 1,
                (major ? t.Color("accent.cyanDim") : t.Color("border.strong")).ToColorRef());
            var old = Native.SelectObject(hdc, pen);
            Native.MoveToEx(hdc, cx + (int)(Math.Cos(rad) * r2), cy + (int)(Math.Sin(rad) * r2), IntPtr.Zero);
            Native.LineTo(hdc, cx + (int)(Math.Cos(rad) * (r2 + len)), cy + (int)(Math.Sin(rad) * (r2 + len)));
            Native.SelectObject(hdc, old);
            Native.DeleteObject(pen);
        }

        // one small dot drifting around the middle ring: a full turn every half minute
        if (st.Animated)
        {
            var a = 2 * Math.PI * (elapsedMs % 30000) / 30000.0 - Math.PI / 2;
            var dot = Palette.Px(3, s);
            var dx = cx + (int)(Math.Cos(a) * r2);
            var dy = cy + (int)(Math.Sin(a) * r2);
            var dotBrush = Palette.Brush(canvas.Mix(t.Color("accent.cyan"), 0.55));
            var oldPen = Native.SelectObject(hdc, Native.GetStockObject(Native.NULL_PEN));
            var oldB = Native.SelectObject(hdc, dotBrush);
            Native.Ellipse(hdc, dx - dot, dy - dot, dx + dot, dy + dot);
            Native.SelectObject(hdc, oldB);
            Native.SelectObject(hdc, oldPen);
        }

        // --- focus timer: a thin arc just outside the main ring that shrinks as the session runs down
        var timerActive = hud.TimerRemaining is not null;
        if (timerActive)
        {
            var tr = r + Palette.Px(16, s);
            var width = Math.Max(2, Palette.Px(3, s));
            Ring(tr, 1, t.Color("border.subtle"));
            var remaining = Math.Clamp(1.0 - hud.TimerProgress, 0, 1);
            if (remaining > 0.003)
            {
                var pen = Native.CreatePen(Native.PS_SOLID, width, canvas.Mix(t.Color("accent.cyan"), 0.72).ToColorRef()); // a little softer than the caption cyan
                var old = Native.SelectObject(hdc, pen);
                Native.SetArcDirection(hdc, Native.AD_CLOCKWISE);
                var endAngle = -Math.PI / 2 + 2 * Math.PI * remaining;
                Native.Arc(hdc, cx - tr, cy - tr, cx + tr, cy + tr,
                    cx, cy - tr, cx + (int)Math.Round(Math.Cos(endAngle) * tr), cy + (int)Math.Round(Math.Sin(endAngle) * tr));
                Native.SelectObject(hdc, old);
                Native.DeleteObject(pen);
            }
        }
        Native.SelectObject(hdc, oldBrush);

        // --- centre: wordmark (or the time left) and the focus indicator
        var markH = Palette.Px(44, s);
        var mark = new Native.RECT { Left = cx - r, Right = cx + r, Top = cy - markH, Bottom = cy + Palette.Px(4, s) };
        var oldFont = Native.SelectObject(hdc, st.Wordmark);
        Native.SetTextColor(hdc, (timerActive ? t.Color("text.primary") : t.Color("accent.lilac")).ToColorRef());
        var centre = timerActive ? FormatRemaining(hud.TimerRemaining!.Value) : "VEIL";
        Native.DrawText(hdc, centre, -1, ref mark, Native.DT_CENTER | Native.DT_VCENTER | Native.DT_SINGLELINE | Native.DT_NOPREFIX);

        Native.SelectObject(hdc, st.Caption);
        var done = hud.Notice is not null;
        // "session complete" pulses softly so it is noticed without being loud
        var captionColor = done ? t.Color("accent.cyan").Mix(canvas, 0.45 * (1 - breath)) : t.Color("accent.cyan");
        Native.SetTextColor(hdc, captionColor.ToColorRef());
        var cap = new Native.RECT { Left = cx - r, Right = cx + r, Top = cy + Palette.Px(6, s), Bottom = cy + Palette.Px(32, s) };
        var captionText = done ? "SESSION COMPLETE" : timerActive ? "FOCUS SESSION" : "FOCUS ACTIVE";
        Native.DrawText(hdc, Spaced(captionText), -1, ref cap, Native.DT_CENTER | Native.DT_VCENTER | Native.DT_SINGLELINE | Native.DT_NOPREFIX);

        // --- clock and date, top centre (UI/UX 3.1); calm and small
        if (hud.ShowClock)
        {
            var clockRc = g.Clock;
            clockRc.Bottom = clockRc.Top + Palette.Px(34, s);
            Native.SelectObject(hdc, st.Clock);
            Native.SetTextColor(hdc, t.Color("text.secondary").ToColorRef());
            Native.DrawText(hdc, hud.Now.ToString("t", CultureInfo.CurrentCulture), -1, ref clockRc,
                Native.DT_CENTER | Native.DT_VCENTER | Native.DT_SINGLELINE | Native.DT_NOPREFIX);
            var dateRc = g.Clock;
            dateRc.Top = clockRc.Bottom;
            dateRc.Bottom = dateRc.Top + Palette.Px(24, s);
            Native.SelectObject(hdc, st.Small);
            Native.SetTextColor(hdc, t.Color("text.muted").ToColorRef());
            Native.DrawText(hdc, hud.Now.ToString("dddd, d MMMM", CultureInfo.CurrentCulture), -1, ref dateRc,
                Native.DT_CENTER | Native.DT_VCENTER | Native.DT_SINGLELINE | Native.DT_NOPREFIX);
        }

        // --- workspace, shown windows and the completion notice, below the outer ring
        var line = g.Info;
        line.Bottom = line.Top + Palette.Px(26, s);
        if (!string.IsNullOrEmpty(hud.Workspace))
        {
            Native.SelectObject(hdc, st.Caption);
            Native.SetTextColor(hdc, t.Color("accent.lilac").ToColorRef());
            Native.DrawText(hdc, "WORKSPACE  ·  " + hud.Workspace!.ToUpper(CultureInfo.CurrentCulture), -1, ref line,
                Native.DT_CENTER | Native.DT_VCENTER | Native.DT_SINGLELINE | Native.DT_NOPREFIX | Native.DT_END_ELLIPSIS);
            line.Top = line.Bottom;
            line.Bottom = line.Top + Palette.Px(24, s);
        }
        var detail = hud.Notice ?? (hud.VisibleWindows > 0
            ? string.Create(CultureInfo.CurrentCulture, $"{hud.VisibleWindows} window{(hud.VisibleWindows == 1 ? "" : "s")} shown")
            : null);
        if (detail is not null)
        {
            Native.SelectObject(hdc, st.Small);
            Native.SetTextColor(hdc, (hud.Notice is not null ? t.Color("text.secondary") : t.Color("text.muted")).ToColorRef());
            Native.DrawText(hdc, detail, -1, ref line, Native.DT_CENTER | Native.DT_VCENTER | Native.DT_SINGLELINE | Native.DT_NOPREFIX | Native.DT_END_ELLIPSIS);
        }

        // --- quiet system status, bottom left
        if (hud.ShowSystemStatus && (hud.CpuPercent is not null || hud.MemoryPercent is not null))
        {
            var parts = new List<string>();
            if (hud.CpuPercent is { } cpu) parts.Add(string.Create(CultureInfo.InvariantCulture, $"CPU {cpu:0}%"));
            if (hud.MemoryPercent is { } mem) parts.Add(string.Create(CultureInfo.InvariantCulture, $"RAM {mem:0}%"));
            var status = g.Status;
            Native.SelectObject(hdc, st.Small);
            Native.SetTextColor(hdc, t.Color("text.muted").ToColorRef());
            Native.DrawText(hdc, string.Join("    ", parts), -1, ref status, Native.DT_LEFT | Native.DT_VCENTER | Native.DT_SINGLELINE | Native.DT_NOPREFIX);
        }

        // --- the way out: a small lock icon, top right of the primary display only. No shortcut is written on the screen.
        if (Primaries.ContainsKey(hWnd))
        {
            var ic = UnlockIconRect(w, s);
            var ring = Native.CreatePen(Native.PS_SOLID, Math.Max(1, Palette.Px(1, s)), t.Color("border.strong").ToColorRef());
            var oldRing = Native.SelectObject(hdc, ring);
            var oldFill = Native.SelectObject(hdc, Palette.Brush(canvas.Mix(t.Color("bg.raised"), 0.6)));
            Native.Ellipse(hdc, ic.Left, ic.Top, ic.Right, ic.Bottom);
            Native.SelectObject(hdc, oldFill);
            Native.SelectObject(hdc, oldRing);
            Native.DeleteObject(ring);

            // padlock: body, and a shackle drawn as an arch above it
            int icx = (ic.Left + ic.Right) / 2, icy = (ic.Top + ic.Bottom) / 2, u = Math.Max(1, Palette.Px(1, s));
            var glyph = Native.CreatePen(Native.PS_SOLID, Math.Max(2, Palette.Px(2, s)), t.Color("text.secondary").ToColorRef());
            var oldGlyph = Native.SelectObject(hdc, glyph);
            var oldNone = Native.SelectObject(hdc, Native.GetStockObject(Native.NULL_BRUSH));
            Native.RoundRect(hdc, icx - 8 * u, icy - 1 * u, icx + 8 * u, icy + 9 * u, 3 * u, 3 * u);
            Native.Arc(hdc, icx - 5 * u, icy - 11 * u, icx + 5 * u, icy + 1 * u, icx + 5 * u, icy - 5 * u, icx - 5 * u, icy - 5 * u);
            Native.SelectObject(hdc, oldNone);
            Native.SelectObject(hdc, oldGlyph);
            Native.DeleteObject(glyph);
        }
        // tray icon (primary display): a window with a docked strip on its left edge; click switches to tray mode
        if (Primaries.ContainsKey(hWnd))
        {
            var tc = TrayIconRect(w, s);
            var ring = Native.CreatePen(Native.PS_SOLID, Math.Max(1, Palette.Px(1, s)), t.Color("border.strong").ToColorRef());
            var oldRing = Native.SelectObject(hdc, ring);
            var oldFill = Native.SelectObject(hdc, Palette.Brush(canvas.Mix(t.Color("bg.raised"), 0.6)));
            Native.Ellipse(hdc, tc.Left, tc.Top, tc.Right, tc.Bottom);
            Native.SelectObject(hdc, oldFill);
            Native.SelectObject(hdc, oldRing);
            Native.DeleteObject(ring);
            int tcx = (tc.Left + tc.Right) / 2, tcy = (tc.Top + tc.Bottom) / 2, u = Math.Max(1, Palette.Px(1, s));
            var glyph = Native.CreatePen(Native.PS_SOLID, Math.Max(2, Palette.Px(2, s)), t.Color("text.secondary").ToColorRef());
            var oldGlyph = Native.SelectObject(hdc, glyph);
            var oldNone = Native.SelectObject(hdc, Native.GetStockObject(Native.NULL_BRUSH));
            Native.RoundRect(hdc, tcx - 9 * u, tcy - 7 * u, tcx + 9 * u, tcy + 7 * u, 3 * u, 3 * u);
            Native.MoveToEx(hdc, tcx - 4 * u, tcy - 7 * u, IntPtr.Zero);
            Native.LineTo(hdc, tcx - 4 * u, tcy + 7 * u);
            Native.SelectObject(hdc, oldNone);
            Native.SelectObject(hdc, oldGlyph);
            Native.DeleteObject(glyph);
        }
        Native.SelectObject(hdc, oldFont);
    }

    private static string FormatRemaining(TimeSpan remaining)
    {
        var total = (int)Math.Ceiling(Math.Max(0, remaining.TotalSeconds));
        return total >= 3600
            ? string.Create(CultureInfo.InvariantCulture, $"{total / 3600}:{total % 3600 / 60:00}:{total % 60:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{total / 60:00}:{total % 60:00}");
    }

    private sealed class Surface(Win32Thread thread, string displayId, IntPtr hwnd) : IOverlaySurface
    {
        private int _disposed;
        public string DisplayId => displayId;
        public bool IsHealthy => _disposed == 0 && Native.IsWindow(hwnd);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
            try
            {
                thread.Invoke(() =>
                {
                    if (Native.IsWindow(hwnd)) Native.DestroyWindow(hwnd);
                    Primaries.TryRemove(hwnd, out _);
                    if (Styles.TryRemove(hwnd, out var st))
                        foreach (var f in st.Fonts) if (f != IntPtr.Zero) Native.DeleteObject(f);
                });
            }
            catch
            {
                // Message thread gone: the OS destroys windows owned by the exiting thread/process.
            }
        }
    }
}
