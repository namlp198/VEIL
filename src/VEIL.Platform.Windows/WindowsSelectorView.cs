using System.Runtime.InteropServices;
using VEIL.Domain.Theme;
using VEIL.Platform.Abstractions;

namespace VEIL.Platform.Windows;

/// <summary>
/// Native Window Selector styled from <see cref="VeilTheme"/> (dark surface, violet/cyan accents, status pills). A topmost
/// popup with a text box, an owner-drawn list and an "only allowed" checkbox that takes real keyboard focus.
/// Tab/Shift+Tab/Enter/Esc come from the dialog manager (<c>IsDialogMessage</c> on the message thread), digits 1-9 from
/// the list's <c>WM_VKEYTOITEM</c>. No global key capture, so input methods work normally. FR-SEL-001..005.
/// Input is read on the window thread but handed to the command thread before any logic runs (ADR-009); the window fades
/// in over about 160 ms unless motion is reduced.
/// </summary>
public sealed class WindowsSelectorView : ISelectorView
{
    private const string ClassName = "VEIL.Selector";
    private const int IdEdit = 101, IdList = 102, IdCheck = 103, IdStatus = 104;
    private const int FadeMs = 160, FadeStepMs = 16;
    private static readonly UIntPtr FadeTimer = new(1);

    private static readonly Native.WndProc Proc = StaticProc;
    private static WindowsSelectorView? _current; // only one selector window can exist
    private static bool _registered;

    private readonly Win32Thread _thread;
    private IntPtr _hwnd, _edit, _list, _check, _status;
    private bool _updating;
    private double _scale = 1.0;
    private IReadOnlyList<SelectorRow> _rows = [];
    private IntPtr _fBody, _fTitle, _fSmall, _fPill;
    private long _fadeStart;

    internal WindowsSelectorView(Win32Thread thread) => _thread = thread;

    public event Action<string>? FilterChanged;
    public event Action<int>? SelectionChanged;
    public event Action<bool>? OnlyAllowedChanged;
    public event Action? ConfirmRequested;
    public event Action? CancelRequested;
    public event Action<int>? NumberPressed;

    public bool IsVisible => _hwnd != IntPtr.Zero && Native.IsWindow(_hwnd);

    private int S(double designPixels) => Palette.Px(designPixels, _scale);
    private static double RowHeight => Palette.Theme.Component.GetValueOrDefault("selectorRowHeight", 52);

    public void Show(SelectorViewState state, SelectorPlacement placement) => _thread.Invoke(() =>
    {
        if (IsVisible) { Render(state); return; }
        EnsureClass();
        _scale = placement.Scale > 0 ? placement.Scale : 1.0;
        var b = placement.Bounds;
        var hInstance = Native.GetModuleHandle(null);
        _current = this;
        var animate = Motion.Enabled;
        _hwnd = Native.CreateWindowEx(Native.WS_EX_TOPMOST | Native.WS_EX_TOOLWINDOW | (animate ? Native.WS_EX_LAYERED : 0), ClassName, "VEIL Window Selector",
            Native.WS_POPUP | Native.WS_CAPTION | Native.WS_SYSMENU | Native.WS_BORDER,
            b.X, b.Y, b.Width, b.Height, IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero) { _current = null; throw new InvalidOperationException("Selector window could not be created."); }
        if (animate) Native.SetLayeredWindowAttributes(_hwnd, 0, 0, Native.LWA_ALPHA);

        ApplyChrome();
        var sizes = Palette.Theme.FontSizesPt;
        _fBody = Palette.Font(sizes["body"], _scale);
        _fTitle = Palette.Font(sizes["body"], _scale, 600);
        _fSmall = Palette.Font(sizes["caption"], _scale);
        _fPill = Palette.Font(sizes["caption"], _scale, 600);

        IntPtr Child(string cls, string text, uint style, int id) =>
            Native.CreateWindowEx(0, cls, text, Native.WS_CHILD | Native.WS_VISIBLE | style, 0, 0, 10, 10, _hwnd, new IntPtr(id), hInstance, IntPtr.Zero);

        _edit = Child("EDIT", "", Native.WS_TABSTOP | Native.WS_BORDER | Native.ES_AUTOHSCROLL, IdEdit);
        _list = Child("LISTBOX", "", Native.WS_TABSTOP | Native.WS_BORDER | Native.WS_VSCROLL | Native.LBS_NOTIFY | Native.LBS_NOINTEGRALHEIGHT
            | Native.LBS_WANTKEYBOARDINPUT | Native.LBS_OWNERDRAWFIXED | Native.LBS_HASSTRINGS, IdList);
        _check = Child("BUTTON", "Show only allowed windows", Native.WS_TABSTOP | Native.BS_AUTOCHECKBOX, IdCheck);
        _status = Child("STATIC", "", 0, IdStatus);
        foreach (var h in new[] { _edit, _check, _list }) Native.SendMessage(h, Native.WM_SETFONT, _fBody, new IntPtr(1));
        Native.SendMessage(_status, Native.WM_SETFONT, _fSmall, new IntPtr(1));
        Native.SendMessage(_list, Native.LB_SETITEMHEIGHT, IntPtr.Zero, new IntPtr(S(RowHeight)));
        // Ask Windows for its dark control variants (scrollbars, edit, checkbox) where available; harmless otherwise.
        foreach (var h in new[] { _edit, _list, _check }) Native.SetWindowTheme(h, "DarkMode_Explorer", null);
        Layout();

        Render(state);
        // Not ShowWindow: a process's first ShowWindow call takes its show state from STARTUPINFO, so a process started
        // hidden (a scheduled task, auto-start) would never display the selector. SWP_SHOWWINDOW has no such quirk.
        Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_SHOWWINDOW);
        _thread.DialogWindow = _hwnd;
        TakeForeground();
        Native.SetFocus(_edit);
        if (animate)
        {
            _fadeStart = System.Diagnostics.Stopwatch.GetTimestamp();
            Native.SetTimer(_hwnd, FadeTimer, FadeStepMs, IntPtr.Zero);
        }
    });

    public void Update(SelectorViewState state) => _thread.Invoke(() => { if (IsVisible) Render(state); });

    public void Hide() => _thread.Invoke(() =>
    {
        if (_hwnd == IntPtr.Zero) return;
        var h = _hwnd;
        _hwnd = IntPtr.Zero;
        _thread.DialogWindow = IntPtr.Zero;
        if (_current == this) _current = null;
        if (Native.IsWindow(h)) Native.DestroyWindow(h);
        foreach (var f in new[] { _fBody, _fTitle, _fSmall, _fPill }) if (f != IntPtr.Zero) Native.DeleteObject(f);
        _fBody = _fTitle = _fSmall = _fPill = IntPtr.Zero;
        _rows = [];
    });

    /// <summary>Dark title bar, themed caption/border colours and rounded corners (Windows 11; ignored where unsupported).</summary>
    private void ApplyChrome()
    {
        void Set(int attr, int value) => Native.DwmSetWindowAttribute(_hwnd, attr, ref value, sizeof(int));
        Set(Native.DWMWA_USE_IMMERSIVE_DARK_MODE, 1);
        Set(Native.DWMWA_CAPTION_COLOR, (int)Palette.Ref("bg.surface"));
        Set(Native.DWMWA_TEXT_COLOR, (int)Palette.Ref("text.primary"));
        Set(Native.DWMWA_BORDER_COLOR, (int)Palette.Ref("border.strong"));
        Set(Native.DWMWA_WINDOW_CORNER_PREFERENCE, 2 /* DWMWCP_ROUND */);
    }

    private void Layout()
    {
        Native.GetClientRect(_hwnd, out var rc);
        var m = S(Palette.Theme.Spacing["md"]);
        var gap = S(Palette.Theme.Spacing["sm"]);
        var w = Math.Max(1, rc.Right - rc.Left - 2 * m);
        var h = rc.Bottom - rc.Top;
        var editH = S(32);
        var checkH = S(24);
        var statusH = S(20);
        var top = m + editH + gap + checkH + gap;
        Native.MoveWindow(_edit, m, m, w, editH, true);
        Native.MoveWindow(_check, m, m + editH + gap, w, checkH, true);
        Native.MoveWindow(_list, m, top, w, Math.Max(S(40), h - top - statusH - 2 * m), true);
        Native.MoveWindow(_status, m, h - statusH - m, w, statusH, true);
    }

    private void Render(SelectorViewState s)
    {
        _updating = true;
        try
        {
            _rows = s.Rows;
            // Do not disturb the caret while the user is typing the same text.
            if (Text(_edit) != s.Filter)
            {
                Native.SetWindowText(_edit, s.Filter);
                Native.SendMessage(_edit, Native.EM_SETSEL, new IntPtr(s.Filter.Length), new IntPtr(s.Filter.Length));
            }
            Native.SendMessage(_check, Native.BM_SETCHECK, new IntPtr(s.OnlyAllowed ? 1 : 0), IntPtr.Zero);
            Native.SendMessage(_list, Native.LB_RESETCONTENT, IntPtr.Zero, IntPtr.Zero);
            foreach (var row in s.Rows) Native.SendMessageText(_list, Native.LB_ADDSTRING, IntPtr.Zero, row.Text);
            if (s.Rows.Count > 0)
                Native.SendMessage(_list, Native.LB_SETCURSEL, new IntPtr(Math.Clamp(s.SelectedIndex, 0, s.Rows.Count - 1)), IntPtr.Zero);
            Native.SetWindowText(_status, s.Rows.Count == 0 ? "No windows match. " + s.Status : s.Status);
            Native.InvalidateRect(_hwnd, IntPtr.Zero, true);
        }
        finally { _updating = false; }
    }

    private static string Text(IntPtr h)
    {
        var len = Native.GetWindowTextLength(h);
        if (len == 0) return string.Empty;
        var sb = new System.Text.StringBuilder(len + 1);
        Native.GetWindowText(h, sb, sb.Capacity);
        return sb.ToString();
    }

    /// <summary>
    /// The hotkey gives this process the right to activate; attaching to the foreground thread is only a fallback. Two
    /// input queues that are attached are shared, so attaching to a frozen thread would freeze keyboard and mouse for this
    /// process too. It is therefore skipped when the foreground window is not responding, and always undone.
    /// </summary>
    private void TakeForeground()
    {
        if (Native.SetForegroundWindow(_hwnd)) return;
        var fg = Native.GetForegroundWindow();
        if (fg == IntPtr.Zero || Native.IsHungAppWindow(fg)) return;
        var fgThread = Native.GetWindowThreadProcessId(fg, out _);
        var me = Native.GetCurrentThreadId();
        if (fgThread == 0 || fgThread == me || !Native.AttachThreadInput(me, fgThread, true)) return;
        try { Native.SetForegroundWindow(_hwnd); }
        finally { Native.AttachThreadInput(me, fgThread, false); }
    }

    private void OnFadeTimer()
    {
        var elapsed = (System.Diagnostics.Stopwatch.GetTimestamp() - _fadeStart) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        var t = Math.Clamp(elapsed / FadeMs, 0, 1);
        var eased = 1 - Math.Pow(1 - t, 3); // ease-out: quick start, soft landing
        if (Native.IsWindow(_hwnd)) Native.SetLayeredWindowAttributes(_hwnd, 0, (byte)Math.Round(255 * eased), Native.LWA_ALPHA);
        if (t >= 1 && Native.IsWindow(_hwnd)) Native.KillTimer(_hwnd, FadeTimer);
    }

    // ---- painting -----------------------------------------------------------------------------------------------------

    private void DrawRow(Native.DRAWITEMSTRUCT d)
    {
        var rc = d.rcItem;
        var hdc = d.hDC;
        if (d.itemID == uint.MaxValue || d.itemID >= _rows.Count)
        {
            Native.FillRect(hdc, ref rc, Palette.Brush("bg.surface"));
            return;
        }
        var row = _rows[(int)d.itemID];
        var selected = (d.itemState & Native.ODS_SELECTED) != 0;
        var listFocused = Native.GetFocus() == _list;

        Native.FillRect(hdc, ref rc, Palette.Brush(selected ? "bg.raised" : "bg.surface"));
        if (selected)
        {
            var bar = new Native.RECT { Left = rc.Left, Top = rc.Top + S(6), Right = rc.Left + Math.Max(2, S(3)), Bottom = rc.Bottom - S(6) };
            Native.FillRect(hdc, ref bar, Palette.Brush(listFocused ? "accent.cyan" : "accent.primary"));
        }
        var line = new Native.RECT { Left = rc.Left + S(10), Top = rc.Bottom - 1, Right = rc.Right - S(10), Bottom = rc.Bottom };
        Native.FillRect(hdc, ref line, Palette.Brush("border.subtle"));
        Native.SetBkMode(hdc, 1);

        var pad = S(10);
        var x = rc.Left + pad + S(4);

        // number chip (shortcut 1-9)
        var chip = S(26);
        if (row.Number <= 9)
        {
            var cy = (rc.Top + rc.Bottom) / 2;
            var chipRc = new Native.RECT { Left = x, Top = cy - chip / 2, Right = x + chip, Bottom = cy + chip / 2 };
            var oldPen = Native.SelectObject(hdc, Native.GetStockObject(Native.NULL_PEN));
            var oldBrush = Native.SelectObject(hdc, Palette.Brush("border.subtle"));
            Native.RoundRect(hdc, chipRc.Left, chipRc.Top, chipRc.Right, chipRc.Bottom, S(8), S(8));
            Native.SelectObject(hdc, oldBrush);
            Native.SelectObject(hdc, oldPen);
            Native.SelectObject(hdc, _fSmall);
            Native.SetTextColor(hdc, Palette.Ref("text.secondary"));
            Native.DrawText(hdc, row.Number.ToString(System.Globalization.CultureInfo.InvariantCulture), -1, ref chipRc,
                Native.DT_CENTER | Native.DT_VCENTER | Native.DT_SINGLELINE | Native.DT_NOPREFIX);
        }
        x += chip + pad;

        // status pills, right-aligned: [policy] [state]. Words, not colour alone (UI/UX 8).
        var pillH = S(Palette.Theme.Component.GetValueOrDefault("pillHeight", 20));
        var right = rc.Right - pad;
        var cyMid = (rc.Top + rc.Bottom) / 2;
        Native.SelectObject(hdc, _fPill);
        var (stateFg, stateBg) = StateColors(row.State);
        right = DrawPill(hdc, right, cyMid, pillH, string.IsNullOrEmpty(row.State) ? "Hidden" : row.State, stateFg, stateBg) - S(6);
        right = DrawPill(hdc, right, cyMid, pillH, row.IsAllowed ? "Allowed" : "Not allowed",
            row.IsAllowed ? "status.success" : "text.secondary", row.IsAllowed ? "status.successBg" : "status.neutralBg") - pad;

        // title + application
        var textW = Math.Max(10, right - x);
        var titleRc = new Native.RECT { Left = x, Top = rc.Top + S(8), Right = x + textW, Bottom = rc.Top + S(28) };
        Native.SelectObject(hdc, _fTitle);
        Native.SetTextColor(hdc, Palette.Ref(row.IsAllowed ? "text.primary" : "text.secondary"));
        var title = string.IsNullOrEmpty(row.Title) ? row.Text : row.Title;
        Native.DrawText(hdc, title, -1, ref titleRc, Native.DT_LEFT | Native.DT_VCENTER | Native.DT_SINGLELINE | Native.DT_NOPREFIX | Native.DT_END_ELLIPSIS);

        var subRc = new Native.RECT { Left = x, Top = rc.Top + S(29), Right = x + textW, Bottom = rc.Bottom - S(6) };
        Native.SelectObject(hdc, _fSmall);
        Native.SetTextColor(hdc, Palette.Ref("text.muted"));
        Native.DrawText(hdc, string.IsNullOrEmpty(row.Application) ? " " : row.Application, -1, ref subRc,
            Native.DT_LEFT | Native.DT_VCENTER | Native.DT_SINGLELINE | Native.DT_NOPREFIX | Native.DT_END_ELLIPSIS);
    }

    private static (string Fg, string Bg) StateColors(string state) => state switch
    {
        "Visible" => ("status.info", "status.infoBg"),
        "Minimized" => ("status.warning", "status.warningBg"),
        _ => ("text.secondary", "status.neutralBg"), // Hidden / unknown
    };

    /// <summary>Draws a rounded status pill whose right edge is at <paramref name="right"/>; returns its left edge.</summary>
    private int DrawPill(IntPtr hdc, int right, int cy, int height, string text, string fgToken, string bgToken)
    {
        var measure = new Native.RECT();
        Native.DrawText(hdc, text, -1, ref measure, Native.DT_CALCRECT | Native.DT_SINGLELINE | Native.DT_NOPREFIX);
        var padX = S(10);
        var width = (measure.Right - measure.Left) + 2 * padX;
        var rc = new Native.RECT { Left = right - width, Top = cy - height / 2, Right = right, Bottom = cy + height / 2 };
        var oldPen = Native.SelectObject(hdc, Native.GetStockObject(Native.NULL_PEN));
        var oldBrush = Native.SelectObject(hdc, Palette.Brush(bgToken));
        Native.RoundRect(hdc, rc.Left, rc.Top, rc.Right, rc.Bottom, height, height);
        Native.SelectObject(hdc, oldBrush);
        Native.SelectObject(hdc, oldPen);
        Native.SetTextColor(hdc, Palette.Ref(fgToken));
        Native.DrawText(hdc, text, -1, ref rc, Native.DT_CENTER | Native.DT_VCENTER | Native.DT_SINGLELINE | Native.DT_NOPREFIX);
        return rc.Left;
    }

    // ---- window class and messages ------------------------------------------------------------------------------------

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
        if (Native.RegisterClassEx(ref cls) == 0) throw new InvalidOperationException("Selector window class registration failed.");
        _registered = true;
    }

    /// <summary>Hands work to the ordered command thread, so no handler logic ever runs on the window thread.</summary>
    private void Raise(Action work) => _thread.Post(work);

    private static IntPtr StaticProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        try { return Dispatch(hWnd, msg, wParam, lParam); }
        catch (Exception) { return Native.DefWindowProc(hWnd, msg, wParam, lParam); } // never let an exception unwind into native code
    }

    private static IntPtr Dispatch(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        var self = _current;
        if (self is null || hWnd != self._hwnd) return Native.DefWindowProc(hWnd, msg, wParam, lParam);
        switch (msg)
        {
            case Native.WM_COMMAND:
            {
                var id = (int)(wParam.ToInt64() & 0xFFFF);
                var code = (int)((wParam.ToInt64() >> 16) & 0xFFFF);
                // Read what is needed here, on the window thread, then let the command thread do the work.
                if (id == Native.IDCANCEL) { self.Raise(() => self.CancelRequested?.Invoke()); return IntPtr.Zero; }
                if (id == Native.IDOK) { self.Raise(() => self.ConfirmRequested?.Invoke()); return IntPtr.Zero; }
                if (self._updating) return IntPtr.Zero;
                if (id == IdEdit && code == Native.EN_CHANGE) { var text = Text(self._edit); self.Raise(() => self.FilterChanged?.Invoke(text)); }
                else if (id == IdList && code == Native.LBN_SELCHANGE)
                {
                    var index = (int)Native.SendMessage(self._list, Native.LB_GETCURSEL, IntPtr.Zero, IntPtr.Zero);
                    self.Raise(() => self.SelectionChanged?.Invoke(index));
                    Native.InvalidateRect(self._list, IntPtr.Zero, true);
                }
                else if (id == IdList && code == Native.LBN_DBLCLK) self.Raise(() => self.ConfirmRequested?.Invoke());
                else if (id == IdCheck && code == Native.BN_CLICKED)
                {
                    var on = Native.SendMessage(self._check, Native.BM_GETCHECK, IntPtr.Zero, IntPtr.Zero) != IntPtr.Zero;
                    self.Raise(() => self.OnlyAllowedChanged?.Invoke(on));
                }
                return IntPtr.Zero;
            }
            case Native.WM_DRAWITEM:
            {
                var d = Marshal.PtrToStructure<Native.DRAWITEMSTRUCT>(lParam);
                if (d.CtlID == IdList) { self.DrawRow(d); return new IntPtr(1); }
                break;
            }
            case Native.WM_CTLCOLOREDIT:
                Native.SetTextColor(wParam, Palette.Ref("text.primary"));
                Native.SetBkColor(wParam, Palette.Ref("bg.raised"));
                return Palette.Brush("bg.raised");
            case Native.WM_CTLCOLORLISTBOX:
                return Palette.Brush("bg.surface");
            case Native.WM_CTLCOLORSTATIC:
            case Native.WM_CTLCOLORBTN:
                Native.SetTextColor(wParam, Palette.Ref(lParam == self._status ? "text.muted" : "text.secondary"));
                Native.SetBkColor(wParam, Palette.Ref("bg.surface"));
                return Palette.Brush("bg.surface");
            case Native.WM_VKEYTOITEM:
            {
                var vk = (int)(wParam.ToInt64() & 0xFFFF);
                if (vk is >= 0x31 and <= 0x39) { var n = vk - 0x30; self.Raise(() => self.NumberPressed?.Invoke(n)); return new IntPtr(-2); }
                if (vk == 0x0D) { self.Raise(() => self.ConfirmRequested?.Invoke()); return new IntPtr(-2); }
                return new IntPtr(-1); // default list behaviour (arrows)
            }
            case Native.WM_TIMER when wParam == (IntPtr)(nint)FadeTimer:
                self.OnFadeTimer();
                return IntPtr.Zero;
            case Native.WM_CLOSE:
                self.Raise(() => self.CancelRequested?.Invoke());
                return IntPtr.Zero;
        }
        return Native.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    public void Dispose() => Hide();
}
