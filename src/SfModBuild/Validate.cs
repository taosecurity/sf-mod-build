// sf-mod-build validate [sfmod.json]: reads a project file and reports every problem in it, each naming the file, the
// field and the fix. Exit 0 when the file is usable (warnings allowed), 2 when it has a problem. It changes nothing.
namespace SfModBuild;

internal static class Validate
{
    public const string Usage =
        "usage: sf-mod-build validate [<sfmod.json or its folder>] [--verbose] [--log <file>]\n" +
        "  checks the project file (default: sfmod.json in the current folder) and prints what a build would use,\n" +
        "  with each command it would run on a \"runs:\" line (read them before building a downloaded mod)";

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
        var archives = p.ArchiveNames.ToList();
        o.WriteLine(archives.Count switch
        {
            0 when p.ZipArchives.Count > 0 => $"plugin:   {p.Plugin} (pack makes no archive: no scripts or loose files; zip.archives lists {string.Join(" and ", p.ZipArchives.Select(a => $"\"{a}\""))})",
            0 => $"plugin:   {p.Plugin} (no archive: no scripts or loose files)",
            1 => $"plugin:   {p.Plugin} (archive \"{archives[0]}\")",
            _ => $"plugin:   {p.Plugin} (archives {string.Join(" and ", archives.Select(a => $"\"{a}\""))})",
        });
        if (p.Assets == null) o.WriteLine("assets:   none");
        else
        {
            o.WriteLine($"assets:   from {p.Assets.Source}: {p.Assets.General.Count} general file(s), {p.Assets.Textures.Count} texture(s)"
                + (p.Assets.UncompressedMain ? " (sound or lip files, so Main is packed uncompressed)" : ""));
            if (p.Assets.XboxTextures != null) o.WriteLine($"          Xbox textures from {p.Assets.XboxTextures}: {p.Assets.XboxTextureFiles.Count} file(s)");
        }
        if (p.Scripts == null) o.WriteLine("scripts:  none");
        else
        {
            o.WriteLine($"scripts:  from {string.Join("; ", p.Scripts.Sources)}");
            if (p.Scripts.Namespaces.Count > 0) o.WriteLine($"          namespaces {string.Join(", ", p.Scripts.Namespaces)}");
            if (p.Scripts.Files.Count > 0) o.WriteLine($"          files {string.Join(", ", p.Scripts.Files)}");
        }
        if (p.BuilderPrebuilt != null)
            o.WriteLine($"builder:  prebuilt {p.BuilderPrebuilt}, copied into dist as {p.Plugin}");
        else
        {
            o.WriteLine(p.BuilderProject != null ? $"builder:  {p.BuilderProject}" : $"builder:  command {p.BuilderCommand![0]}");
            o.WriteLine($"          args {string.Join(" ", p.BuilderArgs)}");
            // every program a build would run, so a downloaded mod's sfmod.json can be read before it is built
            o.WriteLine($"runs:     {Plugin.Shown(p)}");
        }
        o.WriteLine($"dist:     {p.Dist}");
        o.WriteLine($"stage:    {p.Stage}");
        var xboxArchives = (p.HasMainArchive ? new[] { p.XboxBa2Name } : Array.Empty<string>())
            .Concat(p.HasTexturesArchive ? new[] { p.XboxTexturesBa2Name } : Array.Empty<string>()).ToList();
        o.WriteLine($"xbox:     {(!p.Xbox ? "no" : xboxArchives.Count == 0 ? $"yes, but there is nothing to pack for it; {p.CreationsOut} isn't made" : $"yes, {string.Join(" and ", xboxArchives.Select(a => $"\"{a}\""))} in {p.CreationsOut}")}");
        o.WriteLine($"zip:      {p.ZipPath}");
        o.WriteLine($"          holds {p.Plugin}" + string.Concat(archives.Concat(p.ZipArchives).Select(a => $", {a}")) + string.Concat(p.ZipFiles.Select(f => $", {f.NameInZip}")));
        o.WriteLine($"check:    {p.Checker ?? "sf-mod-check or sfmodcheck from PATH"}{(p.CheckRequired ? ", required" : ", a missing checker is a WARNING")}"
            + (p.CheckArgs.Count > 0 ? $", extra options {string.Join(" ", p.CheckArgs)}" : ""));
    }
}
