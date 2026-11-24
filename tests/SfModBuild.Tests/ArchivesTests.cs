using System.IO.Compression;
using System.Text.RegularExpressions;
using Xunit;

namespace SfModBuild.Tests;

/// <summary>The top-level archives list (0.4.0): more archives pack makes in dist, packed from a folder or copied.</summary>
public class ArchivesTests
{
    const string Voices = "{ \"suffix\": \"Voices\", \"source\": \"Voices\", \"compression\": \"none\" }";
    const string Extra = "{ \"suffix\": \"Extra\", \"file\": \"Original/Their - Extra.ba2\" }";

    static string Json(string archives, string more = "") => $$"""
        {
          "plugin": "MyMod.esm",
          "version": "0.1.0",{{more}}
          "builder": { "project": "Builder", "args": ["{data}", "{dist}"] },
          "archives": [ {{archives}} ]
        }
        """;

    /// <summary>A made-up mod with no scripts, a voice folder, a ready archive from another made-up mod, and a fake game.</summary>
    static (TempDir t, string sfmod, string game, FakeHostEnv env) Setup(string json)
    {
        var t = new TempDir();
        t.File(Path.Combine("mod", "Builder", "Builder.csproj"), "<Project />");
        t.File(Path.Combine("mod", "Voices", "Sound", "Voice", "MyMod.esm", "NPCMMyGuy", "0001.wem"), "wem");
        t.File(Path.Combine("mod", "Original", "Their - Extra.ba2"), "a ready archive");
        var sfmod = t.File(Path.Combine("mod", "sfmod.json"), json);
        var game = Fake.Game(t, "game");
        t.Dir(Path.Combine("game", "Data"));
        var env = Fake.EnvWithPath(t, "dotnet");
        env.ExecResult = FakeArchive2;
        return (t, sfmod, game, env);
    }

    static string Mod(TempDir t, params string[] parts) => Path.Combine(new[] { t.Path, "mod" }.Concat(parts).ToArray());

    static int RunApp(IHostEnv env, out string output, params string[] args)
    {
        var sw = new StringWriter();
        var code = App.Run(args, env, sw);
        output = sw.ToString();
        return code;
    }

    /// <summary>A fake Archive2: writes the archive named by -c=, holding the list of files in its source folder.</summary>
    static ProcessResult FakeArchive2(string exe, string args, string workDir)
    {
        var m = Regex.Match(args, "^(\"(?<sq>[^\"]+)\"|(?<sb>\\S+)) (\"-c=(?<q>[^\"]+)\"|-c=(?<b>\\S+))");
        if (!m.Success) return new ProcessResult(3, "fake archive2: no -c= in " + args);
        var source = m.Groups["sq"].Success ? m.Groups["sq"].Value : m.Groups["sb"].Value;
        var ba2 = m.Groups["q"].Success ? m.Groups["q"].Value : m.Groups["b"].Value;
        var files = Directory.GetFiles(source, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(source, f)).Order(StringComparer.OrdinalIgnoreCase);
        File.WriteAllText(ba2, string.Join("\n", files));
        return new ProcessResult(0, "");
    }

    static List<ProjectProblem> Load(string sfmod, out SfModProject? project)
    {
        var problems = new List<ProjectProblem>();
        project = SfModProject.Load(sfmod, problems);
        return problems;
    }

    // ---- the project file

    [Fact]
    public void ValidateListsEachArchiveAndTheZipHoldsThem()
    {
        var (t, sfmod, _, env) = Setup(Json(Voices + ", " + Extra));
        using (t)
        {
            var problems = Load(sfmod, out var p);
            Assert.Empty(problems);
            Assert.Equal(new[] { "MyMod - Voices.ba2", "MyMod - Extra.ba2" }, p!.ArchiveNames);
            Assert.True(p.Archives[0].Uncompressed);
            Assert.Equal(Mod(t, "Original", "Their - Extra.ba2"), p.Archives[1].File);
            var code = RunApp(env, out var output, "validate", sfmod);
            Assert.True(code == ExitCodes.Ok, output);
            Assert.Contains($"archive:  \"MyMod - Voices.ba2\" packed from {Mod(t, "Voices")}, uncompressed", output);
            Assert.Contains($"archive:  \"MyMod - Extra.ba2\" copied from {Mod(t, "Original", "Their - Extra.ba2")}", output);
            Assert.Contains("holds MyMod.esm, MyMod - Voices.ba2, MyMod - Extra.ba2", output);
        }
    }

    [Fact]
    public void ASourceFolderAStepWillFillIsntNeededAtValidate()
    {
        var (t, sfmod, _, _) = Setup(Json("{ \"suffix\": \"Voices\", \"source\": \"Later\" }"));
        using (t)
        {
            Assert.Empty(Load(sfmod, out var p));
            Assert.Equal(Mod(t, "Later"), p!.Archives[0].Source);
        }
    }

    [Fact]
    public void MainIsAllowedWhenPackDoesntMakeIt()
    {
        var (t, sfmod, _, _) = Setup(Json("{ \"suffix\": \"main\", \"file\": \"Original/Their - Extra.ba2\" }"));
        using (t)
        {
            Assert.Empty(Load(sfmod, out var p));
            Assert.Equal(new[] { "MyMod - Main.ba2" }, p!.ArchiveNames);
        }
    }

    [Theory]
    [InlineData(Voices, " \"xbox\": true,", "archives", "can't be used with \"xbox\"")]
    [InlineData("{ \"suffix\": \"Main\", \"source\": \"Voices\" }", " \"scripts\": { \"source\": \"Scripts/Source\", \"namespaces\": [\"MYM\"] },", "archives[0].suffix", "pack already makes")]
    [InlineData("{ \"suffix\": \"Main_xbox\", \"source\": \"Voices\" }", "", "archives[0].suffix", "Xbox archives")]
    [InlineData("{ \"suffix\": \"Voices.ba2\", \"source\": \"Voices\" }", "", "archives[0].suffix", "can't be the end of an archive name")]
    [InlineData("{ \"suffix\": \"Voices\", \"source\": \"Voices\", \"file\": \"Original/Their - Extra.ba2\" }", "", "archives[0]", "both")]
    [InlineData("{ \"suffix\": \"Voices\" }", "", "archives[0]", "neither")]
    [InlineData("{ \"suffix\": \"Voices\", \"source\": \"Voices\", \"compression\": \"zip\" }", "", "archives[0].compression", "isn't a compression")]
    [InlineData("{ \"suffix\": \"Voices\", \"source\": \"Voices\", \"format\": \"xbox\" }", "", "archives[0].format", "isn't an archive format")]
    [InlineData("{ \"suffix\": \"Extra\", \"file\": \"Original/Their - Extra.ba2\", \"compression\": \"none\" }", "", "archives[0].compression", "copied as it is")]
    [InlineData("{ \"suffix\": \"Voices\", \"source\": \"../elsewhere\" }", "", "archives[0].source", "isn't a folder inside the mod")]
    [InlineData("{ \"suffix\": \"Voices\", \"source\": \"dist/Voices\" }", "", "archives[0].source", "where the build deletes and replaces the files it made earlier")]
    [InlineData("{ \"suffix\": \"Voices\", \"source\": \".build/stage/Voices\" }", "", "archives[0].source", "which the build empties")]
    [InlineData("{ \"suffix\": \"Extra\", \"file\": \"Original/Missing.ba2\" }", "", "archives[0].file", "isn't there")]
    [InlineData("{ \"suffix\": \"Extra\", \"file\": \"Builder/Builder.csproj\" }", "", "archives[0].file", "doesn't end in .ba2")]
    [InlineData(Voices + ", { \"suffix\": \"voices\", \"source\": \"Voices\" }", "", "archives[1].suffix", "twice")]
    [InlineData("{ \"suffix\": \"Voices\", \"source\": \"Voices\", \"sorce\": \"x\" }", "", "archives[0].sorce", "isn't a field")]
    [InlineData("{ \"suffix\": \"Voices\", \"source\": \"Voices/Sound\" }", " \"assets\": { \"source\": \"Voices\" },", "archives[0].source", "packed into another archive")]
    [InlineData(Voices + ", { \"suffix\": \"Sub\", \"source\": \"Voices/Sound\" }", "", "archives[1].source", "packed into another archive")]
    public void BadEntriesAreRefused(string archives, string more, string field, string expected)
    {
        var (t, sfmod, _, _) = Setup(Json(archives, more));
        using (t)
        {
            t.File(Path.Combine("mod", "Scripts", "Source", "MYM", "Alpha.psc"), "ScriptName MYM:Alpha extends Quest\n");
            var problems = Load(sfmod, out var p);
            Assert.Null(p);
            Assert.Contains(problems, x => !x.IsWarning && x.Field == field && x.Problem.Contains(expected));
        }
    }

    [Fact]
    public void AnArchiveCantBeInBothLists()
    {
        var json = Json(Extra).Replace("\"archives\":", "\"zip\": { \"archives\": [\"MyMod - Extra.ba2\"] }, \"archives\":");
        var (t, sfmod, _, _) = Setup(json);
        using (t)
        {
            var problems = Load(sfmod, out var p);
            Assert.Null(p);
            Assert.Contains(problems, x => x.Field == "zip.archives" && x.Problem.Contains("also in the archives list"));
        }
    }

    // ---- pack

    [Fact]
    public void PackPacksTheSourceAndCopiesTheFile()
    {
        var (t, sfmod, game, env) = Setup(Json(Voices + ", " + Extra + ", { \"suffix\": \"Faces\", \"source\": \"Faces\", \"format\": \"dds\" }"));
        using (t)
        {
            t.File(Path.Combine("mod", "Faces", "Textures", "MyMod", "Face.dds"), "dds");
            t.File(Path.Combine("mod", "dist", "MyMod.esm"), "made-up plugin");
            t.File(Path.Combine("mod", "dist", "MyMod - Extra.ba2"), "old copy");
            var code = RunApp(env, out var output, "pack", sfmod, "--game", game);
            Assert.True(code == ExitCodes.Ok, output);
            Assert.Equal(2, env.Execs.Count);
            var voices = Mod(t, "Voices");
            Assert.Equal($"{voices} \"-c={Mod(t, "dist", "MyMod - Voices.ba2")}\" -r={voices} -compression=None -q", env.Execs[0].Arguments);
            var faces = Mod(t, "Faces");
            Assert.Equal($"{faces} \"-c={Mod(t, "dist", "MyMod - Faces.ba2")}\" -r={faces} -format=DDS -q", env.Execs[1].Arguments);
            Assert.Equal(Path.Combine("Sound", "Voice", "MyMod.esm", "NPCMMyGuy", "0001.wem"), File.ReadAllText(Mod(t, "dist", "MyMod - Voices.ba2")));
            Assert.Equal("a ready archive", File.ReadAllText(Mod(t, "dist", "MyMod - Extra.ba2")));
            Assert.DoesNotContain("WARNING", output);
            Assert.Contains($"RESULT: OK, \"MyMod - Voices.ba2\" and \"MyMod - Extra.ba2\" and \"MyMod - Faces.ba2\" made in {Mod(t, "dist")}", output);
        }
    }

    [Fact]
    public void PackDeletesAnOldCopyInOtherLetterCase()
    {
        var (t, sfmod, game, env) = Setup(Json(Extra));
        using (t)
        {
            var dist = t.CaseSensitiveDir(Path.Combine("mod", "dist"));
            if (dist == null) return;   // this machine can't hold both spellings
            File.WriteAllText(Path.Combine(dist, "mymod - extra.ba2"), "old");
            var code = RunApp(env, out var output, "pack", sfmod, "--game", game);
            Assert.True(code == ExitCodes.Ok, output);
            Assert.Equal(new[] { "MyMod - Extra.ba2" }, Directory.GetFiles(dist).Select(Path.GetFileName));
        }
    }

    [Theory]
    [InlineData(null, "isn't there")]
    [InlineData("", "holds no files")]
    [InlineData("loose.wem", "at the top of")]
    [InlineData("Sound/Voice/Old.ba2", "never goes inside an archive")]
    [InlineData(".gitkeep", "as it is")]
    [InlineData("Sound/desktop.ini", "as it is")]
    [InlineData(".git/HEAD", "as it is")]
    [InlineData("Sound/.vs/config/settings.json", "as it is")]
    public void PackRefusesABadSourceFolderBeforeArchive2Runs(string? file, string expected)
    {
        var (t, sfmod, game, env) = Setup(Json(Voices + ", { \"suffix\": \"Later\", \"source\": \"Later\" }"));
        using (t)
        {
            if (file != null) t.Dir(Path.Combine("mod", "Later"));
            if (file is { Length: > 0 }) t.File(Path.Combine("mod", "Later", file.Replace('/', Path.DirectorySeparatorChar)), "x");
            var code = RunApp(env, out var output, "pack", sfmod, "--game", game);
            Assert.Equal(ExitCodes.BadInput, code);
            Assert.Empty(env.Execs);
            Assert.Contains("RESULT: CANNOT PACK (the archives entry for \"MyMod - Later.ba2\"", output);
            Assert.Contains(expected, output);
        }
    }

    [Fact]
    public void AStepCanFillTheSourceBeforePack()
    {
        // afterPlugin copies the voice files into the folder pack takes them from
        var json = Json("{ \"suffix\": \"Voices\", \"source\": \"Picked\", \"compression\": \"none\" }")
            .Replace("\"archives\":", "\"steps\": { \"afterPlugin\": [[\"tools/pick.cmd\"]] }, \"archives\":");
        var (t, sfmod, game, env) = Setup(json);
        using (t)
        {
            t.File(Path.Combine("mod", "tools", "pick.cmd"), "made-up step");
            env.ExecResult = (exe, args, dir) =>
            {
                if (exe.EndsWith("pick.cmd", StringComparison.OrdinalIgnoreCase))
                {
                    t.File(Path.Combine("mod", "Picked", "Sound", "Voice", "0001.wem"), "wem");
                    return new ProcessResult(0, "");
                }
                if (exe.EndsWith("Archive2.exe", StringComparison.OrdinalIgnoreCase)) return FakeArchive2(exe, args, dir);
                File.WriteAllText(Mod(t, "dist", "MyMod.esm"), "made-up plugin");   // the builder
                return new ProcessResult(0, "");
            };
            Assert.True(RunApp(env, out var plugin, "plugin", sfmod, "--game", game) == ExitCodes.Ok, plugin);
            var code = RunApp(env, out var output, "pack", sfmod, "--game", game);
            Assert.True(code == ExitCodes.Ok, output);
            Assert.True(File.Exists(Mod(t, "dist", "MyMod - Voices.ba2")));
        }
    }

    // ---- zip

    [Fact]
    public void ZipShipsTheArchivesAndNeedsThem()
    {
        var (t, sfmod, _, env) = Setup(Json(Voices + ", " + Extra));
        using (t)
        {
            t.File(Path.Combine("mod", "dist", "MyMod.esm"), "made-up plugin");
            t.File(Path.Combine("mod", "dist", "MyMod - Voices.ba2"), "voices");
            var code = RunApp(env, out var output, "zip", sfmod);
            Assert.Equal(ExitCodes.BadInput, code);
            Assert.Contains("there is no \"MyMod - Extra.ba2\"", output);
            Assert.Contains("lists it in archives", output);
            t.File(Path.Combine("mod", "dist", "MyMod - Extra.ba2"), "extra");
            code = RunApp(env, out output, "zip", sfmod);
            Assert.True(code == ExitCodes.Ok, output);
            using var zip = ZipFile.OpenRead(Mod(t, "MyMod-0.1.0.zip"));
            Assert.Equal(new[] { "MyMod - Extra.ba2", "MyMod - Voices.ba2", "MyMod.esm" }, zip.Entries.Select(e => e.FullName));
        }
    }
}
