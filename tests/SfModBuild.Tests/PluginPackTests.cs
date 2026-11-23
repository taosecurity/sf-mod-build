using System.Text.RegularExpressions;
using Xunit;

namespace SfModBuild.Tests;

public partial class PluginPackTests
{
    const string Json = """
        {
          "plugin": "MyMod.esm",
          "version": "0.1.0",
          "scripts": { "source": "Scripts/Source", "namespaces": ["MYM"] },
          "builder": { "project": "Builder", "args": ["{data}", "{dist}", "{version}", "{scripts}", "--label", "two words"] }
        }
        """;

    /// <summary>A made-up mod, a fake game with a Data folder and a PATH holding a (made-up) dotnet.</summary>
    static (TempDir t, string sfmod, string game, FakeHostEnv env) Setup(string json = Json)
    {
        var t = new TempDir();
        t.File(Path.Combine("mod", "Scripts", "Source", "MYM", "Alpha.psc"), "ScriptName MYM:Alpha extends Quest\n");
        t.File(Path.Combine("mod", "Builder", "Builder.csproj"), "<Project />");
        var sfmod = t.File(Path.Combine("mod", "sfmod.json"), json);
        var game = Fake.Game(t, "game");
        t.Dir(Path.Combine("game", "Data"));
        var env = Fake.EnvWithPath(t, "dotnet");
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

    /// <summary>A fake builder: writes the plugin into the second argument after "--" (the dist folder).</summary>
    static ProcessResult FakeDotnet(string exe, string args, string workDir)
    {
        var m = Regex.Match(args, "^run -c Release --project \\S+ -- \\S+ (?<dist>\\S+) ");
        if (!m.Success) return new ProcessResult(3, "fake dotnet: unexpected command line " + args);
        File.WriteAllText(Path.Combine(m.Groups["dist"].Value, "MyMod.esm"), "made-up plugin");
        return new ProcessResult(0, "Build succeeded.\nwrote MyMod.esm\n");
    }

    /// <summary>A fake Archive2: writes the archive named by -c=.</summary>
    static ProcessResult FakeArchive2(string exe, string args, string workDir)
    {
        var m = Regex.Match(args, "\"-c=(?<q>[^\"]+)\"|(?<![\"\\S])-c=(?<b>\\S+)");
        if (!m.Success) return new ProcessResult(3, "fake archive2: no -c= in " + args);
        File.WriteAllText(m.Groups["q"].Success ? m.Groups["q"].Value : m.Groups["b"].Value, args.Contains("-compression=XBox") ? "made-up xbox ba2" : "made-up ba2");
        return new ProcessResult(0, "");
    }

    static void CompiledScripts(TempDir t) =>
        t.File(Path.Combine("mod", ".build", "stage", "Scripts", "MYM", "Alpha.pex"), "made-up pex");

    // ---- quoting and safety

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData(@"C:\x\y", @"C:\x\y")]
    [InlineData("two words", "\"two words\"")]
    [InlineData(@"C:\My Mods\dist\", "\"C:\\My Mods\\dist\\\\\"")]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    [InlineData(@"a\\""b", "\"a\\\\\\\\\\\"b\"")]
    [InlineData("", "\"\"")]
    [InlineData(@"C:\Mods\R&D\dist", "\"C:\\Mods\\R&D\\dist\"")]
    [InlineData("a|b", "\"a|b\"")]
    [InlineData("(x)^<y>", "\"(x)^<y>\"")]
    public void QuoteFollowsTheCommandLineRules(string arg, string expected) => Assert.Equal(expected, Common.Quote(arg));

    [Fact]
    public void EmptyingInsideTheGameFolderIsRefused()
    {
        var game = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "g"));
        Assert.Contains("game", Common.UnsafeToEmpty("dist", Path.Combine(game, "Data"), game, Array.Empty<string>(), new[] { game }));
        Assert.Contains("game", Common.UnsafeToEmpty("dist", Path.GetTempPath(), Path.GetPathRoot(Path.GetTempPath())!, Array.Empty<string>(), new[] { game }));
        Assert.Null(Common.UnsafeToEmpty("dist", Path.Combine(Path.GetTempPath(), "mod", "dist"), Path.Combine(Path.GetTempPath(), "mod"), Array.Empty<string>(), new[] { game }));
    }

    // ---- plugin

    [Fact]
    public void PluginRunsTheBuilderWithTheExactCommandLine()
    {
        var (t, sfmod, game, env) = Setup();
        using (t)
        {
            t.File(Path.Combine("mod", "dist", "Old.esm"), "an earlier build's plugin");
            env.ExecResult = FakeDotnet;
            var code = RunApp(env, out var output, "plugin", sfmod, "--game", game);
            Assert.True(code == ExitCodes.Ok, output);
            var (exe, args, wd) = Assert.Single(env.Execs);
            Assert.Equal(Path.Combine(t.Path, "bin", Fake.ExeName("dotnet")), exe);
            Assert.Equal(Mod(t), wd);
            var expected = $"run -c Release --project {Mod(t, "Builder", "Builder.csproj")} -- {Path.Combine(game, "Data")} {Mod(t, "dist")} 0.1.0 {Mod(t, "Scripts", "Source")} --label \"two words\"";
            Assert.Equal(expected, args);
            Assert.False(File.Exists(Mod(t, "dist", "Old.esm")));
            Assert.True(File.Exists(Mod(t, "dist", "MyMod.esm")));
            Assert.Contains("RESULT: OK", output);
        }
    }

    [Fact]
    public void PluginFailsWhenTheBuilderFails()
    {
        var (t, sfmod, game, env) = Setup();
        using (t)
        {
            env.ExecResult = (_, _, _) => new ProcessResult(1, "Program.cs(12,5): error CS1002: ; expected\n");
            var code = RunApp(env, out var output, "plugin", sfmod, "--game", game);
            Assert.Equal(ExitCodes.Failed, code);
            Assert.Contains("CS1002", output);
            Assert.Contains("exited with code 1", output);
        }
    }

    [Fact]
    public void PluginFailsWhenTheBuilderWritesNoPlugin()
    {
        var (t, sfmod, game, env) = Setup();
        using (t)
        {
            var code = RunApp(env, out var output, "plugin", sfmod, "--game", game);
            Assert.Equal(ExitCodes.Failed, code);
            Assert.Contains("wrote no", output);
            Assert.Contains("{dist}", output);
        }
    }

    [Theory]
    [InlineData(".")]
    [InlineData("Scripts")]
    [InlineData("Builder")]
    [InlineData("../game/Data")]
    [InlineData("..")]
    public void PluginRefusesToEmptyAnUnsafeDist(string dist)
    {
        var (t, sfmod, game, env) = Setup(Json.Replace("\"version\": \"0.1.0\",", $"\"version\": \"0.1.0\", \"dist\": \"{dist}\","));
        using (t)
        {
            var code = RunApp(env, out var output, "plugin", sfmod, "--game", game);
            Assert.True(code == ExitCodes.BadInput, output);
            Assert.Empty(env.Execs);
            Assert.True(File.Exists(Mod(t, "Scripts", "Source", "MYM", "Alpha.psc")));
            Assert.True(Directory.Exists(Path.Combine(game, "Data")));
        }
    }

    [Fact]
    public void PluginRefusesScriptsPlaceholderWithoutScripts()
    {
        var json = """
            { "plugin": "MyMod.esm", "version": "0.1.0", "builder": { "project": "Builder", "args": ["{dist}", "{scripts}"] } }
            """;
        var (t, sfmod, game, env) = Setup(json);
        using (t)
        {
            var code = RunApp(env, out var output, "plugin", sfmod, "--game", game);
            Assert.Equal(ExitCodes.BadInput, code);
            Assert.Contains("builder.args[1]", output);
            Assert.Empty(env.Execs);
        }
    }

    [Fact]
    public void PluginNeedsDotnet()
    {
        var (t, sfmod, game, env) = Setup();
        using (t)
        {
            env.Vars["PATH"] = t.Dir("empty");
            var code = RunApp(env, out var output, "plugin", sfmod, "--game", game);
            Assert.Equal(ExitCodes.BadInput, code);
            Assert.Contains("dotnet.microsoft.com", output);
        }
    }

    // ---- pack

    [Fact]
    public void PackWithoutScriptsHasNothingToDo()
    {
        var json = """
            { "plugin": "MyMod.esm", "version": "0.1.0", "builder": { "project": "Builder", "args": ["{dist}"] } }
            """;
        var (t, sfmod, game, env) = Setup(json);
        using (t)
        {
            var code = RunApp(env, out var output, "pack", sfmod, "--game", game);
            Assert.Equal(ExitCodes.Ok, code);
            Assert.Contains("nothing to pack", output);
            Assert.Empty(env.Execs);
        }
    }

    [Fact]
    public void PackNeedsCompiledScripts()
    {
        var (t, sfmod, game, env) = Setup();
        using (t)
        {
            var code = RunApp(env, out var output, "pack", sfmod, "--game", game);
            Assert.Equal(ExitCodes.BadInput, code);
            Assert.Contains("run sf-mod-build compile first", output);
            Assert.Empty(env.Execs);
        }
    }

    [Fact]
    public void PackRunsArchive2WithTheExactCommandLine()
    {
        var (t, sfmod, game, env) = Setup();
        using (t)
        {
            CompiledScripts(t);
            var ba2 = t.File(Path.Combine("mod", "dist", "MyMod - Main.ba2"), "stale");
            env.ExecResult = FakeArchive2;
            var code = RunApp(env, out var output, "pack", sfmod, "--game", game);
            Assert.True(code == ExitCodes.Ok, output);
            var (exe, args, wd) = Assert.Single(env.Execs);
            Assert.Equal(Path.Combine(game, "Tools", "Archive2", "Archive2.exe"), exe);
            Assert.Equal(Mod(t), wd);
            var stage = Mod(t, ".build", "stage");
            Assert.Equal($"{Path.Combine(stage, "Scripts")} \"-c={ba2}\" -r={stage} -q", args);
            Assert.Equal("made-up ba2", File.ReadAllText(ba2));
            Assert.False(Directory.Exists(Mod(t, "creations-out")));
        }
    }

    [Fact]
    public void PackWithXboxMakesTheCreationsSet()
    {
        var (t, sfmod, game, env) = Setup(Json.Replace("\"version\": \"0.1.0\",", "\"version\": \"0.1.0\", \"xbox\": true,"));
        using (t)
        {
            CompiledScripts(t);
            t.File(Path.Combine("mod", "dist", "MyMod.esm"), "made-up plugin");
            t.File(Path.Combine("mod", "dist", "MyMod-0.1.0.zip"), "an old zip");
            t.File(Path.Combine("mod", "creations-out", "stale.txt"), "old");
            env.ExecResult = FakeArchive2;
            var code = RunApp(env, out var output, "pack", sfmod, "--game", game);
            Assert.True(code == ExitCodes.Ok, output);
            Assert.Equal(2, env.Execs.Count);
            var xbox = Mod(t, "creations-out", "MyMod - Main_xbox.ba2");
            var stage = Mod(t, ".build", "stage");
            Assert.Equal($"{Path.Combine(stage, "Scripts")} \"-c={xbox}\" -r={stage} -compression=XBox -q", env.Execs[1].Arguments);
            var files = Directory.GetFiles(Mod(t, "creations-out")).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray();
            Assert.Equal(new[] { "MyMod - Main.ba2", "MyMod - Main_xbox.ba2", "MyMod.esm" }, files);
            Assert.Equal("made-up xbox ba2", File.ReadAllText(xbox));
        }
    }

    [Fact]
    public void PackFailsWhenArchive2Fails()
    {
        var (t, sfmod, game, env) = Setup();
        using (t)
        {
            CompiledScripts(t);
            env.ExecResult = (_, _, _) => new ProcessResult(1, "Archive2: can't open output file\n");
            var code = RunApp(env, out var output, "pack", sfmod, "--game", game);
            Assert.Equal(ExitCodes.Failed, code);
            Assert.Contains("can't open output file", output);
        }
    }

    [Fact]
    public void PackRefusesAnUnsafeCreationsOut()
    {
        var (t, sfmod, game, env) = Setup(Json.Replace("\"version\": \"0.1.0\",", "\"version\": \"0.1.0\", \"xbox\": true, \"creationsOut\": \"dist\","));
        using (t)
        {
            CompiledScripts(t);
            var code = RunApp(env, out var output, "pack", sfmod, "--game", game);
            Assert.True(code == ExitCodes.BadInput, output);
            Assert.Empty(env.Execs);
        }
    }

    // ---- plugin with builder.command and builder.prebuilt (0.4.0)

    const string ProjectBuilder = "\"builder\": { \"project\": \"Builder\", \"args\": [\"{data}\", \"{dist}\", \"{version}\", \"{scripts}\", \"--label\", \"two words\"] }";

    /// <summary>A fake command builder: writes the plugin into the folder after --out.</summary>
    static ProcessResult FakeCommand(string exe, string args, string workDir)
    {
        var m = Regex.Match(args, "--out (?<dist>\\S+)");
        if (!m.Success) return new ProcessResult(3, "fake builder: no --out in " + args);
        File.WriteAllText(Path.Combine(m.Groups["dist"].Value, "MyMod.esm"), "made-up plugin");
        return new ProcessResult(0, "wrote MyMod.esm\n");
    }

    [Fact]
    public void PluginRunsACommandFromAPathWithoutDotnet()
    {
        var (t, sfmod, game, env) = Setup(Json.Replace(ProjectBuilder, "\"builder\": { \"command\": [\"tools/make.cmd\", \"--out\", \"{dist}\"], \"args\": [\"{version}\", \"two words\"] }"));
        using (t)
        {
            t.File(Path.Combine("mod", "tools", "make.cmd"), "@echo off");
            env.Vars["PATH"] = t.Dir("empty-bin"); // no dotnet: a command builder doesn't need it
            env.ExecResult = FakeCommand;
            var code = RunApp(env, out var output, "plugin", sfmod, "--game", game);
            Assert.True(code == ExitCodes.Ok, output);
            var (exe, args, wd) = Assert.Single(env.Execs);
            Assert.Equal(Mod(t, "tools", "make.cmd"), exe);
            Assert.Equal(Mod(t), wd);
            Assert.Equal($"--out {Mod(t, "dist")} 0.1.0 \"two words\"", args);
            Assert.True(File.Exists(Mod(t, "dist", "MyMod.esm")));
            Assert.Contains("RESULT: OK", output);
        }
    }

    [Fact]
    public void PluginFindsABareCommandOnPath()
    {
        var (t, sfmod, game, env) = Setup(Json.Replace(ProjectBuilder, "\"builder\": { \"command\": [\"python\", \"build.py\", \"{scripts}\"], \"args\": [\"--out\", \"{dist}\"] }"));
        using (t)
        {
            t.File(Path.Combine("bin", Fake.ExeName("python")));
            env.ExecResult = FakeCommand;
            var code = RunApp(env, out var output, "plugin", sfmod, "--game", game);
            Assert.True(code == ExitCodes.Ok, output);
            var (exe, args, _) = Assert.Single(env.Execs);
            Assert.Equal(Path.Combine(t.Path, "bin", Fake.ExeName("python")), exe);
            Assert.Equal($"build.py {Mod(t, "Scripts", "Source")} --out {Mod(t, "dist")}", args);
        }
    }

    [Fact]
    public void PluginStopsWhenTheCommandIsNotOnPath()
    {
        var (t, sfmod, game, env) = Setup(Json.Replace(ProjectBuilder, "\"builder\": { \"command\": [\"nosuchbuilder\"], \"args\": [\"{dist}\"] }"));
        using (t)
        {
            var code = RunApp(env, out var output, "plugin", sfmod, "--game", game);
            Assert.True(code == ExitCodes.BadInput, output);
            Assert.Contains("\"nosuchbuilder\" wasn't found on PATH", output);
            Assert.Empty(env.Execs);
        }
    }

    [Fact]
    public void PluginFailsWhenTheCommandFails()
    {
        var (t, sfmod, game, env) = Setup(Json.Replace(ProjectBuilder, "\"builder\": { \"command\": [\"dotnet\", \"run\", \"build.cs\", \"--\"], \"args\": [\"--out\", \"{dist}\"] }"));
        using (t)
        {
            env.ExecResult = (_, _, _) => new ProcessResult(4, "build.cs(3,1): error: made-up failure\n");
            var code = RunApp(env, out var output, "plugin", sfmod, "--game", game);
            Assert.Equal(ExitCodes.Failed, code);
            Assert.Contains("exited with code 4", output);
            Assert.Contains("made-up failure", output);
        }
    }

    [Fact]
    public void PluginCopiesAPrebuiltPluginWithoutTheGameOrDotnet()
    {
        var (t, sfmod, _, env) = Setup(Json.Replace(ProjectBuilder, "\"builder\": { \"prebuilt\": \"../original/Their Mod.esm\" }"));
        using (t)
        {
            t.File(Path.Combine("original", "Their Mod.esm"), "made-up ready plugin");
            t.File(Path.Combine("mod", "dist", "Old.esm"), "an earlier build's plugin");
            env.Vars["PATH"] = t.Dir("empty-bin");
            var code = RunApp(env, out var output, "plugin", sfmod);
            Assert.True(code == ExitCodes.Ok, output);
            Assert.Empty(env.Execs);
            Assert.False(File.Exists(Mod(t, "dist", "Old.esm")));
            Assert.Equal("made-up ready plugin", File.ReadAllText(Mod(t, "dist", "MyMod.esm")));
            Assert.Equal("made-up ready plugin", File.ReadAllText(Path.Combine(t.Path, "original", "Their Mod.esm")));
            Assert.Contains("RESULT: OK, MyMod.esm copied into", output);
        }
    }
}
