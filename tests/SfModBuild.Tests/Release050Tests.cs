using System.IO.Compression;
using Xunit;

namespace SfModBuild.Tests;

/// <summary>0.5.0: one zip per plugin, and the two skips from the 0.4.x reviews.</summary>
public class Release050Tests
{
    static string Json(string parts, string zip = "", string more = "") => $$"""
        {
          "plugin": "MyMod.esm",
          "version": "0.1.0",{{more}}
          "builder": { "project": "Builder", "args": ["{data}", "{dist}"] },
          "parts": [ {{parts}} ],
          "zip": { "files": { "LICENSE": "LICENSE-{name}.txt", "README.md": "README-{name}.md" }{{zip}} }
        }
        """;

    /// <summary>A made-up mod whose dist already holds the plugins (zip needs no game).</summary>
    static (TempDir t, string sfmod, FakeHostEnv env) Setup(string json, params string[] dist)
    {
        var t = new TempDir();
        t.File(Path.Combine("mod", "Builder", "Builder.csproj"), "<Project />");
        t.File(Path.Combine("mod", "LICENSE"), "licence text");
        t.File(Path.Combine("mod", "README.md"), "readme text");
        var sfmod = t.File(Path.Combine("mod", "sfmod.json"), json);
        foreach (var d in dist) t.File(Path.Combine("mod", "dist", d), "made-up " + d);
        return (t, sfmod, Fake.EnvWithPath(t, "dotnet"));
    }

    static string Mod(TempDir t, params string[] parts) => Path.Combine(new[] { t.Path, "mod" }.Concat(parts).ToArray());

    static int RunApp(IHostEnv env, out string output, params string[] args)
    {
        var sw = new StringWriter();
        var code = App.Run(args, env, sw);
        output = sw.ToString();
        return code;
    }

    static string[] Names(string zip)
    {
        using var z = ZipFile.OpenRead(zip);
        return z.Entries.Select(e => e.FullName).ToArray();
    }

    const string Patch = "{ \"plugin\": \"MyMod-Patch.esm\" }";
    const string Dlc = "{ \"plugin\": \"MyMod - Shattered Space.esm\" }";

    [Fact]
    public void ATwoPluginBuildGivesTwoZipsAndEachPartGetsItsOwnLicenseCopy()
    {
        var (t, sfmod, env) = Setup(Json(Patch), "MyMod.esm", "MyMod-Patch.esm", "FEATURES.txt");
        using (t)
        {
            var code = RunApp(env, out var output, "zip", sfmod);
            Assert.True(code == ExitCodes.Ok, output);
            Assert.Equal(new[] { "FEATURES.txt", "LICENSE-MyMod.txt", "MyMod.esm", "README-MyMod.md" }, Names(Mod(t, "MyMod-0.1.0.zip")));
            Assert.Equal(new[] { "LICENSE-MyMod-Patch.txt", "MyMod-Patch.esm" }, Names(Mod(t, "MyMod-Patch-0.1.0.zip")));
            Assert.Contains("RESULT: OK, 2 zips written", output);
        }
    }

    [Fact]
    public void ThePartWordDropsTheModNameAndKeepsLettersAndDigits()
    {
        var (t, sfmod, env) = Setup(Json(Dlc), "MyMod.esm", "MyMod - Shattered Space.esm");
        using (t)
        {
            Assert.Equal(ExitCodes.Ok, RunApp(env, out var output, "zip", sfmod));
            Assert.True(File.Exists(Mod(t, "MyMod-ShatteredSpace-0.1.0.zip")), output);
        }
    }

    [Fact]
    public void ZipPartPicksTheWordAndMustBeLettersAndDigits()
    {
        var (t, sfmod, env) = Setup(Json("{ \"plugin\": \"MyMod-Patch.esm\", \"zipPart\": \"Fixes2\" }"), "MyMod.esm", "MyMod-Patch.esm");
        using (t)
        {
            Assert.Equal(ExitCodes.Ok, RunApp(env, out var output, "zip", sfmod));
            Assert.True(File.Exists(Mod(t, "MyMod-Fixes2-0.1.0.zip")), output);
        }
        var (t2, sfmod2, env2) = Setup(Json("{ \"plugin\": \"MyMod-Patch.esm\", \"zipPart\": \"../x\" }"), "MyMod.esm", "MyMod-Patch.esm");
        using (t2)
        {
            var code = RunApp(env2, out var output, "validate", sfmod2);
            Assert.Equal(ExitCodes.BadInput, code);
            Assert.Contains("parts[0].zipPart", output);
            Assert.Contains("letters and digits only", output);
        }
    }

    [Fact]
    public void ADlcLikePartWithArchiveGoesWithItsOwnPluginNotTheMainOne()
    {
        // the builder's own archive "MyMod - Shattered Space - Main.ba2" belongs to "MyMod - Shattered Space.esm", the longer name
        var json = Json(Dlc, ", \"archives\": [\"MyMod - Shattered Space - Main.ba2\", \"MyMod - Extra.ba2\"]");
        var (t, sfmod, env) = Setup(json, "MyMod.esm", "MyMod - Shattered Space.esm", "MyMod - Shattered Space - Main.ba2", "MyMod - Extra.ba2");
        using (t)
        {
            var code = RunApp(env, out var output, "zip", sfmod);
            Assert.True(code == ExitCodes.Ok, output);
            Assert.Contains("MyMod - Extra.ba2", Names(Mod(t, "MyMod-0.1.0.zip")));
            Assert.Contains("MyMod - Shattered Space - Main.ba2", Names(Mod(t, "MyMod-ShatteredSpace-0.1.0.zip")));
            Assert.DoesNotContain("MyMod - Shattered Space - Main.ba2", Names(Mod(t, "MyMod-0.1.0.zip")));
        }
    }

    [Fact]
    public void AnArchiveNoPluginOwnsStopsTheZip()
    {
        var json = Json(Patch, ", \"archives\": [\"Loose.ba2\"]");
        var (t, sfmod, env) = Setup(json, "MyMod.esm", "MyMod-Patch.esm", "Loose.ba2");
        using (t)
        {
            var code = RunApp(env, out var output, "zip", sfmod);
            Assert.Equal(ExitCodes.BadInput, code);
            Assert.Contains("can't tell which plugin owns the archive \"Loose.ba2\"", output);
            Assert.False(File.Exists(Mod(t, "MyMod-0.1.0.zip")));
        }
    }

    [Theory]
    [InlineData("{ \"plugin\": \"MyMod-Patch.esm\" }")]
    [InlineData("")]
    public void APluginInDistThatSfmodJsonDoesNotNameStopsTheZip(string parts)
    {
        var (t, sfmod, env) = Setup(Json(parts), "MyMod.esm", "MyMod-Patch.esm", "Stranger.esp");
        using (t)
        {
            var code = RunApp(env, out var output, "zip", sfmod);
            Assert.Equal(ExitCodes.BadInput, code);
            Assert.Contains("is a plugin in dist that", output);
            Assert.Contains("list it in parts", output);
        }
    }

    [Fact]
    public void ASplitOffKeepsOneZipWithEveryPlugin()
    {
        var (t, sfmod, env) = Setup(Json(Patch, ", \"split\": false"), "MyMod.esm", "MyMod-Patch.esm");
        using (t)
        {
            Assert.Equal(ExitCodes.Ok, RunApp(env, out var output, "zip", sfmod));
            Assert.Contains("MyMod-Patch.esm", Names(Mod(t, "MyMod-0.1.0.zip")));
            Assert.False(File.Exists(Mod(t, "MyMod-Patch-0.1.0.zip")));
        }
    }

    [Fact]
    public void TwoZipsWithOneNameStopTheZipAndWriteNothing()
    {
        // both parts clean to "Patch"
        var parts = "{ \"plugin\": \"MyMod-Patch.esm\" }, { \"plugin\": \"MyMod Patch.esp\" }";
        var (t, sfmod, env) = Setup(Json(parts), "MyMod.esm", "MyMod-Patch.esm", "MyMod Patch.esp");
        using (t)
        {
            var code = RunApp(env, out var output, "zip", sfmod);
            Assert.Equal(ExitCodes.BadInput, code);
            Assert.Contains("two zips would both be", output);
            Assert.False(File.Exists(Mod(t, "MyMod-0.1.0.zip")));
        }
    }

    [Fact]
    public void ARemovedPartsOldZipIsLeftAloneWithANote()
    {
        var (t, sfmod, env) = Setup(Json(Patch), "MyMod.esm", "MyMod-Patch.esm");
        using (t)
        {
            t.File(Path.Combine("mod", "MyMod-Gone-0.1.0.zip"), "old");
            Assert.Equal(ExitCodes.Ok, RunApp(env, out var output, "zip", sfmod));
            Assert.True(File.Exists(Mod(t, "MyMod-Gone-0.1.0.zip")));
            Assert.Contains("note: MyMod-Gone-0.1.0.zip", output);
        }
    }

    [Fact]
    public void ASplitZipIsTheSameBytesOnEveryRun()
    {
        var (t, sfmod, env) = Setup(Json(Patch), "MyMod.esm", "MyMod-Patch.esm");
        using (t)
        {
            Assert.Equal(ExitCodes.Ok, RunApp(env, out _, "zip", sfmod));
            var a = File.ReadAllBytes(Mod(t, "MyMod-Patch-0.1.0.zip"));
            Assert.Equal(ExitCodes.Ok, RunApp(env, out _, "zip", sfmod));
            Assert.Equal(a, File.ReadAllBytes(Mod(t, "MyMod-Patch-0.1.0.zip")));
        }
    }

    [Fact]
    public void CheckRunsOncePerZipAndPartZipsGetTheMastersDir()
    {
        var (t, sfmod, env) = Setup(Json(Patch), "MyMod.esm", "MyMod-Patch.esm");
        using (t)
        {
            t.File(Path.Combine("bin", Fake.ExeName("sf-mod-check")));
            Assert.Equal(ExitCodes.Ok, RunApp(env, out _, "zip", sfmod));
            env.ExecResult = (_, _, _) => new ProcessResult(0, "RESULT: OK\n");
            var code = RunApp(env, out var output, "check", sfmod, "--game", Fake.Game(t, "game"));
            Assert.True(code == ExitCodes.Ok, output);
            Assert.Equal(2, env.Execs.Count);
            Assert.DoesNotContain("--masters-dir", env.Execs[0].Item2);
            Assert.Contains("MyMod-Patch-0.1.0.zip", env.Execs[1].Item2);
            Assert.Contains($"--masters-dir {Mod(t, "dist")}", env.Execs[1].Item2);
        }
    }

    [Fact]
    public void PartDocNamesFollowTheSplitRules()
    {
        Assert.Equal("LICENSE-MyMod-Patch.txt", Zip.PartDocName("LICENSE-MyMod.txt", "MyMod", "Patch"));
        Assert.Equal("LICENSE-MyMod-Patch.txt", Zip.PartDocName("LICENSE.txt", "MyMod", "Patch"));
        Assert.Equal("COPYING-MyMod-Patch", Zip.PartDocName("COPYING", "MyMod", "Patch"));
    }

    [Fact]
    public void PackWithOnlyAReadyArchiveNeedsNeitherTheGameNorArchive2()
    {
        var t = new TempDir();
        using (t)
        {
            t.File(Path.Combine("mod", "Builder", "Builder.csproj"), "<Project />");
            t.File(Path.Combine("mod", "Original", "Their - Extra.ba2"), "a ready archive");
            var sfmod = t.File(Path.Combine("mod", "sfmod.json"), """
                { "plugin": "MyMod.esm", "version": "0.1.0",
                  "builder": { "project": "Builder", "args": ["{data}", "{dist}"] },
                  "archives": [ { "suffix": "Extra", "file": "Original/Their - Extra.ba2" } ] }
                """);
            var env = Fake.EnvWithPath(t, "dotnet");
            var code = RunApp(env, out var output, "pack", sfmod);
            Assert.True(code == ExitCodes.Ok, output);
            Assert.True(File.Exists(Mod(t, "dist", "MyMod - Extra.ba2")));
            Assert.Empty(env.Execs);
        }
    }

    [Fact]
    public void AFolderOfZipsInsideDistIsNotAddedToTheMadeList()
    {
        var t = new TempDir();
        using (t)
        {
            t.File(Path.Combine("mod", "Builder", "Builder.csproj"), "<Project />");
            var sfmod = t.File(Path.Combine("mod", "sfmod.json"), """
                { "plugin": "MyMod.esm", "version": "0.1.0",
                  "builder": { "project": "Builder", "args": ["{data}", "{dist}"] },
                  "zip": { "folder": "dist/release" } }
                """);
            Directory.CreateDirectory(Mod(t, "dist"));
            var project = SfModProject.Load(sfmod, new List<ProjectProblem>());
            Assert.NotNull(project);
            var code = Common.TrackDist(Mod(t, "dist"), () =>
            {
                Directory.CreateDirectory(Mod(t, "dist", "release"));
                File.WriteAllText(Mod(t, "dist", "a.txt"), "x");
                return 0;
            }, project);
            Assert.Equal(0, code);
            var list = Common.ReadMadeList(Mod(t, "dist"));
            Assert.Contains("a.txt", list);
            Assert.DoesNotContain("release", list);
        }
    }
}
