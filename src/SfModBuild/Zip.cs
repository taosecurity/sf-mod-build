// sf-mod-build zip [sfmod.json]: writes the mod's Vortex/MO2 zip reproducibly: the same files give the same bytes on
// every run on the same operating system and .NET version (the zip records the system that made it, and another
// .NET may compress differently). Contents: dist's top-level files (never zips or .psc) plus each zip.files entry
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
        "  (on the same operating system and .NET version)";

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

    public static int Write(SfModProject project, TextWriter stdout)
    {
        var entries = Entries(project, out var error);
        if (entries == null)
        {
            stdout.WriteLine($"RESULT: CANNOT ZIP ({error}).");
            return ExitCodes.BadInput;
        }
        var zipPath = Path.GetFullPath(project.ZipPath);
        var temp = zipPath + PartialSuffix;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
            if (File.Exists(temp)) File.Delete(temp);
            using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.ReadWrite))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                foreach (var (source, inZip) in entries)
                {
                    var e = zip.CreateEntry(inZip, CompressionLevel.Optimal);
                    e.LastWriteTime = FixedTime;
                    using var to = e.Open();
                    using var from = File.OpenRead(source);
                    from.CopyTo(to);
                    Log.Write($"zip: {inZip} <- {source} ({new FileInfo(source).Length} bytes)");
                }
            }
            File.Move(temp, zipPath, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch (Exception) { /* best effort */ }
            stdout.WriteLine($"RESULT: FAILED, {zipPath} wasn't written ({e.Message}). Fix: close any program that has the zip or its files open (Vortex, an archive viewer) and run again.");
            return ExitCodes.Failed;
        }
        foreach (var (_, inZip) in entries) stdout.WriteLine($"zipped   {inZip}");
        stdout.WriteLine($"sha256   {Sha256(zipPath)}");
        stdout.WriteLine($"RESULT: OK, {zipPath} written ({entries.Count} files, {new FileInfo(zipPath).Length} bytes)");
        return ExitCodes.Ok;
    }

    public static string Sha256(string file)
    {
        using var s = File.OpenRead(file);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(s)).ToLowerInvariant();
    }
}
