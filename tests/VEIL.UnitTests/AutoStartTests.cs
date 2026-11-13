using VEIL.Platform.Windows;

namespace VEIL.UnitTests;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class AutoStartTests
{
    [Fact]
    public void Command_quotes_the_path_because_install_folders_contain_spaces() =>
        Assert.Equal("\"C:\\Program Files\\VEIL\\veil-agent.exe\" run", WindowsAutoStart.BuildCommand("C:\\Program Files\\VEIL\\veil-agent.exe"));

    [Fact]
    public void Command_without_arguments_is_just_the_quoted_path() =>
        Assert.Equal("\"C:\\a b\\x.exe\"", WindowsAutoStart.BuildCommand("C:\\a b\\x.exe", ""));

    [Theory]
    [InlineData("\"C:\\Program Files\\VEIL\\veil-agent.exe\" run", "C:\\Program Files\\VEIL\\veil-agent.exe")]
    [InlineData("C:\\tools\\veil.exe run", "C:\\tools\\veil.exe")]
    [InlineData("  \"C:\\x y\\v.exe\"  ", "C:\\x y\\v.exe")]
    public void Executable_is_recovered_from_a_stored_command(string command, string expected) =>
        Assert.Equal(expected, WindowsAutoStart.ParseExecutable(command));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\"unterminated")]
    public void Unrecognisable_commands_give_null(string? command) => Assert.Null(WindowsAutoStart.ParseExecutable(command));

    [Fact]
    public void Build_and_parse_round_trip()
    {
        var exe = "C:\\Users\\me\\AppData\\Local\\Programs\\VEIL\\veil-agent.exe";
        Assert.Equal(exe, WindowsAutoStart.ParseExecutable(WindowsAutoStart.BuildCommand(exe)));
    }

    [Fact] // real registry, current user only, with a throw-away value name
    public void Enable_then_Disable_round_trips_in_the_current_user_hive()
    {
        if (!OperatingSystem.IsWindows()) return;
        var name = "VEIL.UnitTest." + Guid.NewGuid().ToString("N");
        try
        {
            Assert.False(WindowsAutoStart.IsEnabled(AutoStartScope.CurrentUser, name));
            WindowsAutoStart.Enable(AutoStartScope.CurrentUser, "C:\\Program Files\\VEIL\\veil-agent.exe", "run", name);
            Assert.True(WindowsAutoStart.IsEnabled(AutoStartScope.CurrentUser, name));
            Assert.Equal("\"C:\\Program Files\\VEIL\\veil-agent.exe\" run", WindowsAutoStart.Get(AutoStartScope.CurrentUser, name));
            WindowsAutoStart.Disable(AutoStartScope.CurrentUser, name);
            Assert.False(WindowsAutoStart.IsEnabled(AutoStartScope.CurrentUser, name));
            WindowsAutoStart.Disable(AutoStartScope.CurrentUser, name); // disabling twice is harmless
        }
        finally { WindowsAutoStart.Disable(AutoStartScope.CurrentUser, name); }
    }
}
