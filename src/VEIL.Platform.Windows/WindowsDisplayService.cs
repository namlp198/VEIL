using System.Runtime.InteropServices;
using VEIL.Domain;
using VEIL.Platform.Abstractions;

namespace VEIL.Platform.Windows;

/// <summary>
/// Win32 display enumeration in physical pixels (call <see cref="WindowsPlatform.EnableDpiAwareness"/> at startup).
/// Topology changes arrive as WM_DISPLAYCHANGE on the shared message thread and are debounced.
/// FR-DISP-001/004/007.
/// </summary>
public sealed class WindowsDisplayService : IDisplayService, IDisposable
{
    private readonly Win32Thread _thread;
    private readonly Timer _debounce;

    internal WindowsDisplayService(Win32Thread thread)
    {
        _thread = thread;
        _debounce = new Timer(_ => Raise(), null, Timeout.Infinite, Timeout.Infinite);
        _thread.DisplayChanged += OnDisplayChanged;
    }

    public event EventHandler<DisplayTopologyChangedEventArgs>? TopologyChanged;

    public IReadOnlyList<DisplayInfo> GetDisplays()
    {
        var list = new List<DisplayInfo>();
        Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMon, IntPtr _, ref Native.RECT _, IntPtr _) =>
        {
            var info = new Native.MONITORINFOEX { cbSize = Marshal.SizeOf<Native.MONITORINFOEX>() };
            if (Native.GetMonitorInfo(hMon, ref info))
            {
                var scale = 1.0;
                if (Native.GetDpiForMonitor(hMon, Native.MDT_EFFECTIVE_DPI, out var dpi, out _) == 0 && dpi > 0) scale = dpi / 96.0;
                var r = info.rcMonitor;
                list.Add(new DisplayInfo(
                    Id: info.szDevice,
                    Name: info.szDevice.TrimStart('\\', '.'),
                    Bounds: new WindowBounds(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top),
                    ScaleFactor: scale,
                    IsPrimary: (info.dwFlags & Native.MONITORINFOF_PRIMARY) != 0));
            }
            return true;
        }, IntPtr.Zero);
        return list.OrderByDescending(d => d.IsPrimary).ThenBy(d => d.Bounds.X).ToList();
    }

    /// <summary>Pointer position in virtual-screen physical pixels, or null if it cannot be read.</summary>
    public (int X, int Y)? GetPointerPosition() => Native.GetCursorPos(out var p) ? (p.X, p.Y) : null;

    private void OnDisplayChanged() => _debounce.Change(300, Timeout.Infinite);

    private void Raise()
    {
        try { TopologyChanged?.Invoke(this, new DisplayTopologyChangedEventArgs(GetDisplays())); }
        catch { /* subscribers must not crash the timer thread */ }
    }

    public void Dispose()
    {
        _thread.DisplayChanged -= OnDisplayChanged;
        _debounce.Dispose();
    }
}
