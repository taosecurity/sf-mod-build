using System.Text.RegularExpressions;
using Xunit;

namespace SfModBuild.Tests;

public partial class ZipCheckBuildTests
{
    const string GapOk = "log:   BuildKit: own records written\nlog:   gap check: running C:/Tools/sf-mod-check.exe gap\nlog:   gap check: MyMod.esm: 3 own records\nlog:   gap check:   0 error(s), 0 warning(s)\nlog:   gap check: RESULT: OK (0 errors, 0 warnings)\n";

    static Func<string, string, string, ProcessResult> WithBuilderOutput(string text, int dotnetExit = 0)
    {
        var inner = FakeAll();
        return (exe, args, wd) =>
        {
            var r = inner(exe, args, wd);
            return Path.GetFileNameWithoutExtension(exe).ToLowerInvariant() == "dotnet"
                ? new ProcessResult(dotnetExit, r.Output + text) : r;
        };
    }

    // T3 (0.4.2): the gap check's result shows in the normal build output, with no --verbose
    [Fact]
    public void BuildShowsTheGapCheckResultWithoutVerbose()
    {
        var (t, sfmod, game, env) = Setup(built: false, onPath: "sfmodcheck");
        using (t)
        {
            CompileVanillaZip(game);
            env.ExecResult = WithBuilderOutput(GapOk);
            var code = RunApp(env, out var output, "build", sfmod, "--game", game);
            Assert.True(code == ExitCodes.Ok, output);
            Assert.Contains("gap check: RESULT: OK (0 errors, 0 warnings)", output);
            Assert.DoesNotContain("BuildKit: own records written", output);
        }
    }

    [Fact]
    public void BuildShowsTheGapCheckErrorLinesWhenTheBuilderFails()
    {
        var (t, sfmod, game, env) = Setup(built: false, onPath: "sfmodcheck");
        using (t)
        {
            CompileVanillaZip(game);
            env.ExecResult = WithBuilderOutput("log:   gap check: ERROR [quest-stage] MYM_Quest: stage 20 missing\nlog:   gap check: RESULT: FAIL (1 error, 0 warnings)\n", dotnetExit: 1);
            var code = RunApp(env, out var output, "build", sfmod, "--game", game);
            Assert.Equal(ExitCodes.Failed, code);
            Assert.Contains("gap check: ERROR [quest-stage] MYM_Quest: stage 20 missing", output);
            Assert.Contains("gap check: RESULT: FAIL (1 error, 0 warnings)", output);
            // shown once, not again in the builder's last lines
            Assert.Single(Regex.Matches(output, Regex.Escape("stage 20 missing")));
        }
    }

    // BuildKit 0.5.0's own format: "  gap check: ..." with no "log:" prefix, stderr merged after stdout
    [Fact]
    public void BuildShowsTheGapResultInBuildKitsOwnFormat()
    {
        var (t, sfmod, game, env) = Setup(built: false, onPath: "sfmodcheck");
        using (t)
        {
            CompileVanillaZip(game);
            env.ExecResult = WithBuilderOutput("  gap check: running C:/RESULT/ERROR/sf-mod-check.exe gap\n  gap check:   WARNING [x] MYM_ERRORHandler unused\n  gap check: RESULT: OK (0 errors, 1 warnings)\n");
            var code = RunApp(env, out var output, "build", sfmod, "--game", game);
            Assert.True(code == ExitCodes.Ok, output);
            Assert.Contains("gap check: RESULT: OK (0 errors, 1 warnings)", output);
            Assert.DoesNotContain("MYM_ERRORHandler", output);
            Assert.DoesNotContain("C:/RESULT/ERROR", output);
        }
    }

    // T5 (0.4.2): with creations-out in use, build says where the Creations check runs
    [Fact]
    public void BuildSaysTheCreationsCheckRunsOnCreationsOut()
    {
        var (t, sfmod, game, env) = Setup(Json.Replace("\"xbox\": false", "\"xbox\": true"), built: false, onPath: "sfmodcheck");
        using (t)
        {
            CompileVanillaZip(game);
            env.ExecResult = FakeAll();
            var code = RunApp(env, out var output, "build", sfmod, "--game", game);
            Assert.True(code == ExitCodes.Ok, output);
            Assert.Contains("the Creations check runs on the creations-out folder, not on the PC zip", output);
            Assert.Contains("--profile creations", output);
        }
    }
}

/// <summary>G16/T1 (0.4.2): the README and AGENTS.md must name the current version's files, so a release can't ship
/// with last version's names in them. Skipped where the docs aren't beside the sources.</summary>
public class DocVersionTests
{
    static string? RepoRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "README.md")) && Directory.Exists(Path.Combine(d.FullName, "src"))) return d.FullName;
        return null;
    }

    [Fact]
    public void DocsNameTheCurrentVersionsFiles()
    {
        var root = RepoRoot();
        if (root == null) return;
        var csproj = File.ReadAllText(Path.Combine(root, "src", "SfModBuild", "SfModBuild.csproj"));
        var version = Regex.Match(csproj, "<Version>([^<]+)</Version>").Groups[1].Value;
        Assert.NotEmpty(version);
        foreach (var doc in new[] { "README.md", "AGENTS.md" })
        {
            var text = File.ReadAllText(Path.Combine(root, doc));
            // release file names, and "sf-mod-build <version> was tested/…" statements
            var stale = Regex.Matches(text, @"sf-mod-build(?:[.-]template)?[.-](\d+\.\d+\.\d+)(?:\.nupkg|-win-x64\.zip|\.zip)")
                .Select(m => m.Groups[1].Value).Where(v => v != version).Distinct().ToList();
            Assert.True(stale.Count == 0, $"{doc} names files of version {string.Join(", ", stale)}, not {version}");
            var tested = Regex.Matches(text, @"sf-mod-build (\d+\.\d+\.\d+) (?:was tested|\(tested)")
                .Select(m => m.Groups[1].Value).Where(v => v != version).Distinct().ToList();
            Assert.True(tested.Count == 0, $"{doc} says sf-mod-build {string.Join(", ", tested)} was tested, not {version}");
        }
        Assert.Contains($"sf-mod-build {version}", File.ReadAllText(Path.Combine(root, "AGENTS.md")));
    }
}

public partial class ZipCheckBuildTests
{
    // BuildKit says "gap check: NOT RUN" when it can't find sf-mod-check; a skipped check must not be silent
    [Fact]
    public void BuildShowsWhenTheGapCheckWasNotRun()
    {
        var (t, sfmod, game, env) = Setup(built: false, onPath: "sfmodcheck");
        using (t)
        {
            CompileVanillaZip(game);
            env.ExecResult = WithBuilderOutput("log:   gap check: NOT RUN (sf-mod-check was not found). Get it from github.com/taosecurity/sf-mod-check/releases\n");
            var code = RunApp(env, out var output, "build", sfmod, "--game", game);
            Assert.True(code == ExitCodes.Ok, output);
            Assert.Contains("gap check: NOT RUN (sf-mod-check was not found)", output);
        }
    }

    // code-review round 2: --checker reaches BuildKit's gap check, not only the check step
    [Fact]
    public void BuildGivesTheCheckerToTheGapCheck()
    {
        var (t, sfmod, game, env) = Setup(built: false, onPath: "sfmodcheck");
        using (t)
        {
            CompileVanillaZip(game);
            var checker = t.File("tools/sfmodcheck.exe");
            env.ExecResult = WithBuilderOutput(GapOk);
            var code = RunApp(env, out var output, "build", sfmod, "--game", game, "--checker", checker);
            Assert.True(code == ExitCodes.Ok, output);
            var i = env.Execs.FindIndex(e => Path.GetFileNameWithoutExtension(e.Exe).ToLowerInvariant() == "dotnet");
            Assert.True(i >= 0, output);
            Assert.Equal(Path.GetFullPath(checker), env.ExecEnvironments[i]?[Plugin.GapCheckerVariable]);
        }
    }

    // code-review round 2: a gap FAIL the builder doesn't stop on still gets a WARNING
    [Fact]
    public void BuildWarnsWhenTheGapCheckFailedButTheBuilderDidNot()
    {
        var (t, sfmod, game, env) = Setup(built: false, onPath: "sfmodcheck");
        using (t)
        {
            CompileVanillaZip(game);
            env.ExecResult = WithBuilderOutput("log:   gap check: ERROR MyMod.esm: something\nlog:   gap check: RESULT: FAIL (1 error, 0 warnings)\n");
            var code = RunApp(env, out var output, "build", sfmod, "--game", game);
            Assert.True(code == ExitCodes.Ok, output);
            Assert.Contains("WARNING  the gap check found errors", output);
        }
    }

    // code-review round 3: a checker older than 0.15.0 (no gap command) isn't handed to BuildKit's gap check
    // code-review round 4: a version number too big for an int gives the note, not a crash
    [Theory]
    [InlineData("sfmodcheck 0.10.0 by Richard Bejtlich - GPL-3.0\n")]
    [InlineData("build 12345678901.2.3\n")]
    public void BuildKeepsAnOldCheckerAwayFromTheGapCheck(string versionOutput)
    {
        var (t, sfmod, game, env) = Setup(built: false, onPath: "sfmodcheck");
        using (t)
        {
            CompileVanillaZip(game);
            var checker = t.File("tools/sfmodcheck.exe");
            var old = env.RunResult;
            env.RunResult = (exe, args) => args == "--version" ? versionOutput : old(exe, args);
            env.ExecResult = WithBuilderOutput(GapOk);
            var code = RunApp(env, out var output, "build", sfmod, "--game", game, "--checker", checker);
            Assert.True(code == ExitCodes.Ok, output);
            var i = env.Execs.FindIndex(e => Path.GetFileNameWithoutExtension(e.Exe).ToLowerInvariant() == "dotnet");
            Assert.True(i >= 0, output);
            Assert.Null(env.ExecEnvironments[i]);
            Assert.Contains("note: sfmodcheck.exe doesn't look like sf-mod-check 0.15.0 or later", output);
        }
    }

    // code-review round 3: no "builder still succeeded" WARNING when the builder failed
    [Fact]
    public void BuildDoesNotWarnWhenTheBuilderFailedOnTheGapCheck()
    {
        var (t, sfmod, game, env) = Setup(built: false, onPath: "sfmodcheck");
        using (t)
        {
            CompileVanillaZip(game);
            env.ExecResult = WithBuilderOutput("log:   gap check: ERROR MyMod.esm: something\nlog:   gap check: RESULT: FAIL (1 error, 0 warnings)\n", dotnetExit: 1);
            var code = RunApp(env, out var output, "build", sfmod, "--game", game);
            Assert.Equal(ExitCodes.Failed, code);
            Assert.DoesNotContain("WARNING  the gap check found errors", output);
        }
    }
}
