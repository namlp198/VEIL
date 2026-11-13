using System.Runtime.InteropServices;
using VEIL.Domain;
using VEIL.Platform.Abstractions;

namespace VEIL.Platform.Windows;

/// <summary>
/// RegisterHotKey on the message thread. A refused registration reports the conflicting chord and leaves any
/// previous binding intact (FR-HK-002, AT-011). Handlers run on the message thread, independent of application UI.
/// </summary>
public sealed class WindowsHotkeyService : IHotkeyService
{
    private readonly Win32Thread _thread;
    private readonly Dictionary<HotkeyCommand, int> _ids = new();
    private readonly Dictionary<string, int> _transient = new();
    private int _nextId = 0x5600;

    internal WindowsHotkeyService(Win32Thread thread)
    {
        _thread = thread;
        _thread.Hotkey += OnHotkey;
    }

    public event EventHandler<HotkeyPressedEventArgs>? HotkeyPressed;
    public event EventHandler<TransientHotkeyEventArgs>? TransientPressed;

    public HotkeyRegistrationResult RegisterTransient(string name, KeyModifiers modifiers, string key)
    {
        if (!TryMapKey(key, out var vk)) return HotkeyRegistrationResult.Fail($"Key '{key}' is not supported on Windows.");
        uint mods = Native.MOD_NOREPEAT;
        if (modifiers.HasFlag(KeyModifiers.Ctrl)) mods |= Native.MOD_CONTROL;
        if (modifiers.HasFlag(KeyModifiers.Alt)) mods |= Native.MOD_ALT;
        if (modifiers.HasFlag(KeyModifiers.Shift)) mods |= Native.MOD_SHIFT;
        if (modifiers.HasFlag(KeyModifiers.Win)) mods |= Native.MOD_WIN;
        return _thread.Invoke(() =>
        {
            if (_transient.ContainsKey(name)) return HotkeyRegistrationResult.Ok;
            var id = _nextId++;
            if (!Native.RegisterHotKey(_thread.NotifyWindow, id, mods, vk))
                return HotkeyRegistrationResult.Fail($"{key} is already used by another application.");
            _transient[name] = id;
            return HotkeyRegistrationResult.Ok;
        });
    }

    public void UnregisterTransient(string name) => _thread.Invoke(() =>
    {
        if (_transient.Remove(name, out var id)) Native.UnregisterHotKey(_thread.NotifyWindow, id);
    });

    public HotkeyRegistrationResult Register(HotkeyBinding binding)
    {
        if (!binding.TryGetChord(out var chord))
            return HotkeyRegistrationResult.Fail($"'{binding.Chord}' is not a valid shortcut.");
        if (!TryMapKey(chord.Key, out var vk))
            return HotkeyRegistrationResult.Fail($"Key '{chord.Key}' is not supported on Windows.");

        uint mods = Native.MOD_NOREPEAT;
        if (chord.Modifiers.HasFlag(KeyModifiers.Ctrl)) mods |= Native.MOD_CONTROL;
        if (chord.Modifiers.HasFlag(KeyModifiers.Alt)) mods |= Native.MOD_ALT;
        if (chord.Modifiers.HasFlag(KeyModifiers.Shift)) mods |= Native.MOD_SHIFT;
        if (chord.Modifiers.HasFlag(KeyModifiers.Win)) mods |= Native.MOD_WIN;

        return _thread.Invoke(() =>
        {
            var id = _nextId++;
            if (!Native.RegisterHotKey(_thread.NotifyWindow, id, mods, vk))
            {
                var err = Marshal.GetLastWin32Error();
                return HotkeyRegistrationResult.Fail(err switch
                {
                    Native.ERROR_HOTKEY_ALREADY_REGISTERED => $"{chord} is already used by another application.",
                    0 => $"{chord} is reserved by Windows and cannot be registered; choose another shortcut.",
                    _ => $"{chord} could not be registered (error {err}).",
                });
            }
            // Success: only now replace the previous registration for this command.
            if (_ids.TryGetValue(binding.Command, out var old)) Native.UnregisterHotKey(_thread.NotifyWindow, old);
            _ids[binding.Command] = id;
            return HotkeyRegistrationResult.Ok;
        });
    }

    public void Unregister(HotkeyCommand command) => _thread.Invoke(() =>
    {
        if (_ids.Remove(command, out var id)) Native.UnregisterHotKey(_thread.NotifyWindow, id);
    });

    /// <summary>
    /// Runs on the window thread, so it only looks the key up and hands the work to another thread (ADR-009). Emergency
    /// Recovery gets a thread of its own and therefore can never wait behind a stuck command.
    /// </summary>
    private void OnHotkey(IntPtr wParam, IntPtr lParam)
    {
        var id = wParam.ToInt32();
        foreach (var (name, tid) in _transient)
            if (tid == id)
            {
                var args = new TransientHotkeyEventArgs(name);
                _thread.Post(() => TransientPressed?.Invoke(this, args));
                return;
            }
        foreach (var (command, registered) in _ids)
            if (registered == id)
            {
                var args = new HotkeyPressedEventArgs(command);
                void Raise() => HotkeyPressed?.Invoke(this, args);
                if (command == HotkeyCommand.EmergencyRecovery) _thread.PostUrgent(Raise);
                else _thread.Post(Raise);
                return;
            }
    }

    internal static bool TryMapKey(string key, out uint vk)
    {
        vk = 0;
        if (key.Length == 1 && char.IsAsciiLetterOrDigit(key[0])) { vk = key[0]; return true; }
        if (key.Length >= 2 && key[0] == 'F' && int.TryParse(key.AsSpan(1), out var n) && n is >= 1 and <= 24) { vk = (uint)(0x6F + n); return true; }
        vk = key switch
        {
            "Backspace" => 0x08, "Tab" => 0x09, "Enter" => 0x0D, "Esc" => 0x1B, "Space" => 0x20,
            "Left" => 0x25, "Up" => 0x26, "Right" => 0x27, "Down" => 0x28,
            _ => 0u,
        };
        return vk != 0;
    }

    public void Dispose()
    {
        _thread.Hotkey -= OnHotkey;
        try { foreach (var n in _transient.Keys.ToList()) UnregisterTransient(n); foreach (var c in _ids.Keys.ToList()) Unregister(c); } catch { /* thread may already be gone */ }
    }
}
