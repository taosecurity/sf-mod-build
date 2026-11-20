// sf-mod-build pack [sfmod.json]: packs the compiled scripts (<stage>/Scripts) into "<plugin> - Main.ba2" in dist with
// the Creation Kit's own Archive2. With "xbox": true it also makes the Creations upload set: creations-out is emptied,
// dist's files are copied into it, and "<plugin> - Main_xbox.ba2" is packed there with -compression=XBox.
// Exit 0 when every archive was made (or there is nothing to pack), 1 when Archive2 failed, 2 bad input or setup.
namespace SfModBuild;

internal static class Pack
{
    public const string Usage =
        "usage: sf-mod-build pack [<sfmod.json or its folder>] [--game <game folder>] [--verbose] [--log <file>]\n" +
        "  packs the compiled scripts into \"<plugin> - Main.ba2\" in dist with the Creation Kit's Archive2;\n" +
        "  with \"xbox\": true also makes creations-out (dist's files plus \"<plugin> - Main_xbox.ba2\")";

    public static readonly ArgSpec Spec = new(new[] { "--game" }, Array.Empty<string>(), MaxPositionals: 1);

    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);

    public static int Run(ParsedArgs p, IHostEnv env, TextWriter stdout)
    {
        var project = Common.LoadProject(p, stdout, "CANNOT PACK");
        if (project == null) return ExitCodes.BadInput;
        if (project.Scripts == null)
        {
            stdout.WriteLine("RESULT: OK, nothing to pack (the project has no scripts)");
            return ExitCodes.Ok;
        }
        var scripts = Path.Combine(project.Stage, "Scripts");
        if (!Directory.Exists(scripts) || !Directory.EnumerateFiles(scripts, "*.pex", SearchOption.AllDirectories).Any())
        {
            stdout.WriteLine($"RESULT: CANNOT PACK (there are no compiled scripts in {scripts}. Fix: run sf-mod-build compile first).");
            return ExitCodes.BadInput;
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

    /// <summary>Archive2's command line:
    /// "&lt;stage&gt;\Scripts" "-c=&lt;ba2&gt;" "-r=&lt;stage&gt;" [-compression=XBox] -q.</summary>
    public static string CommandLine(string stage, string ba2, bool xbox)
    {
        var s = Path.TrimEndingDirectorySeparator(Path.GetFullPath(stage));
        var line = $"{Common.Quote(Path.Combine(s, "Scripts"))} {Common.Quote("-c=" + Path.GetFullPath(ba2))} {Common.Quote("-r=" + s)}";
        if (xbox) line += " -compression=XBox";
        return line + " -q";
    }

    /// <summary>Why emptying creations-out would be unsafe, or null.</summary>
    public static string? UnsafeCreationsOut(SfModProject project, string gameFolder)
    {
        var keep = new List<string> { project.Folder, project.Stage, project.Dist, Path.GetDirectoryName(project.BuilderProject)! };
        if (project.Scripts != null) keep.AddRange(project.Scripts.Sources);
        return Common.UnsafeToEmpty("creations-out", project.CreationsOut, project.Folder, keep, new[] { gameFolder });
    }

    public static int PackAll(SfModProject project, string gameFolder, string archive2, IHostEnv env, TextWriter stdout)
    {
        string? unsafeWhy = project.Xbox ? UnsafeCreationsOut(project, gameFolder) : null;
        if (unsafeWhy != null)
        {
            stdout.WriteLine($"RESULT: CANNOT PACK ({unsafeWhy}). Fix: in {project.File}, set \"creationsOut\" to a folder of its own, for example \"creations-out\".");
            return ExitCodes.BadInput;
        }
        try { Directory.CreateDirectory(project.Dist); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            stdout.WriteLine($"RESULT: CANNOT PACK (the dist folder {project.Dist} can't be made: {e.Message}).");
            return ExitCodes.BadInput;
        }
        var pc = Path.Combine(project.Dist, project.Ba2Name);
        if (Archive(project, archive2, pc, xbox: false, env, stdout) is { } code) return code;

        if (!project.Xbox)
        {
            stdout.WriteLine($"RESULT: OK, {pc} made");
            return ExitCodes.Ok;
        }
        if (Common.Empty(project.CreationsOut) is { } emptyError)
        {
            stdout.WriteLine($"RESULT: CANNOT PACK ({emptyError}).");
            return ExitCodes.BadInput;
        }
        try
        {
            // dist's own files (the plugin, the PC archive, anything else the builder wrote): the same ones the zip
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
        var xb = Path.Combine(project.CreationsOut, project.XboxBa2Name);
        if (Archive(project, archive2, xb, xbox: true, env, stdout) is { } xcode) return xcode;
        stdout.WriteLine($"RESULT: OK, {pc} and the Creations set in {project.CreationsOut} made");
        return ExitCodes.Ok;
    }

    /// <summary>Runs Archive2 for one BA2. Null when it worked, else the exit code to return.</summary>
    static int? Archive(SfModProject project, string archive2, string ba2, bool xbox, IHostEnv env, TextWriter stdout)
    {
        try { if (File.Exists(ba2)) File.Delete(ba2); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            stdout.WriteLine($"RESULT: CANNOT PACK (the old {ba2} can't be deleted: {e.Message}). Fix: close the game, the Creation Kit or any program using it, and run again.");
            return ExitCodes.BadInput;
        }
        var line = CommandLine(project.Stage, ba2, xbox);
        Log.Write($"archive2: in {project.Folder}: \"{archive2}\" {line}");
        var r = env.Exec(archive2, line, project.Folder, Timeout);
        Log.Write($"archive2 exit code {r.ExitCode}{(r.Why != null ? $" ({r.Why})" : "")}; output:\n{r.Output.TrimEnd()}");
        string? failed = r.Why != null ? $"Archive2 couldn't be run: {r.Why}"
            : r.ExitCode != 0 ? $"Archive2 exited with code {r.ExitCode}"
            : !File.Exists(ba2) ? $"Archive2 said OK but wrote no {ba2}"
            : null;
        if (failed != null)
        {
            stdout.WriteLine($"FAILED   {Path.GetFileName(ba2)}: {failed}");
            Common.ShowLastLines(stdout, r.Output);
            stdout.WriteLine($"RESULT: FAILED, {ba2} wasn't made. Fix: check the error above; close the game or the Creation Kit if either has the archive open, and run again.");
            return ExitCodes.Failed;
        }
        stdout.WriteLine($"packed   {ba2} ({new FileInfo(ba2).Length} bytes)");
        return null;
    }
}
