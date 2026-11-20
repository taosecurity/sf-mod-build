// sf-mod-build: builds a Starfield mod from one project file. Commands so far:
//   sf-mod-build doctor [--game <folder>] [--checker <exe>] [--verbose] [--log <file>]
//   sf-mod-build validate [sfmod.json] [--verbose] [--log <file>]
//   sf-mod-build compile [sfmod.json] [--game <folder>] [--verbose] [--log <file>]
//   sf-mod-build plugin [sfmod.json] [--game <folder>] [--verbose] [--log <file>]
//   sf-mod-build pack [sfmod.json] [--game <folder>] [--verbose] [--log <file>]
//   sf-mod-build zip [sfmod.json] [--verbose] [--log <file>]
//   sf-mod-build check [sfmod.json] [--game <folder>] [--checker <exe>] [--require-check] [--verbose] [--log <file>]
//   sf-mod-build build [sfmod.json] [--game <folder>] [--checker <exe>] [--require-check] [--verbose] [--log <file>]
//   sf-mod-build --help | --version
// Exit codes: 0 OK, 1 the build or the check failed, 2 bad input or setup (a wrong option, a missing tool).
using SfModBuild;

return App.Run(args, new RealHostEnv(), Console.Out);

namespace SfModBuild
{
    internal static class ExitCodes
    {
        public const int Ok = 0;
        public const int Failed = 1;
        public const int BadInput = 2;
    }

    internal static class App
    {
        public static string Version { get; } = VersionOf(typeof(App).Assembly);
        public static string Byline => $"sf-mod-build {Version} by Richard Bejtlich - GPL-3.0 - https://github.com/taosecurity/sf-mod-build";

        public const string Usage =
            "usage: sf-mod-build <command> [options]\n" +
            "commands:\n" +
            "  doctor      find the game, the Creation Kit's tools, .NET and the checker; say how to get anything missing\n" +
            "  validate    check a project file (sfmod.json) and show what a build would use\n" +
            "  compile     compile the mod's Papyrus scripts with the Creation Kit's compiler (never -release)\n" +
            "  plugin      run the mod's Mutagen builder to write the plugin into dist\n" +
            "  pack        pack the compiled scripts into the BA2 with the Creation Kit's Archive2 (and the Xbox one)\n" +
            "  zip         write the mod's zip reproducibly (same files, same bytes)\n" +
            "  check       run sf-mod-check (or sfmodcheck) release on the zip\n" +
            "  build       compile, plugin, pack, zip and check in order\n" +
            "options for every command:\n" +
            "  --verbose       troubleshooting log on stderr (lines start \"log: \")\n" +
            "  --log <file>    the same log, with times, written to a file\n" +
            "  --help          help for the command\n" +
            "sf-mod-build --version    the version\n" +
            "exit codes: 0 OK, 1 the build or check failed, 2 bad input or setup";

        public static int Run(string[] args, IHostEnv env, TextWriter stdout)
        {
            try
            {
                if (args.Length == 0 || args[0] is "--help" or "-h" or "/?" or "help")
                {
                    stdout.WriteLine(Byline);
                    stdout.WriteLine(Usage);
                    return args.Length == 0 ? ExitCodes.BadInput : ExitCodes.Ok;
                }
                if (args[0] is "--version" or "-v")
                {
                    stdout.WriteLine(Byline);
                    stdout.WriteLine("Free software: you can share and change it under the GNU GPL version 3. It comes with no warranty.");
                    return ExitCodes.Ok;
                }
                return args[0] switch
                {
                    "doctor" => RunCommand(args, Doctor.Spec, Doctor.Usage, p => Doctor.Run(p, env, stdout), stdout),
                    "validate" => RunCommand(args, Validate.Spec, Validate.Usage, p => Validate.Run(p, stdout), stdout),
                    "compile" => Compile.ReleaseAsked(args[1..], Compile.Spec) is { } why
                        ? Refuse(why)
                        : RunCommand(args, Compile.Spec, Compile.Usage, p => Compile.Run(p, env, stdout), stdout),
                    "plugin" => RunCommand(args, Plugin.Spec, Plugin.Usage, p => Plugin.Run(p, env, stdout), stdout),
                    "pack" => RunCommand(args, Pack.Spec, Pack.Usage, p => Pack.Run(p, env, stdout), stdout),
                    "zip" => RunCommand(args, Zip.Spec, Zip.Usage, p => Zip.Run(p, env, stdout), stdout),
                    "check" => RunCommand(args, Check.Spec, Check.Usage, p => Check.Run(p, env, stdout), stdout),
                    "build" => Compile.ReleaseAsked(args[1..], Build.Spec) is { } buildWhy
                        ? Refuse(buildWhy)
                        : RunCommand(args, Build.Spec, Build.Usage, p => Build.Run(p, env, stdout), stdout),
                    _ => Unknown(args[0]),
                };
            }
            catch (Exception e)
            {
                // anything not caught on the way: one RESULT line and exit 2, the full error in the log
                stdout.WriteLine($"RESULT: CANNOT RUN ({e.GetType().Name}: {e.Message})");
                Console.Error.WriteLine(Log.On ? "The log has the full error." : "For the full error, run again with --log sf-mod-build-log.txt and look at that file.");
                Log.Write($"stopped: {e}");
                return ExitCodes.BadInput;
            }
            finally { Log.Close(); }
        }

        static int Refuse(string why)
        {
            Console.Error.WriteLine(why);
            return ExitCodes.BadInput;
        }

        static int Unknown(string command)
        {
            Console.Error.WriteLine($"unknown command \"{command}\". Run sf-mod-build --help to see the commands.");
            return ExitCodes.BadInput;
        }

        /// <summary>Parses a command's options, starts the log, prints help when asked, then runs it.</summary>
        static int RunCommand(string[] args, ArgSpec spec, string usage, Func<ParsedArgs, int> run, TextWriter stdout)
        {
            var p = Args.Parse(args[1..], spec, out var error);
            if (p == null)
            {
                Console.Error.WriteLine(error);
                Console.Error.WriteLine(usage);
                return ExitCodes.BadInput;
            }
            if (p.Has("--help"))
            {
                stdout.WriteLine(usage);
                return ExitCodes.Ok;
            }
            if (p.Has("--verbose")) Log.ToStderr();
            if (p.Get("--log") is { } logFile && Log.ToFile(logFile) is { } logError)
            {
                Console.Error.WriteLine(logError);
                return ExitCodes.BadInput;
            }
            Log.Write($"{Byline}");
            Log.Write($"command line: {string.Join(" ", args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a))}");
            Log.Write($"working folder: {Environment.CurrentDirectory}");
            var code = run(p);
            Log.Write($"exit code {code}");
            return code;
        }

        /// <summary>"0.1.0+abc123" becomes "0.1.0".</summary>
        public static string VersionOf(System.Reflection.Assembly a)
        {
            var info = System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(a)?.InformationalVersion;
            if (info != null) return info.Split('+')[0];
            return a.GetName().Version is { } v ? (v.Build >= 0 ? v.ToString(3) : v.ToString()) : "unknown";
        }
    }
}
