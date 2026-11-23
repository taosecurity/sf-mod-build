using System.Text.RegularExpressions;
using Xunit;

namespace SfModBuild.Tests;

// 0.4.0 fix: plugin deletes only what earlier builds made in dist (0.3.0 emptied it, deleting a folder the user kept
// there), and refuses, deleting nothing, when dist holds anything else.
public partial class PluginPackTests
{
    /// <summary>A fake builder that also writes a file of another kind (an .ini) beside the plugin.</summary>
    static ProcessResult FakeDotnetWithIni(string exe, string args, string workDir)
    {
        var r = FakeDotnet(exe, args, workDir);
        var m = Regex.Match(args, "^run -c Release --project \\S+ -- \\S+ (?<dist>\\S+) ");
        if (m.Success) File.WriteAllText(Path.Combine(m.Groups["dist"].Value, "MyMod.ini"), "made-up ini");
        return r;
    }

    [Fact]
    public void PluginStopsAndDeletesNothingWhenDistHoldsWhatItDidntMake()
    {
        var (t, sfmod, game, env) = Setup();
        using (t)
        {
            t.File(Path.Combine("mod", "dist", "superseded-2026-11-04", "MyMod.esm"), "the user's old copy");
            t.File(Path.Combine("mod", "dist", "notes.txt"), "the user's notes");
            t.File(Path.Combine("mod", "dist", "MyMod.esm"), "an earlier build's plugin");
            env.ExecResult = FakeDotnet;
            var code = RunApp(env, out var output, "plugin", sfmod, "--game", game);
            Assert.Equal(ExitCodes.BadInput, code);
            Assert.Contains("\"notes.txt\"", output);
            Assert.Contains("\"superseded-2026-11-04\" (a folder)", output);
            Assert.Contains("sf-mod-build deletes only the mod's own plugins and archives (the names sfmod.json gives) and what earlier builds made there", output);
            Assert.DoesNotContain("never deletes what it didn't make", output);
            Assert.Contains("Fix: move them out of dist", output);
            Assert.Empty(env.Execs);
            Assert.Equal("the user's old copy", File.ReadAllText(Mod(t, "dist", "superseded-2026-11-04", "MyMod.esm")));
            Assert.Equal("the user's notes", File.ReadAllText(Mod(t, "dist", "notes.txt")));
            Assert.Equal("an earlier build's plugin", File.ReadAllText(Mod(t, "dist", "MyMod.esm")));
            Assert.False(File.Exists(Mod(t, "dist", Common.MadeList)));
        }
    }

    [Fact]
    public void PluginStopsOnAPluginOrArchiveThatIsntTheMods()
    {
        // 0.4.0 deleted every plugin and archive in dist, a backup the user kept there included (V5 review, 2026-11-23)
        var (t, sfmod, game, env) = Setup();
        using (t)
        {
            env.ExecResult = FakeDotnet;
            Assert.Equal(ExitCodes.Ok, RunApp(env, out _, "plugin", sfmod, "--game", game));
            t.File(Path.Combine("mod", "dist", "MyMod-backup.esm"), "the user's backup");
            t.File(Path.Combine("mod", "dist", "Other - Main.ba2"), "someone else's archive");
            t.File(Path.Combine("mod", "dist", "MyMod - Main.ba2"), "an earlier build's archive");
            var code = RunApp(env, out var output, "plugin", sfmod, "--game", game);
            Assert.Equal(ExitCodes.BadInput, code);
            Assert.Contains("\"MyMod-backup.esm\"", output);
            Assert.Contains("\"Other - Main.ba2\"", output);
            Assert.DoesNotContain("\"MyMod - Main.ba2\"", output);
            Assert.Contains("so nothing was deleted", output);
            Assert.Equal("the user's backup", File.ReadAllText(Mod(t, "dist", "MyMod-backup.esm")));
            Assert.Equal("someone else's archive", File.ReadAllText(Mod(t, "dist", "Other - Main.ba2")));
            Assert.Equal("an earlier build's archive", File.ReadAllText(Mod(t, "dist", "MyMod - Main.ba2")));
            Assert.True(File.Exists(Mod(t, "dist", "MyMod.esm")));
        }
    }

    [Fact]
    public void PluginDeletesWhatEarlierBuildsMadeAndKeepsZips()
    {
        var (t, sfmod, game, env) = Setup();
        using (t)
        {
            env.ExecResult = FakeDotnetWithIni;
            Assert.Equal(ExitCodes.Ok, RunApp(env, out var first, "plugin", sfmod, "--game", game));
            Assert.Equal(new[] { "MyMod.esm", "MyMod.ini" }, File.ReadAllLines(Mod(t, "dist", Common.MadeList)));
            // what a later pack or a user drops there: archives and zips; a hand-edited list can't reach outside dist
            t.File(Path.Combine("mod", "dist", "MyMod - Main.ba2"), "an earlier build's archive");
            t.File(Path.Combine("mod", "dist", "MyMod-0.0.9.zip"), "an older zip");
            t.File(Path.Combine("mod", "outside.txt"), "not in dist");
            File.AppendAllLines(Mod(t, "dist", Common.MadeList), new[] { "../outside.txt" });
            env.ExecResult = FakeDotnet;
            var code = RunApp(env, out var output, "plugin", sfmod, "--game", game);
            Assert.True(code == ExitCodes.Ok, output);
            Assert.False(File.Exists(Mod(t, "dist", "MyMod.ini")));
            Assert.False(File.Exists(Mod(t, "dist", "MyMod - Main.ba2")));
            Assert.True(File.Exists(Mod(t, "dist", "MyMod-0.0.9.zip")));
            Assert.True(File.Exists(Mod(t, "outside.txt")));
            Assert.Equal(new[] { "MyMod.esm" }, File.ReadAllLines(Mod(t, "dist", Common.MadeList)));
        }
    }

    [Fact]
    public void WhatAStepWritesIntoDistJoinsTheMadeListAndStaysOutOfTheZip()
    {
        var json = Json.Replace("\"builder\"", "\"steps\": { \"afterPlugin\": [[\"python\", \"notes.py\", \"{dist}\"]] }, \"builder\"");
        var (t, sfmod, game, _) = Setup(json);
        using (t)
        {
            var env = Fake.EnvWithPath(t, "dotnet", "python");
            env.ExecResult = (exe, args, wd) =>
            {
                if (!args.StartsWith("notes.py ", StringComparison.Ordinal)) return FakeDotnet(exe, args, wd);
                File.WriteAllText(Path.Combine(args["notes.py ".Length..], "MyMod-notes.txt"), "made-up notes");
                return new ProcessResult(0, "");
            };
            Assert.Equal(ExitCodes.Ok, RunApp(env, out var output, "plugin", sfmod, "--game", game));
            Assert.Equal(new[] { "MyMod-notes.txt", "MyMod.esm" }, File.ReadAllLines(Mod(t, "dist", Common.MadeList)));
            // a second build deletes the step's file instead of stopping on it
            Assert.True(RunApp(env, out output, "plugin", sfmod, "--game", game) == ExitCodes.Ok, output);
            Assert.False(Common.Shippable(Mod(t, "dist", Common.MadeList)));
        }
    }

    [Fact]
    public void AZipFolderInsideDistStaysForTheNextBuild()
    {
        var json = Json.Replace("\"builder\"", "\"zip\": { \"folder\": \"dist/release\" }, \"builder\"");
        var (t, sfmod, game, env) = Setup(json);
        using (t)
        {
            // what an earlier build's zip step left: the zip folder inside dist, holding the zip
            t.File(Path.Combine("mod", "dist", "release", "MyMod-0.1.0.zip"), "an earlier build's zip");
            t.File(Path.Combine("mod", "dist", "MyMod.esm"), "an earlier build's plugin");
            env.ExecResult = FakeDotnet;
            var code = RunApp(env, out var output, "plugin", sfmod, "--game", game);
            Assert.True(code == ExitCodes.Ok, output);
            Assert.Equal("an earlier build's zip", File.ReadAllText(Mod(t, "dist", "release", "MyMod-0.1.0.zip")));
            // the zip folder never joins the made list, so a build after zip.folder changes can't delete its zips
            Assert.DoesNotContain("release", File.ReadAllLines(Mod(t, "dist", Common.MadeList)));
            File.WriteAllText(sfmod, File.ReadAllText(sfmod).Replace("dist/release", "dist/out"));
            Assert.Equal(ExitCodes.BadInput, RunApp(env, out output, "plugin", sfmod, "--game", game));
            Assert.Contains("\"release\" (a folder)", output);
            Assert.Equal("an earlier build's zip", File.ReadAllText(Mod(t, "dist", "release", "MyMod-0.1.0.zip")));
        }
    }

    [Fact]
    public void ADeleteThatFailsKeepsTheMadeListForTheNextRun()
    {
        if (!OperatingSystem.IsWindows()) return; // an open file blocks its delete only on Windows
        var (t, sfmod, game, env) = Setup();
        using (t)
        {
            env.ExecResult = FakeDotnetWithIni;
            Assert.Equal(ExitCodes.Ok, RunApp(env, out _, "plugin", sfmod, "--game", game));
            string output;
            // the plugin is open in another program (the CK or xEdit), so its delete fails
            using (new FileStream(Mod(t, "dist", "MyMod.esm"), FileMode.Open, FileAccess.Read, FileShare.None))
                Assert.NotEqual(ExitCodes.Ok, RunApp(env, out output, "plugin", sfmod, "--game", game));
            Assert.Contains("close any program", output);
            Assert.True(File.Exists(Mod(t, "dist", Common.MadeList)));
            var code = RunApp(env, out output, "plugin", sfmod, "--game", game);
            Assert.True(code == ExitCodes.Ok, output);
        }
    }
}
