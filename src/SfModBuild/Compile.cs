// sf-mod-build compile [sfmod.json]: compiles the mod's Papyrus scripts with the Creation Kit's own compiler, one call
// per script, into <stage>/Scripts (and each part's scripts into <stage>/parts/<part>/Scripts). The game's script sources (needed as imports) are unpacked once from
// Tools/ContentResources.zip into a cache folder per zip version. Never with -release: it strips every Debug.Trace.
// Exit 0 when every script compiled, 1 when one failed (the compiler's last lines are shown), 2 bad input or setup.
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace SfModBuild;

/// <summary>One script to compile: the source folder it is in, and its name under it as "Namespace\Name" (no .psc).</summary>
internal sealed record ScriptToCompile(string SourceFolder, string Name)
{
    public string RelativePsc => Name.Replace('\\', Path.DirectorySeparatorChar) + ".psc";
    public string RelativePex => Name.Replace('\\', Path.DirectorySeparatorChar) + ".pex";
}

internal static class Compile
{
    public const string Usage =
        "usage: sf-mod-build compile [<sfmod.json or its folder>] [--game <game folder>] [--verbose] [--log <file>]\n" +
        "  compiles the mod's Papyrus scripts with the Creation Kit's compiler into <stage>/Scripts (stage is emptied first),\n" +
        "  and each part's scripts into <stage>/parts/<part plugin without extension>/Scripts\n" +
        "  never with -release: it removes every Debug.Trace, so the mod's log and debug switch would stop working";

    public static readonly ArgSpec Spec = new(new[] { "--game" }, Array.Empty<string>(), MaxPositionals: 1);

    public const string FlagsFile = "Starfield_Papyrus_Flags.flg";
    public static readonly TimeSpan PerScriptTimeout = TimeSpan.FromMinutes(5);

    public const string ReleaseReason =
        "sf-mod-build never compiles with -release (or -r): it removes every Debug.Trace call from the scripts, so the " +
        "mod's Papyrus log and its debug switch stop working, and testers' logs show nothing.";

    /// <summary>The reason a compiler option is refused, or null when it is allowed. -release and its short form -r
    /// (with - or /, any case, with or without a value) are refused.</summary>
    public static string? RefusedOption(string token)
    {
        var t = token.Trim().Trim('"');
        return Regex.IsMatch(t, @"^(?:--?|/)(?:release|r)(?:[=:].*)?$", RegexOptions.IgnoreCase) ? ReleaseReason : null;
    }

    /// <summary>Given the command's own arguments, an exit message when one of them asks for -release; else null.
    /// (There is no such option; this says why instead of only "unknown option".)</summary>
    // Only tokens in option position count: the value after --game, --checker or --log, and a path that exists
    // (a project folder at /r on Linux), are never mistaken for -release.
    public static string? ReleaseAsked(IReadOnlyList<string> args, ArgSpec spec)
    {
        var values = new HashSet<string>(spec.ValueOptions.Concat(Args.CommonValues), StringComparer.Ordinal);
        for (var i = 0; i < args.Count; i++)
        {
            // the parser takes the next token as the value only when it isn't another option
            if (values.Contains(args[i]))
            {
                if (i + 1 < args.Count && args[i + 1].Trim().Length > 0 && !args[i + 1].StartsWith("--", StringComparison.Ordinal)) i++;
                continue;
            }
            if (RefusedOption(args[i]) is not { } why) continue;
            if (args[i].StartsWith('/') && !OperatingSystem.IsWindows() && (Directory.Exists(args[i]) || File.Exists(args[i]))) continue;
            return why;
        }
        return null;
    }

    public static int Run(ParsedArgs p, IHostEnv env, TextWriter stdout)
    {
        var project = Common.LoadProject(p, stdout, "CANNOT COMPILE");
        if (project == null) return ExitCodes.BadInput;
        var scripts = ScriptList(project);
        if (scripts.Count == 0 && !project.Parts.Any(x => x.Scripts != null))
        {
            stdout.WriteLine("RESULT: OK, nothing to compile (the project has no scripts)");
            return ExitCodes.Ok;
        }

        var game = Common.FindGame(p.Get("--game"), env, out var error);
        if (game == null)
        {
            stdout.WriteLine($"RESULT: CANNOT COMPILE ({error})");
            return ExitCodes.BadInput;
        }
        var compiler = Path.Combine(game.Folder, "Tools", "Papyrus Compiler", "PapyrusCompiler.exe");
        var zip = Path.Combine(game.Folder, "Tools", "ContentResources.zip");
        foreach (var (what, file) in new[] { ("the Papyrus compiler", compiler), ("the script sources zip", zip) })
            if (!File.Exists(file))
            {
                stdout.WriteLine($"RESULT: CANNOT COMPILE ({what} isn't at {file}. {Doctor.CkHowTo})");
                return ExitCodes.BadInput;
            }

        var cacheRoot = VanillaCache.Root(env, out error);
        var vanilla = cacheRoot == null ? null : VanillaCache.Ensure(zip, cacheRoot, out error);
        if (vanilla == null)
        {
            stdout.WriteLine($"RESULT: CANNOT COMPILE ({error})");
            return ExitCodes.BadInput;
        }
        stdout.WriteLine($"game script sources: {vanilla}");
        return CompileAll(project, scripts, compiler, vanilla, env, stdout, game.Folder);
    }

    /// <summary>Every script the project names: each .psc in &lt;source&gt;/&lt;namespace&gt;/ (the first source folder
    /// that has that namespace), then scripts.files, each once, sorted by name.</summary>
    public static List<ScriptToCompile> ScriptList(SfModProject project) => ScriptList(project.Scripts);

    /// <summary>The same for one scripts section (the top-level one or a part's); empty for null.</summary>
    public static List<ScriptToCompile> ScriptList(SfModProject.ScriptsSection? s)
    {
        var list = new List<ScriptToCompile>();
        if (s == null) return list;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string source, string name)
        {
            if (seen.Add(name)) list.Add(new ScriptToCompile(source, name));
        }
        foreach (var ns in s.Namespaces)
        {
            var nsPath = ns.Replace('\\', Path.DirectorySeparatorChar);
            var source = s.Sources.FirstOrDefault(x => Directory.Exists(Path.Combine(x, nsPath)));
            if (source == null) continue; // the loader has already refused this
            foreach (var f in Directory.EnumerateFiles(Path.Combine(source, nsPath), "*.psc"))
                Add(source, ns + "\\" + Path.GetFileNameWithoutExtension(f));
        }
        foreach (var name in s.Files)
        {
            var rel = name.Replace('\\', Path.DirectorySeparatorChar) + ".psc";
            var source = s.Sources.FirstOrDefault(x => File.Exists(Path.Combine(x, rel)));
            if (source != null) Add(source, name);
        }
        list.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
        return list;
    }

    /// <summary>The compiler's command line for one script:
    /// "NS\Name.psc" -i="own1;own2;vanilla" -f="Starfield_Papyrus_Flags.flg" -o="stage\Scripts" -optimize.
    /// Extra options are added at the end; -release is refused (throws) whatever the source.</summary>
    public static string CommandLine(ScriptToCompile script, IEnumerable<string> imports, string output, IEnumerable<string>? extra = null)
    {
        var extras = (extra ?? Array.Empty<string>()).ToList();
        foreach (var e in extras)
            if (RefusedOption(e) is { } why) throw new InvalidOperationException($"compiler option \"{e}\": {why}");
        static string Clean(string path)
        {
            var t = Path.TrimEndingDirectorySeparator(path);
            // a quote in a path would break the command line; Windows paths can't hold one anyway
            if (t.Contains('"')) throw new ArgumentException($"the path {t} contains a quote mark, which the compiler's command line can't carry.");
            return t;
        }
        var line = $"\"{Clean(script.RelativePsc)}\" -i=\"{string.Join(";", imports.Select(Clean))}\" -f=\"{FlagsFile}\" -o=\"{Clean(output)}\" -optimize";
        if (extras.Count > 0) line += " " + string.Join(" ", extras);
        return line;
    }

    /// <summary>Why emptying the stage folder would be unsafe (it is or holds the mod's folder, its sources or its
    /// builder, or is a drive root), or null when it is safe.</summary>
    public static string? UnsafeStage(SfModProject project, IEnumerable<string>? gameFolders = null)
    {
        var keep = new List<string> { project.Folder, project.Dist, project.CreationsOut };
        keep.AddRange(project.BuilderFolders);
        keep.AddRange(project.AllScriptSources);
        return Common.UnsafeToEmpty("stage", project.Stage, project.Folder, keep, gameFolders);
    }

    /// <summary>Empties the stage folder, then compiles each script in turn, stopping at the first failure.</summary>
    public static int CompileAll(SfModProject project, IReadOnlyList<ScriptToCompile> scripts, string compiler, string vanillaSource, IHostEnv env, TextWriter stdout, string? gameFolder = null)
    {
        // every install found, plus the game in use when one is given
        if (UnsafeStage(project, Common.GameFolders(gameFolder, env)) is { } unsafeWhy)
        {
            stdout.WriteLine($"RESULT: CANNOT COMPILE ({unsafeWhy}). Fix: in {project.File}, set \"stage\" to a folder of its own, for example \".build/stage\".");
            return ExitCodes.BadInput;
        }
        // Common.Empty also refuses when the --log file is inside stage
        if (Common.Empty(project.Stage) is { } emptyError)
        {
            stdout.WriteLine($"RESULT: CANNOT COMPILE ({emptyError}).");
            return ExitCodes.BadInput;
        }
        // the top-level scripts into <stage>/Scripts, then each part's into <stage>/parts/<plugin base>/Scripts; a
        // part's own source folders come first in its imports, then the top-level ones, so its scripts can use the mod's
        var jobs = new List<(string Output, List<string> Imports, IReadOnlyList<ScriptToCompile> Scripts)>();
        if (project.Scripts != null)
            jobs.Add((Path.Combine(project.Stage, "Scripts"), project.Scripts.Sources.Select(Path.GetFullPath).Append(vanillaSource).ToList(), scripts));
        foreach (var part in project.Parts)
            if (part.Scripts != null)
                jobs.Add((Path.Combine(part.StageIn(project.Stage), "Scripts"),
                    part.Scripts.Sources.Concat(project.Scripts?.Sources ?? Array.Empty<string>()).Select(Path.GetFullPath)
                        .Distinct(StringComparer.OrdinalIgnoreCase).Append(vanillaSource).ToList(),
                    ScriptList(part.Scripts)));
        foreach (var (folder, _, _) in jobs)
            try { Directory.CreateDirectory(folder); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                stdout.WriteLine($"RESULT: CANNOT COMPILE (the folder {folder} can't be made: {e.Message}). Fix: check that this account can write to {project.Stage}.");
                return ExitCodes.BadInput;
            }

        var total = jobs.Sum(j => j.Scripts.Count);
        var n = 0;
        foreach (var (output, imports, list) in jobs)
        foreach (var script in list)
        {
            var args = CommandLine(script, imports, output);
            Log.Write($"compile {script.Name}: in {script.SourceFolder}: \"{compiler}\" {args}");
            var r = env.Exec(compiler, args, script.SourceFolder, PerScriptTimeout);
            Log.Write($"compiler exit code {r.ExitCode}{(r.Why != null ? $" ({r.Why})" : "")}; output:\n{r.Output.TrimEnd()}");
            var pex = Path.Combine(output, script.RelativePex);
            string? failed = r.Why != null ? $"the compiler couldn't be run: {r.Why}"
                : r.ExitCode != 0 ? $"the compiler exited with code {r.ExitCode}"
                : !File.Exists(pex) ? $"the compiler said OK but wrote no {pex}"
                : null;
            if (failed != null)
            {
                stdout.WriteLine($"FAILED   {script.Name}.psc: {failed}");
                Common.ShowLastLines(stdout, r.Output);
                stdout.WriteLine($"RESULT: FAILED, {script.RelativePsc} in {script.SourceFolder} didn't compile ({n} of {total} compiled before it). Fix the error the compiler names above and run again.");
                return ExitCodes.Failed;
            }
            n++;
            stdout.WriteLine($"compiled {script.Name}.psc{(jobs.Count > 1 || project.Scripts == null ? $" into {output}" : "")}");
        }
        stdout.WriteLine(jobs.Count == 1
            ? $"RESULT: OK, {n} script(s) compiled into {jobs[0].Output}"
            : $"RESULT: OK, {n} script(s) compiled into {string.Join(" and ", jobs.Select(j => j.Output))}");
        return ExitCodes.Ok;
    }
}

/// <summary>The game's Papyrus sources (and the compiler's flags file), unpacked once per ContentResources.zip version
/// into &lt;cache&gt;/vanilla/&lt;sha256 of the zip&gt;/.</summary>
internal static class VanillaCache
{
    /// <summary>The tool's data folder: %LOCALAPPDATA%\sf-mod-build on Windows, $XDG_DATA_HOME/sf-mod-build (or
    /// ~/.local/share/sf-mod-build) elsewhere.</summary>
    public static string? Root(IHostEnv env, out string? error)
    {
        error = null;
        string? baseDir = env.IsWindows
            ? env.EnvVar("LOCALAPPDATA")
            : env.EnvVar("XDG_DATA_HOME") is { Length: > 0 } x ? x
            : env.EnvVar("HOME") is { Length: > 0 } h ? Path.Combine(h, ".local", "share") : null;
        if (string.IsNullOrWhiteSpace(baseDir))
        {
            error = env.IsWindows
                ? "the LOCALAPPDATA environment variable isn't set, so there is nowhere to keep the game's script sources. Fix: run from a normal Windows sign-in."
                : "neither XDG_DATA_HOME nor HOME is set, so there is nowhere to keep the game's script sources. Fix: set HOME.";
            return null;
        }
        return Path.Combine(baseDir, "sf-mod-build");
    }

    /// <summary>Wanted entries: Scripts/Source/**.psc and Scripts/Source/**.flg.</summary>
    public static bool Wanted(string entryName)
    {
        var n = entryName.Replace('\\', '/');
        return n.StartsWith("Scripts/Source/", StringComparison.OrdinalIgnoreCase)
               && (n.EndsWith(".psc", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".flg", StringComparison.OrdinalIgnoreCase));
    }

    public static string Sha256(string file)
    {
        using var s = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(s)).ToLowerInvariant();
    }

    /// <summary>The folder that holds the unpacked Scripts\Source (the compiler's import folder), unpacking it first
    /// when this zip hasn't been unpacked before. The unpack goes into a temp folder beside it, renamed only when
    /// complete, so a half-done unpack is never used.</summary>
    public static string? Ensure(string zip, string cacheRoot, out string? error)
    {
        error = null;
        string sha;
        var started = DateTime.UtcNow;
        try { sha = Sha256(zip); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            error = $"{zip} can't be read ({e.Message}). Fix: check that this account can read the game's Tools folder.";
            return null;
        }
        Log.Write($"{zip}: sha256 {sha} ({(DateTime.UtcNow - started).TotalSeconds:0.0} s)");
        var vanillaDir = Path.Combine(cacheRoot, "vanilla");
        var final = Path.Combine(vanillaDir, sha);
        var source = Path.Combine(final, "Scripts", "Source");
        if (File.Exists(Path.Combine(source, Compile.FlagsFile)))
        {
            Log.Write($"game script sources already unpacked: {final}");
            return source;
        }
        if (Directory.Exists(final))
        {
            // ours, but incomplete (someone deleted files in it): unpack again
            Log.Write($"{final} has no {Compile.FlagsFile}; unpacking again");
            try { Directory.Delete(final, recursive: true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                error = $"{final} is incomplete and can't be deleted ({e.Message}). Fix: delete that folder by hand and run again.";
                return null;
            }
        }
        var temp = Path.Combine(vanillaDir, $"{sha}.partial-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(temp);
            var tempFull = Path.GetFullPath(temp) + Path.DirectorySeparatorChar;
            var count = 0;
            using (var archive = ZipFile.OpenRead(zip))
                foreach (var entry in archive.Entries)
                {
                    if (!Wanted(entry.FullName) || entry.FullName.EndsWith('/')) continue;
                    var dest = Path.GetFullPath(Path.Combine(temp, entry.FullName.Replace('\\', '/')));
                    if (!dest.StartsWith(tempFull, StringComparison.OrdinalIgnoreCase))
                    {
                        Log.Write($"skipped {entry.FullName}: it points outside the folder");
                        continue;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    entry.ExtractToFile(dest, overwrite: true);
                    count++;
                }
            Log.Write($"unpacked {count} file(s) from {zip} into {temp}");
            if (!File.Exists(Path.Combine(temp, "Scripts", "Source", Compile.FlagsFile)))
            {
                TryDelete(temp);
                error = $"{zip} has no Scripts/Source/{Compile.FlagsFile}, so it isn't the Creation Kit's script sources zip. Fix: reinstall the Creation Kit (Steam: Library, Tools, Starfield Creation Kit, then Properties, Installed Files, Verify).";
                return null;
            }
            try { Directory.Move(temp, final); }
            catch (IOException) when (File.Exists(Path.Combine(source, Compile.FlagsFile)))
            {
                // another sf-mod-build run finished the same unpack first: use that one
                TryDelete(temp);
            }
            Log.Write($"game script sources ready: {final}");
            return source;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            TryDelete(temp);
            error = $"the game's script sources couldn't be unpacked from {zip} into {vanillaDir} ({e.Message}). Fix: check the disk has room (about 60 MB) and that this account can write there.";
            return null;
        }
    }

    static void TryDelete(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log.Write($"couldn't delete {dir}: {e.Message}"); }
    }
}
