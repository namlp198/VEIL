using VEIL.Platform.Abstractions;

namespace VEIL.Application;

/// <summary>
/// What the owner can do from the full lock with the password: end Focus Mode (the lock icon) or leave the full lock for
/// the tray (the tray icon). The hotkeys need no password. The right password runs the chosen action.
/// Wrong guesses are slowed down (five misses lock the form for 30 seconds), so the form cannot be used to guess the
/// password. The Emergency Recovery hotkey is separate and stays available (NFR-UX-001).
/// </summary>
public sealed class UnlockController : IDisposable
{
    public const int MaxMisses = 5;
    public static readonly TimeSpan Lockout = TimeSpan.FromSeconds(30);

    private readonly IUnlockView _view;
    private readonly Func<string, bool> _verify;
    private readonly Func<SelectorPlacement> _placement;
    private readonly Func<ActionResult> _unlock;
    private readonly Func<ActionResult> _toTray;
    private Func<ActionResult> _pending;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private int _misses;
    private DateTimeOffset _lockedUntil;

    public UnlockController(IUnlockView view, Func<string, bool> verify, Func<SelectorPlacement> placement, Func<ActionResult> unlock, Func<ActionResult>? toTray = null, TimeProvider? time = null)
    {
        _view = view;
        _verify = verify;
        _placement = placement;
        _unlock = unlock;
        _toTray = toTray ?? unlock;
        _pending = unlock;
        _time = time ?? TimeProvider.System;
        _view.Submitted += OnSubmitted;
        _view.Cancelled += Close;
    }

    public bool IsOpen => _view.IsVisible;

    /// <summary>The lock icon was clicked: the password ends the full lock.</summary>
    public void Open() => Open(toTray: false);

    /// <summary>The tray icon was clicked: the password switches to tray mode.</summary>
    public void OpenForTray() => Open(toTray: true);

    private void Open(bool toTray)
    {
        lock (_gate)
        {
            if (_view.IsVisible) return;
            _pending = toTray ? _toTray : _unlock;
            _view.Show(_placement(), toTray ? "Password to switch to tray mode" : "Password to end the full lock");
            if (_time.GetUtcNow() < _lockedUntil) _view.SetStatus(LockedText());
        }
    }

    public void Close() { lock (_gate) _view.Hide(); }

    /// <summary>Returns true when the password was right and the lock ended.</summary>
    public bool Submit(string? password)
    {
        lock (_gate)
        {
            if (_time.GetUtcNow() < _lockedUntil) { _view.SetStatus(LockedText()); return false; }
            if (password is not null && _verify(password))
            {
                _misses = 0;
                _view.Hide();
                _pending();
                return true;
            }
            if (++_misses >= MaxMisses)
            {
                _misses = 0;
                _lockedUntil = _time.GetUtcNow() + Lockout;
                _view.SetStatus(LockedText());
            }
            else _view.SetStatus($"Wrong password ({MaxMisses - _misses} tries left before a pause).");
            return false;
        }
    }

    private string LockedText() => $"Too many wrong passwords. Try again in {Math.Max(1, (int)Math.Ceiling((_lockedUntil - _time.GetUtcNow()).TotalSeconds))} s.";

    private void OnSubmitted(string password) => Submit(password);

    public void Dispose()
    {
        _view.Submitted -= OnSubmitted;
        _view.Cancelled -= Close;
        try { _view.Hide(); } catch { /* shutting down */ }
    }
}
