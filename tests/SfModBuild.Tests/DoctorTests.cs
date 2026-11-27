using Xunit;

namespace SfModBuild.Tests;

public class DoctorTests
{
    static string Escape(string path) => path.Replace("\\", "\\\\");

    /// <summary>A fake Steam folder whose libraryfolders.vdf lists a second library holding the game.</summary>
    static (string steam, string game) SteamWithLibrary(TempDir t, FakeHostEnv env, bool gameInSecondLibrary = true)
    {
        var steam = t.Dir("Steam");
        var lib = t.Dir("SteamLibrary");
        t.File(Path.Combine("Steam", "steamapps", "libraryfolders.vdf"),
            $"\"libraryfolders\"\n{{\n \"0\" {{ \"path\" \"{Escape(steam)}\" }}\n \"1\" {{ \"path\" \"{Escape(lib)}\" }}\n}}\n");
        var game = Fake.Game(t, Path.Combine(gameInSecondLibrary ? "SteamLibrary" : "Steam", "steamapps", "common", "Starfield"));
        if (env.IsWindows) env.Registry[(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath")] = steam.Replace('\\', '/');
        else env.Vars["HOME"] = t.Path;   // not used on Windows
        return (steam, game);
    }

    [Fact]
    public void FindsSteamGameThroughLibraryFolders()
    {
        using var t = new TempDir();
        var env = Fake.EnvWithPath(t, "dotnet", "sfmodcheck");
        if (!env.IsWindows) return;   // the Linux Steam paths are tested below
        var (_, game) = SteamWithLibrary(t, env);
        var r = Doctor.Examine(null, null, env, out var error);
        Assert.Null(error);
        Assert.Equal(game, r!.Game!.Folder);
        Assert.Equal("Steam", r.Game.Kind);
        Assert.Contains("libraryfolders.vdf", r.Game.How);
        Assert.Equal(0, r.MissingRequired);
        Assert.Equal(0, r.MissingOptional);
    }

    [Fact]
    public void LinuxSteamFolderIsFound()
    {
        using var t = new TempDir();
        var env = Fake.EnvWithPath(t, "dotnet");
        env.IsWindows = false;
        env.Vars["HOME"] = t.Path;
        var game = Fake.Game(t, Path.Combine(".steam", "steam", "steamapps", "common", "Starfield"));
        var found = GameLocator.FindAll(env);
        Assert.Equal(game, Assert.Single(found).Folder);
    }

    [Fact]
    public void SteamComesBeforeGamePassAndGamePassIsNoted()
    {
        using var t = new TempDir();
        var env = Fake.EnvWithPath(t, "dotnet");
        if (!env.IsWindows) return;
        var (_, steamGame) = SteamWithLibrary(t, env, gameInSecondLibrary: false);
        var drive = t.Dir("driveD");
        var gp = Fake.Game(t, Path.Combine("driveD", "XboxGames", "Starfield", "Content"), tools: false);
        env.Drives.Add(drive);
        var r = Doctor.Examine(null, null, env, out _)!;
        Assert.Equal(steamGame, r.Game!.Folder);
        var other = Assert.Single(r.OtherInstalls);
        Assert.Equal(gp, other.Folder);
        Assert.Equal("Game Pass", other.Kind);
        var o = new StringWriter();
        Doctor.Print(r, o);
        Assert.Contains("another Starfield install was found", o.ToString());
    }

    [Fact]
    public void UnreadableOrMissingVdfStillTriesTheSteamFolder()
    {
        using var t = new TempDir();
        var env = Fake.EnvWithPath(t, "dotnet");
        if (!env.IsWindows) return;
        var steam = t.Dir("Steam");
        t.File(Path.Combine("Steam", "steamapps", "libraryfolders.vdf"), "\"libraryfolders\" { broken");
        var game = Fake.Game(t, Path.Combine("Steam", "steamapps", "common", "Starfield"));
        env.Registry[(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath")] = steam;
        Assert.Equal(game, Assert.Single(GameLocator.FindAll(env)).Folder);
    }

    [Fact]
    public void GameOptionWorksAndDataFolderMeansItsParent()
    {
        using var t = new TempDir();
        var env = Fake.EnvWithPath(t, "dotnet", "sf-mod-check");
        var game = Fake.Game(t, "MyGame");
        t.File(Path.Combine("MyGame", "Data", "Starfield.esm"));
        var r = Doctor.Examine(Path.Combine(game, "Data"), null, env, out var error);
        Assert.Null(error);
        Assert.Equal(game, r!.Game!.Folder);
        Assert.Equal("given with --game", r.Game.How);
        Assert.EndsWith(Fake.ExeName("sf-mod-check"), r.Items.Single(i => i.Name == "Checker").Path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GameOptionThatIsNotAGameIsBadInput()
    {
        using var t = new TempDir();
        var env = Fake.EnvWithPath(t, "dotnet");
        var notGame = t.Dir("Empty");
        Assert.Null(Doctor.Examine(notGame, null, env, out var error));
        Assert.Contains("no Starfield.exe", error);
        Assert.Null(Doctor.Examine(Path.Combine(t.Path, "Nope"), null, env, out error));
        Assert.Contains("doesn't exist", error);
    }

    [Fact]
    public void MissingToolsSayWhatTheyAreForAndHowToGetThem()
    {
        using var t = new TempDir();
        var env = Fake.EnvWithPath(t);   // no dotnet, no checker
        var game = Fake.Game(t, "Bare", tools: false);
        var r = Doctor.Examine(game, null, env, out _)!;
        Assert.Equal(4, r.MissingRequired);    // compiler, Archive2, ContentResources.zip, .NET SDK
        Assert.Equal(1, r.MissingOptional);    // checker
        var o = new StringWriter();
        Doctor.Print(r, o);
        var text = o.ToString();
        Assert.Contains("MISSING  Papyrus compiler", text);
        Assert.Contains("MISSING  Archive2", text);
        Assert.Contains("MISSING  .NET SDK", text);
        Assert.Contains("WARNING  Checker", text);
        Assert.Contains("Starfield Creation Kit", text);
        Assert.Contains("https://dotnet.microsoft.com/download/dotnet/9.0", text);
        Assert.Contains("https://github.com/taosecurity/sf-mod-check/releases", text);
        Assert.DoesNotContain("github.com/taosecurity/sfmodcheck", text);
        Assert.Contains("RESULT: MISSING 4", text);
        Assert.Equal(8, text.Split('\n').Count(l => l.Contains("what it is for:")));   // 5 missing items + 3 setup warnings
    }

    [Fact]
    public void NoGameAnywhereIsMissing()
    {
        using var t = new TempDir();
        var env = Fake.EnvWithPath(t, "dotnet");
        var r = Doctor.Examine(null, null, env, out var error)!;
        Assert.Null(error);
        Assert.Null(r.Game);
        Assert.False(r.Items.Single(i => i.Name == "Starfield").Found);
        Assert.Contains("--game", r.Items.Single(i => i.Name == "Starfield").HowToGet);
    }

    [Theory]
    [InlineData("8.0.400 [x]\n", false)]
    [InlineData("", false)]
    [InlineData("8.0.400 [x]\n9.0.321 [x]\n10.0.404 [x]\n", true)]
    public void DotnetNeedsSdk9OrLater(string listSdks, bool ok)
    {
        using var t = new TempDir();
        var env = Fake.EnvWithPath(t, "dotnet");
        env.RunResult = (_, _) => listSdks;
        var r = Doctor.Examine(Fake.Game(t, "G"), null, env, out _)!;
        Assert.Equal(ok, r.Items.Single(i => i.Name == ".NET SDK").Found);
        Assert.Contains(env.Ran, x => x.EndsWith("--list-sdks"));
    }

    [Fact]
    public void CheckerOptionMustExist()
    {
        using var t = new TempDir();
        var env = Fake.EnvWithPath(t, "dotnet");
        var game = Fake.Game(t, "G");
        Assert.Null(Doctor.Examine(game, Path.Combine(t.Path, "nope.exe"), env, out var error));
        Assert.Contains("--checker", error);
        var exe = t.File(Path.Combine("tools", "checker.exe"));
        var r = Doctor.Examine(game, exe, env, out error)!;
        Assert.Equal(exe, r.Items.Single(i => i.Name == "Checker").Path);
        Assert.Equal(0, r.MissingRequired + r.MissingOptional);
    }

    [Fact]
    public void ExitCodes()
    {
        using var t = new TempDir();
        var env = Fake.EnvWithPath(t, "dotnet");
        var game = Fake.Game(t, "G");
        var o = new StringWriter();
        Assert.Equal(0, App.Run(new[] { "doctor", "--game", game }, env, o));          // checker missing = warning only
        Assert.Contains("RESULT: OK, ready to build (with 4 WARNINGS", o.ToString());   // the checker + 3 setup warnings
        Assert.Equal(2, App.Run(new[] { "doctor", "--game", t.Dir("Empty") }, env, new StringWriter()));
        Assert.Equal(2, App.Run(new[] { "doctor", "--bogus" }, env, new StringWriter()));
        Assert.Equal(2, App.Run(new[] { "frobnicate" }, env, new StringWriter()));
        Assert.Equal(2, App.Run(Array.Empty<string>(), env, new StringWriter()));
        var v = new StringWriter();
        Assert.Equal(0, App.Run(new[] { "--version" }, env, v));
        Assert.StartsWith("sf-mod-build 0.5.2 ", v.ToString());
        var h = new StringWriter();
        Assert.Equal(0, App.Run(new[] { "doctor", "--help" }, env, h));
        Assert.Contains("usage: sf-mod-build doctor", h.ToString());
    }

    [Fact]
    public void LogFileGetsTheRun()
    {
        using var t = new TempDir();
        var env = Fake.EnvWithPath(t, "dotnet");
        var game = Fake.Game(t, "G");
        var log = Path.Combine(t.Path, "log.txt");
        Assert.Equal(0, App.Run(new[] { "doctor", "--game", game, "--log", log }, env, new StringWriter()));
        var text = File.ReadAllText(log);
        Assert.Contains("command line: doctor", text);
        Assert.Contains("found: Archive2", text);
        Assert.Contains("exit code 0", text);
        Assert.Equal(2, App.Run(new[] { "doctor", "--game", game, "--log", Path.Combine(t.Path, "x.esm") }, env, new StringWriter()));
    }
}
