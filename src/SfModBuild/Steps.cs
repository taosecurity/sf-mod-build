// The project's own commands (sfmod.json "steps"): steps.afterPlugin runs at the end of sf-mod-build plugin and
// steps.afterPack at the end of pack, so build and running one step at a time do the same. Each command is an
// argument list (never a shell), run in the sfmod.json folder with the placeholders filled in. The step's own
// "RESULT: OK" line is held back until its commands pass; the first command that can't run or exits non-zero stops
// the step, and the RESULT line names it.
namespace SfModBuild;

internal static class Steps
{
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(20);

    /// <summary>The placeholder values for steps: the builder's ({data} only when the game was found), plus {name}
    /// and {plugin} (without extension).</summary>
    public static Dictionary<string, string> Values(SfModProject project, string? gameFolder)
    {
        var values = Plugin.Values(project, gameFolder);
        values["name"] = project.Name;
        values["plugin"] = project.PluginBase;
        return values;
    }

    /// <summary>What validate shows for each command, with the placeholders still in.</summary>
    public static IEnumerable<string> Shown(SfModProject project) =>
        project.AfterPlugin.Select((c, i) => $"(steps.afterPlugin[{i}]) {Plugin.Join(c)}")
            .Concat(project.AfterPack.Select((c, i) => $"(steps.afterPack[{i}]) {Plugin.Join(c)}"));

    /// <summary>Runs a step (run, which writes its own lines and RESULT line), then the list's commands. With no
    /// commands it is just run. The programs and {data} are looked up before the step runs, so a missing program
    /// stops it before any work.</summary>
    public static int After(string list, ParsedArgs p, IHostEnv env, TextWriter stdout, string cannot, Func<TextWriter, int> run)
    {
        var project = Common.LoadProject(p, TextWriter.Null, "");
        var commands = project == null ? null : list == "afterPlugin" ? project.AfterPlugin : project.AfterPack;
        if (project == null || commands!.Count == 0) return run(stdout);

        string? gameFolder = null;
        if (commands.Any(c => c.Skip(1).Any(a => a.Contains("{data}"))))
        {
            var game = Common.FindGame(p.Get("--game"), env, out var error);
            if (game == null)
            {
                stdout.WriteLine($"RESULT: {cannot} (steps.{list} uses {{data}}, and {error})");
                return ExitCodes.BadInput;
            }
            gameFolder = game.Folder;
        }
        var values = Values(project, gameFolder);
        var ready = new List<(string Field, string Exe, string Line)>();
        for (var i = 0; i < commands.Count; i++)
        {
            var program = commands[i][0];
            var exe = Path.IsPathRooted(program) ? program : PathSearch.Find(env, program);
            if (exe == null)
            {
                stdout.WriteLine($"RESULT: {cannot} (steps.{list}[{i}]'s program \"{program}\" wasn't found on PATH. Fix: install it or add its folder to PATH, or give its path relative to {project.File}).");
                return ExitCodes.BadInput;
            }
            ready.Add(($"steps.{list}[{i}]", exe, Plugin.Join(commands[i].Skip(1).Select(a => SfModProject.Expand(a, values)))));
        }

        var held = new HoldResult(stdout);
        var code = run(held);
        if (code != ExitCodes.Ok)
        {
            held.Release();
            return code;
        }
        foreach (var (field, exe, line) in ready)
        {
            var name = Path.GetFileName(exe);
            stdout.WriteLine($"running  {field}: {name} {line}");
            Log.Write($"{field}: in {project.Folder}: \"{exe}\" {line}");
            var r = env.Exec(exe, line, project.Folder, Timeout);
            Log.Write($"{field} exit code {r.ExitCode}{(r.Why != null ? $" ({r.Why})" : "")}; output:\n{r.Output.TrimEnd()}");
            if (r.Why != null || r.ExitCode != 0)
            {
                var what = r.Why != null ? $"couldn't be run: {r.Why}" : $"exited with code {r.ExitCode}";
                stdout.WriteLine($"FAILED   {field} ({name}) {what}");
                Common.ShowLastLines(stdout, r.Output, 20);
                stdout.WriteLine($"RESULT: FAILED at {field} ({name} {what}). Fix: fix the error shown above (the full output is in the log: --log <file>), or the command in {project.File}, and run again.");
                return ExitCodes.Failed;
            }
            stdout.WriteLine($"ok       {field}");
        }
        held.Release();
        return ExitCodes.Ok;
    }

    /// <summary>Passes every line through except the RESULT line, which it keeps until Release.</summary>
    sealed class HoldResult : TextWriter
    {
        readonly TextWriter inner;
        readonly System.Text.StringBuilder line = new();
        public string? Result { get; private set; }
        public HoldResult(TextWriter inner) { this.inner = inner; NewLine = inner.NewLine; }
        public override System.Text.Encoding Encoding => inner.Encoding;

        public override void Write(char value)
        {
            line.Append(value);
            if (value != '\n') return;
            var text = line.ToString();
            line.Clear();
            if (text.StartsWith("RESULT: ", StringComparison.Ordinal)) Result = text;
            else inner.Write(text);
        }

        public override void Write(string? value)
        {
            if (value != null) foreach (var c in value) Write(c);
        }

        /// <summary>Writes what was held back (the step failed, so its commands don't run).</summary>
        public void Release()
        {
            if (Result != null) inner.Write(Result);
            if (line.Length > 0) inner.Write(line.ToString());
            Result = null;
            line.Clear();
        }
    }
}
