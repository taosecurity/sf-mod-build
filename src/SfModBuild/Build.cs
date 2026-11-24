// sf-mod-build build [sfmod.json]: the whole build in order: compile, plugin, pack, zip, check. It stops at the first
// step that doesn't exit 0 and returns that step's exit code. One RESULT line at the end names the step that stopped
// it, or says the build is done.
namespace SfModBuild;

internal static class Build
{
    public const string Usage =
        "usage: sf-mod-build build [<sfmod.json or its folder>] [--game <game folder>] [--checker <exe>] [--masters-dir <folder>]... [--require-check] [--verbose] [--log <file>]\n" +
        "  compile, plugin, pack, zip and check in order; stops at the first step that fails\n" +
        "  the plugin step shows BuildKit's gap check result (--verbose shows the builder's other lines)\n" +
        "  the check step checks the PC zip; the Creations check runs on creations-out: sf-mod-check release creations-out --profile creations";

    public static readonly ArgSpec Spec = Check.Spec;

    public static int Run(ParsedArgs p, IHostEnv env, TextWriter stdout)
    {
        // a wrong --masters-dir stops the build before it starts, not after the zip is made
        if (Check.MastersDirs(p, stdout) == null) return ExitCodes.BadInput;
        var steps = new (string Name, Func<ParsedArgs, IHostEnv, TextWriter, int> Run)[]
        {
            ("compile", Compile.Run),
            ("plugin", Plugin.Run),
            ("pack", Pack.Run),
            ("zip", Zip.Run),
            ("check", Check.Run),
        };
        string? zipPath = null;
        string? creationsNote = null;
        foreach (var (name, run) in steps)
        {
            stdout.WriteLine($"== {name}");
            Log.Write($"build: step {name}");
            var code = run(p, env, stdout);
            Log.Write($"build: step {name} exit code {code}");
            if (code != ExitCodes.Ok)
            {
                stdout.WriteLine($"RESULT: BUILD STOPPED at {name} (exit code {code}; see the {name} RESULT line above)");
                return code;
            }
            if (name == "zip")
            {
                var project = Common.LoadProject(p, TextWriter.Null, "");
                zipPath = project == null ? null : Path.GetFullPath(project.ZipPath);
                if (project is { Xbox: true }) creationsNote = $"note: the Creations check runs on the creations-out folder, not on the PC zip, and build does not run it. Run: sf-mod-check release \"{Path.GetFullPath(project.CreationsOut)}\" --profile creations";
            }
        }
        if (creationsNote != null) stdout.WriteLine(creationsNote);
        stdout.WriteLine($"RESULT: OK, built {zipPath}");
        return ExitCodes.Ok;
    }
}
