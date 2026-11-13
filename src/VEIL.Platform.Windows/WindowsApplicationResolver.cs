using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using VEIL.Platform.Abstractions;

namespace VEIL.Platform.Windows;

/// <summary>
/// Turns a dropped file into an allow-list identity: a program (.exe) is used as it is, a shortcut (.lnk, from the desktop,
/// the Start menu or the taskbar folder) is followed to the program it starts. The identity is the same lower-case full
/// path the window enumeration reports, so a rule created this way matches the running application. No path is typed.
/// </summary>
public sealed class WindowsApplicationResolver : IApplicationResolver
{
    public ResolveResult Resolve(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return ResolveResult.Fail("Nothing was dropped.");
        string full;
        try { full = Path.GetFullPath(path.Trim().Trim('"')); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        { return ResolveResult.Fail("That is not a usable path."); }

        if (Directory.Exists(full)) return ResolveResult.Fail("That is a folder, not an application.");
        if (!File.Exists(full)) return ResolveResult.Fail("That file does not exist.");

        var extension = Path.GetExtension(full);
        if (extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            var target = ReadShortcutTarget(full, out var problem);
            if (target is null) return ResolveResult.Fail(problem ?? "That shortcut could not be read.");
            full = target;
            if (Directory.Exists(full)) return ResolveResult.Fail("That shortcut opens a folder, not a program.");
            if (!File.Exists(full)) return ResolveResult.Fail("The shortcut points to a program that is not there any more.");
        }
        if (!Path.GetExtension(full).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            return ResolveResult.Fail("Only programs (.exe) and shortcuts to programs can be added.");

        full = ToLongPath(full);
        if (full.Length > 1024) return ResolveResult.Fail("That path is too long.");
        return ResolveResult.Ok(new ResolvedApplication(full.ToLowerInvariant(), DisplayNameOf(full)));
    }

    private static string DisplayNameOf(string exe)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(exe);
            foreach (var candidate in new[] { info.FileDescription, info.ProductName })
                if (!string.IsNullOrWhiteSpace(candidate)) return candidate.Trim().Length > 80 ? candidate.Trim()[..80] : candidate.Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { /* fall through */ }
        return Path.GetFileNameWithoutExtension(exe);
    }

    /// <summary>Expands 8.3 short names so the identity matches the path Windows reports for the running process.</summary>
    private static string ToLongPath(string path)
    {
        var sb = new StringBuilder(1024);
        var n = GetLongPathName(path, sb, sb.Capacity);
        return n > 0 && n < sb.Capacity ? sb.ToString() : path;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetLongPathName(string shortPath, StringBuilder longPath, int cch);

    // ---- .lnk -----------------------------------------------------------------------------------------------------------

    private static string? ReadShortcutTarget(string lnkPath, out string? problem)
    {
        problem = null;
        object? com = null;
        try
        {
            com = new ShellLink();
            var link = (IShellLinkW)com;
            ((IPersistFile)com).Load(lnkPath, 0);
            // No UI, no searching for a moved target, no updating the file: this must stay quick and read-only.
            try { link.Resolve(IntPtr.Zero, SLR_NO_UI | SLR_NOUPDATE | SLR_NOSEARCH | SLR_NOTRACK); } catch (COMException) { /* use the stored path */ }
            var sb = new StringBuilder(1024);
            link.GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
            var target = Environment.ExpandEnvironmentVariables(sb.ToString());
            if (string.IsNullOrWhiteSpace(target))
            {
                problem = "That shortcut does not point to a regular program (Microsoft Store apps are not supported yet). Add the program's .exe instead.";
                return null;
            }
            return target;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or IOException or UnauthorizedAccessException)
        {
            problem = "That shortcut could not be read.";
            return null;
        }
        finally
        {
            if (com is not null && Marshal.IsComObject(com)) Marshal.FinalReleaseComObject(com);
        }
    }

    private const uint SLR_NO_UI = 0x1, SLR_NOUPDATE = 0x8, SLR_NOSEARCH = 0x10, SLR_NOTRACK = 0x20;

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int cch, IntPtr findData, uint flags);
        void GetIDList(out IntPtr pidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCmd);
        void SetShowCmd(int showCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder icon, int cch, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string icon, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr hwnd, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("0000010B-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid classId);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string fileName, uint mode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string fileName, [MarshalAs(UnmanagedType.Bool)] bool remember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string fileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string fileName);
    }
}
