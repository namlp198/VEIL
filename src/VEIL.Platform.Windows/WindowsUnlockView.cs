using System.Runtime.InteropServices;
using VEIL.Platform.Abstractions;

namespace VEIL.Platform.Windows;

/// <summary>
/// The password form behind the lock and tray icons, drawn in the VEIL HUD style rather than as a Windows dialog: a
/// borderless near-black panel with a violet frame, cyan corner brackets, spaced monospace caps, a masked field that lights
/// up cyan while it has focus, and violet / outlined buttons. Everything is painted from <see cref="Palette"/> (the theme
/// tokens). Enter submits, Esc cancels (the dialog manager on the message thread maps them to IDOK / IDCANCEL). What was
/// typed is read on the window thread, cleared from the box at once and handed to the command thread (ADR-009).
/// </summary>
public sealed class WindowsUnlockView : IUnlockView
{
    private const string ClassName = "VEIL.Unlock";
    private const int IdEdit = 201, IdOk = Native.IDOK, IdCancel = Native.IDCANCEL;
    private const int EnSetFocus = 0x0100, EnKillFocus = 0x0200, BsOwnerDraw = 0x0B;
    private const int FadeMs = 160, FadeStepMs = 16;
    private static readonly UIntPtr FadeTimer = new(1);

    private static readonly Native.WndProc Proc = StaticProc;
    private static WindowsUnlockView? _current;
    private static bool _registered;

    private readonly Win32Thread _thread;
    private IntPtr _hwnd, _edit, _fMono, _fMonoBold, _fSmall, _fField;
    private double _scale = 1.0;
    private string _prompt = string.Empty, _status = string.Empty;
    private Native.RECT _field;
    private long _fadeStart;

    internal WindowsUnlockView(Win32Thread thread) => _thread = thread;

    public event Action<string>? Submitted;
    public event Action? Cancelled;

    public bool IsVisible => _hwnd != IntPtr.Zero && Native.IsWindow(_hwnd);

    private int S(double v) => Palette.Px(v, _scale);

    public void Show(SelectorPlacement placement, string prompt) => _thread.Invoke(() =>
    {
        if (IsVisible) return;
        EnsureClass();
        _scale = placement.Scale > 0 ? placement.Scale : 1.0;
        _prompt = prompt;
        _status = string.Empty;
        int w = S(460), h = S(262);
        var b = placement.Bounds;
        var x = b.X + (b.Width - w) / 2;
        var y = b.Y + (b.Height - h) / 2;
        var hInstance = Native.GetModuleHandle(null);
        var animate = Motion.Enabled;
        _current = this;
        _hwnd = Native.CreateWindowEx(Native.WS_EX_TOPMOST | Native.WS_EX_TOOLWINDOW | (animate ? Native.WS_EX_LAYERED : 0), ClassName, "VEIL password",
            Native.WS_POPUP, x, y, w, h, IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero) { _current = null; throw new InvalidOperationException("Unlock window could not be created."); }
        if (animate) Native.SetLayeredWindowAttributes(_hwnd, 0, 0, Native.LWA_ALPHA);

        var sizes = Palette.Theme.FontSizesPt;
        _fMono = Palette.MonoFont(sizes["caption"], _scale);
        _fMonoBold = Palette.MonoFont(sizes["bodyLarge"], _scale, 600);
        _fSmall = Palette.Font(sizes["caption"], _scale);
        _fField = Palette.MonoFont(sizes["bodyLarge"], _scale);
        void Set(int attr, int value) => Native.DwmSetWindowAttribute(_hwnd, attr, ref value, sizeof(int));
        Set(Native.DWMWA_WINDOW_CORNER_PREFERENCE, 2);
        Set(Native.DWMWA_BORDER_COLOR, (int)Palette.Ref("accent.primary"));

        int m = S(28);
        _field = new Native.RECT { Left = m, Top = S(112), Right = w - m, Bottom = S(112) + S(44) };
        // the edit sits inside the painted field box, with no border of its own
        _edit = Native.CreateWindowEx(0, "EDIT", "", Native.WS_CHILD | Native.WS_VISIBLE | Native.WS_TABSTOP | Native.ES_AUTOHSCROLL | Native.ES_PASSWORD,
            _field.Left + S(14), _field.Top + S(10), _field.Right - _field.Left - S(28), _field.Bottom - _field.Top - S(20), _hwnd, new IntPtr(IdEdit), hInstance, IntPtr.Zero);
        Native.SendMessage(_edit, Native.WM_SETFONT, _fField, new IntPtr(1));
        Native.SendMessage(_edit, 0x00CC /* EM_SETPASSWORDCHAR */, new IntPtr('●'), IntPtr.Zero);

        int bw = S(120), bh = S(38), by = h - S(26) - bh;
        Button("CANCEL", IdCancel, w - m - bw, by, bw, bh);
        Button("UNLOCK", IdOk, w - m - 2 * bw - S(12), by, bw, bh);

        Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_SHOWWINDOW);
        _thread.DialogWindow = _hwnd;
        Native.SetForegroundWindow(_hwnd);
        Native.SetFocus(_edit);
        if (animate)
        {
            _fadeStart = System.Diagnostics.Stopwatch.GetTimestamp();
            Native.SetTimer(_hwnd, FadeTimer, FadeStepMs, IntPtr.Zero);
        }

        void Button(string text, int id, int bx, int byy, int bww, int bhh)
        {
            var uStyle = Native.WS_CHILD | Native.WS_VISIBLE | Native.WS_TABSTOP | (id == IdOk ? Native.BS_DEFPUSHBUTTON : 0u) | BsOwnerDraw;
            Native.CreateWindowEx(0, "BUTTON", text, uStyle, bx, byy, bww, bhh, _hwnd, new IntPtr(id), hInstance, IntPtr.Zero);
        }
    });

    public void SetStatus(string text) => _thread.Invoke(() =>
    {
        if (!IsVisible) return;
        _status = text;
        Native.InvalidateRect(_hwnd, IntPtr.Zero, true);
    });

    public void Hide() => _thread.Invoke(() =>
    {
        if (_hwnd == IntPtr.Zero) return;
        var h = _hwnd;
        _hwnd = IntPtr.Zero;
        _thread.DialogWindow = IntPtr.Zero;
        if (_current == this) _current = null;
        if (Native.IsWindow(h)) Native.DestroyWindow(h);
        foreach (var f in new[] { _fMono, _fMonoBold, _fSmall, _fField }) if (f != IntPtr.Zero) Native.DeleteObject(f);
        _fMono = _fMonoBold = _fSmall = _fField = IntPtr.Zero;
    });

    // ---- painting -----------------------------------------------------------------------------------------------------

    private static string Spaced(string text) => string.Join(' ', text.ToCharArray());

    private void Line(IntPtr hdc, string colour, int x1, int y1, int x2, int y2, int width)
    {
        var pen = Native.CreatePen(Native.PS_SOLID, Math.Max(1, width), Palette.Ref(colour));
        var old = Native.SelectObject(hdc, pen);
        Native.MoveToEx(hdc, x1, y1, IntPtr.Zero);
        Native.LineTo(hdc, x2, y2);
        Native.SelectObject(hdc, old);
        Native.DeleteObject(pen);
    }

    private void Text(IntPtr hdc, string text, int x, int y, int w, int h, IntPtr font, string colour, uint flags = Native.DT_LEFT)
    {
        var r = new Native.RECT { Left = x, Top = y, Right = x + w, Bottom = y + h };
        Native.SelectObject(hdc, font);
        Native.SetTextColor(hdc, Palette.Ref(colour));
        Native.DrawText(hdc, text, -1, ref r, flags | Native.DT_VCENTER | Native.DT_SINGLELINE | Native.DT_NOPREFIX | Native.DT_END_ELLIPSIS);
    }

    private void RoundBox(IntPtr hdc, Native.RECT r, string fill, string border, int penWidth, int radius)
    {
        var pen = Native.CreatePen(Native.PS_SOLID, Math.Max(1, penWidth), Palette.Ref(border));
        var oldPen = Native.SelectObject(hdc, pen);
        var oldBrush = Native.SelectObject(hdc, Palette.Brush(fill));
        Native.RoundRect(hdc, r.Left, r.Top, r.Right, r.Bottom, radius, radius);
        Native.SelectObject(hdc, oldBrush);
        Native.SelectObject(hdc, oldPen);
        Native.DeleteObject(pen);
    }

    private void Paint(IntPtr hdc)
    {
        Native.GetClientRect(_hwnd, out var rc);
        int w = rc.Right, h = rc.Bottom, m = S(28);
        Native.FillRect(hdc, ref rc, Palette.Brush("bg.canvas"));
        Native.SetBkMode(hdc, 1);

        // frame and the four cyan corner brackets of the HUD
        var frame = new Native.RECT { Left = 0, Top = 0, Right = w, Bottom = h };
        var edge = Palette.Brush("border.strong");
        foreach (var e in new[] { new Native.RECT { Left = 0, Top = 0, Right = w, Bottom = 1 }, new Native.RECT { Left = 0, Top = h - 1, Right = w, Bottom = h },
                     new Native.RECT { Left = 0, Top = 0, Right = 1, Bottom = h }, new Native.RECT { Left = w - 1, Top = 0, Right = w, Bottom = h } })
        { var r = e; Native.FillRect(hdc, ref r, edge); }
        int c = S(18), t = Math.Max(2, S(2)), o = S(8);
        Line(hdc, "accent.cyan", o, o, o + c, o, t); Line(hdc, "accent.cyan", o, o, o, o + c, t);
        Line(hdc, "accent.cyan", w - o - 1, o, w - o - 1 - c, o, t); Line(hdc, "accent.cyan", w - o - 1, o, w - o - 1, o + c, t);
        Line(hdc, "accent.cyan", o, h - o - 1, o + c, h - o - 1, t); Line(hdc, "accent.cyan", o, h - o - 1, o, h - o - 1 - c, t);
        Line(hdc, "accent.cyan", w - o - 1, h - o - 1, w - o - 1 - c, h - o - 1, t); Line(hdc, "accent.cyan", w - o - 1, h - o - 1, w - o - 1, h - o - 1 - c, t);

        // heading
        Text(hdc, "VEIL", m, S(22), S(60), S(22), _fMonoBold, "accent.lilac");
        Text(hdc, Spaced("AUTHORIZATION"), m + S(64), S(22), w - 2 * m - S(64), S(22), _fMono, "accent.cyan");
        Line(hdc, "border.subtle", m, S(54), w - m, S(54), 1);
        Text(hdc, _prompt, m, S(62), w - 2 * m, S(24), _fMono, "text.secondary");

        // field: label, then a box that glows cyan while the password box has focus
        Text(hdc, Spaced("KEY"), m, S(90), S(80), S(16), _fMono, "text.muted");
        var focused = Native.GetFocus() == _edit;
        RoundBox(hdc, _field, "bg.raised", focused ? "accent.cyan" : "border.strong", focused ? S(2) : 1, S(10));
        Text(hdc, "›", _field.Left + S(2), _field.Top, S(12), _field.Bottom - _field.Top, _fField, focused ? "accent.cyan" : "text.muted", Native.DT_LEFT);

        // status: errors in the error colour, never colour alone (the words say what happened)
        if (_status.Length > 0) Text(hdc, "! " + _status, m, _field.Bottom + S(10), w - 2 * m, S(22), _fMono, "status.error");
        else Text(hdc, "Enter to confirm  ·  Esc to cancel", m, _field.Bottom + S(10), w - 2 * m, S(22), _fMono, "text.muted");
    }

    private void DrawButton(Native.DRAWITEMSTRUCT d)
    {
        var primary = d.CtlID == IdOk;
        var pressed = (d.itemState & Native.ODS_SELECTED) != 0;
        var focused = (d.itemState & Native.ODS_FOCUS) != 0;
        var r = d.rcItem;
        Native.FillRect(d.hDC, ref r, Palette.Brush("bg.canvas"));
        var fill = primary ? (pressed ? "accent.primaryActive" : focused ? "accent.primaryHover" : "accent.primary") : (pressed ? "bg.raised" : "bg.canvas");
        var border = focused ? "accent.cyan" : primary ? "accent.glow" : "border.strong";
        RoundBox(d.hDC, r, fill, border, focused ? S(2) : 1, S(10));
        Native.SetBkMode(d.hDC, 1);
        Text(d.hDC, Spaced(primary ? "UNLOCK" : "CANCEL"), r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, _fMono, primary ? "text.onAccent" : "text.secondary", Native.DT_CENTER);
    }

    private void OnFade()
    {
        var elapsed = (System.Diagnostics.Stopwatch.GetTimestamp() - _fadeStart) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        var t = Math.Clamp(elapsed / FadeMs, 0, 1);
        var eased = 1 - Math.Pow(1 - t, 3);
        if (Native.IsWindow(_hwnd)) Native.SetLayeredWindowAttributes(_hwnd, 0, (byte)Math.Round(255 * eased), Native.LWA_ALPHA);
        if (t >= 1 && Native.IsWindow(_hwnd)) Native.KillTimer(_hwnd, FadeTimer);
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
            hbrBackground = Palette.Brush("bg.canvas"),
            hCursor = Native.LoadCursor(IntPtr.Zero, new IntPtr(32512)),
            lpszClassName = ClassName,
        };
        if (Native.RegisterClassEx(ref cls) == 0) throw new InvalidOperationException("Unlock window class registration failed.");
        _registered = true;
    }

    private static IntPtr StaticProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        try { return Dispatch(hWnd, msg, wParam, lParam); }
        catch (Exception) { return Native.DefWindowProc(hWnd, msg, wParam, lParam); }
    }

    private static IntPtr Dispatch(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        var self = _current;
        if (self is null || hWnd != self._hwnd) return Native.DefWindowProc(hWnd, msg, wParam, lParam);
        switch (msg)
        {
            case Native.WM_ERASEBKGND: return new IntPtr(1);
            case Native.WM_PAINT:
            {
                var hdc = Native.BeginPaint(hWnd, out var ps);
                try { self.Paint(hdc); }
                finally { Native.EndPaint(hWnd, ref ps); }
                return IntPtr.Zero;
            }
            case Native.WM_DRAWITEM:
            {
                var d = Marshal.PtrToStructure<Native.DRAWITEMSTRUCT>(lParam);
                self.DrawButton(d);
                return new IntPtr(1);
            }
            case Native.WM_COMMAND:
            {
                var id = (int)(wParam.ToInt64() & 0xFFFF);
                var code = (int)((wParam.ToInt64() >> 16) & 0xFFFF);
                if (id == IdEdit && code is EnSetFocus or EnKillFocus) { Native.InvalidateRect(hWnd, IntPtr.Zero, false); return IntPtr.Zero; }
                if (id == IdCancel && code == Native.BN_CLICKED) { self._thread.Post(() => self.Cancelled?.Invoke()); return IntPtr.Zero; }
                if (id == IdOk && code == Native.BN_CLICKED)
                {
                    var text = ReadAndClear(self._edit);
                    self._thread.Post(() => self.Submitted?.Invoke(text));
                    return IntPtr.Zero;
                }
                break;
            }
            case Native.WM_CTLCOLOREDIT:
                Native.SetTextColor(wParam, Palette.Ref("text.primary"));
                Native.SetBkColor(wParam, Palette.Ref("bg.raised"));
                return Palette.Brush("bg.raised");
            case Native.WM_TIMER when wParam == (IntPtr)(nint)FadeTimer:
                self.OnFade();
                return IntPtr.Zero;
            case Native.WM_CLOSE:
                self._thread.Post(() => self.Cancelled?.Invoke());
                return IntPtr.Zero;
        }
        return Native.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private static string ReadAndClear(IntPtr edit)
    {
        var len = Native.GetWindowTextLength(edit);
        var text = string.Empty;
        if (len > 0)
        {
            var sb = new System.Text.StringBuilder(len + 1);
            Native.GetWindowText(edit, sb, sb.Capacity);
            text = sb.ToString();
        }
        Native.SetWindowText(edit, string.Empty); // never leave the password sitting in the box
        return text;
    }

    public void Dispose() => Hide();
}
