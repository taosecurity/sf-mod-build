using System.Text.Json;
using Xunit;

namespace SfModBuild.Tests;

/// <summary>Checks the dotnet new sfmod template (templates\sfmod, copied into the test output as template\) with
/// no game: its sfmod.json passes validation, and the names the template engine replaces line up.</summary>
public class TemplateTests
{
    static readonly string Template = Path.Combine(AppContext.BaseDirectory, "template");

    static string Read(params string[] parts) => File.ReadAllText(Path.Combine([Template, .. parts]));

    [Fact]
    public void TemplateSfmodJsonPassesValidation()
    {
        var problems = new List<ProjectProblem>();
        var p = SfModProject.Load(Path.Combine(Template, "sfmod.json"), problems);
        Assert.True(problems.Count == 0, string.Join("\n", problems.Select(x => x.Format("sfmod.json"))));
        Assert.NotNull(p);
        Assert.Equal("MyMod.esm", p!.Plugin);
        Assert.Equal("0.1.0", p.Version);
    }

    [Fact]
    public void TemplateConfigNamesMatchTheFiles()
    {
        using var doc = JsonDocument.Parse(Read(".template.config", "template.json"));
        var root = doc.RootElement;
        Assert.Equal("sfmod", root.GetProperty("shortName").GetString());
        Assert.Equal("MyMod", root.GetProperty("sourceName").GetString());
        var prefix = root.GetProperty("symbols").GetProperty("prefix");
        Assert.Equal("MYM", prefix.GetProperty("defaultValue").GetString());
        // The prefix the user types is cleaned into a Papyrus-safe namespace before it replaces MYM.
        var safe = root.GetProperty("symbols").GetProperty("safePrefix");
        Assert.Equal("prefix", safe.GetProperty("parameters").GetProperty("source").GetString());
        Assert.Equal("MYM", safe.GetProperty("replaces").GetString());
        Assert.Equal("MYM", safe.GetProperty("fileRename").GetString());

        // The script sits in the prefix's namespace folder, and the builder attaches it by that name.
        var psc = Read("Scripts", "Source", "MYM", "StarterQuestScript.psc");
        Assert.StartsWith("Scriptname MYM:StarterQuestScript extends Quest", psc);
        Assert.Contains("GameplayOption Property MYM_Option_Debug Auto Const Mandatory", psc);
        Assert.Contains("Debug.Trace(\"[MyMod] \"", psc);
        var program = Read("Builder", "Program.cs");
        Assert.Contains("\"MYM:StarterQuestScript\"", program);
        Assert.Contains("Name = \"MYM_Option_Debug\"", program);
        Assert.Contains("GameplayOptions.Debug(mod, \"MYM_Option_Debug\"", program);
        Assert.Contains("Plugin.New(\"MyMod.esm\"", program);
    }

    [Fact]
    public void AuthorOptionIsModAuthorOnTheCommandLine()
    {
        // "--author" is one of dotnet new's own options, so without this the option shows up as --param:author.
        using var doc = JsonDocument.Parse(Read(".template.config", "dotnetcli.host.json"));
        var info = doc.RootElement.GetProperty("symbolInfo");
        Assert.Equal("mod-author", info.GetProperty("author").GetProperty("longName").GetString());
        Assert.Equal("prefix", info.GetProperty("prefix").GetProperty("longName").GetString());
        using var template = JsonDocument.Parse(Read(".template.config", "template.json"));
        Assert.Equal("AUTHOR_NAME", template.RootElement.GetProperty("symbols").GetProperty("author").GetProperty("replaces").GetString());
    }

    [Fact]
    public void TemplateBuilderPinsExactVersions()
    {
        var csproj = Read("Builder", "MyMod.Builder.csproj");
        Assert.Contains("<PackageReference Include=\"Taosecurity.Starfield.BuildKit\" Version=\"[0.4.2]\" />", csproj);
        Assert.Contains("<PackageReference Include=\"Mutagen.Bethesda.Starfield\" Version=\"[0.54.4]\" />", csproj);
        Assert.Contains("<TargetFramework>net9.0</TargetFramework>", csproj);
        Assert.Contains("<RollForward>Major</RollForward>", csproj);
    }

    [Fact]
    public void TemplateReadmeSaysHowToTurnOnPapyrusLogging()
    {
        var readme = Read("README.md");
        Assert.Contains("bEnableLogging=1", readme);
        Assert.Contains("bEnableTrace=1", readme);
        Assert.Contains("Show Debug Messages (PC only)", readme);
        Assert.True(File.Exists(Path.Combine(Template, "LICENSE")));
        Assert.True(File.Exists(Path.Combine(Template, ".gitignore")));
    }
}
