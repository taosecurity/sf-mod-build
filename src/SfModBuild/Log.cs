// The troubleshooting log: what sf-mod-build read, what it found, what it ran and why it failed. Off unless asked for:
//   --verbose      log lines on stderr, each starting "log: "
//   --log <file>   the same lines, with times, written to that file (overwritten each run)
// It never writes to stdout, so the normal output is the same with or without it. Adapted from sfmodcheck's Log.cs.
using System.Diagnostics;
using System.Text;

namespace SfModBuild;

internal static class Log
{
    static bool toStderr;
    static StreamWriter? file;
    static string filePath = "";
    static readonly Stopwatch clock = Stopwatch.StartNew();

    public static bool On => toStderr || file != null;

    /// <summary>The full path of the open log file, or null. Output folders leave it out and refuse to empty around it.</summary>
    public static string? FilePath => file != null ? filePath : null;

    public static void ToStderr() => toStderr = true;

    /// <summary>Opens the log file (overwrites it). Returns an error text naming the file and the fix, or null.
    /// Refuses plugin, archive, zip and script names so a mistyped --log can't overwrite a mod's files.</summary>
    public static string? ToFile(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            var dir = Path.GetDirectoryName(full);
            if (dir != null && !Directory.Exists(dir))
                return $"can't write the log file {full}: the folder {dir} doesn't exist. Make the folder, or give --log a path in a folder that exists.";
            var ext = Path.GetExtension(full).ToLowerInvariant();
            if (ext is ".esm" or ".esp" or ".esl" or ".ba2" or ".zip" or ".psc" or ".pex" or ".json" or ".cs" or ".csproj")
                return $"won't write the log to {full}: that looks like one of your mod's files, and the log would overwrite it. " +
                       "Give --log a .txt file, for example --log sf-mod-build-log.txt";
            file = new StreamWriter(full, append: false, new UTF8Encoding(false)) { AutoFlush = true };
            filePath = full;
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return $"can't write the log file {path}: {e.Message} Give --log a path you can write to, such as a file in your Documents folder.";
        }
    }

    /// <summary>Writes a log line. A log file that stops working is closed with one note on stderr, and the run carries
    /// on, so its result is the same with or without the log.</summary>
    public static void Write(string text)
    {
        if (!On) return;
        foreach (var line in text.Replace("\r", "").Split('\n'))
        {
            if (toStderr) Console.Error.WriteLine($"log: {line}");
            if (file == null) continue;
            try { file.WriteLine($"{clock.Elapsed.TotalSeconds,8:F2}s  {line}"); }
            catch (Exception e) when (e is IOException or ObjectDisposedException or UnauthorizedAccessException)
            {
                var f = file;
                file = null;
                try { f.Dispose(); } catch (Exception) { /* already broken */ }
                Console.Error.WriteLine($"note: stopped writing the log file {filePath}: {e.Message} The run carries on.");
            }
        }
    }

    /// <summary>A file's size and modified time (UTC), for the log.</summary>
    public static string Describe(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            return fi.Exists ? $"{fi.FullName} ({fi.Length:N0} bytes, modified {fi.LastWriteTimeUtc:yyyy-MM-dd HH:mm:ss} UTC)" : $"{path} (not found)";
        }
        catch (Exception e) { return $"{path} ({e.GetType().Name}: {e.Message})"; }
    }

    /// <summary>Closes the log and turns it off (the tests run several commands in one process).</summary>
    public static void Close()
    {
        try { file?.Dispose(); } catch (Exception) { /* closing a broken log must not change the exit code */ }
        file = null;
        toStderr = false;
    }
}
