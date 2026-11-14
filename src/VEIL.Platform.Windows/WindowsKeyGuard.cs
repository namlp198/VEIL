using System.Runtime.InteropServices;

namespace VEIL.Platform.Windows;

/// <summary>
/// Keeps the Windows keys from doing anything while the full lock is on (Start menu, Win+D, Win+Tab and the other Win
/// shortcuts), with a low-level keyboard hook that swallows both Windows keys. The hook is installed only while the lock
/// is on and is released the moment it ends. It runs on the window thread, so it only reads one flag; and the flag is
/// cleared before anything else happens when the lock ends, so a stuck window thread can never leave the keys dead.
/// Ctrl+Alt+Del and the secure desktop cannot be blocked by any program, and are not.
/// </summary>
public sealed class WindowsKeyGuard : IDisposable
{
    private static readonly Native.HookProc Proc = HookProc;
    private static volatile bool _block;

    private readonly Win32Thread _thread;
    private IntPtr _hook;

    internal WindowsKeyGuard(Win32Thread thread) => _thread = thread;

    public bool IsBlocking => _block;

    public void BlockWindowsKeys(bool on)
    {
        _block = on; // first, and without waiting for the window thread
        try
        {
            _thread.Invoke(() =>
            {
                if (on && _hook == IntPtr.Zero)
                    _hook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, Proc, Native.GetModuleHandle(null), 0);
                else if (!on && _hook != IntPtr.Zero)
                {
                    Native.UnhookWindowsHookEx(_hook);
                    _hook = IntPtr.Zero;
                }
            });
        }
        catch (Exception) { /* window thread gone: Windows removes the hook with it */ }
    }

    private static IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (code == Native.HC_ACTION && _block)
            {
                var vk = Marshal.ReadInt32(lParam);
                if (vk is Native.VK_LWIN or Native.VK_RWIN) return new IntPtr(1);
            }
        }
        catch (Exception) { /* fall through to the next hook */ }
        return Native.CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    public void Dispose() => BlockWindowsKeys(false);
}
