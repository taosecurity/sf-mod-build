// The tool's log (Log.cs) is one static writer for the whole process, and App.Run closes it at the end of every run,
// so test classes that run commands must not run at the same time.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace SfModBuild.Tests;

/// <summary>A made-up machine: registry values, drives, environment variables and program output set by the test.</summary>
internal sealed class FakeHostEnv : IHostEnv
{
    public bool IsWindows { get; set; } = OperatingSystem.IsWindows();
    public Dictionary<(string, string), string> Registry { get; } = new();
    public List<string> Drives { get; } = new();
    public Dictionary<string, string> Vars { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Func<string, string, string?> RunResult { get; set; } = (_, _) => "9.0.321 [/fake/sdk]\n";
    public List<string> Ran { get; } = new();

    public string? RegistryString(string key, string valueName) => Registry.TryGetValue((key, valueName), out var v) ? v : null;
    public IEnumerable<string> DriveRoots() => Drives;
    public string? EnvVar(string name) => Vars.TryGetValue(name, out var v) ? v : null;
    /// <summary>The made-up Documents folder; null (the default) means the account has none.</summary>
    public string? Documents { get; set; }
    public string? DocumentsFolder() => Documents;

    public string? Run(string exe, string arguments, out string? why)
    {
        Ran.Add($"{exe} {arguments}");
        var r = RunResult(exe, arguments);
        why = r == null ? "fake failure" : null;
        return r;
    }

    /// <summary>What Exec does: given the program, the arguments and the working folder. Default: exit 0, no output.</summary>
    public Func<string, string, string, ProcessResult> ExecResult { get; set; } = (_, _, _) => new ProcessResult(0, "");
    public List<(string Exe, string Arguments, string WorkingDirectory)> Execs { get; } = new();

    public ProcessResult Exec(string exe, string arguments, string workingDirectory, TimeSpan timeout)
    {
        Execs.Add((exe, arguments, workingDirectory));
        return ExecResult(exe, arguments, workingDirectory);
    }
}

/// <summary>A temp folder deleted at the end of the test.</summary>
internal sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sfmb-test-" + Guid.NewGuid().ToString("N")[..10]);
    public TempDir() => Directory.CreateDirectory(Path);

    public string File(string relative, string text = "x")
    {
        var full = System.IO.Path.Combine(Path, relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        System.IO.File.WriteAllText(full, text);
        return full;
    }

    public string Dir(string relative)
    {
        var full = System.IO.Path.Combine(Path, relative);
        Directory.CreateDirectory(full);
        return full;
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

internal static class Fake
{
    public static string ExeName(string name) => OperatingSystem.IsWindows() ? name + ".exe" : name;

    /// <summary>A fake game folder with the Creation Kit's tools (empty made-up files).</summary>
    public static string Game(TempDir t, string relative, bool tools = true)
    {
        t.File(System.IO.Path.Combine(relative, "Starfield.exe"));
        if (tools)
        {
            t.File(System.IO.Path.Combine(relative, "Tools", "Papyrus Compiler", "PapyrusCompiler.exe"));
            t.File(System.IO.Path.Combine(relative, "Tools", "Archive2", "Archive2.exe"));
            t.File(System.IO.Path.Combine(relative, "Tools", "ContentResources.zip"));
        }
        return System.IO.Path.Combine(t.Path, relative);
    }

    /// <summary>A fake env whose PATH is one bin folder holding the named programs.</summary>
    public static FakeHostEnv EnvWithPath(TempDir t, params string[] programs)
    {
        var bin = t.Dir("bin");
        foreach (var p in programs) t.File(System.IO.Path.Combine("bin", ExeName(p)));
        var env = new FakeHostEnv();
        env.Vars["PATH"] = bin;
        env.Vars["PATHEXT"] = ".COM;.EXE;.BAT;.CMD";
        return env;
    }
}
