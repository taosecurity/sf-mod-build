using System.IO.Compression;
using System.Text.RegularExpressions;
using Xunit;

namespace SfModBuild.Tests;

/// <summary>parts (0.4.0): other plugins the builder writes into dist, each with optional scripts of its own.</summary>
public class PartsTests
{
    const string Extra = "{ \"plugin\": \"MyMod-Extra.esm\", \"scripts\": { \"namespaces\": [\"MYMX\"] } }";
    const string Patch = "{ \"plugin\": \"MyMod-Patch.esm\" }";

    static string Json(string parts, string more = "", string builder = "\"project\": \"Builder\", \"args\": [\"{data}\", \"{dist}\"]") => $$"""
        {
          "plugin": "MyMod.esm",
          "version": "0.1.0",{{more}}
          "scripts": { "source": "Scripts/Source", "namespaces": ["MYM"] },
          "builder": { {{builder}} },
          "parts": [ {{parts}} ]
        }
        """;

    /// <summary>A made-up mod: MYM\Alpha for the main plugin, MYMX\Gamma for the part, and a fake game.</summary>
    static (TempDir t, string sfmod, string game, FakeHostEnv env) Setup(string json)
    {
        var t = new TempDir();
        t.File(Path.Combine("mod", "Builder", "Builder.csproj"), "<Project />");
        t.File(Path.Combine("mod", "Scripts", "Source", "MYM", "Alpha.psc"), "ScriptName MYM:Alpha extends Quest\n");
        t.File(Path.Combine("mod", "Scripts", "Source", "MYMX", "Gamma.psc"), "ScriptName MYMX:Gamma extends Quest\n");
        t.File(Path.Combine("mod", "Original", "MyMod.esm"), "a ready plugin");
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

    /// <summary>A fake compiler: writes the .pex the real one would, into -o under the script's namespace folder.</summary>
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

    static List<ProjectProblem> Load(string sfmod, out SfModProject? project)
    {
        var problems = new List<ProjectProblem>();
        project = SfModProject.Load(sfmod, problems);
        return problems;
    }

    // ---- the project file

    [Fact]
    public void ValidateListsEachPartAndTheZipHoldsThem()
    {
        var (t, sfmod, _, env) = Setup(Json(Extra + ", " + Patch));
        using (t)
        {
            var problems = Load(sfmod, out var p);
            Assert.Empty(problems);
            Assert.Equal(new[] { "MyMod-Extra.esm", "MyMod-Patch.esm" }, p!.Parts.Select(x => x.Plugin));
            Assert.Equal(new[] { Mod(t, "Scripts", "Source") }, p.Parts[0].Scripts!.Sources);   // the top-level source
            Assert.Null(p.Parts[1].Scripts);
            Assert.Equal(new[] { "MyMod - Main.ba2", "MyMod-Extra - Main.ba2" }, p.ArchiveNames);
            var code = RunApp(env, out var output, "validate", sfmod);
            Assert.True(code == ExitCodes.Ok, output);
            Assert.Contains("part:     MyMod-Extra.esm (archive \"MyMod-Extra - Main.ba2\")", output);
            Assert.Contains($"          scripts from {Mod(t, "Scripts", "Source")}", output);
            Assert.Contains("part:     MyMod-Patch.esm (no scripts, so no archive)", output);
            Assert.Contains("holds MyMod.esm, MyMod - Main.ba2, and the other top-level files in dist", output);
            Assert.Contains("MyMod-Extra-0.1.0.zip", output);
            Assert.Contains("holds MyMod-Extra.esm, MyMod-Extra - Main.ba2, and a copy of each LICENSE or COPYING file", output);
            Assert.Contains("holds MyMod-Patch.esm, and a copy", output);
        }
    }

    [Fact]
    public void APartWithoutScriptsIsAllowedWithXbox()
    {
        var (t, sfmod, _, _) = Setup(Json(Patch, " \"xbox\": true,"));
        using (t)
        {
            var problems = Load(sfmod, out var p);
            Assert.True(p != null, string.Join("\n", problems.Select(x => x.Format(sfmod))));
        }
    }

    [Theory]
    [InlineData("{ \"plugin\": \"MyMod.esp\" }", "", "parts[0].plugin", "same name as the main plugin")]
    [InlineData(Patch + ", { \"plugin\": \"mymod-patch.esp\" }", "", "parts[1].plugin", "same name as parts[0].plugin")]
    [InlineData("{ \"plugin\": \"MyMod-Starfield.esm\" }", "", "parts[0].plugin", "ends in the official plugin name")]
    [InlineData("{ \"plugin\": \"MyMod-Extra.txt\" }", "", "parts[0].plugin", "doesn't end in .esm")]
    [InlineData("{ \"plugin\": \"Sub/MyMod-Extra.esm\" }", "", "parts[0].plugin", "isn't a plain file name")]
    [InlineData("{ \"scripts\": { \"namespaces\": [\"MYMX\"] } }", "", "parts[0].plugin", "it is missing")]
    [InlineData("{ \"plugin\": \"MyMod-Extra.esm\", \"scripts\": { \"files\": [\"MYM/Alpha\"] } }", "", "parts[0].scripts", "also in the top-level scripts")]
    [InlineData(Extra + ", { \"plugin\": \"MyMod-More.esm\", \"scripts\": { \"files\": [\"MYMX/Gamma\"] } }", "", "parts[1].scripts", "also in parts[0].scripts")]
    [InlineData(Extra, " \"xbox\": true,", "parts[0].scripts", "can't be used with \"xbox\"")]
    [InlineData("{ \"plugin\": \"MyMod-Extra.esm\", \"scripts\": { \"namespaces\": [\"NOPE\"] } }", "", "parts[0].scripts.namespaces[0]", "there is no folder")]
    [InlineData("{ \"plugin\": \"MyMod-Extra.esm\", \"script\": {} }", "", "parts[0].script", "isn't a field")]
    [InlineData("\"MyMod-Extra.esm\"", "", "parts[0]", "not an object")]
    public void BadPartsAreRefused(string parts, string more, string field, string expected)
    {
        var (t, sfmod, _, _) = Setup(Json(parts, more));
        using (t)
        {
            var problems = Load(sfmod, out var p);
            Assert.Null(p);
            Assert.Contains(problems, x => !x.IsWarning && x.Field == field && x.Problem.Contains(expected));
        }
    }

    [Fact]
    public void PartsMustBeAList()
    {
        var (t, sfmod, _, _) = Setup(Json("").Replace("\"parts\": [  ]", "\"parts\": " + Patch));
        using (t)
        {
            Assert.Contains(Load(sfmod, out var p), x => x.Field == "parts" && x.Problem.Contains("not a list"));
            Assert.Null(p);
        }
    }

    [Fact]
    public void APrebuiltPluginCantHaveParts()
    {
        var (t, sfmod, _, _) = Setup(Json(Patch, builder: "\"prebuilt\": \"Original/MyMod.esm\""));
        using (t)
        {
            Assert.Contains(Load(sfmod, out var p), x => x.Field == "parts" && x.Problem.Contains("prebuilt"));
            Assert.Null(p);
        }
    }

    [Fact]
    public void APartsArchiveCantTakeAnotherArchivesName()
    {
        // "MyMod - Extra.esm" packs into "MyMod - Extra - Main.ba2", the name of the archives entry with suffix "Extra - Main"
        var json = Json("{ \"plugin\": \"MyMod - Extra.esm\", \"scripts\": { \"namespaces\": [\"MYMX\"] } }")
            .Replace("\"parts\":", "\"archives\": [ { \"suffix\": \"Extra - Main\", \"source\": \"Later\" } ], \"parts\":");
        var (t, sfmod, _, _) = Setup(json);
        using (t)
        {
            Assert.Contains(Load(sfmod, out var p), x => x.Field == "parts[0].plugin" && x.Problem.Contains("another archive"));
            Assert.Null(p);
        }
    }

    [Fact]
    public void ZipFilesCantTakeAPartPluginsName()
    {
        var json = Json(Patch).Replace("\"parts\":", "\"zip\": { \"files\": { \"Original/MyMod.esm\": \"MyMod-Patch.esm\" } }, \"parts\":");
        var (t, sfmod, _, _) = Setup(json);
        using (t)
        {
            Assert.Contains(Load(sfmod, out var p), x => x.Field == "zip.files" && x.Problem.Contains("the build itself puts in the zip"));
            Assert.Null(p);
        }
    }

    // ---- compile

    [Fact]
    public void CompileWritesEachPartsScriptsIntoItsOwnFolder()
    {
        var json = Json(Extra + ", { \"plugin\": \"MyMod-Own.esm\", \"scripts\": { \"source\": \"Own\", \"files\": [\"OWN/Delta\"] } }");
        var (t, sfmod, _, env) = Setup(json);
        using (t)
        {
            t.File(Path.Combine("mod", "Own", "OWN", "Delta.psc"), "ScriptName OWN:Delta extends Quest\n");
            Assert.Empty(Load(sfmod, out var p));
            env.ExecResult = FakeCompiler;
            var sw = new StringWriter();
            var code = Compile.CompileAll(p!, Compile.ScriptList(p!), "compiler", "vanilla", env, sw);
            var output = sw.ToString();
            Assert.True(code == ExitCodes.Ok, output);
            var stage = Mod(t, ".build", "stage");
            var top = Mod(t, "Scripts", "Source");
            var own = Mod(t, "Own");
            const string vanilla = "vanilla";
            Assert.Equal(3, env.Execs.Count);
            Assert.Equal($"\"{Path.Combine("MYM", "Alpha")}.psc\" -i=\"{top};{vanilla}\" -f=\"Starfield_Papyrus_Flags.flg\" -o=\"{Path.Combine(stage, "Scripts")}\" -optimize", env.Execs[0].Arguments);
            Assert.Equal($"\"{Path.Combine("MYMX", "Gamma")}.psc\" -i=\"{top};{vanilla}\" -f=\"Starfield_Papyrus_Flags.flg\" -o=\"{Path.Combine(stage, "parts", "MyMod-Extra", "Scripts")}\" -optimize", env.Execs[1].Arguments);
            // a part's own source comes first in its imports, then the top-level one
            Assert.Equal($"\"{Path.Combine("OWN", "Delta")}.psc\" -i=\"{own};{top};{vanilla}\" -f=\"Starfield_Papyrus_Flags.flg\" -o=\"{Path.Combine(stage, "parts", "MyMod-Own", "Scripts")}\" -optimize", env.Execs[2].Arguments);
            Assert.True(File.Exists(Path.Combine(stage, "parts", "MyMod-Extra", "Scripts", "MYMX", "Gamma.pex")));
            Assert.False(File.Exists(Path.Combine(stage, "Scripts", "MYMX", "Gamma.pex")), "a part's script never goes into the main archive");
            Assert.Contains("RESULT: OK, 3 script(s) compiled into", output);
        }
    }

    [Fact]
    public void OptimizeFalseLeavesOutTheOptionAndAPartFollowsItUnlessItSetsItsOwn()
    {
        var json = Json(Extra + ", { \"plugin\": \"MyMod-Own.esm\", \"scripts\": { \"source\": \"Own\", \"files\": [\"OWN/Delta\"], \"optimize\": true } }")
            .Replace("\"namespaces\": [\"MYM\"] }", "\"namespaces\": [\"MYM\"], \"optimize\": false }");
        var (t, sfmod, _, env) = Setup(json);
        using (t)
        {
            t.File(Path.Combine("mod", "Own", "OWN", "Delta.psc"), "ScriptName OWN:Delta extends Quest\n");
            Assert.Empty(Load(sfmod, out var p));
            Assert.False(p!.Scripts!.Optimize);
            RunApp(env, out var shown, "validate", sfmod);
            Assert.Contains("compiled without -optimize", shown);
            env.Execs.Clear();
            env.ExecResult = (exe, args, workDir) =>
            {
                var m = Regex.Match(args, "^\"(?<psc>[^\"]+)\\.psc\" .* -o=\"(?<out>[^\"]+)\"( -optimize)?$");
                if (!m.Success) return new ProcessResult(3, "fake compiler: unexpected command line " + args);
                var pex = Path.Combine(m.Groups["out"].Value, m.Groups["psc"].Value + ".pex");
                Directory.CreateDirectory(Path.GetDirectoryName(pex)!);
                File.WriteAllText(pex, "made-up pex");
                return new ProcessResult(0, "Compilation succeeded.\n");
            };
            var sw = new StringWriter();
            var code = Compile.CompileAll(p, Compile.ScriptList(p), "compiler", "vanilla", env, sw);
            Assert.True(code == ExitCodes.Ok, sw.ToString());
            Assert.Equal(3, env.Execs.Count);
            Assert.DoesNotContain("-optimize", env.Execs[0].Arguments); // top-level, optimize false
            Assert.DoesNotContain("-optimize", env.Execs[1].Arguments); // MyMod-Extra follows the top-level value
            Assert.EndsWith(" -optimize", env.Execs[2].Arguments);      // MyMod-Own sets its own
        }
    }

    [Fact]
    public void OptimizeMustBeTrueOrFalse()
    {
        var json = Json(Patch).Replace("\"namespaces\": [\"MYM\"] }", "\"namespaces\": [\"MYM\"], \"optimize\": \"no\" }");
        var (t, sfmod, _, _) = Setup(json);
        using (t)
            Assert.Contains(Load(sfmod, out _), x => x.Field == "scripts.optimize" && x.Problem.Contains("not true or false"));
    }

    [Fact]
    public void OnlyAPartNeedsToHaveScripts()
    {
        var json = Json(Extra).Replace("\"scripts\": { \"source\": \"Scripts/Source\", \"namespaces\": [\"MYM\"] },", "");
        var (t, sfmod, _, env) = Setup(json);
        using (t)
        {
            var problems = Load(sfmod, out var p);
            Assert.True(p != null, string.Join("\n", problems.Select(x => x.Format(sfmod))));
            Assert.Equal(new[] { Mod(t, "Scripts", "Source") }, p!.Parts[0].Scripts!.Sources);   // the default source
            Assert.False(p.HasMainArchive);
            env.ExecResult = FakeCompiler;
            var sw = new StringWriter();
            Assert.True(Compile.CompileAll(p, Compile.ScriptList(p), "compiler", "vanilla", env, sw) == ExitCodes.Ok, sw.ToString());
            Assert.Single(env.Execs);
            Assert.True(File.Exists(Mod(t, ".build", "stage", "parts", "MyMod-Extra", "Scripts", "MYMX", "Gamma.pex")));
        }
    }

    // ---- plugin

    [Fact]
    public void PluginFailsWhenTheBuilderWritesNoPartPlugin()
    {
        var (t, sfmod, game, env) = Setup(Json(Extra + ", " + Patch));
        using (t)
        {
            env.ExecResult = (_, _, _) =>
            {
                File.WriteAllText(Mod(t, "dist", "MyMod.esm"), "made-up plugin");
                File.WriteAllText(Mod(t, "dist", "MyMod-Extra.esm"), "made-up part");
                return new ProcessResult(0, "");
            };
            var code = RunApp(env, out var output, "plugin", sfmod, "--game", game);
            Assert.Equal(ExitCodes.Failed, code);
            Assert.Contains("wrote no MyMod-Patch.esm", output);
            Assert.Contains("lists it", output);
            env.ExecResult = (_, _, _) =>
            {
                foreach (var name in new[] { "MyMod.esm", "MyMod-Extra.esm", "MyMod-Patch.esm" })
                    File.WriteAllText(Mod(t, "dist", name), "made-up plugin");
                return new ProcessResult(0, "");
            };
            code = RunApp(env, out output, "plugin", sfmod, "--game", game);
            Assert.True(code == ExitCodes.Ok, output);
            Assert.Contains($"RESULT: OK, MyMod.esm and 2 part plugin(s) built into {Mod(t, "dist")}", output);
        }
    }

    // ---- pack

    [Fact]
    public void PackMakesEachPartsArchiveFromItsOwnFolder()
    {
        var (t, sfmod, game, env) = Setup(Json(Extra + ", " + Patch));
        using (t)
        {
            t.File(Path.Combine("mod", ".build", "stage", "Scripts", "MYM", "Alpha.pex"), "pex");
            t.File(Path.Combine("mod", "dist", "MyMod.esm"), "made-up plugin");
            var code = RunApp(env, out var output, "pack", sfmod, "--game", game);
            Assert.Equal(ExitCodes.BadInput, code);
            Assert.Contains("no compiled scripts for the part MyMod-Extra.esm", output);
            Assert.Empty(env.Execs);

            t.File(Path.Combine("mod", ".build", "stage", "parts", "MyMod-Extra", "Scripts", "MYMX", "Gamma.pex"), "pex");
            t.File(Path.Combine("mod", "dist", "MyMod-Patch - Main.ba2"), "left from an earlier build");
            code = RunApp(env, out output, "pack", sfmod, "--game", game);
            Assert.True(code == ExitCodes.Ok, output);
            Assert.Equal(2, env.Execs.Count);
            var root = Mod(t, ".build", "stage", "parts", "MyMod-Extra");
            Assert.Equal($"{Path.Combine(root, "Scripts")} \"-c={Mod(t, "dist", "MyMod-Extra - Main.ba2")}\" -r={root} -q", env.Execs[1].Arguments);
            Assert.Equal(Path.Combine("MYMX", "Gamma.pex"), File.ReadAllText(Mod(t, "dist", "MyMod-Extra - Main.ba2")));
            Assert.Equal(Path.Combine("MYM", "Alpha.pex"), File.ReadAllText(Mod(t, "dist", "MyMod - Main.ba2")));
            Assert.False(File.Exists(Mod(t, "dist", "MyMod-Patch - Main.ba2")), "a records-only part's old archive is deleted");
            Assert.DoesNotContain("WARNING", output);
        }
    }

    [Fact]
    public void PackKeepsAPartsArchiveThatZipArchivesLists()
    {
        // a records-only part whose archive the builder writes on purpose, the fix the stray-archive message gives
        var (t, sfmod, game, env) = Setup(Json(Patch, "\n  \"zip\": { \"archives\": [\"MyMod-Patch - Main.ba2\"] },"));
        using (t)
        {
            Assert.Empty(Load(sfmod, out _).Where(p => !p.IsWarning));
            t.File(Path.Combine("mod", ".build", "stage", "Scripts", "MYM", "Alpha.pex"), "pex");
            t.File(Path.Combine("mod", "dist", "MyMod.esm"), "made-up plugin");
            t.File(Path.Combine("mod", "dist", "MyMod-Patch.esm"), "made-up plugin");
            t.File(Path.Combine("mod", "dist", "MyMod-Patch - Main.ba2"), "the builder's own archive");
            var code = RunApp(env, out var output, "pack", sfmod, "--game", game);
            Assert.True(code == ExitCodes.Ok, output);
            Assert.True(File.Exists(Mod(t, "dist", "MyMod-Patch - Main.ba2")), output);
            Assert.DoesNotContain("deleted", output);
        }
    }

    // ---- zip

    [Fact]
    public void ZipShipsThePartsAndNeedsThem()
    {
        var (t, sfmod, _, env) = Setup(Json(Extra + ", " + Patch));
        using (t)
        {
            foreach (var name in new[] { "MyMod.esm", "MyMod - Main.ba2", "MyMod-Extra.esm" })
                t.File(Path.Combine("mod", "dist", name), "made-up");
            var code = RunApp(env, out var output, "zip", sfmod);
            Assert.Equal(ExitCodes.BadInput, code);
            Assert.Contains("there is no \"MyMod-Extra - Main.ba2\"", output);
            t.File(Path.Combine("mod", "dist", "MyMod-Extra - Main.ba2"), "made-up");
            code = RunApp(env, out output, "zip", sfmod);
            Assert.Equal(ExitCodes.BadInput, code);
            Assert.Contains("there is no MyMod-Patch.esm", output);
            Assert.Contains("lists it in parts", output);
            t.File(Path.Combine("mod", "dist", "MyMod-Patch.esm"), "made-up");
            code = RunApp(env, out output, "zip", sfmod);
            Assert.True(code == ExitCodes.Ok, output);
            using var zip = ZipFile.OpenRead(Mod(t, "MyMod-0.1.0.zip"));
            Assert.Equal(new[] { "MyMod - Main.ba2", "MyMod.esm" }, zip.Entries.Select(e => e.FullName));
            using var extra = ZipFile.OpenRead(Mod(t, "MyMod-Extra-0.1.0.zip"));
            Assert.Equal(new[] { "MyMod-Extra - Main.ba2", "MyMod-Extra.esm" }, extra.Entries.Select(e => e.FullName));
            using var patch = ZipFile.OpenRead(Mod(t, "MyMod-Patch-0.1.0.zip"));
            Assert.Equal(new[] { "MyMod-Patch.esm" }, patch.Entries.Select(e => e.FullName));
        }
    }
}
