namespace VEIL.Domain;

/// <summary>Focus lifecycle states (SRS section 5).</summary>
public enum FocusState { Inactive, Preparing, Focused, SelectorOpen, Recovering, ErrorSafe }

public sealed class FocusStateChangedEventArgs(FocusState previous, FocusState current) : EventArgs
{
    public FocusState Previous { get; } = previous;
    public FocusState Current { get; } = current;
}

/// <summary>Enforces the allowed transitions of the SRS state model. Thread-safe.</summary>
public sealed class FocusStateMachine
{
    private readonly object _gate = new();
    private FocusState _state = FocusState.Inactive;

    public FocusState State { get { lock (_gate) return _state; } }

    public event EventHandler<FocusStateChangedEventArgs>? StateChanged;

    public static bool IsAllowed(FocusState from, FocusState to) => (from, to) switch
    {
        (FocusState.Inactive, FocusState.Preparing) => true,
        (FocusState.Preparing, FocusState.Focused) => true,
        (FocusState.Preparing, FocusState.ErrorSafe) => true,
        (FocusState.Focused, FocusState.SelectorOpen) => true,
        (FocusState.SelectorOpen, FocusState.Focused) => true,
        (FocusState.Focused, FocusState.Inactive) => true,
        (FocusState.SelectorOpen, FocusState.Inactive) => true,
        (FocusState.Recovering, FocusState.Inactive) => true,
        (FocusState.ErrorSafe, FocusState.Inactive) => true,
        (FocusState.ErrorSafe, FocusState.Preparing) => true,
        // Any runtime state may enter Recovering (fatal shutdown signal / emergency recovery).
        (_, FocusState.Recovering) => from != FocusState.Recovering,
        _ => false,
    };

    public bool TryTransition(FocusState to)
    {
        FocusState from;
        lock (_gate)
        {
            if (!IsAllowed(_state, to)) return false;
            from = _state;
            _state = to;
        }
        StateChanged?.Invoke(this, new FocusStateChangedEventArgs(from, to));
        return true;
    }

    public void Transition(FocusState to)
    {
        if (!TryTransition(to))
            throw new InvalidOperationException($"Transition {State} -> {to} is not allowed.");
    }
}
