// sf-mod-build check [sfmod.json]: runs the release checker (sf-mod-check, called sfmodcheck before its rename) on the
// mod's zip: <checker> release "<zip>" [--data "<game>\Data"] <check.args>. The checker is --checker, else
// check.checker in sfmod.json, else sf-mod-check or sfmodcheck on PATH. --data is added when the game is known and
// check.args doesn't give it, so a Game Pass or other non-default install is checked against its own Data folder.
// Exit: the checker's own code (0 OK, 1 FAIL, 2 it couldn't run). No checker: a WARNING and exit 0, unless
// --require-check or check.required is set (then exit 2).
namespace SfModBuild;

internal static class Check
{
    public const string Usage =
        "usage: sf-mod-build check [<sfmod.json or its folder>] [--game <game folder>] [--checker <exe>] [--require-check] [--verbose] [--log <file>]\n" +
        "  runs sf-mod-check (or sfmodcheck) release on the mod's zip; without a checker this is a WARNING,\n" +
        "  or a failure with --require-check (or \"check\": { \"required\": true } in sfmod.json)";

    public static readonly ArgSpec Spec = new(new[] { "--game", "--checker" }, new[] { "--require-check" }, MaxPositionals: 1);

    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(15);

    public static int Run(ParsedArgs p, IHostEnv env, TextWriter stdout)
    {
        var project = Common.LoadProject(p, stdout, "CANNOT CHECK");
        if (project == null) return ExitCodes.BadInput;
        var required = p.Has("--require-check") || project.CheckRequired;

        string? checker;
        if (p.Get("--checker") is { } given)
        {
            try { checker = Path.GetFullPath(given.Trim().Trim('"')); }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
            {
                stdout.WriteLine($"RESULT: CANNOT CHECK (--checker \"{given}\" isn't a usable file path: {e.Message}. Fix: give the full path of sf-mod-check.exe or sfmodcheck.exe).");
                return ExitCodes.BadInput;
            }
            if (!File.Exists(checker))
            {
                stdout.WriteLine($"RESULT: CANNOT CHECK (--checker {checker} isn't there. Fix: give the full path of sf-mod-check.exe or sfmodcheck.exe).");
                return ExitCodes.BadInput;
            }
        }
        else checker = project.Checker ?? PathSearch.Find(env, "sf-mod-check", "sfmodcheck");

        if (checker == null)
        {
            if (required)
            {
                stdout.WriteLine($"RESULT: CANNOT CHECK (no checker was found, and a check is required{(project.CheckRequired ? $" by check.required in {project.File}" : " by --require-check")}. Fix: {Doctor.CheckerHowTo})");
                return ExitCodes.BadInput;
            }
            stdout.WriteLine($"WARNING  the zip wasn't checked: sf-mod-check (or sfmodcheck) isn't on PATH. {Doctor.CheckerHowTo}");
            stdout.WriteLine("RESULT: OK, not checked (no checker)");
            return ExitCodes.Ok;
        }

        var zip = Path.GetFullPath(project.ZipPath);
        if (!File.Exists(zip))
        {
            stdout.WriteLine($"RESULT: CANNOT CHECK (there is no zip at {zip}. Fix: run sf-mod-build zip first, or sf-mod-build build).");
            return ExitCodes.BadInput;
        }

        var hasData = project.CheckArgs.Any(a => a == "--data" || a.StartsWith("--data=", StringComparison.Ordinal));
        string? data = null;
        if (!hasData)
        {
            var game = Common.FindGame(p.Get("--game"), env, out var error);
            if (game == null && p.Get("--game") != null)
            {
                stdout.WriteLine($"RESULT: CANNOT CHECK ({error})");
                return ExitCodes.BadInput;
            }
            if (game != null) data = Path.Combine(game.Folder, "Data");
            else Log.Write("check: the game wasn't found, so the checker uses its own default Data folder");
        }
        var line = CommandLine(zip, data, project.CheckArgs);
        stdout.WriteLine($"checking {Path.GetFileName(zip)} with {Path.GetFileName(checker)}");
        Log.Write($"checker: in {project.Folder}: \"{checker}\" {line}");
        var r = env.Exec(checker, line, project.Folder, Timeout);
        Log.Write($"checker exit code {r.ExitCode}{(r.Why != null ? $" ({r.Why})" : "")}; output:\n{r.Output.TrimEnd()}");
        if (r.Why != null)
        {
            stdout.WriteLine($"RESULT: CANNOT CHECK ({checker} couldn't be run: {r.Why}). Fix: run it by hand to see why, or give another one with --checker.");
            return ExitCodes.BadInput;
        }
        if (r.ExitCode == 0)
        {
            // its findings (warnings, notes) and its own RESULT line
            foreach (var l in Common.LastLines(r.Output, int.MaxValue).Where(l => !l.StartsWith("log: ", StringComparison.Ordinal)))
                stdout.WriteLine($"         {l}");
            stdout.WriteLine(r.Incomplete
                ? $"RESULT: OK, {Path.GetFileName(zip)} passed the release check (exit code 0), but the checker's output was cut short, so some of its findings may be missing above. Run the checker by hand to see them all."
                : $"RESULT: OK, {Path.GetFileName(zip)} passed the release check");
            return ExitCodes.Ok;
        }
        Common.ShowLastLines(stdout, r.Output, 30);
        if (r.ExitCode == ExitCodes.BadInput)
        {
            stdout.WriteLine($"RESULT: CANNOT CHECK (the checker couldn't run, exit code 2; see its message above). Fix: what it says, then run again.");
            return ExitCodes.BadInput;
        }
        stdout.WriteLine($"RESULT: FAILED, {Path.GetFileName(zip)} didn't pass the release check (exit code {r.ExitCode}). Fix: the PROBLEM lines above, then build again.");
        return ExitCodes.Failed;
    }

    /// <summary>release "&lt;zip&gt;" [--data "&lt;Data&gt;"] &lt;args&gt;, each quoted as needed.</summary>
    public static string CommandLine(string zip, string? data, IEnumerable<string> args)
    {
        var line = $"release {Common.Quote(zip)}";
        if (data != null) line += $" --data {Common.Quote(Path.TrimEndingDirectorySeparator(Path.GetFullPath(data)))}";
        foreach (var a in args) line += " " + Common.Quote(a);
        return line;
    }
}
