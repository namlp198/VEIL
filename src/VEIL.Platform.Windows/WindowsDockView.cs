using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using VEIL.Domain;
using VEIL.Platform.Abstractions;

namespace VEIL.Platform.Windows;

/// <summary>
/// The dock: allowed applications at the left edge of the Focus screen. A topmost, never-activating tool window (it takes no
/// keyboard focus, so the application in front keeps it) painted from the theme. Expanded it is a list with the workspace
/// chips and a tray band at the bottom; collapsed (tray mode) it is a slim strip of pinned apps with a "…" button.
/// Tiles are dragged with the mouse: a list tile dropped on the tray band pins its application, a tray tile dropped anywhere
/// else (or right-clicked) unpins it. In tray mode a window of another program that is dragged by its title bar and let go
/// over the dock is "thrown in": the dock polls the mouse (a window of another program cannot send it a drop). Nothing here runs application logic: every event is handed to the command thread
/// (ADR-009), and icons are fetched there as well, never on the window thread.
/// </summary>
public sealed class WindowsDockView : IDockView
{
    private const string ClassName = "VEIL.Dock";
    private const int DragThresholdPx = 6;

    private static readonly Native.WndProc Proc = StaticProc;
    private static WindowsDockView? _current;
    private static bool _registered;

    private enum HitKind { None, Tile, PinTile, Band, Chip, Close, Expand, Remove, Lock }
    private readonly record struct Hit(Native.RECT Rect, HitKind Kind, DockTile? Tile = null, string? Tag = null);
    private static readonly UIntPtr PollTimer = new(2);
    private const int PollMs = 50, StatusMs = 4000;

    private readonly Win32Thread _thread;
    private readonly ConcurrentDictionary<string, IntPtr> _icons = new(StringComparer.OrdinalIgnoreCase);
    private IntPtr _hwnd, _fBody, _fTitle, _fSmall;
    private double _scale = 1.0;
    private WindowBounds _area = new(0, 0, 1920, 1080);
    private DockViewState _state = new(DockMode.Panel, false, [], [], string.Empty);
    private List<Hit> _hits = [];
    private Native.RECT _band;
    private int _width, _height;
    private Hit? _down;
    private Native.POINT _downAt, _cursor;
    private bool _dragging;
    private bool _pollDown, _hot;          // a window of another program is being dragged / is over the dock
    private string? _dragWindow;
    private IntPtr _dragRoot;
    private Native.RECT _dragStartRect;
    private bool _dragCaption, _dragZoomed;
    private string _lastStatus = string.Empty;
    private long _statusUntil;

    internal WindowsDockView(Win32Thread thread) => _thread = thread;

    public event Action<string>? TileClicked;
    public event Action<string>? WindowDropped;
    public event Action<string>? RemoveRequested;
    public event Action? LockRequested;
    public event Action<string?>? WorkspaceRequested;
    public event Action<string>? PinRequested;
    public event Action<string>? UnpinRequested;
    public event Action? ExpandToggleRequested;

    public bool IsVisible => _hwnd != IntPtr.Zero && Native.IsWindow(_hwnd);

    private int S(double dip) => Palette.Px(dip, _scale);

    public void Show(DockViewState state, SelectorPlacement area)
    {
        LoadIcons(state); // shell call: on the caller's (command) thread, not the window thread
        _thread.Invoke(() =>
        {
            if (IsVisible) { Render(state); return; }
            EnsureClass();
            _scale = area.Scale > 0 ? area.Scale : 1.0;
            _area = area.Bounds;
            _current = this;
            _hwnd = Native.CreateWindowEx(Native.WS_EX_TOPMOST | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE, ClassName, "VEIL Dock",
                Native.WS_POPUP, _area.X, _area.Y, 10, 10, IntPtr.Zero, IntPtr.Zero, Native.GetModuleHandle(null), IntPtr.Zero);
            if (_hwnd == IntPtr.Zero) { _current = null; throw new InvalidOperationException("Dock window could not be created."); }
            var sizes = Palette.Theme.FontSizesPt;
            _fBody = Palette.Font(sizes["body"], _scale);
            _fTitle = Palette.Font(sizes["body"], _scale, 600);
            _fSmall = Palette.MonoFont(sizes["caption"], _scale);
            var corner = 2; // DWMWCP_ROUND
            Native.DwmSetWindowAttribute(_hwnd, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
            var border = (int)Palette.Ref("border.strong");
            Native.DwmSetWindowAttribute(_hwnd, Native.DWMWA_BORDER_COLOR, ref border, sizeof(int));
            Render(state);
        });
    }

    public void Update(DockViewState state)
    {
        LoadIcons(state);
        _thread.Invoke(() => { if (IsVisible) Render(state); });
    }

    public void Hide() => _thread.Invoke(() =>
    {
        if (_hwnd == IntPtr.Zero) return;
        var h = _hwnd;
        _hwnd = IntPtr.Zero;
        _down = null;
        _dragging = false;
        _pollDown = _hot = false;
        _dragWindow = null;
        if (_current == this) _current = null;
        if (Native.IsWindow(h)) Native.DestroyWindow(h);
        foreach (var f in new[] { _fBody, _fTitle, _fSmall }) if (f != IntPtr.Zero) Native.DeleteObject(f);
        _fBody = _fTitle = _fSmall = IntPtr.Zero;
    });

    // ---- icons ---------------------------------------------------------------------------------------------------------

    private void LoadIcons(DockViewState state)
    {
        foreach (var t in state.Tiles.Concat(state.Pinned))
        {
            var id = t.ApplicationId;
            if (_icons.ContainsKey(id)) continue;
            var icon = IntPtr.Zero;
            try
            {
                if (!id.StartsWith("unknown:", StringComparison.Ordinal) && File.Exists(id))
                {
                    var info = new Native.SHFILEINFO();
                    if (Native.SHGetFileInfo(id, 0, ref info, (uint)Marshal.SizeOf<Native.SHFILEINFO>(), Native.SHGFI_ICON | Native.SHGFI_LARGEICON) != IntPtr.Zero)
                        icon = info.hIcon;
                }
            }
            catch (Exception) { /* no icon: a letter tile is drawn instead */ }
            _icons[id] = icon;
        }
    }

    // ---- layout --------------------------------------------------------------------------------------------------------

    private static Native.RECT Box(int x, int y, int w, int h) => new() { Left = x, Top = y, Right = x + w, Bottom = y + h };

    /// <summary>Works out every rectangle for the current state and resizes/moves the window to fit.</summary>
    private void Render(DockViewState state)
    {
        _state = state;
        var hits = new List<Hit>();
        int w, h;
        _band = default;
        if (state.Expanded)
        {
            int m = S(12), gap = S(6), tileH = S(56), bandH = S(84), head = S(44);
            w = S(280);
            hits.Add(new Hit(Box(w - m - S(28), m + S(8), S(28), S(28)), HitKind.Close));
            var top = m + head + gap;
            // workspace chips: "All" and each workspace, wrapping onto at most two rows
            if (state.Workspaces is { Count: > 0 } spaces)
            {
                int cx = m, cy = top, chipH = S(26), rows = 1;
                var names = new List<string?> { null };
                names.AddRange(spaces);
                foreach (var n in names)
                {
                    var cw = Math.Max(S(44), S(22) + (n ?? "All").Length * S(7));
                    if (cx + cw > w - m) { if (rows == 2) break; rows++; cx = m; cy += chipH + S(4); }
                    hits.Add(new Hit(Box(cx, cy, cw, chipH), HitKind.Chip, Tag: n ?? string.Empty));
                    cx += cw + S(6);
                }
                top = cy + chipH + gap;
            }
            var room = _area.Height - 2 * S(16) - top - bandH - m - gap;
            var maxTiles = Math.Max(1, room / (tileH + gap));
            var shown = Math.Min(state.Tiles.Count, maxTiles);
            for (var i = 0; i < shown; i++)
            {
                var tile = Box(m, top + i * (tileH + gap), w - 2 * m, tileH);
                hits.Add(new Hit(tile, HitKind.Tile, state.Tiles[i]));
                hits.Add(new Hit(Box(tile.Right - S(30), tile.Top + S(4), S(26), S(26)), HitKind.Remove, state.Tiles[i]));
            }
            var bandTop = top + Math.Max(1, shown) * (tileH + gap);
            _band = Box(m, bandTop, w - 2 * m, bandH);
            hits.Add(new Hit(_band, HitKind.Band));
            var px = m + S(10);
            for (var i = 0; i < state.Pinned.Count && px + S(44) <= w - m; i++, px += S(52))
                hits.Add(new Hit(Box(px, bandTop + S(30), S(44), S(44)), HitKind.PinTile, state.Pinned[i]));
            h = bandTop + bandH + m;
        }
        else
        {
            int pad = S(10), cell = S(64);
            w = S(76);
            var y = pad;
            foreach (var p in state.Pinned) { hits.Add(new Hit(Box(pad, y, S(56), S(56)), HitKind.PinTile, p)); y += cell; }
            if (state.Pinned.Count == 0) { hits.Add(new Hit(Box(pad, y, S(56), S(56)), HitKind.Band)); y += cell; }
            hits.Add(new Hit(Box(pad, y, S(56), S(28)), HitKind.Expand));
            y += S(32);
            hits.Add(new Hit(Box(pad, y, S(56), S(28)), HitKind.Lock));
            h = y + S(28) + pad;
        }
        if (state.Status != _lastStatus) { _lastStatus = state.Status; _statusUntil = Environment.TickCount64 + StatusMs; }
        var toast = !state.Expanded && state.Status.Length > 0 && Environment.TickCount64 < _statusUntil;
        if (toast) w += S(230);
        _hits = hits;
        _width = w;
        _height = h;
        Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, _area.X + S(12), _area.Y + S(16), w, h, Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
        if (state.Mode == DockMode.Tray) Native.SetTimer(_hwnd, PollTimer, PollMs, IntPtr.Zero); else Native.KillTimer(_hwnd, PollTimer);
        Native.InvalidateRect(_hwnd, IntPtr.Zero, true);
    }

    private Hit? HitTest(int x, int y)
    {
        for (var i = _hits.Count - 1; i >= 0; i--) // later entries sit on top of earlier ones
        {
            var r = _hits[i].Rect;
            if (x >= r.Left && x < r.Right && y >= r.Top && y < r.Bottom) return _hits[i];
        }
        return null;
    }

    private bool InBand(int x, int y) => _band.Right > _band.Left && x >= _band.Left && x < _band.Right && y >= _band.Top && y < _band.Bottom;

    // ---- painting ------------------------------------------------------------------------------------------------------

    private void Round(IntPtr hdc, Native.RECT r, string fill, string? border, int penWidth, int radius)
    {
        var pen = border is null ? Native.GetStockObject(Native.NULL_PEN) : Native.CreatePen(Native.PS_SOLID, Math.Max(1, penWidth), Palette.Ref(border));
        var oldPen = Native.SelectObject(hdc, pen);
        var oldBrush = Native.SelectObject(hdc, Palette.Brush(fill));
        Native.RoundRect(hdc, r.Left, r.Top, r.Right, r.Bottom, radius, radius);
        Native.SelectObject(hdc, oldBrush);
        Native.SelectObject(hdc, oldPen);
        if (border is not null) Native.DeleteObject(pen);
    }

    private void Text(IntPtr hdc, string text, Native.RECT r, IntPtr font, string colour, uint flags)
    {
        Native.SelectObject(hdc, font);
        Native.SetTextColor(hdc, Palette.Ref(colour));
        Native.DrawText(hdc, text, -1, ref r, flags | Native.DT_VCENTER | Native.DT_SINGLELINE | Native.DT_NOPREFIX | Native.DT_END_ELLIPSIS);
    }

    private void Icon(IntPtr hdc, DockTile t, int cx, int cy, int size)
    {
        if (_icons.TryGetValue(t.ApplicationId, out var icon) && icon != IntPtr.Zero)
        {
            Native.DrawIconEx(hdc, cx - size / 2, cy - size / 2, icon, size, size, 0, IntPtr.Zero, Native.DI_NORMAL);
            return;
        }
        var r = Box(cx - size / 2, cy - size / 2, size, size);
        Round(hdc, r, "accent.primary", null, 0, S(8));
        Text(hdc, t.AppName.Length > 0 ? char.ToUpperInvariant(t.AppName[0]).ToString() : "?", r, _fTitle, "text.primary", Native.DT_CENTER);
    }

    private void DrawTile(IntPtr hdc, Hit hit, bool dim)
    {
        var t = hit.Tile!;
        var r = hit.Rect;
        var compact = hit.Kind == HitKind.PinTile && !_state.Expanded;
        var small = hit.Kind == HitKind.PinTile;
        var empty = small && t.RuntimeId.Length == 0;
        Round(hdc, r, "bg.raised", t.IsShown ? "accent.cyan" : "border.strong", t.IsShown ? S(2) : 1, S(compact ? 14 : 12));
        var cy = (r.Top + r.Bottom) / 2;
        if (small)
        {
            // While dragged only the empty slot stays (the icon follows the pointer). Not running: quiet border, no shown-state ring.
            if (!dim) Icon(hdc, t, (r.Left + r.Right) / 2, cy, S(compact ? 32 : 28));
            if (empty && !dim) Text(hdc, "·", Box(r.Left, r.Bottom - S(14), r.Right - r.Left, S(12)), _fSmall, "text.muted", Native.DT_CENTER);
            return;
        }
        Icon(hdc, t, r.Left + S(14) + S(16), cy, S(32));
        var x = r.Left + S(62);
        var state = t.IsShown ? "Shown" : "Hidden";
        var stateW = S(52);
        Text(hdc, state, Box(r.Right - stateW - S(34), r.Top, stateW, r.Bottom - r.Top), _fSmall, t.IsShown ? "status.success" : "text.muted", Native.DT_RIGHT);
        var textW = r.Right - stateW - S(38) - x;
        Text(hdc, t.AppName, Box(x, r.Top + S(8), textW, S(20)), _fTitle, dim ? "text.muted" : "text.primary", Native.DT_LEFT);
        Text(hdc, t.Title, Box(x, r.Top + S(29), textW, S(18)), _fSmall, "text.muted", Native.DT_LEFT);
    }

    private void Paint(IntPtr hdc)
    {
        var client = Box(0, 0, _width, _height);
        Native.FillRect(hdc, ref client, Palette.Brush("bg.surface"));
        var dragTile = _dragging ? _down?.Tile : null;
        var dragKind = _dragging ? _down?.Kind : null;
        var overBand = _dragging && dragKind == HitKind.Tile && InBand(_cursor.X, _cursor.Y);

        if (_state.Expanded)
        {
            Text(hdc, _state.ActiveWorkspace is { } ws ? $"ALLOWED APPS · {ws.ToUpperInvariant()}" : "ALLOWED APPS", Box(S(16), S(12), S(190), S(44)), _fSmall, "accent.lilac", Native.DT_LEFT);
            foreach (var h in _hits)
            {
                switch (h.Kind)
                {
                    case HitKind.Close: Text(hdc, "×", h.Rect, _fTitle, "text.secondary", Native.DT_CENTER); break;
                    case HitKind.Chip:
                    {
                        var on = h.Tag!.Length == 0 ? _state.ActiveWorkspace is null
                            : string.Equals(h.Tag, _state.ActiveWorkspace, StringComparison.OrdinalIgnoreCase);
                        Round(hdc, h.Rect, on ? "accent.primary" : "bg.raised", on ? null : "border.subtle", 1, S(13));
                        Text(hdc, h.Tag.Length == 0 ? "All" : h.Tag, h.Rect, _fSmall, on ? "text.primary" : "text.secondary", Native.DT_CENTER);
                        break;
                    }
                }
            }
            if (_state.Tiles.Count == 0)
                Text(hdc, "The tray is empty. Press the tray icon at the top right, then drag an app onto the tray.", Box(S(12), S(62), _width - S(24), S(56)), _fSmall, "text.muted", Native.DT_CENTER);
            var hidden = _state.Tiles.Count - _hits.Count(x => x.Kind == HitKind.Tile);
            foreach (var h in _hits)
            {
                if (h.Kind == HitKind.Tile) DrawTile(hdc, h, dim: ReferenceEquals(h.Tile, dragTile));
                else if (h.Kind == HitKind.Remove) Text(hdc, "×", h.Rect, _fTitle, "text.muted", Native.DT_CENTER);
            }
            if (hidden > 0) Text(hdc, $"+{hidden} more", Box(S(12), _band.Top - S(22), _width - S(24), S(18)), _fSmall, "text.muted", Native.DT_CENTER);

            Round(hdc, _band, "bg.surface", overBand ? "accent.cyan" : "border.subtle", overBand ? S(2) : 1, S(12));
            Text(hdc, "TRAY", Box(_band.Left + S(12), _band.Top + S(4), S(60), S(22)), _fSmall, "accent.lilac", Native.DT_LEFT);
            var note = _state.Status.Length > 0 ? _state.Status : _state.Pinned.Count == 0 ? "Drag an app here to pin it" : "Drag out or right-click to unpin";
            Text(hdc, note, Box(_band.Left + S(62), _band.Top + S(4), _band.Right - _band.Left - S(70), S(22)), _fSmall, "text.muted", Native.DT_RIGHT);
            foreach (var h in _hits) if (h.Kind == HitKind.PinTile) DrawTile(hdc, h, dim: ReferenceEquals(h.Tile, dragTile));
        }
        else
        {
            foreach (var h in _hits)
            {
                if (h.Kind == HitKind.PinTile) DrawTile(hdc, h, dim: ReferenceEquals(h.Tile, dragTile));
                else if (h.Kind == HitKind.Expand) Text(hdc, "…", h.Rect, _fTitle, "text.secondary", Native.DT_CENTER);
                else if (h.Kind == HitKind.Lock)
                {
                    Round(hdc, h.Rect, "accent.primary", null, 0, S(10));
                    Text(hdc, "Lock", h.Rect, _fSmall, "text.primary", Native.DT_CENTER);
                }
                else if (h.Kind == HitKind.Band)
                {
                    Round(hdc, h.Rect, "bg.raised", _hot ? "accent.cyan" : "border.strong", _hot ? S(2) : 1, S(14));
                    Text(hdc, "+", Box(h.Rect.Left, h.Rect.Top, S(56), S(30)), _fTitle, "text.secondary", Native.DT_CENTER);
                    Text(hdc, "Drop", Box(h.Rect.Left, h.Rect.Top + S(26), S(56), S(18)), _fSmall, "text.muted", Native.DT_CENTER);
                }
            }
            if (_state.Status.Length > 0 && Environment.TickCount64 < _statusUntil)
                Text(hdc, _state.Status, Box(S(80), S(10), _width - S(88), _height - S(20)), _fSmall, "text.secondary", Native.DT_LEFT);
        }
        if (_hot)
        {
            var t = Math.Max(2, S(2));
            foreach (var edge in new[] { Box(0, 0, _width, t), Box(0, _height - t, _width, t), Box(0, 0, t, _height), Box(_width - t, 0, t, _height) })
            { var e = edge; Native.FillRect(hdc, ref e, Palette.Brush("accent.cyan")); }
        }

        if (_dragging && dragTile is not null) Icon(hdc, dragTile, _cursor.X, _cursor.Y, S(36)); // follows the pointer
    }

    private void PaintWindow()
    {
        var hdc = Native.BeginPaint(_hwnd, out var ps);
        try
        {
            var mem = Native.CreateCompatibleDC(hdc);
            var bmp = mem == IntPtr.Zero ? IntPtr.Zero : Native.CreateCompatibleBitmap(hdc, Math.Max(1, _width), Math.Max(1, _height));
            if (bmp == IntPtr.Zero) { if (mem != IntPtr.Zero) Native.DeleteDC(mem); Native.SetBkMode(hdc, 1); Paint(hdc); return; }
            var old = Native.SelectObject(mem, bmp);
            try
            {
                Native.SetBkMode(mem, 1);
                Paint(mem);
                Native.BitBlt(hdc, 0, 0, _width, _height, mem, 0, 0, Native.SRCCOPY);
            }
            finally { Native.SelectObject(mem, old); Native.DeleteObject(bmp); Native.DeleteDC(mem); }
        }
        finally { Native.EndPaint(_hwnd, ref ps); }
    }

    // ---- window class and messages ---------------------------------------------------------------------------------------

    private static void EnsureClass()
    {
        if (_registered) return;
        var cls = new Native.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<Native.WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(Proc),
            hInstance = Native.GetModuleHandle(null),
            hbrBackground = Palette.Brush("bg.surface"),
            hCursor = Native.LoadCursor(IntPtr.Zero, new IntPtr(32512)),
            lpszClassName = ClassName,
        };
        if (Native.RegisterClassEx(ref cls) == 0) throw new InvalidOperationException("Dock window class registration failed.");
        _registered = true;
    }

    private void Raise(Action work) => _thread.Post(work);

    private static IntPtr StaticProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        try { return Dispatch(hWnd, msg, wParam, lParam); }
        catch (Exception) { return Native.DefWindowProc(hWnd, msg, wParam, lParam); } // never unwind into native code
    }

    private static IntPtr Dispatch(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        var self = _current;
        if (self is null || hWnd != self._hwnd) return Native.DefWindowProc(hWnd, msg, wParam, lParam);
        switch (msg)
        {
            case Native.WM_ERASEBKGND: return new IntPtr(1);
            case Native.WM_PAINT: self.PaintWindow(); return IntPtr.Zero;
            case Native.WM_MOUSEACTIVATE: return new IntPtr(Native.MA_NOACTIVATE); // clicking the dock must not steal focus
            case Native.WM_LBUTTONDOWN: self.MouseDown(Point(lParam)); return IntPtr.Zero;
            case Native.WM_MOUSEMOVE: self.MouseMove(Point(lParam)); return IntPtr.Zero;
            case Native.WM_LBUTTONUP: self.MouseUp(Point(lParam)); return IntPtr.Zero;
            case Native.WM_CAPTURECHANGED: self._down = null; self._dragging = false; return IntPtr.Zero;
            case Native.WM_RBUTTONUP:
            {
                var p = Point(lParam);
                if (self.HitTest(p.X, p.Y) is { Kind: HitKind.PinTile, Tile: { } t }) self.Raise(() => self.UnpinRequested?.Invoke(t.ApplicationId));
                return IntPtr.Zero;
            }
            case Native.WM_TIMER when wParam == (IntPtr)(nint)PollTimer: self.Poll(); return IntPtr.Zero;
            case Native.WM_CLOSE: return IntPtr.Zero; // only VEIL closes the dock
        }
        return Native.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private static Native.POINT Point(IntPtr lParam)
    {
        var v = lParam.ToInt64();
        return new Native.POINT { X = (short)(v & 0xFFFF), Y = (short)((v >> 16) & 0xFFFF) }; // signed: the pointer may be outside while captured
    }

    private void MouseDown(Native.POINT p)
    {
        _down = HitTest(p.X, p.Y);
        _downAt = _cursor = p;
        _dragging = false;
        if (_down is not null) Native.SetCapture(_hwnd);
    }

    private void MouseMove(Native.POINT p)
    {
        if (_down is not { Kind: HitKind.Tile or HitKind.PinTile }) return;
        _cursor = p;
        if (!_dragging && (Math.Abs(p.X - _downAt.X) > S(DragThresholdPx) || Math.Abs(p.Y - _downAt.Y) > S(DragThresholdPx))) _dragging = true;
        if (_dragging) Native.InvalidateRect(_hwnd, IntPtr.Zero, false);
    }

    private void MouseUp(Native.POINT p)
    {
        var down = _down;
        var dragged = _dragging;
        _down = null;
        _dragging = false;
        Native.ReleaseCapture();
        Native.InvalidateRect(_hwnd, IntPtr.Zero, false);
        if (down is not { } d) return;

        if (dragged && d.Tile is { } tile)
        {
            if (d.Kind == HitKind.Tile && InBand(p.X, p.Y)) Raise(() => PinRequested?.Invoke(tile.ApplicationId));
            else if (d.Kind == HitKind.PinTile && !InBand(p.X, p.Y)) Raise(() => UnpinRequested?.Invoke(tile.ApplicationId));
            return;
        }
        // A click: the pointer must still be on what it went down on.
        var up = HitTest(p.X, p.Y);
        if (up is null || up.Value.Kind != d.Kind) return;
        switch (d.Kind)
        {
            case HitKind.Tile or HitKind.PinTile when d.Tile is { } t: Raise(() => TileClicked?.Invoke(t.RuntimeId)); break;
            case HitKind.Remove when d.Tile is { } rt: Raise(() => RemoveRequested?.Invoke(rt.ApplicationId)); break;
            case HitKind.Lock: Raise(() => LockRequested?.Invoke()); break;
            case HitKind.Chip: { var tag = d.Tag; Raise(() => WorkspaceRequested?.Invoke(string.IsNullOrEmpty(tag) ? null : tag)); break; }
            case HitKind.Close or HitKind.Expand: Raise(() => ExpandToggleRequested?.Invoke()); break;
        }
    }

    // ---- tray mode: windows thrown onto the dock ----------------------------------------------------------------------------

    /// <summary>
    /// Runs on the window thread every <see cref="PollMs"/> ms in tray mode. A drag starts when the button goes down over the
    /// title bar of another program's window; letting go over the dock hands that window to the command thread. Cheap and
    /// bounded: the caption test is a timed message that gives up on a frozen window.
    /// </summary>
    private void Poll()
    {
        if (!IsVisible) return;
        // the status toast folds away after a few seconds
        if (_lastStatus.Length > 0 && !_state.Expanded && Environment.TickCount64 >= _statusUntil && _width > S(100)) Render(_state);

        var down = Native.GetAsyncKeyState(Native.VK_LBUTTON) < 0;
        Native.GetCursorPos(out var pt);
        Native.GetWindowRect(_hwnd, out var rc);
        // a little slack around the dock: a drop that lands just beside it still counts
        var slack = S(28);
        var over = pt.X >= rc.Left - slack && pt.X < rc.Right + slack && pt.Y >= rc.Top - slack && pt.Y < rc.Bottom + slack;
        var inside = pt.X >= rc.Left && pt.X < rc.Right && pt.Y >= rc.Top && pt.Y < rc.Bottom;
        if (down && !_pollDown)
        {
            _pollDown = true;
            _dragWindow = null;
            _dragRoot = IntPtr.Zero;
            if (!inside) BeginDrag(pt);
        }
        else if (!down && _pollDown)
        {
            _pollDown = false;
            var dropped = _dragWindow is not null && (_dragCaption || WindowMoved()) ? _dragWindow : null;
            var root = _dragRoot;
            var start = _dragStartRect;
            var zoomed = _dragZoomed;
            _dragWindow = null;
            _dragRoot = IntPtr.Zero;
            SetHot(false);
            if (dropped is not null && over)
            {
                // Dropping only adds the app to the tray: the window goes back to where the drag began.
                PutBack(root, start, zoomed);
                Raise(() => WindowDropped?.Invoke(dropped));
            }
            return;
        }
        if (_pollDown) SetHot(_dragWindow is not null && over && (_dragCaption || WindowMoved()));
    }

    private void SetHot(bool hot)
    {
        if (_hot == hot) return;
        _hot = hot;
        Native.InvalidateRect(_hwnd, IntPtr.Zero, false);
    }

    /// <summary>
    /// Remembers the top-level window under the pointer when the button goes down. It counts as "being dragged" if the press
    /// was on its title bar, or - for programs that draw their own title bar - if the window has moved by the time the button
    /// is released. Own windows and frozen windows never count.
    /// </summary>
    private void BeginDrag(Native.POINT pt)
    {
        var hit = Native.WindowFromPoint(pt);
        if (hit == IntPtr.Zero) return;
        var root = Native.GetAncestor(hit, Native.GA_ROOT);
        if (root == IntPtr.Zero || root == _hwnd || Native.IsHungAppWindow(root)) return;
        Native.GetWindowThreadProcessId(root, out var pid);
        if (pid == (uint)Environment.ProcessId) return;
        var id = WindowsWindowService.RuntimeIdFor(root);
        if (id is null) return;
        var lParam = new IntPtr(((long)(pt.Y & 0xFFFF) << 16) | (long)(pt.X & 0xFFFF));
        _dragCaption = Native.SendMessageTimeout(root, Native.WM_NCHITTEST, IntPtr.Zero, lParam, Native.SMTO_ABORTIFHUNG, 100, out var result) != IntPtr.Zero
            && result.ToInt64() == Native.HTCAPTION;
        _dragRoot = root;
        _dragWindow = id;
        Native.GetWindowRect(root, out _dragStartRect);
        _dragZoomed = Native.IsZoomed(root);
    }

    /// <summary>
    /// Puts the window back where it was. Done twice, because the program that owns it is still finishing its own move when the
    /// button comes up. Both calls are asynchronous towards that program, so a slow one cannot hold anything up.
    /// </summary>
    private static void PutBack(IntPtr root, Native.RECT r, bool zoomed)
    {
        void Once()
        {
            if (!Native.IsWindow(root)) return;
            if (zoomed) { Native.ShowWindowAsync(root, Native.SW_MAXIMIZE); return; }
            Native.SetWindowPos(root, IntPtr.Zero, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top,
                Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_ASYNCWINDOWPOS);
        }
        Once();
        _ = Task.Delay(150).ContinueWith(_ => { try { Once(); } catch { /* window gone */ } });
    }

    private bool WindowMoved()
    {
        if (_dragRoot == IntPtr.Zero || !Native.GetWindowRect(_dragRoot, out var now)) return false;
        var t = S(DragThresholdPx);
        return Math.Abs(now.Left - _dragStartRect.Left) > t || Math.Abs(now.Top - _dragStartRect.Top) > t;
    }

    public void Dispose()
    {
        Hide();
        foreach (var icon in _icons.Values) if (icon != IntPtr.Zero) Native.DestroyIcon(icon);
        _icons.Clear();
    }
}
