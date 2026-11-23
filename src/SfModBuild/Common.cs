// Pieces the build commands share: loading the project file with its problems shown, finding the game, emptying an
// output folder safely, quoting a command-line argument, finding dotnet, and the last lines of a program's output.
namespace SfModBuild;

internal static class Common
{
    /// <summary>Loads the sfmod.json named by the first positional argument (default: the one in the current folder),
    /// printing its problems (warnings only when it can't be loaded, since validate shows them). Null when it can't
    /// be used; the RESULT line has been printed then.</summary>
    public static SfModProject? LoadProject(ParsedArgs p, TextWriter stdout, string cannot)
    {
        var path = p.Positionals.Count > 0 ? p.Positionals[0] : Path.Combine(Environment.CurrentDirectory, SfModProject.FileName);
        var problems = new List<ProjectProblem>();
        var project = SfModProject.Load(path, problems);
        var shown = Path.GetFullPath(path);
        if (Directory.Exists(shown)) shown = Path.Combine(shown, SfModProject.FileName);
        foreach (var x in problems.Where(x => !x.IsWarning || project == null))
        {
            stdout.WriteLine(x.Format(shown));
            Log.Write(x.Format(shown));
        }
        if (project == null)
            stdout.WriteLine($"RESULT: {cannot} ({problems.Count(x => !x.IsWarning)} PROBLEM(S) in {shown}; run sf-mod-build validate for details)");
        return project;
    }

    /// <summary>The game named by --game, else the first one found (Steam before Game Pass).</summary>
    public static GameInstall? FindGame(string? option, IHostEnv env, out string? error)
    {
        if (option != null) return GameLocator.FromOption(option, out error);
        error = null;
        var game = GameLocator.FindAll(env).FirstOrDefault();
        if (game == null) error = "Starfield wasn't found. Give the folder that holds Starfield.exe with --game <folder>, or run sf-mod-build doctor.";
        else Log.Write($"game: {game.Folder} ({game.How})");
        return game;
    }

    /// <summary>The game folders no emptied folder may be, hold or be inside: the game in use plus every install
    /// found (a mod can sit in a second install, for example Game Pass while --game names Steam). With no game in
    /// use (null), just the installs found.</summary>
    public static List<string> GameFolders(string? gameFolder, IHostEnv env)
    {
        var folders = new List<string>();
        if (gameFolder != null) folders.Add(gameFolder);
        foreach (var g in GameLocator.FindAll(env))
            if (!folders.Any(f => Same(Full(f), Full(g.Folder)))) folders.Add(g.Folder);
        Log.Write($"game folders protected: {string.Join(", ", folders)}");
        return folders;
    }

    static string Full(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    // Paths compare without case everywhere: the cautious choice for "never empty this" checks, since macOS, WSL's
    // /mnt/c and mounted NTFS drives are case-insensitive too. (The loader already refuses, case-sensitively off
    // Windows, an output folder outside the mod's folder.)
    const StringComparison PathCase = StringComparison.OrdinalIgnoreCase;
    static bool Same(string a, string b) => string.Equals(a, b, PathCase);

    // a drive root keeps its separator ("C:\"), so don't add a second one
    static bool Under(string inner, string outer) =>
        inner.StartsWith(Path.EndsInDirectorySeparator(outer) ? outer : outer + Path.DirectorySeparatorChar, PathCase)
        && !Same(inner, outer);

    /// <summary>Why emptying <paramref name="folder"/> would be unsafe, or null when it is safe. Unsafe: a drive root,
    /// a folder that is or holds one of <paramref name="keep"/>, or a folder that is, holds or is inside one of
    /// <paramref name="never"/> (the game's folder: emptying any part of it would delete game files).</summary>
    public static string? UnsafeToEmpty(string what, string folder, string modFolder, IEnumerable<string> keep, IEnumerable<string>? never = null)
    {
        var f = Full(folder);
        if (Path.GetPathRoot(f) is { } root && Same(Path.TrimEndingDirectorySeparator(root), f))
            return $"the {what} folder {f} is a drive root";
        // Only folders inside the mod's own folder are ever emptied, so a project file from someone else can't point
        // an output folder at Documents or the game and have it deleted.
        if (!Under(f, Full(modFolder)))
            return $"the {what} folder {f} isn't inside the mod's folder {Full(modFolder)}";
        // A link or junction on the way down (shipped inside a downloaded mod) could lead the folder out of the mod.
        if (LinkOnTheWay(Full(modFolder), f) is { } link)
            return $"the {what} folder {f} {link}";
        foreach (var k in keep.Select(Full))
            if (Same(k, f) || Under(k, f))
                return $"the {what} folder {f} is or holds {k}, which emptying it would delete";
        foreach (var n in (never ?? Array.Empty<string>()).Select(Full))
            if (Same(n, f) || Under(n, f) || Under(f, n))
                return $"the {what} folder {f} is inside or holds the game's folder {n}, and emptying it would delete game files";
        return null;
    }

    /// <summary>Why the way from the mod's folder (not included) down to the target's parent can't be trusted, or
    /// null: an existing folder on it is a symlink or junction (it has a LinkTarget), or couldn't be read. Other
    /// reparse points (OneDrive placeholders and the like) don't lead elsewhere and are allowed. The target itself
    /// may be a link: emptying it removes only the link, never what it points to.</summary>
    static string? LinkOnTheWay(string modFolder, string target)
    {
        var relative = Path.GetRelativePath(modFolder, target);
        // UnsafeToEmpty calls this only after Under (without case) found the target inside the mod's folder, so a
        // ".." or rooted relative path here can only mean a letter-case difference on a case-sensitive system
        if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative))
            return $"is spelled differently from the mod's folder {modFolder} (letter case), so its path can't be checked; spell it the same way";
        var parts = relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        var at = modFolder;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            at = Path.Combine(at, parts[i]);
            try
            {
                var d = new DirectoryInfo(at);
                if (!d.Exists) return null; // nothing further down exists yet
                // only symlinks and junctions (they have a target); other reparse points such as OneDrive's cloud
                // placeholders are ordinary folders
                if (d.LinkTarget != null) return $"goes through {at}, which is a link or junction to {d.LinkTarget}";
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return $"goes through {at}, which couldn't be checked ({e.Message})";
            }
        }
        return null;
    }

    /// <summary>True for a dist file that belongs in the zip and the Creations set: not a zip, Papyrus source, a
    /// half-written zip or the open log file.</summary>
    public static bool Shippable(string file)
    {
        var name = Path.GetFileName(file);
        if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".psc", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(Zip.PartialSuffix, StringComparison.OrdinalIgnoreCase))
            return false;
        return Log.FilePath is not { } log || !Same(Full(log), Full(file));
    }

    /// <summary>True for a .ba2 in dist whose name isn't one of the archives this build makes (names match without
    /// case): an old archive, or one the builder or someone else put there. Zip and pack refuse it, so a build never
    /// passes while silently leaving out, or shipping, an archive it didn't make.</summary>
    public static bool StrayArchive(SfModProject project, string file) =>
        file.EndsWith(".ba2", StringComparison.OrdinalIgnoreCase)
        && !project.ArchiveNames.Concat(project.ZipArchives).Contains(Path.GetFileName(file), StringComparer.OrdinalIgnoreCase);

    /// <summary>The fix for an archive the build doesn't make, in dist or in zip.files.</summary>
    public const string StrayArchiveFix = "delete it from dist, or, if your builder writes it on purpose, list its name in zip.archives in sfmod.json; sf-mod-build ships only the archives it builds and the ones listed there";

    /// <summary>Why a stray archive stops the zip or the Creations set, with the fix.</summary>
    public static string StrayArchiveError(SfModProject project, string file, string what) =>
        StrayArchiveText(project, file, $"{what} can't take it");

    /// <summary>pack's warning (without "xbox") about a stray archive that zip will stop on, with the fix.</summary>
    public static string StrayArchiveWarning(SfModProject project, string file) =>
        StrayArchiveText(project, file, "zip will stop on it");

    static string StrayArchiveText(SfModProject project, string file, string so)
    {
        var all = project.ArchiveNames.Concat(project.ZipArchives).ToList();
        var made = all.Any() ? "the mod's archives are " + string.Join(" and ", all.Select(n => $"\"{n}\"")) : "the mod has no archives";
        return $"{file} is an archive in dist that this build doesn't make ({made}), so {so}. Fix: {StrayArchiveFix}";
    }

    /// <summary>Every file in the folder with this name, matched without case (a case-sensitive disk can hold
    /// "mymod - main.ba2" beside "MyMod - Main.ba2"; the game treats them as one). Empty when the folder isn't there.</summary>
    public static List<string> FilesNamed(string folder, string name) =>
        Directory.Exists(folder)
            ? Directory.GetFiles(folder).Where(f => Same(Path.GetFileName(f), name)).Order(StringComparer.Ordinal).ToList()
            : new List<string>();

    /// <summary>Deletes the folder (when there) and makes it again, empty. Null when done, else why it failed.</summary>
    public static string? Empty(string folder)
    {
        if (Log.FilePath is { } log && Under(Full(log), Full(folder)))
            return $"the log file {log} is inside {folder}, which is emptied on every build. Fix: give --log a file outside the mod's output folders";
        try
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
            Directory.CreateDirectory(folder);
            Log.Write($"emptied: {folder}");
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return $"{folder} can't be emptied: {e.Message} Fix: close any program using files there and run again";
        }
    }

    /// <summary>One argument for a Windows-style command line (the rules .NET and the C runtime use to split it):
    /// left bare when it has no space, tab or quote, else quoted, with backslashes before a quote (or before the
    /// closing quote) doubled and quotes escaped.</summary>
    public static string Quote(string arg)
    {
        if (arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '\t', '\n', '\v', '"' }) < 0) return arg;
        var sb = new System.Text.StringBuilder("\"");
        var backslashes = 0;
        foreach (var c in arg)
        {
            if (c == '\\') { backslashes++; continue; }
            if (c == '"')
            {
                sb.Append('\\', backslashes * 2 + 1).Append('"');
                backslashes = 0;
                continue;
            }
            sb.Append('\\', backslashes).Append(c);
            backslashes = 0;
        }
        sb.Append('\\', backslashes * 2).Append('"');
        return sb.ToString();
    }

    /// <summary>dotnet on PATH, else in DOTNET_ROOT; null when neither has it.</summary>
    public static string? FindDotnet(IHostEnv env)
    {
        var exe = PathSearch.Find(env, "dotnet");
        if (exe == null && env.EnvVar("DOTNET_ROOT") is { Length: > 0 } root)
        {
            var candidate = Path.Combine(root, env.IsWindows ? "dotnet.exe" : "dotnet");
            if (File.Exists(candidate)) exe = candidate;
        }
        return exe;
    }

    public static IEnumerable<string> LastLines(string text, int count) =>
        text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Trim().Length > 0).TakeLast(count);

    /// <summary>Prints a failed program's last output lines, indented.</summary>
    public static void ShowLastLines(TextWriter stdout, string output, int count = 15)
    {
        foreach (var line in LastLines(output, count)) stdout.WriteLine($"         {line}");
    }
}
