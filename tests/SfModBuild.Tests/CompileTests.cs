using System.IO.Compression;
using System.Text.RegularExpressions;
using Xunit;

namespace SfModBuild.Tests;

public class CompileTests
{
    const string Json = """
        {
          "plugin": "MyMod.esm",
          "version": "0.1.0",
          "scripts": { "source": "Scripts/Source", "namespaces": ["MYM"], "files": ["OTH/Zed", "MYM/Beta"] },
          "builder": { "project": "Builder", "args": ["{data}", "{dist}"] }
        }
        """;

    /// <summary>A made-up mod (three scripts in two namespaces) and a fake game whose ContentResources.zip holds a
    /// made-up flags file and import script, plus things that must not be unpacked.</summary>
    static (TempDir t, string sfmod, string game, FakeHostEnv env) Setup(string json = Json)
    {
        var t = new TempDir();
        var mod = Path.Combine(t.Path, "mod");
        t.File(Path.Combine("mod", "Scripts", "Source", "MYM", "Beta.psc"), "ScriptName MYM:Beta extends Quest\n");
        t.File(Path.Combine("mod", "Scripts", "Source", "MYM", "Alpha.psc"), "ScriptName MYM:Alpha extends Quest\n");
        t.File(Path.Combine("mod", "Scripts", "Source", "OTH", "Zed.psc"), "ScriptName OTH:Zed extends Quest\n");
        t.File(Path.Combine("mod", "Scripts", "Source", "OTH", "NotListed.psc"), "ScriptName OTH:NotListed extends Quest\n");
        t.File(Path.Combine("mod", "Builder", "Builder.csproj"), "<Project />");
        var sfmod = t.File(Path.Combine("mod", "sfmod.json"), json);
        var game = Fake.Game(t, "game");
        MakeZip(Path.Combine(game, "Tools", "ContentResources.zip"), new()
        {
            ["Scripts/Source/Starfield_Papyrus_Flags.flg"] = "made-up flags",
            ["Scripts/Source/FakeBase.psc"] = "ScriptName FakeBase\n",
            ["Scripts/Source/Sub/FakeSub.psc"] = "ScriptName Sub:FakeSub\n",
            ["Scripts/Source/readme.txt"] = "not wanted",
            ["Scripts/FakeBase.pex"] = "not wanted",
            ["Other/Thing.psc"] = "not wanted",
        });
        var env = new FakeHostEnv();
        env.Vars["LOCALAPPDATA"] = t.Dir("appdata");
        env.Vars["XDG_DATA_HOME"] = Path.Combine(t.Path, "appdata");
        _ = mod;
        return (t, sfmod, game, env);
    }

    static void MakeZip(string path, Dictionary<string, string> entries)
    {
        if (File.Exists(path)) File.Delete(path);
        using var z = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, text) in entries)
        {
            using var w = new StreamWriter(z.CreateEntry(name).Open());
            w.Write(text);
        }
    }

    /// <summary>A fake compiler: records each call and writes the .pex the real one would (into -o, under the
    /// script's namespace folder).</summary>
    static ProcessResult FakeCompiler(string exe, string args, string workDir)
    {
        var m = Regex.Match(args, "^\"(?<psc>[^\"]+)\\.psc\" .* -o=\"(?<out>[^\"]+)\" -optimize$");
        if (!m.Success) return new ProcessResult(3, "fake compiler: unexpected command line " + args);
        if (!File.Exists(Path.Combine(workDir, m.Groups["psc"].Value + ".psc"))) return new ProcessResult(1, "fake compiler: no such script");
        var pex = Path.Combine(m.Groups["out"].Value, m.Groups["psc"].Value + ".pex");
        Directory.CreateDirectory(Path.GetDirectoryName(pex)!);
        File.WriteAllText(pex, "made-up pex");
        return new ProcessResult(0, "Compilation succeeded.\n");
    }

    static SfModProject LoadProject(string sfmod)
    {
        var problems = new List<ProjectProblem>();
        var p = SfModProject.Load(sfmod, problems);
        Assert.True(p != null, string.Join("\n", problems.Select(x => x.Format(sfmod))));
        return p!;
    }

    static int RunApp(IHostEnv env, out string output, params string[] args)
    {
        var sw = new StringWriter();
        var code = App.Run(args, env, sw);
        output = sw.ToString();
        return code;
    }

    [Fact]
    public void ScriptListTakesNamespacesAndFilesOnceSortedByName()
    {
        var (t, sfmod, _, _) = Setup();
        using (t)
        {
            var list = Compile.ScriptList(LoadProject(sfmod));
            Assert.Equal(new[] { @"MYM\Alpha", @"MYM\Beta", @"OTH\Zed" }, list.Select(s => s.Name));
            Assert.All(list, s => Assert.Equal(Path.Combine(t.Path, "mod", "Scripts", "Source"), s.SourceFolder));
        }
    }

    [Fact]
    public void CommandLineIsExact()
    {
        var s = new ScriptToCompile(@"C:\mod\Scripts\Source", @"MYM\Alpha");
        var line = Compile.CommandLine(s, new[] { @"C:\mod\Scripts\Source", @"C:\cache\vanilla\abc\Scripts\Source\" }, @"C:\mod\.build\stage\Scripts");
        var psc = Path.Combine("MYM", "Alpha.psc");
        Assert.Equal($"\"{psc}\" -i=\"C:\\mod\\Scripts\\Source;C:\\cache\\vanilla\\abc\\Scripts\\Source{(OperatingSystem.IsWindows() ? "" : "\\")}\" -f=\"Starfield_Papyrus_Flags.flg\" -o=\"C:\\mod\\.build\\stage\\Scripts\" -optimize", line);
    }

    [Theory]
    [InlineData("-release")]
    [InlineData("-r")]
    [InlineData("/release")]
    [InlineData("--release")]
    [InlineData("-RELEASE")]
    [InlineData("-release=true")]
    public void ReleaseIsRefusedWithTheReason(string option)
    {
        var why = Compile.RefusedOption(option);
        Assert.NotNull(why);
        Assert.Contains("Debug.Trace", why);
        var s = new ScriptToCompile("src", @"MYM\Alpha");
        var e = Assert.Throws<InvalidOperationException>(() => Compile.CommandLine(s, new[] { "src" }, "out", new[] { option }));
        Assert.Contains("Debug.Trace", e.Message);
    }

    [Theory]
    [InlineData("-optimize")]
    [InlineData("-quiet")]
    [InlineData("-final")]
    [InlineData("--game")]
    [InlineData("-o=release")]
    public void OtherOptionsAreNotRefused(string option) => Assert.Null(Compile.RefusedOption(option));

    [Fact]
    public void CompileCommandRefusesReleaseBeforeAnythingElse()
    {
        var env = new FakeHostEnv();
        var code = RunApp(env, out _, "compile", "--release");
        Assert.Equal(ExitCodes.BadInput, code);
        Assert.Empty(env.Execs);
    }

    [Fact]
    public void CompilesEveryScriptInOrderWithTheExactCommandLine()
    {
        var (t, sfmod, game, env) = Setup();
        using (t)
        {
            env.ExecResult = FakeCompiler;
            var stage = Path.Combine(t.Path, "mod", ".build", "stage");
            t.File(Path.Combine("mod", ".build", "stage", "old.txt"), "left from a previous build");
            var code = RunApp(env, out var output, "compile", sfmod, "--game", game);
            Assert.True(code == ExitCodes.Ok, output);
            Assert.Contains("RESULT: OK, 3 script(s) compiled", output);
            Assert.False(File.Exists(Path.Combine(stage, "old.txt")), "the stage folder is emptied first");
            Assert.Equal(3, env.Execs.Count);
            var compiler = Path.Combine(game, "Tools", "Papyrus Compiler", "PapyrusCompiler.exe");
            var source = Path.Combine(t.Path, "mod", "Scripts", "Source");
            var sha = VanillaCache.Sha256(Path.Combine(game, "Tools", "ContentResources.zip"));
            var vanilla = Path.Combine(t.Path, "appdata", "sf-mod-build", "vanilla", sha, "Scripts", "Source");
            var names = new[] { Path.Combine("MYM", "Alpha"), Path.Combine("MYM", "Beta"), Path.Combine("OTH", "Zed") };
            for (var i = 0; i < 3; i++)
            {
                Assert.Equal(compiler, env.Execs[i].Exe);
                Assert.Equal(source, env.Execs[i].WorkingDirectory);
                Assert.Equal($"\"{names[i]}.psc\" -i=\"{source};{vanilla}\" -f=\"Starfield_Papyrus_Flags.flg\" -o=\"{Path.Combine(stage, "Scripts")}\" -optimize", env.Execs[i].Arguments);
                Assert.True(File.Exists(Path.Combine(stage, "Scripts", names[i] + ".pex")));
            }
            Assert.DoesNotContain(env.Execs, e => e.Arguments.Contains("-release", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void VanillaIsUnpackedOnceAndOnlyPscAndFlg()
    {
        var (t, _, game, _) = Setup();
        using (t)
        {
            var zip = Path.Combine(game, "Tools", "ContentResources.zip");
            var root = Path.Combine(t.Path, "cache");
            var source = VanillaCache.Ensure(zip, root, out var error);
            Assert.True(source != null, error);
            var final = Path.GetDirectoryName(Path.GetDirectoryName(source))!;
            Assert.Equal(Path.Combine(root, "vanilla", VanillaCache.Sha256(zip)), final);
            var files = Directory.GetFiles(final, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(final, f).Replace('\\', '/')).OrderBy(f => f).ToList();
            Assert.Equal(new[] { "Scripts/Source/FakeBase.psc", "Scripts/Source/Starfield_Papyrus_Flags.flg", "Scripts/Source/Sub/FakeSub.psc" }, files);
            Assert.Single(Directory.GetDirectories(Path.Combine(root, "vanilla"))); // no partial folder left

            // a second call uses what is there: a marker file put in the cache survives
            var marker = Path.Combine(source!, "marker.psc");
            File.WriteAllText(marker, "kept");
            Assert.Equal(source, VanillaCache.Ensure(zip, root, out _));
            Assert.True(File.Exists(marker));

            // a different zip goes into a folder of its own
            MakeZip(zip, new() { ["Scripts/Source/Starfield_Papyrus_Flags.flg"] = "new flags" });
            var source2 = VanillaCache.Ensure(zip, root, out _);
            Assert.NotEqual(source, source2);
            Assert.Equal(2, Directory.GetDirectories(Path.Combine(root, "vanilla")).Length);
        }
    }

    [Fact]
    public void IncompleteCacheIsUnpackedAgain()
    {
        var (t, _, game, _) = Setup();
        using (t)
        {
            var zip = Path.Combine(game, "Tools", "ContentResources.zip");
            var root = Path.Combine(t.Path, "cache");
            var source = VanillaCache.Ensure(zip, root, out _)!;
            File.Delete(Path.Combine(source, Compile.FlagsFile));
            Assert.Equal(source, VanillaCache.Ensure(zip, root, out _));
            Assert.True(File.Exists(Path.Combine(source, Compile.FlagsFile)));
        }
    }

    [Fact]
    public void ZipWithoutFlagsFileIsRefused()
    {
        using var t = new TempDir();
        var zip = Path.Combine(t.Path, "ContentResources.zip");
        MakeZip(zip, new() { ["Scripts/Source/FakeBase.psc"] = "x" });
        Assert.Null(VanillaCache.Ensure(zip, Path.Combine(t.Path, "cache"), out var error));
        Assert.Contains("Starfield_Papyrus_Flags.flg", error);
        Assert.Empty(Directory.GetDirectories(Path.Combine(t.Path, "cache", "vanilla")));
    }

    [Fact]
    public void EntriesPointingOutsideAreSkipped()
    {
        using var t = new TempDir();
        var zip = Path.Combine(t.Path, "ContentResources.zip");
        MakeZip(zip, new()
        {
            ["Scripts/Source/Starfield_Papyrus_Flags.flg"] = "flags",
            ["Scripts/Source/../../../../evil.psc"] = "evil",
        });
        Assert.NotNull(VanillaCache.Ensure(zip, Path.Combine(t.Path, "cache"), out _));
        Assert.Empty(Directory.GetFiles(t.Path, "evil.psc", SearchOption.AllDirectories));
    }

    [Fact]
    public void CompilerFailureStopsWithItsLastLines()
    {
        var (t, sfmod, game, env) = Setup();
        using (t)
        {
            env.ExecResult = (exe, args, wd) => args.Contains("Beta")
                ? new ProcessResult(1, "Starting 1 compile threads...\nBeta.psc(3,4): variable Foo is undefined\nNo output generated for MYM\\Beta, compilation failed.\n")
                : FakeCompiler(exe, args, wd);
            var code = RunApp(env, out var output, "compile", sfmod, "--game", game);
            Assert.Equal(ExitCodes.Failed, code);
            Assert.Contains("variable Foo is undefined", output);
            Assert.Contains("RESULT: FAILED", output);
            Assert.Contains("1 of 3 compiled before it", output);
            Assert.Equal(2, env.Execs.Count); // stops at the first failure
        }
    }

    [Fact]
    public void ExitZeroWithoutPexIsAFailure()
    {
        var (t, sfmod, game, env) = Setup();
        using (t)
        {
            env.ExecResult = (_, _, _) => new ProcessResult(0, "");
            var code = RunApp(env, out var output, "compile", sfmod, "--game", game);
            Assert.Equal(ExitCodes.Failed, code);
            Assert.Contains("wrote no", output);
        }
    }

    [Fact]
    public void CompilerThatCannotRunIsAFailure()
    {
        var (t, sfmod, game, env) = Setup();
        using (t)
        {
            env.ExecResult = (_, _, _) => new ProcessResult(-1, "", "The system cannot find the file specified.");
            var code = RunApp(env, out var output, "compile", sfmod, "--game", game);
            Assert.Equal(ExitCodes.Failed, code);
            Assert.Contains("couldn't be run", output);
        }
    }

    [Fact]
    public void MissingCompilerIsASetupError()
    {
        var (t, sfmod, game, env) = Setup();
        using (t)
        {
            File.Delete(Path.Combine(game, "Tools", "Papyrus Compiler", "PapyrusCompiler.exe"));
            var code = RunApp(env, out var output, "compile", sfmod, "--game", game);
            Assert.Equal(ExitCodes.BadInput, code);
            Assert.Contains("Creation Kit", output);
            Assert.Empty(env.Execs);
        }
    }

    [Fact]
    public void ProjectWithoutScriptsHasNothingToCompile()
    {
        var (t, _, game, env) = Setup();
        using (t)
        {
            var sfmod = t.File(Path.Combine("mod", "sfmod.json"), """{ "plugin": "MyMod.esm", "version": "0.1.0", "builder": { "project": "Builder", "args": ["{data}", "{dist}"] } }""");
            var code = RunApp(env, out var output, "compile", sfmod, "--game", game);
            Assert.Equal(ExitCodes.Ok, code);
            Assert.Contains("nothing to compile", output);
            Assert.Empty(env.Execs);
        }
    }

    [Fact]
    public void StageThatHoldsTheModIsNotEmptied()
    {
        var (t, sfmod, _, env) = Setup(Json.Replace("\"version\": \"0.1.0\",", "\"version\": \"0.1.0\", \"stage\": \"..\","));
        using (t)
        {
            var problems = new List<ProjectProblem>();
            var p = SfModProject.Load(sfmod, problems);
            Assert.Null(p); // the loader refuses a stage outside the mod folder
            Assert.Contains(problems, x => x.Field == "stage" && x.Problem.Contains("isn't inside the mod's folder"));
            var code = RunApp(env, out _, "compile", sfmod);
            Assert.Equal(ExitCodes.BadInput, code);
            Assert.True(File.Exists(sfmod));
            Assert.Empty(env.Execs);
        }
    }

    [Theory]
    [InlineData("stage")]
    [InlineData("dist")]
    [InlineData("creationsOut")]
    public void OutputFoldersOutsideTheModAreRefused(string field)
    {
        var outside = Path.Combine(Path.GetTempPath(), "sf-mod-build-outside").Replace('\\', '/');
        foreach (var value in new[] { "../elsewhere", outside })
        {
            var (t, sfmod, _, _) = Setup(Json.Replace("\"version\": \"0.1.0\",", $"\"version\": \"0.1.0\", \"{field}\": \"{value}\","));
            using (t)
            {
                var problems = new List<ProjectProblem>();
                Assert.Null(SfModProject.Load(sfmod, problems));
                Assert.Contains(problems, x => x.Field == field && x.Problem.Contains("isn't inside the mod's folder"));
            }
        }
    }

    [Fact]
    public void UnsafeToEmptyRefusesAFolderOutsideTheMod()
    {
        var mod = Path.Combine(Path.GetTempPath(), "m", "mod");
        Assert.Contains("isn't inside", Common.UnsafeToEmpty("stage", Path.Combine(Path.GetTempPath(), "m", "other"), mod, Array.Empty<string>()));
        Assert.Null(Common.UnsafeToEmpty("stage", Path.Combine(mod, ".build", "stage"), mod, Array.Empty<string>()));
        // the game guard holds for stage too, even with the mod inside the game's folder
        Assert.Contains("game", Common.UnsafeToEmpty("stage", Path.Combine(mod, "stage"), mod, Array.Empty<string>(), new[] { mod }));
    }

    [Fact]
    public void ModAtADriveRootKeepsItsOutputFolders()
    {
        // the loader's IsUnder must handle "E:\" (or "/"), which keeps its separator
        var root = Path.GetPathRoot(Path.GetTempPath())!;
        Assert.Null(Common.UnsafeToEmpty("dist", Path.Combine(root, "sfmb-dist-test"), root, Array.Empty<string>()));
    }

    [Fact]
    public void OutputSpelledInAnotherCaseIsRefusedOffWindows()
    {
        // .NET ignores case in Windows and macOS paths, so this can only happen on Linux
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()) return;
        var mod = Path.Combine(Path.GetTempPath(), "sfmb-case", "Mod");
        var why = Common.UnsafeToEmpty("dist", Path.Combine(Path.GetTempPath(), "sfmb-case", "MOD", "dist"), mod, Array.Empty<string>());
        Assert.NotNull(why);
        Assert.Contains("spelled differently", why);
    }

    [Fact]
    public void CreationsOutAsTheModFolderGetsItsOwnReason()
    {
        var (t, sfmod, _, _) = Setup(Json.Replace("\"version\": \"0.1.0\",", "\"version\": \"0.1.0\", \"creationsOut\": \".\","));
        using (t)
        {
            var problems = new List<ProjectProblem>();
            Assert.Null(SfModProject.Load(sfmod, problems));
            Assert.Contains(problems, x => x.Field == "creationsOut" && x.Problem.Contains("mod's own folder"));
            Assert.DoesNotContain(problems, x => x.Problem.Contains("isn't inside"));
        }
    }

    [Fact]
    public void ReleaseAfterAValueOptionIsStillRefused()
    {
        Assert.NotNull(Compile.ReleaseAsked(new[] { "--game", "--release" }, Compile.Spec));
        Assert.Null(Compile.ReleaseAsked(new[] { "--log", "/release" }, Compile.Spec));
    }

    [Fact]
    public void LogInsideStageIsRefusedBeforeEmptying()
    {
        var (t, sfmod, _, env) = Setup();
        using (t)
        {
            var p = SfModProject.Load(sfmod, new List<ProjectProblem>())!;
            Directory.CreateDirectory(p.Stage);
            var log = Path.Combine(p.Stage, "log.txt");
            Assert.Null(Log.ToFile(log));
            try
            {
                var sw = new StringWriter();
                Assert.Equal(ExitCodes.BadInput, Compile.CompileAll(p, Compile.ScriptList(p), "compiler", "vanilla", env, sw));
                Assert.Contains("log file", sw.ToString());
                Assert.True(File.Exists(log));
            }
            finally { Log.Close(); }
        }
    }

    [Fact]
    public void StageThroughALinkIsNotEmptied()
    {
        var (t, sfmod, _, _) = Setup(Json.Replace("\"version\": \"0.1.0\",", "\"version\": \"0.1.0\", \"stage\": \"lnk/stage\","));
        using (t)
        {
            var p = SfModProject.Load(sfmod, new List<ProjectProblem>())!;
            var outside = Path.Combine(t.Path, "outside");
            Directory.CreateDirectory(Path.Combine(outside, "stage"));
            File.WriteAllText(Path.Combine(outside, "stage", "keep.txt"), "x");
            var lnk = Path.Combine(p.Folder, "lnk");
            try { Directory.CreateSymbolicLink(lnk, outside); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Windows without admin or Developer Mode: a junction needs neither
                if (!OperatingSystem.IsWindows()) throw;
                var mk = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{lnk}\" \"{outside}\"") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true })!;
                mk.WaitForExit();
                Assert.True(Directory.Exists(lnk), "mklink /J failed");
            }
            Assert.Contains("link", Compile.UnsafeStage(p));
            Assert.True(File.Exists(Path.Combine(outside, "stage", "keep.txt")));
        }
    }

    [Fact]
    public void StageInsideTheGameFolderIsNotEmptied()
    {
        var (t, sfmod, _, env) = Setup();
        using (t)
        {
            var p = SfModProject.Load(sfmod, new List<ProjectProblem>())!;
            var sw = new StringWriter();
            Assert.Equal(ExitCodes.BadInput, Compile.CompileAll(p, Compile.ScriptList(p), "compiler", "vanilla", env, sw, gameFolder: p.Folder));
            Assert.Contains("game", sw.ToString());
            Assert.Empty(env.Execs);
        }
    }

    [Fact]
    public void RealProcessGetsTheArgumentsAsGiven()
    {
        // a made-up compiler script run through the real process runner: it records its arguments and working
        // folder and writes the .pex, so the quoting the real compiler needs is checked end to end
        var (t, sfmod, _, _) = Setup();
        using (t)
        {
            var p = LoadProject(sfmod);
            var scripts = Compile.ScriptList(p).Take(1).ToList();
            var record = Path.Combine(t.Path, "args.txt");
            var pex = Path.Combine(p.Stage, "Scripts", scripts[0].RelativePex);
            string compiler;
            if (OperatingSystem.IsWindows())
            {
                compiler = t.File("fakecompiler.cmd",
                    "@echo off\r\n" +
                    $"echo %*> \"{record}\"\r\n" +
                    $"cd >> \"{record}\"\r\n" +
                    $"mkdir \"{Path.GetDirectoryName(pex)}\" 2>nul\r\n" +
                    $"echo pex> \"{pex}\"\r\n" +
                    "exit /b 0\r\n");
            }
            else
            {
                compiler = t.File("fakecompiler.sh",
                    "#!/bin/sh\n" +
                    $"printf '%s|' \"$@\" > '{record}'\necho >> '{record}'\npwd >> '{record}'\n" +
                    $"mkdir -p '{Path.GetDirectoryName(pex)}'\necho pex > '{pex}'\n");
                File.SetUnixFileMode(compiler, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            var sw = new StringWriter();
            var code = Compile.CompileAll(p, scripts, compiler, Path.Combine(t.Path, "vanilla"), new RealHostEnv(), sw);
            Assert.True(code == ExitCodes.Ok, sw.ToString());
            var lines = File.ReadAllLines(record);
            var expected = Compile.CommandLine(scripts[0], p.Scripts!.Sources.Append(Path.Combine(t.Path, "vanilla")), Path.Combine(p.Stage, "Scripts"));
            if (OperatingSystem.IsWindows()) Assert.Equal(expected, lines[0].TrimEnd());
            else Assert.StartsWith(Path.Combine("MYM", "Alpha.psc") + "|-i=", lines[0]);
            Assert.Equal(scripts[0].SourceFolder, lines[1].Trim());
        }
    }
}
