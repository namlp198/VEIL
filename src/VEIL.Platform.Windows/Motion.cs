namespace VEIL.Platform.Windows;

/// <summary>
/// Whether VEIL may animate at all (UI/UX 5, "reduced motion"). Two things switch motion off: VEIL's own preference and
/// Windows' "Animation effects" setting, so someone who turned animations off system-wide never gets them from VEIL.
/// </summary>
internal static class Motion
{
    private static volatile bool _reducedByPreference;

    public static bool ReducedByPreference
    {
        get => _reducedByPreference;
        set => _reducedByPreference = value;
    }

    public static bool Enabled => !_reducedByPreference && SystemAnimationsOn();

    private static bool SystemAnimationsOn()
    {
        try { return !Native.SystemParametersInfo(Native.SPI_GETCLIENTAREAANIMATION, 0, out var on, 0) || on != 0; }
        catch { return true; }
    }
}

/// <summary>CPU and memory load for the quiet status line. Cheap kernel counters, sampled off the window thread.</summary>
public sealed class WindowsSystemStats
{
    private ulong _lastIdle, _lastTotal;
    private bool _primed;

    /// <summary>CPU is null on the first call (it needs two samples).</summary>
    public (double? CpuPercent, double? MemoryPercent) Sample()
    {
        double? cpu = null;
        if (Native.GetSystemTimes(out var idle, out var kernel, out var user))
        {
            var total = kernel.Value + user.Value; // kernel time already includes idle time
            if (_primed && total > _lastTotal)
            {
                var busy = 1.0 - (double)(idle.Value - _lastIdle) / (total - _lastTotal);
                cpu = Math.Clamp(busy * 100.0, 0, 100);
            }
            _lastIdle = idle.Value;
            _lastTotal = total;
            _primed = true;
        }

        double? memory = null;
        var status = new Native.MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.MEMORYSTATUSEX>() };
        if (Native.GlobalMemoryStatusEx(ref status)) memory = status.dwMemoryLoad;
        return (cpu, memory);
    }
}
