// sf-mod-build voice [sfmod.json]: makes the game's voice files for the plugin's new lines from WAVs.
//   1. sf-mod-check voice-lines lists every voiced line the built plugin (dist\<plugin>) adds: voice type, file
//      number, text and the voice type's sex (needs sf-mod-check 0.21.0 or later).
//   2. Each line is matched with voice.source\<voice type>\<FILENAME>.wav (Voice Cloner's layout; its
//      Sound\Voice\<plugin>\ folders above them work too). A line with no WAV is a WARNING (it stays silent) unless
//      assets.source already has its .wem; a WAV with no line is a WARNING and isn't used.
//   3. The modder's own Audiokinetic Wwise (2021.1, like the Creation Kit) converts the WAVs to .wem, with the
//      Creation Kit's Wwise project (a copy in voice.out, or voice.wwiseProject) and its "Voice Conversion" setting.
//   4. The Creation Kit's LipGenerator and FaceFX ffxc make the .ffxanim lip file of each line, with the face set of
//      the voice type's sex (voice.faces overrides it; "none" makes no lip file).
//   5. Both go into voice.out\data\Sound\Voice\<plugin>\<voice type>\, which pack puts in "<plugin> - Main.ba2"
//      (uncompressed). Files there that no current line with a WAV has are deleted, so the WAVs must stay.
//      A line with no text gets its .wem but no lip file (LipGenerator needs the words).
// A line whose WAV, text and face set haven't changed since the last run is skipped (voice.out\cache.json), so a
// rerun needs no Wwise when nothing changed. Wwise is never shipped or bundled: the voice step uses the modder's own
// install, found by --wwise, WWISEROOT, or the Audiokinetic folder on each drive.
// Exit 0 when every line with a WAV has its files, 1 when Wwise, LipGenerator or ffxc failed, 2 bad input or setup.
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace SfModBuild;

internal static class Voice
{
    public const string Usage =
        "usage: sf-mod-build voice [<sfmod.json or its folder>] [--game <game folder>] [--checker <exe>] [--wwise <folder or exe>] [--masters-dir <folder>]... [--verbose] [--log <file>]\n" +
        "  makes the .wem voice files and .ffxanim lip files of the plugin's new lines from the WAVs in voice.source:\n" +
        "  sf-mod-check voice-lines (0.21.0 or later) lists the lines, your own Wwise 2021.1 converts the WAVs, and the\n" +
        "  Creation Kit's LipGenerator and ffxc make the lip files; they go into voice.out\\data, which pack puts in Main.\n" +
        "  --wwise: the Wwise folder (such as C:\\Audiokinetic\\Wwise2021.1.14.8108) or its WwiseConsole.exe\n" +
        "  (default: WWISEROOT, then the Audiokinetic folder on each drive); unchanged lines are skipped";

    public static readonly ArgSpec Spec = new(new[] { "--game", "--checker", "--masters-dir", "--wwise" }, Array.Empty<string>(), MaxPositionals: 1, Repeatable: new[] { "--masters-dir" });

    public static readonly TimeSpan ListTimeout = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan WwiseTimeout = TimeSpan.FromMinutes(60);
    public static readonly TimeSpan LipTimeout = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan FfxcTimeout = TimeSpan.FromMinutes(30);

    public const string WwiseHowTo =
        "Install Audiokinetic Wwise 2021.1 (the version the Creation Kit's project is made for, such as 2021.1.14.8108) with the Audiokinetic Launcher " +
        "from https://www.audiokinetic.com (a free account; Authoring and the Windows platform), or give its folder with --wwise <folder>. " +
        "Wwise's license is Audiokinetic's: register your mod as a non-commercial project there.";

    /// <summary>One row of the voice-lines list.</summary>
    public sealed record Line(string VoiceType, string File, string Text, string Sex)
    {
        public string Key => VoiceType + "\\" + File;
    }

    static readonly Regex VoiceTypeName = new("^[a-z0-9_]+$");
    static readonly Regex FileNumber = new("^[0-9A-F]{8}$");

    public static int Run(ParsedArgs p, IHostEnv env, TextWriter stdout)
    {
        var project = Common.LoadProject(p, stdout, "CANNOT MAKE VOICE");
        if (project == null) return ExitCodes.BadInput;
        var voice = project.Voice;
        if (voice == null)
        {
            stdout.WriteLine("RESULT: OK, nothing to do (the project has no voice section)");
            return ExitCodes.Ok;
        }
        var plugin = Path.Combine(project.Dist, project.Plugin);
        if (!File.Exists(plugin))
        {
            stdout.WriteLine($"RESULT: CANNOT MAKE VOICE (there is no plugin at {plugin}. Fix: run sf-mod-build plugin first, or sf-mod-build build).");
            return ExitCodes.BadInput;
        }
        var masters = Check.MastersDirs(p, stdout);
        if (masters == null) return ExitCodes.BadInput;
        var game = Common.FindGame(p.Get("--game"), env, out var gameError);
        if (game == null)
        {
            stdout.WriteLine($"RESULT: CANNOT MAKE VOICE ({gameError})");
            return ExitCodes.BadInput;
        }
        var checker = FindChecker(p, project, env, stdout);
        if (checker == null) return ExitCodes.BadInput;

        // 1. the list of lines
        var lines = ListLines(project, voice, env, stdout, checker, plugin, Path.Combine(game.Folder, "Data"), masters);
        if (lines == null) return ExitCodes.BadInput;

        // 2. the WAVs
        var wavRoot = WavRoot(voice.Source, project.Plugin);
        var wavs = IndexWavs(wavRoot);
        var inAssets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in project.Assets?.General ?? Array.Empty<SfModProject.AssetFile>())
            inAssets.Add(f.Relative);
        string Rel(Line l, string ext) => Path.Combine("Sound", "Voice", project.Plugin, l.VoiceType, l.File + ext);

        var work = new List<(Line Line, string Wav)>();
        var noWav = new List<string>();
        var alreadyInAssets = 0;
        foreach (var l in lines)
        {
            if (wavs.TryGetValue(l.Key, out var wav)) work.Add((l, wav));
            else if (inAssets.Contains(Rel(l, ".wem"))) alreadyInAssets++;
            else noWav.Add(l.Key);
        }
        var listed = new HashSet<string>(lines.Select(l => l.Key), StringComparer.OrdinalIgnoreCase);
        var orphans = wavs.Keys.Where(k => !listed.Contains(k)).Order(StringComparer.OrdinalIgnoreCase).ToList();
        if (noWav.Count > 0)
            stdout.WriteLine($"WARNING  {noWav.Count} line(s) have no WAV in {wavRoot}, so they stay silent: {Few(noWav)}. Fix: make each one as <voice type>\\<FILENAME>.wav (the voice-lines list is {Path.Combine(voice.Out, "lines.tsv")}).");
        if (orphans.Count > 0)
            stdout.WriteLine($"WARNING  {orphans.Count} WAV(s) in {wavRoot} match no line of the plugin and aren't used: {Few(orphans)}. Fix: check the voice type folder and the file number, or delete them.");

        // 3. the face set of each voice type
        var faces = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var vt in work.Select(w => w.Line.VoiceType).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var sex = work.First(w => w.Line.VoiceType == vt).Line.Sex.ToLowerInvariant();
            var face = voice.Faces.TryGetValue(vt, out var given) ? given : sex is "female" or "male" ? sex : null;
            if (face == null)
            {
                stdout.WriteLine($"WARNING  the voice type {vt} has no known sex, so its lines get no lip file. Fix: set \"faces\": {{ \"{vt}\": \"female\" }} (or \"male\", or \"none\" for a voice with no lips) in voice.");
                face = "none";
            }
            faces[vt] = face;
        }
        // LipGenerator needs the line's words, so a line with no text (a sigh, a laugh) gets its .wem but no lip file
        string FaceOf(Line l) => l.Text.Length == 0 ? "none" : faces[l.VoiceType];
        var noText = work.Where(w => w.Line.Text.Length == 0 && faces[w.Line.VoiceType] != "none").Select(w => w.Line.Key).ToList();
        if (noText.Count > 0)
            stdout.WriteLine($"WARNING  {noText.Count} line(s) have no text, so they get a .wem but no lip file: {Few(noText)}. Fix: give the line its words in the plugin if the face should move.");

        // 4. which lines need making
        var cachePath = Path.Combine(voice.Out, "cache.json");
        var cache = ReadCache(cachePath);
        var newCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var todo = new List<(Line Line, string Wav, string Face)>();
        foreach (var (l, wav) in work)
        {
            var face = FaceOf(l);
            string hash;
            try { hash = Hash(wav, l.Text, face, voice.Conversion); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                stdout.WriteLine($"RESULT: CANNOT MAKE VOICE ({wav} can't be read: {e.Message}. Fix: close any program using it and run again).");
                return ExitCodes.BadInput;
            }
            newCache[l.Key] = hash;
            var made = File.Exists(Path.Combine(voice.Data, Rel(l, ".wem"))) && (face == "none" || File.Exists(Path.Combine(voice.Data, Rel(l, ".ffxanim"))));
            if (!(made && cache.TryGetValue(l.Key, out var old) && old == hash)) todo.Add((l, wav, face));
        }
        stdout.WriteLine($"lines    {lines.Count} voiced line(s): {work.Count} with a WAV ({todo.Count} to make, {work.Count - todo.Count} unchanged){(alreadyInAssets > 0 ? $", {alreadyInAssets} already in assets.source" : "")}, {noWav.Count} without");

        if (todo.Count > 0)
        {
            var workDir = Path.Combine(voice.Out, "work");
            if (Common.Empty(workDir) is { } why)
            {
                stdout.WriteLine($"RESULT: CANNOT MAKE VOICE ({why}).");
                return ExitCodes.BadInput;
            }
            var tools = Path.Combine(game.Folder, "Tools");
            var code = Convert(project, voice, env, stdout, p.Get("--wwise"), tools, wavRoot, todo, workDir, Rel);
            if (code != ExitCodes.Ok) return code;
            code = Lips(voice, env, stdout, tools, todo, workDir, Rel);
            if (code != ExitCodes.Ok) return code;
        }

        // 5. files no current line with a WAV has are left from an earlier build: deleted
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (l, _) in work)
        {
            keep.Add(Path.Combine(voice.Data, Rel(l, ".wem")));
            if (FaceOf(l) != "none") keep.Add(Path.Combine(voice.Data, Rel(l, ".ffxanim")));
        }
        var removed = 0;
        try
        {
            if (Directory.Exists(voice.Data))
            {
                foreach (var f in Directory.GetFiles(voice.Data, "*", SearchOption.AllDirectories))
                    if (!keep.Contains(Path.GetFullPath(f)))
                    {
                        File.Delete(f);
                        removed++;
                        Log.Write($"voice: deleted {f} (no current line has it)");
                    }
            }
            WriteCache(cachePath, newCache);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            stdout.WriteLine($"RESULT: CANNOT MAKE VOICE ({voice.Data} can't be tidied: {e.Message}. Fix: close any program using files there and run again).");
            return ExitCodes.BadInput;
        }
        var lipCount = work.Count(w => FaceOf(w.Line) != "none");
        stdout.WriteLine($"RESULT: OK, {work.Count} voice line(s) in {voice.Data} ({todo.Count} made, {work.Count - todo.Count} unchanged; {lipCount} with a lip file{(removed > 0 ? $"; {removed} old file(s) deleted" : "")})");
        return ExitCodes.Ok;
    }

    static string Few(IReadOnlyList<string> names) => string.Join(", ", names.Take(5)) + (names.Count > 5 ? $" and {names.Count - 5} more" : "");

    /// <summary>The checker: --checker, else check.checker, else sf-mod-check or sfmodcheck on PATH. Null (with the
    /// RESULT line printed) when there is none: the voice step can't run without it.</summary>
    static string? FindChecker(ParsedArgs p, SfModProject project, IHostEnv env, TextWriter stdout)
    {
        if (p.Get("--checker") is { } given)
        {
            string full;
            try { full = Path.GetFullPath(given.Trim().Trim('"')); }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
            {
                stdout.WriteLine($"RESULT: CANNOT MAKE VOICE (--checker \"{given}\" isn't a usable file path: {e.Message}. Fix: give the full path of sf-mod-check.exe).");
                return null;
            }
            if (File.Exists(full)) return full;
            stdout.WriteLine($"RESULT: CANNOT MAKE VOICE (--checker {full} isn't there. Fix: give the full path of sf-mod-check.exe).");
            return null;
        }
        var found = project.Checker ?? PathSearch.Find(env, "sf-mod-check", "sfmodcheck");
        if (found == null)
            stdout.WriteLine($"RESULT: CANNOT MAKE VOICE (sf-mod-check isn't on PATH, and the voice step needs its voice-lines command (0.21.0 or later) to list the lines. Fix: {Doctor.CheckerHowTo})");
        return found;
    }

    /// <summary>Runs voice-lines into voice.out\lines.tsv and reads it. Null (with the RESULT line) when it failed.</summary>
    static List<Line>? ListLines(SfModProject project, SfModProject.VoiceSection voice, IHostEnv env, TextWriter stdout, string checker, string plugin, string data, List<string> masters)
    {
        var tsv = Path.Combine(voice.Out, "lines.tsv");
        try
        {
            Directory.CreateDirectory(voice.Out);
            if (File.Exists(tsv)) File.Delete(tsv);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            stdout.WriteLine($"RESULT: CANNOT MAKE VOICE ({tsv} can't be replaced: {e.Message}. Fix: close any program using it and run again).");
            return null;
        }
        var line = $"voice-lines {Common.Quote(plugin)} --out {Common.Quote(tsv)} --utf8 --data {Common.Quote(Path.TrimEndingDirectorySeparator(Path.GetFullPath(data)))}";
        foreach (var m in masters) line += $" --masters-dir {Common.Quote(m)}";
        stdout.WriteLine($"listing  the voiced lines of {project.Plugin} with {Path.GetFileName(checker)} voice-lines");
        Log.Write($"voice-lines: in {project.Folder}: \"{checker}\" {line}");
        var r = env.Exec(checker, line, project.Folder, ListTimeout);
        Log.Write($"voice-lines exit code {r.ExitCode}{(r.Why != null ? $" ({r.Why})" : "")}; output:\n{r.Output.TrimEnd()}");
        if (r.Why != null)
        {
            stdout.WriteLine($"RESULT: CANNOT MAKE VOICE ({checker} couldn't be run: {r.Why}). Fix: run it by hand to see why, or give another one with --checker.");
            return null;
        }
        foreach (var l in Common.LastLines(r.Output, int.MaxValue).Where(l => l.StartsWith("WARNING", StringComparison.Ordinal) || l.StartsWith("NOTE", StringComparison.Ordinal)))
            stdout.WriteLine($"         {l}");
        if (r.ExitCode != 0 || !File.Exists(tsv))
        {
            Common.ShowLastLines(stdout, r.Output, 15);
            stdout.WriteLine($"RESULT: CANNOT MAKE VOICE (sf-mod-check voice-lines didn't write the list (exit code {r.ExitCode}; see its message above). Fix: what it says; voice-lines needs sf-mod-check 0.21.0 or later).");
            return null;
        }
        string text;
        try { text = File.ReadAllText(tsv, new UTF8Encoding(false)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            stdout.WriteLine($"RESULT: CANNOT MAKE VOICE ({tsv} can't be read: {e.Message}).");
            return null;
        }
        var lines = ParseList(text, out var error, out var skipped);
        if (lines == null)
        {
            stdout.WriteLine($"RESULT: CANNOT MAKE VOICE ({tsv}: {error}. Fix: use sf-mod-check 0.21.0 or later).");
            return null;
        }
        foreach (var s in skipped) stdout.WriteLine($"WARNING  {s}");
        return lines;
    }

    /// <summary>Reads a voice-lines list (tab-separated, a header line, a field with a quote quoted CSV-style). Null
    /// (with the error) when the header lacks a needed column. Rows whose voice type or file number isn't in the
    /// expected form are left out and named in skipped, since they would become paths.</summary>
    public static List<Line>? ParseList(string text, out string? error, out List<string> skipped)
    {
        error = null;
        skipped = new List<string>();
        var rows = text.Replace("\r\n", "\n").Split('\n').Where(r => r.Length > 0).Select(SplitRow).ToList();
        if (rows.Count == 0)
        {
            error = "it is empty (no header line)";
            return null;
        }
        var header = rows[0].Select(h => h.Trim().ToUpperInvariant()).ToList();
        int Col(string name) => header.IndexOf(name);
        int vt = Col("VOICE TYPE"), fn = Col("FILENAME"), rt = Col("RESPONSE TEXT"), sx = Col("SEX");
        var missing = new[] { ("VOICE TYPE", vt), ("FILENAME", fn), ("RESPONSE TEXT", rt), ("SEX", sx) }.Where(x => x.Item2 < 0).Select(x => x.Item1).ToList();
        if (missing.Count > 0)
        {
            error = $"its header lacks the column(s) {string.Join(", ", missing)}";
            return null;
        }
        var list = new List<Line>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows.Skip(1))
        {
            string Get(int i) => i < row.Count ? row[i] : "";
            var line = new Line(Get(vt).Trim().ToLowerInvariant(), Get(fn).Trim().ToUpperInvariant(), Get(rt).Trim(), Get(sx).Trim());
            if (!VoiceTypeName.IsMatch(line.VoiceType) || !FileNumber.IsMatch(line.File))
            {
                skipped.Add($"the voice-lines row \"{Get(vt)}\" \"{Get(fn)}\" was left out: a voice type must be letters, digits and _, and a file number 8 hex digits.");
                continue;
            }
            if (seen.Add(line.Key)) list.Add(line);
        }
        return list;
    }

    /// <summary>One tab-separated row; a field that starts with a quote runs to the closing quote, with "" for a quote.</summary>
    static List<string> SplitRow(string row)
    {
        var fields = new List<string>();
        var i = 0;
        while (true)
        {
            var sb = new StringBuilder();
            if (i < row.Length && row[i] == '"')
            {
                i++;
                while (i < row.Length)
                {
                    if (row[i] == '"')
                    {
                        if (i + 1 < row.Length && row[i + 1] == '"') { sb.Append('"'); i += 2; continue; }
                        i++;
                        break;
                    }
                    sb.Append(row[i++]);
                }
                while (i < row.Length && row[i] != '\t') sb.Append(row[i++]);
            }
            else
                while (i < row.Length && row[i] != '\t') sb.Append(row[i++]);
            fields.Add(sb.ToString());
            if (i >= row.Length) break;
            i++; // the tab
        }
        return fields;
    }

    /// <summary>The folder holding the voice type folders: source\Sound\Voice\&lt;plugin&gt; when Voice Cloner's
    /// whole output folder is given, else source itself.</summary>
    public static string WavRoot(string source, string plugin)
    {
        foreach (var candidate in new[] { Path.Combine(source, "Sound", "Voice", plugin), Path.Combine(source, "Voice", plugin), Path.Combine(source, plugin) })
            if (Directory.Exists(candidate)) return candidate;
        return source;
    }

    /// <summary>Every &lt;voice type&gt;\&lt;8 hex digits&gt;.wav under the root, keyed "voicetype\FILENAME" (voice type
    /// in lower case, file number in upper case).</summary>
    public static Dictionary<string, string> IndexWavs(string root)
    {
        var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(root)) return found;
        foreach (var dir in Directory.GetDirectories(root).Order(StringComparer.OrdinalIgnoreCase))
        {
            var vt = Path.GetFileName(dir).ToLowerInvariant();
            if (!VoiceTypeName.IsMatch(vt)) continue;
            foreach (var wav in Directory.GetFiles(dir, "*.wav").Order(StringComparer.OrdinalIgnoreCase))
            {
                var id = Path.GetFileNameWithoutExtension(wav).ToUpperInvariant();
                if (FileNumber.IsMatch(id)) found.TryAdd(vt + "\\" + id, wav);
            }
        }
        return found;
    }

    static string Hash(string wav, string text, string face, string conversion)
    {
        using var sha = SHA256.Create();
        using (var s = File.OpenRead(wav))
        {
            var fileHash = sha.ComputeHash(s);
            var all = fileHash.Concat(Encoding.UTF8.GetBytes($"\n{text}\n{face}\n{conversion}")).ToArray();
            return System.Convert.ToHexString(SHA256.HashData(all));
        }
    }

    static Dictionary<string, string> ReadCache(string path)
    {
        try
        {
            if (File.Exists(path) && System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) is { } d)
                return new Dictionary<string, string>(d, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            Log.Write($"voice: the cache {path} can't be read ({e.Message}); every line is made again");
        }
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    static void WriteCache(string path, Dictionary<string, string> cache) =>
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new SortedDictionary<string, string>(cache, StringComparer.OrdinalIgnoreCase), new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

    /// <summary>WwiseConsole.exe: --wwise (a Wwise folder or the exe), else WWISEROOT, else the newest 2021.1 (else
    /// the newest of any version) in an Audiokinetic folder on each drive or in Program Files.</summary>
    public static string? FindWwise(string? option, IHostEnv env, out string? error)
    {
        error = null;
        static string Exe(string root) => Path.Combine(root, "Authoring", "x64", "Release", "bin", "WwiseConsole.exe");
        if (option != null)
        {
            string full;
            try { full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(option.Trim().Trim('"'))); }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
            {
                error = $"--wwise \"{option}\" isn't a usable path ({e.Message}). Give the Wwise folder, such as C:\\Audiokinetic\\Wwise2021.1.14.8108";
                return null;
            }
            if (File.Exists(full) && Path.GetFileName(full).Equals("WwiseConsole.exe", StringComparison.OrdinalIgnoreCase)) return full;
            if (File.Exists(Exe(full))) return Exe(full);
            error = $"--wwise {full}: there is no WwiseConsole.exe there or at {Exe(full)}. Give the Wwise folder, such as C:\\Audiokinetic\\Wwise2021.1.14.8108";
            return null;
        }
        if (env.EnvVar("WWISEROOT") is { Length: > 0 } root)
        {
            var exe = Exe(root.Trim().Trim('"'));
            if (File.Exists(exe)) return exe;
            Log.Write($"WWISEROOT is {root}, but {exe} isn't there");
        }
        var parents = env.DriveRoots().Select(d => Path.Combine(d, "Audiokinetic")).ToList();
        foreach (var pf in new[] { env.EnvVar("ProgramFiles(x86)"), env.EnvVar("ProgramFiles") })
            if (pf is { Length: > 0 }) parents.Add(Path.Combine(pf, "Audiokinetic"));
        var candidates = new List<(string Exe, Version Version)>();
        foreach (var parent in parents.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string[] dirs;
            try { dirs = Directory.Exists(parent) ? Directory.GetDirectories(parent, "Wwise*") : Array.Empty<string>(); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
            foreach (var d in dirs)
                if (File.Exists(Exe(d))) candidates.Add((Exe(d), WwiseVersion(Exe(d)) ?? new Version(0, 0)));
        }
        var best = candidates.Where(c => c.Version.Major == 2021 && c.Version.Minor == 1).OrderByDescending(c => c.Version).Concat(candidates.OrderByDescending(c => c.Version)).FirstOrDefault();
        if (best.Exe != null) return best.Exe;
        error = "Audiokinetic Wwise wasn't found (it converts the WAVs to .wem). " + WwiseHowTo;
        return null;
    }

    /// <summary>The version in the Wwise folder's name (Wwise2021.1.14.8108), from WwiseConsole.exe's path; null
    /// when the name has none.</summary>
    public static Version? WwiseVersion(string wwiseConsole)
    {
        foreach (var part in wwiseConsole.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            var m = Regex.Match(part, @"^Wwise[ _-]?v?(\d+)\.(\d+)\.(\d+)(?:\.(\d+))?$", RegexOptions.IgnoreCase);
            if (m.Success) return new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value), m.Groups[4].Success ? int.Parse(m.Groups[4].Value) : 0);
        }
        return null;
    }

    /// <summary>The Wwise project: voice.wwiseProject, else a copy of the Creation Kit's Tools\wwise\Starfield in
    /// voice.out\wwise (made again when the Creation Kit's project file changes). Null (with the error) when neither is there.</summary>
    static string? WwiseProject(SfModProject.VoiceSection voice, string tools, out string? error)
    {
        error = null;
        if (voice.WwiseProject != null) return voice.WwiseProject;
        var ck = Path.Combine(tools, "wwise", "Starfield");
        var ckProject = Path.Combine(ck, "Starfield.wproj");
        if (!File.Exists(ckProject))
        {
            error = $"the Creation Kit's Wwise project isn't at {ckProject}. {Doctor.CkHowTo} Or give your own copy with voice.wwiseProject";
            return null;
        }
        var copy = Path.Combine(voice.Out, "wwise", "Starfield");
        var stamp = Path.Combine(voice.Out, "wwise", "copied-from.txt");
        var info = new FileInfo(ckProject);
        var want = $"{ckProject}\n{info.Length}\n{info.LastWriteTimeUtc:O}";
        try
        {
            if (File.Exists(stamp) && File.ReadAllText(stamp) == want && File.Exists(Path.Combine(copy, "Starfield.wproj"))) return Path.Combine(copy, "Starfield.wproj");
            if (Directory.Exists(copy)) Directory.Delete(copy, recursive: true);
            CopyTree(ck, copy);
            File.WriteAllText(stamp, want);
            Log.Write($"voice: copied the Creation Kit's Wwise project {ck} to {copy}");
            return Path.Combine(copy, "Starfield.wproj");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            error = $"the Creation Kit's Wwise project couldn't be copied into {copy}: {e.Message}";
            return null;
        }
    }

    static void CopyTree(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.GetFiles(from)) Common.CopyFile(f, Path.Combine(to, Path.GetFileName(f)), overwrite: true);
        foreach (var d in Directory.GetDirectories(from))
            if (!Path.GetFileName(d).StartsWith('.') && new DirectoryInfo(d).LinkTarget == null) CopyTree(d, Path.Combine(to, Path.GetFileName(d)));
    }

    /// <summary>Converts the WAVs to .wem with WwiseConsole convert-external-source, in one run.</summary>
    static int Convert(SfModProject project, SfModProject.VoiceSection voice, IHostEnv env, TextWriter stdout, string? wwiseOption, string tools, string wavRoot,
        List<(Line Line, string Wav, string Face)> todo, string workDir, Func<Line, string, string> rel)
    {
        var wwise = FindWwise(wwiseOption, env, out var error);
        if (wwise == null)
        {
            stdout.WriteLine($"RESULT: CANNOT MAKE VOICE ({error})");
            return ExitCodes.BadInput;
        }
        if (WwiseVersion(wwise) is { } version && !(version.Major == 2021 && version.Minor == 1))
            stdout.WriteLine($"WARNING  {wwise} is Wwise {version}, but the Creation Kit's project is made for Wwise 2021.1, so this one may refuse or change the project. Fix: install Wwise 2021.1 (such as 2021.1.14.8108) and give it with --wwise.");
        var wproj = WwiseProject(voice, tools, out error);
        if (wproj == null)
        {
            stdout.WriteLine($"RESULT: CANNOT MAKE VOICE ({error}).");
            return ExitCodes.BadInput;
        }
        var sources = Path.Combine(workDir, "convert.wsources");
        var output = Path.Combine(workDir, "wem");
        var xml = new StringBuilder();
        xml.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n");
        xml.Append($"<ExternalSourcesList SchemaVersion=\"1\" Root=\"{Xml(Path.GetFullPath(wavRoot))}\">\r\n");
        foreach (var (l, wav, _) in todo)
            xml.Append($"  <Source Path=\"{Xml(Path.GetRelativePath(wavRoot, wav))}\" Conversion=\"{Xml(voice.Conversion)}\" Destination=\"{Xml(l.VoiceType + "\\" + l.File + ".wem")}\"/>\r\n");
        xml.Append("</ExternalSourcesList>\r\n");
        File.WriteAllText(sources, xml.ToString(), new UTF8Encoding(false));
        var args = $"convert-external-source {Common.Quote(wproj)} --platform Windows --source-file {Common.Quote(sources)} --output {Common.Quote(output)}";
        stdout.WriteLine($"wwise    converting {todo.Count} WAV(s) with {wwise}");
        Log.Write($"wwise: in {workDir}: \"{wwise}\" {args}");
        var r = env.Exec(wwise, args, workDir, WwiseTimeout);
        Log.Write($"wwise exit code {r.ExitCode}{(r.Why != null ? $" ({r.Why})" : "")}; output:\n{r.Output.TrimEnd()}");
        if (r.Why != null)
        {
            stdout.WriteLine($"RESULT: CANNOT MAKE VOICE ({wwise} couldn't be run: {r.Why}).");
            return ExitCodes.BadInput;
        }
        var missing = new List<string>();
        foreach (var (l, _, _) in todo)
        {
            var made = Path.Combine(output, "Windows", l.VoiceType, l.File + ".wem");
            if (!File.Exists(made)) { missing.Add(l.Key); continue; }
            var to = Path.Combine(voice.Data, rel(l, ".wem"));
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            Common.CopyFile(made, to, overwrite: true);
        }
        if (missing.Count > 0)
        {
            Common.ShowLastLines(stdout, r.Output, 20);
            stdout.WriteLine($"RESULT: FAILED, Wwise made no .wem for {missing.Count} line(s) (exit code {r.ExitCode}): {Few(missing)}. Fix: see Wwise's message above (run with --log for all of it); check that the WAVs play and that the project has the conversion setting \"{voice.Conversion}\".");
            return ExitCodes.Failed;
        }
        if (r.ExitCode != 0) stdout.WriteLine($"note     Wwise exited with code {r.ExitCode} but made every .wem (the log has its messages)");
        return ExitCodes.Ok;
    }

    static string Xml(string s) => System.Security.SecurityElement.Escape(s) ?? "";

    /// <summary>Makes the lip files: LipGenerator per line (WAV + text + face set into an .animset), then ffxc per voice
    /// type (the .animset files into .ffxanim), each in work\lip\&lt;voice type&gt;.</summary>
    static int Lips(SfModProject.VoiceSection voice, IHostEnv env, TextWriter stdout, string tools, List<(Line Line, string Wav, string Face)> todo, string workDir, Func<Line, string, string> rel)
    {
        var withLips = todo.Where(t => t.Face != "none").ToList();
        if (withLips.Count == 0) return ExitCodes.Ok;
        var lipGenerator = Path.Combine(tools, "LipGenerator", "LipGenerator.exe");
        var ffxc = Path.Combine(tools, "FaceFX", "ffxc.exe");
        var genesis = Path.Combine(tools, "FaceFX", "GenesisDefault.facefx");
        foreach (var needed in new[] { lipGenerator, ffxc, genesis })
            if (!File.Exists(needed))
            {
                stdout.WriteLine($"RESULT: CANNOT MAKE VOICE ({needed} isn't there; it makes the lip files. {Doctor.CkHowTo})");
                return ExitCodes.BadInput;
            }
        stdout.WriteLine($"lips     making {withLips.Count} lip file(s) with LipGenerator and ffxc");
        foreach (var group in withLips.GroupBy(t => t.Line.VoiceType, StringComparer.OrdinalIgnoreCase))
        {
            var faceFile = group.First().Face == "male" ? "StarfieldHumanMale.facefx" : "StarfieldHumanFemale.facefx";
            var faceSet = Path.Combine(tools, "FaceFX", faceFile);
            if (!File.Exists(faceSet))
            {
                stdout.WriteLine($"RESULT: CANNOT MAKE VOICE ({faceSet} isn't there; it is the face set the lip files are made for. {Doctor.CkHowTo})");
                return ExitCodes.BadInput;
            }
            var dir = Path.Combine(workDir, "lip", group.Key);
            Directory.CreateDirectory(dir);
            Common.CopyFile(faceSet, Path.Combine(dir, faceFile), overwrite: true);
            var n = 0;
            foreach (var (l, wav, _) in group)
            {
                Common.CopyFile(wav, Path.Combine(dir, l.File + ".wav"), overwrite: true);
                // LipGenerator is given the line's text for its phonemes; a quote inside it would end the argument
                var text = l.Text.Replace('"', '\'');
                var args = $"{l.File}.wav {Common.Quote(text)} {Common.Quote(faceSet)} {Common.Quote(genesis)} -OutputFileName:{l.File}.animset";
                Log.Write($"lip: in {dir}: \"{lipGenerator}\" {args}");
                var r = env.Exec(lipGenerator, args, dir, LipTimeout);
                Log.Write($"lip exit code {r.ExitCode}{(r.Why != null ? $" ({r.Why})" : "")}; output:\n{r.Output.TrimEnd()}");
                if (r.Why != null || !File.Exists(Path.Combine(dir, l.File + ".animset")))
                {
                    Common.ShowLastLines(stdout, r.Output, 15);
                    stdout.WriteLine($"RESULT: FAILED, LipGenerator made no animation set for {l.Key} ({r.Why ?? $"exit code {r.ExitCode}"}). Fix: see its message above; check that {wav} plays.");
                    return ExitCodes.Failed;
                }
                if (++n % 50 == 0) stdout.WriteLine($"         {group.Key}: {n} of {group.Count()}");
            }
            var proj = Path.Combine(dir, group.Key + ".ffxcproj");
            var yaml = new StringBuilder("version: 1\nitems:\n");
            yaml.Append($"  - facefx_file: {faceFile}\n    mount_files:\n");
            foreach (var (l, _, _) in group) yaml.Append($"      - {l.File}.animset\n");
            File.WriteAllText(proj, yaml.ToString(), new UTF8Encoding(false));
            var outDir = Path.Combine(dir, "out");
            var ffxArgs = $"--nologo -p=x86 {Common.Quote("-o=" + outDir)} {Common.Quote(proj)}";
            Log.Write($"ffxc: in {dir}: \"{ffxc}\" {ffxArgs}");
            var fr = env.Exec(ffxc, ffxArgs, dir, FfxcTimeout);
            Log.Write($"ffxc exit code {fr.ExitCode}{(fr.Why != null ? $" ({fr.Why})" : "")}; output:\n{fr.Output.TrimEnd()}");
            if (fr.Why != null)
            {
                stdout.WriteLine($"RESULT: CANNOT MAKE VOICE ({ffxc} couldn't be run: {fr.Why}).");
                return ExitCodes.BadInput;
            }
            var made = Directory.Exists(outDir)
                ? Directory.GetFiles(outDir, "*.ffxanim", SearchOption.AllDirectories).GroupBy(f => Path.GetFileNameWithoutExtension(f), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var missing = group.Where(t => !made.ContainsKey(t.Line.File)).Select(t => t.Line.Key).ToList();
            if (missing.Count > 0)
            {
                Common.ShowLastLines(stdout, fr.Output, 20);
                stdout.WriteLine($"RESULT: FAILED, ffxc made no lip file for {missing.Count} line(s) (exit code {fr.ExitCode}): {Few(missing)}. Fix: see its message above (run with --log for all of it).");
                return ExitCodes.Failed;
            }
            foreach (var (l, _, _) in group)
            {
                var to = Path.Combine(voice.Data, rel(l, ".ffxanim"));
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                Common.CopyFile(made[l.File], to, overwrite: true);
            }
        }
        return ExitCodes.Ok;
    }
}
