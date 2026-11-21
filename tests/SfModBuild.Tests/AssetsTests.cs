using System.IO.Compression;
using System.Text.RegularExpressions;
using Xunit;

namespace SfModBuild.Tests;

public class AssetsTests
{
    const string Json = """
        {
          "plugin": "MyMod.esm",
          "version": "0.1.0",
          "scripts": { "source": "Scripts/Source", "namespaces": ["MYM"] },
          "builder": { "project": "Builder", "args": ["{data}", "{dist}"] },
          "assets": { "source": "Assets" }
        }
        """;

    /// <summary>A made-up mod with a script, a mesh, a material and a texture, and a fake game.</summary>
    static (TempDir t, string sfmod, string game, FakeHostEnv env) Setup(string json = Json, bool withScript = true)
    {
        var t = new TempDir();
        if (withScript) t.File(Path.Combine("mod", "Scripts", "Source", "MYM", "Alpha.psc"), "ScriptName MYM:Alpha extends Quest\n");
        t.File(Path.Combine("mod", "Builder", "Builder.csproj"), "<Project />");
        t.File(Path.Combine("mod", "Assets", "Meshes", "MyMod", "Box.nif"), "nif");
        t.File(Path.Combine("mod", "Assets", "Materials", "MyMod", "Box.mat"), "mat");
        t.File(Path.Combine("mod", "Assets", "Textures", "MyMod", "Box_color.dds"), "dds");
        t.File(Path.Combine("mod", "Assets", ".gitkeep"), "");
        t.File(Path.Combine("mod", "Assets", "Textures", "Thumbs.db"), "clutter");
        var sfmod = t.File(Path.Combine("mod", "sfmod.json"), json);
        var game = Fake.Game(t, "game");
        t.Dir(Path.Combine("game", "Data"));
        var env = Fake.EnvWithPath(t, "dotnet");
        env.ExecResult = FakeArchive2;
        return (t, sfmod, game, env);
    }

    static string Mod(TempDir t, params string[] parts) => Path.Combine(new[] { t.Path, "mod" }.Concat(parts).ToArray());

    static string WithXbox(string json) => json.Replace("\"version\": \"0.1.0\",", "\"version\": \"0.1.0\", \"xbox\": true,");

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
        var files = Directory.Exists(source)
            ? Directory.GetFiles(source, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(source, f)).Order(StringComparer.OrdinalIgnoreCase)
            : Enumerable.Empty<string>();
        File.WriteAllText(ba2, string.Join("\n", files));
        return new ProcessResult(0, "");
    }

    static void CompiledScripts(TempDir t) =>
        t.File(Path.Combine("mod", ".build", "stage", "Scripts", "MYM", "Alpha.pex"), "made-up pex");

    static string[] Listed(string ba2) => File.ReadAllText(ba2).Split('\n', StringSplitOptions.RemoveEmptyEntries);

    static List<ProjectProblem> Load(string sfmod, out SfModProject? project)
    {
        var problems = new List<ProjectProblem>();
        project = SfModProject.Load(sfmod, problems);
        return problems;
    }

    // ---- the project file

    [Fact]
    public void AssetsAreSortedIntoGeneralAndTextures()
    {
        var (t, sfmod, _, _) = Setup();
        using (t)
        {
            var problems = Load(sfmod, out var p);
            Assert.Empty(problems);
            Assert.Equal(new[] { Path.Combine("Materials", "MyMod", "Box.mat"), Path.Combine("Meshes", "MyMod", "Box.nif") }, p!.Assets!.General.Select(f => f.Relative));
            Assert.Equal(Path.Combine("Textures", "MyMod", "Box_color.dds"), Assert.Single(p.Assets.Textures).Relative);
            Assert.Equal(new[] { "MyMod - Main.ba2", "MyMod - Textures.ba2" }, p.ArchiveNames);
            Assert.False(p.Assets.UncompressedMain);
            var code = RunApp(new FakeHostEnv(), out var output, "validate", sfmod);
            Assert.Equal(ExitCodes.Ok, code);
            Assert.Contains("archives \"MyMod - Main.ba2\" and \"MyMod - Textures.ba2\"", output);
            Assert.Contains("2 general file(s), 1 texture(s)", output);
            Assert.Contains("holds MyMod.esm, MyMod - Main.ba2, MyMod - Textures.ba2", output);
        }
    }

    [Theory]
    [InlineData("Meshes/MyMod/Box.psc", "never ships")]
    [InlineData("Meshes/Other.esm", "doesn't go inside an archive")]
    [InlineData("Textures/Old - Textures.ba2", "doesn't go inside an archive")]
    [InlineData("README.md", "top of the assets folder")]
    public void FilesThatDontBelongAreRefused(string relative, string expected)
    {
        var (t, sfmod, _, _) = Setup();
        using (t)
        {
            t.File(Path.Combine("mod", "Assets", relative.Replace('/', Path.DirectorySeparatorChar)), "x");
            var problems = Load(sfmod, out var p);
            Assert.Null(p);
            Assert.Contains(problems, x => !x.IsWarning && x.Problem.Contains(expected) && x.Field.StartsWith("assets.source ("));
        }
    }

    [Fact]
    public void SourceArtIsAWarning()
    {
        var (t, sfmod, _, _) = Setup();
        using (t)
        {
            t.File(Path.Combine("mod", "Assets", "Textures", "MyMod", "Box_color.tif"), "tif");
            var problems = Load(sfmod, out var p);
            Assert.NotNull(p);
            var w = Assert.Single(problems);
            Assert.True(w.IsWarning);
            Assert.Contains(".tif", w.Problem);
        }
    }

    [Fact]
    public void LinksInTheAssetsFolderAreLeftOut()
    {
        var (t, sfmod, _, _) = Setup();
        using (t)
        {
            var outside = t.Dir("elsewhere");
            File.WriteAllText(Path.Combine(outside, "secret.txt"), "not the mod's");
            try { Directory.CreateSymbolicLink(Mod(t, "Assets", "Meshes", "Linked"), outside); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return; }   // no right to make links here
            var problems = Load(sfmod, out var p);
            Assert.NotNull(p);
            Assert.Contains(problems, x => x.IsWarning && x.Problem.Contains("link or junction"));
            Assert.DoesNotContain(p!.Assets!.General, f => f.Relative.Contains("secret"));
        }
    }

    [Theory]
    [InlineData(".", "the mod's own folder")]
    [InlineData("dist", "which the build empties")]
    [InlineData(".build", "which the build empties")]
    [InlineData("Scripts", "a script source folder")]
    [InlineData("NoSuchFolder", "isn't there")]
    public void UnsafeOrMissingAssetFoldersAreRefused(string source, string expected)
    {
        var (t, sfmod, _, _) = Setup(Json.Replace("\"source\": \"Assets\"", $"\"source\": \"{source}\""));
        using (t)
        {
            t.Dir(Path.Combine("mod", "dist"));
            t.Dir(Path.Combine("mod", ".build"));
            var problems = Load(sfmod, out var p);
            Assert.Null(p);
            Assert.Contains(problems, x => x.Field == "assets.source" && x.Problem.Contains(expected));
        }
    }

    [Fact]
    public void AssetsSectionNeedsASource()
    {
        var (t, sfmod, _, _) = Setup(Json.Replace("{ \"source\": \"Assets\" }", "{ \"sorce\": \"Assets\" }"));
        using (t)
        {
            var problems = Load(sfmod, out var p);
            Assert.Null(p);
            Assert.Contains(problems, x => x.Field == "assets.sorce" && x.Fix.Contains("\"source\""));
            Assert.Contains(problems, x => x.Field == "assets.source" && x.Problem.Contains("missing"));
        }
    }

    [Fact]
    public void XboxTexturesWithoutXboxIsAWarning()
    {
        var (t, sfmod, _, _) = Setup(Json.Replace("{ \"source\": \"Assets\" }", "{ \"source\": \"Assets\", \"xboxTextures\": \"AssetsXbox\" }"));
        using (t)
        {
            t.File(Path.Combine("mod", "AssetsXbox", "Textures", "MyMod", "Box_color.dds"), "xbox dds");
            var problems = Load(sfmod, out var p);
            Assert.NotNull(p);
            Assert.Contains(problems, x => x.IsWarning && x.Field == "assets.xboxTextures" && x.Problem.Contains("\"xbox\" is off"));
        }
    }

    [Fact]
    public void XboxTexturesMustBeDdsAndShouldMatchThePcSet()
    {
        var json = WithXbox(Json.Replace("{ \"source\": \"Assets\" }", "{ \"source\": \"Assets\", \"xboxTextures\": \"AssetsXbox\" }"));
        var (t, sfmod, _, _) = Setup(json);
        using (t)
        {
            t.File(Path.Combine("mod", "AssetsXbox", "Textures", "MyMod", "Other.dds"), "xbox dds");
            var problems = Load(sfmod, out var p);
            Assert.NotNull(p);
            Assert.Contains(problems, x => x.IsWarning && x.Problem.Contains("1 PC texture(s) have no Xbox copy"));
            Assert.Contains(problems, x => x.IsWarning && x.Problem.Contains("1 Xbox texture(s) have no PC copy"));
            t.File(Path.Combine("mod", "AssetsXbox", "Meshes", "Box.nif"), "nif");
            problems = Load(sfmod, out p);
            Assert.Null(p);
            Assert.Contains(problems, x => !x.IsWarning && x.Problem.Contains("only .dds"));
        }
    }

    [Fact]
    public void XboxTexturesInsideTheAssetsFolderIsRefused()
    {
        var json = WithXbox(Json.Replace("{ \"source\": \"Assets\" }", "{ \"source\": \"Assets\", \"xboxTextures\": \"Assets/Textures\" }"));
        var (t, sfmod, _, _) = Setup(json);
        using (t)
        {
            var problems = Load(sfmod, out var p);
            Assert.Null(p);
            Assert.Contains(problems, x => x.Field == "assets.xboxTextures" && x.Problem.Contains("packed twice"));
        }
    }

    [Fact]
    public void WemWithXboxWarnsThatConsolesNeedTheirOwnAudio()
    {
        var (t, sfmod, _, _) = Setup(WithXbox(Json));
        using (t)
        {
            t.File(Path.Combine("mod", "Assets", "Sound", "FX", "MyMod", "Beep.wem"), "wem");
            var problems = Load(sfmod, out var p);
            Assert.True(p!.Assets!.UncompressedMain);
            Assert.Contains(problems, x => x.IsWarning && x.Problem.Contains("plays silent on Xbox"));
        }
    }

    [Fact]
    public void ZipFilesCantTakeTheTextureArchiveName()
    {
        var json = Json.Replace("\"assets\":", "\"zip\": { \"files\": { \"Builder/Builder.csproj\": \"MyMod - Textures.ba2\" } }, \"assets\":");
        var (t, sfmod, _, _) = Setup(json);
        using (t)
        {
            var problems = Load(sfmod, out _);
            Assert.Contains(problems, x => x.Field == "zip.files" && x.Problem.Contains("MyMod - Textures.ba2"));
        }
    }

    // ---- pack

    [Fact]
    public void PackStagesScriptsAndLooseFilesAndMakesBothArchives()
    {
        var (t, sfmod, game, env) = Setup();
        using (t)
        {
            CompiledScripts(t);
            var code = RunApp(env, out var output, "pack", sfmod, "--game", game);
            Assert.True(code == ExitCodes.Ok, output);
            Assert.Equal(2, env.Execs.Count);
            var main = Mod(t, ".build", "stage", "pack", "main");
            var tex = Mod(t, ".build", "stage", "pack", "textures");
            var mainBa2 = Mod(t, "dist", "MyMod - Main.ba2");
            var texBa2 = Mod(t, "dist", "MyMod - Textures.ba2");
            Assert.Equal($"{main} \"-c={mainBa2}\" -r={main} -q", env.Execs[0].Arguments);
            Assert.Equal($"{tex} \"-c={texBa2}\" -r={tex} -format=DDS -q", env.Execs[1].Arguments);
            Assert.Equal(new[] { Path.Combine("Materials", "MyMod", "Box.mat"), Path.Combine("Meshes", "MyMod", "Box.nif"), Path.Combine("Scripts", "MYM", "Alpha.pex") }, Listed(mainBa2));
            Assert.Equal(new[] { Path.Combine("Textures", "MyMod", "Box_color.dds") }, Listed(texBa2));
            Assert.Contains($"RESULT: OK, \"MyMod - Main.ba2\" and \"MyMod - Textures.ba2\" made in {Mod(t, "dist")}", output);
        }
    }

    [Fact]
    public void PackWithXboxMakesSeparateXboxArchives()
    {
        var (t, sfmod, game, env) = Setup(WithXbox(Json));
        using (t)
        {
            CompiledScripts(t);
            t.File(Path.Combine("mod", "dist", "MyMod.esm"), "made-up plugin");
            var code = RunApp(env, out var output, "pack", sfmod, "--game", game);
            Assert.True(code == ExitCodes.Ok, output);
            Assert.Equal(4, env.Execs.Count);
            var tex = Mod(t, ".build", "stage", "pack", "textures");
            var xboxTex = Mod(t, "creations-out", "MyMod - Textures_xbox.ba2");
            Assert.EndsWith("-compression=XBox -q", env.Execs[2].Arguments);
            Assert.Equal($"{tex} \"-c={xboxTex}\" -r={tex} -format=DDS -compression=XBox -q", env.Execs[3].Arguments);
            var files = Directory.GetFiles(Mod(t, "creations-out")).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray();
            Assert.Equal(new[] { "MyMod - Main.ba2", "MyMod - Main_xbox.ba2", "MyMod - Textures.ba2", "MyMod - Textures_xbox.ba2", "MyMod.esm" }, files);
            Assert.Contains("holds the PC-format textures", output);
        }
    }

    [Fact]
    public void PackUsesTheXboxTexturesWhenGiven()
    {
        var json = WithXbox(Json.Replace("{ \"source\": \"Assets\" }", "{ \"source\": \"Assets\", \"xboxTextures\": \"AssetsXbox\" }"));
        var (t, sfmod, game, env) = Setup(json);
        using (t)
        {
            t.File(Path.Combine("mod", "AssetsXbox", "Textures", "MyMod", "Box_color.dds"), "xbox dds");
            CompiledScripts(t);
            var code = RunApp(env, out var output, "pack", sfmod, "--game", game);
            Assert.True(code == ExitCodes.Ok, output);
            var xboxStage = Mod(t, ".build", "stage", "pack", "textures-xbox");
            Assert.StartsWith($"{xboxStage} ", env.Execs[3].Arguments);
            Assert.Equal("xbox dds", File.ReadAllText(Path.Combine(xboxStage, "Textures", "MyMod", "Box_color.dds")));
            Assert.DoesNotContain("holds the PC-format textures", output);
        }
    }

    [Fact]
    public void PackWithoutScriptsMakesOnlyWhatTheAssetsNeed()
    {
        var json = Json.Replace("\"scripts\": { \"source\": \"Scripts/Source\", \"namespaces\": [\"MYM\"] },", "");
        var (t, sfmod, game, env) = Setup(json, withScript: false);
        using (t)
        {
            File.Delete(Mod(t, "Assets", "Meshes", "MyMod", "Box.nif"));
            File.Delete(Mod(t, "Assets", "Materials", "MyMod", "Box.mat"));
            var code = RunApp(env, out var output, "pack", sfmod, "--game", game);
            Assert.True(code == ExitCodes.Ok, output);
            var (_, args, _) = Assert.Single(env.Execs);
            Assert.Contains("-format=DDS", args);
            Assert.False(File.Exists(Mod(t, "dist", "MyMod - Main.ba2")));
            Assert.True(File.Exists(Mod(t, "dist", "MyMod - Textures.ba2")));

            // the zip takes the texture archive and needs no Main
            t.File(Path.Combine("mod", "dist", "MyMod.esm"), "made-up plugin");
            code = RunApp(env, out output, "zip", sfmod);
            Assert.True(code == ExitCodes.Ok, output);
            using var zip = ZipFile.OpenRead(Mod(t, "MyMod-0.1.0.zip"));
            Assert.Equal(new[] { "MyMod - Textures.ba2", "MyMod.esm" }, zip.Entries.Select(e => e.FullName));
        }
    }

    [Fact]
    public void ZipNeedsTheTextureArchive()
    {
        var (t, sfmod, _, env) = Setup();
        using (t)
        {
            t.File(Path.Combine("mod", "dist", "MyMod.esm"), "made-up plugin");
            t.File(Path.Combine("mod", "dist", "MyMod - Main.ba2"), "made-up ba2");
            var code = RunApp(env, out var output, "zip", sfmod);
            Assert.Equal(ExitCodes.BadInput, code);
            Assert.Contains("MyMod - Textures.ba2", output);
        }
    }

    [Fact]
    public void SoundFilesMakeMainUncompressedOnBothPlatforms()
    {
        var (t, sfmod, game, env) = Setup(WithXbox(Json));
        using (t)
        {
            t.File(Path.Combine("mod", "Assets", "Sound", "Voice", "MyMod.esm", "Hello.ffxanim"), "lip");
            CompiledScripts(t);
            var code = RunApp(env, out var output, "pack", sfmod, "--game", game);
            Assert.True(code == ExitCodes.Ok, output);
            Assert.EndsWith("-compression=None -q", env.Execs[0].Arguments);
            Assert.EndsWith("-compression=None -q", env.Execs[2].Arguments);
            Assert.Contains("-compression=None", output);
        }
    }

    [Fact]
    public void ALooseCopyOfACompiledScriptIsRefused()
    {
        var (t, sfmod, game, env) = Setup();
        using (t)
        {
            t.File(Path.Combine("mod", "Assets", "Scripts", "MYM", "Alpha.pex"), "an old pex");
            CompiledScripts(t);
            var code = RunApp(env, out var output, "pack", sfmod, "--game", game);
            Assert.Equal(ExitCodes.BadInput, code);
            Assert.Contains("also the compiled script", output);
            Assert.Empty(env.Execs);
        }
    }

    [Fact]
    public void AStaleTextureArchiveIsDeleted()
    {
        var (t, sfmod, game, env) = Setup();
        using (t)
        {
            File.Delete(Mod(t, "Assets", "Textures", "MyMod", "Box_color.dds"));
            var stale = t.File(Path.Combine("mod", "dist", "MyMod - Textures.ba2"), "old textures");
            CompiledScripts(t);
            var code = RunApp(env, out var output, "pack", sfmod, "--game", game);
            Assert.True(code == ExitCodes.Ok, output);
            Assert.False(File.Exists(stale));
            Assert.Single(env.Execs);
        }
    }

    [Fact]
    public void AssetsInTheGameFolderAreRefused()
    {
        var (t, sfmod, game, env) = Setup(Json.Replace("\"source\": \"Assets\"", "\"source\": \"../game/Data\""));
        using (t)
        {
            t.File(Path.Combine("game", "Data", "Meshes", "x.nif"), "a game file");
            CompiledScripts(t);
            var code = RunApp(env, out var output, "pack", sfmod, "--game", game);
            Assert.Equal(ExitCodes.BadInput, code);
            Assert.Contains("game's folder", output);
            Assert.Empty(env.Execs);
        }
    }

    [Fact]
    public void ScriptsOnlyModKeepsTheOldCommandLine()
    {
        var (t, sfmod, game, env) = Setup(Json.Replace(",\n  \"assets\": { \"source\": \"Assets\" }", "").Replace(",\r\n  \"assets\": { \"source\": \"Assets\" }", ""));
        using (t)
        {
            CompiledScripts(t);
            var code = RunApp(env, out var output, "pack", sfmod, "--game", game);
            Assert.True(code == ExitCodes.Ok, output);
            var stage = Mod(t, ".build", "stage");
            Assert.Equal($"{Path.Combine(stage, "Scripts")} \"-c={Mod(t, "dist", "MyMod - Main.ba2")}\" -r={stage} -q", Assert.Single(env.Execs).Arguments);
            Assert.False(Directory.Exists(Path.Combine(stage, "pack")));
        }
    }
}
