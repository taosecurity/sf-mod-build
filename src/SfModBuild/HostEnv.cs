// Everything sf-mod-build asks of the machine (registry, drives, environment variables, running a program), behind
// one interface so the tests can give it a fake machine.
using System.Diagnostics;

namespace SfModBuild;

internal interface IHostEnv
{
    bool IsWindows { get; }
    /// <summary>A registry string such as (@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath"); null when it
    /// isn't there or this isn't Windows.</summary>
    string? RegistryString(string key, string valueName);
    /// <summary>The roots of the fixed drives, such as C:\ and D:\.</summary>
    IEnumerable<string> DriveRoots();
    string? EnvVar(string name);
    /// <summary>Runs a program and returns its standard output, or null (with the reason) when it can't be run or
    /// doesn't finish in time.</summary>
    string? Run(string exe, string arguments, out string? why);
    /// <summary>Runs a program in a working folder with the arguments exactly as given (one string, so quotes stay
    /// where the program wants them) and returns its exit code and output. Why is set when it couldn't be started or
    /// didn't finish in time.</summary>
    ProcessResult Exec(string exe, string arguments, string workingDirectory, TimeSpan timeout);
}

/// <summary>What a program run by Exec did: its exit code (-1 when it didn't run or finish), its standard output
/// followed by its standard error, why it failed to run (null when it ran), and whether the output was cut short
/// (a program it started kept the pipes open, or reading them failed).</summary>
internal sealed record ProcessResult(int ExitCode, string Output, string? Why = null, bool Incomplete = false);

internal sealed class RealHostEnv : IHostEnv
{
    public bool IsWindows => OperatingSystem.IsWindows();

    public string? RegistryString(string key, string valueName)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try { return Microsoft.Win32.Registry.GetValue(key, valueName, null) as string; }
        catch (Exception e) when (e is System.Security.SecurityException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            Log.Write($"registry {key} {valueName}: {e.GetType().Name}: {e.Message}");
            return null;
        }
    }

    public IEnumerable<string> DriveRoots()
    {
        DriveInfo[] drives;
        try { drives = DriveInfo.GetDrives(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { yield break; }
        foreach (var d in drives)
        {
            bool fixedReady;
            try { fixedReady = d.DriveType == DriveType.Fixed && d.IsReady; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { fixedReady = false; }
            if (fixedReady) yield return d.RootDirectory.FullName;
        }
    }

    public string? EnvVar(string name) => Environment.GetEnvironmentVariable(name);

    public string? Run(string exe, string arguments, out string? why)
    {
        why = null;
        try
        {
            var psi = new ProcessStartInfo(exe, arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p == null) { why = "it did not start"; return null; }
            var outTask = p.StandardOutput.ReadToEndAsync();
            var errTask = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(60_000))
            {
                try { p.Kill(entireProcessTree: true); } catch (Exception) { /* already gone */ }
                why = "it did not finish within 60 seconds";
                return null;
            }
            var output = outTask.Result;
            if (p.ExitCode != 0) { why = $"it exited with code {p.ExitCode}: {errTask.Result.Trim()}"; return null; }
            return output;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            why = e.Message;
            return null;
        }
    }

    /// <summary>How long to wait for a finished program's output pipes to close.</summary>
    static readonly TimeSpan PipeGrace = TimeSpan.FromSeconds(20);

    /// <summary>Reads a stream to its end in the background, keeping what has been read so far in text (lock it).
    /// The task's result is true when the stream was read to its end, false when reading stopped on an error.</summary>
    static Task<bool> Drain(StreamReader reader, out System.Text.StringBuilder text)
    {
        var sb = new System.Text.StringBuilder();
        text = sb;
        return Task.Run(async () =>
        {
            var buf = new char[4096];
            int n;
            try
            {
                while ((n = await reader.ReadAsync(buf, 0, buf.Length)) > 0)
                    lock (sb) sb.Append(buf, 0, n);
                return true;
            }
            // it only gathers text: any failure (the process killed, the reader closed) ends the
            // reading, and the result says the output is incomplete
            catch (Exception) { return false; }
        });
    }

    public ProcessResult Exec(string exe, string arguments, string workingDirectory, TimeSpan timeout)
    {
        try
        {
            var psi = new ProcessStartInfo(exe)
            {
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            // dotnet run (the builder step) would otherwise leave MSBuild nodes, the MSBuild server and the compiler
            // server running, holding our output pipes open after it exits. These are MSBuild's own switches;
            // the game's tools ignore them.
            psi.Environment["MSBUILDDISABLENODEREUSE"] = "1";
            psi.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";
            psi.Environment["UseSharedCompilation"] = "false";
            using var p = Process.Start(psi);
            if (p == null) return new ProcessResult(-1, "", "it did not start");
            var outTask = Drain(p.StandardOutput, out var outText);
            var errTask = Drain(p.StandardError, out var errText);
            if (!p.WaitForExit(timeout))
            {
                try { p.Kill(entireProcessTree: true); } catch (Exception) { /* already gone */ }
                return new ProcessResult(-1, "", $"it did not finish within {timeout.TotalSeconds:0} seconds");
            }
            // The program has exited. A helper it left running may still hold the pipes, so wait a short while for
            // the rest of its output, never forever; then go on with what was read and the real exit code.
            var complete = Task.WaitAll(new Task[] { outTask, errTask }, PipeGrace) && outTask.Result && errTask.Result;
            string output, err;
            lock (outText) output = outText.ToString();
            lock (errText) err = errText.ToString();
            if (err.Length > 0) output = output.Length > 0 && !output.EndsWith('\n') ? output + "\n" + err : output + err;
            if (!complete)
                output += (output.Length > 0 && !output.EndsWith('\n') ? "\n" : "") +
                          $"(sf-mod-build: the program exited with code {p.ExitCode}, but the rest of its output couldn't be read (a program it started kept it open, or reading it failed), so some of it may be missing)";
            return new ProcessResult(p.ExitCode, output, Incomplete: !complete);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return new ProcessResult(-1, "", e.Message);
        }
    }
}

/// <summary>Finding programs on PATH.</summary>
internal static class PathSearch
{
    /// <summary>The full path of the first program named one of names on PATH (with PATHEXT's extensions on
    /// Windows), or null.</summary>
    public static string? Find(IHostEnv env, params string[] names)
    {
        var path = env.EnvVar("PATH") ?? "";
        var exts = env.IsWindows
            ? (env.EnvVar("PATHEXT") is { Length: > 0 } pe ? pe : ".COM;.EXE;.BAT;.CMD").ToLowerInvariant().Split(';', StringSplitOptions.RemoveEmptyEntries)
            : new[] { "" };
        foreach (var name in names)
            foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var d = dir.Trim().Trim('"');
                if (d.Length == 0) continue;
                // on Windows a name with an extension is tried as it is, then every name with each PATHEXT extension
                var tries = env.IsWindows && Path.HasExtension(name) ? exts.Prepend("") : exts;
                foreach (var ext in tries)
                {
                    string candidate;
                    try { candidate = Path.Combine(d, name + ext); }
                    catch (ArgumentException) { continue; }
                    if (File.Exists(candidate)) return candidate;
                }
            }
        return null;
    }
}
