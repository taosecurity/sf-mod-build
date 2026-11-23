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
            || name.EndsWith(Zip.PartialSuffix, StringComparison.OrdinalIgnoreCase) || Same(name, MadeList))
            return false;
        return Log.FilePath is not { } log || !Same(Full(log), Full(file));
    }

    /// <summary>True for a .ba2 in dist whose name isn't one of the archives this build makes or zip.archives lists
    /// (names match without case): an old archive, or one the builder or someone else put there. Zip and pack refuse it, so a build never
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
        var pack = project.ArchiveNames.ToList();
        static string Names(IEnumerable<string> names) => string.Join(" and ", names.Select(n => $"\"{n}\""));
        // pack's archives and the builder's listed ones are named apart, so a listed archive isn't taken for one pack makes
        var made = project.ZipArchives.Count == 0
            ? (pack.Any() ? "the mod's archives are " + Names(pack) : "the mod has no archives")
            : (pack.Any() ? "pack makes " + Names(pack) : "pack makes no archives") + ", and zip.archives lists " + Names(project.ZipArchives);
        return $"{file} is an archive in dist that this build doesn't make ({made}), so {so}. Fix: {StrayArchiveFix}";
    }

    /// <summary>Every file in the folder with this name, matched without case (a case-sensitive disk can hold
    /// "mymod - main.ba2" beside "MyMod - Main.ba2"; the game treats them as one). Empty when the folder isn't there.</summary>
    public static List<string> FilesNamed(string folder, string name) =>
        Directory.Exists(folder)
            ? Directory.GetFiles(folder).Where(f => Same(Path.GetFileName(f), name)).Order(StringComparer.Ordinal).ToList()
            : new List<string>();

    /// <summary>The list in dist (and in creations-out, with xbox) of what sf-mod-build and the mod's own builder and
    /// steps put there (one name per line), so the next build deletes those and nothing else. It never goes in the
    /// zip; in creations-out it stays behind when the user uploads the plugin and archives.</summary>
    public const string MadeList = ".sf-mod-build-made";

    /// <summary>The plugin and archive names sfmod.json gives the mod (matched without case): the plugin and each
    /// part, every archive pack makes or made for them (Main and Textures, PC and Xbox, each part's, the archives
    /// list) and the names zip.archives lists. Only these, the made list's names and half-written zips are deleted
    /// from dist and creations-out; any other plugin or archive there is the user's.</summary>
    public static HashSet<string> OwnNames(SfModProject project)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { project.Plugin, project.Ba2Name, project.TexturesBa2Name, project.XboxBa2Name, project.XboxTexturesBa2Name };
        foreach (var part in project.Parts) { names.Add(part.Plugin); names.Add(part.Ba2Name); }
        names.UnionWith(project.Archives.Select(a => a.Name));
        names.UnionWith(project.ZipArchives);
        return names;
    }

    /// <summary>The top-level names in dist, or null when it isn't there, is a link or can't be read.</summary>
    public static HashSet<string>? DistEntries(string dist)
    {
        try
        {
            var d = new DirectoryInfo(Path.TrimEndingDirectorySeparator(dist));
            if (!d.Exists || d.LinkTarget != null) return null;
            return d.EnumerateFileSystemInfos("*", new EnumerationOptions { AttributesToSkip = 0 }).Select(e => e.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    static bool PlainName(string name) =>
        name.Length > 0 && name != "." && name != ".." && name.IndexOfAny(new[] { '/', '\\', ':' }) < 0 && name.Trim() == name;

    static HashSet<string> ReadMadeList(string dist)
    {
        var file = Path.Combine(dist, MadeList);
        if (!File.Exists(file)) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // only plain names count, so a hand-edited list can't reach outside dist
        return File.ReadAllLines(file).Where(PlainName).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Writes the names (never zips or the list itself) into dist's made list; with add, the names already
    /// listed stay. Does nothing when dist isn't there or is a link. A failure is only logged: it means the next
    /// build names those files and stops, never that it deletes something.</summary>
    public static void RecordMade(string dist, IEnumerable<string> names, bool add)
    {
        try
        {
            var mine = names.Where(n => PlainName(n) && !Same(n, MadeList) && !n.EndsWith(".zip", PathCase)).ToList();
            if ((add && mine.Count == 0) || DistEntries(dist) == null) return;
            var list = add ? ReadMadeList(dist) : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            list.UnionWith(mine);
            File.WriteAllLines(Path.Combine(dist, MadeList), list.Order(StringComparer.OrdinalIgnoreCase));
            Log.Write($"made list in {dist}: {string.Join(", ", list.Order(StringComparer.OrdinalIgnoreCase))}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"the made list in {dist} couldn't be written: {e.Message}");
        }
    }

    /// <summary>Runs a step that writes into dist, then adds whatever new it left there to the made list.</summary>
    public static int TrackDist(string? dist, Func<int> run)
    {
        var before = dist == null ? null : DistEntries(dist);
        var code = run();
        if (dist != null && DistEntries(dist) is { } after)
            RecordMade(dist, after.Where(n => before == null || !before.Contains(n)), add: true);
        return code;
    }

    /// <summary>True when the path is zip.folder or a folder holding it.</summary>
    public static bool HoldsZipFolder(SfModProject project, string path) =>
        Same(Full(project.ZipFolder), Full(path)) || Under(Full(project.ZipFolder), Full(path));

    /// <summary>Gets dist ready for a new plugin, deleting only what earlier builds made there: the names in the made
    /// list, the mod's own plugins and archives (<see cref="OwnNames"/>), and half-written zips. Zips stay. When dist holds anything else (a folder or
    /// file the user keeps there), nothing is deleted and the reason names each one. Null when done, else why it
    /// can't (with the fix).</summary>
    public static string? CleanDist(SfModProject project)
    {
        var folder = project.Dist;
        if (Log.FilePath is { } log && Under(Full(log), Full(folder)))
            return $"the log file {log} is inside {folder}, where the plugin's files go. Fix: give --log a file outside the mod's output folders";
        try
        {
            var d = new DirectoryInfo(Path.TrimEndingDirectorySeparator(folder));
            if (!d.Exists)
            {
                Directory.CreateDirectory(folder);
                return null;
            }
            // deleting files through a link would delete them wherever it points (a game's Data folder included)
            if (d.LinkTarget is { } to)
                return $"the dist folder {folder} is a link or junction to {to}, so deleting the earlier build's files there would delete them wherever it points. Fix: in {project.File}, set \"dist\" to a real folder of its own, for example \"dist\"";
            var made = ReadMadeList(folder);
            var own = OwnNames(project);
            var delete = new List<FileSystemInfo>();
            var foreign = new List<string>();
            foreach (var e in d.EnumerateFileSystemInfos("*", new EnumerationOptions { AttributesToSkip = 0 }).OrderBy(e => e.Name, StringComparer.Ordinal))
            {
                var n = e.Name;
                var file = e is FileInfo;
                if (file && n.EndsWith(".zip", PathCase)) continue;
                // zip.folder inside dist ("dist/release"): the zips stay, so the folder holding them stays too
                if (!file && HoldsZipFolder(project, e.FullName)) continue;
                // a plugin or archive counts as the build's only by a name sfmod.json gives it (0.4.0 deleted every
                // one, a backup plugin kept in dist included)
                if (Same(n, MadeList) || made.Contains(n)
                    || (file && (own.Contains(n) || n.EndsWith(Zip.PartialSuffix, PathCase))))
                    delete.Add(e);
                else foreign.Add(file ? $"\"{n}\"" : $"\"{n}\" (a folder)");
            }
            if (foreign.Count > 0)
                return $"the dist folder {folder} holds {string.Join(", ", foreign)}, which no earlier build listed as its own, and sf-mod-build deletes only the mod's own plugins and archives (the names sfmod.json gives) and what earlier builds made there, so nothing was deleted. "
                       + $"Fix: move {(foreign.Count == 1 ? "it" : "them")} out of dist and run again (a build by sf-mod-build 0.3.0 or earlier left no list of what it made, so its builder's other files are named here once), or set \"dist\" in {project.File} to a folder of its own";
            // the made list goes last, so a delete that fails (a plugin open in the CK or xEdit) leaves it for the next run
            foreach (var e in delete.OrderBy(e => Same(e.Name, MadeList)))
            {
                // a link is removed, never what it points to
                if (e is DirectoryInfo sub) { if (sub.LinkTarget != null) sub.Delete(); else sub.Delete(recursive: true); }
                else e.Delete();
                Log.Write($"deleted from dist (made by an earlier build): {e.FullName}");
            }
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return $"{folder} can't be cleaned out: {e.Message} Fix: close any program using files there and run again";
        }
    }

    /// <summary>File.Copy, then clears a read-only mark the copy took from its source, so the next build can delete
    /// the copy from dist, stage or creations-out.</summary>
    public static void CopyFile(string from, string to, bool overwrite = false)
    {
        File.Copy(from, to, overwrite);
        var attributes = File.GetAttributes(to);
        if (attributes.HasFlag(FileAttributes.ReadOnly)) File.SetAttributes(to, attributes & ~FileAttributes.ReadOnly);
    }

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
    /// closing quote) doubled and quotes escaped. An argument with a character cmd.exe reads as syntax (&amp;, |, &lt;,
    /// &gt;, ^, parentheses) is quoted too. With <paramref name="batch"/> (a .cmd or .bat program), so is one with a
    /// character cmd.exe splits batch parameters on (, ; =), so the program gets it as one plain argument. (cmd.exe
    /// still expands %NAME% inside quotes.)</summary>
    public static string Quote(string arg, bool batch = false)
    {
        if (arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '\t', '\n', '\v', '"', '&', '|', '<', '>', '^', '(', ')' }) < 0
            && !(batch && arg.IndexOfAny(new[] { ',', ';', '=' }) >= 0)) return arg;
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
