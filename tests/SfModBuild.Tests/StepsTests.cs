using Xunit;

namespace SfModBuild.Tests;

// steps.afterPlugin and steps.afterPack (0.4.0): reading them, validate's runs: lines, and the run order.

public partial class SfModProjectTests
{
    const string StepsJson = "\"steps\": { \"afterPlugin\": [[\"python\", \"tools/copy.py\", \"{dist}/{plugin}.esm\"]], \"afterPack\": [[\"tools/check.cmd\", \"{name}\", \"{version}\"]] }";

    static string WithSteps(string steps) => Good.Replace(ProjectBuilder, ProjectBuilder + ", " + steps);

    [Fact]
    public void StepsLoadWithTheirProgramsResolved()
    {
        using var t = Mod();
        var check = t.File(Path.Combine("tools", "check.cmd"), "@echo off");
        var (p, problems) = Load(t, WithSteps(StepsJson));
        Assert.True(p != null, All(problems));
        Assert.Empty(problems);
        Assert.Equal(new[] { "python", "tools/copy.py", "{dist}/{plugin}.esm" }, Assert.Single(p!.AfterPlugin));
        Assert.Equal(new[] { check, "{name}", "{version}" }, Assert.Single(p.AfterPack));
    }

    [Fact]
    public void LeavingOutStepsKeepsBothListsEmpty()
    {
        using var t = Mod();
        var (p, problems) = Load(t, Good);
        Assert.True(p != null, All(problems));
        Assert.Empty(p!.AfterPlugin);
        Assert.Empty(p.AfterPack);
    }

    [Theory]
    [InlineData("\"steps\": [\"python\"]", "steps", "not an object")]
    [InlineData("\"steps\": { \"afterZip\": [] }", "steps.afterZip", "isn't a field")]
    [InlineData("\"steps\": { \"afterPlugin\": \"python x.py\" }", "steps.afterPlugin", "not a list of commands")]
    [InlineData("\"steps\": { \"afterPlugin\": [\"python x.py\"] }", "steps.afterPlugin[0]", "not a list")]
    [InlineData("\"steps\": { \"afterPack\": [[]] }", "steps.afterPack[0]", "empty")]
    [InlineData("\"steps\": { \"afterPack\": [[\"python\", \"{dsit}\"]] }", "steps.afterPack[0][1]", "isn't a placeholder")]
    [InlineData("\"steps\": { \"afterPack\": [[\"{dist}/x.exe\"]] }", "steps.afterPack[0][0]", "placeholder")]
    [InlineData("\"steps\": { \"afterPlugin\": [[\"tools/nope.py\"]] }", "steps.afterPlugin[0][0]", "isn't there")]
    [InlineData("\"steps\": { \"afterPlugin\": [[\"dist/fix.cmd\"]] }", "steps.afterPlugin[0][0]", "inside dist")]
    public void BadStepsAreRefused(string steps, string field, string words)
    {
        using var t = Mod();
        t.File(Path.Combine("dist", "fix.cmd"), "@echo off");
        var (p, problems) = Load(t, WithSteps(steps));
        Assert.Null(p);
        Assert.Contains(problems, x => x.Field == field && x.Problem.Contains(words));
    }

    [Fact]
    public void ScriptsPlaceholderInAStepNeedsAScriptsSection()
    {
        using var t = Mod();
        var json = WithSteps("\"steps\": { \"afterPlugin\": [[\"python\", \"{scripts}\"]] }")
            .Replace("\"scripts\": { \"source\": \"Scripts/Source\", \"namespaces\": [\"MYM\"] },", "");
        var (p, problems) = Load(t, json);
        Assert.Null(p);
        Assert.Contains(problems, x => x.Field == "steps.afterPlugin[0]" && x.Problem.Contains("{scripts}"));
    }

    [Fact]
    public void ValidatePrintsEveryStepCommand()
    {
        using var t = Mod();
        var check = t.File(Path.Combine("tools", "check.cmd"), "@echo off");
        var file = t.File("sfmod.json", WithSteps(StepsJson));
        var o = new StringWriter();
        Assert.Equal(0, App.Run(new[] { "validate", file }, new FakeHostEnv(), o));
        Assert.Contains("runs:     (steps.afterPlugin[0]) python tools/copy.py {dist}/{plugin}.esm", o.ToString());
        Assert.Contains($"runs:     (steps.afterPack[0]) {check} {{name}} {{version}}", o.ToString());
    }
}

public partial class PluginPackTests
{
    static string WithSteps(string steps) => Json.Replace(ProjectBuilder, ProjectBuilder + ", " + steps);

    /// <summary>The fake builder and Archive2, and exit 0 for anything else (the step commands).</summary>
    static Func<string, string, string, ProcessResult> FakeWithSteps(int failAt = -1)
    {
        var steps = 0;
        return (exe, args, wd) =>
        {
            var name = Path.GetFileNameWithoutExtension(exe).ToLowerInvariant();
            if (name == "dotnet") return FakeDotnet(exe, args, wd);
            if (name == "archive2") return FakeArchive2(exe, args, wd);
            return steps++ == failAt ? new ProcessResult(5, "made-up step failure\n") : new ProcessResult(0, "step ok\n");
        };
    }

    [Fact]
    public void PluginRunsItsStepsInOrderAfterTheBuilder()
    {
        var (t, sfmod, game, env) = Setup(WithSteps("\"steps\": { \"afterPlugin\": [[\"python\", \"copy.py\", \"{dist}\", \"{plugin}\", \"{name}-{version}\"], [\"tools/fix.cmd\", \"{data}\"]], \"afterPack\": [[\"python\", \"never.py\"]] }"));
        using (t)
        {
            var fix = t.File(Path.Combine("mod", "tools", "fix.cmd"), "@echo off");
            t.File(Path.Combine("bin", Fake.ExeName("python")));
            env.ExecResult = FakeWithSteps();
            var code = RunApp(env, out var output, "plugin", sfmod, "--game", game);
            Assert.True(code == ExitCodes.Ok, output);
            Assert.Equal(3, env.Execs.Count);
            Assert.Equal("dotnet", Path.GetFileNameWithoutExtension(env.Execs[0].Exe));
            Assert.Equal(Path.Combine(t.Path, "bin", Fake.ExeName("python")), env.Execs[1].Exe);
            Assert.Equal($"copy.py {Mod(t, "dist")} MyMod MyMod-0.1.0", env.Execs[1].Arguments);
            Assert.Equal(Mod(t), env.Execs[1].WorkingDirectory);
            Assert.Equal(fix, env.Execs[2].Exe);
            Assert.Equal(Path.Combine(game, "Data"), env.Execs[2].Arguments);
            // the plugin's RESULT line comes last, after the steps passed
            var lines = output.TrimEnd().Split('\n').Select(l => l.TrimEnd('\r')).ToList();
            Assert.StartsWith("RESULT: OK, MyMod.esm built into", lines[^1]);
            Assert.Equal("ok       steps.afterPlugin[1]", lines[^2]);
            Assert.Single(lines, l => l.StartsWith("RESULT:"));
        }
    }

    [Fact]
    public void AFailingStepStopsThePluginAndIsNamed()
    {
        var (t, sfmod, game, env) = Setup(WithSteps("\"steps\": { \"afterPlugin\": [[\"python\", \"first.py\"], [\"python\", \"second.py\"]] }"));
        using (t)
        {
            t.File(Path.Combine("bin", Fake.ExeName("python")));
            env.ExecResult = FakeWithSteps(failAt: 0);
            var code = RunApp(env, out var output, "plugin", sfmod, "--game", game);
            Assert.Equal(ExitCodes.Failed, code);
            Assert.Equal(2, env.Execs.Count); // the builder and the first step; the second never ran
            Assert.Contains("made-up step failure", output);
            Assert.Contains($"RESULT: FAILED at steps.afterPlugin[0] ({Fake.ExeName("python")} exited with code 5)", output);
            Assert.DoesNotContain("RESULT: OK", output);
        }
    }

    [Fact]
    public void AMissingStepProgramStopsBeforeTheBuilder()
    {
        var (t, sfmod, game, env) = Setup(WithSteps("\"steps\": { \"afterPlugin\": [[\"nosuchtool\", \"{dist}\"]] }"));
        using (t)
        {
            env.ExecResult = FakeWithSteps();
            var code = RunApp(env, out var output, "plugin", sfmod, "--game", game);
            Assert.True(code == ExitCodes.BadInput, output);
            Assert.Empty(env.Execs);
            Assert.Contains("steps.afterPlugin[0]'s program \"nosuchtool\" wasn't found on PATH", output);
        }
    }

    [Fact]
    public void AFailingBuilderSkipsTheSteps()
    {
        var (t, sfmod, game, env) = Setup(WithSteps("\"steps\": { \"afterPlugin\": [[\"python\", \"copy.py\"]] }"));
        using (t)
        {
            t.File(Path.Combine("bin", Fake.ExeName("python")));
            env.ExecResult = (_, _, _) => new ProcessResult(1, "made-up builder failure\n");
            var code = RunApp(env, out var output, "plugin", sfmod, "--game", game);
            Assert.Equal(ExitCodes.Failed, code);
            Assert.Single(env.Execs);
            Assert.Contains("RESULT: FAILED, the plugin wasn't built", output);
        }
    }

    [Fact]
    public void PackRunsItsStepsAfterArchive2()
    {
        var (t, sfmod, game, env) = Setup(WithSteps("\"steps\": { \"afterPlugin\": [[\"python\", \"never.py\"]], \"afterPack\": [[\"python\", \"check.py\", \"{dist}\"]] }"));
        using (t)
        {
            CompiledScripts(t);
            t.File(Path.Combine("bin", Fake.ExeName("python")));
            env.ExecResult = FakeWithSteps();
            var code = RunApp(env, out var output, "pack", sfmod, "--game", game);
            Assert.True(code == ExitCodes.Ok, output);
            Assert.Equal(2, env.Execs.Count);
            Assert.Equal("archive2", Path.GetFileNameWithoutExtension(env.Execs[0].Exe).ToLowerInvariant());
            Assert.Equal($"check.py {Mod(t, "dist")}", env.Execs[1].Arguments);
            Assert.StartsWith("RESULT: OK", output.TrimEnd().Split('\n')[^1]);
        }
    }

    [Fact]
    public void PackRunsItsStepsWhenThereIsNothingToPack()
    {
        var json = WithSteps("\"steps\": { \"afterPack\": [[\"python\", \"check.py\"]] }")
            .Replace("\"scripts\": { \"source\": \"Scripts/Source\", \"namespaces\": [\"MYM\"] },", "")
            .Replace(", \"{scripts}\"", "");
        var (t, sfmod, game, env) = Setup(json);
        using (t)
        {
            t.File(Path.Combine("bin", Fake.ExeName("python")));
            env.ExecResult = FakeWithSteps(failAt: 0);
            var code = RunApp(env, out var output, "pack", sfmod, "--game", game);
            Assert.Equal(ExitCodes.Failed, code);
            Assert.Single(env.Execs);
            Assert.Contains("RESULT: FAILED at steps.afterPack[0]", output);
            Assert.DoesNotContain("nothing to pack", output);
        }
    }
}

public partial class ZipCheckBuildTests
{
    static string WithSteps(string steps) => Json.Replace("\"xbox\": false,", steps + ", \"xbox\": false,");

    [Fact]
    public void BuildRunsTheStepsWherePluginAndPackDo()
    {
        var (t, sfmod, game, env) = Setup(WithSteps("\"steps\": { \"afterPlugin\": [[\"python\", \"after-plugin.py\"]], \"afterPack\": [[\"python\", \"after-pack.py\"]] }"), built: false, onPath: new[] { "sfmodcheck", "python" });
        using (t)
        {
            CompileVanillaZip(game);
            var fake = FakeAll();
            env.ExecResult = (exe, args, wd) => Path.GetFileNameWithoutExtension(exe) == "python" ? new ProcessResult(0, "") : fake(exe, args, wd);
            var code = RunApp(env, out var output, "build", sfmod, "--game", game);
            Assert.True(code == ExitCodes.Ok, output);
            var order = env.Execs.Select(e => Path.GetFileNameWithoutExtension(e.Exe).ToLowerInvariant() + (e.Arguments.StartsWith("after") ? " " + e.Arguments : "")).ToList();
            Assert.Equal(new[] { "papyruscompiler", "dotnet", "python after-plugin.py", "archive2", "python after-pack.py", "sfmodcheck" }, order);
        }
    }

    [Fact]
    public void BuildStopsAtPackWhenAnAfterPackStepFails()
    {
        var (t, sfmod, game, env) = Setup(WithSteps("\"steps\": { \"afterPack\": [[\"python\", \"after-pack.py\"]] }"), built: false, onPath: new[] { "sfmodcheck", "python" });
        using (t)
        {
            CompileVanillaZip(game);
            var fake = FakeAll();
            env.ExecResult = (exe, args, wd) => Path.GetFileNameWithoutExtension(exe) == "python" ? new ProcessResult(2, "made-up check failure\n") : fake(exe, args, wd);
            var code = RunApp(env, out var output, "build", sfmod, "--game", game);
            Assert.Equal(ExitCodes.Failed, code);
            Assert.Contains("RESULT: FAILED at steps.afterPack[0]", output);
            Assert.Contains("RESULT: BUILD STOPPED at pack", output);
            Assert.False(File.Exists(Mod(t, "MyMod-0.1.0.zip")));
        }
    }
}
