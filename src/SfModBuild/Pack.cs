// sf-mod-build pack [sfmod.json]: packs the mod's files into archives named after the plugin, with the Creation Kit's
// own Archive2:
//   "<plugin> - Main.ba2"      the compiled scripts (<stage>/Scripts) and the general loose files from assets.source
//                              (meshes, materials, terrain, sounds ...); packed with -compression=None when it holds a
//                              .wem or .ffxanim, which play silent when stored compressed
//   "<plugin> - Textures.ba2"  the loose .dds files from assets.source, packed with -format=DDS
// With "xbox": true it also makes the Creations upload set: creations-out is emptied, dist's files are copied into it,
// and "<plugin> - Main_xbox.ba2" and "<plugin> - Textures_xbox.ba2" are packed there with -compression=XBox. The Xbox
// texture archive is always a separate archive (never the PC one renamed by hand); it takes assets.xboxTextures when
// given (Xbox-format exports), else the PC textures.
// A scripts-only mod is packed straight from <stage>/Scripts, as before loose files were supported. With loose files,
// each archive's files are first copied into <stage>/pack/<archive>, so every archive has one root.
// Exit 0 when every archive was made (or there is nothing to pack), 1 when Archive2 failed, 2 bad input or setup.
namespace SfModBuild;

internal static class Pack
{
    public const string Usage =
        "usage: sf-mod-build pack [<sfmod.json or its folder>] [--game <game folder>] [--verbose] [--log <file>]\n" +
        "  packs the compiled scripts and the loose files (assets) into \"<plugin> - Main.ba2\" and \"<plugin> - Textures.ba2\"\n" +
        "  in dist with the Creation Kit's Archive2; with \"xbox\": true also makes creations-out (dist's files plus the\n" +
        "  \"_xbox\" archives)";

    public static readonly ArgSpec Spec = new(new[] { "--game" }, Array.Empty<string>(), MaxPositionals: 1);

    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);

    public static int Run(ParsedArgs p, IHostEnv env, TextWriter stdout)
    {
        var project = Common.LoadProject(p, stdout, "CANNOT PACK");
        if (project == null) return ExitCodes.BadInput;
        if (!project.HasMainArchive && !project.HasTexturesArchive)
        {
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
        var game = Common.FindGame(p.Get("--game"), env, out var error);
        if (game == null)
        {
            stdout.WriteLine($"RESULT: CANNOT PACK ({error})");
            return ExitCodes.BadInput;
        }
        var archive2 = Path.Combine(game.Folder, "Tools", "Archive2", "Archive2.exe");
        if (!File.Exists(archive2))
        {
            stdout.WriteLine($"RESULT: CANNOT PACK (Archive2 isn't at {archive2}. {Doctor.CkHowTo})");
            return ExitCodes.BadInput;
        }
        return PackAll(project, game.Folder, archive2, env, stdout);
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

    /// <summary>Why emptying creations-out would be unsafe, or null.</summary>
    public static string? UnsafeCreationsOut(SfModProject project, string gameFolder) =>
        Common.UnsafeToEmpty("creations-out", project.CreationsOut, project.Folder, Keep(project), new[] { gameFolder });

    /// <summary>The folders no emptied folder may be or hold: the mod, the stage, dist, the builder, the script
    /// sources and the assets, plus any given.</summary>
    static List<string> Keep(SfModProject project, params string[] more)
    {
        var keep = new List<string> { project.Folder, project.Stage, project.Dist, Path.GetDirectoryName(project.BuilderProject)! };
        keep.AddRange(more);
        if (project.Scripts != null) keep.AddRange(project.Scripts.Sources);
        if (project.Assets != null)
        {
            keep.Add(project.Assets.Source);
            if (project.Assets.XboxTextures != null) keep.Add(project.Assets.XboxTextures);
        }
        return keep;
    }

    /// <summary>The folder loose files are copied into before packing: &lt;stage&gt;/pack.</summary>
    public static string PackFolder(SfModProject project) => Path.Combine(project.Stage, "pack");

    /// <summary>One archive to make: its file, its source folder and root, and Archive2's format and compression.</summary>
    sealed record Job(string Ba2, string Source, string Root, string? Format, string? Compression);

    public static int PackAll(SfModProject project, string gameFolder, string archive2, IHostEnv env, TextWriter stdout)
    {
        string? unsafeWhy = project.Xbox ? UnsafeCreationsOut(project, gameFolder) : null;
        if (unsafeWhy != null)
        {
            stdout.WriteLine($"RESULT: CANNOT PACK ({unsafeWhy}). Fix: in {project.File}, set \"creationsOut\" to a folder of its own, for example \"creations-out\".");
            return ExitCodes.BadInput;
        }
        if (project.Assets != null && AssetsInGame(project.Assets, gameFolder) is { } inGame)
        {
            stdout.WriteLine($"RESULT: CANNOT PACK ({inGame}). Fix: copy the mod's own loose files into a folder of the mod, for example \"Assets\", and point assets.source at it.");
            return ExitCodes.BadInput;
        }
        try { Directory.CreateDirectory(project.Dist); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            stdout.WriteLine($"RESULT: CANNOT PACK (the dist folder {project.Dist} can't be made: {e.Message}).");
            return ExitCodes.BadInput;
        }
        // An archive this build doesn't make any more (the mod's textures were removed) must not stay in dist and
        // reach the zip.
        foreach (var stale in new[] { project.Ba2Name, project.TexturesBa2Name }.Except(project.ArchiveNames))
            if (DeleteOld(Path.Combine(project.Dist, stale), stdout) is { } code) return code;

        // Where each archive's files come from
        string? mainSource = null, mainRoot = null, texSource = null, xboxTexSource = null;
        var assets = project.Assets;
        var staged = assets != null && (assets.General.Count > 0 || assets.Textures.Count > 0);
        if (project.HasMainArchive && !(assets is { General.Count: > 0 }))
        {
            mainRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(project.Stage));
            mainSource = Path.Combine(mainRoot, "Scripts");
        }
        if (staged)
        {
            var pack = PackFolder(project);
            var why = Common.UnsafeToEmpty("pack", pack, project.Folder, Keep(project, project.CreationsOut), new[] { gameFolder })
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
        var mainCompression = assets is { UncompressedMain: true } ? "None" : null;
        if (mainCompression != null)
            stdout.WriteLine($"note     \"{project.Ba2Name}\" holds .wem or .ffxanim files, which play silent when stored compressed, so it is packed with -compression=None");

        var pcJobs = new List<Job>();
        if (project.HasMainArchive) pcJobs.Add(new Job(Path.Combine(project.Dist, project.Ba2Name), mainSource!, mainRoot!, null, mainCompression));
        if (project.HasTexturesArchive) pcJobs.Add(new Job(Path.Combine(project.Dist, project.TexturesBa2Name), texSource!, texSource!, "DDS", null));
        foreach (var job in pcJobs)
            if (Archive(project, archive2, job, env, stdout) is { } code) return code;

        var made = pcJobs.Count == 1 ? pcJobs[0].Ba2
            : $"{string.Join(" and ", pcJobs.Select(j => $"\"{Path.GetFileName(j.Ba2)}\""))} made in {project.Dist}";
        if (!project.Xbox)
        {
            stdout.WriteLine(pcJobs.Count == 1 ? $"RESULT: OK, {made} made" : $"RESULT: OK, {made}");
            return ExitCodes.Ok;
        }
        if (Common.Empty(project.CreationsOut) is { } emptyError)
        {
            stdout.WriteLine($"RESULT: CANNOT PACK ({emptyError}).");
            return ExitCodes.BadInput;
        }
        try
        {
            // dist's own files (the plugin, the PC archives, anything else the builder wrote): the same ones the zip
            // takes, so never zips, Papyrus source, half-written zips or the log
            foreach (var f in Directory.GetFiles(project.Dist).Order(StringComparer.OrdinalIgnoreCase))
            {
                if (!Common.Shippable(f)) continue;
                File.Copy(f, Path.Combine(project.CreationsOut, Path.GetFileName(f)));
                Log.Write($"copied {f} into {project.CreationsOut}");
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            stdout.WriteLine($"RESULT: CANNOT PACK (dist's files couldn't be copied into {project.CreationsOut}: {e.Message}).");
            return ExitCodes.BadInput;
        }
        var xboxJobs = new List<Job>();
        if (project.HasMainArchive)
            xboxJobs.Add(new Job(Path.Combine(project.CreationsOut, project.XboxBa2Name), mainSource!, mainRoot!, null, mainCompression ?? "XBox"));
        if (project.HasTexturesArchive)
            xboxJobs.Add(new Job(Path.Combine(project.CreationsOut, project.XboxTexturesBa2Name), xboxTexSource ?? texSource!, xboxTexSource ?? texSource!, "DDS", "XBox"));
        foreach (var job in xboxJobs)
            if (Archive(project, archive2, job, env, stdout) is { } code) return code;
        if (project.HasTexturesArchive && assets!.XboxTextures == null)
            stdout.WriteLine($"note     \"{project.XboxTexturesBa2Name}\" holds the PC-format textures (no assets.xboxTextures given); a console test is the only proof they look right on Xbox");
        stdout.WriteLine(pcJobs.Count == 1 ? $"RESULT: OK, {made} and the Creations set in {project.CreationsOut} made" : $"RESULT: OK, {made}, and the Creations set made in {project.CreationsOut}");
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

    /// <summary>Copies each archive's files into its own folder under pack: main (compiled scripts plus the general
    /// loose files), textures and textures-xbox. Null when done, else why it failed. A source is null when that
    /// archive has no files.</summary>
    static string? Stage(SfModProject project, string pack, out string? main, out string? textures, out string? xboxTextures, TextWriter stdout)
    {
        main = textures = xboxTextures = null;
        var assets = project.Assets!;
        try
        {
            if (assets.General.Count > 0)
            {
                main = Path.Combine(pack, "main");
                var taken = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (project.Scripts != null)
                {
                    var compiled = Path.Combine(project.Stage, "Scripts");
                    foreach (var pex in Directory.GetFiles(compiled, "*", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase))
                    {
                        var rel = Path.Combine("Scripts", Path.GetRelativePath(compiled, pex));
                        Copy(pex, Path.Combine(main, rel));
                        taken[rel] = pex;
                    }
                }
                foreach (var f in assets.General)
                {
                    if (taken.TryGetValue(f.Relative, out var other))
                        return $"{f.Full} in assets.source would be {f.Relative} in the archive, which is also the compiled script {other}. Fix: remove the loose copy from the assets folder; the script is compiled from its source";
                    Copy(f.Full, Path.Combine(main, f.Relative));
                    taken[f.Relative] = f.Full;
                }
            }
            if (assets.Textures.Count > 0)
            {
                textures = Path.Combine(pack, "textures");
                foreach (var f in assets.Textures) Copy(f.Full, Path.Combine(textures, f.Relative));
            }
            if (project.Xbox && assets.XboxTextures != null && assets.Textures.Count > 0)
            {
                xboxTextures = Path.Combine(pack, "textures-xbox");
                foreach (var f in assets.XboxTextureFiles) Copy(f.Full, Path.Combine(xboxTextures, f.Relative));
                Directory.CreateDirectory(xboxTextures); // an empty Xbox folder still gives Archive2 a folder to read
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return $"the loose files couldn't be copied into {pack}: {e.Message}. Fix: check that every file in the assets folder is there and readable, and run again";
        }
        stdout.WriteLine($"staged   {assets.General.Count} general and {assets.Textures.Count} texture file(s) from {assets.Source}");
        return null;
    }

    static void Copy(string from, string to)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        File.Copy(from, to);
        Log.Write($"staged {from} -> {to}");
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
