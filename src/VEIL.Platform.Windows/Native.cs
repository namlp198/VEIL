using System.Runtime.InteropServices;
using System.Text;

namespace VEIL.Platform.Windows;

/// <summary>Minimal Win32 surface used by the Windows adapter. Handles are transient; always validate before use.</summary>
internal static class Native
{
    public delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data);
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    public const uint WM_DESTROY = 0x0002, WM_PAINT = 0x000F, WM_CLOSE = 0x0010, WM_QUIT = 0x0012, WM_ERASEBKGND = 0x0014,
        WM_DISPLAYCHANGE = 0x007E, WM_HOTKEY = 0x0312, WM_APP = 0x8000;
    public const uint WS_POPUP = 0x80000000;
    public const uint WS_EX_TOPMOST = 0x8, WS_EX_TOOLWINDOW = 0x80, WS_EX_APPWINDOW = 0x40000, WS_EX_NOACTIVATE = 0x08000000;
    public const int GWL_EXSTYLE = -20, GW_OWNER = 4;
    public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;
    public static readonly IntPtr HWND_TOPMOST = new(-1), HWND_NOTOPMOST = new(-2);
    public const int SW_SHOWNOACTIVATE = 4, SW_RESTORE = 9;
    public const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4, MOD_WIN = 8, MOD_NOREPEAT = 0x4000;
    public const uint MONITORINFOF_PRIMARY = 1;
    public const int MDT_EFFECTIVE_DPI = 0;
    public const uint MONITOR_DEFAULTTONEAREST = 2;
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    public const int DWMWA_CLOAKED = 14;
    public const int ERROR_HOTKEY_ALREADY_REGISTERED = 1409;
    public const uint DT_CENTER = 1, DT_VCENTER = 4, DT_SINGLELINE = 0x20;
    public static readonly IntPtr DPI_AWARENESS_PER_MONITOR_V2 = new(-4);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public POINT pt; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFOEX
    {
        public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WNDCLASSEX
    {
        public uint cbSize, style; public IntPtr lpfnWndProc; public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        public string? lpszMenuName; public string lpszClassName; public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PAINTSTRUCT
    {
        public IntPtr hdc; public bool fErase; public RECT rcPaint; public bool fRestore, fIncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] rgbReserved;
    }

    private const string User32 = "user32.dll", Kernel32 = "kernel32.dll", Gdi32 = "gdi32.dll";

    [DllImport(User32)] public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);
    [DllImport(User32, CharSet = CharSet.Unicode)] public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX info);
    [DllImport(User32)] public static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);
    [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(IntPtr hMonitor, int type, out uint dpiX, out uint dpiY);
    [DllImport(User32)] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    [DllImport(User32)] public static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
    [DllImport(User32)] public static extern bool IsWindow(IntPtr hWnd);
    [DllImport(User32)] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport(User32)] public static extern bool IsIconic(IntPtr hWnd);
    [DllImport(User32)] public static extern IntPtr GetWindow(IntPtr hWnd, int cmd);
    [DllImport(User32, CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int max);
    [DllImport(User32)] public static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport(User32)] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport(User32)] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport(User32, EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);
    [DllImport(User32)] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport(User32)] public static extern bool ShowWindow(IntPtr hWnd, int cmd);
    [DllImport(User32)] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, out int value, int size);

    [DllImport(Kernel32)] public static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport(Kernel32)] public static extern bool CloseHandle(IntPtr handle);
    [DllImport(Kernel32, CharSet = CharSet.Unicode)] public static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder name, ref int size);
    [DllImport(Kernel32, CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string? name);
    [DllImport(Kernel32)] public static extern uint GetCurrentThreadId();

    [DllImport(User32, CharSet = CharSet.Unicode)] public static extern ushort RegisterClassEx(ref WNDCLASSEX cls);
    [DllImport(User32, CharSet = CharSet.Unicode)] public static extern bool UnregisterClass(string name, IntPtr hInstance);
    [DllImport(User32, CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateWindowEx(uint exStyle, string cls, string title, uint style, int x, int y, int w, int h,
        IntPtr parent, IntPtr menu, IntPtr hInstance, IntPtr param);
    [DllImport(User32)] public static extern bool DestroyWindow(IntPtr hWnd);
    [DllImport(User32, CharSet = CharSet.Unicode)] public static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport(User32)] public static extern int GetMessage(out MSG msg, IntPtr hWnd, uint min, uint max);
    [DllImport(User32)] public static extern bool TranslateMessage(ref MSG msg);
    [DllImport(User32)] public static extern IntPtr DispatchMessage(ref MSG msg);
    [DllImport(User32)] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport(User32)] public static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport(User32, SetLastError = true)] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);
    [DllImport(User32)] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport(User32)] public static extern IntPtr LoadCursor(IntPtr hInstance, IntPtr name);
    [DllImport(User32)] public static extern bool InvalidateRect(IntPtr hWnd, IntPtr rect, bool erase);
    [DllImport(User32)] public static extern IntPtr BeginPaint(IntPtr hWnd, out PAINTSTRUCT ps);
    [DllImport(User32)] public static extern bool EndPaint(IntPtr hWnd, ref PAINTSTRUCT ps);
    [DllImport(User32)] public static extern bool GetClientRect(IntPtr hWnd, out RECT rect);
    [DllImport(User32, CharSet = CharSet.Unicode)] public static extern int DrawText(IntPtr hdc, string text, int len, ref RECT rect, uint format);
    [DllImport(Gdi32)] public static extern IntPtr CreateSolidBrush(uint color);
    [DllImport(Gdi32)] public static extern bool DeleteObject(IntPtr obj);
    [DllImport(Gdi32)] public static extern int SetBkMode(IntPtr hdc, int mode);
    [DllImport(Gdi32)] public static extern uint SetTextColor(IntPtr hdc, uint color);
    [DllImport(User32)] public static extern int FillRect(IntPtr hdc, ref RECT rect, IntPtr brush);

    [DllImport(User32)] public static extern bool IsDialogMessage(IntPtr dlg, ref MSG msg);
    [DllImport(User32)] public static extern IntPtr SetFocus(IntPtr hWnd);
    [DllImport(User32)] public static extern int GetSystemMetrics(int index);
    [DllImport(User32)] public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport(User32, CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")] public static extern IntPtr SendMessageText(IntPtr hWnd, uint msg, IntPtr wParam, string lParam);
    [DllImport(User32, CharSet = CharSet.Unicode)] public static extern bool SetWindowText(IntPtr hWnd, string text);
    [DllImport(User32)] public static extern bool MoveWindow(IntPtr hWnd, int x, int y, int w, int h, bool repaint);
    [DllImport(User32)] public static extern IntPtr GetForegroundWindow();
    [DllImport(User32)] public static extern bool AttachThreadInput(uint from, uint to, bool attach);
    [DllImport(Gdi32)] public static extern IntPtr GetStockObject(int index);

    public const uint WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000, WS_TABSTOP = 0x00010000, WS_BORDER = 0x00800000,
        WS_VSCROLL = 0x00200000, WS_CAPTION = 0x00C00000, WS_SYSMENU = 0x00080000;
    public const uint ES_AUTOHSCROLL = 0x80, LBS_NOTIFY = 0x1, LBS_NOINTEGRALHEIGHT = 0x100, LBS_WANTKEYBOARDINPUT = 0x400, BS_AUTOCHECKBOX = 0x3;
    public const uint WM_COMMAND = 0x0111, WM_SETFONT = 0x30, WM_VKEYTOITEM = 0x002E,
        EM_SETSEL = 0xB1, LB_ADDSTRING = 0x180, LB_RESETCONTENT = 0x184, LB_SETCURSEL = 0x186, LB_GETCURSEL = 0x188,
        BM_GETCHECK = 0xF0, BM_SETCHECK = 0xF1;
    public const int EN_CHANGE = 0x0300, LBN_SELCHANGE = 1, LBN_DBLCLK = 2, BN_CLICKED = 0, IDOK = 1, IDCANCEL = 2,
        SM_CXSCREEN = 0, SM_CYSCREEN = 1, DEFAULT_GUI_FONT = 17;

    [DllImport(User32)] public static extern bool GetCursorPos(out POINT p);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr CreateFont(int h, int w, int esc, int ori, int weight, uint italic, uint underline, uint strike,
        uint charset, uint outPrecision, uint clipPrecision, uint quality, uint pitchFamily, string face);

    [DllImport(Gdi32)] public static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);

    [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hWnd, int attr, ref int value, int size);
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] public static extern int SetWindowTheme(IntPtr hWnd, string? subApp, string? subIdList);
    [DllImport(Gdi32)] public static extern IntPtr CreatePen(int style, int width, uint color);
    [DllImport(Gdi32)] public static extern bool RoundRect(IntPtr hdc, int l, int t, int r, int b, int w, int h);
    [DllImport(Gdi32)] public static extern bool Ellipse(IntPtr hdc, int l, int t, int r, int b);
    [DllImport(Gdi32)] public static extern bool MoveToEx(IntPtr hdc, int x, int y, IntPtr prev);
    [DllImport(Gdi32)] public static extern bool LineTo(IntPtr hdc, int x, int y);
    [DllImport(Gdi32)] public static extern uint SetBkColor(IntPtr hdc, uint color);

    [StructLayout(LayoutKind.Sequential)]
    public struct DRAWITEMSTRUCT
    {
        public uint CtlType, CtlID, itemID, itemAction, itemState;
        public IntPtr hwndItem, hDC;
        public RECT rcItem;
        public UIntPtr itemData;
    }

    public const uint WM_DRAWITEM = 0x002B, WM_CTLCOLOREDIT = 0x0133, WM_CTLCOLORLISTBOX = 0x0134, WM_CTLCOLORBTN = 0x0135,
        WM_CTLCOLORSTATIC = 0x0138, LB_SETITEMHEIGHT = 0x01A0, LBS_OWNERDRAWFIXED = 0x10, LBS_HASSTRINGS = 0x40;
    public const uint ODS_SELECTED = 1, ODS_FOCUS = 0x10;
    public const uint DT_LEFT = 0, DT_RIGHT = 2, DT_CALCRECT = 0x400, DT_NOPREFIX = 0x800, DT_END_ELLIPSIS = 0x8000;
    public const int PS_SOLID = 0, PS_DOT = 2, NULL_BRUSH = 5, NULL_PEN = 8;
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20, DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWA_BORDER_COLOR = 34,
        DWMWA_CAPTION_COLOR = 35, DWMWA_TEXT_COLOR = 36;

    [DllImport(User32)] public static extern IntPtr GetFocus();

    public static uint Rgb(byte r, byte g, byte b) => (uint)(r | (g << 8) | (b << 16));

    // ---- calls into other processes must be bounded or asynchronous (ADR-009) ----------------------------------------

    public const uint SWP_ASYNCWINDOWPOS = 0x4000;
    public const uint SMTO_BLOCK = 0x1, SMTO_ABORTIFHUNG = 0x2;
    public const uint WM_GETTEXT = 0x000D, WM_GETTEXTLENGTH = 0x000E, WM_TIMER = 0x0113;

    /// <summary>True when the window's thread has not processed messages for about five seconds.</summary>
    [DllImport(User32)] public static extern bool IsHungAppWindow(IntPtr hWnd);

    [DllImport(User32, SetLastError = true)]
    public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeoutMs, out IntPtr result);

    [DllImport(User32, CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SendMessageTimeoutW")]
    public static extern IntPtr SendMessageTimeoutText(IntPtr hWnd, uint msg, IntPtr wParam, StringBuilder lParam, uint flags, uint timeoutMs, out IntPtr result);

    /// <summary>Posts the request to the window's thread instead of waiting for it.</summary>
    [DllImport(User32)] public static extern bool ShowWindowAsync(IntPtr hWnd, int cmd);

    // ---- gentle motion: timers, fade-in, double-buffered painting -----------------------------------------------------

    public const uint WS_EX_LAYERED = 0x80000, LWA_ALPHA = 0x2;
    public const int SRCCOPY = 0x00CC0020, AD_CLOCKWISE = 2, SPI_GETCLIENTAREAANIMATION = 0x1042;

    [DllImport(User32)] public static extern UIntPtr SetTimer(IntPtr hWnd, UIntPtr id, uint ms, IntPtr proc);
    [DllImport(User32)] public static extern bool KillTimer(IntPtr hWnd, UIntPtr id);
    [DllImport(User32)] public static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint colorKey, byte alpha, uint flags);
    [DllImport(User32)] public static extern bool InvalidateRect(IntPtr hWnd, ref RECT rect, bool erase);
    [DllImport(User32)] public static extern bool SystemParametersInfo(uint action, uint param, out int value, uint winIni);
    [DllImport(User32)] public static extern bool MessageBeep(uint type);

    [DllImport(Gdi32)] public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport(Gdi32)] public static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);
    [DllImport(Gdi32)] public static extern bool DeleteDC(IntPtr hdc);
    [DllImport(Gdi32)] public static extern bool BitBlt(IntPtr dest, int x, int y, int w, int h, IntPtr src, int sx, int sy, int rop);
    [DllImport(Gdi32)] public static extern bool SetViewportOrgEx(IntPtr hdc, int x, int y, IntPtr prev);
    [DllImport(Gdi32)] public static extern int SetArcDirection(IntPtr hdc, int direction);
    [DllImport(Gdi32)] public static extern bool Arc(IntPtr hdc, int l, int t, int r, int b, int xStart, int yStart, int xEnd, int yEnd);

    [StructLayout(LayoutKind.Sequential)]
    public struct FILETIME { public uint Low, High; public readonly ulong Value => ((ulong)High << 32) | Low; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [DllImport(Kernel32)] public static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);
    [DllImport(Kernel32)] public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX status);
}
