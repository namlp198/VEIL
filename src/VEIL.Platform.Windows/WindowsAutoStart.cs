using Microsoft.Win32;

namespace VEIL.Platform.Windows;

public enum AutoStartScope { CurrentUser, AllUsers }

/// <summary>
/// Start-with-Windows via the registry <c>Run</c> key: HKCU for one user, HKLM for everyone. FR-LIFE-005: starting the
/// agent only readies the hotkeys; it never enters Focus Mode by itself. Writing HKLM needs administrator rights.
/// </summary>
public static class WindowsAutoStart
{
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string DefaultValueName = "VEIL";

    /// <summary>The command stored in the Run key: the executable quoted (paths contain spaces), then its arguments.</summary>
    public static string BuildCommand(string executablePath, string arguments = "run") =>
        string.IsNullOrWhiteSpace(arguments) ? $"\"{executablePath}\"" : $"\"{executablePath}\" {arguments}";

    /// <summary>Extracts the executable path from a stored command (the quoted first token), or null if it is not recognisable.</summary>
    public static string? ParseExecutable(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        command = command.Trim();
        if (command[0] != '"') return command.Split(' ', 2)[0];
        var end = command.IndexOf('"', 1);
        return end > 1 ? command[1..end] : null;
    }

    private static RegistryKey Root(AutoStartScope scope) => scope == AutoStartScope.AllUsers ? Registry.LocalMachine : Registry.CurrentUser;

    public static string? Get(AutoStartScope scope, string valueName = DefaultValueName)
    {
        using var key = Root(scope).OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(valueName) as string;
    }

    public static bool IsEnabled(AutoStartScope scope, string valueName = DefaultValueName) => Get(scope, valueName) is not null;

    public static void Enable(AutoStartScope scope, string executablePath, string arguments = "run", string valueName = DefaultValueName)
    {
        using var key = Root(scope).CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("The Run registry key could not be opened.");
        key.SetValue(valueName, BuildCommand(executablePath, arguments), RegistryValueKind.String);
    }

    public static void Disable(AutoStartScope scope, string valueName = DefaultValueName)
    {
        using var key = Root(scope).OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
    }
}
