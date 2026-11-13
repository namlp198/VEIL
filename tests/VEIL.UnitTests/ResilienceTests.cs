using VEIL.Application;
using VEIL.Domain;
using VEIL.Platform.Abstractions;

namespace VEIL.UnitTests;

/// <summary>An overlay host whose Create call is stuck inside "the platform" until the test lets it go.</summary>
internal sealed class BlockingOverlayHost : IOverlayHost
{
    private readonly List<FakeSurface> _created = [];
    public ManualResetEventSlim Entered { get; } = new();
    public ManualResetEventSlim Release { get; } = new();

    public IOverlaySurface Create(DisplayInfo display)
    {
        Entered.Set();
        Release.Wait(TimeSpan.FromSeconds(10));
        var s = new FakeSurface(display.Id);
        lock (_created) _created.Add(s);
        return s;
    }

    public int Live { get { lock (_created) return _created.Count(s => !s.Disposed); } }
}

/// <summary>
/// ADR-009: nothing may ever stop Emergency Recovery, and a stuck platform call must not be able to freeze the desktop.
/// These tests pin the behaviours that make that true.
/// </summary>
public class ResilienceTests
{
    // ---- heartbeat monitor (the last line of defence) ----------------------------------------------------------------

    private static (HeartbeatMonitor Monitor, ManualTime Time, List<TimeSpan> Stalls, int[] Pings) Heartbeat()
    {
        var time = new ManualTime();
        var stalls = new List<TimeSpan>();
        var pings = new int[1];
        var monitor = new HeartbeatMonitor(() => pings[0]++, stalls.Add, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), time, start: false);
        return (monitor, time, stalls, pings);
    }

    [Fact]
    public void A_thread_that_answers_is_never_reported()
    {
        var (monitor, time, stalls, pings) = Heartbeat();
        for (var i = 0; i < 60; i++) { time.Advance(TimeSpan.FromSeconds(1)); monitor.Beat(); monitor.Check(); }
        Assert.Empty(stalls);
        Assert.Equal(60, pings[0]);
    }

    [Fact]
    public void A_silent_thread_is_reported_once_after_the_limit()
    {
        var (monitor, time, stalls, pings) = Heartbeat();
        for (var i = 0; i < 4; i++) { time.Advance(TimeSpan.FromSeconds(1)); monitor.Check(); }
        Assert.Empty(stalls);                       // 4 s of silence is still tolerated
        time.Advance(TimeSpan.FromSeconds(1)); monitor.Check();
        Assert.Single(stalls);                      // 5 s: wedged
        Assert.True(stalls[0] >= TimeSpan.FromSeconds(5));
        time.Advance(TimeSpan.FromSeconds(1)); monitor.Check();
        time.Advance(TimeSpan.FromSeconds(1)); monitor.Check();
        Assert.Single(stalls);                      // not reported again while it stays wedged
        Assert.Equal(4, pings[0]);                  // and no more pings are piled onto a stuck thread
    }

    [Fact]
    public void A_thread_that_recovers_is_watched_again()
    {
        var (monitor, time, stalls, _) = Heartbeat();
        for (var i = 0; i < 5; i++) { time.Advance(TimeSpan.FromSeconds(1)); monitor.Check(); }
        Assert.Single(stalls);
        monitor.Beat();
        time.Advance(TimeSpan.FromSeconds(1)); monitor.Check();   // answered: re-armed
        for (var i = 0; i < 5; i++) { time.Advance(TimeSpan.FromSeconds(1)); monitor.Check(); }
        Assert.Equal(2, stalls.Count);
    }

    [Fact]
    public void Suspend_and_resume_is_not_mistaken_for_a_stall()
    {
        var (monitor, time, stalls, _) = Heartbeat();
        time.Advance(TimeSpan.FromSeconds(1)); monitor.Beat(); monitor.Check();
        time.Advance(TimeSpan.FromMinutes(30));     // the laptop slept; no checks happened in between
        monitor.Check();
        Assert.Empty(stalls);
        time.Advance(TimeSpan.FromSeconds(1)); monitor.Beat(); monitor.Check();
        Assert.Empty(stalls);
    }

    [Fact]
    public void A_failing_ping_or_handler_never_breaks_the_monitor()
    {
        var time = new ManualTime();
        using var monitor = new HeartbeatMonitor(() => throw new InvalidOperationException("ping"), _ => throw new InvalidOperationException("stall"),
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), time, start: false);
        for (var i = 0; i < 6; i++) { time.Advance(TimeSpan.FromSeconds(1)); monitor.Check(); }
    }

    private static async Task<bool> FinishesSoon(Task task) =>
        await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(2))) == task;

    // ---- overlays: teardown never waits for creation -----------------------------------------------------------------

    [Fact]
    public async Task Teardown_does_not_wait_behind_a_creation_that_is_stuck_in_the_platform()
    {
        var host = new BlockingOverlayHost();
        var manager = new OverlayManager(new FakeDisplays(2), host);
        var show = Task.Run(manager.Show);
        Assert.True(host.Entered.Wait(TimeSpan.FromSeconds(2)));

        var teardown = Task.Run(manager.Teardown);
        Assert.True(await FinishesSoon(teardown), "Teardown waited for the stuck creation.");

        host.Release.Set();
        await Assert.ThrowsAsync<OperationCanceledException>(() => show);
        Assert.Equal(0, host.Live);                 // what the stuck creation finally made was disposed, not shown
        Assert.Equal(0, manager.Count);
    }

    // ---- Focus service: Emergency Recovery is unconditional ----------------------------------------------------------

    private sealed class Rig
    {
        public readonly FocusStateMachine Machine = new();
        public readonly FakeDisplays Displays;
        public readonly FakeWindows Windows = new();
        public readonly OverlayManager Overlays;
        public readonly FocusService Focus;
        public ApplicationPolicy Policy = new([]);

        public Rig(IOverlayHost host, int displays = 1)
        {
            Displays = new FakeDisplays(displays);
            Overlays = new OverlayManager(Displays, host);
            Focus = new FocusService(Machine, Displays, Windows, Overlays, new RecoverySupervisor(Overlays.Teardown), () => Policy);
        }
    }

    [Fact]
    public async Task Emergency_recovery_is_not_blocked_by_a_focus_on_that_is_stuck()
    {
        var host = new BlockingOverlayHost();
        var r = new Rig(host);
        var on = Task.Run(r.Focus.FocusOn);
        Assert.True(host.Entered.Wait(TimeSpan.FromSeconds(2)));

        var recover = Task.Run(r.Focus.EmergencyRecover);
        Assert.True(await FinishesSoon(recover), "Emergency Recovery waited for the stuck FocusOn.");
        Assert.Equal(FocusState.Inactive, r.Focus.State);

        host.Release.Set();
        var result = await on;
        Assert.False(result.Success);               // the interrupted start reports failure...
        Assert.Equal(0, host.Live);                 // ...and nothing is left on screen
        Assert.Equal(FocusState.Inactive, r.Focus.State);
    }

    [Fact]
    public void Emergency_recovery_survives_a_subscriber_that_throws_and_an_overlay_that_will_not_close()
    {
        var host = new FakeOverlayHost();
        var r = new Rig(host, displays: 3);
        r.Focus.FocusOn();
        host.Created[1].ThrowOnDispose = true;
        r.Machine.StateChanged += (_, _) => throw new InvalidOperationException("a UI subscriber blew up");

        var result = r.Focus.EmergencyRecover();

        Assert.True(result.Success);
        Assert.Equal(0, host.Live);
        Assert.Equal(FocusState.Inactive, r.Focus.State);
    }

    [Fact]
    public void Emergency_recovery_survives_windows_that_can_no_longer_be_lowered()
    {
        var host = new FakeOverlayHost();
        var r = new Rig(host);
        r.Policy = new ApplicationPolicy([new ApplicationRule("c:\\ide.exe", "IDE")]);
        r.Windows.Windows.Add(FakeWindows.Win("A", "c:\\ide.exe"));
        r.Focus.FocusOn();
        r.Focus.Reveal("A");
        r.Windows.Windows.Clear();                  // the window vanished (or its process hung) meanwhile

        Assert.True(r.Focus.EmergencyRecover().Success);
        Assert.Equal(0, host.Live);
    }

    [Fact]
    public void Windows_that_were_lifted_above_the_overlay_go_back_to_normal_layering_when_focus_ends()
    {
        var r = new Rig(new FakeOverlayHost());
        r.Policy = new ApplicationPolicy([new ApplicationRule("c:\\ide.exe", "IDE")]);
        r.Windows.Windows.Add(FakeWindows.Win("A", "c:\\ide.exe"));
        r.Focus.FocusOn();
        r.Focus.Reveal("A");
        Assert.Contains("A", r.Windows.Raised);

        r.Focus.FocusOff();

        Assert.DoesNotContain("A", r.Windows.Raised);   // not left "always on top" after the overlay is gone
        Assert.Empty(r.Focus.RevealedWindowIds);
    }

    [Fact]
    public void Emergency_recovery_also_puts_lifted_windows_back()
    {
        var r = new Rig(new FakeOverlayHost());
        r.Policy = new ApplicationPolicy([new ApplicationRule("c:\\ide.exe", "IDE")]);
        r.Windows.Windows.Add(FakeWindows.Win("A", "c:\\ide.exe"));
        r.Focus.FocusOn();
        r.Focus.Reveal("A");

        r.Focus.EmergencyRecover();

        Assert.DoesNotContain("A", r.Windows.Raised);
    }

    [Fact]
    public void A_window_that_is_no_longer_allowed_goes_back_behind_the_overlay()
    {
        var r = new Rig(new FakeOverlayHost());
        r.Policy = new ApplicationPolicy([new ApplicationRule("c:\\ide.exe", "IDE"), new ApplicationRule("c:\\web.exe", "Web")]);
        r.Windows.Windows.Add(FakeWindows.Win("A", "c:\\ide.exe"));
        r.Windows.Windows.Add(FakeWindows.Win("B", "c:\\web.exe"));
        r.Focus.FocusOn();
        r.Focus.Reveal("A");
        r.Focus.Reveal("B");

        // switch to a workspace that only contains the IDE
        r.Policy = new ApplicationPolicy(r.Policy.Rules, new WorkspaceProfile("Coding", ["c:\\ide.exe"]));
        var result = r.Focus.RefreshPolicy();

        Assert.True(result.Success);
        Assert.Contains("A", r.Windows.Raised);
        Assert.DoesNotContain("B", r.Windows.Raised);
        Assert.Equal(["A"], r.Focus.RevealedWindowIds.ToArray());
    }

    [Fact]
    public void Refreshing_the_policy_outside_focus_mode_does_nothing()
    {
        var r = new Rig(new FakeOverlayHost());
        Assert.True(r.Focus.RefreshPolicy().Success);
    }
}
