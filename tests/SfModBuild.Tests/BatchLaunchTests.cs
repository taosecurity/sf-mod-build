using SfModBuild;
using Xunit;

namespace SfModBuild.Tests;

/// <summary>Real launches of a .cmd program (Windows only): the command-line quoting must survive cmd.exe.</summary>
public class BatchLaunchTests
{
    [Fact]
    public void ABatchProgramInAFolderWithASpaceGetsItsQuotedArgument()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var t = new TempDir();
        var record = Path.Combine(t.Path, "My Mods", "record.txt");
        var exe = t.File(Path.Combine("My Mods", "tools", "check.cmd"),
            "@echo off\r\n" +
            $"echo [%~1]> \"{record}\"\r\n" +
            $"echo [%~2]>> \"{record}\"\r\n" +
            "exit /b 0\r\n");
        var arg1 = Path.Combine(t.Path, "My Mods", "dist");
        var line = Plugin.Join(new[] { arg1, "--x=1,2" }, exe);
        var r = new RealHostEnv().Exec(exe, line, t.Path, TimeSpan.FromSeconds(30));
        Assert.True(r.ExitCode == 0, r.Output);
        var lines = File.ReadAllLines(record);
        Assert.Equal($"[{arg1}]", lines[0].TrimEnd());
        Assert.Equal("[--x=1,2]", lines[1].TrimEnd());
    }
}
