namespace VEIL.Application;

/// <summary>
/// Owns emergency teardown (architecture 5.8). Arming hooks process exit and unhandled exceptions so overlays are
/// removed even on abnormal managed shutdown. Hard process kills are covered by OS window ownership cleanup.
/// </summary>
public sealed class RecoverySupervisor : IDisposable
{
    private readonly Action _teardown;
    private readonly IDiagnosticSink _diag;
    private readonly object _gate = new();
    private bool _armed;

    public RecoverySupervisor(Action teardown, IDiagnosticSink? diagnostics = null)
    {
        _teardown = teardown;
        _diag = diagnostics ?? NullDiagnosticSink.Instance;
    }

    public bool IsArmed { get { lock (_gate) return _armed; } }

    /// <summary>Must be called before any overlay is created (architecture 5.2). Idempotent.</summary>
    public void Arm()
    {
        lock (_gate)
        {
            if (_armed) return;
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandled;
            _armed = true;
        }
    }

    public void Disarm()
    {
        lock (_gate)
        {
            if (!_armed) return;
            AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
            AppDomain.CurrentDomain.UnhandledException -= OnUnhandled;
            _armed = false;
        }
    }

    /// <summary>Runs teardown; never throws and is safe to call repeatedly (NFR-REL-003).</summary>
    public void EmergencyTeardown()
    {
        try { _teardown(); }
        catch (Exception ex) { _diag.Report($"Emergency teardown error: {ex.Message}"); }
    }

    private void OnProcessExit(object? sender, EventArgs e) => EmergencyTeardown();
    private void OnUnhandled(object sender, UnhandledExceptionEventArgs e) => EmergencyTeardown();

    public void Dispose() => Disarm();
}
