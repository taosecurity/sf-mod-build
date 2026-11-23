using Xunit;

namespace SfModBuild.Tests;

public partial class SfModProjectTests
{
    /// <summary>A made-up mod folder: one script, a builder project, README and LICENSE.</summary>
    static TempDir Mod()
    {
        var t = new TempDir();
        t.File(Path.Combine("Scripts", "Source", "MYM", "MyQuestScript.psc"), "ScriptName MYM:MyQuestScript extends Quest\n");
        t.File(Path.Combine("Builder", "Builder.csproj"), "<Project />");
        t.File("README.md", "# MyMod");
        t.File("LICENSE", "license");
        return t;
    }

    const string Good = """
        // a comment
        {
          "plugin": "MyMod.esm",
          "version": "0.1.0",
          "scripts": { "source": "Scripts/Source", "namespaces": ["MYM"] },
          "builder": { "project": "Builder", "args": ["{data}", "{dist}", "{version}"] },
          "zip": { "files": { "README.md": "README-{name}.md", "LICENSE": "LICENSE-{name}.txt" } },
        }
        """;

    static (SfModProject? p, List<ProjectProblem> problems) Load(TempDir t, string json)
    {
        var file = t.File("sfmod.json", json);
        var problems = new List<ProjectProblem>();
        return (SfModProject.Load(file, problems), problems);
    }

    static string All(List<ProjectProblem> problems) => string.Join("\n", problems.Select(x => x.Format("sfmod.json")));

    [Fact]
    public void GoodFileLoadsWithDefaults()
    {
        using var t = Mod();
        var (p, problems) = Load(t, Good);
        Assert.True(p != null, All(problems));
        Assert.Empty(problems);
        Assert.Equal("MyMod", p!.Name);
        Assert.Equal("MyMod - Main.ba2", p.Ba2Name);
        Assert.Equal(Path.Combine(t.Path, "Builder", "Builder.csproj"), p.BuilderProject);
        Assert.Equal(Path.Combine(t.Path, "dist"), p.Dist);
        Assert.Equal(Path.Combine(t.Path, ".build", "stage"), p.Stage);
        Assert.Equal(Path.Combine(t.Path, "MyMod-0.1.0.zip"), p.ZipPath);
        Assert.Equal(new[] { "README-MyMod.md", "LICENSE-MyMod.txt" }, p.ZipFiles.Select(f => f.NameInZip));
        Assert.Equal(new[] { Path.Combine(t.Path, "Scripts", "Source") }, p.Scripts!.Sources);
        Assert.Equal(new[] { "MYM" }, p.Scripts.Namespaces);
        Assert.False(p.Xbox);
        Assert.Null(p.Checker);
    }

    [Fact]
    public void FolderPathsDropATrailingSeparator()
    {
        // "dist/" and "dist" must be one path to every check: on Linux a link spelled "dist/" is followed by lstat,
        // readlink and a recursive delete, so the separator is dropped once, where the path is read
        using var t = Mod();
        var (p, problems) = Load(t, Good.Replace("\"version\": \"0.1.0\",",
            "\"version\": \"0.1.0\", \"xbox\": true, \"dist\": \"dist/\", \"stage\": \"stage\\\\\", \"creationsOut\": \"creations-out/\","));
        Assert.True(p != null, All(problems));
        Assert.Equal(Path.Combine(t.Path, "dist"), p!.Dist);
        Assert.Equal(Path.Combine(t.Path, "stage"), p.Stage);
        Assert.Equal(Path.Combine(t.Path, "creations-out"), p.CreationsOut);
    }

    [Fact]
    public void FileFieldsEndingInASeparatorStayErrors()
    {
        // the dropped separator is for folders; a field that takes a file still refuses "LICENSE/"
        using var t = Mod();
        t.File(Path.Combine("tools", "sf-mod-check.exe"));
        var (p, problems) = Load(t, Good
            .Replace("\"LICENSE\": \"LICENSE-{name}.txt\"", "\"LICENSE/\": \"LICENSE-{name}.txt\"")
            .Replace("\"project\": \"Builder\"", "\"project\": \"Builder/Builder.csproj/\"")
            .Replace("\"zip\":", "\"check\": { \"checker\": \"tools/sf-mod-check.exe/\" }, \"zip\":"));
        Assert.Null(p);
        Assert.Contains(problems, x => x.Field == "zip.files[\"LICENSE/\"]" && x.Problem.Contains("ends in a slash"));
        Assert.Contains(problems, x => x.Field == "check.checker" && x.Problem.Contains("ends in a slash"));
        Assert.Contains(problems, x => x.Field == "builder.project" && x.Problem.Contains("ends in a slash"));
    }

    [Fact]
    public void CheckerThatIsAFolderSaysSo()
    {
        using var t = Mod();
        t.File(Path.Combine("tools", "sf-mod-check.exe"));
        var (p, problems) = Load(t, Good.Replace("\"zip\":", "\"check\": { \"checker\": \"tools\" }, \"zip\":"));
        Assert.Null(p);
        Assert.Contains(problems, x => x.Field == "check.checker" && x.Problem.Contains("is a folder"));
    }

    [Fact]
    public void FolderArgumentFindsSfmodJson()
    {
        using var t = Mod();
        t.File("sfmod.json", Good);
        var problems = new List<ProjectProblem>();
        Assert.NotNull(SfModProject.Load(t.Path, problems));
    }

    [Fact]
    public void MissingFileSaysHowToMakeOne()
    {
        using var t = new TempDir();
        var problems = new List<ProjectProblem>();
        Assert.Null(SfModProject.Load(Path.Combine(t.Path, "sfmod.json"), problems));
        Assert.Contains("dotnet new sfmod", problems.Single().Fix);
    }

    [Fact]
    public void BadJsonGivesTheLine()
    {
        using var t = Mod();
        var (p, problems) = Load(t, "{\n  \"plugin\": \"MyMod.esm\"\n  \"version\": \"0.1.0\"\n}");
        Assert.Null(p);
        Assert.Contains("line 3", problems.Single().Problem);
    }

    [Fact]
    public void UnknownFieldSuggestsTheNearOne()
    {
        using var t = Mod();
        var (p, problems) = Load(t, Good.Replace("\"version\"", "\"verison\""));
        Assert.Null(p);
        Assert.Contains(problems, x => x.Field == "verison" && x.Fix.Contains("\"version\""));
        Assert.Contains(problems, x => x.Field == "version" && x.Problem.Contains("missing"));
    }

    [Fact]
    public void UnknownNestedFieldNamesItsPath()
    {
        using var t = Mod();
        var (p, problems) = Load(t, Good.Replace("\"namespaces\"", "\"namespace\""));
        Assert.Null(p);
        Assert.Contains(problems, x => x.Field == "scripts.namespace" && x.Fix.Contains("\"namespaces\""));
    }

    [Theory]
    [InlineData("\"MyMod\"", "doesn't end in .esm")]
    [InlineData("\"sub/MyMod.esm\"", "plain file name")]
    [InlineData("\"MyModStarfield.esm\"", "official plugin name")]
    [InlineData("\"Big-ShatteredSpace.esm\"", "official plugin name")]
    [InlineData("\"BlueprintShips-Mine.esm\"", "BlueprintShips-")]
    [InlineData("\"ShatteredSpace.esm\"", "game's own plugins")]
    [InlineData("\"starfield.esm\"", "game's own plugins")]
    [InlineData("\"SFBGS003.esm\"", "game's own plugins")]
    [InlineData("12", "not a string")]
    public void BadPluginNames(string value, string expected)
    {
        using var t = Mod();
        var (p, problems) = Load(t, Good.Replace("\"MyMod.esm\"", value));
        Assert.Null(p);
        Assert.Contains(problems, x => x.Field == "plugin" && x.Problem.Contains(expected));
    }

    [Theory]
    [InlineData("MyMod-SFBGS003.esm")]
    [InlineData("MyModsfbgs00a.esm")]
    public void SfbgsEndingIsAWarning(string name)
    {
        using var t = Mod();
        var (p, problems) = Load(t, Good.Replace("MyMod.esm", name));
        Assert.True(p != null, All(problems));
        var w = Assert.Single(problems);
        Assert.True(w.IsWarning);
        Assert.Equal("plugin", w.Field);
        Assert.Contains("SFBGS", w.Problem);
    }

    [Fact]
    public void SfbgsInTheMiddleIsFine()
    {
        using var t = Mod();
        foreach (var name in new[] { "MySFBGS003Addon.esm" })
        {
            var (p, problems) = Load(t, Good.Replace("MyMod.esm", name));
            Assert.True(p != null && problems.Count == 0, name + ": " + All(problems));
        }
    }

    [Theory]
    [InlineData("0.1")]
    [InlineData("1.2.3.4")]
    public void GoodVersions(string v)
    {
        using var t = Mod();
        var (p, problems) = Load(t, Good.Replace("0.1.0", v));
        Assert.True(p != null, All(problems));
    }

    [Theory]
    [InlineData("v0.1")]
    [InlineData("2026-11-19")]
    [InlineData("1.2.3.4.5")]
    public void BadVersions(string v)
    {
        using var t = Mod();
        var (p, problems) = Load(t, Good.Replace("0.1.0", v));
        Assert.Null(p);
        Assert.Contains(problems, x => x.Field == "version");
    }

    [Fact]
    public void UnknownPlaceholderSuggestsTheNearOne()
    {
        using var t = Mod();
        var (p, problems) = Load(t, Good.Replace("\"{version}\"]", "\"{verion}\"]"));
        Assert.Null(p);
        var x = problems.Single(x => x.Field == "builder.args[2]");
        Assert.Contains("{verion}", x.Problem);
        Assert.Contains("{version}", x.Fix);
    }

    [Fact]
    public void ZipPlaceholdersAreNotBuilderPlaceholders()
    {
        using var t = Mod();
        var (p, problems) = Load(t, Good.Replace("README-{name}.md", "README-{dist}.md"));
        Assert.Null(p);
        Assert.Contains(problems, x => x.Field == "zip.files[\"README.md\"]" && x.Problem.Contains("{dist}"));
    }

    [Fact]
    public void BuilderWithoutDistIsAWarning()
    {
        using var t = Mod();
        var (p, problems) = Load(t, Good.Replace("\"{dist}\", ", ""));
        Assert.NotNull(p);
        Assert.True(problems.Single().IsWarning);
        Assert.Equal("builder.args", problems.Single().Field);
    }

    [Fact]
    public void MissingBuilderProjectIsAProblem()
    {
        using var t = Mod();
        var (p, problems) = Load(t, Good.Replace("\"project\": \"Builder\"", "\"project\": \"Maker\""));
        Assert.Null(p);
        Assert.Contains(problems, x => x.Field == "builder.project" && x.Problem.Contains("Maker"));
    }

    [Fact]
    public void BuilderFolderWithTwoProjectsAsksWhichOne()
    {
        using var t = Mod();
        t.File(Path.Combine("Builder", "Other.csproj"), "<Project />");
        var (p, problems) = Load(t, Good);
        Assert.Null(p);
        Assert.Contains(problems, x => x.Field == "builder.project" && x.Problem.Contains("2 .csproj"));
        Assert.NotNull(Load(t, Good.Replace("\"project\": \"Builder\"", "\"project\": \"Builder/Builder.csproj\"")).p);
    }

    [Fact]
    public void MissingNamespaceFolderIsAProblem()
    {
        using var t = Mod();
        var (p, problems) = Load(t, Good.Replace("[\"MYM\"]", "[\"MYM\", \"XYZ\"]"));
        Assert.Null(p);
        Assert.Contains(problems, x => x.Field == "scripts.namespaces[1]" && x.Problem.Contains("XYZ"));
    }

    [Fact]
    public void ExplicitScriptFiles()
    {
        using var t = Mod();
        var (p, problems) = Load(t, Good.Replace("\"namespaces\": [\"MYM\"]", "\"files\": [\"MYM/MyQuestScript.psc\"]"));
        Assert.True(p != null, All(problems));
        Assert.Equal(new[] { "MYM\\MyQuestScript" }, p!.Scripts!.Files);
        var (q, problems2) = Load(t, Good.Replace("\"namespaces\": [\"MYM\"]", "\"files\": [\"MYM/Nope\"]"));
        Assert.Null(q);
        Assert.Contains(problems2, x => x.Field == "scripts.files[0]");
    }

    [Fact]
    public void ScriptsWithNothingToCompileIsAProblem()
    {
        using var t = Mod();
        var (p, problems) = Load(t, Good.Replace("\"namespaces\": [\"MYM\"]", "\"namespaces\": []"));
        Assert.Null(p);
        Assert.Contains(problems, x => x.Field == "scripts" && x.Problem.Contains("nothing to compile"));
    }

    [Fact]
    public void NoScriptsIsFine()
    {
        using var t = Mod();
        var (p, problems) = Load(t, Good.Replace("\"scripts\": { \"source\": \"Scripts/Source\", \"namespaces\": [\"MYM\"] },", ""));
        Assert.True(p != null, All(problems));
        Assert.Null(p!.Scripts);
    }

    [Fact]
    public void ScriptSourceNeverGoesInTheZip()
    {
        using var t = Mod();
        var (p, problems) = Load(t, Good.Replace("\"LICENSE\": \"LICENSE-{name}.txt\"", "\"Scripts/Source/MYM/MyQuestScript.psc\": \"MyQuestScript.psc\""));
        Assert.Null(p);
        Assert.Contains(problems, x => x.Problem.Contains(".psc") && x.Problem.Contains("never"));
    }

    [Theory]
    [InlineData("../evil.md")]
    [InlineData("/abs.md")]
    [InlineData("C:/abs.md")]
    [InlineData("a//b.md")]
    public void BadNamesInsideTheZip(string inZip)
    {
        using var t = Mod();
        var (p, problems) = Load(t, Good.Replace("LICENSE-{name}.txt", inZip));
        Assert.Null(p);
        Assert.Contains(problems, x => x.Field == "zip.files[\"LICENSE\"]");
    }

    [Fact]
    public void TwoFilesWithOneZipNameIsAProblem()
    {
        using var t = Mod();
        var (p, problems) = Load(t, Good.Replace("LICENSE-{name}.txt", "readme-{name}.MD"));
        Assert.Null(p);
        Assert.Contains(problems, x => x.Problem.Contains("both be"));
    }

    [Fact]
    public void ZipFileThatIsThePluginIsAProblem()
    {
        using var t = Mod();
        var (p, problems) = Load(t, Good.Replace("LICENSE-{name}.txt", "{plugin}.esm"));
        Assert.Null(p);
        Assert.Contains(problems, x => x.Field == "zip.files" && x.Problem.Contains("MyMod.esm"));
    }

    [Fact]
    public void MissingZipFileIsAProblem()
    {
        using var t = Mod();
        var (p, problems) = Load(t, Good.Replace("\"LICENSE\":", "\"LICENSE.txt\":"));
        Assert.Null(p);
        Assert.Contains(problems, x => x.Field == "zip.files[\"LICENSE.txt\"]" && x.Problem.Contains("isn't there"));
    }

    [Fact]
    public void MissingZipFileFixSaysWhereABuiltFileGoes()
    {
        // A file the builder or a step makes isn't there at validate; the fix must say to put it in dist instead
        // (bug hunt, 2026-11-23: the old fix only said "give a file").
        using var t = Mod();
        var (_, problems) = Load(t, Good.Replace("\"LICENSE\":", "\"LICENSE.txt\":"));
        var problem = Assert.Single(problems, x => x.Field == "zip.files[\"LICENSE.txt\"]");
        Assert.Contains("dist", problem.Fix);
        Assert.Contains("steps.afterPlugin", problem.Fix);
    }

    [Fact]
    public void ZipArchivesLoadsAndShowsInValidateOrder()
    {
        using var t = Mod();
        var (p, problems) = Load(t, Good.Replace("\"zip\": {", "\"zip\": { \"archives\": [\"MyMod - Extra.ba2\"],"));
        Assert.True(p != null, All(problems));
        Assert.Equal(new[] { "MyMod - Extra.ba2" }, p!.ZipArchives);
    }

    [Theory]
    [InlineData("\"MyMod - Main.ba2\"", "pack uses for its own archives")]
    // no assets, so pack doesn't make this one, but it would delete it from dist as an old leftover
    [InlineData("\"mymod - textures.ba2\"", "pack uses for its own archives")]
    [InlineData("\"MyMod - Main_xbox.ba2\"", "pack uses for its own archives")]
    [InlineData("\"MyMod - Textures_xbox.ba2\"", "pack uses for its own archives")]
    [InlineData("\".ba2\"", "plain archive file name")]
    [InlineData("\"sub/Extra.ba2\"", "plain archive file name")]
    [InlineData("\"Extra.zip\"", "plain archive file name")]
    [InlineData("\"A.ba2\", \"a.BA2\"", "listed twice")]
    [InlineData("3", "it is a")]
    public void ZipArchivesBadEntriesAreProblems(string list, string words)
    {
        using var t = Mod();
        var (p, problems) = Load(t, Good.Replace("\"zip\": {", $"\"zip\": {{ \"archives\": [{list}],"));
        Assert.Null(p);
        Assert.Contains(problems, x => x.Field.StartsWith("zip.archives") && x.Problem.Contains(words));
    }

    [Fact]
    public void ZipArchivesThatIsNotAListIsAProblem()
    {
        using var t = Mod();
        var (p, problems) = Load(t, Good.Replace("\"zip\": {", "\"zip\": { \"archives\": \"Extra.ba2\","));
        Assert.Null(p);
        Assert.Contains(problems, x => x.Field == "zip.archives" && x.Problem.Contains("not a list"));
    }

    [Fact]
    public void ZipNameAndFolder()
    {
        using var t = Mod();
        var (p, problems) = Load(t, Good.Replace("\"zip\": {", "\"zip\": { \"name\": \"{name}-spike-{version}.zip\", \"folder\": \"dist\","));
        Assert.True(p != null, All(problems));
        Assert.Equal(Path.Combine(t.Path, "dist", "MyMod-spike-0.1.0.zip"), p!.ZipPath);
        var (q, problems2) = Load(t, Good.Replace("\"zip\": {", "\"zip\": { \"name\": \"{name}.7z\","));
        Assert.Null(q);
        Assert.Contains(problems2, x => x.Field == "zip.name" && x.Problem.Contains(".zip"));
    }

    [Fact]
    public void StageAndDistMustDiffer()
    {
        using var t = Mod();
        var (p, problems) = Load(t, Good.Replace("\"zip\":", "\"dist\": \"out\", \"stage\": \"out/\", \"zip\":"));
        Assert.Null(p);
        Assert.Contains(problems, x => x.Field == "stage" && x.Problem.Contains("same folder"));
        var (q, problems2) = Load(t, Good.Replace("\"zip\":", "\"stage\": \".\", \"zip\":"));
        Assert.Null(q);
        Assert.Contains(problems2, x => x.Field == "stage" && x.Problem.Contains("own folder"));
    }

    [Fact]
    public void BooleansMustBeBooleans()
    {
        using var t = Mod();
        var (p, problems) = Load(t, Good.Replace("\"zip\":", "\"xbox\": \"yes\", \"zip\":"));
        Assert.Null(p);
        Assert.Contains(problems, x => x.Field == "xbox" && x.Fix.Contains("true or false"));
    }

    [Fact]
    public void CheckSection()
    {
        using var t = Mod();
        var checker = t.File(Path.Combine("tools", "sf-mod-check.exe"));
        var (p, problems) = Load(t, Good.Replace("\"zip\":", "\"check\": { \"checker\": \"tools/sf-mod-check.exe\", \"required\": true, \"args\": [\"--strict\"] }, \"zip\":"));
        Assert.True(p != null, All(problems));
        Assert.Equal(checker, p!.Checker);
        Assert.True(p.CheckRequired);
        Assert.Equal(new[] { "--strict" }, p.CheckArgs);
        var (q, problems2) = Load(t, Good.Replace("\"zip\":", "\"check\": { \"checker\": \"nope.exe\" }, \"zip\":"));
        Assert.Null(q);
        Assert.Contains(problems2, x => x.Field == "check.checker");
    }

    [Fact]
    public void EveryProblemIsListedAtOnce()
    {
        using var t = Mod();
        var (p, problems) = Load(t, "{ \"plugin\": \"x\", \"version\": \"one\", \"oops\": 1 }");
        Assert.Null(p);
        Assert.Contains(problems, x => x.Field == "plugin");
        Assert.Contains(problems, x => x.Field == "version");
        Assert.Contains(problems, x => x.Field == "oops");
        Assert.Contains(problems, x => x.Field == "builder");
    }

    [Fact]
    public void FormatNamesFileFieldAndFix()
    {
        var x = new ProjectProblem("builder.args[1]", "it is a number.", "write it as a string.");
        Assert.Equal("PROBLEM: C:\\m\\sfmod.json: builder.args[1]: it is a number. Fix: write it as a string.", x.Format("C:\\m\\sfmod.json"));
    }

    [Fact]
    public void ExpandFillsPlaceholders()
    {
        Assert.Equal("MyMod-0.1.0.zip", SfModProject.Expand("{name}-{version}.zip", new Dictionary<string, string> { ["name"] = "MyMod", ["version"] = "0.1.0" }));
    }
}

public class ValidateCommandTests
{
    [Fact]
    public void ValidateOkAndProblems()
    {
        using var t = new TempDir();
        t.File(Path.Combine("Builder", "Builder.csproj"), "<Project />");
        var file = t.File("sfmod.json", "{ \"plugin\": \"MyMod.esm\", \"version\": \"0.1.0\", \"builder\": { \"project\": \"Builder\", \"args\": [\"{data}\", \"{dist}\"] } }");
        var o = new StringWriter();
        Assert.Equal(0, App.Run(new[] { "validate", file }, new FakeHostEnv(), o));
        Assert.Contains("RESULT: OK", o.ToString());
        Assert.Contains("MyMod-0.1.0.zip", o.ToString());

        File.WriteAllText(file, "{ \"plugin\": \"MyMod.esm\" }");
        o = new StringWriter();
        Assert.Equal(2, App.Run(new[] { "validate", file }, new FakeHostEnv(), o));
        Assert.Contains("PROBLEM: " + file + ": version: it is missing.", o.ToString());
        Assert.Contains("RESULT: 2 PROBLEM(S)", o.ToString());
    }

    [Fact]
    public void ValidateTakesOnePath()
    {
        var o = new StringWriter();
        Assert.Equal(2, App.Run(new[] { "validate", "a.json", "b.json" }, new FakeHostEnv(), o));
    }
}

public partial class SfModProjectTests
{
    // ---- builder kinds (0.4.0): command and prebuilt

    const string ProjectBuilder = "\"builder\": { \"project\": \"Builder\", \"args\": [\"{data}\", \"{dist}\", \"{version}\"] }";

    [Fact]
    public void CommandBuilderWithABareProgramLoads()
    {
        using var t = Mod();
        var (p, problems) = Load(t, Good.Replace(ProjectBuilder, "\"builder\": { \"command\": [\"python\", \"tools/build.py\", \"--out\", \"{dist}\"], \"args\": [\"{version}\"] }"));
        Assert.True(p != null, All(problems));
        Assert.Empty(problems);
        Assert.Null(p!.BuilderProject);
        Assert.Equal("command", p.BuilderKind);
        Assert.Equal(new[] { "python", "tools/build.py", "--out", "{dist}" }, p.BuilderCommand);
        Assert.Equal(new[] { "{version}" }, p.BuilderArgs);
        Assert.Empty(p.BuilderFolders);
    }

    [Fact]
    public void CommandBuilderWithAPathProgramIsMadeFull()
    {
        using var t = Mod();
        t.File(Path.Combine("tools", "make plugin.cmd"), "@echo off");
        var (p, problems) = Load(t, Good.Replace(ProjectBuilder, "\"builder\": { \"command\": [\"tools/make plugin.cmd\"], \"args\": [\"{dist}\"] }"));
        Assert.True(p != null, All(problems));
        Assert.Equal(Path.Combine(t.Path, "tools", "make plugin.cmd"), p!.BuilderCommand![0]);
        Assert.Equal(new[] { Path.Combine(t.Path, "tools") }, p.BuilderFolders);
    }

    [Theory]
    [InlineData("\"command\": [\"tools/nope.py\"], \"args\": []", "builder.command[0]", "isn't there")]
    [InlineData("\"command\": [\"{dist}/x.exe\"], \"args\": []", "builder.command[0]", "placeholder")]
    [InlineData("\"command\": \"python build.py\", \"args\": []", "builder.command", "not a list")]
    [InlineData("\"command\": [], \"args\": []", "builder.command", "empty")]
    [InlineData("\"command\": [\"python\", \"{dsit}\"], \"args\": []", "builder.command[1]", "isn't a placeholder")]
    [InlineData("\"command\": [\"python\"]", "builder.args", "missing")]
    [InlineData("\"args\": []", "builder", "names no builder")]
    [InlineData("\"project\": \"Builder\", \"command\": [\"python\"], \"args\": []", "builder", "both \"project\" and \"command\"")]
    [InlineData("\"prebuilt\": \"Original/MyMod.esm\", \"args\": [\"{dist}\"]", "builder.args", "copied, not run")]
    [InlineData("\"prebuilt\": \"Original/Nope.esm\"", "builder.prebuilt", "isn't there")]
    [InlineData("\"prebuilt\": \"README.md\"", "builder.prebuilt", "doesn't end in .esm")]
    [InlineData("\"prebuilt\": \"dist/Old.esm\"", "builder.prebuilt", "which the build empties")]
    public void BadBuilderKindsAreProblems(string builder, string field, string words)
    {
        using var t = Mod();
        t.File(Path.Combine("Original", "MyMod.esm"), "made-up plugin");
        t.File(Path.Combine("dist", "Old.esm"), "made-up plugin");
        var (p, problems) = Load(t, Good.Replace(ProjectBuilder, "\"builder\": { " + builder + " }"));
        Assert.Null(p);
        Assert.Contains(problems, x => x.Field == field && x.Problem.Contains(words));
    }

    [Fact]
    public void PrebuiltBuilderLoadsWithoutArgs()
    {
        using var t = Mod();
        var original = t.File(Path.Combine("Original", "Their Mod.esm"), "made-up plugin");
        var (p, problems) = Load(t, Good.Replace(ProjectBuilder, "\"builder\": { \"prebuilt\": \"Original/Their Mod.esm\" }"));
        Assert.True(p != null, All(problems));
        Assert.Empty(problems);
        Assert.Equal("prebuilt", p!.BuilderKind);
        Assert.Equal(original, p.BuilderPrebuilt);
        Assert.Empty(p.BuilderArgs);
    }

    [Fact]
    public void ValidatePrintsWhatEachBuilderRuns()
    {
        using var t = Mod();
        var file = t.File("sfmod.json", Good);
        var o = new StringWriter();
        Assert.Equal(0, App.Run(new[] { "validate", file }, new FakeHostEnv(), o));
        Assert.Contains($"runs:     dotnet run -c Release --project {Path.Combine(t.Path, "Builder", "Builder.csproj")} -- {{data}} {{dist}} {{version}}", o.ToString());

        File.WriteAllText(file, Good.Replace(ProjectBuilder, "\"builder\": { \"command\": [\"python\", \"tools/build.py\", \"two words\"], \"args\": [\"{dist}\"] }"));
        o = new StringWriter();
        Assert.Equal(0, App.Run(new[] { "validate", file }, new FakeHostEnv(), o));
        Assert.Contains("builder:  command python", o.ToString());
        Assert.Contains("runs:     python tools/build.py \"two words\" {dist}", o.ToString());

        t.File(Path.Combine("Original", "MyMod.esm"), "made-up plugin");
        File.WriteAllText(file, Good.Replace(ProjectBuilder, "\"builder\": { \"prebuilt\": \"Original/MyMod.esm\" }"));
        o = new StringWriter();
        Assert.Equal(0, App.Run(new[] { "validate", file }, new FakeHostEnv(), o));
        Assert.Contains($"builder:  prebuilt {Path.Combine(t.Path, "Original", "MyMod.esm")}, copied into dist as MyMod.esm", o.ToString());
        Assert.DoesNotContain("runs:", o.ToString());
    }
}
