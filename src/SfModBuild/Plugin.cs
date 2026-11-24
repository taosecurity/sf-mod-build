// sf-mod-build plugin [sfmod.json]: deletes what earlier builds made in dist (it refuses, deleting nothing, when dist
// holds anything it didn't make), then makes the plugin there with the project's builder: a .NET
// project (dotnet run -c Release --project <builder> -- <args>), any command (builder.command, then <args>), or a
// ready plugin copied in (builder.prebuilt). Placeholders in the arguments are filled in first. Every plugin in parts
// must be in dist when the builder ends.
// Exit 0 when the plugin is there, 1 when the builder failed, 2 bad input or setup.
namespace SfModBuild;

internal static class Plugin
{
    public const string Usage =
        "usage: sf-mod-build plugin [<sfmod.json or its folder>] [--game <game folder>] [--verbose] [--log <file>]\n" +
        "  makes the plugin in dist (first deleting what earlier builds made there; it stops if dist holds anything else) with the mod's builder: a .NET project (dotnet run -c Release),\n" +
        "  a command from builder.command, or a ready plugin copied from builder.prebuilt; checks that every plugin in\n" +
        "  parts is there too; then runs steps.afterPlugin";

    public static readonly ArgSpec Spec = new(new[] { "--game" }, Array.Empty<string>(), MaxPositionals: 1);

    /// <summary>The first run restores packages and builds the builder, which can take a while.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(20);

    /// <summary>Makes the plugin, then runs steps.afterPlugin. What the steps add to dist joins the made list.</summary>
    public static int Run(ParsedArgs p, IHostEnv env, TextWriter stdout)
    {
        string? dist = null;
        HashSet<string>? before = null;
        var code = Steps.After("afterPlugin", p, env, stdout, "CANNOT BUILD THE PLUGIN", o =>
        {
            var made = Make(p, env, o);
            dist = Common.LoadProject(p, TextWriter.Null, "")?.Dist;
            if (dist != null) before = Common.DistEntries(dist);
            return made;
        });
        if (dist != null && before != null && Common.DistEntries(dist) is { } after)
            Common.RecordMade(dist, after.Where(n => !before.Contains(n)), add: true);
        return code;
    }

    static int Make(ParsedArgs p, IHostEnv env, TextWriter stdout)
    {
        var project = Common.LoadProject(p, stdout, "CANNOT BUILD THE PLUGIN");
        if (project == null) return ExitCodes.BadInput;
        // a ready plugin is only copied: it needs neither the game nor .NET
        if (project.BuilderPrebuilt != null) return BuildPlugin(project, null, null, env, stdout);
        var game = Common.FindGame(p.Get("--game"), env, out var error);
        if (game == null)
        {
            stdout.WriteLine($"RESULT: CANNOT BUILD THE PLUGIN ({error})");
            return ExitCodes.BadInput;
        }
        var data = Path.Combine(game.Folder, "Data");
        if (!Directory.Exists(data))
        {
            stdout.WriteLine($"RESULT: CANNOT BUILD THE PLUGIN (the game has no Data folder at {data}. Fix: start the game once, or give the right folder with --game).");
            return ExitCodes.BadInput;
        }
        string? exe;
        if (project.BuilderProject != null)
        {
            exe = Common.FindDotnet(env);
            if (exe == null)
            {
                stdout.WriteLine("RESULT: CANNOT BUILD THE PLUGIN (dotnet wasn't found on PATH or in DOTNET_ROOT. Fix: install the .NET 9 SDK from https://dotnet.microsoft.com/download/dotnet/9.0 and open a new terminal).");
                return ExitCodes.BadInput;
            }
        }
        else
        {
            var program = project.BuilderCommand![0];
            exe = Path.IsPathRooted(program) ? program : PathSearch.Find(env, program);
            if (exe == null)
            {
                stdout.WriteLine($"RESULT: CANNOT BUILD THE PLUGIN (builder.command's program \"{program}\" wasn't found on PATH. Fix: install it or add its folder to PATH, or give its path relative to {project.File}).");
                return ExitCodes.BadInput;
            }
        }
        return BuildPlugin(project, game.Folder, exe, env, stdout, GapChecker(p, project, env, stdout));
    }

    /// <summary>The checker BuildKit's gap check should use: build's --checker when it names a file, else check.checker
    /// in sfmod.json, when its --version says 0.15.0 or later (the first with the gap command); null leaves BuildKit
    /// to its own search (BUILDKIT_SFMODCHECK, then PATH). A wrong --checker is reported by the check step.</summary>
    public static string? GapChecker(ParsedArgs p, SfModProject project, IHostEnv env, TextWriter stdout)
    {
        string? chosen = null;
        if (p.Get("--checker") is { } given)
        {
            try
            {
                var full = Path.GetFullPath(given.Trim().Trim('"'));
                if (File.Exists(full)) chosen = full;
            }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { }
        }
        chosen ??= project.Checker;
        if (chosen == null) return null;
        var output = env.Run(chosen, "--version", out var why);
        var m = output == null ? null : System.Text.RegularExpressions.Regex.Match(output, @"(\d+)\.(\d+)\.(\d+)");
        if (m is { Success: true } && new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value)) >= MinGapChecker)
            return chosen;
        var said = output == null ? $"its --version failed: {why}" : $"its --version said \"{output.Trim()}\"";
        Log.Write($"builder: not giving {chosen} to BuildKit's gap check ({said})");
        stdout.WriteLine($"note: {Path.GetFileName(chosen)} doesn't look like sf-mod-check {MinGapChecker} or later (the first with the gap command), so BuildKit's gap check looks for sf-mod-check itself (BUILDKIT_SFMODCHECK, then PATH).");
        return null;
    }

    /// <summary>The first sf-mod-check with the gap command.</summary>
    static readonly Version MinGapChecker = new(0, 15, 0);

    /// <summary>The name of the variable BuildKit reads for the sf-mod-check path.</summary>
    public const string GapCheckerVariable = "BUILDKIT_SFMODCHECK";

    /// <summary>The placeholder values: full paths without a trailing separator, and the version.</summary>
    public static Dictionary<string, string> Values(SfModProject project, string? gameFolder)
    {
        static string Full(string x) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(x));
        var values = new Dictionary<string, string>
        {
            ["dist"] = Full(project.Dist),
            ["stage"] = Full(project.Stage),
            ["version"] = project.Version,
        };
        if (gameFolder != null) values["data"] = Full(Path.Combine(gameFolder, "Data"));
        if (project.Scripts is { Sources.Count: > 0 } s) values["scripts"] = Full(s.Sources[0]);
        return values;
    }

    /// <summary>The builder's arguments, expanded: with builder.command, its arguments after the program come first,
    /// then builder.args. Null (with the reason) when one uses {scripts} and the project has no scripts.</summary>
    public static List<string>? BuilderArgs(SfModProject project, string gameFolder, out string? error)
    {
        error = null;
        var values = Values(project, gameFolder);
        var list = new List<string>();
        var fields = (project.BuilderCommand ?? Array.Empty<string>()).Select((a, i) => (a, $"builder.command[{i}]")).Skip(1)
            .Concat(project.BuilderArgs.Select((a, i) => (a, $"builder.args[{i}]")));
        foreach (var (a, field) in fields)
        {
            if (a.Contains("{scripts}") && !values.ContainsKey("scripts"))
            {
                error = $"{field} uses {{scripts}}, but {project.File} has no \"scripts\" section. Fix: add the scripts section, or take {{scripts}} out of {field[..field.IndexOf('[')]}.";
                return null;
            }
            list.Add(SfModProject.Expand(a, values));
        }
        return list;
    }

    /// <summary>The dotnet command line: run -c Release --project "&lt;csproj&gt;" -- &lt;args&gt;, each quoted as needed.</summary>
    public static string CommandLine(string builderProject, IEnumerable<string> args)
    {
        var line = $"run -c Release --project {Common.Quote(Path.GetFullPath(builderProject))} --";
        foreach (var a in args) line += " " + Common.Quote(a);
        return line;
    }

    /// <summary>Arguments joined into one command line, each quoted as needed (for cmd.exe's rules too when the
    /// program is a .cmd or .bat file).</summary>
    public static string Join(IEnumerable<string> args, string? program = null)
    {
        var batch = program != null && (program.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || program.EndsWith(".bat", StringComparison.OrdinalIgnoreCase));
        return string.Join(" ", args.Select(a => Common.Quote(a, batch)));
    }

    /// <summary>What a build would run, with the placeholders still in (validate's "runs:" line), or null for a
    /// prebuilt plugin, which is only copied.</summary>
    public static string? Shown(SfModProject project) =>
        project.BuilderProject != null ? "dotnet " + CommandLine(project.BuilderProject, project.BuilderArgs)
        : project.BuilderCommand != null ? Join(project.BuilderCommand.Concat(project.BuilderArgs))
        : null;

    /// <summary>Why emptying dist would be unsafe, or null.</summary>
    public static string? UnsafeDist(SfModProject project, IEnumerable<string> gameFolders)
    {
        var keep = new List<string> { project.Folder, project.Stage };
        keep.AddRange(project.BuilderFolders);
        keep.AddRange(project.AllScriptSources);
        return Common.UnsafeToEmpty("dist", project.Dist, project.Folder, keep, gameFolders);
    }

    /// <summary>Empties dist and makes the plugin there. gameFolder and exe (dotnet, or builder.command's program)
    /// are null only for a prebuilt plugin.</summary>
    public static int BuildPlugin(SfModProject project, string? gameFolder, string? exe, IHostEnv env, TextWriter stdout, string? gapChecker = null)
    {
        List<string>? args = null;
        if (project.BuilderPrebuilt == null)
        {
            args = BuilderArgs(project, gameFolder!, out var error);
            if (args == null)
            {
                stdout.WriteLine($"RESULT: CANNOT BUILD THE PLUGIN ({error})");
                return ExitCodes.BadInput;
            }
        }
        if (UnsafeDist(project, Common.GameFolders(gameFolder, env)) is { } why)
        {
            stdout.WriteLine($"RESULT: CANNOT BUILD THE PLUGIN ({why}). Fix: in {project.File}, set \"dist\" to a folder of its own, for example \"dist\".");
            return ExitCodes.BadInput;
        }
        if (Common.CleanDist(project) is { } cleanError)
        {
            stdout.WriteLine($"RESULT: CANNOT BUILD THE PLUGIN ({cleanError}).");
            return ExitCodes.BadInput;
        }
        // dist now holds only zips (and the zip folder), so everything else there when the builder ends (even when it
        // fails) is the build's; the zip folder never joins the list, so its zips can't be deleted later
        try { return MakeIn(project, exe, args, env, stdout, gapChecker); }
        finally
        {
            Common.RecordMade(project.Dist, (Common.DistEntries(project.Dist) ?? new HashSet<string>())
                .Where(n => !Common.HoldsZipFolder(project, Path.Combine(project.Dist, n))), add: false);
        }
    }

    /// <summary>BuildKit's gap check prints its lines through the builder; show its RESULT line, any ERROR lines and a "NOT RUN" line in the
    /// normal output (the rest stays with --verbose and --log). A builder that runs no gap check prints nothing here.</summary>
    public static void ShowGapResult(TextWriter stdout, string output, bool builderSucceeded = true)
    {
        var failed = false;
        foreach (var l in Common.LastLines(output, int.MaxValue))
            if (GapLine(l) is { } t)
            {
                stdout.WriteLine($"         {t}");
                if (t.Substring("gap check:".Length).TrimStart().StartsWith("RESULT: FAIL", StringComparison.Ordinal)) failed = true;
            }
        // a builder that sets FailOnGapErrors = false still exits 0, so the step's RESULT: OK must not hide the failure
        if (failed && builderSucceeded) stdout.WriteLine("WARNING  the gap check found errors (the ERROR lines above). Fix them before you release the mod.");
    }

    /// <summary>The line as shown, when it is a gap check RESULT, ERROR or NOT RUN line (with or without a "log: "
    /// prefix); null for any other line.</summary>
    public static string? GapLine(string line)
    {
        var t = line.Trim();
        if (t.StartsWith("log: ", StringComparison.Ordinal)) t = t.Substring(5).Trim();
        const string prefix = "gap check:";
        if (!t.StartsWith(prefix, StringComparison.Ordinal)) return null;
        var rest = t.Substring(prefix.Length).TrimStart();
        return rest.StartsWith("RESULT", StringComparison.Ordinal) || rest.StartsWith("ERROR", StringComparison.Ordinal)
            || rest.StartsWith("NOT RUN", StringComparison.Ordinal) ? t : null;
    }

    static int MakeIn(SfModProject project, string? exe, List<string>? args, IHostEnv env, TextWriter stdout, string? gapChecker)
    {
        var plugin = Path.Combine(project.Dist, project.Plugin);
        if (project.BuilderPrebuilt is { } prebuilt)
        {
            Log.Write($"builder: copying the prebuilt plugin {prebuilt} to {plugin}");
            try
            {
                Directory.CreateDirectory(project.Dist);
                Common.CopyFile(prebuilt, plugin, overwrite: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                stdout.WriteLine($"FAILED   {project.Plugin}: {prebuilt} couldn't be copied ({e.Message})");
                stdout.WriteLine($"RESULT: FAILED, the plugin wasn't built. Fix: check that builder.prebuilt in {project.File} names a file this account can read.");
                return ExitCodes.Failed;
            }
            stdout.WriteLine($"copied   {prebuilt} to {plugin} ({new FileInfo(plugin).Length} bytes)");
            stdout.WriteLine($"RESULT: OK, {project.Plugin} copied into {project.Dist}");
            return ExitCodes.Ok;
        }
        string line, label;
        if (project.BuilderProject != null)
        {
            line = CommandLine(project.BuilderProject, args!);
            label = $"{Path.GetFileName(project.BuilderProject)} (the first run restores and builds it, which can take a few minutes)";
        }
        else
        {
            line = Join(args!, exe);
            label = $"{Path.GetFileName(exe!)} (builder.command)";
        }
        stdout.WriteLine($"running the builder: {label}");
        Log.Write($"builder ({project.BuilderKind}): in {project.Folder}: \"{exe}\" {line}");
        Dictionary<string, string>? vars = null;
        if (gapChecker != null)
        {
            vars = new() { [GapCheckerVariable] = gapChecker };
            Log.Write($"builder: {GapCheckerVariable}={gapChecker} (for BuildKit's gap check)");
        }
        var r = env.Exec(exe!, line, project.Folder, Timeout, vars);
        Log.Write($"builder exit code {r.ExitCode}{(r.Why != null ? $" ({r.Why})" : "")}; output:\n{r.Output.TrimEnd()}");
        ShowGapResult(stdout, r.Output, r.Why == null && r.ExitCode == 0);
        var what = project.BuilderProject != null ? "dotnet" : $"builder.command's program {exe}";
        string? failed = r.Why != null ? $"{what} couldn't be run: {r.Why}"
            : r.ExitCode != 0 ? $"the builder exited with code {r.ExitCode}"
            : !File.Exists(plugin) ? $"the builder said OK but wrote no {plugin}"
            : null;
        if (failed != null)
        {
            stdout.WriteLine($"FAILED   {project.Plugin}: {failed}");
            // the gap lines are already shown above, so the tail leaves them out
            var tail = string.Join("\n", r.Output.Split('\n').Where(l => GapLine(l) == null));
            Common.ShowLastLines(stdout, tail, 20);
            var fix = r.ExitCode == 0 && r.Why == null
                ? $"check that builder.args in {project.File} pass the output folder where the builder expects it ({{dist}}), and that the builder writes {project.Plugin}."
                : "fix the error shown above (the full output is in the log: --log <file>) and run again.";
            stdout.WriteLine($"RESULT: FAILED, the plugin wasn't built. Fix: {fix}");
            return ExitCodes.Failed;
        }
        stdout.WriteLine($"built    {plugin} ({new FileInfo(plugin).Length} bytes)");
        var missing = project.Parts.Select(x => x.Plugin).Where(x => !File.Exists(Path.Combine(project.Dist, x))).ToList();
        foreach (var part in project.Parts.Where(x => !missing.Contains(x.Plugin)))
            stdout.WriteLine($"built    {Path.Combine(project.Dist, part.Plugin)} ({new FileInfo(Path.Combine(project.Dist, part.Plugin)).Length} bytes)");
        if (missing.Count > 0)
        {
            Log.Write($"parts missing from {project.Dist}: {string.Join(", ", missing)}");
            stdout.WriteLine($"RESULT: FAILED, the builder said OK but wrote no {string.Join(" and no ", missing)} in {project.Dist}, and parts in {project.File} lists {(missing.Count == 1 ? "it" : "them")}. Fix: make the builder write every part plugin into dist ({{dist}}), or take the plugin out of parts.");
            return ExitCodes.Failed;
        }
        if (project.Parts.Count > 0)
        {
            stdout.WriteLine($"RESULT: OK, {project.Plugin} and {project.Parts.Count} part plugin(s) built into {project.Dist}");
            return ExitCodes.Ok;
        }
        stdout.WriteLine($"RESULT: OK, {project.Plugin} built into {project.Dist}");
        return ExitCodes.Ok;
    }
}
