// sf-mod-build plugin [sfmod.json]: empties dist, then makes the plugin there with the project's builder: a .NET
// project (dotnet run -c Release --project <builder> -- <args>), any command (builder.command, then <args>), or a
// ready plugin copied in (builder.prebuilt). Placeholders in the arguments are filled in first.
// Exit 0 when the plugin is there, 1 when the builder failed, 2 bad input or setup.
namespace SfModBuild;

internal static class Plugin
{
    public const string Usage =
        "usage: sf-mod-build plugin [<sfmod.json or its folder>] [--game <game folder>] [--verbose] [--log <file>]\n" +
        "  makes the plugin in dist (dist is emptied first) with the mod's builder: a .NET project (dotnet run -c Release),\n" +
        "  a command from builder.command, or a ready plugin copied from builder.prebuilt; then runs steps.afterPlugin";

    public static readonly ArgSpec Spec = new(new[] { "--game" }, Array.Empty<string>(), MaxPositionals: 1);

    /// <summary>The first run restores packages and builds the builder, which can take a while.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(20);

    /// <summary>Makes the plugin, then runs steps.afterPlugin.</summary>
    public static int Run(ParsedArgs p, IHostEnv env, TextWriter stdout) =>
        Steps.After("afterPlugin", p, env, stdout, "CANNOT BUILD THE PLUGIN", o => Make(p, env, o));

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
        return BuildPlugin(project, game.Folder, exe, env, stdout);
    }

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

    /// <summary>Arguments joined into one command line, each quoted as needed.</summary>
    public static string Join(IEnumerable<string> args) => string.Join(" ", args.Select(Common.Quote));

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
        if (project.Scripts != null) keep.AddRange(project.Scripts.Sources);
        return Common.UnsafeToEmpty("dist", project.Dist, project.Folder, keep, gameFolders);
    }

    /// <summary>Empties dist and makes the plugin there. gameFolder and exe (dotnet, or builder.command's program)
    /// are null only for a prebuilt plugin.</summary>
    public static int BuildPlugin(SfModProject project, string? gameFolder, string? exe, IHostEnv env, TextWriter stdout)
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
        if (Common.Empty(project.Dist) is { } emptyError)
        {
            stdout.WriteLine($"RESULT: CANNOT BUILD THE PLUGIN ({emptyError}).");
            return ExitCodes.BadInput;
        }
        var plugin = Path.Combine(project.Dist, project.Plugin);
        if (project.BuilderPrebuilt is { } prebuilt)
        {
            Log.Write($"builder: copying the prebuilt plugin {prebuilt} to {plugin}");
            try
            {
                Directory.CreateDirectory(project.Dist);
                File.Copy(prebuilt, plugin, overwrite: true);
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
            line = Join(args!);
            label = $"{Path.GetFileName(exe!)} (builder.command)";
        }
        stdout.WriteLine($"running the builder: {label}");
        Log.Write($"builder ({project.BuilderKind}): in {project.Folder}: \"{exe}\" {line}");
        var r = env.Exec(exe!, line, project.Folder, Timeout);
        Log.Write($"builder exit code {r.ExitCode}{(r.Why != null ? $" ({r.Why})" : "")}; output:\n{r.Output.TrimEnd()}");
        var what = project.BuilderProject != null ? "dotnet" : $"builder.command's program {exe}";
        string? failed = r.Why != null ? $"{what} couldn't be run: {r.Why}"
            : r.ExitCode != 0 ? $"the builder exited with code {r.ExitCode}"
            : !File.Exists(plugin) ? $"the builder said OK but wrote no {plugin}"
            : null;
        if (failed != null)
        {
            stdout.WriteLine($"FAILED   {project.Plugin}: {failed}");
            Common.ShowLastLines(stdout, r.Output, 20);
            var fix = r.ExitCode == 0 && r.Why == null
                ? $"check that builder.args in {project.File} pass the output folder where the builder expects it ({{dist}}), and that the builder writes {project.Plugin}."
                : "fix the error shown above (the full output is in the log: --log <file>) and run again.";
            stdout.WriteLine($"RESULT: FAILED, the plugin wasn't built. Fix: {fix}");
            return ExitCodes.Failed;
        }
        stdout.WriteLine($"built    {plugin} ({new FileInfo(plugin).Length} bytes)");
        stdout.WriteLine($"RESULT: OK, {project.Plugin} built into {project.Dist}");
        return ExitCodes.Ok;
    }
}
