// The setup checks doctor runs beyond the tools a build needs: the settings a modder wants while testing and working in
// the Creation Kit. None of them stops a build, so each is OK or a WARNING with the exact lines to add:
//   StarfieldCustom.ini (My Games\Starfield): [Papyrus] bEnableLogging=1, bEnableTrace=1, bLoadDebugInformation=1
//     (the Papyrus log, with Debug.Trace lines and line numbers) and [Archive] bInvalidateOlderFiles=1 (loose files
//     win over archives while testing).
//   CreationKitCustom.ini (the game folder): [Materials] bUseCompiledDB=0, [Papyrus]
//     sScriptSourceFolder="Data/Scripts/Source", [Archive] bInvalidateOlderFiles=1 (the settings Bethesda's setup page
//     asks for).
//   The script sources unpacked into Data\Scripts\Source for the Creation Kit (sf-mod-build itself unpacks its own
//   copy and doesn't need them).
// It only reads files; it never writes an INI file or anything in the game folder.
namespace SfModBuild;

/// <summary>One setup check: OK or a WARNING, what it is for, and the fix (lines to add, or what to do).</summary>
internal sealed record SetupCheck(string Name, bool Ok, string? Path, string Detail, string For, IReadOnlyList<string> Fix);

internal static class SetupChecks
{
    /// <summary>Starfield's Steam app id, the name of its Proton prefix folder on Linux.</summary>
    const string SteamAppId = "1716740";

    /// <summary>One INI setting to look for: section, key, the value wanted and the line to add.</summary>
    sealed record Want(string Section, string Key, Func<string, bool> Good, string Line);

    static bool IsOne(string v) => v.Split(new[] { ' ', '\t', ';', '#' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() == "1";
    static bool IsZero(string v) => v.Split(new[] { ' ', '\t', ';', '#' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() == "0";

    /// <summary>The value without a trailing ; or # comment (one inside double quotes is part of the value).</summary>
    public static string WithoutComment(string v)
    {
        var quoted = false;
        for (var i = 0; i < v.Length; i++)
        {
            if (v[i] == '"') quoted = !quoted;
            else if (!quoted && v[i] is ';' or '#') return v[..i].TrimEnd();
        }
        return v.Trim();
    }

    static bool IsScriptSourceFolder(string v) =>
        string.Equals(WithoutComment(v).Trim().Trim('"').Replace('\\', '/').TrimEnd('/'), "Data/Scripts/Source", StringComparison.OrdinalIgnoreCase);

    // Loose files: bInvalidateOlderFiles=1 under [Archive] is the one line Bethesda's pages ask for, in both
    // StarfieldCustom.ini and CreationKitCustom.ini ("Creation Kit Setup and Installation", "Initialization (.ini)
    // Settings" and "Creating Overlays and World Spaces" on the official Creation Kit wiki, checked 2026-11-20).
    // sResourceDataDirsFinal is not asked for: the official INI page says to avoid it and that it only works when
    // bInvalidateOlderFiles is off (and the older and current wiki texts disagree on what an empty value does).
    static readonly Want[] GameWants =
    {
        new("Papyrus", "bEnableLogging", IsOne, "bEnableLogging=1"),
        new("Papyrus", "bEnableTrace", IsOne, "bEnableTrace=1"),
        new("Papyrus", "bLoadDebugInformation", IsOne, "bLoadDebugInformation=1"),
        new("Archive", "bInvalidateOlderFiles", IsOne, "bInvalidateOlderFiles=1"),
    };

    static readonly Want[] CkWants =
    {
        new("Materials", "bUseCompiledDB", IsZero, "bUseCompiledDB=0"),
        new("Papyrus", "sScriptSourceFolder", IsScriptSourceFolder, "sScriptSourceFolder=\"Data/Scripts/Source\""),
        new("Archive", "bInvalidateOlderFiles", IsOne, "bInvalidateOlderFiles=1"),
    };

    /// <summary>The folder that holds StarfieldCustom.ini: --my-games when given, else Documents\My Games\Starfield
    /// on Windows, or the game's Proton prefix on Linux. Null when it can't be told.</summary>
    public static string? MyGamesFolder(string? option, GameInstall? game, IHostEnv env)
    {
        if (option != null) return System.IO.Path.GetFullPath(option.Trim().Trim('"'));
        if (env.IsWindows)
            return env.DocumentsFolder() is { } docs ? System.IO.Path.Combine(docs, "My Games", "Starfield") : null;
        // Linux (Steam with Proton): <library>/steamapps/common/Starfield -> <library>/steamapps/compatdata/1716740/...
        if (game == null) return null;
        var common = System.IO.Path.GetDirectoryName(game.Folder);
        var steamapps = common == null ? null : System.IO.Path.GetDirectoryName(common);
        return steamapps == null ? null : System.IO.Path.Combine(steamapps, "compatdata", SteamAppId, "pfx", "drive_c", "users", "steamuser", "Documents", "My Games", "Starfield");
    }

    public static List<SetupCheck> Run(GameInstall? game, string? myGamesOption, IHostEnv env)
    {
        var list = new List<SetupCheck>();
        var myGames = MyGamesFolder(myGamesOption, game, env);
        Log.Write($"My Games folder: {myGames ?? "(not known)"}");
        list.Add(myGames == null
            ? new SetupCheck("StarfieldCustom.ini (Papyrus log, loose files)", false, null,
                env.IsWindows ? "not checked: the Documents folder isn't known"
                    : "not checked: Starfield wasn't found, so its Proton folder (which holds My Games/Starfield) isn't known", GameFor,
                env.IsWindows ? new[] { "give the folder that holds StarfieldCustom.ini with --my-games <folder> (Documents\\My Games\\Starfield)" }
                    : new[] { "give the game's folder with --game <folder> (its Proton folder is found from it), or the folder that holds StarfieldCustom.ini with --my-games <folder>" })
            : IniCheck("StarfieldCustom.ini (Papyrus log, loose files)", System.IO.Path.Combine(myGames, "StarfieldCustom.ini"), GameWants, GameFor,
                "start the game once so it makes the folder, then make the file"));
        if (game != null)
        {
            list.Add(IniCheck("CreationKitCustom.ini (Creation Kit settings)", System.IO.Path.Combine(game.Folder, "CreationKitCustom.ini"), CkWants,
                "the Creation Kit's own settings: the Material Editor works from the unpacked materials, scripts are found in Data\\Scripts\\Source, and loose files win while you test",
                null));
            list.Add(SourcesCheck(game));
        }
        foreach (var c in list) Log.Write($"setup {(c.Ok ? "ok" : "warning")}: {c.Name} {c.Path ?? ""}: {c.Detail}");
        return list;
    }

    const string GameFor = "turns on the Papyrus log with your Debug.Trace lines and line numbers, and lets loose files win over archives while you test";

    static SetupCheck IniCheck(string name, string file, Want[] wants, string forWhat, string? noFolderFix)
    {
        var folder = System.IO.Path.GetDirectoryName(file)!;
        string[] AllLines(IEnumerable<Want> ws) => ws.GroupBy(w => w.Section).SelectMany(g => new[] { $"[{g.Key}]" }.Concat(g.Select(w => w.Line))).ToArray();
        if (!File.Exists(file))
        {
            if (File.Exists(file + ".txt"))
                return new SetupCheck(name, false, file + ".txt", "the file is named .ini.txt, so the program never reads it", forWhat,
                    new[] { $"rename it to {System.IO.Path.GetFileName(file)} (in File Explorer, turn on View > Show > File name extensions first)" });
            if (!Directory.Exists(folder) && noFolderFix != null)
                return new SetupCheck(name, false, file, $"the folder {folder} isn't there", forWhat,
                    new[] { $"{noFolderFix} with these lines:" }.Concat(AllLines(wants)).ToArray());
            return new SetupCheck(name, false, file, "the file isn't there (or this account can't see it)", forWhat,
                new[] { $"make {file} with these lines:" }.Concat(AllLines(wants)).ToArray());
        }
        Dictionary<(string, string), string> values;
        try { values = ReadIni(File.ReadAllLines(file)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new SetupCheck(name, false, file, $"it can't be read ({e.Message})", forWhat, new[] { "check that this account can read the file" });
        }
        var missing = wants.Where(w => !(values.TryGetValue((w.Section.ToLowerInvariant(), w.Key.ToLowerInvariant()), out var v) && w.Good(v))).ToList();
        if (missing.Count == 0)
            return new SetupCheck(name, true, file, "has " + string.Join(", ", wants.Select(w => w.Line)), forWhat, Array.Empty<string>());
        return new SetupCheck(name, false, file, "doesn't set " + string.Join(", ", missing.Select(w => w.Line)), forWhat,
            new[] { "add these lines (each under its [section]), then restart the program:" }.Concat(AllLines(missing)).ToArray());
    }

    /// <summary>An INI file's values by (section, key), both lower case. Later lines win; ; and # start comments.</summary>
    public static Dictionary<(string, string), string> ReadIni(IEnumerable<string> lines)
    {
        var values = new Dictionary<(string, string), string>();
        var section = "";
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] is ';' or '#') continue;
            if (line[0] == '[')
            {
                var end = line.IndexOf(']');
                section = (end > 0 ? line[1..end] : line[1..]).Trim().ToLowerInvariant();
                continue;
            }
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            values[(section, line[..eq].Trim().ToLowerInvariant())] = line[(eq + 1)..].Trim();
        }
        return values;
    }

    static SetupCheck SourcesCheck(GameInstall game)
    {
        const string name = "Script sources unpacked (for the Creation Kit)";
        const string forWhat = "the Creation Kit shows and compiles scripts only from Data\\Scripts\\Source; sf-mod-build doesn't need it (it unpacks its own copy)";
        var data = System.IO.Path.Combine(game.Folder, "Data");
        var flags = System.IO.Path.Combine(data, "Scripts", "Source", Compile.FlagsFile);
        if (File.Exists(flags)) return new SetupCheck(name, true, System.IO.Path.Combine(data, "Scripts", "Source"), "the flags file is there", forWhat, Array.Empty<string>());
        var nested = System.IO.Path.Combine(data, "ContentResources", "Scripts", "Source", Compile.FlagsFile);
        if (File.Exists(nested))
            return new SetupCheck(name, false, System.IO.Path.Combine(data, "ContentResources"), "the zip was unpacked one folder too deep", forWhat,
                new[] { $"move the Scripts, Materials and Particles folders from {System.IO.Path.Combine(data, "ContentResources")} up into {data}" });
        return new SetupCheck(name, false, flags, "not there (or this account can't see it)", forWhat,
            new[] { $"unzip {System.IO.Path.Combine(game.Folder, "Tools", "ContentResources.zip")} into {data}, keeping its folders (Scripts\\Source, Materials, Particles)" });
    }
}
