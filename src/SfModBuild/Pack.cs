// sf-mod-build pack [sfmod.json]: packs the mod's files into archives named after the plugin, with the Creation Kit's
// own Archive2:
//   "<plugin> - Main.ba2"      the compiled scripts (<stage>/Scripts) and the general loose files from assets.source
//                              (meshes, materials, terrain, sounds ...); packed with -compression=None when it holds a
//                              .wem or .ffxanim, which play silent when stored compressed
//   "<plugin> - Textures.ba2"  the loose .dds files from assets.source, packed with -format=DDS
//   "<part> - Main.ba2"        each part's compiled scripts (<stage>/parts/<part>/Scripts), for a part with scripts
//   "<plugin> - <suffix>.ba2"  each entry of the top-level archives list: its source folder packed as it is (with
//                              -compression=None and -format=DDS when asked), or its ready file copied (not with xbox)
// With "xbox": true it also makes the Creations upload set: what earlier builds made in creations-out is deleted (anything else there stops it), dist's files are copied into it,
// and "<plugin> - Main_xbox.ba2" and "<plugin> - Textures_xbox.ba2" are packed there with -compression=XBox. The Xbox
// texture archive is always a separate archive (never the PC one renamed by hand); it takes assets.xboxTextures when
// given (Xbox-format exports, which must cover every PC texture), else the PC textures. Pack deletes the mod's own old
// archives from dist (names match without case), and with "xbox" on it refuses any other .ba2 in dist (sf-mod-build
// ships only the archives it builds; zip.archives can't be used with "xbox"), so the Creations set never silently lacks an archive. With nothing to pack and "xbox" on, it deletes
// the mod's own old archives from creations-out too.
// A scripts-only mod is packed straight from <stage>/Scripts, as before loose files were supported. With loose files,
// each archive's files are first copied into <stage>/pack/<archive>, so every archive has one root.
// Exit 0 when every archive was made (or there is nothing to pack), 1 when Archive2 failed, 2 bad input or setup.
namespace SfModBuild;

internal static class Pack
{
    public const string Usage =
        "usage: sf-mod-build pack [<sfmod.json or its folder>] [--game <game folder>] [--verbose] [--log <file>]\n" +
        "  packs the compiled scripts and the loose files (assets) into \"<plugin> - Main.ba2\" and \"<plugin> - Textures.ba2\"\n" +
        "  in dist with the Creation Kit's Archive2, packs each part's scripts into \"<part> - Main.ba2\", and makes each\n" +
        "  archive in the archives list (packed from its source folder, or copied from its file); with \"xbox\": true\n" +
        "  also makes creations-out (dist's files plus the \"_xbox\" archives), first deleting what earlier builds made\n" +
        "  there (it stops if creations-out holds anything else); then runs steps.afterPack";

    public static readonly ArgSpec Spec = new(new[] { "--game" }, Array.Empty<string>(), MaxPositionals: 1);

    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);

    /// <summary>Packs, then runs steps.afterPack. What they add to dist joins the made list.</summary>
    public static int Run(ParsedArgs p, IHostEnv env, TextWriter stdout) =>
        Common.TrackDist(Common.LoadProject(p, TextWriter.Null, "")?.Dist,
            () => Steps.After("afterPack", p, env, stdout, "CANNOT PACK", o => PackProject(p, env, o)),
            Common.LoadProject(p, TextWriter.Null, ""));

    static int PackProject(ParsedArgs p, IHostEnv env, TextWriter stdout)
    {
        var project = Common.LoadProject(p, stdout, "CANNOT PACK");
        if (project == null) return ExitCodes.BadInput;
        if (!project.HasMainArchive && !project.HasTexturesArchive && project.Archives.Count == 0 && !project.Parts.Any(x => x.Scripts != null))
        {
            // archives from an earlier build that had scripts or loose files must not stay in dist and reach the zip;
            // the game folders are looked up once, and only when there is something to delete
            List<string>? found = null;
            List<string> Games() => found ??= NothingToPackGames(project, p.Get("--game"), env, stdout);
            if (DeleteStale(project, Games, stdout) is { } code) return code;
            if (DeleteStaleCreations(project, Games, stdout) is { } xboxCode) return xboxCode;
            stdout.WriteLine($"RESULT: OK, nothing to pack (the project has no scripts{(project.Assets != null ? " and its assets folder has no files" : " and no assets")})");
            return ExitCodes.Ok;
        }
        if (project.Scripts != null)
        {
            var scripts = Path.Combine(project.Stage, "Scripts");
            if (!Directory.Exists(scripts) || !Directory.EnumerateFiles(scripts, "*.pex", SearchOption.AllDirectories).Any())
            {
                stdout.WriteLine($"RESULT: CANNOT PACK (there are no compiled scripts in {scripts}. Fix: run sf-mod-build compile first).");
                return ExitCodes.BadInput;
            }
        }
        foreach (var part in project.Parts.Where(x => x.Scripts != null))
        {
            var scripts = Path.Combine(part.StageIn(project.Stage), "Scripts");
            if (!Directory.Exists(scripts) || !Directory.EnumerateFiles(scripts, "*.pex", SearchOption.AllDirectories).Any())
            {
                stdout.WriteLine($"RESULT: CANNOT PACK (there are no compiled scripts for the part {part.Plugin} in {scripts}. Fix: run sf-mod-build compile first).");
                return ExitCodes.BadInput;
            }
        }
        // only ready archives to copy (archives entries with "file"): Archive2 never runs, so it isn't needed, and a game that
        // can't be found doesn't stop the copy (a --game that is wrong still does)
        var needsArchive2 = project.HasMainArchive || project.HasTexturesArchive || project.Parts.Any(x => x.Scripts != null) || project.Archives.Any(a => a.File == null);
        var game = Common.FindGame(p.Get("--game"), env, out var error);
        if (game == null && (needsArchive2 || p.Get("--game") != null))
        {
            stdout.WriteLine($"RESULT: CANNOT PACK ({error})");
            return ExitCodes.BadInput;
        }
        if (!needsArchive2) return PackAll(project, game?.Folder, "", env, stdout);
        var archive2 = Path.Combine(game!.Folder, "Tools", "Archive2", "Archive2.exe");
        if (!File.Exists(archive2))
        {
            stdout.WriteLine($"RESULT: CANNOT PACK (Archive2 isn't at {archive2}. {Doctor.CkHowTo})");
            return ExitCodes.BadInput;
        }
        return PackAll(project, game!.Folder, archive2, env, stdout);
    }

    /// <summary>Archive2's command line for a scripts-only archive:
    /// "&lt;stage&gt;\Scripts" "-c=&lt;ba2&gt;" "-r=&lt;stage&gt;" [-compression=XBox] -q.</summary>
    public static string CommandLine(string stage, string ba2, bool xbox)
    {
        var s = Path.TrimEndingDirectorySeparator(Path.GetFullPath(stage));
        return CommandLine(Path.Combine(s, "Scripts"), s, ba2, format: null, compression: xbox ? "XBox" : null);
    }

    /// <summary>Archive2's command line: "&lt;source&gt;" "-c=&lt;ba2&gt;" "-r=&lt;root&gt;" [-format=..] [-compression=..] -q.</summary>
    public static string CommandLine(string source, string root, string ba2, string? format, string? compression)
    {
        var r = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var line = $"{Common.Quote(Path.TrimEndingDirectorySeparator(Path.GetFullPath(source)))} {Common.Quote("-c=" + Path.GetFullPath(ba2))} {Common.Quote("-r=" + r)}";
        if (format != null) line += " -format=" + format;
        if (compression != null) line += " -compression=" + compression;
        return line + " -q";
    }

    /// <summary>Why deleting files in creations-out would be unsafe, or null.</summary>
    public static string? UnsafeCreationsOut(SfModProject project, IEnumerable<string> gameFolders) =>
        Common.UnsafeToEmpty("creations-out", project.CreationsOut, project.Folder, Keep(project), gameFolders);

    /// <summary>Before the Creations set is made: finds what an earlier build made in creations-out, which pack then
    /// deletes. That is the names its made list gives, the mod's own plugins and archives (<see cref="Common.OwnNames"/>)
    /// and the files about to be copied there from dist. Any other entry (a folder, a file the user keeps there such
    /// as a description draft, another plugin) stops pack, naming each one, before anything is deleted. Null when
    /// pack may go on (<paramref name="delete"/> then holds the files), else why not, with the fix.</summary>
    static string? CheckCreationsOut(SfModProject project, List<FileInfo> delete)
    {
        var folder = project.CreationsOut;
        if (Log.FilePath is { } log && Path.GetFullPath(log).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return $"the log file {log} is inside {folder}, where the Creations set goes. Fix: give --log a file outside the mod's output folders";
        try
        {
            var d = new DirectoryInfo(Path.TrimEndingDirectorySeparator(folder));
            if (!d.Exists) return null;
            // deleting files through a link would delete them wherever it points
            if (d.LinkTarget is { } to)
                return $"the creations-out folder {folder} is a link or junction to {to}, so pack would delete files wherever it points. Fix: in {project.File}, set \"creationsOut\" to a real folder of its own, for example \"creations-out\"";
            var mine = Common.OwnNames(project);
            mine.Add(Common.MadeList);
            mine.UnionWith(Common.ReadMadeList(folder));
            if (Directory.Exists(project.Dist))
                mine.UnionWith(Directory.GetFiles(project.Dist).Where(Common.Shippable).Select(f => Path.GetFileName(f)));
            var foreign = new List<string>();
            // zip.folder is creations-out itself: its zips stay; anywhere else, a zip here is the user's and is named
            var zipsHere = Common.IsZipFolder(project, folder);
            foreach (var e in d.EnumerateFileSystemInfos("*", new EnumerationOptions { AttributesToSkip = 0 }).OrderBy(e => e.Name, StringComparer.Ordinal))
            {
                // zip.folder at or inside creations-out: the zips (or the folder holding them) stay, as in dist
                if (zipsHere && e is FileInfo z && (z.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || z.Name.EndsWith(Zip.PartialSuffix, StringComparison.OrdinalIgnoreCase))) continue;
                if (e is DirectoryInfo && Common.HoldsZipFolder(project, e.FullName)) continue;
                // only plain files are ever deleted here: a folder (or a link) is always the user's
                if (e is FileInfo f && f.LinkTarget == null && mine.Contains(f.Name)) delete.Add(f);
                else foreign.Add(e is FileInfo ? $"\"{e.Name}\"" : $"\"{e.Name}\" (a folder)");
            }
            if (foreign.Count == 0) return null;
            delete.Clear();
            return $"the creations-out folder {folder} holds {string.Join(", ", foreign)}, which sf-mod-build didn't make, and pack deletes only the mod's own plugins and archives (the names sfmod.json gives) and what earlier builds made there, so nothing was deleted. "
                   + $"Fix: move {(foreign.Count == 1 ? "it" : "them")} out of creations-out and run again (sf-mod-build 0.4.0 and earlier left no list of what they made there, so their other files are named here once), or set \"creationsOut\" in {project.File} to a folder of its own";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return $"the creations-out folder {folder} can't be checked ({e.Message}). Fix: check that this account can read it and run again";
        }
    }

    /// <summary>The folders no emptied folder may be or hold: the mod, the stage, dist, the builder, the script
    /// sources and the assets, plus any given.</summary>
    static List<string> Keep(SfModProject project, params string[] more)
    {
        var keep = new List<string> { project.Folder, project.Stage, project.Dist };
        keep.AddRange(project.BuilderFolders);
        keep.AddRange(more);
        keep.AddRange(project.AllScriptSources);
        if (project.Assets != null)
        {
            keep.Add(project.Assets.Source);
            if (project.Assets.XboxTextures != null) keep.Add(project.Assets.XboxTextures);
        }
        keep.AddRange(project.Archives.Where(a => a.Source != null).Select(a => a.Source!));
        if (project.Voice != null)
        {
            keep.Add(project.Voice.Source);
            keep.Add(project.Voice.Out);
        }
        return keep;
    }

    /// <summary>The folder loose files are copied into before packing: &lt;stage&gt;/pack.</summary>
    public static string PackFolder(SfModProject project) => Path.Combine(project.Stage, "pack");

    /// <summary>One archive to make: its file, its source folder and root, and Archive2's format and compression.</summary>
    sealed record Job(string Ba2, string Source, string Root, string? Format, string? Compression);

    public static int PackAll(SfModProject project, string? gameFolder, string archive2, IHostEnv env, TextWriter stdout)
    {
        // every install found, not only the one in use: the mod may sit in a second install
        var games = Common.GameFolders(gameFolder, env);
        string? unsafeWhy = project.Xbox ? UnsafeCreationsOut(project, games) : null;
        if (unsafeWhy != null)
        {
            stdout.WriteLine($"RESULT: CANNOT PACK ({unsafeWhy}). Fix: in {project.File}, set \"creationsOut\" to a folder of its own, for example \"creations-out\".");
            return ExitCodes.BadInput;
        }
        // dist gets the new archives and loses the old ones: it may not be or be inside any install either
        if (UnsafePackDist(project, games) is { } distWhy)
        {
            stdout.WriteLine(DistRefused(project, distWhy));
            return ExitCodes.BadInput;
        }
        if (project.Assets != null && gameFolder != null && AssetsInGame(project.Assets, gameFolder) is { } inGame)
        {
            stdout.WriteLine($"RESULT: CANNOT PACK ({inGame}). Fix: copy the mod's own loose files into a folder of the mod, for example \"Assets\", and point assets.source at it.");
            return ExitCodes.BadInput;
        }
        // checked before any archive is made, so a missing or empty folder stops pack before Archive2 runs
        if (ExtraProblem(project, gameFolder) is { } extraWhy)
        {
            stdout.WriteLine($"RESULT: CANNOT PACK ({extraWhy}).");
            return ExitCodes.BadInput;
        }
        try { Directory.CreateDirectory(project.Dist); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            stdout.WriteLine($"RESULT: CANNOT PACK (the dist folder {project.Dist} can't be made: {e.Message}).");
            return ExitCodes.BadInput;
        }
        // creations-out is checked before anything is deleted anywhere: a file or folder of the user's there stops pack
        var creationsDelete = new List<FileInfo>();
        if (project.Xbox && CheckCreationsOut(project, creationsDelete) is { } creationsWhy)
        {
            stdout.WriteLine($"RESULT: CANNOT PACK ({creationsWhy}).");
            return ExitCodes.BadInput;
        }
        if (DeleteStale(project, () => games, stdout) is { } staleCode) return staleCode;
        if (!project.Xbox)
        {
            // without the Creations set pack doesn't need to stop here, but zip will: say so one command earlier
            foreach (var f in Directory.GetFiles(project.Dist).Order(StringComparer.Ordinal)
                         .Where(f => Common.Shippable(f) && Common.StrayArchive(project, f)))
                stdout.WriteLine($"WARNING  {Common.StrayArchiveWarning(project, f)}");
        }
        else
        {
            // checked before any archive is made: the Creations set takes dist's files, so an archive there that this
            // build doesn't make would otherwise be left out of it, or shipped stale
            var stray = Directory.GetFiles(project.Dist).Order(StringComparer.Ordinal)
                .FirstOrDefault(f => Common.Shippable(f) && Common.StrayArchive(project, f));
            if (stray != null)
            {
                stdout.WriteLine($"RESULT: CANNOT PACK ({Common.StrayArchiveError(project, stray, "the Creations set in " + project.CreationsOut)}).");
                return ExitCodes.BadInput;
            }
        }

        // Where each archive's files come from
        string? mainSource = null, mainRoot = null, texSource = null, xboxTexSource = null;
        var assets = project.Assets;
        var voiceFiles = project.Voice?.Files ?? Array.Empty<SfModProject.AssetFile>();
        var staged = (assets != null && (assets.General.Count > 0 || assets.Textures.Count > 0)) || voiceFiles.Count > 0;
        if (project.HasMainArchive && !(assets is { General.Count: > 0 }) && voiceFiles.Count == 0)
        {
            mainRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(project.Stage));
            mainSource = Path.Combine(mainRoot, "Scripts");
        }
        if (staged)
        {
            var pack = PackFolder(project);
            var why = Common.UnsafeToEmpty("pack", pack, project.Folder, Keep(project, project.CreationsOut), games)
                      ?? Common.Empty(pack);
            if (why != null)
            {
                stdout.WriteLine($"RESULT: CANNOT PACK ({why}).");
                return ExitCodes.BadInput;
            }
            var stagedError = Stage(project, pack, out var stagedMain, out texSource, out xboxTexSource, stdout);
            if (stagedError != null)
            {
                stdout.WriteLine($"RESULT: CANNOT PACK ({stagedError}).");
                return ExitCodes.BadInput;
            }
            if (stagedMain != null) mainSource = mainRoot = stagedMain;
        }
        var mainCompression = project.UncompressedMain ? "None" : null;
        if (mainCompression != null)
            stdout.WriteLine($"note     \"{project.Ba2Name}\" holds .wem or .ffxanim files, which play silent when stored compressed, so it is packed with -compression=None");

        var pcJobs = new List<Job>();
        if (project.HasMainArchive) pcJobs.Add(new Job(Path.Combine(project.Dist, project.Ba2Name), mainSource!, mainRoot!, null, mainCompression));
        if (project.HasTexturesArchive) pcJobs.Add(new Job(Path.Combine(project.Dist, project.TexturesBa2Name), texSource!, texSource!, "DDS", null));
        // each part's compiled scripts (refused with xbox, so PC only)
        foreach (var part in project.Parts.Where(x => x.Scripts != null))
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(part.StageIn(project.Stage)));
            pcJobs.Add(new Job(Path.Combine(project.Dist, part.Ba2Name), Path.Combine(root, "Scripts"), root, null, null));
        }
        foreach (var job in pcJobs)
            if (Archive(project, archive2, job, env, stdout) is { } code) return code;
        // the archives list (refused with xbox, so these are PC only)
        var madeNames = pcJobs.Select(j => j.Ba2).ToList();
        foreach (var extra in project.Archives)
        {
            var ba2 = Path.Combine(project.Dist, extra.Name);
            if (extra.Source != null)
            {
                var job = new Job(ba2, extra.Source, extra.Source, extra.Dds ? "DDS" : null, extra.Uncompressed ? "None" : null);
                if (Archive(project, archive2, job, env, stdout) is { } code) return code;
            }
            else
            {
                if (DeleteOld(ba2, stdout) is { } code) return code;
                try { Common.CopyFile(extra.File!, ba2); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    stdout.WriteLine($"RESULT: FAILED, {extra.File} couldn't be copied to {ba2} ({e.Message}). Fix: check that the file is there and readable, close any program using the archive, and run again.");
                    return ExitCodes.Failed;
                }
                Log.Write($"copied {extra.File} -> {ba2}");
                stdout.WriteLine($"copied   {ba2} from {extra.File} ({new FileInfo(ba2).Length} bytes)");
            }
            madeNames.Add(ba2);
        }

        var made = madeNames.Count == 1 ? madeNames[0]
            : $"{string.Join(" and ", madeNames.Select(j => $"\"{Path.GetFileName(j)}\""))} made in {project.Dist}";
        if (!project.Xbox)
        {
            stdout.WriteLine(madeNames.Count == 1 ? $"RESULT: OK, {made} made" : $"RESULT: OK, {made}");
            return ExitCodes.Ok;
        }
        var xboxJobs = new List<Job>();
        if (project.HasMainArchive)
            xboxJobs.Add(new Job(Path.Combine(project.CreationsOut, project.XboxBa2Name), mainSource!, mainRoot!, null, mainCompression ?? "XBox"));
        if (project.HasTexturesArchive)
            xboxJobs.Add(new Job(Path.Combine(project.CreationsOut, project.XboxTexturesBa2Name), xboxTexSource ?? texSource!, xboxTexSource ?? texSource!, "DDS", "XBox"));
        // only what CheckCreationsOut found to be an earlier build's (0.4.0 and earlier emptied the folder); the made
        // list goes last, so a delete that fails (an archive open in another program) leaves it for the next run
        foreach (var f in creationsDelete.OrderBy(f => string.Equals(f.Name, Common.MadeList, StringComparison.OrdinalIgnoreCase)))
        {
            try { f.Delete(); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                stdout.WriteLine($"RESULT: CANNOT PACK (\"{f.Name}\", an earlier build's file in {project.CreationsOut}, couldn't be deleted: {e.Message}). Fix: close any program using it and run again.");
                return ExitCodes.BadInput;
            }
            Log.Write($"deleted from creations-out (made by an earlier build): {f.FullName}");
        }
        try
        {
            Directory.CreateDirectory(project.CreationsOut);
            // dist's own files (the plugin, the PC archives, anything else the builder wrote): the same ones the zip
            // takes, so never zips, Papyrus source, half-written zips or the log (archives this build doesn't make
            // were refused above)
            var copies = Directory.GetFiles(project.Dist).Order(StringComparer.OrdinalIgnoreCase).Where(Common.Shippable).ToList();
            // listed before anything is written, so a copy or Archive2 run that fails still leaves its file listed
            Common.RecordMade(project.CreationsOut, copies.Concat(xboxJobs.Select(j => j.Ba2)).Select(Path.GetFileName).Select(n => n!), add: false);
            foreach (var f in copies)
            {
                Common.CopyFile(f, Path.Combine(project.CreationsOut, Path.GetFileName(f)), overwrite: true);
                Log.Write($"copied {f} into {project.CreationsOut}");
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            stdout.WriteLine($"RESULT: CANNOT PACK (dist's files couldn't be copied into {project.CreationsOut}: {e.Message}). Fix: close any program using files there and run again.");
            return ExitCodes.BadInput;
        }
        foreach (var job in xboxJobs)
            if (Archive(project, archive2, job, env, stdout) is { } code) return code;
        if (project.HasTexturesArchive && assets!.XboxTextures == null)
            stdout.WriteLine($"note     \"{project.XboxTexturesBa2Name}\" holds the PC-format textures (no assets.xboxTextures given); a console test is the only proof they look right on Xbox");
        stdout.WriteLine(madeNames.Count == 1 ? $"RESULT: OK, {made} and the Creations set in {project.CreationsOut} made" : $"RESULT: OK, {made}, and the Creations set made in {project.CreationsOut}");
        return ExitCodes.Ok;
    }

    /// <summary>Why the assets can't be packed from where they are, or null: a folder in the game (or holding it)
    /// would pack the game's own files.</summary>
    static string? AssetsInGame(SfModProject.AssetsSection assets, string gameFolder)
    {
        var game = Path.TrimEndingDirectorySeparator(Path.GetFullPath(gameFolder));
        foreach (var (field, folder) in new[] { ("assets.source", assets.Source), ("assets.xboxTextures", assets.XboxTextures) })
        {
            if (folder == null) continue;
            var f = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
            if (string.Equals(f, game, StringComparison.OrdinalIgnoreCase)
                || f.StartsWith(game + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || game.StartsWith(f + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return $"{field} {f} is inside or holds the game's folder {game}, so the game's own files would be packed";
        }
        return null;
    }

    /// <summary>Why an entry of the archives list can't be made, or null. A source folder may have been filled by an
    /// afterPlugin step, so it is checked here, with the assets rules: it must be there, hold files, be apart from the
    /// game's folder, hold no links, no files at its top and no .psc, plugins, archives or zips. A ready archive
    /// must still be there.</summary>
    static string? ExtraProblem(SfModProject project, string? gameFolder)
    {
        var game = gameFolder == null ? null : Path.TrimEndingDirectorySeparator(Path.GetFullPath(gameFolder));
        foreach (var a in project.Archives)
        {
            var what = $"the archives entry for \"{a.Name}\"";
            if (a.File != null)
            {
                if (!File.Exists(a.File)) return $"{what} copies {a.File}, which isn't there. Fix: put the ready archive back, or change archives in {project.File}";
                continue;
            }
            var src = a.Source!;
            if (!Directory.Exists(src))
                return $"{what} packs the folder {src}, which isn't there. Fix: make the folder and put the archive's files in it (laid out like Data), or have a steps.afterPlugin command fill it";
            if (game != null && (string.Equals(src, game, StringComparison.OrdinalIgnoreCase)
                || src.StartsWith(game + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || game.StartsWith(src + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                return $"{what} packs {src}, which is inside or holds the game's folder {game}, so the game's own files would be packed. Fix: copy the files into a folder of the mod";
            var count = 0;
            try
            {
                foreach (var e in new DirectoryInfo(src).EnumerateFileSystemInfos("*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 }))
                {
                    if (e.LinkTarget != null)
                        return $"{what}: {e.FullName} in {src} is a link or junction, which pack doesn't follow. Fix: copy the files themselves into the folder";
                    if (e is not FileInfo f) continue;
                    // Archive2 packs the folder as it is, so clutter that assets.source leaves out would ship
                    var rel = Path.GetRelativePath(src, f.FullName);
                    // a dot folder (.git, .vs) counts too, as in assets.source
                    if (rel.Split(Path.DirectorySeparatorChar).Any(part => part.StartsWith('.')) || SfModProject.SkippedAssetNames.Contains(f.Name, StringComparer.OrdinalIgnoreCase))
                        return $"{what}: {f.FullName} would be packed into the archive, because pack packs {src} as it is (it leaves out dot files, dot folders, Thumbs.db and desktop.ini only in assets.source). Fix: delete it or move it out of {src}";
                    if (!rel.Contains(Path.DirectorySeparatorChar))
                        return $"{what}: {f.FullName} is at the top of {src}, where the game never looks inside an archive. Fix: put it in the Data folder it belongs to (Sound\\, Meshes\\ and so on), or move it out";
                    if (SfModProject.RefusedAssetExtensions.Contains(f.Extension, StringComparer.OrdinalIgnoreCase))
                        return $"{what}: {f.FullName} is a {f.Extension} file, which never goes inside an archive. Fix: move it out of {src}";
                    count++;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return $"{what}: the folder {src} can't be read ({e.Message}). Fix: check that this account can read it";
            }
            if (count == 0)
                return $"{what} packs the folder {src}, which holds no files, so the archive would be empty. Fix: put the archive's files in it (laid out like Data), or remove the entry";
            Log.Write($"archives: {count} file(s) in {src} for {a.Name}");
        }
        return null;
    }

    /// <summary>Copies each archive's files into its own folder under pack: main (compiled scripts plus the general
    /// loose files), textures and textures-xbox. Null when done, else why it failed. A source is null when that
    /// archive has no files.</summary>
    static string? Stage(SfModProject project, string pack, out string? main, out string? textures, out string? xboxTextures, TextWriter stdout)
    {
        main = textures = xboxTextures = null;
        var assets = project.Assets ?? new SfModProject.AssetsSection { Source = "", General = Array.Empty<SfModProject.AssetFile>(), Textures = Array.Empty<SfModProject.AssetFile>(), XboxTextureFiles = Array.Empty<SfModProject.AssetFile>() };
        var voiceFiles = project.Voice?.Files ?? Array.Empty<SfModProject.AssetFile>();
        try
        {
            if (assets.General.Count > 0 || voiceFiles.Count > 0)
            {
                main = Path.Combine(pack, "main");
                // Paths compare without case: the game and Windows treat Meshes\Box.nif and meshes\box.nif as one file
                var taken = new Dictionary<string, (string Full, bool Script)>(StringComparer.OrdinalIgnoreCase);
                if (project.Scripts != null)
                {
                    var compiled = Path.Combine(project.Stage, "Scripts");
                    foreach (var pex in Directory.GetFiles(compiled, "*", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase))
                    {
                        var rel = Path.Combine("Scripts", Path.GetRelativePath(compiled, pex));
                        Copy(pex, Path.Combine(main, rel));
                        taken[rel] = (pex, true);
                    }
                }
                foreach (var f in assets.General)
                {
                    if (taken.TryGetValue(f.Relative, out var other))
                        return other.Script
                            ? $"{f.Full} in assets.source would be {f.Relative} in the archive, which is also the compiled script {other.Full}. Fix: remove the loose copy from the assets folder; the script is compiled from its source"
                            : CaseTwin("assets.source", f.Full, other.Full);
                    Copy(f.Full, Path.Combine(main, f.Relative));
                    taken[f.Relative] = (f.Full, false);
                }
                // the voice step's .wem and .ffxanim files; one that assets.source also has would ship twice
                foreach (var f in voiceFiles)
                {
                    if (taken.TryGetValue(f.Relative, out var other))
                        return $"{f.Full}, made by the voice step, would be {f.Relative} in the archive, which is also {other.Full}. Fix: remove the loose copy from the assets folder (or the WAV from voice.source), so the line has one sound file";
                    Copy(f.Full, Path.Combine(main, f.Relative));
                    taken[f.Relative] = (f.Full, false);
                }
            }
            if (assets.Textures.Count > 0)
            {
                textures = Path.Combine(pack, "textures");
                if (StageTextures(assets.Textures, textures, "assets.source") is { } twin) return twin;
            }
            if (project.Xbox && assets.XboxTextures != null && assets.Textures.Count > 0)
            {
                // The loader refuses these; checked again so an empty folder never reaches Archive2
                if (assets.XboxTextureFiles.Count == 0)
                    return $"assets.xboxTextures {assets.XboxTextures} holds no .dds textures, so the Xbox texture archive would be empty. Fix: put the Xbox-format .dds files there, or remove assets.xboxTextures to pack the PC textures for Xbox too";
                var xb = new HashSet<string>(assets.XboxTextureFiles.Select(f => f.Relative), StringComparer.OrdinalIgnoreCase);
                if (assets.Textures.FirstOrDefault(f => !xb.Contains(f.Relative)) is { } lacking)
                    return $"assets.xboxTextures {assets.XboxTextures} has no Xbox copy of {lacking.Relative}, so it would be missing on Xbox. Fix: export an Xbox version of each PC texture, or remove assets.xboxTextures to pack the PC textures for Xbox too";
                xboxTextures = Path.Combine(pack, "textures-xbox");
                if (StageTextures(assets.XboxTextureFiles, xboxTextures, "assets.xboxTextures") is { } twin) return twin;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return $"the loose files couldn't be copied into {pack}: {e.Message}. Fix: check that every file in the assets folder is there and readable, and run again";
        }
        if (project.Assets != null)
            stdout.WriteLine($"staged   {assets.General.Count} general and {assets.Textures.Count} texture file(s) from {assets.Source}");
        if (voiceFiles.Count > 0)
            stdout.WriteLine($"staged   {voiceFiles.Count} voice and lip file(s) from {project.Voice!.Data}");
        return null;
    }

    /// <summary>Copies the textures into the folder. Null when done, else why not: two of them differ only in case.</summary>
    static string? StageTextures(IReadOnlyList<SfModProject.AssetFile> files, string into, string field)
    {
        var taken = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in files)
        {
            if (taken.TryGetValue(f.Relative, out var other)) return CaseTwin(field, f.Full, other);
            Copy(f.Full, Path.Combine(into, f.Relative));
            taken[f.Relative] = f.Full;
        }
        return null;
    }

    /// <summary>Two loose files whose paths differ only in case (possible on Linux and in case-sensitive folders).</summary>
    static string CaseTwin(string field, string file, string other) =>
        $"{SfModProject.CaseTwinProblem(field, file, other)} Fix: {SfModProject.CaseTwinFix.TrimEnd('.')}";

    static void Copy(string from, string to)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        Common.CopyFile(from, to);
        Log.Write($"staged {from} -> {to}");
    }

    /// <summary>Deletes from dist each of the mod's archives this build doesn't make any more (the mod's textures or
    /// scripts were removed), and any copy of an archive it does make spelled in other letter case (a case-sensitive
    /// disk can hold both), so neither can reach the zip. Before deleting anything, dist may not be or be inside any
    /// of <paramref name="games"/> (asked for only then); dist itself may never be a link, checked before it is
    /// read. Null when done, else the exit code to return.</summary>
    static int? DeleteStale(SfModProject project, Func<List<string>> games, TextWriter stdout)
    {
        var made = project.ArchiveNames.ToList();
        var old = new List<string>();
        string? why = DistLink(project);
        if (why == null)
        {
            try
            {
                // an archive zip.archives lists is the builder's own (a records-only part's, say): never deleted here
                foreach (var name in new[] { project.Ba2Name, project.TexturesBa2Name }.Concat(project.Parts.Select(x => x.Ba2Name))
                             .Concat(project.Archives.Select(a => a.Name)).Distinct()
                             .Where(n => !project.ZipArchives.Contains(n, StringComparer.OrdinalIgnoreCase)))
                    foreach (var path in Common.FilesNamed(project.Dist, name))
                        // the archive this build makes is deleted at its exact name by Archive just before Archive2
                        // writes it (so a link there is removed, never written through)
                        if (!(made.Contains(name) && Path.GetFileName(path) == name)) old.Add(path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                why = $"the dist folder {project.Dist} can't be checked ({e.Message})";
            }
            if (why == null && old.Count == 0) return null;
            if (why == null)
            {
                // the game lookup stays outside the dist check's catch, so its errors aren't blamed on dist
                var never = games();
                try { why = Plugin.UnsafeDist(project, never); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    why = $"the dist folder {project.Dist} can't be checked ({e.Message})";
                }
            }
        }
        if (why != null)
        {
            stdout.WriteLine(DistRefused(project, why));
            return ExitCodes.BadInput;
        }
        foreach (var path in old)
        {
            if (DeleteOld(path, stdout) is { } code) return code;
            var name = Path.GetFileName(path);
            var byThatSpelling = made.Any(m => string.Equals(m, name, StringComparison.OrdinalIgnoreCase));
            stdout.WriteLine($"deleted  {path} (from an earlier build; this build doesn't make it{(byThatSpelling ? " by that spelling" : "")})");
        }
        return null;
    }

    /// <summary>Why pack may not delete or write archives in dist, or null: the plugin step's dist check against
    /// <paramref name="games"/>, then <see cref="DistLink"/>.</summary>
    static string? UnsafePackDist(SfModProject project, List<string> games)
    {
        try { return Plugin.UnsafeDist(project, games) ?? DistLink(project); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return $"the dist folder {project.Dist} can't be checked ({e.Message})";
        }
    }

    /// <summary>Why dist itself is a link or junction (or can't be checked), or null. pack doesn't empty dist (which
    /// would remove only the link): it deletes and writes files in it, so through a link they would land wherever it
    /// points, a game's Data folder included.</summary>
    static string? DistLink(SfModProject project)
    {
        try
        {
            return FolderLink(project.Dist) is { } to
                ? $"the dist folder {project.Dist} is a link or junction to {to}, so pack would delete and write archives wherever it points"
                : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return $"the dist folder {project.Dist} can't be checked ({e.Message})";
        }
    }

    /// <summary>Where a folder that is a link or junction points, or null. A trailing separator is dropped first:
    /// on Linux, readlink on "dist/" follows the link and fails, so the link wouldn't be seen.</summary>
    static string? FolderLink(string folder) => new DirectoryInfo(Path.TrimEndingDirectorySeparator(folder)).LinkTarget;

    static string DistRefused(SfModProject project, string why) =>
        $"RESULT: CANNOT PACK ({why}). Fix: in {project.File}, set \"dist\" to a folder of its own, for example \"dist\".";

    /// <summary>The game folders the nothing-to-pack path protects: every install found (not only the first), plus
    /// the --game folder. A game that isn't found doesn't stop this path (nothing here needs the game's tools); a
    /// --game folder that isn't a game gets a WARNING.</summary>
    static List<string> NothingToPackGames(SfModProject project, string? gameOption, IHostEnv env, TextWriter stdout)
    {
        var never = GameLocator.FindAll(env).Select(g => g.Folder).ToList();
        if (gameOption != null)
        {
            var given = GameLocator.FromOption(gameOption, out var gameError);
            if (given != null) never.Add(given.Folder);
            else stdout.WriteLine($"WARNING  {gameError!.TrimEnd().TrimEnd('.')}. Checking {(project.Xbox ? "dist and creations-out" : "dist")} against the installs found without it{(never.Count == 0 ? " (none)" : "")}.");
        }
        if (never.Count == 0) Log.Write("nothing to pack: no game folder was found; checking without one");
        else Log.Write($"nothing to pack: dist and creations-out may not be, hold or be inside {string.Join(", ", never)}");
        return never;
    }

    /// <summary>With "xbox" on and nothing to pack: deletes the mod's own old archives (the _xbox ones and the PC
    /// copies) from creations-out, so an upload of that folder can't carry them. Only those names are touched; the
    /// folder is otherwise left as it is. First, always and before it is read, creations-out itself may never be a
    /// link here, as with dist (this path deletes in it rather than emptying it). Then, only when there is something
    /// to delete, it is checked against the games: it may not be, hold or be inside any of
    /// <paramref name="games"/>, as on the full pack.
    /// Null when done, else the exit code.</summary>
    static int? DeleteStaleCreations(SfModProject project, Func<List<string>> games, TextWriter stdout)
    {
        if (!project.Xbox || !Directory.Exists(project.CreationsOut)) return null;
        var files = new List<string>();
        string? why;
        try
        {
            why = FolderLink(project.CreationsOut) is { } to
                ? $"the creations-out folder {project.CreationsOut} is a link or junction to {to}, so pack would delete archives wherever it points"
                : null;
            if (why == null)
                files = new[] { project.XboxBa2Name, project.XboxTexturesBa2Name, project.Ba2Name, project.TexturesBa2Name }
                    .SelectMany(n => Common.FilesNamed(project.CreationsOut, n)).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            why = $"the creations-out folder {project.CreationsOut} can't be checked ({e.Message})";
        }
        if (why == null && files.Count == 0) return null;
        if (why == null)
        {
            // the game lookup stays outside this catch, so its errors aren't blamed on creations-out
            var never = games();
            try { why = Common.UnsafeToEmpty("creations-out", project.CreationsOut, project.Folder, Keep(project), never.Count == 0 ? null : never); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                why = $"the creations-out folder {project.CreationsOut} can't be checked ({e.Message})";
            }
        }
        if (why != null)
        {
            stdout.WriteLine($"RESULT: CANNOT PACK ({why}). Fix: in {project.File}, set \"creationsOut\" to a folder of its own, for example \"creations-out\".");
            return ExitCodes.BadInput;
        }
        foreach (var path in files)
        {
            if (DeleteOld(path, stdout) is { } code) return code;
            stdout.WriteLine($"deleted  {path} (from an earlier build; this build makes no Creations archives)");
        }
        return null;
    }

    static int? DeleteOld(string ba2, TextWriter stdout)
    {
        try
        {
            if (File.Exists(ba2))
            {
                File.Delete(ba2);
                Log.Write($"deleted the old {ba2}");
            }
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            stdout.WriteLine($"RESULT: CANNOT PACK (the old {ba2} can't be deleted: {e.Message}). Fix: close the game, the Creation Kit or any program using it, and run again.");
            return ExitCodes.BadInput;
        }
    }

    /// <summary>Runs Archive2 for one BA2. Null when it worked, else the exit code to return.</summary>
    static int? Archive(SfModProject project, string archive2, Job job, IHostEnv env, TextWriter stdout)
    {
        if (DeleteOld(job.Ba2, stdout) is { } code) return code;
        var line = CommandLine(job.Source, job.Root, job.Ba2, job.Format, job.Compression);
        Log.Write($"archive2: in {project.Folder}: \"{archive2}\" {line}");
        var r = env.Exec(archive2, line, project.Folder, Timeout);
        Log.Write($"archive2 exit code {r.ExitCode}{(r.Why != null ? $" ({r.Why})" : "")}; output:\n{r.Output.TrimEnd()}");
        string? failed = r.Why != null ? $"Archive2 couldn't be run: {r.Why}"
            : r.ExitCode != 0 ? $"Archive2 exited with code {r.ExitCode}"
            : !File.Exists(job.Ba2) ? $"Archive2 said OK but wrote no {job.Ba2}"
            : null;
        if (failed != null)
        {
            stdout.WriteLine($"FAILED   {Path.GetFileName(job.Ba2)}: {failed}");
            Common.ShowLastLines(stdout, r.Output);
            stdout.WriteLine($"RESULT: FAILED, {job.Ba2} wasn't made. Fix: check the error above; close the game or the Creation Kit if either has the archive open, and run again.");
            return ExitCodes.Failed;
        }
        stdout.WriteLine($"packed   {job.Ba2} ({new FileInfo(job.Ba2).Length} bytes)");
        return null;
    }
}
