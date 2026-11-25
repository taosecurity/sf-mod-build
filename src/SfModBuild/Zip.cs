// sf-mod-build zip [sfmod.json]: writes the mod's Vortex/MO2 zip reproducibly: the same files give the same bytes on
// every run on the same operating system and .NET version (the zip records the system that made it, and another
// .NET may compress differently). A plugin in parts, or a part's archive, missing from dist stops it. Contents: dist's top-level files (never zips or .psc) plus each zip.files entry
// under its name in the zip. A .ba2 in dist that this build doesn't make is refused (sf-mod-build ships only the archives
// it builds and the ones zip.archives lists); the mod's own archives go in under their own spelling, whatever their letter case in dist. Entries
// are sorted by name (ordinal), use forward slashes, a fixed timestamp (2000-01-01 00:00) and deflate. The zip is
// written to a temp file beside it and then moved into place.
// Exit 0 when the zip was written, 1 when writing it failed, 2 bad input or setup (no plugin in dist, a missing file,
// two files with the same name, an archive this build doesn't make).
using System.IO.Compression;

namespace SfModBuild;

internal static class Zip
{
    public const string Usage =
        "usage: sf-mod-build zip [<sfmod.json or its folder>] [--verbose] [--log <file>]\n" +
        "  writes the mod's zip (dist's files plus zip.files; zip.archives lists the archives your builder writes there) with fixed order and timestamps, so every run gives the same bytes\n" +
        "  (on the same operating system and .NET version)\n" +
        "  a mod with parts gets one zip per plugin: {name}-{version}.zip and {name}-<Part>-{version}.zip (zip.split false keeps one zip)";

    public static readonly ArgSpec Spec = new(Array.Empty<string>(), Array.Empty<string>(), MaxPositionals: 1);

    /// <summary>Every entry gets this time, so the zip doesn't change when the files haven't.</summary>
    public static readonly DateTimeOffset FixedTime = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public const string PartialSuffix = ".sfmb-partial";

    public static int Run(ParsedArgs p, IHostEnv env, TextWriter stdout)
    {
        var project = Common.LoadProject(p, stdout, "CANNOT ZIP");
        if (project == null) return ExitCodes.BadInput;
        return Write(project, stdout);
    }

    /// <summary>What goes in the zip, sorted by name in the zip: (full path here, name in the zip). Null (with the
    /// reason) when something is missing or two files would get the same name.</summary>
    public static List<(string Source, string NameInZip)>? Entries(SfModProject project, out string? error)
    {
        error = null;
        var plugin = Path.Combine(project.Dist, project.Plugin);
        if (!File.Exists(plugin))
        {
            error = $"there is no {project.Plugin} in {project.Dist}. Fix: run sf-mod-build plugin first (or sf-mod-build build)";
            return null;
        }
        foreach (var part in project.Parts)
        {
            if (!File.Exists(Path.Combine(project.Dist, part.Plugin)))
            {
                error = $"there is no {part.Plugin} in {project.Dist}, and {project.File} lists it in parts. Fix: run sf-mod-build plugin first (or sf-mod-build build)";
                return null;
            }
            if (part.Scripts != null && Common.FilesNamed(project.Dist, part.Ba2Name).Count == 0)
            {
                error = $"there is no \"{part.Ba2Name}\" in {project.Dist}, and the part {part.Plugin} has scripts. Fix: run sf-mod-build compile and sf-mod-build pack first (or sf-mod-build build)";
                return null;
            }
        }
        if (project.HasMainArchive && Common.FilesNamed(project.Dist, project.Ba2Name).Count == 0)
        {
            error = $"there is no \"{project.Ba2Name}\" in {project.Dist}, and the mod has {(project.Scripts != null ? "scripts" : "loose files")}. Fix: run {(project.Scripts != null ? "sf-mod-build compile and " : "")}sf-mod-build pack first (or sf-mod-build build)";
            return null;
        }
        if (project.HasTexturesArchive && Common.FilesNamed(project.Dist, project.TexturesBa2Name).Count == 0)
        {
            error = $"there is no \"{project.TexturesBa2Name}\" in {project.Dist}, and the mod has loose textures. Fix: run sf-mod-build pack first (or sf-mod-build build)";
            return null;
        }
        foreach (var a in project.Archives)
            if (Common.FilesNamed(project.Dist, a.Name).Count == 0)
            {
                error = $"there is no \"{a.Name}\" in {project.Dist}, and {project.File} lists it in archives. Fix: run sf-mod-build pack first (or sf-mod-build build)";
                return null;
            }
        foreach (var a in project.ZipArchives)
            if (Common.FilesNamed(project.Dist, a).Count == 0)
            {
                error = $"there is no \"{a}\" in {project.Dist}, and {project.File} lists it in zip.archives. Fix: make your builder write it into dist, or take it out of zip.archives";
                return null;
            }
        var list = new List<(string, string)>();
        // sorted, so the stray archive reported is the same on every disk
        foreach (var f in Directory.GetFiles(project.Dist).Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileName(f);
            if (!Common.Shippable(f))
            {
                Log.Write($"zip: left out {f}");
                continue;
            }
            if (Common.StrayArchive(project, f))
            {
                error = Common.StrayArchiveError(project, f, "the zip");
                return null;
            }
            // an archive this build makes goes in under its own spelling, even when dist has it in other letter case
            name = project.ArchiveNames.Concat(project.ZipArchives).FirstOrDefault(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase)) ?? name;
            list.Add((f, name));
        }
        foreach (var (source, inZip) in project.ZipFiles)
        {
            if (!File.Exists(source))
            {
                error = $"{source} (in zip.files of {project.File}) isn't there. Fix: put the file back, or take it out of zip.files";
                return null;
            }
            list.Add((source, inZip));
        }
        // Windows and most unzip tools treat names that differ only in case as the same file
        var dup = list.GroupBy(x => x.Item2, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (dup != null)
        {
            error = $"two files would both be \"{dup.Key}\" in the zip: {string.Join(" and ", dup.Select(x => x.Item1))}. Fix: give the zip.files entry in {project.File} another name, or remove the extra file from dist";
            return null;
        }
        list.Sort((a, b) => string.CompareOrdinal(a.Item2, b.Item2));
        return list;
    }

    /// <summary>One zip to write: where, and what goes in it (full path here, name in the zip, sorted by name).</summary>
    public sealed record Job(string ZipPath, List<(string Source, string NameInZip)> Entries);

    /// <summary>The word in a part's zip name: zipPart, else the part plugin's name without extension with a leading
    /// mod name taken off and only letters and digits left (the whole plugin name cleaned when nothing is left).
    /// Empty when even that leaves nothing.</summary>
    public static string PartLabel(SfModProject project, SfModProject.Part part)
    {
        if (!string.IsNullOrEmpty(part.ZipPart)) return part.ZipPart;
        static string Clean(string x) => new(x.Where(char.IsAsciiLetterOrDigit).ToArray());
        var b = part.PluginBase;
        if (project.Name.Length > 0 && b.StartsWith(project.Name, StringComparison.OrdinalIgnoreCase)) b = b[project.Name.Length..];
        var c = Clean(b);
        return c.Length > 0 ? c : Clean(part.PluginBase);
    }

    /// <summary>The part's zip: {name}-&lt;Part&gt;-{version}.zip beside the main zip. Null when the part has no usable label.</summary>
    public static string? PartZipPath(SfModProject project, SfModProject.Part part)
    {
        var label = PartLabel(project, part);
        return label.Length == 0 ? null : Path.Combine(project.ZipFolder, $"{project.Name}-{label}-{project.Version}.zip");
    }

    /// <summary>True when the build writes one zip per plugin: it has parts and zip.split isn't false.</summary>
    public static bool Splits(SfModProject project) => project.ZipSplit && project.Parts.Count > 0;

    /// <summary>Every zip this build writes, main zip first. Doesn't look at dist, so check can use it.</summary>
    public static List<string> ZipPaths(SfModProject project)
    {
        var list = new List<string> { Path.GetFullPath(project.ZipPath) };
        if (Splits(project))
            foreach (var part in project.Parts)
                if (PartZipPath(project, part) is { } z) list.Add(Path.GetFullPath(z));
        return list;
    }

    /// <summary>A LICENSE or COPYING file's name for a part's zip: {name} in it becomes {name}-&lt;Part&gt;, else
    /// -{name}-&lt;Part&gt; goes before the extension (so no two zips hold the same file).</summary>
    public static string PartDocName(string fileName, string modName, string label)
    {
        var at = modName.Length == 0 ? -1 : fileName.IndexOf(modName, StringComparison.OrdinalIgnoreCase);
        if (at >= 0) return fileName[..(at + modName.Length)] + "-" + label + fileName[(at + modName.Length)..];
        var dot = fileName.LastIndexOf('.');
        return dot > 0 ? fileName[..dot] + $"-{modName}-{label}" + fileName[dot..] : fileName + $"-{modName}-{label}";
    }

    static bool IsPluginFile(string name) =>
        name.EndsWith(".esm", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".esp", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".esl", StringComparison.OrdinalIgnoreCase);

    /// <summary>The zips to write. Without parts (or with zip.split false) it is the one zip of Entries. With parts each
    /// plugin gets its own zip, and nothing is guessed: a file whose owner can't be told stops the zip. Null (with the
    /// reason) when something is missing or unclear.</summary>
    public static List<Job>? Plan(SfModProject project, out string? error)
    {
        var entries = Entries(project, out error);
        if (entries == null) return null;
        var main = new Job(Path.GetFullPath(project.ZipPath), entries);
        if (!Splits(project) && project.ZipSplit == false) return new List<Job> { main };
        // a plugin in dist that the project doesn't name would ship silently, so it stops the zip (with or without parts)
        var named = new[] { project.Plugin }.Concat(project.Parts.Select(x => x.Plugin)).ToList();
        foreach (var (source, inZip) in entries)
            if (IsDistFile(project, source) && IsPluginFile(inZip) && !named.Contains(inZip, StringComparer.OrdinalIgnoreCase))
            {
                error = $"{source} is a plugin in dist that {project.File} doesn't name (plugin is {project.Plugin}{(project.Parts.Count > 0 ? ", parts lists " + string.Join(" and ", project.Parts.Select(x => x.Plugin)) : "")}), so zip can't tell which zip it belongs in. Fix: list it in parts, or delete it from dist";
                return null;
            }
        if (project.Parts.Count == 0) return new List<Job> { main };

        var mainJob = main with { Entries = new List<(string, string)>() };
        var jobs = new List<Job> { mainJob };
        var partJobs = new List<Job>();
        var labels = new List<string>();
        foreach (var part in project.Parts)
        {
            var label = PartLabel(project, part);
            var path = PartZipPath(project, part);
            if (path == null)
            {
                error = $"the part {part.Plugin} has no letters or digits to name its zip with. Fix: set \"zipPart\" on it in {project.File}";
                return null;
            }
            labels.Add(label);
            partJobs.Add(new Job(Path.GetFullPath(path), new List<(string, string)>()));
        }
        // two zips with one name (a part named like the main zip, or two parts alike) would overwrite each other
        var all = jobs.Concat(partJobs).ToList();
        var same = all.GroupBy(j => Path.GetFileName(j.ZipPath), StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (same != null)
        {
            error = $"two zips would both be \"{same.Key}\". Fix: set \"zipPart\" on one of the parts in {project.File}, or change zip.name";
            return null;
        }
        // the plugin that owns an archive named "<plugin base> - <suffix>.ba2": the longest base wins
        var owners = new List<(string Base, int Index)> { (project.PluginBase, -1) };
        for (var k = 0; k < project.Parts.Count; k++) owners.Add((project.Parts[k].PluginBase, k));
        foreach (var (source, inZip) in entries)
        {
            var target = mainJob;
            if (IsDistFile(project, source))
            {
                int idx = -1;
                var found = false;
                var k = project.Parts.ToList().FindIndex(x => string.Equals(x.Plugin, inZip, StringComparison.OrdinalIgnoreCase));
                if (k >= 0) { idx = k; found = true; }
                else if (inZip.EndsWith(".ba2", StringComparison.OrdinalIgnoreCase))
                {
                    k = project.Parts.ToList().FindIndex(x => x.Scripts != null && string.Equals(x.Ba2Name, inZip, StringComparison.OrdinalIgnoreCase));
                    if (k >= 0) { idx = k; found = true; }
                    else if (project.ArchiveNames.Any(a => string.Equals(a, inZip, StringComparison.OrdinalIgnoreCase)) && !project.Parts.Any(x => string.Equals(x.Ba2Name, inZip, StringComparison.OrdinalIgnoreCase))) found = true;
                    else
                    {
                        // zip.archives: the builder's own archive, owned by the plugin whose name starts it
                        var best = owners.Where(o => inZip.StartsWith(o.Base + " - ", StringComparison.OrdinalIgnoreCase)).OrderByDescending(o => o.Base.Length).Cast<(string Base, int Index)?>().FirstOrDefault();
                        if (best == null)
                        {
                            error = $"zip can't tell which plugin owns the archive \"{inZip}\" (zip.archives): no plugin's name followed by \" - \" starts it. Fix: name it \"<plugin> - <suffix>.ba2\", for example \"{project.PluginBase} - Extra.ba2\", so it goes with that plugin";
                            return null;
                        }
                        idx = best.Value.Index;
                        found = true;
                    }
                }
                if (found && idx >= 0) target = partJobs[idx];
            }
            Log.Write($"zip: {inZip} goes in {Path.GetFileName(target.ZipPath)}");
            target.Entries.Add((source, inZip));
        }
        // a copy of each LICENSE/COPYING for every part zip, named for the part, so the zips don't share a file
        var docs = mainJob.Entries.Where(e => IsLicenseName(Path.GetFileName(e.NameInZip))).ToList();
        for (var k = 0; k < partJobs.Count; k++)
            foreach (var (source, inZip) in docs)
            {
                var slash = inZip.LastIndexOf('/');
                var dir = slash >= 0 ? inZip[..(slash + 1)] : "";
                var renamed = dir + PartDocName(inZip[(slash + 1)..], project.Name, labels[k]);
                partJobs[k].Entries.Add((source, renamed));
                Log.Write($"zip: {renamed} (a copy of {inZip}) goes in {Path.GetFileName(partJobs[k].ZipPath)}");
            }
        foreach (var job in all)
        {
            var dup = job.Entries.GroupBy(x => x.NameInZip, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
            if (dup != null)
            {
                error = $"two files would both be \"{dup.Key}\" in {Path.GetFileName(job.ZipPath)}: {string.Join(" and ", dup.Select(x => x.Source))}. Fix: rename one of the files, or set \"zipPart\" on the part in {project.File}";
                return null;
            }
            job.Entries.Sort((a, b) => string.CompareOrdinal(a.NameInZip, b.NameInZip));
        }
        return all;
    }

    static bool IsLicenseName(string fileName) =>
        fileName.StartsWith("LICENSE", StringComparison.OrdinalIgnoreCase) || fileName.StartsWith("COPYING", StringComparison.OrdinalIgnoreCase);

    static bool IsDistFile(SfModProject project, string source) =>
        string.Equals(Path.GetFullPath(Path.GetDirectoryName(source) ?? ""), Path.GetFullPath(project.Dist), StringComparison.OrdinalIgnoreCase);

    public static int Write(SfModProject project, TextWriter stdout)
    {
        var jobs = Plan(project, out var error);
        if (jobs == null)
        {
            stdout.WriteLine($"RESULT: CANNOT ZIP ({error}).");
            return ExitCodes.BadInput;
        }
        // every zip goes to its temp file first and all are moved in after, so a failure leaves no mix of new and old zips
        var temps = new List<(Job Job, string Temp)>();
        try
        {
            foreach (var job in jobs)
            {
                var temp = job.ZipPath + PartialSuffix;
                Directory.CreateDirectory(Path.GetDirectoryName(job.ZipPath)!);
                if (File.Exists(temp)) File.Delete(temp);
                temps.Add((job, temp));
                using var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.ReadWrite);
                using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
                foreach (var (source, inZip) in job.Entries)
                {
                    var e = zip.CreateEntry(inZip, CompressionLevel.Optimal);
                    e.LastWriteTime = FixedTime;
                    using var to = e.Open();
                    using var from = File.OpenRead(source);
                    from.CopyTo(to);
                    Log.Write($"zip: {inZip} <- {source} ({new FileInfo(source).Length} bytes)");
                }
            }
            foreach (var (job, temp) in temps) File.Move(temp, job.ZipPath, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            foreach (var (_, temp) in temps)
                try { if (File.Exists(temp)) File.Delete(temp); } catch (Exception) { /* best effort */ }
            stdout.WriteLine($"RESULT: FAILED, {jobs[0].ZipPath} wasn't written ({e.Message}). Fix: close any program that has the zip or its files open (Vortex, an archive viewer) and run again.");
            return ExitCodes.Failed;
        }
        foreach (var job in jobs)
        {
            if (jobs.Count > 1) stdout.WriteLine($"zip      {Path.GetFileName(job.ZipPath)}");
            foreach (var (_, inZip) in job.Entries) stdout.WriteLine($"zipped   {inZip}");
            stdout.WriteLine($"sha256   {Sha256(job.ZipPath)}");
        }
        // a part zip left from an earlier build (a part since removed) is never deleted
        if (jobs.Count > 1 || Splits(project))
        {
            var made = jobs.Select(j => Path.GetFileName(j.ZipPath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var prefix = project.Name + "-";
            var suffix = "-" + project.Version + ".zip";
            if (Directory.Exists(project.ZipFolder))
                foreach (var old in Directory.GetFiles(project.ZipFolder, prefix + "*" + suffix).Select(Path.GetFileName).Order(StringComparer.Ordinal))
                    if (old != null && !made.Contains(old))
                        stdout.WriteLine($"note: {old} in {Path.GetFullPath(project.ZipFolder)} is from an earlier build and wasn't touched; delete it if its plugin is gone");
        }
        if (jobs.Count == 1)
            stdout.WriteLine($"RESULT: OK, {jobs[0].ZipPath} written ({jobs[0].Entries.Count} files, {new FileInfo(jobs[0].ZipPath).Length} bytes)");
        else
            stdout.WriteLine($"RESULT: OK, {jobs.Count} zips written in {Path.GetFullPath(project.ZipFolder)} ({string.Join("; ", jobs.Select(j => $"{Path.GetFileName(j.ZipPath)}: {j.Entries.Count} files, {new FileInfo(j.ZipPath).Length} bytes"))})");
        return ExitCodes.Ok;
    }

    public static string Sha256(string file)
    {
        using var s = File.OpenRead(file);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(s)).ToLowerInvariant();
    }
}
