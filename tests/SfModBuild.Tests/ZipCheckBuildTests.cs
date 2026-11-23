using System.IO.Compression;
using System.Text.RegularExpressions;
using Xunit;

namespace SfModBuild.Tests;

public partial class ZipCheckBuildTests
{
    const string Json = """
        {
          "plugin": "MyMod.esm",
          "version": "0.1.0",
          "scripts": { "source": "Scripts/Source", "namespaces": ["MYM"] },
          "builder": { "project": "Builder", "args": ["{data}", "{dist}", "{version}"] },
          "xbox": false,
          "zip": { "files": { "README.md": "README-{name}.md", "LICENSE": "LICENSE-{name}.txt" } }
        }
        """;

    /// <summary>A made-up mod with its build output already in dist, a fake game and a PATH with dotnet on it.</summary>
    static (TempDir t, string sfmod, string game, FakeHostEnv env) Setup(string json = Json, bool built = true, params string[] onPath)
    {
        var t = new TempDir();
        t.File(Path.Combine("mod", "Scripts", "Source", "MYM", "Alpha.psc"), "ScriptName MYM:Alpha extends Quest\n");
        t.File(Path.Combine("mod", "Builder", "Builder.csproj"), "<Project />");
        t.File(Path.Combine("mod", "README.md"), "# MyMod\n");
        t.File(Path.Combine("mod", "LICENSE"), "made-up license\n");
        var sfmod = t.File(Path.Combine("mod", "sfmod.json"), json);
        if (built)
        {
            t.File(Path.Combine("mod", "dist", "MyMod.esm"), "made-up plugin");
            t.File(Path.Combine("mod", "dist", "MyMod - Main.ba2"), "made-up ba2");
        }
        var game = Fake.Game(t, "game");
        t.Dir(Path.Combine("game", "Data"));
        var env = Fake.EnvWithPath(t, onPath.Prepend("dotnet").ToArray());
        env.Vars["LOCALAPPDATA"] = t.Dir("appdata");
        env.Vars["XDG_DATA_HOME"] = Path.Combine(t.Path, "appdata");
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

    static string Sha(string file) => Zip.Sha256(file);

    static List<string> EntryNames(string zip)
    {
        using var z = ZipFile.OpenRead(zip);
        return z.Entries.Select(e => e.FullName).ToList();
    }

    // ---- zip

    [Fact]
    public void ZipIsTheSameBytesOnEveryRun()
    {
        var (t, sfmod, _, env) = Setup();
        using (t)
        {
            var zip = Mod(t, "MyMod-0.1.0.zip");
            Assert.Equal(ExitCodes.Ok, RunApp(env, out var o1, "zip", sfmod));
            var first = Sha(zip);
            // new modification times on every input must not change the zip
            foreach (var f in Directory.GetFiles(Mod(t, "dist")).Append(Mod(t, "README.md")))
                File.SetLastWriteTime(f, DateTime.Now.AddDays(-3));
            Assert.Equal(ExitCodes.Ok, RunApp(env, out var o2, "zip", sfmod));
            Assert.Equal(first, Sha(zip));
            Assert.Contains($"sha256   {first}", o2);
            Assert.Contains("RESULT: OK", o1);
        }
    }

    [Fact]
    public void ZipEntriesAreSortedWithFixedTimesAndDeflate()
    {
        var (t, sfmod, _, env) = Setup();
        using (t)
        {
            Assert.Equal(ExitCodes.Ok, RunApp(env, out _, "zip", sfmod));
            using var z = ZipFile.OpenRead(Mod(t, "MyMod-0.1.0.zip"));
            var names = z.Entries.Select(e => e.FullName).ToList();
            Assert.Equal(new[] { "LICENSE-MyMod.txt", "MyMod - Main.ba2", "MyMod.esm", "README-MyMod.md" }, names);
            Assert.Equal(names.OrderBy(n => n, StringComparer.Ordinal), names);
            foreach (var e in z.Entries)
                Assert.Equal(new DateTime(2000, 1, 1, 0, 0, 0), e.LastWriteTime.DateTime);
            using var r = new StreamReader(z.GetEntry("README-MyMod.md")!.Open());
            Assert.Equal("# MyMod\n", r.ReadToEnd());
        }
    }

    [Fact]
    public void ZipLeavesOutScriptSourcesAndZipsInDist()
    {
        var (t, sfmod, _, env) = Setup(Json.Replace("\"zip\": {", "\"zip\": { \"folder\": \"dist\","));
        using (t)
        {
            t.File(Path.Combine("mod", "dist", "Alpha.psc"), "ScriptName MYM:Alpha");
            t.File(Path.Combine("mod", "dist", "Old-0.0.9.zip"), "old zip");
            t.File(Path.Combine("mod", "dist", "MyMod-0.1.0.zip.sfmb-partial"), "left from a crash");
            Assert.Equal(ExitCodes.Ok, RunApp(env, out var output, "zip", sfmod));
            var zip = Mod(t, "dist", "MyMod-0.1.0.zip");
            var names = EntryNames(zip);
            Assert.DoesNotContain(names, n => n.EndsWith(".psc") || n.EndsWith(".zip") || n.EndsWith(".sfmb-partial"));
            Assert.Equal(4, names.Count);
            // a second run with the zip now in dist gives the same zip
            var first = Sha(zip);
            Assert.Equal(ExitCodes.Ok, RunApp(env, out _, "zip", sfmod));
            Assert.Equal(first, Sha(zip));
            Assert.False(File.Exists(zip + ".sfmb-partial"));
        }
    }

    [Fact]
    public void ZipRefusesAnUnlistedArchiveInAModWithNoArchivesAndNamesZipArchives()
    {
        // a regex, so the test holds whether the file has LF or CRLF line endings
        var json = Regex.Replace(Json, "\"scripts\": \\{[^}]*\\},", "");
        Assert.DoesNotContain("\"scripts\"", json);
        var (t, sfmod, _, env) = Setup(json);
        using (t)
        {
            File.Delete(Mod(t, "dist", "MyMod - Main.ba2"));
            var extra = t.File(Path.Combine("mod", "dist", "MyMod - Extra.ba2"), "the builder's own archive");
            Assert.Equal(ExitCodes.BadInput, RunApp(env, out var output, "zip", sfmod));
            Assert.Contains($"RESULT: CANNOT ZIP ({extra} is an archive in dist that this build doesn't make (the mod has no archives), so the zip can't take it.", output);
            Assert.Contains("Fix: delete it from dist, or, if your builder writes it on purpose, list its name in zip.archives in sfmod.json;", output);
            Assert.False(File.Exists(Mod(t, "MyMod-0.1.0.zip")));
        }
    }

    [Fact]
    public void ZipRefusesWithoutAPlugin()
    {
        var (t, sfmod, _, env) = Setup(built: false);
        using (t)
        {
            Assert.Equal(ExitCodes.BadInput, RunApp(env, out var output, "zip", sfmod));
            Assert.Contains("run sf-mod-build plugin first", output);
            Assert.False(File.Exists(Mod(t, "MyMod-0.1.0.zip")));
        }
    }

    [Fact]
    public void ZipRefusesWithoutTheBa2WhenTheModHasScripts()
    {
        var (t, sfmod, _, env) = Setup();
        using (t)
        {
            File.Delete(Mod(t, "dist", "MyMod - Main.ba2"));
            Assert.Equal(ExitCodes.BadInput, RunApp(env, out var output, "zip", sfmod));
            Assert.Contains("sf-mod-build pack", output);
        }
    }

    [Fact]
    public void ZipRefusesAMissingZipFile()
    {
        var (t, sfmod, _, env) = Setup();
        using (t)
        {
            var problems = new List<ProjectProblem>();
            var project = SfModProject.Load(sfmod, problems)!;
            File.Delete(Mod(t, "LICENSE")); // gone after the project was read
            Assert.Equal(ExitCodes.BadInput, Zip.Write(project, new StringWriter()));
            Assert.Null(Zip.Entries(project, out var error));
            Assert.Contains("LICENSE", error);
            Assert.Contains("isn't there", error);
        }
    }

    [Fact]
    public void ZipRefusesTwoEntriesWithTheSameName()
    {
        var (t, sfmod, _, env) = Setup();
        using (t)
        {
            t.File(Path.Combine("mod", "dist", "readme-mymod.md"), "the builder wrote one too");
            Assert.Equal(ExitCodes.BadInput, RunApp(env, out var output, "zip", sfmod));
            Assert.Contains("two files would both be", output);
        }
    }

    // ---- check

    /// <summary>A fake checker: OK unless the zip name has "bad" in it; records nothing itself (env.Execs does).</summary>
    static ProcessResult FakeChecker(string exe, string args, string workDir) =>
        args.Contains("bad")
            ? new ProcessResult(1, "PROBLEM  plugin-name: made-up problem\nRESULT: FAIL (1 problems, 0 to review, 0 warnings)\n")
            : new ProcessResult(0, "WARNING  made-up warning\nRESULT: OK (0 problems, 0 to review, 1 warnings)\n");

    [Fact]
    public void CheckRunsTheCheckerOnPathWithTheExactCommandLine()
    {
        var (t, sfmod, game, env) = Setup(Json.Replace("\"xbox\": false,", "\"xbox\": false, \"check\": { \"args\": [\"--profile\", \"house\"] },"), onPath: "sfmodcheck");
        using (t)
        {
            Assert.Equal(ExitCodes.Ok, RunApp(env, out _, "zip", sfmod));
            env.ExecResult = FakeChecker;
            var code = RunApp(env, out var output, "check", sfmod, "--game", game);
            Assert.True(code == ExitCodes.Ok, output);
            var (exe, args, wd) = Assert.Single(env.Execs);
            Assert.Equal(Path.Combine(t.Path, "bin", Fake.ExeName("sfmodcheck")), exe);
            Assert.Equal(Mod(t), wd);
            Assert.Equal($"release {Mod(t, "MyMod-0.1.0.zip")} --data {Path.Combine(game, "Data")} --profile house", args);
            Assert.Contains("made-up warning", output);
            Assert.Contains("RESULT: OK", output);
        }
    }

    [Fact]
    public void CheckSaysWhenTheCheckersOutputWasCutShort()
    {
        var (t, sfmod, game, env) = Setup(onPath: "sfmodcheck");
        using (t)
        {
            Assert.Equal(ExitCodes.Ok, RunApp(env, out _, "zip", sfmod));
            env.ExecResult = (_, _, _) => new ProcessResult(0, "WARNING part of it\n", Incomplete: true);
            var code = RunApp(env, out var output, "check", sfmod, "--game", game);
            Assert.Equal(ExitCodes.Ok, code);
            Assert.Contains("cut short", output);
        }
    }

    [Fact]
    public void CheckPrefersTheNewName()
    {
        var (t, sfmod, game, env) = Setup(onPath: new[] { "sfmodcheck", "sf-mod-check" });
        using (t)
        {
            RunApp(env, out _, "zip", sfmod);
            env.ExecResult = FakeChecker;
            Assert.Equal(ExitCodes.Ok, RunApp(env, out _, "check", sfmod, "--game", game));
            Assert.EndsWith(Fake.ExeName("sf-mod-check"), Assert.Single(env.Execs).Exe);
        }
    }

    [Fact]
    public void CheckLeavesDataAloneWhenTheArgsGiveIt()
    {
        var (t, sfmod, game, env) = Setup(Json.Replace("\"xbox\": false,", "\"xbox\": false, \"check\": { \"args\": [\"--data\", \"D:/Elsewhere/Data\"] },"), onPath: "sfmodcheck");
        using (t)
        {
            RunApp(env, out _, "zip", sfmod);
            Assert.Equal(ExitCodes.Ok, RunApp(env, out _, "check", sfmod, "--game", game));
            Assert.Equal($"release {Mod(t, "MyMod-0.1.0.zip")} --data D:/Elsewhere/Data", Assert.Single(env.Execs).Arguments);
        }
    }

    [Fact]
    public void CheckPassesTheCheckersFailureThrough()
    {
        var (t, sfmod, game, env) = Setup(Json.Replace("\"zip\": {", "\"zip\": { \"name\": \"{name}-bad-{version}.zip\","), onPath: "sfmodcheck");
        using (t)
        {
            RunApp(env, out _, "zip", sfmod);
            env.ExecResult = FakeChecker;
            Assert.Equal(ExitCodes.Failed, RunApp(env, out var output, "check", sfmod, "--game", game));
            Assert.Contains("made-up problem", output);
            Assert.Contains("RESULT: FAILED", output);
            env.ExecResult = (_, _, _) => new ProcessResult(2, "can't find the game's Data folder\n");
            Assert.Equal(ExitCodes.BadInput, RunApp(env, out output, "check", sfmod, "--game", game));
            Assert.Contains("can't find the game's Data folder", output);
        }
    }

    [Fact]
    public void ANoCheckerIsAWarningUnlessRequired()
    {
        var (t, sfmod, game, env) = Setup();
        using (t)
        {
            RunApp(env, out _, "zip", sfmod);
            Assert.Equal(ExitCodes.Ok, RunApp(env, out var output, "check", sfmod, "--game", game));
            Assert.Contains("WARNING", output);
            Assert.Contains("https://github.com/taosecurity/sf-mod-check/releases", output);
            Assert.Empty(env.Execs);
            Assert.Equal(ExitCodes.BadInput, RunApp(env, out output, "check", sfmod, "--game", game, "--require-check"));
            Assert.Contains("--require-check", output);
            File.WriteAllText(sfmod, Json.Replace("\"xbox\": false,", "\"xbox\": false, \"check\": { \"required\": true },"));
            Assert.Equal(ExitCodes.BadInput, RunApp(env, out output, "check", sfmod, "--game", game));
            Assert.Contains("check.required", output);
        }
    }

    [Fact]
    public void CheckUsesTheGivenCheckerAndRefusesAMissingOne()
    {
        var (t, sfmod, game, env) = Setup(onPath: "sfmodcheck");
        using (t)
        {
            RunApp(env, out _, "zip", sfmod);
            var mine = t.File(Path.Combine("tools", Fake.ExeName("mychecker")));
            Assert.Equal(ExitCodes.Ok, RunApp(env, out _, "check", sfmod, "--game", game, "--checker", mine));
            Assert.Equal(mine, Assert.Single(env.Execs).Exe);
            Assert.Equal(ExitCodes.BadInput, RunApp(env, out var output, "check", sfmod, "--checker", Path.Combine(t.Path, "nope.exe")));
            Assert.Contains("isn't there", output);
        }
    }

    [Fact]
    public void CheckNeedsTheZip()
    {
        var (t, sfmod, game, env) = Setup(onPath: "sfmodcheck");
        using (t)
        {
            Assert.Equal(ExitCodes.BadInput, RunApp(env, out var output, "check", sfmod, "--game", game));
            Assert.Contains("run sf-mod-build zip first", output);
            Assert.Empty(env.Execs);
        }
    }

    // ---- build

    /// <summary>Fakes every program the build runs, told apart by the program's name.</summary>
    static Func<string, string, string, ProcessResult> FakeAll(string? failing = null) => (exe, args, wd) =>
    {
        var name = Path.GetFileNameWithoutExtension(exe).ToLowerInvariant();
        if (name == failing) return new ProcessResult(1, $"fake {name}: made-up failure\n");
        switch (name)
        {
            case "papyruscompiler":
            {
                var m = Regex.Match(args, "^\"(?<psc>[^\"]+)\\.psc\" .* -o=\"(?<out>[^\"]+)\" -optimize$");
                var pex = Path.Combine(m.Groups["out"].Value, m.Groups["psc"].Value + ".pex");
                Directory.CreateDirectory(Path.GetDirectoryName(pex)!);
                File.WriteAllText(pex, "made-up pex");
                return new ProcessResult(0, "Compilation succeeded.\n");
            }
            case "dotnet":
            {
                var m = Regex.Match(args, "^run -c Release --project \\S+ -- \\S+ (?<dist>\\S+) ");
                File.WriteAllText(Path.Combine(m.Groups["dist"].Value, "MyMod.esm"), "made-up plugin");
                return new ProcessResult(0, "Build succeeded.\n");
            }
            case "archive2":
            {
                var m = Regex.Match(args, "\"-c=(?<q>[^\"]+)\"|(?<![\"\\S])-c=(?<b>\\S+)");
                File.WriteAllText(m.Groups["q"].Success ? m.Groups["q"].Value : m.Groups["b"].Value, "made-up ba2");
                return new ProcessResult(0, "");
            }
            case "sfmodcheck":
                return new ProcessResult(0, "RESULT: OK (0 problems, 0 to review, 0 warnings)\n");
            default:
                return new ProcessResult(3, $"unexpected program {exe}");
        }
    };



    [Fact]
    public void BuildRunsEveryStepInOrder()
    {
        var (t, sfmod, game, env) = Setup(built: false, onPath: "sfmodcheck");
        using (t)
        {
            CompileVanillaZip(game);
            env.ExecResult = FakeAll();
            var code = RunApp(env, out var output, "build", sfmod, "--game", game);
            Assert.True(code == ExitCodes.Ok, output);
            var order = env.Execs.Select(e => Path.GetFileNameWithoutExtension(e.Exe).ToLowerInvariant()).ToList();
            Assert.Equal(new[] { "papyruscompiler", "dotnet", "archive2", "sfmodcheck" }, order);
            var zip = Mod(t, "MyMod-0.1.0.zip");
            Assert.Contains("MyMod.esm", EntryNames(zip));
            Assert.Contains($"RESULT: OK, built {zip}", output);
            Assert.Single(output.Split('\n'), l => l.StartsWith("RESULT: OK, built"));
        }
    }

    [Theory]
    [InlineData("papyruscompiler", "compile", 1)]
    [InlineData("dotnet", "plugin", 2)]
    [InlineData("archive2", "pack", 3)]
    [InlineData("sfmodcheck", "check", 4)]
    public void BuildStopsAtTheFirstFailingStep(string failing, string step, int ran)
    {
        var (t, sfmod, game, env) = Setup(built: false, onPath: "sfmodcheck");
        using (t)
        {
            CompileVanillaZip(game);
            env.ExecResult = FakeAll(failing);
            var code = RunApp(env, out var output, "build", sfmod, "--game", game);
            Assert.Equal(ExitCodes.Failed, code);
            Assert.Equal(ran, env.Execs.Count);
            Assert.Contains($"RESULT: BUILD STOPPED at {step}", output);
        }
    }

    [Fact]
    public void BuildRefusesRelease()
    {
        var (t, sfmod, game, env) = Setup(built: false);
        using (t)
        {
            Assert.Equal(ExitCodes.BadInput, RunApp(env, out _, "build", sfmod, "-release"));
            Assert.Empty(env.Execs);
        }
    }

    /// <summary>The fake game's ContentResources.zip, holding the made-up flags file compile needs.</summary>
    static void CompileVanillaZip(string game)
    {
        var path = Path.Combine(game, "Tools", "ContentResources.zip");
        File.Delete(path);
        using var z = ZipFile.Open(path, ZipArchiveMode.Create);
        using var w = new StreamWriter(z.CreateEntry("Scripts/Source/Starfield_Papyrus_Flags.flg").Open());
        w.Write("made-up flags");
    }
}
