using Xunit;

namespace SfModBuild.Tests;

// 0.4.0: check and build take --masters-dir (repeatable) and pass each one to the checker, for a master outside Data.
public partial class ZipCheckBuildTests
{
    [Fact]
    public void CheckPassesEachMastersDirToTheChecker()
    {
        var (t, sfmod, game, env) = Setup(onPath: "sf-mod-check");
        using (t)
        {
            var main = t.Dir(Path.Combine("mods", "Main Mod"));
            var other = t.Dir(Path.Combine("mods", "Other"));
            Assert.Equal(ExitCodes.Ok, RunApp(env, out _, "zip", sfmod));
            env.ExecResult = FakeChecker;
            var code = RunApp(env, out var output, "check", sfmod, "--game", game, "--masters-dir", main, "--masters-dir", other);
            Assert.True(code == ExitCodes.Ok, output);
            var (_, args, _) = Assert.Single(env.Execs);
            Assert.Equal($"release {Mod(t, "MyMod-0.1.0.zip")} --data {Path.Combine(game, "Data")} --masters-dir \"{main}\" --masters-dir {other}", args);
        }
    }

    [Fact]
    public void AMastersDirThatIsntThereStopsCheckAndBuildBeforeTheyRun()
    {
        var (t, sfmod, game, env) = Setup(onPath: "sf-mod-check");
        using (t)
        {
            var missing = Path.Combine(t.Path, "no such folder");
            Assert.Equal(ExitCodes.Ok, RunApp(env, out _, "zip", sfmod));
            Assert.Equal(ExitCodes.BadInput, RunApp(env, out var output, "check", sfmod, "--game", game, "--masters-dir", missing));
            Assert.Contains($"--masters-dir {missing} isn't a folder", output);
            Assert.Equal(ExitCodes.BadInput, RunApp(env, out output, "build", sfmod, "--game", game, "--masters-dir", missing));
            Assert.Contains("isn't a folder", output);
            Assert.DoesNotContain("== compile", output);
            Assert.Empty(env.Execs);
        }
    }

    [Fact]
    public void OnlyRepeatableOptionsMayBeGivenTwice()
    {
        var p = Args.Parse(new[] { "--masters-dir", "a", "--masters-dir", "b" }, Check.Spec, out var error);
        Assert.Null(error);
        Assert.Equal(new[] { "a", "b" }, p!.GetAll("--masters-dir"));
        Assert.Null(Args.Parse(new[] { "--game", "a", "--game", "b" }, Check.Spec, out error));
        Assert.Contains("given twice", error);
    }
}
