// sf-mod-build doctor: finds the game and every tool a build needs, and says for each missing one what it is for and
// how to get it. Exit 0 when everything a build needs is there (a missing checker is only a warning: a build still
// works, it just isn't checked), 2 when something a build needs is missing or the options are wrong.
using System.Text.RegularExpressions;

namespace SfModBuild;

internal sealed record DoctorItem(string Name, bool Found, bool Required, string? Path, string For, string? HowToGet, string? Detail = null);

internal sealed record DoctorReport(GameInstall? Game, List<GameInstall> OtherInstalls, List<DoctorItem> Items)
{
    public int MissingRequired => Items.Count(i => i.Required && !i.Found);
    public int MissingOptional => Items.Count(i => !i.Required && !i.Found);
}

internal static class Doctor
{
    public const string Usage =
        "usage: sf-mod-build doctor [--game <game folder>] [--checker <sf-mod-check exe>] [--verbose] [--log <file>]";

    public static readonly ArgSpec Spec = new(new[] { "--game", "--checker" }, Array.Empty<string>());

    public const string CkHowTo =
        "Install the Starfield Creation Kit from Steam (Library, then Tools, then \"Starfield Creation Kit\"). " +
        "It installs into the game folder's Tools folder. If the game is somewhere else, give it with --game <folder>.";

    public static int Run(ParsedArgs p, IHostEnv env, TextWriter stdout)
    {
        var report = Examine(p.Get("--game"), p.Get("--checker"), env, out var error);
        if (report == null)
        {
            Console.Error.WriteLine(error);
            stdout.WriteLine($"RESULT: CANNOT CHECK ({error})");
            return ExitCodes.BadInput;
        }
        Print(report, stdout);
        return report.MissingRequired > 0 ? ExitCodes.BadInput : ExitCodes.Ok;
    }

    /// <summary>Looks for the game and the tools. Null (with an error naming the option and the fix) only when an
    /// option given is unusable; a game that isn't found is a missing item in the report.</summary>
    public static DoctorReport? Examine(string? gameOption, string? checkerOption, IHostEnv env, out string? error)
    {
        error = null;
        GameInstall? game;
        var others = new List<GameInstall>();
        if (gameOption != null)
        {
            game = GameLocator.FromOption(gameOption, out error);
            if (game == null) return null;
            Log.Write($"game (from --game): {game.Folder}");
        }
        else
        {
            var all = GameLocator.FindAll(env);
            game = all.FirstOrDefault();
            others = all.Skip(1).ToList();
            Log.Write(game == null ? "no Starfield install found" : $"game: {game.Folder} ({game.How})");
        }

        string? checkerGiven = null;
        if (checkerOption != null)
        {
            try { checkerGiven = System.IO.Path.GetFullPath(checkerOption.Trim().Trim('"')); }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
            {
                error = $"--checker \"{checkerOption}\" is not a usable file path ({e.Message}). Give the full path of sf-mod-check.exe or sfmodcheck.exe.";
                return null;
            }
            if (!File.Exists(checkerGiven))
            {
                error = $"--checker {checkerGiven}: that file doesn't exist. Give the full path of sf-mod-check.exe or sfmodcheck.exe, for example --checker {Args.Example("--checker")}";
                return null;
            }
        }

        var items = new List<DoctorItem>();
        items.Add(new DoctorItem("Starfield", game != null, true, game?.Folder,
            "the game folder holds the Creation Kit's tools and the Data folder your mod's builder reads",
            "Install Starfield (Steam or Game Pass). If it is installed somewhere this tool didn't look, give the folder that holds Starfield.exe with --game <folder>.",
            game?.How));

        string Tool(string rel) => game == null ? rel : System.IO.Path.Combine(game.Folder, rel);
        items.Add(FileItem("Papyrus compiler", Tool(System.IO.Path.Combine("Tools", "Papyrus Compiler", "PapyrusCompiler.exe")), game != null,
            "compiles your mod's Papyrus scripts (.psc) into .pex files", CkHowTo));
        items.Add(FileItem("Archive2", Tool(System.IO.Path.Combine("Tools", "Archive2", "Archive2.exe")), game != null,
            "packs the compiled scripts into your mod's BA2 archive", CkHowTo));
        items.Add(FileItem("Script sources (ContentResources.zip)", Tool(System.IO.Path.Combine("Tools", "ContentResources.zip")), game != null,
            "holds the game's own script sources and the compiler's flags file, which every script compile needs",
            CkHowTo + " The zip comes with the Creation Kit; don't unpack it by hand, sf-mod-build unpacks what it needs."));

        items.Add(DotnetItem(env));
        items.Add(CheckerItem(env, checkerGiven));
        foreach (var i in items) Log.Write($"{(i.Found ? "found" : "missing")}: {i.Name} {i.Path ?? ""} {i.Detail ?? ""}");
        return new DoctorReport(game, others, items);
    }

    static DoctorItem FileItem(string name, string path, bool haveGame, string forWhat, string how) =>
        new(name, haveGame && File.Exists(path), true, haveGame ? path : null, forWhat,
            haveGame ? how : "Find the game first (see Starfield above); this comes with the Creation Kit in the game's Tools folder.");

    static DoctorItem DotnetItem(IHostEnv env)
    {
        const string forWhat = "runs your mod's Mutagen builder (the program that writes the plugin)";
        const string how = "Install the .NET 9 SDK (or later) from https://dotnet.microsoft.com/download/dotnet/9.0 and open a new terminal.";
        var exe = Common.FindDotnet(env);
        if (exe == null) return new DoctorItem(".NET SDK", false, true, null, forWhat, how);
        var output = env.Run(exe, "--list-sdks", out var why);
        if (output == null)
            return new DoctorItem(".NET SDK", false, true, exe, forWhat, $"{exe} --list-sdks didn't work ({why}). {how}");
        var versions = SdkVersions(output);
        Log.Write($"dotnet --list-sdks: {string.Join(", ", versions)}");
        var usable = versions.Where(v => v.Major >= 9).ToList();
        if (usable.Count == 0)
            return new DoctorItem(".NET SDK", false, true, exe, forWhat,
                (versions.Count == 0 ? "dotnet is installed but has no SDK (only the runtime). " : $"only older SDKs are installed ({string.Join(", ", versions)}). ") + how);
        return new DoctorItem(".NET SDK", true, true, exe, forWhat, null, "SDK " + string.Join(", ", usable));
    }

    /// <summary>The SDK versions in "dotnet --list-sdks" output ("9.0.321 [C:\Program Files\dotnet\sdk]" per line).</summary>
    public static List<Version> SdkVersions(string listSdksOutput)
    {
        var list = new List<Version>();
        foreach (var line in listSdksOutput.Split('\n'))
        {
            var m = Regex.Match(line.Trim(), @"^(\d+)\.(\d+)\.(\d+)");
            if (m.Success) list.Add(new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value)));
        }
        return list;
    }

    public const string CheckerHowTo = "Get sf-mod-check (called sfmodcheck before its rename) from https://github.com/taosecurity/sf-mod-check/releases. " +
                                       "Put it on PATH, or give it with --checker <exe>.";

    static DoctorItem CheckerItem(IHostEnv env, string? given)
    {
        const string forWhat = "checks the finished zip (release check); without it the build still works, unchecked";
        const string how = CheckerHowTo;
        if (given != null) return new DoctorItem("Checker", true, false, given, forWhat, null, "given with --checker");
        var found = PathSearch.Find(env, "sf-mod-check", "sfmodcheck");
        return found != null
            ? new DoctorItem("Checker", true, false, found, forWhat, null, "on PATH")
            : new DoctorItem("Checker", false, false, null, forWhat, how);
    }

    public static void Print(DoctorReport r, TextWriter o)
    {
        foreach (var i in r.Items)
        {
            var label = i.Found ? "FOUND  " : i.Required ? "MISSING" : "WARNING";
            o.WriteLine($"{label}  {i.Name}: {(i.Found ? i.Path : i.Path != null ? $"not at {i.Path}" : "not found")}{(i.Found && i.Detail != null ? $" ({i.Detail})" : "")}");
            if (!i.Found)
            {
                o.WriteLine($"         what it is for: {i.For}");
                if (i.HowToGet != null) o.WriteLine($"         how to get it: {i.HowToGet}");
            }
        }
        foreach (var other in r.OtherInstalls)
            o.WriteLine($"note: another Starfield install was found ({other.How}): {other.Folder}. To use it instead, give --game \"{other.Folder}\"");
        if (r.MissingRequired > 0)
            o.WriteLine($"RESULT: MISSING {r.MissingRequired} item(s) a build needs; see the MISSING lines above for how to get them");
        else if (r.MissingOptional > 0)
            o.WriteLine("RESULT: OK, ready to build (with a WARNING: see above)");
        else
            o.WriteLine("RESULT: OK, ready to build");
    }
}
