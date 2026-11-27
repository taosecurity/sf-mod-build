// sfmod.json: one project file per mod. It says what the mod is called, where its Papyrus sources are, how to run its
// Mutagen builder, where the build output goes and what else goes into the zip. Paths are relative to the folder that
// holds sfmod.json. Comments (// and /* */) and trailing commas are allowed.
//
//   {
//     "plugin": "MyMod.esm",                       the plugin file the builder writes into dist (required)
//     "name": "MyMod",                             the mod's name for the zip (default: the plugin name without .esm)
//     "version": "0.1.0",                          numbers and dots (required)
//     "scripts": {                                 leave out when the mod has no scripts
//       "source": "Scripts/Source",                one folder, or a list of folders (default Scripts/Source)
//       "namespaces": ["MYM"],                     every .psc in <source>/<namespace>/ ...
//       "files": ["MYM/MyQuestScript"],            ... or an explicit list (namespace/name, .psc optional)
//       "optimize": true                           false compiles without -optimize (default true)
//     },
//     "builder": {                                 exactly one of project, command and prebuilt (required)
//       "project": "Builder",                      a folder holding one .csproj, or the .csproj itself, run with dotnet run
//       "command": ["python", "tools/build.py"],   ... or any program and its first arguments (a list, never a shell)
//       "prebuilt": "Original/MyMod.esm",          ... or a ready plugin, copied into dist under the plugin name
//       "args": ["{data}", "{dist}", "{version}"]  what the builder is given; placeholders below (required with
//     },                                           project and command, refused with prebuilt)
//     "steps": {                                   your own commands, each a list like builder.command (leave out
//       "afterPlugin": [["python", "tools/copy_voice.py", "{dist}"]],  when unused): run at the end of plugin ...
//       "afterPack": [["tools/check_archives.cmd", "{dist}"]]          ... and at the end of pack, in order
//     },
//     "dist": "dist",                              the plugin and the BA2 go here (default dist)
//     "stage": ".build/stage",                     compiled scripts go here (default .build/stage)
//     "xbox": false,                               also make "<plugin> - Main_xbox.ba2" in creations-out (default false)
//     "creationsOut": "creations-out",             where the Creations upload set goes (default creations-out)
//     "assets": {                                  loose files to pack (leave out when the mod has none)
//       "source": "Assets",                        a folder laid out like Data: Meshes\, Materials\, Textures\, ...
//       "xboxTextures": "Assets-xbox"              optional: Xbox-format .dds files for the _xbox texture archive
//     },
//     "archives": [                                more archives pack makes in dist, "<plugin> - <suffix>.ba2" (leave
//       { "suffix": "Voices", "source": "Voices",  out when unused): a folder laid out like Data, packed with Archive2;
//         "compression": "none" },                 compression "default" or "none" (sound), format "general" or "dds"
//       { "suffix": "Extra", "file": "Original/MyMod - Extra.ba2" }   ... or a ready archive, copied as it is
//     ],                                           Main or Textures only when pack doesn't make that one; not with xbox
//     "parts": [                                   other plugins your builder writes into dist (leave out when unused)
//       { "plugin": "MyMod-Extra.esm",             each ships in the zip; with scripts (same shape as above, source
//         "scripts": { "namespaces": ["MYMX"] } }, defaulting to the top-level source) it gets "<part> - Main.ba2"
//       { "plugin": "MyMod-Patch.esm" }            ... or none (a records-only plugin); scripts not with xbox
//       ...                                        each part also gets its own zip {name}-<Part>-{version}.zip; "zipPart":
//                                                  "Patch" picks <Part> (default: the plugin name, cleaned)
//     ],
//     "zip": {
//       "name": "{name}-{version}.zip",            the zip's name (default {name}-{version}.zip)
//       "folder": ".",                             where the zip is written (default: the sfmod.json folder)
//       "files": { "README.md": "README-{name}.md" },  extra files: file here -> name inside the zip (never .psc)
//       "archives": ["MyMod - Extra.ba2"],         archives your builder writes into dist itself: they ship in the zip
//       "split": true                              with parts: one zip per plugin (default); false: one zip for all
//     },
//     "check": {
//       "checker": "path/to/sf-mod-check.exe",     default: sf-mod-check or sfmodcheck on PATH
//       "required": false,                         true: a missing checker fails the build
//       "args": []                                 extra options for the checker's release command
//     }
//   }
//
// Builder placeholders (in builder.args and in builder.command after the program): {data} the game's Data folder,
// {dist}, {scripts} the (first) script source folder, {stage}, {version}. Zip name and zip file name placeholders:
// {name}, {plugin} (without extension), {version}.
//
// Steps placeholders: the builder's, plus {name} and {plugin} (without extension).
//
// builder.command and steps run programs named in this file, so sfmod.json is trusted input, like the builder project
// itself: read a downloaded mod's sfmod.json (validate prints every command as a "runs:" line) before building it. A
// program with a / or \ in it is a path relative to this file's folder; a bare name is looked up on PATH. The working
// folder is this file's folder. A step command that exits non-zero stops plugin or pack, naming the command.
//
// A part's scripts compile into <stage>/parts/<part plugin without extension>/Scripts, with the part's source folders
// and then the top-level ones as imports, and pack makes "<part plugin without extension> - Main.ba2" from them. A
// part plugin can't share its name (without the extension) with the main plugin or another part, and a script can be
// in only one place (the top level or one part). plugin checks that the builder wrote every part plugin into dist, so
// parts can't be used with builder.prebuilt.
//
// An archives source folder must be inside the mod's folder and apart from dist, stage and creations-out; it may be
// filled by an afterPlugin step, so pack (not validate) checks that it is there and holds files, with the assets rules
// (no files at its top; no .psc, plugins, archives or zips). A ready archive (file) may sit anywhere; it is only read.
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SfModBuild;

/// <summary>One problem in an sfmod.json: the field (a JSON path such as builder.args[1]), what is wrong and the fix.</summary>
internal sealed record ProjectProblem(string Field, string Problem, string Fix, bool IsWarning = false)
{
    public string Format(string file) =>
        $"{(IsWarning ? "WARNING" : "PROBLEM")}: {file}: {Field}: {Problem} Fix: {Fix}";
}

/// <summary>A loaded sfmod.json with every path made absolute.</summary>
internal sealed class SfModProject
{
    public required string File { get; init; }
    public required string Folder { get; init; }
    public required string Name { get; init; }
    public required string Version { get; init; }
    /// <summary>The plugin file name, such as MyMod.esm.</summary>
    public required string Plugin { get; init; }
    public string PluginBase => Path.GetFileNameWithoutExtension(Plugin);
    public string Ba2Name => $"{PluginBase} - Main.ba2";
    public string XboxBa2Name => $"{PluginBase} - Main_xbox.ba2";
    public string TexturesBa2Name => $"{PluginBase} - Textures.ba2";
    public string XboxTexturesBa2Name => $"{PluginBase} - Textures_xbox.ba2";
    public ScriptsSection? Scripts { get; init; }
    public AssetsSection? Assets { get; init; }
    /// <summary>True when the build makes "&lt;plugin&gt; - Main.ba2": the mod has scripts or general loose files.</summary>
    public bool HasMainArchive => Scripts != null || Assets is { General.Count: > 0 };
    /// <summary>True when the build makes "&lt;plugin&gt; - Textures.ba2": the mod has loose .dds files.</summary>
    public bool HasTexturesArchive => Assets is { Textures.Count: > 0 };
    /// <summary>The archives pack puts in dist, in the order it makes them: Main and Textures from the scripts and
    /// assets, then the entries of the top-level archives list.</summary>
    public IEnumerable<string> ArchiveNames =>
        (HasMainArchive ? new[] { Ba2Name } : Array.Empty<string>()).Concat(HasTexturesArchive ? new[] { TexturesBa2Name } : Array.Empty<string>())
        .Concat(Parts.Where(x => x.Scripts != null).Select(x => x.Ba2Name))
        .Concat(Archives.Select(a => a.Name));
    /// <summary>parts: the other plugins the builder writes into dist, each with its own scripts or none.</summary>
    public IReadOnlyList<Part> Parts { get; init; } = Array.Empty<Part>();
    /// <summary>Every script source folder: the top-level ones and each part's (never emptied, never packed).</summary>
    public IEnumerable<string> AllScriptSources =>
        (Scripts?.Sources ?? Array.Empty<string>()).Concat(Parts.SelectMany(x => x.Scripts?.Sources ?? Array.Empty<string>()))
        .Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>One entry of parts: another plugin in dist and, optionally, its own scripts, which compile into
    /// &lt;stage&gt;/parts/&lt;plugin base&gt;/Scripts and are packed into "&lt;plugin base&gt; - Main.ba2".</summary>
    public sealed record Part(string Plugin, ScriptsSection? Scripts, string? ZipPart = null)
    {
        public string PluginBase => Path.GetFileNameWithoutExtension(Plugin);
        public string Ba2Name => $"{PluginBase} - Main.ba2";
        /// <summary>The part's stage folder (the archive's root): &lt;stage&gt;/parts/&lt;plugin base&gt;.</summary>
        public string StageIn(string stage) => System.IO.Path.Combine(stage, "parts", PluginBase);
    }
    /// <summary>The top-level archives list: more archives pack makes in dist, each "&lt;plugin&gt; - &lt;suffix&gt;.ba2".</summary>
    public IReadOnlyList<ExtraArchive> Archives { get; init; } = Array.Empty<ExtraArchive>();

    /// <summary>One entry of the archives list: the archive's file name, and either the folder packed into it (with
    /// Archive2's compression and format) or a ready archive copied as it is.</summary>
    public sealed record ExtraArchive(string Name, string? Source, string? File, bool Uncompressed, bool Dds);
    /// <summary>builder.project: the .csproj run with dotnet run, or null when another kind of builder is used.</summary>
    public string? BuilderProject { get; init; }
    /// <summary>builder.command: the program (a full path when it was given as a path, else the bare name looked up
    /// on PATH) and its leading arguments, or null.</summary>
    public IReadOnlyList<string>? BuilderCommand { get; init; }
    /// <summary>builder.prebuilt: the full path of a ready plugin copied into dist, or null.</summary>
    public string? BuilderPrebuilt { get; init; }
    /// <summary>builder.args (empty with prebuilt).</summary>
    public required IReadOnlyList<string> BuilderArgs { get; init; }
    /// <summary>"project", "command" or "prebuilt".</summary>
    public string BuilderKind => BuilderProject != null ? "project" : BuilderCommand != null ? "command" : "prebuilt";
    /// <summary>The folders holding the builder (its project, its program when given as a path, or the ready plugin):
    /// never emptied and never packed.</summary>
    public IEnumerable<string> BuilderFolders =>
        BuilderProject != null ? new[] { Path.GetDirectoryName(BuilderProject)! }
        : BuilderCommand != null ? (Path.IsPathRooted(BuilderCommand[0]) ? new[] { Path.GetDirectoryName(BuilderCommand[0])! } : Array.Empty<string>())
        : new[] { Path.GetDirectoryName(BuilderPrebuilt!)! };
    /// <summary>steps.afterPlugin: commands run at the end of plugin, each the program (resolved like builder.command's)
    /// and its arguments with the placeholders still in.</summary>
    public IReadOnlyList<IReadOnlyList<string>> AfterPlugin { get; init; } = Array.Empty<IReadOnlyList<string>>();
    /// <summary>steps.afterPack: commands run at the end of pack.</summary>
    public IReadOnlyList<IReadOnlyList<string>> AfterPack { get; init; } = Array.Empty<IReadOnlyList<string>>();
    public required string Dist { get; init; }
    public required string Stage { get; init; }
    public bool Xbox { get; init; }
    public required string CreationsOut { get; init; }
    public required string ZipName { get; init; }
    public required string ZipFolder { get; init; }
    public string ZipPath => Path.Combine(ZipFolder, ZipName);
    /// <summary>zip.split (default true): with parts, one zip per plugin. False keeps every plugin in the one zip.</summary>
    public bool ZipSplit { get; init; } = true;
    /// <summary>Extra zip files in the order written: full path here, name inside the zip.</summary>
    public required IReadOnlyList<(string Source, string NameInZip)> ZipFiles { get; init; }
    /// <summary>zip.archives: the .ba2 files the builder itself writes into dist; they ship in the zip and aren't stray.</summary>
    public IReadOnlyList<string> ZipArchives { get; init; } = Array.Empty<string>();
    public string? Checker { get; init; }
    public bool CheckRequired { get; init; }
    public required IReadOnlyList<string> CheckArgs { get; init; }

    public sealed class ScriptsSection
    {
        public required IReadOnlyList<string> Sources { get; init; }
        public required IReadOnlyList<string> Namespaces { get; init; }
        /// <summary>Explicit scripts as "Namespace\Name" (no .psc); empty when the namespaces' folders are used.</summary>
        public required IReadOnlyList<string> Files { get; init; }
        /// <summary>Whether the compiler gets -optimize (scripts.optimize, default true).</summary>
        public bool Optimize { get; init; } = true;
    }

    /// <summary>One loose file: its full path here and its path inside the archive (relative to the assets folder,
    /// with the system's separator), such as Meshes\MyMod\Box.nif.</summary>
    public sealed record AssetFile(string Full, string Relative);

    /// <summary>The mod's loose files, read from the assets folder when the project is loaded.</summary>
    public sealed class AssetsSection
    {
        public required string Source { get; init; }
        /// <summary>The folder of Xbox-format textures, or null to pack the PC textures into the _xbox archive too.</summary>
        public string? XboxTextures { get; init; }
        /// <summary>Everything that isn't a .dds, for "&lt;plugin&gt; - Main.ba2", sorted by path.</summary>
        public required IReadOnlyList<AssetFile> General { get; init; }
        /// <summary>The .dds files, for "&lt;plugin&gt; - Textures.ba2", sorted by path.</summary>
        public required IReadOnlyList<AssetFile> Textures { get; init; }
        /// <summary>The .dds files from XboxTextures (empty when it isn't given).</summary>
        public required IReadOnlyList<AssetFile> XboxTextureFiles { get; init; }
        /// <summary>True when Main holds a .wem or .ffxanim: those play silent when stored compressed, so Main is
        /// packed with -compression=None.</summary>
        public bool UncompressedMain => General.Any(f => Uncompressed(f.Relative));
        public static bool Uncompressed(string path) =>
            path.EndsWith(".wem", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".ffxanim", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Two loose files whose paths differ only in case: the words validate and pack both use.</summary>
    public static string CaseTwinProblem(string field, string file, string other) =>
        $"{file} and {other} in {field} differ only in case, so they would be one file in the archive (the game and Windows don't tell them apart).";
    public const string CaseTwinFix = "keep one of them and delete or rename the other.";

    public const string FileName = "sfmod.json";

    static readonly string[] TopFields = { "$schema", "name", "version", "plugin", "scripts", "builder", "steps", "dist", "stage", "xbox", "creationsOut", "assets", "archives", "parts", "zip", "check" };
    static readonly string[] PartFields = { "plugin", "scripts", "zipPart" };
    static readonly string[] ArchiveFields = { "suffix", "source", "file", "compression", "format" };
    static readonly string[] StepsFields = { "afterPlugin", "afterPack" };
    public static readonly string[] StepPlaceholders = { "data", "dist", "scripts", "stage", "version", "name", "plugin" };
    static readonly string[] AssetsFields = { "source", "xboxTextures" };
    // Never packed into an archive: plugins and archives belong at the top of the zip, Papyrus source never ships.
    public static readonly string[] RefusedAssetExtensions = { ".psc", ".esm", ".esp", ".esl", ".ba2", ".zip", ".7z", ".rar" };
    // Artists' source formats the game doesn't read; packed anyway, with a warning, in case the modder means it.
    static readonly string[] SourceArtExtensions = { ".tif", ".tiff", ".fbx", ".wav", ".psd", ".blend", ".png", ".tga", ".jpg", ".jpeg", ".xcf", ".spp" };
    // Left out without a word: system clutter and dot files such as .gitkeep.
    public static readonly string[] SkippedAssetNames ={ "Thumbs.db", "desktop.ini" };
    static readonly string[] ScriptsFields = { "source", "namespaces", "files", "optimize" };
    static readonly string[] BuilderFields = { "project", "command", "prebuilt", "args" };
    static readonly string[] ZipFields = { "name", "folder", "files", "archives", "split" };
    static readonly string[] CheckFields = { "checker", "required", "args" };
    public static readonly string[] BuilderPlaceholders = { "data", "dist", "scripts", "stage", "version" };
    public static readonly string[] ZipPlaceholders = { "name", "plugin", "version" };
    static readonly string[] PluginExtensions = { ".esm", ".esp", ".esl" };
    // Starfield drops a plugin from Plugins.txt when its name ends in an official plugin's name.
    static readonly string[] OfficialEndings = { "Starfield", "Constellation", "OldMars", "ShatteredSpace" };
    // Official Creations' plugins (SFBGS003.esm and so on): a mod must never take one of these names.
    static readonly System.Text.RegularExpressions.Regex SfbgsName =
        new(@"^SFBGS[0-9A-Fa-f]{3}$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    static readonly System.Text.RegularExpressions.Regex SfbgsEnding =
        new(@".SFBGS[0-9A-Fa-f]{3}$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>Reads and checks an sfmod.json. Returns null when it has a PROBLEM; problems (and warnings) are
    /// listed either way.</summary>
    public static SfModProject? Load(string file, List<ProjectProblem> problems)
    {
        var full = Path.GetFullPath(file);
        if (Directory.Exists(full)) full = Path.Combine(full, FileName);
        Log.Write($"project file: {full}");
        if (!System.IO.File.Exists(full))
        {
            problems.Add(new("(file)", $"there is no {Path.GetFileName(full)} at {Path.GetDirectoryName(full)}.",
                $"run the command in the mod's folder, give the path to its {FileName}, or make one (dotnet new sfmod makes a starter project)."));
            return null;
        }
        string text;
        try { text = System.IO.File.ReadAllText(full); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            problems.Add(new("(file)", $"it can't be read ({e.Message}).", "check that the file isn't open elsewhere and that this account can read it."));
            return null;
        }
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(text, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException e)
        {
            var where = e.LineNumber is { } l ? $" at line {l + 1}, column {(e.BytePositionInLine ?? 0) + 1}" : "";
            problems.Add(new("(file)", $"it isn't valid JSON{where}: {FirstSentence(e.Message)}",
                "look for a missing comma, quote or bracket just before that spot. Backslashes in paths must be doubled (\\\\) or written as /."));
            return null;
        }
        using (doc)
        {
            var r = new Reader(Path.GetDirectoryName(full)!, problems);
            var p = r.Project(doc.RootElement, full);
            var errors = problems.Count(x => !x.IsWarning);
            Log.Write($"project file read: {errors} problem(s), {problems.Count - errors} warning(s)");
            return errors == 0 ? p : null;
        }
    }

    static string FirstSentence(string message)
    {
        var i = message.IndexOf(". ", StringComparison.Ordinal);
        return i > 0 ? message[..(i + 1)] : message;
    }

    /// <summary>Puts values into a template such as "{name}-{version}.zip". Unknown placeholders are left as they are
    /// (the loader has already refused them).</summary>
    public static string Expand(string template, IReadOnlyDictionary<string, string> values) =>
        Regex.Replace(template, @"\{([A-Za-z]+)\}", m => values.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);

    sealed class Reader
    {
        readonly string folder;
        readonly List<ProjectProblem> problems;
        public Reader(string folder, List<ProjectProblem> problems) { this.folder = folder; this.problems = problems; }

        void Error(string field, string problem, string fix) => problems.Add(new(field, problem, fix));
        void Warn(string field, string problem, string fix) => problems.Add(new(field, problem, fix, IsWarning: true));

        public SfModProject? Project(JsonElement root, string file)
        {
            if (root.ValueKind != JsonValueKind.Object)
            {
                Error("(file)", $"it holds a JSON {Kind(root)}, not an object.", "wrap the settings in { }, for example { \"plugin\": \"MyMod.esm\", ... }.");
                return null;
            }
            Fields(root, "", TopFields);

            var plugin = String(root, "plugin", required: true, example: "\"MyMod.esm\"");
            if (plugin != null) plugin = CheckPlugin(plugin);
            var name = String(root, "name", required: false, example: "\"MyMod\"");
            if (name != null && (!GoodFileName(name) || name.Contains('/') || name.Contains('\\')))
                Error("name", $"\"{name}\" can't be part of a file name (it has / \\ : * ? \" < > | or is empty).", "use letters, digits, dashes and dots, for example \"MyMod\".");
            name ??= plugin != null ? Path.GetFileNameWithoutExtension(plugin) : null;

            var version = String(root, "version", required: true, example: "\"0.1.0\"");
            if (version != null && !Regex.IsMatch(version, @"^\d+(\.\d+){0,3}$"))
                Error("version", $"\"{version}\" isn't a version made of numbers and dots.", "write it like \"0.1.0\" or \"1.2\" (up to four numbers).");

            var scripts = Scripts(root);
            var builder = Builder(root);
            var steps = Steps(root, scripts != null);
            var dist = Folder(root, "dist", "dist");
            var stage = Folder(root, "stage", Path.Combine(".build", "stage"));
            if (dist != null && stage != null && SamePath(dist, stage))
                Error("stage", "it is the same folder as dist.", "use separate folders, for example \"dist\" and \".build/stage\" (stage is emptied on every build).");
            if (stage != null && SamePath(stage, folder))
                Error("stage", "it is the mod's own folder, which every build empties.", "use a folder of its own, for example \".build/stage\".");
            if (dist != null && SamePath(dist, folder))
                Error("dist", "it is the mod's own folder.", "use a folder of its own, for example \"dist\".");
            var xbox = Bool(root, "xbox", false);
            var creationsOut = Folder(root, "creationsOut", "creations-out");
            if (creationsOut != null && SamePath(creationsOut, folder))
                Error("creationsOut", "it is the mod's own folder, where pack deletes the files earlier builds made when xbox is on.","use a folder of its own, for example \"creations-out\".");
            var parts = Parts(root, plugin, scripts, xbox, builder?.Prebuilt != null);
            // The tool empties stage and deletes its own earlier files in dist and creations-out before writing, so
            // these three must be folders of their own inside the mod's folder.
            foreach (var (field, path) in new[] { ("dist", dist), ("stage", stage), ("creationsOut", creationsOut) })
                if (path != null && !SamePath(path, folder) && !IsUnder(path, folder))
                    Error(field, $"{path} isn't inside the mod's folder {folder}, and every build {(field == "stage" ? "empties it" : "deletes the files earlier builds made there")}.",
                        $"give a folder inside the mod's folder, relative to sfmod.json, for example \"{(field == "stage" ? ".build/stage" : field == "dist" ? "dist" : "creations-out")}\".");
            // a builder file inside an output folder could be deleted or refused before it is used
            var programs = new List<(string Field, string? File)>();
            if (builder != null)
                programs.AddRange(new[] { ("builder.command[0]", builder.Command is { } c && Path.IsPathRooted(c[0]) ? c[0] : null), ("builder.prebuilt", builder.Prebuilt) });
            if (steps is { } st)
                foreach (var (list, commands) in new[] { ("afterPlugin", st.AfterPlugin), ("afterPack", st.AfterPack) })
                    for (var i = 0; i < commands.Count; i++)
                        if (Path.IsPathRooted(commands[i][0])) programs.Add(($"steps.{list}[{i}][0]", commands[i][0]));
            foreach (var (bField, bFile) in programs)
                if (bFile != null)
                    foreach (var (field, path) in new[] { ("dist", dist), ("stage", stage), ("creationsOut", creationsOut) })
                        if (path != null && IsUnder(bFile, path))
                            Error(bField, $"{bFile} is inside {field} ({path}), {Clears(field)}.",
                                "keep it in a folder of its own, outside dist, stage and creations-out.");
            var assetsOk = true;
            AssetsSection? assets = null;
            if (dist != null && stage != null && creationsOut != null)
            {
                var keep = new List<(string, string)> { ("dist", dist), ("stage", stage), ("creationsOut", creationsOut) };
                var hold = new List<(string, string)>();
                if (builder?.Project != null) hold.Add(("the builder", Path.GetDirectoryName(builder.Project)!));
                if (builder?.Command is { } cmd && Path.IsPathRooted(cmd[0])) hold.Add(("the builder program", cmd[0]));
                if (builder?.Prebuilt != null) hold.Add(("the prebuilt plugin", builder.Prebuilt));
                hold.AddRange(programs.Where(x => x.Field.StartsWith("steps.", StringComparison.Ordinal)).Select(x => ("a steps program", x.File!)));
                if (scripts != null) hold.AddRange(scripts.Sources.Select(s => ("a script source folder", s)));
                if (parts != null)
                    hold.AddRange(parts.SelectMany(x => x.Scripts?.Sources ?? Array.Empty<string>()).Select(s => ("a part's script source folder", s)));
                assets = Assets(root, xbox, keep, hold, out assetsOk);
            }
            var packed = new List<(string, string)>();
            if (assets?.Source != null) packed.Add(("assets.source", assets.Source));
            if (assets?.XboxTextures != null) packed.Add(("assets.xboxTextures", assets.XboxTextures));
            var extra = Archives(root, plugin, xbox, scripts != null || assets is { General.Count: > 0 }, assets is { Textures.Count: > 0 },
                dist, stage, creationsOut, packed);
            var (zipName, zipFolder, zipFiles, zipArchives) = Zip(root, name, plugin, version);
            // creationsOut inside dist: the next build's plugin step empties dist and would delete the user's files there
            if (creationsOut != null && dist != null && (SamePath(creationsOut, dist) || IsUnder(creationsOut, dist)))
                Error("creationsOut", $"it is {(SamePath(creationsOut, dist) ? "the same folder as" : "inside")} dist ({dist}), which the plugin step empties on every build, files you added included.", "use a folder of its own, for example \"creations-out\".");
            // zip.folder in stage: every build empties stage and the zips would go with it
            if (zipFolder != null && stage != null && (SamePath(zipFolder, stage) || IsUnder(zipFolder, stage)))
                Error("zip.folder", $"it is {(SamePath(zipFolder, stage) ? "the same folder as" : "inside")} stage ({stage}), which every build empties.", "use a folder outside dist and stage, for example the mod's own folder (the default).");
            var zipSplit = true;
            if (root.TryGetProperty("zip", out var zsec) && zsec.ValueKind == JsonValueKind.Object && zsec.TryGetProperty("split", out var zs) && zs.ValueKind != JsonValueKind.Null)
            {
                if (zs.ValueKind is JsonValueKind.True or JsonValueKind.False) zipSplit = zs.GetBoolean();
                else Error("zip.split", $"it is a {Kind(zs)}, not true or false.", "write true (one zip per plugin, the default) or false (every plugin in one zip).");
            }
            var (checker, checkRequired, checkArgs) = Check(root);

            if (plugin == null || name == null || version == null || builder == null || steps == null || dist == null
                || stage == null || creationsOut == null || parts == null || !assetsOk || extra == null || zipName == null || zipFolder == null || zipFiles == null || zipArchives == null || checkArgs == null)
                return null;
            var p = new SfModProject
            {
                File = file, Folder = folder, Name = name, Version = version, Plugin = plugin, Scripts = scripts, Assets = assets,
                BuilderProject = builder.Project, BuilderCommand = builder.Command, BuilderPrebuilt = builder.Prebuilt,
                BuilderArgs = builder.Args, AfterPlugin = steps.Value.AfterPlugin, AfterPack = steps.Value.AfterPack, Dist = dist, Stage = stage, Xbox = xbox,
                CreationsOut = creationsOut, Parts = parts, Archives = extra, ZipName = zipName, ZipFolder = zipFolder, ZipFiles = zipFiles, ZipArchives = zipArchives,
                Checker = checker, CheckRequired = checkRequired, CheckArgs = checkArgs, ZipSplit = zipSplit,
            };
            foreach (var a in zipArchives)
                if (extra.Any(x => string.Equals(x.Name, a, StringComparison.OrdinalIgnoreCase)))
                    Error("zip.archives", $"\"{a}\" is also in the archives list, which pack makes.", "list it in one place only: in archives when pack makes or copies it, in zip.archives when your builder writes it.");
            // a part's archive may not take a name another archive of this build has
            var otherArchives = new[] { p.Ba2Name, p.TexturesBa2Name, p.XboxBa2Name, p.XboxTexturesBa2Name }
                .Concat(extra.Select(x => x.Name)).Concat(zipArchives).ToList();
            for (var i = 0; i < parts.Count; i++)
                if (parts[i].Scripts != null && otherArchives.Contains(parts[i].Ba2Name, StringComparer.OrdinalIgnoreCase))
                    Error($"parts[{i}].plugin", $"its archive \"{parts[i].Ba2Name}\" has the name of another archive of this build.", "give the part plugin another name, or rename the other archive's suffix.");
            // archives are refused per entry above, so only the plugins' names are left to check
            foreach (var (_, inZip) in zipFiles)
                if (string.Equals(inZip, p.Plugin, StringComparison.OrdinalIgnoreCase) || parts.Any(x => string.Equals(inZip, x.Plugin, StringComparison.OrdinalIgnoreCase)))
                    Error("zip.files", $"\"{inZip}\" is a name the build itself puts in the zip (the plugin and its archives come from dist).", "leave the plugin and the archives out of zip.files.");
            // all four of pack's names are refused, made by this build or not: pack deletes the PC ones from dist as
            // old leftovers and writes the Xbox ones into creations-out over whatever is there
            var packNames = new[] { p.Ba2Name, p.TexturesBa2Name, p.XboxBa2Name, p.XboxTexturesBa2Name };
            foreach (var a in zipArchives)
                if (packNames.Contains(a, StringComparer.OrdinalIgnoreCase))
                    Error("zip.archives", $"\"{a}\" is a name that sf-mod-build pack uses for its own archives.", "give your builder's archive another name (for example \"MyMod - Extra.ba2\"), and list only archives your builder writes.");
            // the Creations set needs an Xbox build of every archive, and pack can make one only of its own archives
            if (xbox && zipArchives.Count > 0)
                Error("zip.archives", "it can't be used with \"xbox\": true, because pack can't make the Xbox copies of archives your builder writes, so the Creations set would lack them.",
                    "leave \"xbox\" off for this mod, or have pack make the archives instead (put the files in assets.source and leave zip.archives out).");
            // one zip per plugin: the same refusals zip makes, so validate reports them before a build
            if (SfModBuild.Zip.Splits(p))
            {
                for (var i = 0; i < parts.Count; i++)
                    if (SfModBuild.Zip.PartZipPath(p, parts[i]) == null)
                        Error($"parts[{i}].plugin", $"the part {parts[i].Plugin} has no letters or digits to name its zip with.", "set \"zipPart\" on it, for example \"zipPart\": \"Patch\".");
                if (SfModBuild.Zip.SharedZipName(p) is { } shared)
                    Error("parts", $"two zips would both be \"{shared}\".", "set \"zipPart\" on one of the parts, or change zip.name.");
                foreach (var a in zipArchives)
                    if (SfModBuild.Zip.ArchiveOwner(p, a) == null)
                        Error("zip.archives", $"zip can't tell which plugin owns the archive \"{a}\": no plugin's name followed by \" - \" starts it.",
                            $"name it \"<plugin> - <suffix>.ba2\", for example \"{p.PluginBase} - Extra.ba2\", so it goes with that plugin, or set \"split\": false.");
            }
            return p;
        }

        /// <summary>parts: the other plugins the builder writes into dist, each with optional scripts of its own (the
        /// source defaults to the top-level source folders). Empty when left out; null when it has a PROBLEM.</summary>
        List<SfModProject.Part>? Parts(JsonElement root, string? plugin, SfModProject.ScriptsSection? scripts, bool xbox, bool prebuilt)
        {
            var list = new List<SfModProject.Part>();
            if (!root.TryGetProperty("parts", out var l) || l.ValueKind == JsonValueKind.Null) return list;
            const string example = "[ { \"plugin\": \"MyMod-Extra.esm\", \"scripts\": { \"namespaces\": [\"MYMX\"] } } ]";
            if (l.ValueKind != JsonValueKind.Array)
            {
                Error("parts", $"it is a {Kind(l)}, not a list.", $"write it as a list of plugins, for example \"parts\": {example}.");
                return null;
            }
            var before = problems.Count(x => !x.IsWarning);
            if (prebuilt && l.GetArrayLength() > 0)
                Error("parts", "a prebuilt plugin copies only the one plugin, so the part plugins would never be in dist.",
                    "use \"command\" with a program that writes every plugin into dist (it may only copy them), or leave parts out.");
            // the stems are compared, because a plugin's archive is named after its stem: MyMod.esm and MyMod.esp share one
            var stems = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (plugin != null) stems[Path.GetFileNameWithoutExtension(plugin)] = "the main plugin";
            var owner = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var x in Compile.ScriptList(scripts)) owner[x.Name] = "the top-level scripts";
            var i = 0;
            foreach (var e in l.EnumerateArray())
            {
                var at = $"parts[{i++}]";
                if (e.ValueKind != JsonValueKind.Object)
                {
                    Error(at, $"it is a {Kind(e)}, not an object.", $"write each part as an object, for example {example}.");
                    continue;
                }
                Fields(e, at + ".", PartFields);
                var name = String(e, "plugin", required: true, example: "\"MyMod-Extra.esm\"", field: at + ".plugin");
                if (name != null) name = CheckPlugin(name, at + ".plugin");
                if (name != null && stems.TryGetValue(Path.GetFileNameWithoutExtension(name), out var clash))
                {
                    Error(at + ".plugin", $"\"{name}\" has the same name as {clash} (without the extension), so the two would share one archive name.", "give each plugin a name of its own, for example \"MyMod-Extra.esm\".");
                    name = null;
                }
                if (name != null) stems[Path.GetFileNameWithoutExtension(name)] = $"{at}.plugin";
                var own = Scripts(e, at + ".scripts", scripts?.Sources, scripts?.Optimize ?? true);
                if (own != null)
                {
                    if (xbox)
                        Error(at + ".scripts", "a part with scripts can't be used with \"xbox\": true in this version: pack makes the Xbox archive of the main plugin only, so the Creations set would lack the part's scripts.",
                            "leave \"xbox\" off for this mod, or move the part's scripts into the top-level scripts.");
                    foreach (var x in Compile.ScriptList(own))
                        if (owner.TryGetValue(x.Name, out var first))
                            Error(at + ".scripts", $"the script {x.Name} is also in {first}, so two archives would hold it and whichever loaded last would win.", "list each script in one place only.");
                        else owner[x.Name] = at + ".scripts";
                }
                string? zipPart = null;
                if (e.TryGetProperty("zipPart", out var zp) && zp.ValueKind != JsonValueKind.Null)
                {
                    zipPart = String(e, "zipPart", false, "\"Patch\"", at + ".zipPart");
                    if (zipPart != null && (zipPart.Length == 0 || !zipPart.All(char.IsAsciiLetterOrDigit)))
                    {
                        Error(at + ".zipPart", $"\"{zipPart}\" isn't letters and digits only.", "give the word that goes in the part's zip name, such as \"Patch\" (letters and digits only, no spaces), or leave zipPart out.");
                        zipPart = null;
                    }
                }
                if (name != null) list.Add(new SfModProject.Part(name, own, zipPart));
            }
            return problems.Count(x => !x.IsWarning) == before ? list : null;
        }

        /// <summary>The top-level archives list: more archives pack makes in dist. A source folder is only checked for
        /// where it is (an afterPlugin step may fill it; pack checks its files); a file must exist now. Empty when left
        /// out; null when it has a PROBLEM.</summary>
        List<SfModProject.ExtraArchive>? Archives(JsonElement root, string? plugin, bool xbox, bool hasMain, bool hasTextures, string? dist, string? stage, string? creationsOut,
            List<(string Field, string Path)> packed)
        {
            // packed: folders already packed into an archive (assets.source, assets.xboxTextures and, as the list is
            // read, each earlier entry's source); a source that is, holds or is inside one would ship files twice
            var list = new List<SfModProject.ExtraArchive>();
            if (!root.TryGetProperty("archives", out var l) || l.ValueKind == JsonValueKind.Null) return list;
            const string example = "[ { \"suffix\": \"Voices\", \"source\": \"Voices\", \"compression\": \"none\" } ]";
            if (l.ValueKind != JsonValueKind.Array)
            {
                Error("archives", $"it is a {Kind(l)}, not a list.", $"write it as a list of archives, for example \"archives\": {example}.");
                return null;
            }
            var before = problems.Count(x => !x.IsWarning);
            if (xbox && l.GetArrayLength() > 0)
                Error("archives", "it can't be used with \"xbox\": true in this version: pack can't make a correct Xbox copy of these archives (a PC .wem plays silent on Xbox, and a copied archive has no Xbox copy), so the Creations set would lack them.",
                    "leave \"xbox\" off for this mod, or put the files in assets.source so pack makes Main and Textures for both platforms.");
            var stem = plugin != null ? Path.GetFileNameWithoutExtension(plugin) : null;
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var i = 0;
            foreach (var e in l.EnumerateArray())
            {
                var at = $"archives[{i++}]";
                if (e.ValueKind != JsonValueKind.Object)
                {
                    Error(at, $"it is a {Kind(e)}, not an object.", $"write each archive as an object, for example {example}.");
                    continue;
                }
                Fields(e, at + ".", ArchiveFields);
                var suffix = String(e, "suffix", required: true, example: "\"Voices\"", field: at + ".suffix");
                if (suffix != null)
                {
                    if (!GoodFileName(suffix) || suffix.Contains('/') || suffix.Contains('\\') || suffix.EndsWith(".ba2", StringComparison.OrdinalIgnoreCase))
                    {
                        Error(at + ".suffix", $"\"{suffix}\" can't be the end of an archive name (it has / \\ : * ? \" < > |, spaces at an end, or .ba2).", "give only the word after \"<plugin> - \", for example \"Voices\" (the archive is then \"MyMod - Voices.ba2\").");
                        suffix = null;
                    }
                    else if (suffix.Equals("Main", StringComparison.OrdinalIgnoreCase) || suffix.Equals("Textures", StringComparison.OrdinalIgnoreCase))
                    {
                        var main = suffix.Equals("Main", StringComparison.OrdinalIgnoreCase);
                        suffix = main ? "Main" : "Textures";
                        if (main ? hasMain : hasTextures)
                        {
                            Error(at + ".suffix", main ? "pack already makes \"<plugin> - Main.ba2\" from the scripts and the general loose files."
                                : "pack already makes \"<plugin> - Textures.ba2\" from the .dds files in assets.source.",
                                "give the archive another suffix, or move its files into assets.source and remove this entry.");
                            suffix = null;
                        }
                    }
                    else if (suffix.Equals("Main_xbox", StringComparison.OrdinalIgnoreCase) || suffix.Equals("Textures_xbox", StringComparison.OrdinalIgnoreCase))
                    {
                        Error(at + ".suffix", $"\"{suffix}\" is the name pack uses for its own Xbox archives.", "give the archive another suffix, for example \"Voices\".");
                        suffix = null;
                    }
                }
                string? name = suffix != null && stem != null ? $"{stem} - {suffix}.ba2" : null;
                if (name != null && !taken.Add(name))
                {
                    Error(at + ".suffix", $"\"{name}\" is in the archives list twice.", "give each archive its own suffix.");
                    name = null;
                }
                var kinds = new[] { "source", "file" }.Where(k => e.TryGetProperty(k, out var v) && v.ValueKind != JsonValueKind.Null).ToList();
                if (kinds.Count != 1)
                {
                    Error(at, kinds.Count == 0 ? "it gives neither \"source\" nor \"file\"." : "it gives both \"source\" and \"file\".",
                        "give exactly one: \"source\", a folder laid out like Data to pack, or \"file\", a ready .ba2 to copy as it is.");
                    continue;
                }
                string? source = null, file = null;
                bool uncompressed = false, dds = false;
                if (kinds[0] == "source")
                {
                    source = Folder(e, "source", "Voices", at + ".source");
                    if (source != null && !IsUnder(source, folder))
                    {
                        Error(at + ".source", $"{source} isn't a folder inside the mod's folder {folder}.", "give a folder inside the mod's folder, relative to sfmod.json, for example \"Voices\".");
                        source = null;
                    }
                    foreach (var (field, path) in new[] { ("dist", dist), ("stage", stage), ("creationsOut", creationsOut) })
                        if (source != null && path != null && (SamePath(source, path) || IsUnder(source, path) || IsUnder(path, source)))
                        {
                            Error(at + ".source", $"{source} is, holds or is inside {field} ({path}), {Clears(field)}.", "keep the archive's files in a folder of their own, for example \"Voices\".");
                            source = null;
                        }
                    foreach (var (field, path) in packed)
                        if (source != null && (SamePath(source, path) || IsUnder(source, path) || IsUnder(path, source)))
                        {
                            Error(at + ".source", $"{source} is, holds or is inside {field} ({path}), which is packed into another archive, so the same files would ship twice.",
                                "keep this archive's files in a folder of their own, beside the others, for example \"Voices\".");
                            source = null;
                        }
                    if (source != null) packed.Add((at + ".source", source));
                    var compression = String(e, "compression", required: false, example: "\"none\"", field: at + ".compression") ?? "default";
                    if (compression is not ("default" or "none"))
                        Error(at + ".compression", $"\"{compression}\" isn't a compression pack knows.", "write \"default\", or \"none\" for sound (.wem) and lip files, which play silent when stored compressed.");
                    var format = String(e, "format", required: false, example: "\"dds\"", field: at + ".format") ?? "general";
                    if (format is not ("general" or "dds"))
                        Error(at + ".format", $"\"{format}\" isn't an archive format pack knows.", "write \"general\" (meshes, sounds and other files) or \"dds\" (textures only).");
                    uncompressed = compression == "none";
                    dds = format == "dds";
                }
                else
                {
                    foreach (var k in new[] { "compression", "format" })
                        if (e.TryGetProperty(k, out var v) && v.ValueKind != JsonValueKind.Null)
                            Error($"{at}.{k}", "a ready archive is copied as it is, so it isn't packed with a compression or format.", $"remove {k}, or use \"source\" to have pack make the archive.");
                    var value = String(e, "file", required: true, example: "\"Original/MyMod - Voices.ba2\"", field: at + ".file");
                    if (value != null && PathOf(value, at + ".file") is { } full)
                    {
                        if (!full.EndsWith(".ba2", StringComparison.OrdinalIgnoreCase))
                            Error(at + ".file", $"\"{value}\" doesn't end in .ba2.", "give the ready archive file, for example \"Original/MyMod - Voices.ba2\".");
                        else if (NamesFolder(value) || !System.IO.File.Exists(full))
                            Error(at + ".file", Directory.Exists(full) ? $"{full} is a folder, not an archive." : $"{full} isn't there.", "give the ready archive file, relative to sfmod.json.");
                        else if (new[] { ("dist", dist), ("stage", stage), ("creationsOut", creationsOut) }.FirstOrDefault(x => x.Item2 != null && IsUnder(full, x.Item2)) is { Item1: { } field, Item2: { } path })
                            Error(at + ".file", $"{full} is inside {field} ({path}), {Clears(field)}.", "keep the ready archive in a folder of its own, outside dist, stage and creations-out.");
                        else file = full;
                    }
                }
                if (name != null && (source != null || file != null))
                    list.Add(new SfModProject.ExtraArchive(name, source, file, uncompressed, dds));
            }
            return problems.Count(x => !x.IsWarning) == before ? list : null;
        }

        /// <summary>The assets section: the folder of loose files and, optionally, Xbox-format textures. Every file is
        /// read now, so validate can list problems before a build. ok is false when the section has a PROBLEM.</summary>
        SfModProject.AssetsSection? Assets(JsonElement root, bool xbox, List<(string Name, string Path)> outputs, List<(string What, string Path)> mustNotHold, out bool ok)
        {
            ok = true;
            if (!root.TryGetProperty("assets", out var a) || a.ValueKind == JsonValueKind.Null) return null;
            var before = problems.Count(x => !x.IsWarning);
            if (a.ValueKind != JsonValueKind.Object)
            {
                Error("assets", $"it is a {Kind(a)}, not an object.", "write it like \"assets\": { \"source\": \"Assets\" }, or leave it out when the mod has no loose files.");
                ok = false;
                return null;
            }
            Fields(a, "assets.", AssetsFields);
            var source = AssetFolder(a, "source", required: true, outputs, mustNotHold);
            var xboxFolder = AssetFolder(a, "xboxTextures", required: false, outputs, mustNotHold);
            if (source != null && xboxFolder != null && (SamePath(source, xboxFolder) || IsUnder(xboxFolder, source) || IsUnder(source, xboxFolder)))
            {
                Error("assets.xboxTextures", $"{xboxFolder} is, holds or is inside assets.source {source}, so the same files would be packed twice.", "keep the Xbox textures in a folder of their own, beside the assets folder, for example \"Assets-xbox\".");
                xboxFolder = null;
            }
            if (xboxFolder != null && !xbox)
                Warn("assets.xboxTextures", "it is given, but \"xbox\" is off, so no Xbox archive is made and these files aren't used.", "set \"xbox\": true to make the Creations set, or remove assets.xboxTextures.");

            var general = new List<SfModProject.AssetFile>();
            var textures = new List<SfModProject.AssetFile>();
            if (source != null)
            {
                foreach (var f in ScanAssets(source, "assets.source"))
                {
                    var ext = Path.GetExtension(f.Relative);
                    var field = $"assets.source ({f.Relative})";
                    if (RefusedAssetExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
                    {
                        Error(field, ext.Equals(".psc", StringComparison.OrdinalIgnoreCase)
                                ? "Papyrus source (.psc) never ships; the compiled scripts come from the scripts section."
                                : $"a {ext} file doesn't go inside an archive (plugins and archives go at the top of the zip).",
                            ext.Equals(".psc", StringComparison.OrdinalIgnoreCase)
                                ? "move it to the script source folder (scripts.source), or delete it from the assets folder."
                                : "move it out of the assets folder; a plugin comes from the builder, and any other file can go in zip.files.");
                        continue;
                    }
                    if (!f.Relative.Contains(Path.DirectorySeparatorChar))
                    {
                        Error(field, "it is at the top of the assets folder, where the game never looks inside an archive.",
                            "put it in the Data folder it belongs to (Meshes\\, Materials\\, Textures\\, Sound\\ and so on, inside the assets folder), or move it out.");
                        continue;
                    }
                    if (SourceArtExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
                        Warn(field, $"{ext} is an artist's source format that the game doesn't read; it will be packed anyway.",
                            "convert it first (textures to .dds, models to .nif and .mesh, sound to .wem with the Creation Kit's tools), and keep the source outside the assets folder.");
                    (ext.Equals(".dds", StringComparison.OrdinalIgnoreCase) ? textures : general).Add(f);
                }
                if (general.Count + textures.Count == 0 && problems.Count(x => !x.IsWarning) == before)
                    Warn("assets.source", $"{source} holds no files to pack.", "put the mod's loose files there (laid out like Data: Meshes\\, Textures\\ ...), or remove the assets section.");
                if (xbox && general.Any(f => f.Relative.EndsWith(".wem", StringComparison.OrdinalIgnoreCase)))
                    Warn("assets.source", "it holds .wem sound files and \"xbox\" is on. A PC .wem copied into the Xbox archive plays silent on Xbox and PlayStation 5.",
                        "make console versions of the sounds in the Creation Kit from WAV sources, or use the game's own sounds for the Creation.");
            }
            var xboxFiles = new List<SfModProject.AssetFile>();
            if (xboxFolder != null)
            {
                foreach (var f in ScanAssets(xboxFolder, "assets.xboxTextures"))
                {
                    if (!Path.GetExtension(f.Relative).Equals(".dds", StringComparison.OrdinalIgnoreCase))
                        Error($"assets.xboxTextures ({f.Relative})", "only .dds textures go in the Xbox texture folder.", "keep other files in assets.source; they go into the Main archives for both platforms.");
                    else xboxFiles.Add(f);
                }
                var pc = new HashSet<string>(textures.Select(t => t.Relative), StringComparer.OrdinalIgnoreCase);
                var xb = new HashSet<string>(xboxFiles.Select(t => t.Relative), StringComparer.OrdinalIgnoreCase);
                var missing = pc.Where(x => !xb.Contains(x)).Order(StringComparer.OrdinalIgnoreCase).ToList();
                var extra = xb.Where(x => !pc.Contains(x)).Order(StringComparer.OrdinalIgnoreCase).ToList();
                // With xbox on, the given folder is the whole Xbox texture archive: an empty one would be packed as
                // an empty archive, and a texture it lacks is simply missing on Xbox (the console never reads the PC
                // archive). Filling the gaps from the PC set would mix formats without a word, so both are refused.
                // A mod with no PC textures makes no texture archive, so an empty folder is harmless (pack agrees).
                if (xbox && xboxFiles.Count == 0 && textures.Count > 0)
                    Error("assets.xboxTextures", $"{xboxFolder} holds no .dds textures, so the Xbox texture archive would be empty.", "put the Xbox-format .dds files there (laid out like the PC textures), or remove assets.xboxTextures to pack the PC textures for Xbox too.");
                else if (missing.Count > 0)
                {
                    var (problem, fix) = ($"{missing.Count} PC texture(s) have no Xbox copy, so they would be missing on Xbox: {Few(missing)}.", "export an Xbox version of each, or remove assets.xboxTextures to pack the PC textures for Xbox too.");
                    if (xbox) Error("assets.xboxTextures", problem, fix);
                    else Warn("assets.xboxTextures", problem, fix);
                }
                if (extra.Count > 0)
                    Warn("assets.xboxTextures", $"{extra.Count} Xbox texture(s) have no PC copy in assets.source: {Few(extra)}.", "check the paths match the PC textures, folder for folder.");
            }
            CaseTwins(general, "assets.source");
            CaseTwins(textures, "assets.source");
            CaseTwins(xboxFiles, "assets.xboxTextures");
            ok = problems.Count(x => !x.IsWarning) == before;
            if (source == null) return null;
            return new SfModProject.AssetsSection { Source = source, XboxTextures = xboxFolder, General = general, Textures = textures, XboxTextureFiles = xboxFiles };
        }

        /// <summary>A PROBLEM for each pair of files whose archive paths differ only in case (a case-sensitive folder,
        /// as on Linux, can hold both): they would be one file in the archive. Pack refuses them with the same words.</summary>
        void CaseTwins(List<SfModProject.AssetFile> files, string field)
        {
            var taken = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in files)
            {
                if (taken.TryGetValue(f.Relative, out var other)) Error(field, CaseTwinProblem(field, f.Full, other), CaseTwinFix);
                else taken[f.Relative] = f.Full;
            }
        }

        static string Few(List<string> names) => string.Join(", ", names.Take(3)) + (names.Count > 3 ? $" and {names.Count - 3} more" : "");

        /// <summary>An assets folder: it must exist and be a folder of its own, apart from every folder the tool
        /// empties (their files would be lost or packed twice) and from the builder and the script sources.</summary>
        string? AssetFolder(JsonElement a, string name, bool required, List<(string Name, string Path)> outputs, List<(string What, string Path)> mustNotHold)
        {
            var field = "assets." + name;
            if (!a.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null)
            {
                if (required) Error(field, "it is missing.", $"add \"{name}\": \"Assets\" (the folder that holds the mod's loose files, laid out like Data).");
                return null;
            }
            var example = name == "xboxTextures" ? "Assets-xbox" : "Assets";
            var full = Folder(a, name, example, field);
            if (full == null) return null;
            if (!Directory.Exists(full))
            {
                Error(field, $"the folder {full} isn't there.", "give the folder that holds the loose files (Meshes\\, Textures\\ ... inside it), relative to sfmod.json.");
                return null;
            }
            if (SamePath(full, folder) || IsUnder(folder, full))
            {
                Error(field, $"{full} is or holds the mod's own folder, so the project file, the builder and the build output would be packed.", $"give a folder of its own inside the mod, for example \"{example}\".");
                return null;
            }
            foreach (var (outName, outPath) in outputs)
                if (SamePath(full, outPath) || IsUnder(full, outPath) || IsUnder(outPath, full))
                {
                    Error(field, $"{full} is, holds or is inside {outName} ({outPath}), {Clears(outName)}.", $"keep the loose files in a folder of their own, for example \"{example}\".");
                    return null;
                }
            foreach (var (what, path) in mustNotHold)
                if (SamePath(full, path) || IsUnder(path, full))
                {
                    Error(field, $"{full} is or holds {what} ({path}), whose files would be packed.", $"give a folder that holds only the loose files, for example \"{example}\".");
                    return null;
                }
            return full;
        }

        /// <summary>Every file under the folder, sorted by path. Links and junctions are left out (with a warning),
        /// so a folder can't pull in files from elsewhere; so are dot files and Windows clutter.</summary>
        List<SfModProject.AssetFile> ScanAssets(string root, string field)
        {
            var list = new List<SfModProject.AssetFile>();
            var pending = new Stack<DirectoryInfo>();
            pending.Push(new DirectoryInfo(root));
            while (pending.Count > 0)
            {
                var dir = pending.Pop();
                FileSystemInfo[] entries;
                try { entries = dir.GetFileSystemInfos(); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    Error(field, $"{dir.FullName} can't be read ({e.Message}).", "check that this account can read the folder.");
                    continue;
                }
                foreach (var e in entries)
                {
                    if (e.Name.StartsWith('.') || SkippedAssetNames.Contains(e.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        Log.Write($"assets: left out {e.FullName}");
                        continue;
                    }
                    string? link;
                    try { link = e.LinkTarget; }
                    catch (Exception x) when (x is IOException or UnauthorizedAccessException)
                    {
                        Error(field, $"{e.FullName} can't be checked ({x.Message}).", "check that this account can read it, or move it out of the assets folder.");
                        continue;
                    }
                    if (link != null)
                    {
                        Warn(field, $"{e.FullName} is a link or junction (to {link}), so it is left out.", "copy the files themselves into the assets folder.");
                        continue;
                    }
                    if (e is DirectoryInfo d) pending.Push(d);
                    else list.Add(new SfModProject.AssetFile(e.FullName, Path.GetRelativePath(root, e.FullName)));
                }
            }
            list.Sort((x, y) => string.Compare(x.Relative, y.Relative, StringComparison.OrdinalIgnoreCase));
            Log.Write($"assets: {list.Count} file(s) in {root}");
            return list;
        }

        string? CheckPlugin(string plugin, string field = "plugin")
        {
            var ext = Path.GetExtension(plugin);
            if (!PluginExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            {
                Error(field, $"\"{plugin}\" doesn't end in .esm, .esp or .esl.", "give the plugin's file name with its extension, for example \"MyMod.esm\".");
                return null;
            }
            if (!GoodFileName(plugin) || plugin.Contains('/') || plugin.Contains('\\'))
            {
                Error(field, $"\"{plugin}\" isn't a plain file name.", "give only the file name the builder writes into dist, for example \"MyMod.esm\" (no folder).");
                return null;
            }
            var stem = Path.GetFileNameWithoutExtension(plugin);
            if (OfficialEndings.Any(o => stem.Equals(o, StringComparison.OrdinalIgnoreCase)) || SfbgsName.IsMatch(stem))
            {
                Error(field, ext.Equals(".esm", StringComparison.OrdinalIgnoreCase)
                        ? $"\"{plugin}\" is the name of one of the game's own plugins; installing the mod would replace it."
                        : $"\"{plugin}\" uses the name of one of the game's own plugins; Starfield removes such names from Plugins.txt, so the mod would stop loading.",
                    $"give the mod a name of its own, for example \"My{stem}Addon{ext}\".");
                return null;
            }
            foreach (var official in OfficialEndings)
                if (stem.Length > official.Length && stem.EndsWith(official, StringComparison.OrdinalIgnoreCase))
                    Error(field, $"\"{plugin}\" ends in the official plugin name \"{official}\"; Starfield removes such names from Plugins.txt, so the mod would stop loading.",
                        $"add a word after it, for example \"{stem}Addon{ext}\".");
            if (stem.StartsWith("BlueprintShips-", StringComparison.OrdinalIgnoreCase))
                Error(field, $"\"{plugin}\" starts with \"BlueprintShips-\", which Starfield removes from Plugins.txt, so the mod would stop loading.",
                    "pick a name that doesn't start with BlueprintShips-.");
            // Official Creations are named SFBGS001.esm and so on. Endings like "-SFBGS003" are untested in game,
            // but the names the game is known to drop are all official ones, so warn rather than refuse.
            if (SfbgsEnding.IsMatch(stem))
                Warn(field, $"\"{plugin}\" ends in an official Creation's name (SFBGS...); Starfield may remove it from Plugins.txt like other official names, so the mod could stop loading.",
                    $"add a word after it, for example \"{stem}Addon{ext}\".");
            return plugin;
        }

        SfModProject.ScriptsSection? Scripts(JsonElement root, string sf = "scripts", IReadOnlyList<string>? defaultSources = null, bool defaultOptimize = true)
        {
            if (!root.TryGetProperty("scripts", out var s) || s.ValueKind == JsonValueKind.Null) return null;
            if (s.ValueKind != JsonValueKind.Object)
            {
                Error(sf, $"it is a {Kind(s)}, not an object.", "write it like \"scripts\": { \"source\": \"Scripts/Source\", \"namespaces\": [\"MYM\"] }, or leave it out when the mod has no scripts.");
                return null;
            }
            Fields(s, sf + ".", ScriptsFields);
            var sources = new List<string>();
            if (s.TryGetProperty("source", out var src))
            {
                if (src.ValueKind == JsonValueKind.String) AddSource(src.GetString()!, sf + ".source", sources);
                else if (src.ValueKind == JsonValueKind.Array)
                {
                    var i = 0;
                    foreach (var e in src.EnumerateArray())
                    {
                        if (e.ValueKind == JsonValueKind.String) AddSource(e.GetString()!, $"{sf}.source[{i}]", sources);
                        else Error($"{sf}.source[{i}]", $"it is a {Kind(e)}, not a folder name.", "write each folder as a string, for example \"Scripts/Source\".");
                        i++;
                    }
                    if (i == 0) Error(sf + ".source", "the list is empty.", "give at least one folder, for example \"Scripts/Source\".");
                }
                else Error(sf + ".source", $"it is a {Kind(src)}.", "give a folder, for example \"Scripts/Source\", or a list of folders.");
            }
            else if (defaultSources != null) sources.AddRange(defaultSources);
            else AddSource("Scripts/Source", sf + ".source (default Scripts/Source)", sources);

            var namespaces = StringList(s, "namespaces", sf + ".namespaces", "[\"MYM\"]");
            var files = StringList(s, "files", sf + ".files", "[\"MYM/MyQuestScript\"]");
            if (namespaces.Count == 0 && files.Count == 0)
                Error(sf, "it lists neither namespaces nor files, so there is nothing to compile.",
                    "add \"namespaces\": [\"<the folder under the source folder that holds your scripts>\"], or leave out \"scripts\" when the mod has none.");
            if (sources.Count == 0) return null;

            var nsFound = new List<string>();
            for (var i = 0; i < namespaces.Count; i++)
            {
                var ns = namespaces[i];
                if (!Regex.IsMatch(ns, @"^[A-Za-z_][A-Za-z0-9_]*(?:[/\\][A-Za-z_][A-Za-z0-9_]*)*$"))
                {
                    Error($"{sf}.namespaces[{i}]", $"\"{ns}\" isn't a Papyrus namespace (letters, digits and _, starting with a letter; / between levels).", "give the folder name under the source folder, for example \"MYM\".");
                    continue;
                }
                var nsPath = ns.Replace('/', '\\').Replace('\\', Path.DirectorySeparatorChar);
                var where = sources.Select(x => Path.Combine(x, nsPath)).FirstOrDefault(Directory.Exists);
                if (where == null)
                    Error($"{sf}.namespaces[{i}]", $"there is no folder \"{ns}\" in {string.Join(" or ", sources)}.", "check the spelling, or move the scripts into <source>/<namespace>/.");
                else if (!Directory.EnumerateFiles(where, "*.psc").Any())
                    Error($"{sf}.namespaces[{i}]", $"{where} holds no .psc files.", "put the namespace's scripts there, or remove it from the list.");
                else nsFound.Add(ns.Replace('/', '\\'));
            }
            var fileNames = new List<string>();
            for (var i = 0; i < files.Count; i++)
            {
                var f = files[i].Replace('/', '\\');
                if (f.EndsWith(".psc", StringComparison.OrdinalIgnoreCase)) f = f[..^4];
                if (!Regex.IsMatch(f, @"^(?:[A-Za-z_][A-Za-z0-9_]*\\)*[A-Za-z_][A-Za-z0-9_]*$"))
                {
                    Error($"{sf}.files[{i}]", $"\"{files[i]}\" isn't a script name like \"Namespace/ScriptName\".", "give the namespace folder and the script name, for example \"MYM/MyQuestScript\".");
                    continue;
                }
                var rel = f.Replace('\\', Path.DirectorySeparatorChar) + ".psc";
                if (!sources.Any(x => System.IO.File.Exists(Path.Combine(x, rel))))
                    Error($"{sf}.files[{i}]", $"there is no {rel} in {string.Join(" or ", sources)}.", "check the spelling and the namespace folder.");
                else if (fileNames.Contains(f, StringComparer.OrdinalIgnoreCase))
                    Warn($"{sf}.files[{i}]", $"\"{files[i]}\" is listed twice.", "list each script once.");
                else fileNames.Add(f);
            }
            // a part's scripts follow the top-level value unless they set their own
            var optimize = Bool(s, "optimize", defaultOptimize, sf + ".optimize");
            return new SfModProject.ScriptsSection { Sources = sources, Namespaces = nsFound, Files = fileNames, Optimize = optimize };
        }

        void AddSource(string value, string field, List<string> sources)
        {
            var full = PathOf(value, field);
            if (full == null) return;
            if (!Directory.Exists(full))
                Error(field, $"the script source folder {full} isn't there.", "give the folder that holds your namespace folders (the one with MYM\\MyScript.psc under it), relative to sfmod.json.");
            else sources.Add(full);
        }

        sealed record BuilderSpec(string? Project, List<string>? Command, string? Prebuilt, List<string> Args);

        /// <summary>The builder section: exactly one of project, command and prebuilt, and args (none with prebuilt).
        /// Null when it has a PROBLEM.</summary>
        BuilderSpec? Builder(JsonElement root)
        {
            if (!root.TryGetProperty("builder", out var b))
            {
                Error("builder", "it is missing.", "add \"builder\": { \"project\": \"Builder\", \"args\": [\"{data}\", \"{dist}\", \"{version}\"] } (the Mutagen project that writes the plugin), or use \"command\" or \"prebuilt\" instead of \"project\".");
                return null;
            }
            if (b.ValueKind != JsonValueKind.Object)
            {
                Error("builder", $"it is a {Kind(b)}, not an object.", "write it like \"builder\": { \"project\": \"Builder\", \"args\": [\"{data}\", \"{dist}\"] }.");
                return null;
            }
            var before = problems.Count(x => !x.IsWarning);
            Fields(b, "builder.", BuilderFields);
            var kinds = new[] { "project", "command", "prebuilt" }.Where(k => b.TryGetProperty(k, out var v) && v.ValueKind != JsonValueKind.Null).ToList();
            if (kinds.Count == 0)
            {
                Error("builder", "it names no builder.", "give exactly one of \"project\" (a .NET builder project), \"command\" (a program and its arguments, as a list) or \"prebuilt\" (a ready plugin file to copy).");
                return null;
            }
            if (kinds.Count > 1)
            {
                Error("builder", $"it gives both {string.Join(" and ", kinds.Select(k => $"\"{k}\""))}.", "keep exactly one of \"project\", \"command\" and \"prebuilt\".");
                return null;
            }
            string? project = null, prebuilt = null;
            List<string>? command = null;
            if (kinds[0] == "project") project = BuilderProject(b);
            else if (kinds[0] == "command") command = BuilderCommand(b);
            else prebuilt = BuilderPrebuilt(b);

            var args = new List<string>();
            if (kinds[0] == "prebuilt")
            {
                if (b.TryGetProperty("args", out var pa) && pa.ValueKind != JsonValueKind.Null)
                    Error("builder.args", "a prebuilt plugin is copied, not run, so it takes no arguments.", "remove builder.args, or use \"command\" to run a program that writes the plugin.");
            }
            else if (BuilderArgs(b) is { } list) args = list;
            else return null;
            return problems.Count(x => !x.IsWarning) == before ? new BuilderSpec(project, command, prebuilt, args) : null;
        }

        /// <summary>builder.command: a list, the program first. A program with a slash is a file relative to sfmod.json
        /// (made a full path); a bare name stays as it is and is looked up on PATH when the plugin is built.</summary>
        List<string>? BuilderCommand(JsonElement b) =>
            Command(b.GetProperty("command"), "builder.command", BuilderPlaceholders, "[\"python\", \"tools/build.py\"]");

        /// <summary>One command (builder.command or a steps entry): a list of strings, the program first. A program with
        /// a slash is a file relative to sfmod.json (made a full path); a bare name stays as it is and is looked up on
        /// PATH when the command runs. Null when it has a PROBLEM.</summary>
        List<string>? Command(JsonElement c, string field, string[] placeholders, string example)
        {
            if (c.ValueKind != JsonValueKind.Array)
            {
                Error(field, $"it is a {Kind(c)}, not a list.", $"write the program and its arguments as a list, for example {example} (it is never run through a shell).");
                return null;
            }
            var raw = new List<string>();
            var i = 0;
            foreach (var e in c.EnumerateArray())
            {
                if (e.ValueKind == JsonValueKind.String && e.GetString()!.Trim().Length > 0) raw.Add(e.GetString()!);
                else Error($"{field}[{i}]", e.ValueKind == JsonValueKind.String ? "it is empty." : $"it is a {Kind(e)}, not a string.", $"write each entry as a string, for example {example}.");
                i++;
            }
            if (raw.Count != i) return null;
            if (raw.Count == 0)
            {
                Error(field, "the list is empty.", $"give the program first, then its arguments, for example {example}.");
                return null;
            }
            var program = raw[0];
            if (program.Contains('{') || program.Contains('}'))
            {
                Error($"{field}[0]", $"\"{program}\" is the program, which can't hold a placeholder.", "give the program's name (found on PATH) or its path relative to sfmod.json, and put placeholders in the arguments after it.");
                return null;
            }
            if (program.Contains('/') || program.Contains('\\'))
            {
                var full = PathOf(program, $"{field}[0]");
                if (full == null) return null;
                if (NamesFolder(program) || !System.IO.File.Exists(full))
                {
                    Error($"{field}[0]", Directory.Exists(full) ? $"{full} is a folder, not a program." : $"{full} isn't there.",
                        "give the program's path relative to sfmod.json, or a bare name such as \"python\" to find it on PATH.");
                    return null;
                }
                program = full;
            }
            else if (!GoodFileName(program))
            {
                Error($"{field}[0]", $"\"{program}\" isn't a program name.", "give a program found on PATH, such as \"python\" or \"dotnet\", or a path relative to sfmod.json.");
                return null;
            }
            var ok = true;
            for (var j = 1; j < raw.Count; j++) ok &= Placeholders(raw[j], $"{field}[{j}]", placeholders);
            if (!ok) return null;
            return raw.Skip(1).Prepend(program).ToList();
        }

        /// <summary>The steps section: commands run at the end of plugin (afterPlugin) and of pack (afterPack). Both
        /// lists are empty when it is left out; null when it has a PROBLEM.</summary>
        (List<List<string>> AfterPlugin, List<List<string>> AfterPack)? Steps(JsonElement root, bool hasScripts)
        {
            var afterPlugin = new List<List<string>>();
            var afterPack = new List<List<string>>();
            if (!root.TryGetProperty("steps", out var s) || s.ValueKind == JsonValueKind.Null) return (afterPlugin, afterPack);
            const string example = "[\"python\", \"tools/copy_voice.py\", \"{dist}\"]";
            if (s.ValueKind != JsonValueKind.Object)
            {
                Error("steps", $"it is a {Kind(s)}, not an object.", $"write it like \"steps\": {{ \"afterPlugin\": [ {example} ] }}.");
                return null;
            }
            var before = problems.Count(x => !x.IsWarning);
            Fields(s, "steps.", StepsFields);
            foreach (var (name, list) in new[] { ("afterPlugin", afterPlugin), ("afterPack", afterPack) })
            {
                if (!s.TryGetProperty(name, out var l) || l.ValueKind == JsonValueKind.Null) continue;
                if (l.ValueKind != JsonValueKind.Array)
                {
                    Error($"steps.{name}", $"it is a {Kind(l)}, not a list of commands.", $"write it as a list of commands, each a list, for example [ {example} ].");
                    continue;
                }
                var i = 0;
                foreach (var e in l.EnumerateArray())
                {
                    var field = $"steps.{name}[{i++}]";
                    if (Command(e, field, StepPlaceholders, example) is not { } command) continue;
                    if (!hasScripts && command.Skip(1).Any(a => a.Contains("{scripts}")))
                        Error(field, "it uses {scripts}, but the project has no \"scripts\" section.", "add the scripts section, or take {scripts} out of the command.");
                    else list.Add(command);
                }
            }
            return problems.Count(x => !x.IsWarning) == before ? (afterPlugin, afterPack) : null;
        }

        /// <summary>builder.prebuilt: a plugin file that already exists (it may be outside the mod; it is only read).</summary>
        string? BuilderPrebuilt(JsonElement b)
        {
            var value = String(b, "prebuilt", required: true, example: "\"Original/MyMod.esm\"", field: "builder.prebuilt");
            if (value == null || PathOf(value, "builder.prebuilt") is not { } full) return null;
            if (!PluginExtensions.Contains(Path.GetExtension(full), StringComparer.OrdinalIgnoreCase))
            {
                Error("builder.prebuilt", $"\"{value}\" doesn't end in .esm, .esp or .esl.", "give the ready plugin file, for example \"Original/MyMod.esm\".");
                return null;
            }
            if (NamesFolder(value) || !System.IO.File.Exists(full))
            {
                Error("builder.prebuilt", Directory.Exists(full) ? $"{full} is a folder, not a plugin file." : $"{full} isn't there.", "give the ready plugin file, relative to sfmod.json.");
                return null;
            }
            return full;
        }

        string? BuilderProject(JsonElement b)
        {
            string? project = null;
            var value = String(b, "project", required: true, example: "\"Builder\"", field: "builder.project");
            if (value != null && PathOf(value, "builder.project") is { } full)
            {
                if (!NamesFolder(value) && System.IO.File.Exists(full) && full.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)) project = full;
                else if (Directory.Exists(full))
                {
                    var projects = Directory.GetFiles(full, "*.csproj");
                    if (projects.Length == 1) project = projects[0];
                    else if (projects.Length == 0) Error("builder.project", $"{full} holds no .csproj file.", "point it at the folder of your Mutagen builder project (the one with the .csproj).");
                    else Error("builder.project", $"{full} holds {projects.Length} .csproj files.", $"name the one to run, for example \"{value.TrimEnd('/', '\\')}/{Path.GetFileName(projects[0])}\".");
                }
                else if (NamesFolder(value) && System.IO.File.Exists(full)) Error("builder.project", $"\"{value}\" ends in a slash, so it names a folder, not the .csproj.", "give the .csproj without the slash, or the builder's folder.");
                else Error("builder.project", $"{full} isn't there (or isn't a .csproj file).", "give the builder's folder or its .csproj, relative to sfmod.json.");
            }
            return project;
        }

        List<string>? BuilderArgs(JsonElement b)
        {
            List<string>? args = null;
            if (!b.TryGetProperty("args", out var a))
                Error("builder.args", "it is missing.", "list what your builder expects, in its order, for example [\"{data}\", \"{dist}\", \"{version}\"]. Look at its Program.cs or the old build script's dotnet run line.");
            else if (a.ValueKind != JsonValueKind.Array)
                Error("builder.args", $"it is a {Kind(a)}, not a list.", "write it as a list, for example [\"{data}\", \"{dist}\"].");
            else
            {
                args = new List<string>();
                var i = 0;
                foreach (var e in a.EnumerateArray())
                {
                    if (e.ValueKind != JsonValueKind.String)
                        Error($"builder.args[{i}]", $"it is a {Kind(e)}.", "write each argument as a string, for example \"{dist}\" or \"--fast\".");
                    else
                    {
                        var s = e.GetString()!;
                        Placeholders(s, $"builder.args[{i}]", BuilderPlaceholders);
                        args.Add(s);
                    }
                    i++;
                }
                var command = b.TryGetProperty("command", out var c) && c.ValueKind == JsonValueKind.Array
                    ? c.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).Skip(1) : Enumerable.Empty<string>();
                if (args.Count > 0 && !args.Concat(command).Any(x => x.Contains("{dist}")))
                    Warn("builder.args", "no argument holds {dist}, so the builder isn't told where to write the plugin.",
                        "add \"{dist}\" where your builder expects its output folder, unless it always writes to dist by itself.");
            }
            return args;
        }

        (string?, string?, List<(string, string)>?, List<string>?) Zip(JsonElement root, string? name, string? plugin, string? version)
        {
            var values = new Dictionary<string, string>
            {
                ["name"] = name ?? "", ["plugin"] = plugin != null ? Path.GetFileNameWithoutExtension(plugin) : "", ["version"] = version ?? "",
            };
            JsonElement z = default;
            var has = root.TryGetProperty("zip", out z) && z.ValueKind != JsonValueKind.Null;
            if (has && z.ValueKind != JsonValueKind.Object)
            {
                Error("zip", $"it is a {Kind(z)}, not an object.", "write it like \"zip\": { \"files\": { \"README.md\": \"README-{name}.md\" } }.");
                return (null, null, null, null);
            }
            if (has) Fields(z, "zip.", ZipFields);

            var nameTemplate = has ? String(z, "name", false, "\"{name}-{version}.zip\"", "zip.name") : null;
            nameTemplate ??= "{name}-{version}.zip";
            string? zipName = null;
            if (Placeholders(nameTemplate, "zip.name", ZipPlaceholders))
            {
                zipName = Expand(nameTemplate, values);
                if (!zipName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    Error("zip.name", $"\"{nameTemplate}\" doesn't end in .zip.", "end it in .zip, for example \"{name}-{version}.zip\".");
                else if (!GoodFileName(zipName) || zipName.Contains('/') || zipName.Contains('\\'))
                    Error("zip.name", $"\"{zipName}\" isn't a plain file name.", "give only the file name; put the folder in zip.folder.");
            }
            var zipFolder = has && z.TryGetProperty("folder", out _) ? Folder(z, "folder", ".", "zip.folder") : folder;

            var files = new List<(string, string)>();
            if (has && z.TryGetProperty("files", out var f) && f.ValueKind != JsonValueKind.Null)
            {
                if (f.ValueKind != JsonValueKind.Object)
                {
                    Error("zip.files", $"it is a {Kind(f)}, not an object.", "map each file here to its name in the zip, for example { \"README.md\": \"README-{name}.md\", \"LICENSE\": \"LICENSE-{name}.txt\" }.");
                    return (zipName, zipFolder, null, null);
                }
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var e in f.EnumerateObject())
                {
                    var field = $"zip.files[\"{e.Name}\"]";
                    if (e.Value.ValueKind != JsonValueKind.String)
                    {
                        Error(field, $"it is a {Kind(e.Value)}.", "give the name in the zip as a string, for example \"README-{name}.md\".");
                        continue;
                    }
                    var target = e.Value.GetString()!;
                    if (IsScriptSource(e.Name) || IsScriptSource(target))
                    {
                        Error(field, "Papyrus source (.psc) never goes in the zip: players need only the compiled scripts, which are in the BA2.", "remove it from zip.files (share source on GitHub instead, if you like).");
                        continue;
                    }
                    if (e.Name.EndsWith(".ba2", StringComparison.OrdinalIgnoreCase) || target.EndsWith(".ba2", StringComparison.OrdinalIgnoreCase))
                    {
                        Error(field, "it is an archive (.ba2); zip.files takes other files only (README, LICENSE and the like).", "remove it from zip.files; if your builder writes it into dist, list its name in zip.archives instead.");
                        continue;
                    }
                    var source = PathOf(e.Name, field);
                    if (source != null && (NamesFolder(e.Name) || !System.IO.File.Exists(source)))
                    {
                        var missing = !Directory.Exists(source) && !NamesFolder(e.Name);
                        Error(field, Directory.Exists(source) ? $"{source} is a folder; zip.files takes files."
                            : NamesFolder(e.Name) ? $"\"{e.Name}\" ends in a slash, so it names a folder; zip.files takes files."
                            : $"{source} isn't there.",
                            missing ? "give a file that is there before the build, relative to sfmod.json. A file that your builder or a steps command makes goes in dist instead, because zip takes dist's files as they are (a steps.afterPlugin command can rename it there)."
                                : "give a file, relative to sfmod.json.");
                        source = null;
                    }
                    if (!Placeholders(target, field, ZipPlaceholders)) continue;
                    var inZip = Expand(target, values).Replace('\\', '/');
                    if (inZip.Length == 0 || inZip.StartsWith('/') || inZip.Contains(':') || inZip.Split('/').Any(x => x is "" or "." or ".."))
                    {
                        Error(field, $"\"{target}\" isn't a usable name inside the zip.", "use a plain name such as \"README-{name}.md\", or a folder inside the zip such as \"Docs/README.md\" (no .., no drive, no leading /).");
                        continue;
                    }
                    if (!seen.Add(inZip))
                    {
                        Error(field, $"two files would both be \"{inZip}\" in the zip.", "give each file its own name in the zip.");
                        continue;
                    }
                    if (source != null) files.Add((source, inZip));
                }
            }
            var archives = new List<string>();
            if (has && z.TryGetProperty("archives", out var ar) && ar.ValueKind != JsonValueKind.Null)
            {
                if (ar.ValueKind != JsonValueKind.Array)
                {
                    Error("zip.archives", $"it is a {Kind(ar)}, not a list.", "list the archive file names your builder writes into dist, for example [\"MyMod - Extra.ba2\"].");
                    return (zipName, zipFolder, files, null);
                }
                var seenArchives = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var i = 0;
                foreach (var e in ar.EnumerateArray())
                {
                    var field = $"zip.archives[{i++}]";
                    if (e.ValueKind != JsonValueKind.String)
                    {
                        Error(field, $"it is a {Kind(e)}.", "give the archive's file name as a string, for example \"MyMod - Extra.ba2\".");
                        continue;
                    }
                    var a = e.GetString()!;
                    if (!a.EndsWith(".ba2", StringComparison.OrdinalIgnoreCase) || a.Length <= ".ba2".Length || !GoodFileName(a) || a.Contains('/') || a.Contains('\\'))
                    {
                        Error(field, $"\"{a}\" isn't a plain archive file name ending in .ba2.", "give only the file name of an archive in dist, for example \"MyMod - Extra.ba2\".");
                        continue;
                    }
                    if (!seenArchives.Add(a))
                    {
                        Error(field, $"\"{a}\" is listed twice.", "list each archive once.");
                        continue;
                    }
                    archives.Add(a);
                }
            }
            return (zipName, zipFolder, files, archives);
        }

        (string?, bool, List<string>?) Check(JsonElement root)
        {
            if (!root.TryGetProperty("check", out var c) || c.ValueKind == JsonValueKind.Null) return (null, false, new List<string>());
            if (c.ValueKind != JsonValueKind.Object)
            {
                Error("check", $"it is a {Kind(c)}, not an object.", "write it like \"check\": { \"required\": true }, or leave it out.");
                return (null, false, null);
            }
            Fields(c, "check.", CheckFields);
            string? checker = null;
            if (String(c, "checker", false, "\"tools/sf-mod-check.exe\"", "check.checker") is { } value && PathOf(value, "check.checker") is { } full)
            {
                if (NamesFolder(value) || !System.IO.File.Exists(full)) Error("check.checker", NamesFolder(value) ? $"\"{value}\" ends in a slash, so it names a folder, not the checker program." : Directory.Exists(full) ? $"{full} is a folder, not the checker program." : $"{full} isn't there.", "give the checker program's path, or leave check.checker out to find sf-mod-check (or sfmodcheck) on PATH.");
                else checker = full;
                if (Path.IsPathRooted(value))
                    Warn("check.checker", "it is a full path, which other people building your mod won't have.", "leave it out (the checker is found on PATH) or give it on the command line with --checker.");
            }
            var required = Bool(c, "required", false, "check.required");
            var args = StringList(c, "args", "check.args", "[\"--strict\"]");
            return (checker, required, args);
        }

        // ---- small readers ----

        void Fields(JsonElement obj, string prefix, string[] known)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var prop in obj.EnumerateObject())
            {
                if (!seen.Add(prop.Name))
                    Error(prefix + prop.Name, "it is given twice.", "keep one of them.");
                if (known.Contains(prop.Name, StringComparer.Ordinal)) continue;
                var near = known.Select(k => (k, d: Args.Distance(prop.Name.ToLowerInvariant(), k.ToLowerInvariant()))).OrderBy(x => x.d).First();
                var fix = near.d <= 3 ? $"did you mean \"{near.k}\"?" : $"remove it. The fields here are: {string.Join(", ", known.Where(k => k != "$schema"))}.";
                Error(prefix + prop.Name, "it isn't a field sf-mod-build knows.", fix);
            }
        }

        string? String(JsonElement obj, string name, bool required, string example, string? field = null)
        {
            field ??= name;
            if (!obj.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null)
            {
                if (required) Error(field, "it is missing.", $"add \"{name}\": {example}.");
                return null;
            }
            if (v.ValueKind != JsonValueKind.String)
            {
                Error(field, $"it is a {Kind(v)}, not a string.", $"write it in quotes, for example \"{name}\": {example}.");
                return null;
            }
            var s = v.GetString()!;
            if (s.Trim().Length == 0)
            {
                Error(field, "it is empty.", $"give a value, for example \"{name}\": {example}" + (required ? "." : ", or leave it out."));
                return null;
            }
            return s;
        }

        bool Bool(JsonElement obj, string name, bool fallback, string? field = null)
        {
            if (!obj.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null) return fallback;
            if (v.ValueKind is JsonValueKind.True or JsonValueKind.False) return v.GetBoolean();
            Error(field ?? name, $"it is a {Kind(v)}, not true or false.", $"write true or false without quotes, for example \"{name}\": true.");
            return fallback;
        }

        List<string> StringList(JsonElement obj, string name, string field, string example)
        {
            var list = new List<string>();
            if (!obj.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null) return list;
            if (v.ValueKind != JsonValueKind.Array)
            {
                Error(field, $"it is a {Kind(v)}, not a list.", $"write it as a list, for example \"{name}\": {example}.");
                return list;
            }
            var i = 0;
            foreach (var e in v.EnumerateArray())
            {
                if (e.ValueKind == JsonValueKind.String && e.GetString()!.Trim().Length > 0) list.Add(e.GetString()!);
                else Error($"{field}[{i}]", e.ValueKind == JsonValueKind.String ? "it is empty." : $"it is a {Kind(e)}, not a string.", $"write each entry as a string, for example {example}.");
                i++;
            }
            return list;
        }

        string? Folder(JsonElement obj, string name, string fallback, string? field = null)
        {
            field ??= name;
            var value = String(obj, name, false, $"\"{fallback.Replace('\\', '/')}\"", field) ?? (obj.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null ? null : fallback);
            if (value == null) return null;
            var full = PathOf(value, field);
            if (full != null && System.IO.File.Exists(full))
            {
                Error(field, $"{full} is a file, not a folder.", "give a folder name, relative to sfmod.json.");
                return null;
            }
            return full;
        }

        /// <summary>A path from the file made absolute (relative to the sfmod.json folder); / and \ both work.</summary>
        string? PathOf(string value, string field)
        {
            if (value.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || value.Contains('*') || value.Contains('?'))
            {
                Error(field, $"\"{value}\" isn't a usable path (it has a character paths can't hold, or a wildcard).", "give one plain path, relative to sfmod.json.");
                return null;
            }
            var native = value.Replace('\\', '/').Replace('/', Path.DirectorySeparatorChar);
            // a trailing separator is dropped (a drive or file-system root keeps its own), so "dist/" and "dist" are one
            // path to every check: on Linux, a link spelled "dist/" is followed by lstat, readlink and recursive delete
            try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(folder, native))); }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
            {
                Error(field, $"\"{value}\" isn't a usable path ({e.Message}).", "give one plain path, relative to sfmod.json.");
                return null;
            }
        }

        /// <summary>True when the value ends in / or \, so it names a folder: PathOf drops that separator, so a field that
        /// takes a file checks it here, and "LICENSE/" stays an error as it was before the drop.</summary>
        static bool NamesFolder(string value) => value.EndsWith('/') || value.EndsWith('\\');

        /// <summary>True when every {placeholder} in the text is one of the allowed ones.</summary>
        bool Placeholders(string text, string field, string[] allowed)
        {
            var ok = true;
            foreach (Match m in Regex.Matches(text, @"\{([^{}]*)\}"))
            {
                var name = m.Groups[1].Value;
                if (allowed.Contains(name, StringComparer.Ordinal)) continue;
                ok = false;
                var near = allowed.Select(k => (k, d: Args.Distance(name.ToLowerInvariant(), k))).OrderBy(x => x.d).First();
                Error(field, $"{{{name}}} isn't a placeholder here.",
                    (near.d <= 2 ? $"did you mean {{{near.k}}}? " : "") + $"The placeholders here are {string.Join(" ", allowed.Select(a => "{" + a + "}"))}.");
            }
            var stripped = Regex.Replace(text, @"\{[^{}]*\}", "");
            if (ok && (stripped.Contains('{') || stripped.Contains('}')))
            {
                Error(field, $"\"{text}\" has a {{ or }} that doesn't make a placeholder.", "check the braces: placeholders look like {version}.");
                ok = false;
            }
            return ok;
        }

        /// <summary>What the build does to an output folder, for messages: stage is emptied, while dist and
        /// creations-out only have the files earlier builds made there replaced.</summary>
        static string Clears(string field) => field == "stage" ? "which the build empties" : "where the build deletes and replaces the files it made earlier";

        /// <summary>True when <paramref name="inner"/> is inside <paramref name="outer"/> (not the same folder).</summary>
        bool IsUnder(string inner, string outer)
        {
            var o = Path.TrimEndingDirectorySeparator(outer);
            if (!Path.EndsInDirectorySeparator(o)) o += Path.DirectorySeparatorChar; // a drive root keeps its "\"
            var i = Path.TrimEndingDirectorySeparator(inner);
            return i.Length > o.Length && i.StartsWith(o, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }

        bool SamePath(string a, string b) =>
            string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    static bool IsScriptSource(string name) => name.TrimEnd().EndsWith(".psc", StringComparison.OrdinalIgnoreCase);

    static bool GoodFileName(string name) =>
        name.Trim().Length > 0 && name.IndexOfAny(new[] { '<', '>', ':', '"', '|', '?', '*' }) < 0 && !name.Any(char.IsControl) && name.Trim() == name;

    static string Kind(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Object => "object",
        JsonValueKind.Array => "list",
        JsonValueKind.String => "string",
        JsonValueKind.Number => "number",
        JsonValueKind.True or JsonValueKind.False => "true/false value",
        JsonValueKind.Null => "null",
        _ => "value",
    };
}
