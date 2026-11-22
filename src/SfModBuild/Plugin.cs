// sf-mod-build plugin [sfmod.json]: runs the mod's Mutagen builder (dotnet run -c Release --project <builder> -- <args>)
// with the project's argument template filled in, after emptying dist. The builder writes the plugin into dist.
// Exit 0 when the builder succeeded and the plugin is there, 1 when it failed, 2 bad input or setup.
namespace SfModBuild;

internal static class Plugin
{
    public const string Usage =
        "usage: sf-mod-build plugin [<sfmod.json or its folder>] [--game <game folder>] [--verbose] [--log <file>]\n" +
        "  runs the mod's Mutagen builder (dotnet run -c Release) to write the plugin into dist (dist is emptied first)";

    public static readonly ArgSpec Spec = new(new[] { "--game" }, Array.Empty<string>(), MaxPositionals: 1);

    /// <summary>The first run restores packages and builds the builder, which can take a while.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(20);

    public static int Run(ParsedArgs p, IHostEnv env, TextWriter stdout)
    {
        var project = Common.LoadProject(p, stdout, "CANNOT BUILD THE PLUGIN");
        if (project == null) return ExitCodes.BadInput;
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
        var dotnet = Common.FindDotnet(env);
        if (dotnet == null)
        {
            stdout.WriteLine("RESULT: CANNOT BUILD THE PLUGIN (dotnet wasn't found on PATH or in DOTNET_ROOT. Fix: install the .NET 9 SDK from https://dotnet.microsoft.com/download/dotnet/9.0 and open a new terminal).");
            return ExitCodes.BadInput;
        }
        return BuildPlugin(project, game.Folder, dotnet, env, stdout);
    }

    /// <summary>The placeholder values: full paths without a trailing separator, and the version.</summary>
    public static Dictionary<string, string> Values(SfModProject project, string gameFolder)
    {
        static string Full(string x) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(x));
        var values = new Dictionary<string, string>
        {
            ["data"] = Full(Path.Combine(gameFolder, "Data")),
            ["dist"] = Full(project.Dist),
            ["stage"] = Full(project.Stage),
            ["version"] = project.Version,
        };
        if (project.Scripts is { Sources.Count: > 0 } s) values["scripts"] = Full(s.Sources[0]);
        return values;
    }

    /// <summary>The builder's arguments, expanded. Null (with the reason) when one uses {scripts} and the project has
    /// no scripts.</summary>
    public static List<string>? BuilderArgs(SfModProject project, string gameFolder, out string? error)
    {
        error = null;
        var values = Values(project, gameFolder);
        var list = new List<string>();
        for (var i = 0; i < project.BuilderArgs.Count; i++)
        {
            var a = project.BuilderArgs[i];
            if (a.Contains("{scripts}") && !values.ContainsKey("scripts"))
            {
                error = $"builder.args[{i}] uses {{scripts}}, but {project.File} has no \"scripts\" section. Fix: add the scripts section, or take {{scripts}} out of builder.args.";
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

    /// <summary>Why emptying dist would be unsafe, or null.</summary>
    public static string? UnsafeDist(SfModProject project, IEnumerable<string> gameFolders)
    {
        var keep = new List<string> { project.Folder, project.Stage, Path.GetDirectoryName(project.BuilderProject)! };
        if (project.Scripts != null) keep.AddRange(project.Scripts.Sources);
        return Common.UnsafeToEmpty("dist", project.Dist, project.Folder, keep, gameFolders);
    }

    public static int BuildPlugin(SfModProject project, string gameFolder, string dotnet, IHostEnv env, TextWriter stdout)
    {
        var args = BuilderArgs(project, gameFolder, out var error);
        if (args == null)
        {
            stdout.WriteLine($"RESULT: CANNOT BUILD THE PLUGIN ({error})");
            return ExitCodes.BadInput;
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
        var line = CommandLine(project.BuilderProject, args);
        stdout.WriteLine($"running the builder: {Path.GetFileName(project.BuilderProject)} (the first run restores and builds it, which can take a few minutes)");
        Log.Write($"builder: in {project.Folder}: \"{dotnet}\" {line}");
        var r = env.Exec(dotnet, line, project.Folder, Timeout);
        Log.Write($"builder exit code {r.ExitCode}{(r.Why != null ? $" ({r.Why})" : "")}; output:\n{r.Output.TrimEnd()}");
        var plugin = Path.Combine(project.Dist, project.Plugin);
        string? failed = r.Why != null ? $"dotnet couldn't be run: {r.Why}"
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
