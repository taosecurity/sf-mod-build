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
            // with xbox on, a PC texture with no Xbox copy would be missing on Xbox: refused
            var problems = Load(sfmod, out var p);
            Assert.Null(p);
            Assert.Contains(problems, x => !x.IsWarning && x.Problem.Contains("1 PC texture(s) have no Xbox copy"));
            Assert.Contains(problems, x => x.IsWarning && x.Problem.Contains("1 Xbox texture(s) have no PC copy"));
            // an Xbox copy of every PC texture: an extra Xbox texture is only a warning
            t.File(Path.Combine("mod", "AssetsXbox", "Textures", "MyMod", "Box_color.dds"), "xbox dds");
            problems = Load(sfmod, out p);
            Assert.NotNull(p);
            Assert.Contains(problems, x => x.IsWarning && x.Problem.Contains("1 Xbox texture(s) have no PC copy"));
            t.File(Path.Combine("mod", "AssetsXbox", "Meshes", "Box.nif"), "nif");
            problems = Load(sfmod, out p);
            Assert.Null(p);
            Assert.Contains(problems, x => !x.IsWarning && x.Problem.Contains("only .dds"));
        }
    }

    [Fact]
    public void AMissingXboxCopyIsOnlyAWarningWithXboxOff()
    {
        var (t, sfmod, _, _) = Setup(Json.Replace("{ \"source\": \"Assets\" }", "{ \"source\": \"Assets\", \"xboxTextures\": \"AssetsXbox\" }"));
        using (t)
        {
            t.File(Path.Combine("mod", "AssetsXbox", "Textures", "MyMod", "Other.dds"), "xbox dds");
            var problems = Load(sfmod, out var p);
            Assert.NotNull(p);
            Assert.Contains(problems, x => x.IsWarning && x.Problem.Contains("1 PC texture(s) have no Xbox copy"));
        }
    }

    [Theory]
    [InlineData(null)]          // an empty folder
    [InlineData("readme.txt")]  // only a file that isn't a texture
    public void AnXboxTextureFolderWithNoDdsIsRefused(string? onlyFile)
    {
        var json = WithXbox(Json.Replace("{ \"source\": \"Assets\" }", "{ \"source\": \"Assets\", \"xboxTextures\": \"AssetsXbox\" }"));
        var (t, sfmod, game, env) = Setup(json);
        using (t)
        {
            t.Dir(Path.Combine("mod", "AssetsXbox"));
            if (onlyFile != null) t.File(Path.Combine("mod", "AssetsXbox", "Textures", onlyFile), "text");
            var problems = Load(sfmod, out var p);
            Assert.Null(p);
            Assert.Contains(problems, x => !x.IsWarning && x.Field == "assets.xboxTextures" && x.Problem.Contains("holds no .dds textures"));
            // pack refuses it too, before Archive2 runs
            CompiledScripts(t);
            var code = RunApp(env, out var output, "pack", sfmod, "--game", game);
            Assert.Equal(ExitCodes.BadInput, code);
            Assert.Contains("holds no .dds textures", output);
            Assert.Empty(env.Execs);
        }
    }

    [Fact]
    public void ABadXboxTexturesValueShowsAnXboxFolderAsTheExample()
    {
        var json = WithXbox(Json.Replace("{ \"source\": \"Assets\" }", "{ \"source\": \"Assets\", \"xboxTextures\": 5 }"));
        var (t, sfmod, _, _) = Setup(json);
        using (t)
        {
            var problems = Load(sfmod, out _);
            var x = Assert.Single(problems, x => x.Field == "assets.xboxTextures");
            Assert.Contains("\"Assets-xbox\"", x.Fix);
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
            var problems = Load(sfmod, out var p);
            Assert.Null(p);
            Assert.Contains(problems, x => x.Field == "zip.files[\"Builder/Builder.csproj\"]" && x.Problem.Contains("archive (.ba2)"));
        }
    }

    [Theory]
    [InlineData("dist/Extra.ba2", "Extra.ba2")]       // the builder's own archive
    [InlineData("Builder/Builder.csproj", "Extra.ba2")] // any file, shipped under an archive's name
    [InlineData("dist/Extra.ba2", "Extra.bin")]       // an archive, shipped under another name
    public void ValidateRefusesAnArchiveInZipFiles(string source, string inZip)
    {
        var json = Json.Replace("\"assets\":", $"\"zip\": {{ \"files\": {{ \"{source}\": \"{inZip}\" }} }}, \"assets\":");
        var (t, sfmod, _, env) = Setup(json);
        using (t)
        {
            t.File(Path.Combine("mod", "dist", "Extra.ba2"), "the builder's own archive");
            var code = RunApp(env, out var output, "validate", sfmod);
            Assert.Equal(ExitCodes.BadInput, code);
            Assert.Contains($"zip.files[\"{source}\"]", output);
            Assert.Contains("PROBLEM:", output);
            Assert.Contains("delete it from dist; sf-mod-build ships only the archives it builds", output);
        }
    }

    [Fact]
    public void ZipFilesStillTakesOtherFiles()
    {
        var json = Json.Replace("\"assets\":", "\"zip\": { \"files\": { \"Builder/Builder.csproj\": \"Docs/README-{name}.txt\" } }, \"assets\":");
        var (t, sfmod, _, env) = Setup(json);
        using (t)
        {
            t.File(Path.Combine("mod", "dist", "MyMod.esm"), "made-up plugin");
            t.File(Path.Combine("mod", "dist", "MyMod - Main.ba2"), "main");
            t.File(Path.Combine("mod", "dist", "MyMod - Textures.ba2"), "textures");
            var code = RunApp(env, out var output, "zip", sfmod);
            Assert.True(code == ExitCodes.Ok, output);
            using var zip = ZipFile.OpenRead(Mod(t, "MyMod-0.1.0.zip"));
            Assert.Equal(new[] { "Docs/README-MyMod.txt", "MyMod - Main.ba2", "MyMod - Textures.ba2", "MyMod.esm" }, zip.Entries.Select(e => e.FullName));
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

    [Fact]
    public void NothingToPackStillDeletesTheOldArchives()
    {
        var json = Json.Replace("\"scripts\": { \"source\": \"Scripts/Source\", \"namespaces\": [\"MYM\"] },", "")
            .Replace(",\n  \"assets\": { \"source\": \"Assets\" }", "").Replace(",\r\n  \"assets\": { \"source\": \"Assets\" }", "");
        var (t, sfmod, game, env) = Setup(json, withScript: false);
        using (t)
        {
            var main = t.File(Path.Combine("mod", "dist", "MyMod - Main.ba2"), "old main");
            var tex = t.File(Path.Combine("mod", "dist", "MyMod - Textures.ba2"), "old textures");
            var code = RunApp(env, out var output, "pack", sfmod, "--game", game);
            Assert.True(code == ExitCodes.Ok, output);
            Assert.Contains("nothing to pack", output);
            Assert.False(File.Exists(main));
            Assert.False(File.Exists(tex));
            Assert.Empty(env.Execs);
        }
    }

    [Theory]
    [InlineData("MyMod - Main.ba2")]          // the mod's own archive from an earlier build
    [InlineData("Someone Else - Main.ba2")]   // one the builder or someone else put there
    public void ZipRefusesArchivesThisBuildDoesntMake(string name)
    {
        var json = Json.Replace("\"scripts\": { \"source\": \"Scripts/Source\", \"namespaces\": [\"MYM\"] },", "");
        var (t, sfmod, _, env) = Setup(json, withScript: false);
        using (t)
        {
            // no meshes or materials any more, so the build makes only the texture archive
            File.Delete(Mod(t, "Assets", "Meshes", "MyMod", "Box.nif"));
            File.Delete(Mod(t, "Assets", "Materials", "MyMod", "Box.mat"));
            t.File(Path.Combine("mod", "dist", "MyMod.esm"), "made-up plugin");
            t.File(Path.Combine("mod", "dist", "MyMod - Textures.ba2"), "textures");
            var stray = t.File(Path.Combine("mod", "dist", name), "not made by this build");
            var code = RunApp(env, out var output, "zip", sfmod);
            Assert.Equal(ExitCodes.BadInput, code);
            Assert.Contains($"RESULT: CANNOT ZIP ({stray} is an archive in dist that this build doesn't make", output);
            Assert.Contains("Fix: delete it from dist; sf-mod-build ships only the archives it builds", output);
            Assert.DoesNotContain("zip.files", output);
            Assert.False(File.Exists(Mod(t, "MyMod-0.1.0.zip")));
        }
    }

    [Fact]
    public void ABuilderWrittenArchiveIsRefusedByPackAndZip()
    {
        var (t, sfmod, game, env) = Setup(WithXbox(Json));
        using (t)
        {
            CompiledScripts(t);
            t.File(Path.Combine("mod", "dist", "MyMod.esm"), "made-up plugin");
            var extra = t.File(Path.Combine("mod", "dist", "Extra.ba2"), "the builder's own archive");
            var code = RunApp(env, out var output, "pack", sfmod, "--game", game);
            Assert.Equal(ExitCodes.BadInput, code);
            Assert.Contains($"{extra} is an archive in dist that this build doesn't make", output);
            Assert.Contains("Fix: delete it from dist; sf-mod-build ships only the archives it builds", output);
            Assert.Empty(env.Execs);
            Assert.False(File.Exists(Mod(t, "creations-out", "Extra.ba2")));
            t.File(Path.Combine("mod", "dist", "MyMod - Main.ba2"), "main");
            t.File(Path.Combine("mod", "dist", "MyMod - Textures.ba2"), "textures");
            code = RunApp(env, out output, "zip", sfmod);
            Assert.Equal(ExitCodes.BadInput, code);
            Assert.Contains($"RESULT: CANNOT ZIP ({extra} is an archive in dist that this build doesn't make", output);
            Assert.False(File.Exists(Mod(t, "MyMod-0.1.0.zip")));
        }
    }

    [Fact]
    public void ZipNamesTheSameStrayArchiveOnEveryDisk()
    {
        var (t, sfmod, _, env) = Setup();
        using (t)
        {
            t.File(Path.Combine("mod", "dist", "MyMod.esm"), "made-up plugin");
            t.File(Path.Combine("mod", "dist", "MyMod - Main.ba2"), "main");
            t.File(Path.Combine("mod", "dist", "MyMod - Textures.ba2"), "textures");
            // NTFS lists "a - Other.ba2" first (it sorts without case); ordinal order puts "B - Other.ba2" first
            t.File(Path.Combine("mod", "dist", "a - Other.ba2"), "stray");
            var first = t.File(Path.Combine("mod", "dist", "B - Other.ba2"), "stray");
            var code = RunApp(env, out var output, "zip", sfmod);
            Assert.Equal(ExitCodes.BadInput, code);
            Assert.Contains($"RESULT: CANNOT ZIP ({first} is an archive", output);
        }
    }

    [Fact]
    public void ZipTakesARequiredArchiveSpelledInOtherCaseUnderItsOwnName()
    {
        var (t, sfmod, _, env) = Setup();
        using (t)
        {
            t.File(Path.Combine("mod", "dist", "MyMod.esm"), "made-up plugin");
            t.File(Path.Combine("mod", "dist", "mymod - main.ba2"), "main, spelled in lower case");
            t.File(Path.Combine("mod", "dist", "MYMOD - TEXTURES.BA2"), "textures, spelled in upper case");
            var code = RunApp(env, out var output, "zip", sfmod);
            Assert.True(code == ExitCodes.Ok, output);
            Assert.Contains("zipped   MyMod - Main.ba2", output);
            using var zip = ZipFile.OpenRead(Mod(t, "MyMod-0.1.0.zip"));
            Assert.Equal(new[] { "MyMod - Main.ba2", "MyMod - Textures.ba2", "MyMod.esm" }, zip.Entries.Select(e => e.FullName));
        }
    }

    [Fact]
    public void TheCreationsSetRefusesArchivesThisBuildDoesntMake()
    {
        var (t, sfmod, game, env) = Setup(WithXbox(Json));
        using (t)
        {
            CompiledScripts(t);
            t.File(Path.Combine("mod", "dist", "MyMod.esm"), "made-up plugin");
            var other = t.File(Path.Combine("mod", "dist", "Old - Main.ba2"), "copied in by hand");
            var code = RunApp(env, out var output, "pack", sfmod, "--game", game);
            Assert.Equal(ExitCodes.BadInput, code);
            Assert.Contains($"RESULT: CANNOT PACK ({other} is an archive in dist that this build doesn't make", output);
            Assert.Contains("Fix: delete it from dist; sf-mod-build ships only the archives it builds", output);
            Assert.Empty(env.Execs);   // refused before any archive is made
            Assert.False(File.Exists(Mod(t, "creations-out", "Old - Main.ba2")));
        }
    }

    [Fact]
    public void WithoutXboxPackLeavesTheStrayArchiveToZip()
    {
        var (t, sfmod, game, env) = Setup();
        using (t)
        {
            CompiledScripts(t);
            var other = t.File(Path.Combine("mod", "dist", "Old - Main.ba2"), "copied in by hand");
            var code = RunApp(env, out var output, "pack", sfmod, "--game", game);
            Assert.True(code == ExitCodes.Ok, output);
            // a warning only (zip stops on it), naming the file and the fix, once
            Assert.Contains($"WARNING  {other} is an archive in dist that this build doesn't make", output);
            Assert.Contains("so zip will stop on it. Fix: delete it from dist", output);
            Assert.Single(output.Split('\n'), l => l.StartsWith("WARNING"));
        }
    }

    [Fact]
    public void WithoutXboxPackDoesntWarnAboutTheModsOwnArchives()
    {
        var (t, sfmod, game, env) = Setup();
        using (t)
        {
            CompiledScripts(t);
            t.File(Path.Combine("mod", "dist", "MyMod.esm"), "made-up plugin");
            t.File(Path.Combine("mod", "dist", "mymod - main.ba2"), "an old copy, spelled in lower case");
            t.File(Path.Combine("mod", "dist", "MyMod-0.1.0.zip"), "an old zip");
            var code = RunApp(env, out var output, "pack", sfmod, "--game", game);
            Assert.True(code == ExitCodes.Ok, output);
            Assert.DoesNotContain("WARNING", output);
        }
    }

    [Fact]
    public void NothingToPackWithXboxDeletesTheModsOldCreationsArchives()
    {
        var json = WithXbox(Json.Replace("\"scripts\": { \"source\": \"Scripts/Source\", \"namespaces\": [\"MYM\"] },", "")
            .Replace(",\n  \"assets\": { \"source\": \"Assets\" }", "").Replace(",\r\n  \"assets\": { \"source\": \"Assets\" }", ""));
        var (t, sfmod, game, env) = Setup(json, withScript: false);
        using (t)
        {
            var old = new[] { "MyMod - Main_xbox.ba2", "MyMod - Textures_xbox.ba2", "MyMod - Main.ba2", "MyMod - Textures.ba2" }
                .Select(n => t.File(Path.Combine("mod", "creations-out", n), "old")).ToList();
            var plugin = t.File(Path.Combine("mod", "creations-out", "MyMod.esm"), "the plugin");
            var other = t.File(Path.Combine("mod", "creations-out", "Someone Else - Main_xbox.ba2"), "not the mod's");
            var code = RunApp(env, out var output, "pack", sfmod, "--game", game);
            Assert.True(code == ExitCodes.Ok, output);
            Assert.Contains("nothing to pack", output);
            foreach (var f in old) Assert.False(File.Exists(f), f);
            Assert.True(File.Exists(plugin));
            Assert.True(File.Exists(other));
            Assert.Empty(env.Execs);
        }
    }

    const string NothingToPackJson = """
        {
          "plugin": "MyMod.esm",
          "version": "0.1.0",
          "xbox": true,
          "builder": { "project": "Builder", "args": ["{data}", "{dist}"] }
        }
        """;

    [Fact]
    public void NothingToPackRefusesACreationsFolderInsideTheGame()
    {
        // the mod's folder is the game's folder, and creationsOut is the game's Data folder: the old archive there is
        // the installed copy, so it must not be deleted (the full pack refuses this folder too)
        using var t = new TempDir();
        var game = Fake.Game(t, "game");
        t.File(Path.Combine("game", "Builder", "Builder.csproj"), "<Project />");
        var sfmod = t.File(Path.Combine("game", "sfmod.json"), NothingToPackJson.Replace("\"xbox\": true,", "\"xbox\": true, \"creationsOut\": \"Data\","));
        var installed = t.File(Path.Combine("game", "Data", "MyMod - Main.ba2"), "the installed archive");
        var env = Fake.EnvWithPath(t, "dotnet");
        var code = RunApp(env, out var output, "pack", sfmod, "--game", game);
        Assert.True(code == ExitCodes.BadInput, output);
        Assert.Contains("game's folder", output);
        Assert.True(File.Exists(installed));
    }

    [Fact]
    public void NothingToPackStillDeletesWhenTheGameIsNotFound()
    {
        using var t = new TempDir();
        t.File(Path.Combine("mod", "Builder", "Builder.csproj"), "<Project />");
        var sfmod = t.File(Path.Combine("mod", "sfmod.json"), NothingToPackJson);
        var old = t.File(Path.Combine("mod", "creations-out", "MyMod - Main_xbox.ba2"), "old");
        var env = Fake.EnvWithPath(t, "dotnet");
        var code = RunApp(env, out var output, "pack", sfmod, "--game", Path.Combine(t.Path, "no-such-game"));
        Assert.True(code == ExitCodes.Ok, output);
        Assert.Contains("nothing to pack", output);
        Assert.Contains("WARNING  --game", output);
        Assert.False(File.Exists(old));
    }

    [Fact]
    public void NothingToPackChecksEveryInstallNotOnlyTheGivenOne()
    {
        // --game names one install, but the mod (and its creations-out, the game's Data folder) sits in another one
        // that auto-detection finds through Steam: that installed archive must not be deleted either
        using var t = new TempDir();
        var env = Fake.EnvWithPath(t, "dotnet");
        var given = Fake.Game(t, "OtherGame");
        var steamGame = SteamInstall(t, env);
        t.File(Path.Combine(steamGame, "Builder", "Builder.csproj"), "<Project />");
        var sfmod = t.File(Path.Combine(steamGame, "sfmod.json"), NothingToPackJson.Replace("\"xbox\": true,", "\"xbox\": true, \"creationsOut\": \"Data\","));
        var installed = t.File(Path.Combine(steamGame, "Data", "MyMod - Main.ba2"), "the installed archive");
        var code = RunApp(env, out var output, "pack", sfmod, "--game", given);
        Assert.True(code == ExitCodes.BadInput, output);
        Assert.Contains("game's folder", output);
        Assert.True(File.Exists(installed));
    }

    [Fact]
    public void NothingToPackWithABadGameOptionStillChecksTheInstallsFound()
    {
        using var t = new TempDir();
        var env = Fake.EnvWithPath(t, "dotnet");
        var steamGame = SteamInstall(t, env);
        t.File(Path.Combine(steamGame, "Builder", "Builder.csproj"), "<Project />");
        var sfmod = t.File(Path.Combine(steamGame, "sfmod.json"), NothingToPackJson.Replace("\"xbox\": true,", "\"xbox\": true, \"creationsOut\": \"Data\","));
        var installed = t.File(Path.Combine(steamGame, "Data", "MyMod - Main.ba2"), "the installed archive");
        var code = RunApp(env, out var output, "pack", sfmod, "--game", Path.Combine(t.Path, "Stearn"));
        Assert.True(code == ExitCodes.BadInput, output);
        Assert.Contains("WARNING  --game", output);
        Assert.True(File.Exists(installed));
    }

    /// <summary>A fake Steam install that auto-detection finds: through the registry on Windows, through
    /// ~/.steam/steam elsewhere. Returns its Starfield folder.</summary>
    static string SteamInstall(TempDir t, FakeHostEnv env)
    {
        var steam = t.Dir(Path.Combine("home", ".steam", "steam"));
        if (env.IsWindows) env.Registry[(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath")] = steam.Replace('\\', '/');
        else env.Vars["HOME"] = Path.Combine(t.Path, "home");
        return Fake.Game(t, Path.Combine("home", ".steam", "steam", "steamapps", "common", "Starfield"));
    }

    [Fact]
    public void FullPackChecksEveryInstallNotOnlyTheGivenOne()
    {
        // the full pack (scripts staged, xbox on) with --game naming one install, while the mod and its
        // creations-out (that install's Data folder) sit in a second one: nothing there may be emptied
        using var t = new TempDir();
        var env = Fake.EnvWithPath(t, "dotnet");
        var given = Fake.Game(t, "OtherGame");
        var steamGame = SteamInstall(t, env);
        t.File(Path.Combine(steamGame, "Builder", "Builder.csproj"), "<Project />");
        t.File(Path.Combine(steamGame, "Scripts", "Source", "MYM", "Alpha.psc"), "ScriptName MYM:Alpha extends Quest\n");
        t.File(Path.Combine(steamGame, ".build", "stage", "Scripts", "MYM", "Alpha.pex"), "made-up pex");
        var json = WithXbox(Json.Replace("\"assets\": { \"source\": \"Assets\" }", "\"creationsOut\": \"Data\""));
        var sfmod = t.File(Path.Combine(steamGame, "sfmod.json"), json);
        var installed = t.File(Path.Combine(steamGame, "Data", "Starfield.esm"), "the game's own file");
        var code = RunApp(env, out var output, "pack", sfmod, "--game", given);
        Assert.True(code == ExitCodes.BadInput, output);
        Assert.Contains("game's folder", output);
        Assert.True(File.Exists(installed));
        Assert.Empty(env.Execs);
    }

    [Fact]
    public void PluginChecksDistAgainstEveryInstall()
    {
        // dist is a second install's Data folder while --game names another install: dist must not be emptied
        using var t = new TempDir();
        var env = Fake.EnvWithPath(t, "dotnet");
        var given = Fake.Game(t, "OtherGame");
        var steamGame = SteamInstall(t, env);
        t.File(Path.Combine(steamGame, "Builder", "Builder.csproj"), "<Project />");
        var sfmod = t.File(Path.Combine(steamGame, "sfmod.json"), NothingToPackJson.Replace("\"xbox\": true,", "\"dist\": \"Data\","));
        var installed = t.File(Path.Combine(steamGame, "Data", "Starfield.esm"), "the game's own file");
        t.Dir(Path.Combine("OtherGame", "Data"));
        var code = RunApp(env, out var output, "plugin", sfmod, "--game", given);
        Assert.True(code == ExitCodes.BadInput, output);
        Assert.Contains("game's folder", output);
        Assert.True(File.Exists(installed));
        Assert.Empty(env.Execs);
    }

    [Fact]
    public void FullPackChecksDistAgainstEveryInstall()
    {
        // the full pack without xbox, with dist a second install's Data folder holding an old archive by the mod's
        // name: pack would delete it and write its own archives there, so it must refuse first
        using var t = new TempDir();
        var env = Fake.EnvWithPath(t, "dotnet");
        var given = Fake.Game(t, "OtherGame");
        var steamGame = SteamInstall(t, env);
        t.File(Path.Combine(steamGame, "Builder", "Builder.csproj"), "<Project />");
        t.File(Path.Combine(steamGame, "Scripts", "Source", "MYM", "Alpha.psc"), "ScriptName MYM:Alpha extends Quest\n");
        t.File(Path.Combine(steamGame, ".build", "stage", "Scripts", "MYM", "Alpha.pex"), "made-up pex");
        var sfmod = t.File(Path.Combine(steamGame, "sfmod.json"), Json.Replace("\"assets\": { \"source\": \"Assets\" }", "\"dist\": \"Data\""));
        var installed = t.File(Path.Combine(steamGame, "Data", "MyMod - Textures.ba2"), "the installed archive");
        var code = RunApp(env, out var output, "pack", sfmod, "--game", given);
        Assert.True(code == ExitCodes.BadInput, output);
        Assert.Contains("game's folder", output);
        Assert.Contains("set \"dist\"", output);
        Assert.True(File.Exists(installed));
        Assert.Empty(env.Execs);
    }

    [Fact]
    public void NothingToPackChecksDistAgainstEveryInstall()
    {
        // nothing to pack, no xbox, dist a second install's Data folder holding an old archive by the mod's name
        using var t = new TempDir();
        var env = Fake.EnvWithPath(t, "dotnet");
        var given = Fake.Game(t, "OtherGame");
        var steamGame = SteamInstall(t, env);
        t.File(Path.Combine(steamGame, "Builder", "Builder.csproj"), "<Project />");
        var sfmod = t.File(Path.Combine(steamGame, "sfmod.json"), NothingToPackJson.Replace("\"xbox\": true,", "\"dist\": \"Data\","));
        var installed = t.File(Path.Combine(steamGame, "Data", "MyMod - Main.ba2"), "the installed archive");
        var code = RunApp(env, out var output, "pack", sfmod, "--game", given);
        Assert.True(code == ExitCodes.BadInput, output);
        Assert.Contains("game's folder", output);
        Assert.True(File.Exists(installed));
    }

    [Fact]
    public void NothingToPackWithNoOldArchiveDoesNotLookForTheGame()
    {
        // nothing to delete: no game lookup, no WARNING about a bad --game, exit 0 as before
        using var t = new TempDir();
        var env = Fake.EnvWithPath(t, "dotnet");
        t.File(Path.Combine("mod", "Builder", "Builder.csproj"), "<Project />");
        var sfmod = t.File(Path.Combine("mod", "sfmod.json"), NothingToPackJson);
        var code = RunApp(env, out var output, "pack", sfmod, "--game", Path.Combine(t.Path, "Stearn"));
        Assert.True(code == ExitCodes.Ok, output);
        Assert.DoesNotContain("WARNING", output);
    }

    [Fact]
    public void CompileWithoutAGameFolderStillChecksTheInstallsFound()
    {
        // CompileAll called with no game in use: the stage folder (an install's Data folder) must still not be emptied
        using var t = new TempDir();
        var env = Fake.EnvWithPath(t, "dotnet");
        var steamGame = SteamInstall(t, env);
        t.File(Path.Combine(steamGame, "Builder", "Builder.csproj"), "<Project />");
        t.File(Path.Combine(steamGame, "Scripts", "Source", "MYM", "Alpha.psc"), "ScriptName MYM:Alpha extends Quest\n");
        var sfmod = t.File(Path.Combine(steamGame, "sfmod.json"), Json.Replace("\"assets\": { \"source\": \"Assets\" }", "\"stage\": \"Data\""));
        var installed = t.File(Path.Combine(steamGame, "Data", "Starfield.esm"), "the game's own file");
        var problems = Load(sfmod, out var p);
        Assert.True(p != null, string.Join("\n", problems.Select(x => x.Format(sfmod))));
        var sw = new StringWriter();
        Assert.Equal(ExitCodes.BadInput, Compile.CompileAll(p!, Compile.ScriptList(p!), "compiler", "vanilla", env, sw));
        Assert.Contains("game's folder", sw.ToString());
        Assert.True(File.Exists(installed));
        Assert.Empty(env.Execs);
    }

    [Fact]
    public void AnEmptyXboxTextureFolderIsFineWhenTheModHasNoTextures()
    {
        var json = WithXbox(Json.Replace("{ \"source\": \"Assets\" }", "{ \"source\": \"Assets\", \"xboxTextures\": \"AssetsXbox\" }"));
        var (t, sfmod, game, env) = Setup(json);
        using (t)
        {
            File.Delete(Mod(t, "Assets", "Textures", "MyMod", "Box_color.dds"));
            t.Dir(Path.Combine("mod", "AssetsXbox"));
            var problems = Load(sfmod, out var p);
            Assert.True(p != null, string.Join("\n", problems.Select(x => x.Format(sfmod))));
            Assert.False(p!.HasTexturesArchive);
            Assert.Equal(ExitCodes.Ok, RunApp(env, out var validated, "validate", sfmod));
            CompiledScripts(t);
            var code = RunApp(env, out var output, "pack", sfmod, "--game", game);
            Assert.True(code == ExitCodes.Ok, output);
            Assert.Equal(2, env.Execs.Count);   // Main and Main_xbox only
        }
    }

    [Fact]
    public void ValidateNamesLooseFilesThatDifferOnlyInCase()
    {
        var (t, sfmod, game, env) = Setup();
        using (t)
        {
            var dir = t.CaseSensitiveDir(Path.Combine("mod", "Assets", "Meshes", "Twins"));
            if (dir == null) return;   // this machine can't hold two names that differ only in case
            var first = t.File(Path.Combine("mod", "Assets", "Meshes", "Twins", "Box.nif"), "1");
            var second = t.File(Path.Combine("mod", "Assets", "Meshes", "Twins", "box.nif"), "2");
            var code = RunApp(env, out var output, "validate", sfmod);
            Assert.Equal(ExitCodes.BadInput, code);
            Assert.Contains("PROBLEM:", output);
            Assert.Contains("differ only in case", output);
            Assert.Contains(first, output);
            Assert.Contains(second, output);
            CompiledScripts(t);
            code = RunApp(env, out output, "pack", sfmod, "--game", game);
            Assert.Equal(ExitCodes.BadInput, code);
            Assert.Empty(env.Execs);
        }
    }

    [Fact]
    public void ArchiveNamesInDistMatchWithoutCase()
    {
        var json = Json.Replace("\"scripts\": { \"source\": \"Scripts/Source\", \"namespaces\": [\"MYM\"] },", "");
        var (t, sfmod, game, env) = Setup(json, withScript: false);
        using (t)
        {
            var dist = t.CaseSensitiveDir(Path.Combine("mod", "dist"));
            if (dist == null) return;   // this machine can't hold two names that differ only in case
            // no meshes or materials any more, so the build makes only the texture archive
            File.Delete(Mod(t, "Assets", "Meshes", "MyMod", "Box.nif"));
            File.Delete(Mod(t, "Assets", "Materials", "MyMod", "Box.mat"));
            t.File(Path.Combine("mod", "dist", "MyMod.esm"), "made-up plugin");
            var oldMain = t.File(Path.Combine("mod", "dist", "mymod - main.ba2"), "an old main, spelled in lower case");
            var oldTextures = t.File(Path.Combine("mod", "dist", "mymod - textures.ba2"), "old textures, spelled in lower case");
            var code = RunApp(env, out var output, "pack", sfmod, "--game", game);
            Assert.True(code == ExitCodes.Ok, output);
            Assert.False(File.Exists(oldMain));
            Assert.False(File.Exists(oldTextures));
            Assert.True(File.Exists(Mod(t, "dist", "MyMod - Textures.ba2")));
            // zip finds the texture archive however it is spelled
            File.Move(Mod(t, "dist", "MyMod - Textures.ba2"), Mod(t, "dist", "mymod - textures.ba2"));
            code = RunApp(env, out output, "zip", sfmod);
            Assert.True(code == ExitCodes.Ok, output);
        }
    }

    /// <summary>The loaded project with its loose files replaced, for paths a case-insensitive disk can't hold.</summary>
    static void SetAssets(SfModProject p, SfModProject.AssetsSection assets) =>
        typeof(SfModProject).GetProperty(nameof(SfModProject.Assets))!.SetValue(p, assets);

    [Theory]
    [InlineData(false)]   // two general files
    [InlineData(true)]    // two textures
    public void FilesThatDifferOnlyInCaseAreRefused(bool textures)
    {
        var (t, sfmod, game, env) = Setup();
        using (t)
        {
            CompiledScripts(t);
            Load(sfmod, out var p);
            var a = p!.Assets!;
            // two real files whose archive paths differ only in case, as a case-sensitive folder (Linux) can hold
            var first = t.File(Path.Combine("mod", "Elsewhere", "one"), "1");
            var second = t.File(Path.Combine("mod", "Elsewhere", "two"), "2");
            var ext = textures ? "dds" : "nif";
            var twins = new List<SfModProject.AssetFile>
            {
                new(first, Path.Combine("Meshes", "MyMod", "Box." + ext)),
                new(second, Path.Combine("meshes", "mymod", "box." + ext)),
            };
            SetAssets(p, new SfModProject.AssetsSection
            {
                Source = a.Source,
                General = textures ? a.General : twins,
                Textures = textures ? twins : a.Textures,
                XboxTextureFiles = a.XboxTextureFiles,
            });
            var o = new StringWriter();
            var code = Pack.PackAll(p, game, Path.Combine(game, "Tools", "Archive2", "Archive2.exe"), env, o);
            Assert.Equal(ExitCodes.BadInput, code);
            Assert.Contains("differ only in case", o.ToString());
            Assert.DoesNotContain("compiled script", o.ToString());
            Assert.Empty(env.Execs);
        }
    }
}
