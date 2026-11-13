namespace VEIL.Platform.Abstractions;

/// <summary>
/// Dead-man's switch for the thread that owns every overlay window. A monitor thread asks it to answer a ping; when no
/// answer has arrived for <c>stallAfter</c> the supervised thread is wedged (typically inside a call into a hung
/// application) and <c>onStall</c> fires once. Nothing in-process can recover a wedged window thread, so the owner of
/// this monitor decides what to do (see ADR-009: end the process, the OS then removes every overlay).
///
/// A long gap between two checks means the machine was suspended or the process was starved, not that the thread is
/// stuck, so the clock is re-based instead of reporting a false stall after resume.
/// </summary>
public sealed class HeartbeatMonitor : IDisposable
{
    private readonly Action _ping;
    private readonly Action<TimeSpan> _onStall;
    private readonly TimeSpan _interval;
    private readonly TimeSpan _stallAfter;
    private readonly TimeProvider _time;
    private readonly ITimer? _timer;
    private long _lastBeat;
    private long _lastCheck;
    private int _fired;

    public HeartbeatMonitor(Action ping, Action<TimeSpan> onStall, TimeSpan interval, TimeSpan stallAfter,
        TimeProvider? time = null, bool start = true)
    {
        _ping = ping;
        _onStall = onStall;
        _interval = interval;
        _stallAfter = stallAfter;
        _time = time ?? TimeProvider.System;
        _lastBeat = _lastCheck = _time.GetTimestamp();
        if (start) _timer = _time.CreateTimer(_ => Check(), null, interval, interval);
    }

    /// <summary>Called by the supervised thread when it handles a ping.</summary>
    public void Beat() => Volatile.Write(ref _lastBeat, _time.GetTimestamp());

    /// <summary>One supervision step (driven by the timer; public so it can be exercised deterministically).</summary>
    public void Check()
    {
        var now = _time.GetTimestamp();
        var sinceLastCheck = _time.GetElapsedTime(Volatile.Read(ref _lastCheck), now);
        Volatile.Write(ref _lastCheck, now);
        if (sinceLastCheck > _interval * 3 + TimeSpan.FromSeconds(1))
        {
            // Suspended or starved: forgive the gap.
            Volatile.Write(ref _lastBeat, now);
            Interlocked.Exchange(ref _fired, 0);
            SafePing();
            return;
        }

        var silent = _time.GetElapsedTime(Volatile.Read(ref _lastBeat), now);
        if (silent >= _stallAfter)
        {
            if (Interlocked.Exchange(ref _fired, 1) == 0)
            {
                try { _onStall(silent); } catch { /* the handler must never take the monitor down */ }
            }
            return;
        }
        Interlocked.Exchange(ref _fired, 0);
        SafePing();
    }

    private void SafePing()
    {
        try { _ping(); } catch { /* a failed ping simply produces no beat, which is what we measure */ }
    }

    public void Dispose() => _timer?.Dispose();
}
