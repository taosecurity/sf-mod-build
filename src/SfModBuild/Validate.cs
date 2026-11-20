// sf-mod-build validate [sfmod.json]: reads a project file and reports every problem in it, each naming the file, the
// field and the fix. Exit 0 when the file is usable (warnings allowed), 2 when it has a problem. It changes nothing.
namespace SfModBuild;

internal static class Validate
{
    public const string Usage =
        "usage: sf-mod-build validate [<sfmod.json or its folder>] [--verbose] [--log <file>]\n" +
        "  checks the project file (default: sfmod.json in the current folder) and prints what a build would use";

    public static readonly ArgSpec Spec = new(Array.Empty<string>(), Array.Empty<string>(), MaxPositionals: 1);

    public static int Run(ParsedArgs p, TextWriter stdout)
    {
        var path = p.Positionals.Count > 0 ? p.Positionals[0] : Path.Combine(Environment.CurrentDirectory, SfModProject.FileName);
        var problems = new List<ProjectProblem>();
        var project = SfModProject.Load(path, problems);
        var shown = Path.GetFullPath(path);
        if (Directory.Exists(shown)) shown = Path.Combine(shown, SfModProject.FileName);
        foreach (var x in problems)
        {
            stdout.WriteLine(x.Format(shown));
            Log.Write(x.Format(shown));
        }
        var errors = problems.Count(x => !x.IsWarning);
        if (project == null)
        {
            stdout.WriteLine($"RESULT: {errors} PROBLEM(S) in {shown}; fix them and run validate again");
            return ExitCodes.BadInput;
        }
        Print(project, stdout);
        var warnings = problems.Count - errors;
        stdout.WriteLine(warnings == 0 ? "RESULT: OK" : $"RESULT: OK (with {warnings} WARNING(S): see above)");
        return ExitCodes.Ok;
    }

    static void Print(SfModProject p, TextWriter o)
    {
        o.WriteLine($"project:  {p.File}");
        o.WriteLine($"mod:      {p.Name} {p.Version}");
        o.WriteLine(p.Scripts != null ? $"plugin:   {p.Plugin} (archive \"{p.Ba2Name}\")" : $"plugin:   {p.Plugin} (no archive: no scripts)");
        if (p.Scripts == null) o.WriteLine("scripts:  none");
        else
        {
            o.WriteLine($"scripts:  from {string.Join("; ", p.Scripts.Sources)}");
            if (p.Scripts.Namespaces.Count > 0) o.WriteLine($"          namespaces {string.Join(", ", p.Scripts.Namespaces)}");
            if (p.Scripts.Files.Count > 0) o.WriteLine($"          files {string.Join(", ", p.Scripts.Files)}");
        }
        o.WriteLine($"builder:  {p.BuilderProject}");
        o.WriteLine($"          args {string.Join(" ", p.BuilderArgs)}");
        o.WriteLine($"dist:     {p.Dist}");
        o.WriteLine($"stage:    {p.Stage}");
        o.WriteLine($"xbox:     {(p.Xbox ? $"yes, \"{p.XboxBa2Name}\" in {p.CreationsOut}" : "no")}");
        o.WriteLine($"zip:      {p.ZipPath}");
        o.WriteLine($"          holds {p.Plugin}" + (p.Scripts != null ? $", {p.Ba2Name}" : "") +string.Concat(p.ZipFiles.Select(f => $", {f.NameInZip}")));
        o.WriteLine($"check:    {p.Checker ?? "sf-mod-check or sfmodcheck from PATH"}{(p.CheckRequired ? ", required" : ", a missing checker is a WARNING")}"
            + (p.CheckArgs.Count > 0 ? $", extra options {string.Join(" ", p.CheckArgs)}" : ""));
    }
}
