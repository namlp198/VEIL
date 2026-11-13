using VEIL.Domain;

namespace VEIL.Application;

public enum TimerPhase { Idle, Running, Completed }

/// <summary>One finished focus session, for the "actual focused time" record (UI/UX 4.2).</summary>
public sealed record FocusSessionRecord(
    DateTimeOffset Started,
    DateTimeOffset Ended,
    int PlannedMinutes,
    int ActualSeconds,
    bool Completed,
    string? Workspace);

public interface IFocusSessionLog
{
    /// <summary>Must never throw: a full disk must not interrupt a focus session.</summary>
    void Append(FocusSessionRecord record);
    IReadOnlyList<FocusSessionRecord> ReadRecent(int max);
}

/// <summary>What the Focus screen needs to draw the timer.</summary>
public readonly record struct TimerSnapshot(TimerPhase Phase, TimeSpan Remaining, TimeSpan Planned, double Progress, string? Notice);

/// <summary>
/// Pomodoro-style focus timer (UI/UX 4.2): 25/50/90 minute sessions, remaining time on the Focus screen, a quiet notice
/// when time is up, and a record of the focus time actually spent. It never ends Focus Mode by itself; finishing a session
/// is a gentle prompt, not a sudden change of the user's screen.
/// </summary>
public sealed class FocusTimer : IDisposable
{
    public const int MaxMinutes = 12 * 60;
    public static readonly IReadOnlyList<int> Presets = [25, 50, 90];
    private static readonly TimeSpan NoticeFor = TimeSpan.FromSeconds(20);

    private readonly TimeProvider _time;
    private readonly IFocusSessionLog? _log;
    private readonly Func<string?> _workspace;
    private readonly ITimer? _ticker;
    private readonly object _gate = new();

    private TimerPhase _phase;
    private DateTimeOffset _started;
    private TimeSpan _planned;
    private long _startStamp;
    private DateTimeOffset _completedAt;
    private string? _sessionWorkspace;

    public FocusTimer(IFocusSessionLog? log = null, Func<string?>? workspace = null, TimeProvider? time = null, bool tick = true)
    {
        _log = log;
        _workspace = workspace ?? (() => null);
        _time = time ?? TimeProvider.System;
        if (tick) _ticker = _time.CreateTimer(_ => Tick(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    /// <summary>Raised when the phase changes or a session completes. Handlers must be quick and must not call back in a way that blocks.</summary>
    public event Action? Changed;
    public event Action<FocusSessionRecord>? SessionEnded;

    public bool IsRunning { get { lock (_gate) return _phase == TimerPhase.Running; } }

    public ActionResult Start(TimeSpan planned)
    {
        if (planned < TimeSpan.FromMinutes(1) || planned > TimeSpan.FromMinutes(MaxMinutes))
            return new(false, $"A focus session must be between 1 minute and {MaxMinutes / 60} hours.");
        lock (_gate)
        {
            if (_phase == TimerPhase.Running) return new(false, "A focus timer is already running.");
            _phase = TimerPhase.Running;
            _planned = planned;
            _started = _time.GetUtcNow();
            _startStamp = _time.GetTimestamp();
            _sessionWorkspace = _workspace();
        }
        Changed?.Invoke();
        return new(true, $"Focus timer started: {(int)planned.TotalMinutes} min.");
    }

    /// <summary>Ends a running session early and records the time actually spent. Also used when Focus Mode ends underneath it.</summary>
    public ActionResult Stop()
    {
        FocusSessionRecord? record;
        lock (_gate)
        {
            if (_phase != TimerPhase.Running)
            {
                if (_phase == TimerPhase.Completed) { _phase = TimerPhase.Idle; Changed?.Invoke(); }
                return new(true, "No focus timer is running.");
            }
            record = EndLocked(completed: false);
            _phase = TimerPhase.Idle;
        }
        Publish(record);
        Changed?.Invoke();
        return new(true, $"Focus timer stopped after {Describe(record.ActualSeconds)}.");
    }

    public TimerSnapshot Snapshot()
    {
        lock (_gate)
        {
            if (_phase == TimerPhase.Running)
            {
                var elapsed = _time.GetElapsedTime(_startStamp);
                var remaining = _planned - elapsed;
                if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
                return new(TimerPhase.Running, remaining, _planned, Math.Clamp(elapsed / _planned, 0, 1), null);
            }
            if (_phase == TimerPhase.Completed)
            {
                if (_time.GetUtcNow() - _completedAt <= NoticeFor)
                    return new(TimerPhase.Completed, TimeSpan.Zero, _planned, 1, $"Session complete - {(int)_planned.TotalMinutes} min of focus");
                _phase = TimerPhase.Idle;
            }
            return new(TimerPhase.Idle, TimeSpan.Zero, TimeSpan.Zero, 0, null);
        }
    }

    /// <summary>Driven by a one second timer; public so tests can advance deterministic time.</summary>
    public void Tick()
    {
        FocusSessionRecord? record = null;
        var changed = false;
        lock (_gate)
        {
            if (_phase == TimerPhase.Running && _time.GetElapsedTime(_startStamp) >= _planned)
            {
                record = EndLocked(completed: true);
                _phase = TimerPhase.Completed;
                _completedAt = _time.GetUtcNow();
                changed = true;
            }
            else if (_phase == TimerPhase.Completed && _time.GetUtcNow() - _completedAt > NoticeFor)
            {
                _phase = TimerPhase.Idle;
                changed = true;
            }
        }
        if (record is not null) Publish(record);
        if (changed) Changed?.Invoke();
    }

    private FocusSessionRecord EndLocked(bool completed)
    {
        var actual = completed ? _planned : _time.GetElapsedTime(_startStamp);
        if (actual > _planned) actual = _planned;
        return new FocusSessionRecord(_started, _started + actual, (int)Math.Round(_planned.TotalMinutes),
            (int)actual.TotalSeconds, completed, _sessionWorkspace);
    }

    private void Publish(FocusSessionRecord record)
    {
        try { _log?.Append(record); } catch { /* recording is best effort */ }
        try { SessionEnded?.Invoke(record); } catch { /* a subscriber must not break the timer */ }
    }

    private static string Describe(int seconds) => seconds >= 60 ? $"{seconds / 60} min" : $"{seconds} s";

    public void Dispose() => _ticker?.Dispose();
}
