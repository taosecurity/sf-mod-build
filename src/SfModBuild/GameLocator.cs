// Finding Starfield: a folder given with --game, Steam (its library folders, from libraryfolders.vdf), or Game Pass
// (the XboxGames\Starfield\Content folder on each drive). Steam comes first when both are installed, because the
// Creation Kit (and with it the Papyrus compiler, Archive2 and the script sources) installs into the Steam copy.
namespace SfModBuild;

internal sealed record GameInstall(string Folder, string Kind, string How);

internal static class GameLocator
{
    /// <summary>True when the folder looks like a Starfield install (Starfield.exe, or Data\Starfield.esm).</summary>
    public static bool IsGameFolder(string folder) =>
        File.Exists(Path.Combine(folder, "Starfield.exe")) || File.Exists(Path.Combine(folder, "Data", "Starfield.esm"));

    /// <summary>Checks a folder given with --game. A Data folder given by mistake is taken as its parent. Returns null
    /// and an error naming the folder and the fix when it isn't a game folder.</summary>
    public static GameInstall? FromOption(string given, out string? error)
    {
        error = null;
        string full;
        try { full = Path.GetFullPath(given.Trim().Trim('"')); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = $"--game \"{given}\" is not a usable folder path ({e.Message}). Give the folder that holds Starfield.exe.";
            return null;
        }
        full = Path.TrimEndingDirectorySeparator(full);
        if (!Directory.Exists(full))
        {
            error = $"--game {full}: that folder doesn't exist. Give the folder that holds Starfield.exe, for example --game {Args.Example("--game")}";
            return null;
        }
        if (string.Equals(Path.GetFileName(full), "Data", StringComparison.OrdinalIgnoreCase) && Path.GetDirectoryName(full) is { } parent && IsGameFolder(parent))
        {
            Log.Write($"--game {full} is the Data folder; using its parent {parent}");
            full = parent;
        }
        if (!IsGameFolder(full))
        {
            error = $"--game {full}: no Starfield.exe or Data\\Starfield.esm there. Give the folder that holds Starfield.exe (not the Tools or Data folder).";
            return null;
        }
        return new GameInstall(full, Kind(full), "given with --game");
    }

    static string Kind(string folder) =>
        folder.Contains("XboxGames", StringComparison.OrdinalIgnoreCase) || folder.Contains("ModifiableWindowsApps", StringComparison.OrdinalIgnoreCase)
            ? "Game Pass" : folder.Contains("steamapps", StringComparison.OrdinalIgnoreCase) ? "Steam" : "other";

    /// <summary>Every Starfield install found, Steam ones first.</summary>
    public static List<GameInstall> FindAll(IHostEnv env)
    {
        var found = new List<GameInstall>();
        void Add(string folder, string kind, string how)
        {
            string full;
            try { full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)); }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return; }
            var ok = IsGameFolder(full);
            Log.Write($"  {(ok ? "found" : "no game in")} {full}");
            if (ok && !found.Any(f => string.Equals(f.Folder, full, StringComparison.OrdinalIgnoreCase))) found.Add(new GameInstall(full, kind, how));
        }

        Log.Write("looking for Steam");
        foreach (var steam in SteamRoots(env))
        {
            var libraries = new List<string> { steam };
            var vdfUsed = false;
            foreach (var vdf in new[] { Path.Combine(steam, "steamapps", "libraryfolders.vdf"), Path.Combine(steam, "config", "libraryfolders.vdf") })
            {
                if (!File.Exists(vdf)) { Log.Write($"  {vdf}: not there, or this account can't see it"); continue; }
                try
                {
                    var libs = Vdf.LibraryFolders(File.ReadAllText(vdf));
                    Log.Write($"  {vdf}: {libs.Count} library folders");
                    libraries.AddRange(libs);
                    vdfUsed = true;
                    break;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException)
                {
                    // Steam's steamapps folder can be unreadable to a non-admin account; the default library is still tried.
                    Log.Write($"  {vdf}: can't read it ({e.GetType().Name}: {e.Message}); trying the Steam folder itself");
                }
            }
            foreach (var lib in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
                Add(Path.Combine(lib, "steamapps", "common", "Starfield"), "Steam",
                    vdfUsed && !string.Equals(lib, steam, StringComparison.OrdinalIgnoreCase) ? "Steam library (libraryfolders.vdf)" : "Steam folder");
        }

        Log.Write("looking for Game Pass");
        foreach (var root in env.DriveRoots())
        {
            Add(Path.Combine(root, "XboxGames", "Starfield", "Content"), "Game Pass", "Game Pass (XboxGames folder)");
            Add(Path.Combine(root, "Program Files", "ModifiableWindowsApps", "Starfield"), "Game Pass", "Game Pass (ModifiableWindowsApps)");
        }
        return found;
    }

    /// <summary>Steam's own folder: the registry, then the usual install paths.</summary>
    public static List<string> SteamRoots(IHostEnv env)
    {
        var roots = new List<string>();
        void Add(string? p, string from)
        {
            if (string.IsNullOrWhiteSpace(p)) return;
            p = p.Replace('/', Path.DirectorySeparatorChar);
            try { p = Path.TrimEndingDirectorySeparator(Path.GetFullPath(p)); }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return; }
            if (!Directory.Exists(p) || roots.Contains(p, StringComparer.OrdinalIgnoreCase)) return;
            Log.Write($"  Steam folder {p} ({from})");
            roots.Add(p);
        }
        if (env.IsWindows)
        {
            Add(env.RegistryString(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath"), "registry, current user");
            Add(env.RegistryString(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"), "registry");
            Add(env.RegistryString(@"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam", "InstallPath"), "registry");
            if (env.EnvVar("ProgramFiles(x86)") is { Length: > 0 } pf86) Add(Path.Combine(pf86, "Steam"), "usual place");
            if (env.EnvVar("ProgramFiles") is { Length: > 0 } pf) Add(Path.Combine(pf, "Steam"), "usual place");
        }
        else if (env.EnvVar("HOME") is { Length: > 0 } home)
        {
            Add(Path.Combine(home, ".steam", "steam"), "usual place");
            Add(Path.Combine(home, ".local", "share", "Steam"), "usual place");
        }
        return roots;
    }
}
