using Xunit;

namespace SfModBuild.Tests;

/// <summary>0.4.1 code-review round 1 (2026-11-23): the made-list rules in normal use.</summary>
public partial class PluginPackTests
{
    /// <summary>A builder that writes the plugin sfmod.json names (read from the file each run), plus a fake Archive2.</summary>
    static Func<string, string, string, ProcessResult> BuilderAndArchive2(TempDir t, string sfmod) => (exe, args, dir) =>
    {
        if (exe.EndsWith("Archive2.exe", StringComparison.OrdinalIgnoreCase)) return FakeArchive2(exe, args, dir);
        var name = File.ReadAllText(sfmod).Contains("MyNewMod.esm") ? "MyNewMod.esm" : "MyMod.esm";
        File.WriteAllText(Mod(t, "dist", name), "made-up plugin");
        return new ProcessResult(0, "");
    };

    [Fact]
    public void PluginAfterARenameDeletesTheOldNamesArchive()
    {
        var (t, sfmod, game, env) = Setup();
        using (t)
        {
            CompiledScripts(t);
            env.ExecResult = BuilderAndArchive2(t, sfmod);
            Assert.True(RunApp(env, out var output, "plugin", sfmod, "--game", game) == ExitCodes.Ok, output);
            Assert.True(RunApp(env, out output, "pack", sfmod, "--game", game) == ExitCodes.Ok, output);
            Assert.True(File.Exists(Mod(t, "dist", "MyMod - Main.ba2")));
            File.WriteAllText(sfmod, File.ReadAllText(sfmod).Replace("MyMod.esm", "MyNewMod.esm"));
            var code = RunApp(env, out output, "plugin", sfmod, "--game", game);
            Assert.True(code == ExitCodes.Ok, output);
            Assert.False(File.Exists(Mod(t, "dist", "MyMod - Main.ba2")));
            Assert.False(File.Exists(Mod(t, "dist", "MyMod.esm")));
            Assert.True(File.Exists(Mod(t, "dist", "MyNewMod.esm")));
        }
    }

    [Fact]
    public void PackWithXboxKeepsTheModsZipWhenZipFolderIsCreationsOut()
    {
        var json = Json.Replace("\"version\": \"0.1.0\",", "\"version\": \"0.1.0\", \"xbox\": true, \"zip\": { \"folder\": \"creations-out\" },");
        var (t, sfmod, game, env) = Setup(json);
        using (t)
        {
            CompiledScripts(t);
            t.File(Path.Combine("mod", "dist", "MyMod.esm"), "made-up plugin");
            t.File(Path.Combine("mod", "creations-out", "MyMod-0.1.0.zip"), "the last build's zip");
            env.ExecResult = FakeArchive2;
            var code = RunApp(env, out var output, "pack", sfmod, "--game", game);
            Assert.True(code == ExitCodes.Ok, output);
            Assert.Equal("the last build's zip", File.ReadAllText(Mod(t, "creations-out", "MyMod-0.1.0.zip")));
            Assert.True(RunApp(env, out output, "pack", sfmod, "--game", game) == ExitCodes.Ok, output);
        }
    }

    /// <summary>0.4.1 code-review round 2: with zip.folder elsewhere, a zip in creations-out is the user's, so pack
    /// names it and stops instead of leaving it silently in the upload set.</summary>
    [Fact]
    public void PackWithXboxNamesAZipInCreationsOutWhenZipFolderIsElsewhere()
    {
        var (t, sfmod, game, env) = Setup(Json.Replace("\"version\": \"0.1.0\",", "\"version\": \"0.1.0\", \"xbox\": true,"));
        using (t)
        {
            CompiledScripts(t);
            t.File(Path.Combine("mod", "dist", "MyMod.esm"), "made-up plugin");
            t.File(Path.Combine("mod", "creations-out", "MyMod-0.1.0.zip"), "a zip the user copied here");
            env.ExecResult = FakeArchive2;
            var code = RunApp(env, out var output, "pack", sfmod, "--game", game);
            Assert.Equal(ExitCodes.BadInput, code);
            Assert.Contains("\"MyMod-0.1.0.zip\"", output);
            Assert.Equal("a zip the user copied here", File.ReadAllText(Mod(t, "creations-out", "MyMod-0.1.0.zip")));
        }
    }

    [Fact]
    public void PackWithXboxKeepsCreationsOutsListWhenADeleteFails()
    {
        var (t, sfmod, game, env) = Setup(Json.Replace("\"version\": \"0.1.0\",", "\"version\": \"0.1.0\", \"xbox\": true,"));
        using (t)
        {
            CompiledScripts(t);
            t.File(Path.Combine("mod", "dist", "MyMod.esm"), "made-up plugin");
            // what an earlier build left: an extra its builder no longer makes, after the archive in name order
            t.File(Path.Combine("mod", "creations-out", Common.MadeList), "MyMod - Main_xbox.ba2\nZed.txt\n");
            var locked = t.File(Path.Combine("mod", "creations-out", "MyMod - Main_xbox.ba2"), "old");
            t.File(Path.Combine("mod", "creations-out", "Zed.txt"), "old");
            env.ExecResult = FakeArchive2;
            string output;
            using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var code = RunApp(env, out output, "pack", sfmod, "--game", game);
                Assert.Equal(ExitCodes.BadInput, code);
                Assert.Contains("couldn't be deleted", output);
            }
            Assert.True(RunApp(env, out output, "pack", sfmod, "--game", game) == ExitCodes.Ok, output);
            Assert.False(File.Exists(Mod(t, "creations-out", "Zed.txt")));
        }
    }
}
