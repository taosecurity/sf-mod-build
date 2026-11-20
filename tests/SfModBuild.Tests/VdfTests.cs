using Xunit;

namespace SfModBuild.Tests;

public class VdfTests
{
    static string Sample(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "samples", name));

    [Fact]
    public void NewFormatListsEveryLibrary()
    {
        var libs = Vdf.LibraryFolders(Sample("libraryfolders-new.vdf"));
        Assert.Equal(new[] { @"C:\Program Files (x86)\Steam", @"E:\SteamLibrary" }, libs);
    }

    [Fact]
    public void OldFormatListsEveryLibraryAndSkipsOtherFields()
    {
        var libs = Vdf.LibraryFolders(Sample("libraryfolders-old.vdf"));
        Assert.Equal(new[] { @"D:\Games\Steam Lib", @"F:\SteamLibrary" }, libs);
    }

    [Fact]
    public void EscapedQuotesAreRead()
    {
        var root = Vdf.Parse(Sample("libraryfolders-new.vdf"));
        Assert.Equal("games \"fast\" disk", root["libraryfolders"]!["1"]!["label"]!.Value);
    }

    [Theory]
    [InlineData("\"libraryfolders\" { \"0\" { \"path\" \"C:\\\\x\" }")]
    [InlineData("\"a\" \"unclosed")]
    [InlineData("}")]
    [InlineData("\"key\"")]
    public void BrokenTextIsAFormatErrorWithALine(string text)
    {
        var e = Assert.Throws<FormatException>(() => Vdf.Parse(text));
        Assert.StartsWith("line ", e.Message);
    }

    [Fact]
    public void NoLibraryFoldersKeyGivesAnEmptyList() => Assert.Empty(Vdf.LibraryFolders("\"other\" { }"));
}
