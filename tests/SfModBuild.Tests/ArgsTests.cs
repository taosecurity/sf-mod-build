using Xunit;

namespace SfModBuild.Tests;

public class ArgsTests
{
    static readonly ArgSpec Spec = new(new[] { "--game", "--checker" }, new[] { "--require-check" }, MaxPositionals: 1);

    [Fact]
    public void ReadsValuesFlagsAndPositionals()
    {
        var p = Args.Parse(new[] { "sfmod.json", "--game", @"C:\Games\Starfield", "--verbose", "--require-check", "--log=out.txt" }, Spec, out var error);
        Assert.Null(error);
        Assert.NotNull(p);
        Assert.Equal(@"C:\Games\Starfield", p!.Get("--game"));
        Assert.Equal("out.txt", p.Get("--log"));
        Assert.True(p.Has("--verbose"));
        Assert.True(p.Has("--require-check"));
        Assert.Equal(new[] { "sfmod.json" }, p.Positionals);
    }

    [Theory]
    [InlineData("--game")]
    [InlineData("--game", "--verbose")]
    [InlineData("--game", " ")]
    [InlineData("--game=")]
    public void MissingOrEmptyValueNamesTheOption(params string[] a)
    {
        Assert.Null(Args.Parse(a, Spec, out var error));
        Assert.StartsWith("--game needs the game folder after it", error);
        Assert.Contains("for example: --game", error);
    }

    [Fact]
    public void UnknownOptionSuggestsTheNearOne()
    {
        Assert.Null(Args.Parse(new[] { "--gmae", "x" }, Spec, out var error));
        Assert.Contains("unknown option --gmae", error);
        Assert.Contains("Did you mean --game?", error);
    }

    [Fact]
    public void UnknownFarOptionPointsAtHelp()
    {
        Assert.Null(Args.Parse(new[] { "--zzzzzzzz" }, Spec, out var error));
        Assert.Contains("--help", error);
    }

    [Fact]
    public void TwiceIsAnError()
    {
        Assert.Null(Args.Parse(new[] { "--game", "a", "--game", "b" }, Spec, out var error));
        Assert.Contains("given twice", error);
    }

    [Fact]
    public void FlagWithValueIsAnError()
    {
        Assert.Null(Args.Parse(new[] { "--verbose=yes" }, Spec, out var error));
        Assert.Contains("doesn't take a value", error);
    }

    [Fact]
    public void TooManyPositionals()
    {
        Assert.Null(Args.Parse(new[] { "a", "b" }, Spec, out var error));
        Assert.Contains("unexpected extra argument \"b\"", error);
        Assert.Null(Args.Parse(new[] { "a" }, Doctor.Spec, out error));
        Assert.Contains("takes only options", error);
    }
}
