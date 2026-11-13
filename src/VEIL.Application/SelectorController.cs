using VEIL.Domain;
using VEIL.Platform.Abstractions;

namespace VEIL.Application;

/// <summary>
/// Connects <see cref="SelectorSession"/> to a real <see cref="ISelectorView"/>. The view owns text input and focus;
/// this class only translates its events into session calls and pushes state back. A refused reveal keeps the selector
/// open with the policy message (AT-006).
/// </summary>
public sealed class SelectorController : IDisposable
{
    private readonly SelectorSession _session;
    private readonly ISelectorView _view;
    private readonly Func<SelectorPlacement> _placement;
    private readonly object _gate = new();
    private string _status = string.Empty;

    public SelectorController(SelectorSession session, ISelectorView view, Func<SelectorPlacement> placement)
    {
        _session = session;
        _view = view;
        _placement = placement;
        _view.FilterChanged += OnFilter;
        _view.SelectionChanged += OnSelection;
        _view.OnlyAllowedChanged += OnOnlyAllowed;
        _view.ConfirmRequested += OnConfirm;
        _view.CancelRequested += OnCancel;
        _view.NumberPressed += OnNumber;
    }

    public bool IsOpen => _session.IsOpen;

    // The view raises its events on a worker thread and hotkeys arrive on another, so the session is guarded by one lock.
    // Emergency Recovery never waits for it for long (see OnFocusEnded).
    public ActionResult Open()
    {
        lock (_gate)
        {
            var r = _session.Open();
            if (!_session.IsOpen) return r;
            _status = r.Message;
            _view.Show(Build(), _placement());
            return r;
        }
    }

    public ActionResult Toggle()
    {
        lock (_gate) return _session.IsOpen ? Close() : Open();
    }

    public ActionResult Close()
    {
        lock (_gate)
        {
            var r = _session.Close();
            _view.Hide();
            return r;
        }
    }

    /// <summary>
    /// Focus ended underneath the selector (Focus Off, emergency recovery). Called on the recovery path, so it gives up
    /// waiting for the lock after a second and closes the window anyway.
    /// </summary>
    public void OnFocusEnded()
    {
        var locked = Monitor.TryEnter(_gate, TimeSpan.FromSeconds(1));
        try
        {
            if (locked) _session.ForceClosed();
            _view.Hide();
        }
        finally { if (locked) Monitor.Exit(_gate); }
    }

    private void OnFilter(string text) { lock (_gate) { _status = _session.SetFilter(text).Message; Push(); } }
    private void OnOnlyAllowed(bool v) { lock (_gate) { _status = _session.SetOnlyAllowed(v).Message; Push(); } }
    private void OnSelection(int i) { lock (_gate) _session.Select(i); }
    private void OnCancel() { lock (_gate) { if (_session.IsOpen) Close(); } }

    private void OnConfirm() { lock (_gate) Finish(_session.RevealSelected()); }
    private void OnNumber(int n)
    {
        lock (_gate)
        {
            var entry = WindowSelectorService.GetByNumber(_session.Entries, n);
            if (entry is null) { _status = $"There is no entry {n}."; Push(); return; }
            _session.Select(entry.Number - 1);
            Finish(_session.RevealSelected());
        }
    }

    private void Finish(ActionResult result)
    {
        _status = result.Message;
        if (_session.IsOpen) Push(); else _view.Hide();
    }

    private static string ApplicationName(string applicationId) =>
        applicationId.StartsWith("unknown:", StringComparison.Ordinal) ? "unknown application" : Path.GetFileName(applicationId);

    private void Push() { if (_session.IsOpen) _view.Update(Build()); }

    private SelectorViewState Build() => new(
        _session.Filter,
        _session.OnlyAllowed,
        _session.Entries.Select(e => new SelectorRow(
            e.Number,
            $"{e.Number}. [{(e.IsAllowed ? "Allowed" : "Not allowed")} / {e.State}]  {e.Window.Title}",
            e.IsAllowed,
            e.Window.Title,
            ApplicationName(e.Window.ApplicationId),
            e.State.ToString())).ToList(),
        _session.SelectedIndex,
        _status);

    public void Dispose()
    {
        _view.FilterChanged -= OnFilter;
        _view.SelectionChanged -= OnSelection;
        _view.OnlyAllowedChanged -= OnOnlyAllowed;
        _view.ConfirmRequested -= OnConfirm;
        _view.CancelRequested -= OnCancel;
        _view.NumberPressed -= OnNumber;
    }
}
