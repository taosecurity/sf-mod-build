using Xunit;

namespace SfModBuild.Tests;

public class SetupChecksTests
{
    const string GoodGameIni = "[Archive]\nbInvalidateOlderFiles=1\n\n[Papyrus]\nbEnableLogging=1\nbEnableTrace=1\nbLoadDebugInformation=1\n";
    const string GoodCkIni = "[Materials]\nbUseCompiledDB=0\n\n[Papyrus]\nsScriptSourceFolder=\"Data/Scripts/Source\"\n\n[Archive]\nbInvalidateOlderFiles=1\n";

    /// <summary>A fake Steam game in lib\steamapps\common\Starfield and the folder doctor reads StarfieldCustom.ini
    /// from by default on this system (Documents\My Games\Starfield on Windows, the game's Proton folder elsewhere),
    /// both inside the temp folder.</summary>
    static (TempDir t, FakeHostEnv env, string game, string myGames) Setup()
    {
        var t = new TempDir();
        var env = Fake.EnvWithPath(t, "dotnet", "sf-mod-check");
        env.Documents = t.Dir("Documents");
        var game = Fake.Game(t, Path.Combine("lib", "steamapps", "common", "Starfield"));
        var myGames = SetupChecks.MyGamesFolder(null, new GameInstall(game, "Steam", "test"), env)!;
        Assert.StartsWith(t.Path, myGames);
        Directory.CreateDirectory(myGames);
        return (t, env, game, myGames);
    }

    static void Write(string path, string text = "x")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    static SetupCheck Find(DoctorReport r, string start) => r.Setup.Single(s => s.Name.StartsWith(start, StringComparison.Ordinal));

    [Fact]
    public void AllGoodGivesNoWarnings()
    {
        var (t, env, game, myGames) = Setup();
        using (t)
        {
            Write(Path.Combine(myGames, "StarfieldCustom.ini"), GoodGameIni);
            Write(Path.Combine(game, "CreationKitCustom.ini"), GoodCkIni);
            Write(Path.Combine(game, "Data", "Scripts", "Source", Compile.FlagsFile));
            var r = Doctor.Examine(game, null, env, out var error)!;
            Assert.Null(error);
            Assert.Equal(3, r.Setup.Count);
            Assert.All(r.Setup, s => Assert.True(s.Ok, s.Name + ": " + s.Detail));
            var o = new StringWriter();
            Doctor.Print(r, o);
            Assert.Contains("RESULT: OK, ready to build\n", o.ToString().Replace("\r\n", "\n"));
            Assert.Contains("OK       StarfieldCustom.ini", o.ToString());
        }
    }

    [Fact]
    public void MissingPapyrusLinesAreListedUnderTheirSection()
    {
        var (t, env, game, myGames) = Setup();
        using (t)
        {
            // trace off, debug information missing; a comment after a value doesn't hide it
            Write(Path.Combine(myGames, "StarfieldCustom.ini"), "[papyrus]\nBENABLELOGGING=1 ; on\nbEnableTrace=0\n[Archive]\nbInvalidateOlderFiles=1\n");
            var r = Doctor.Examine(game, null, env, out _)!;
            var c = Find(r, "StarfieldCustom.ini");
            Assert.False(c.Ok);
            Assert.Contains("bEnableTrace=1", c.Detail);
            Assert.Contains("bLoadDebugInformation=1", c.Detail);
            Assert.DoesNotContain("bEnableLogging", c.Detail);
            Assert.Equal(new[] { "[Papyrus]", "bEnableTrace=1", "bLoadDebugInformation=1" }, c.Fix.Skip(1));
            var o = new StringWriter();
            Doctor.Print(r, o);
            Assert.Contains("WARNING  StarfieldCustom.ini", o.ToString());
            Assert.Contains("             bLoadDebugInformation=1", o.ToString());
        }
    }

    [Fact]
    public void LaterLinesWinAndTheLastSectionCounts()
    {
        var v = SetupChecks.ReadIni(new[] { "[Papyrus]", "bEnableTrace=1", "; bEnableTrace=0", "bEnableTrace = 0", "[General]", "bEnableTrace=1" });
        Assert.Equal("0", v[("papyrus", "benabletrace")]);
        Assert.Equal("1", v[("general", "benabletrace")]);
    }

    [Fact]
    public void IniTxtIsCalledOut()
    {
        var (t, env, game, myGames) = Setup();
        using (t)
        {
            Write(Path.Combine(myGames, "StarfieldCustom.ini.txt"), GoodGameIni);
            var c = Find(Doctor.Examine(game, null, env, out _)!, "StarfieldCustom.ini");
            Assert.False(c.Ok);
            Assert.Contains(".ini.txt", c.Detail);
            Assert.Contains("File name extensions", c.Fix[0]);
        }
    }

    [Fact]
    public void NoFileGivesTheWholeFile()
    {
        var (t, env, game, _) = Setup();
        using (t)
        {
            var r = Doctor.Examine(game, null, env, out _)!;
            var c = Find(r, "StarfieldCustom.ini");
            Assert.False(c.Ok);
            Assert.Equal(new[] { "[Papyrus]", "bEnableLogging=1", "bEnableTrace=1", "bLoadDebugInformation=1", "[Archive]", "bInvalidateOlderFiles=1" }, c.Fix.Skip(1));
            var ck = Find(r, "CreationKitCustom.ini");
            Assert.False(ck.Ok);
            Assert.Contains("sScriptSourceFolder=\"Data/Scripts/Source\"", ck.Fix);
            Assert.Contains("bUseCompiledDB=0", ck.Fix);
        }
    }

    [Fact]
    public void NoMyGamesFolderSaysStartTheGameOnce()
    {
        var (t, env, game, myGames) = Setup();
        using (t)
        {
            Directory.Delete(myGames);
            var c = Find(Doctor.Examine(game, null, env, out _)!, "StarfieldCustom.ini");
            Assert.False(c.Ok);
            Assert.Contains("start the game once", c.Fix[0]);
        }
    }

    [Theory]
    [InlineData("sScriptSourceFolder=Data\\Scripts\\Source\\", true)]
    [InlineData("sScriptSourceFolder=\"data/scripts/source/\"", true)]
    [InlineData("sScriptSourceFolder=\"../Source/Scripts/\"", false)]
    public void ScriptSourceFolderAcceptsEitherSlash(string line, bool ok)
    {
        var (t, env, game, _) = Setup();
        using (t)
        {
            Write(Path.Combine(game, "CreationKitCustom.ini"), $"[Materials]\nbUseCompiledDB=0\n[Papyrus]\n{line}\n[Archive]\nbInvalidateOlderFiles=1\n");
            Assert.Equal(ok, Find(Doctor.Examine(game, null, env, out _)!, "CreationKitCustom.ini").Ok);
        }
    }

    [Fact]
    public void SourcesUnpackedOneFolderTooDeepIsCalledOut()
    {
        var (t, env, game, _) = Setup();
        using (t)
        {
            Write(Path.Combine(game, "Data", "ContentResources", "Scripts", "Source", Compile.FlagsFile));
            var c = Find(Doctor.Examine(game, null, env, out _)!, "Script sources");
            Assert.False(c.Ok);
            Assert.Contains("too deep", c.Detail);
            Assert.Contains("up into", c.Fix[0]);
        }
    }

    [Fact]
    public void MyGamesOptionIsUsedAndMustExist()
    {
        var (t, env, game, _) = Setup();
        using (t)
        {
            var profile = t.Dir(Path.Combine("MO2", "profiles", "Default"));
            Write(Path.Combine(profile, "StarfieldCustom.ini"), GoodGameIni);
            var r = Doctor.Examine(game, null, env, out var error, profile)!;
            Assert.Null(error);
            var c = Find(r, "StarfieldCustom.ini");
            Assert.True(c.Ok, c.Detail);
            Assert.Equal(Path.Combine(profile, "StarfieldCustom.ini"), c.Path);
            Assert.Null(Doctor.Examine(game, null, env, out error, Path.Combine(t.Path, "nope")));
            Assert.Contains("--my-games", error);
            Assert.Equal(2, App.Run(new[] { "doctor", "--game", game, "--my-games", Path.Combine(t.Path, "nope") }, env, new StringWriter()));
            Assert.Equal(0, App.Run(new[] { "doctor", "--game", game, "--my-games", profile }, env, new StringWriter()));
        }
    }

    [Fact]
    public void WindowsLooksInDocumentsAndLinuxInTheProtonPrefix()
    {
        using var t = new TempDir();
        var game = new GameInstall(Path.Combine(t.Path, "lib", "steamapps", "common", "Starfield"), "Steam", "test");
        var windows = new FakeHostEnv { IsWindows = true, Documents = Path.Combine(t.Path, "Docs") };
        Assert.Equal(Path.Combine(t.Path, "Docs", "My Games", "Starfield"), SetupChecks.MyGamesFolder(null, game, windows));
        Assert.Null(SetupChecks.MyGamesFolder(null, game, new FakeHostEnv { IsWindows = true }));   // no Documents folder
        var linux = new FakeHostEnv { IsWindows = false };
        Assert.Equal(Path.Combine(t.Path, "lib", "steamapps", "compatdata", "1716740", "pfx", "drive_c", "users", "steamuser", "Documents", "My Games", "Starfield"),
            SetupChecks.MyGamesFolder(null, game, linux));
        Assert.Null(SetupChecks.MyGamesFolder(null, null, linux));
    }

    [Fact]
    public void NoDocumentsFolderIsAWarningThatNamesTheOption()
    {
        using var t = new TempDir();
        var env = new FakeHostEnv { IsWindows = true };   // Documents left null
        var c = SetupChecks.Run(new GameInstall(Fake.Game(t, "G"), "Steam", "test"), null, env).Single(s => s.Name.StartsWith("StarfieldCustom.ini", StringComparison.Ordinal));
        Assert.False(c.Ok);
        Assert.Contains("--my-games", c.Fix[0]);
    }
}
