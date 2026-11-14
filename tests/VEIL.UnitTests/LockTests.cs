using VEIL.Application;
using VEIL.Domain;
using VEIL.Platform.Abstractions;

namespace VEIL.UnitTests;

internal sealed class FakeUnlockView : IUnlockView
{
    public bool IsVisible { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public int Shown { get; private set; }
    public event Action<string>? Submitted;
    public event Action? Cancelled;
    public string Prompt { get; private set; } = string.Empty;
    public void Show(SelectorPlacement placement, string prompt) { IsVisible = true; Shown++; Prompt = prompt; }
    public void SetStatus(string text) => Status = text;
    public void Hide() => IsVisible = false;
    public void Dispose() { }
    public void Type(string password) => Submitted?.Invoke(password);
    public void Cancel() => Cancelled?.Invoke();
}

public class LockPasswordTests
{
    [Fact]
    public void The_default_password_works_until_one_is_stored()
    {
        Assert.True(LockPassword.Verify("admin", null));
        Assert.True(LockPassword.Verify("admin", ""));
        Assert.False(LockPassword.Verify("Admin", null));
        Assert.False(LockPassword.Verify(null, null));
    }

    [Fact]
    public void A_stored_password_is_a_salted_hash_that_only_the_right_password_opens()
    {
        var a = LockPassword.Hash("s3cret!");
        var b = LockPassword.Hash("s3cret!");
        Assert.NotEqual(a, b);                       // salted
        Assert.DoesNotContain("s3cret", a);
        Assert.True(LockPassword.Verify("s3cret!", a));
        Assert.False(LockPassword.Verify("s3cret", a));
        Assert.False(LockPassword.Verify("admin", a)); // the default no longer works once one is set
    }

    [Fact]
    public void A_damaged_hash_never_unlocks_and_never_throws()
    {
        Assert.False(LockPassword.Verify("admin", "garbage"));
        Assert.False(LockPassword.Verify("admin", "pbkdf2-sha256$1$AAAA$BBBB"));
        Assert.False(LockPassword.Verify("admin", "pbkdf2-sha256$200000$!!$!!"));
    }

    [Fact]
    public void Passwords_need_a_sensible_length()
    {
        Assert.NotNull(LockPassword.Validate(""));
        Assert.NotNull(LockPassword.Validate("abc"));
        Assert.Null(LockPassword.Validate("abcd"));
        Assert.NotNull(LockPassword.Validate(new string('x', 200)));
    }
}

public class UnlockControllerTests
{
    private readonly FakeUnlockView _view = new();
    private readonly ManualTime _time = new();
    private int _unlocked, _toTray;

    private UnlockController Make() => new(_view, pw => pw == "admin",
        () => new SelectorPlacement(new WindowBounds(0, 0, 100, 100), 1.0, "D0"),
        () => { _unlocked++; return new ActionResult(true, "off"); },
        () => { _toTray++; return new ActionResult(true, "tray"); }, _time);

    [Fact]
    public void The_right_password_closes_the_form_and_ends_the_lock()
    {
        using var c = Make();
        c.Open();
        Assert.True(_view.IsVisible);
        _view.Type("admin");
        Assert.Equal(1, _unlocked);
        Assert.False(_view.IsVisible);
    }

    [Fact]
    public void The_tray_icon_needs_the_password_and_then_switches_to_tray_instead_of_ending_the_lock()
    {
        using var c = Make();
        c.OpenForTray();
        Assert.Contains("tray", _view.Prompt);
        _view.Type("nope");
        Assert.Equal(0, _toTray);
        _view.Type("admin");
        Assert.Equal(1, _toTray);
        Assert.Equal(0, _unlocked);
    }

    [Fact]
    public void A_wrong_password_keeps_the_lock_and_says_so()
    {
        using var c = Make();
        c.Open();
        _view.Type("nope");
        Assert.Equal(0, _unlocked);
        Assert.True(_view.IsVisible);
        Assert.Contains("Wrong password", _view.Status);
    }

    [Fact]
    public void Five_misses_pause_the_form_even_for_the_right_password_until_the_pause_is_over()
    {
        using var c = Make();
        c.Open();
        for (var i = 0; i < UnlockController.MaxMisses; i++) _view.Type("nope");
        Assert.Contains("Too many", _view.Status);

        _view.Type("admin");
        Assert.Equal(0, _unlocked);

        _time.Advance(UnlockController.Lockout + TimeSpan.FromSeconds(1));
        _view.Type("admin");
        Assert.Equal(1, _unlocked);
    }

    [Fact]
    public void Cancel_closes_the_form_without_ending_the_lock()
    {
        using var c = Make();
        c.Open();
        _view.Cancel();
        Assert.False(_view.IsVisible);
        Assert.Equal(0, _unlocked);
    }
}
