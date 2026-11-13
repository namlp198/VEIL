using System.Collections.Concurrent;
using VEIL.Domain;
using VEIL.Platform.Abstractions;

namespace VEIL.Application;

public interface IDiagnosticSink
{
    void Report(string message);
}

public sealed class NullDiagnosticSink : IDiagnosticSink
{
    public static NullDiagnosticSink Instance { get; } = new();
    public void Report(string message) { }
}

public sealed record OverlayStatus(string DisplayId, bool IsHealthy);

/// <summary>
/// Owns one overlay per active display (FR-DISP-002/005/006). Teardown is idempotent and one failing
/// overlay never blocks teardown of the others (NFR-REL-002/003).
/// </summary>
public sealed class OverlayManager(IDisplayService displays, IOverlayHost host, IDiagnosticSink? diagnostics = null)
{
    // _gate serializes creation (Show/Reconcile). Teardown deliberately does NOT take it: Emergency Recovery must never
    // wait behind a creation that is stuck in the platform. Instead Teardown bumps _epoch, which makes any creation in
    // flight dispose what it just made and give up (ADR-009).
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<string, IOverlaySurface> _overlays = new();
    private readonly IDiagnosticSink _diag = diagnostics ?? NullDiagnosticSink.Instance;
    private int _epoch;

    public int Count => _overlays.Count;

    public IReadOnlyList<OverlayStatus> GetStatus() =>
        _overlays.Values.Select(o => new OverlayStatus(o.DisplayId, o.IsHealthy)).ToList();

    /// <summary>Creates overlays on every display. On any failure everything created so far is destroyed (architecture 11).</summary>
    public void Show()
    {
        var list = displays.GetDisplays();
        if (list.Count == 0) throw new InvalidOperationException("No active displays were detected.");
        var epoch = Volatile.Read(ref _epoch);
        try
        {
            lock (_gate)
            {
                foreach (var d in list)
                {
                    if (_overlays.ContainsKey(d.Id)) continue;
                    var surface = host.Create(d);
                    if (Volatile.Read(ref _epoch) != epoch)
                    {
                        SafeDispose(surface);
                        throw new OperationCanceledException("Focus Mode was cancelled while the overlays were being created.");
                    }
                    _overlays[d.Id] = surface;
                }
            }
        }
        catch
        {
            Teardown();
            throw;
        }
    }

    /// <summary>Adds overlays for new displays and releases those of removed displays (FR-DISP-005/006).</summary>
    public void Reconcile()
    {
        var current = displays.GetDisplays();
        var ids = current.Select(d => d.Id).ToHashSet();
        var epoch = Volatile.Read(ref _epoch);
        lock (_gate)
        {
            foreach (var gone in _overlays.Keys.Where(k => !ids.Contains(k)).ToList())
                if (_overlays.TryRemove(gone, out var removed)) SafeDispose(removed);
            foreach (var d in current)
            {
                if (_overlays.TryGetValue(d.Id, out var existing) && existing.IsHealthy) continue;
                if (existing is not null && _overlays.TryRemove(d.Id, out var stale)) SafeDispose(stale);
                try
                {
                    var surface = host.Create(d);
                    if (Volatile.Read(ref _epoch) != epoch) { SafeDispose(surface); return; } // torn down meanwhile: stay down
                    _overlays[d.Id] = surface;
                }
                catch (Exception ex) { _diag.Report($"Overlay for display {d.Id} could not be created: {ex.Message}"); }
            }
        }
    }

    public void Teardown()
    {
        Interlocked.Increment(ref _epoch);
        foreach (var key in _overlays.Keys.ToList())
            if (_overlays.TryRemove(key, out var overlay)) SafeDispose(overlay);
    }

    private void SafeDispose(IOverlaySurface overlay)
    {
        try { overlay.Dispose(); }
        catch (Exception ex) { _diag.Report($"Overlay {overlay.DisplayId} teardown failed: {ex.Message}"); }
    }
}
