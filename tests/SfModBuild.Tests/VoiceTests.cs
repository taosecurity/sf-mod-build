using System.Text.RegularExpressions;
using Xunit;

namespace SfModBuild.Tests;

public class VoiceTests
{
    const string Json = """
        {
          "plugin": "MyMod.esm",
          "version": "0.1.0",
          "builder": { "prebuilt": "Original/MyMod.esm" },
          "voice": { "source": "Voice" }
        }
        """;

    const string Header = "VOICE TYPE\tFILENAME\tRESPONSE TEXT\tFACE\tSCRIPT NOTES\tSEX\tFULL PATH\tINFO\tRESPONSE\r\n";

    static string Row(string vt, string file, string text, string sex = "female") =>
        $"{vt}\t{file}\t{text}\t\t\t{sex}\tData\\Sound\\Voice\\MyMod.esm\\{vt}\\{file}.wem\t000800:MyMod.esm\t1\r\n";

    /// <summary>A made-up mod with a built plugin in dist, a fake game with the Creation Kit's voice tools, a fake
    /// Wwise (WWISEROOT) and sf-mod-check on PATH. The fake programs write what the real ones would.</summary>
    sealed class Rig : IDisposable
    {
        public TempDir T { get; } = new();
        public string Sfmod { get; }
        public string Game { get; }
        public FakeHostEnv Env { get; }
        public string Tsv { get; set; } = Header + Row("npcfmyvoice", "0001A2B3", "Hello there.") + Row("npcfmyvoice", "0001A2B4", "\"She said \"\"run\"\".\"");
        public int CheckerExit { get; set; }
        public List<string> WwiseSources { get; } = new();
        public List<string> LipArgs { get; } = new();

        public Rig(string json = Json)
        {
            T.File(Path.Combine("mod", "Original", "MyMod.esm"), "made-up plugin");
            T.File(Path.Combine("mod", "dist", "MyMod.esm"), "made-up plugin");
            Sfmod = T.File(Path.Combine("mod", "sfmod.json"), json);
            Game = Fake.Game(T, "game");
            T.Dir(Path.Combine("game", "Data"));
            foreach (var f in new[] { Path.Combine("LipGenerator", "LipGenerator.exe"), Path.Combine("FaceFX", "ffxc.exe"), Path.Combine("FaceFX", "GenesisDefault.facefx"),
                         Path.Combine("FaceFX", "StarfieldHumanFemale.facefx"), Path.Combine("FaceFX", "StarfieldHumanMale.facefx"), Path.Combine("wwise", "Starfield", "Starfield.wproj"),
                         Path.Combine("wwise", "Starfield", "Conversion Settings", "Default Work Unit.wwu") })
                T.File(Path.Combine("game", "Tools", f), "made-up tool");
            T.File(Path.Combine("Wwise2021.1.14.8108", "Authoring", "x64", "Release", "bin", "WwiseConsole.exe"), "made-up wwise");
            Env = Fake.EnvWithPath(T, "sf-mod-check");
            Env.Vars["WWISEROOT"] = Path.Combine(T.Path, "Wwise2021.1.14.8108");
            Env.ExecResult = Exec;
            Wav("npcfmyvoice", "0001A2B3", "wav one");
            Wav("npcfmyvoice", "0001A2B4", "wav two");
        }

        public string Mod(params string[] parts) => Path.Combine(new[] { T.Path, "mod" }.Concat(parts).ToArray());
        public string Data(string vt, string file) => Mod(".build", "voice", "data", "Sound", "Voice", "MyMod.esm", vt, file);
        public void Wav(string vt, string id, string content) => T.File(Path.Combine("mod", "Voice", vt, id + ".wav"), content);

        static List<string> Split(string args) =>
            Regex.Matches(args, "\"([^\"]*)\"|(\\S+)").Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value).ToList();

        ProcessResult Exec(string exe, string args, string workDir)
        {
            var a = Split(args);
            switch (Path.GetFileNameWithoutExtension(exe))
            {
                case "sf-mod-check":
                    if (CheckerExit != 0) return new ProcessResult(CheckerExit, "RESULT: CANNOT CHECK (a missing master)");
                    Assert.Equal("voice-lines", a[0]);
                    Assert.Contains("--utf8", a);
                    File.WriteAllText(a[a.IndexOf("--out") + 1], Tsv);
                    return new ProcessResult(0, "RESULT: 2 rows");
                case "WwiseConsole":
                    Assert.Equal("convert-external-source", a[0]);
                    var sources = File.ReadAllText(a[a.IndexOf("--source-file") + 1]);
                    WwiseSources.Add(sources);
                    var root = Regex.Match(sources, "Root=\"([^\"]+)\"").Groups[1].Value;
                    foreach (Match m in Regex.Matches(sources, "Path=\"([^\"]+)\" Conversion=\"[^\"]+\" Destination=\"([^\"]+)\""))
                    {
                        var to = Path.Combine(a[a.IndexOf("--output") + 1], "Windows", m.Groups[2].Value.Replace('\\', Path.DirectorySeparatorChar));
                        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                        File.WriteAllText(to, "wem of " + File.ReadAllText(Path.Combine(root, m.Groups[1].Value)));
                    }
                    return new ProcessResult(0, "Conversion done");
                case "LipGenerator":
                    LipArgs.Add(args);
                    var name = Regex.Match(args, "-OutputFileName:(\\S+)").Groups[1].Value;
                    File.WriteAllText(Path.Combine(workDir, name), "animset");
                    return new ProcessResult(0, "");
                case "ffxc":
                    var outDir = a.First(x => x.StartsWith("-o=", StringComparison.Ordinal))[3..];
                    foreach (Match m in Regex.Matches(File.ReadAllText(a[^1]), "- ([0-9A-F]{8})\\.animset"))
                    {
                        var to = Path.Combine(outDir, "StarfieldHumanFemale", "x86", m.Groups[1].Value + "_group", m.Groups[1].Value + ".ffxanim");
                        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                        File.WriteAllText(to, "ffxanim");
                    }
                    return new ProcessResult(0, "1 succeeded");
                case "Archive2":
                    var c = Regex.Match(args, "\"-c=(?<q>[^\"]+)\"|(?<![\"\\S])-c=(?<b>\\S+)");
                    File.WriteAllText(c.Groups["q"].Success ? c.Groups["q"].Value : c.Groups["b"].Value, "made-up ba2");
                    return new ProcessResult(0, "");
            }
            return new ProcessResult(3, "unexpected program " + exe);
        }

        public int Run(out string output, params string[] more)
        {
            var sw = new StringWriter();
            var code = App.Run(new[] { "voice", Sfmod, "--game", Game }.Concat(more).ToArray(), Env, sw);
            output = sw.ToString();
            return code;
        }

        public int Ran(string program) => Env.Execs.Count(e => Path.GetFileNameWithoutExtension(e.Exe) == program);

        public void Dispose() => T.Dispose();
    }

    [Fact]
    public void MakesTheWemAndLipFileOfEveryLineWithAWav()
    {
        using var r = new Rig();
        Assert.Equal(0, r.Run(out var o));
        Assert.Contains("RESULT: OK, 2 voice line(s)", o);
        Assert.Equal("wem of wav one", File.ReadAllText(r.Data("npcfmyvoice", "0001A2B3.wem")));
        Assert.True(File.Exists(r.Data("npcfmyvoice", "0001A2B3.ffxanim")));
        Assert.True(File.Exists(r.Data("npcfmyvoice", "0001A2B4.ffxanim")));
        // one Wwise run for all lines, with the voice conversion, from a copy of the Creation Kit's project
        Assert.Equal(1, r.Ran("WwiseConsole"));
        Assert.Contains("Conversion=\"Voice Conversion\"", r.WwiseSources[0]);
        Assert.True(File.Exists(r.Mod(".build", "voice", "wwise", "Starfield", "Starfield.wproj")));
        Assert.True(File.Exists(r.Mod(".build", "voice", "wwise", "Starfield", "Conversion Settings", "Default Work Unit.wwu")));
        // the text's quotes don't reach LipGenerator's command line as quotes
        Assert.Contains(r.LipArgs, a => a.Contains("She said 'run'."));
        Assert.Contains("StarfieldHumanFemale.facefx", r.LipArgs[0]);
    }

    [Fact]
    public void ASecondRunWithNothingChangedNeedsNoWwise()
    {
        using var r = new Rig();
        Assert.Equal(0, r.Run(out _));
        r.Env.Vars.Remove("WWISEROOT");
        r.Env.Execs.Clear();
        Assert.Equal(0, r.Run(out var o));
        Assert.Contains("0 to make, 2 unchanged", o);
        Assert.Equal(0, r.Ran("WwiseConsole") + r.Ran("LipGenerator") + r.Ran("ffxc"));
    }

    [Fact]
    public void AChangedWavOrTextIsMadeAgain()
    {
        using var r = new Rig();
        Assert.Equal(0, r.Run(out _));
        r.Wav("npcfmyvoice", "0001A2B3", "wav one, new take");
        r.Tsv = r.Tsv.Replace("Hello there.", "Hello there!").Replace("Hello", "Hello");
        Assert.Equal(0, r.Run(out var o));
        Assert.Contains("1 to make, 1 unchanged", o);
        Assert.Single(Regex.Matches(r.WwiseSources[1], "<Source "));
        Assert.Equal("wem of wav one, new take", File.ReadAllText(r.Data("npcfmyvoice", "0001A2B3.wem")));
    }

    [Fact]
    public void MissingAndExtraWavsAreWarningsAndOldFilesAreDeleted()
    {
        using var r = new Rig();
        r.Tsv += Row("npcfmyvoice", "0001A2B5", "No take yet.");
        r.Wav("npcfmyvoice", "00FFFFFF", "a stray take");
        r.T.File(Path.Combine("mod", ".build", "voice", "data", "Sound", "Voice", "MyMod.esm", "npcfmyvoice", "00000001.wem"), "left from an old build");
        Assert.Equal(0, r.Run(out var o));
        Assert.Contains("WARNING  1 line(s) have no WAV", o);
        Assert.Contains("npcfmyvoice\\0001A2B5", o);
        Assert.Contains("WARNING  1 WAV(s)", o);
        Assert.Contains("npcfmyvoice\\00FFFFFF", o);
        Assert.False(File.Exists(r.Data("npcfmyvoice", "00000001.wem")));
        Assert.Contains("1 old file(s) deleted", o);
    }

    [Fact]
    public void ALineAlreadyInTheAssetsNeedsNoWav()
    {
        using var r = new Rig(Json.Replace("\"voice\"", "\"assets\": { \"source\": \"Assets\" }, \"voice\""));
        r.T.File(Path.Combine("mod", "Assets", "Sound", "Voice", "MyMod.esm", "npcfmyvoice", "0001A2B5.wem"), "an old recording");
        r.Tsv += Row("npcfmyvoice", "0001A2B5", "Recorded long ago.");
        Assert.Equal(0, r.Run(out var o));
        Assert.DoesNotContain("have no WAV", o);
        Assert.Contains("1 already in assets.source", o);
    }

    [Fact]
    public void FacesNoneMakesNoLipFileAndAnUnknownSexIsAWarning()
    {
        using var r = new Rig(Json.Replace("\"source\": \"Voice\"", "\"source\": \"Voice\", \"faces\": { \"robotvoice\": \"none\" }"));
        r.Tsv = Header + Row("robotvoice", "00000010", "Beep.", sex: "") + Row("oddvoice", "00000011", "Hm.", sex: "");
        r.Wav("robotvoice", "00000010", "beep");
        r.Wav("oddvoice", "00000011", "hm");
        Assert.Equal(0, r.Run(out var o));
        Assert.Contains("the voice type oddvoice has no known sex", o);
        Assert.DoesNotContain("robotvoice has no known sex", o);
        Assert.Equal(0, r.Ran("LipGenerator"));
        Assert.True(File.Exists(r.Data("robotvoice", "00000010.wem")));
        Assert.False(File.Exists(r.Data("robotvoice", "00000010.ffxanim")));
    }

    [Fact]
    public void ALineWithNoTextGetsItsWemButNoLipFile()
    {
        using var r = new Rig();
        r.Tsv += Row("npcfmyvoice", "0001A2B5", "");
        r.Wav("npcfmyvoice", "0001A2B5", "a sigh");
        Assert.Equal(0, r.Run(out var o));
        Assert.Contains("WARNING  1 line(s) have no text", o);
        Assert.DoesNotContain("match no line", o);
        Assert.Equal("wem of a sigh", File.ReadAllText(r.Data("npcfmyvoice", "0001A2B5.wem")));
        Assert.False(File.Exists(r.Data("npcfmyvoice", "0001A2B5.ffxanim")));
        Assert.True(File.Exists(r.Data("npcfmyvoice", "0001A2B3.ffxanim")));
        Assert.Equal(2, r.Ran("LipGenerator"));
        Assert.Contains("3 voice line(s)", o);
        Assert.Contains("2 with a lip file", o);
        // a rerun keeps it as unchanged
        Assert.Equal(0, r.Run(out o));
        Assert.Contains("0 to make, 3 unchanged", o);
        Assert.True(File.Exists(r.Data("npcfmyvoice", "0001A2B5.wem")));
    }

    [Fact]
    public void AMaleVoiceUsesTheMaleFaceSet()
    {
        using var r = new Rig();
        r.Tsv = Header + Row("npcmvoice", "00000020", "Hey.", sex: "male");
        r.Wav("npcmvoice", "00000020", "hey");
        Assert.Equal(0, r.Run(out _));
        Assert.Contains("StarfieldHumanMale.facefx", r.LipArgs.Single());
    }

    [Fact]
    public void NoWwiseIsASetupError()
    {
        using var r = new Rig();
        r.Env.Vars.Remove("WWISEROOT");
        Assert.Equal(2, r.Run(out var o));
        Assert.Contains("RESULT: CANNOT MAKE VOICE (Audiokinetic Wwise wasn't found", o);
        Assert.Contains("--wwise", o);
    }

    [Fact]
    public void AFailedListStopsTheStep()
    {
        using var r = new Rig { CheckerExit = 2 };
        Assert.Equal(2, r.Run(out var o));
        Assert.Contains("voice-lines needs sf-mod-check 0.21.0 or later", o);
        Assert.Equal(0, r.Ran("WwiseConsole"));
    }

    [Fact]
    public void NoPluginInDistSaysToBuildItFirst()
    {
        using var r = new Rig();
        File.Delete(r.Mod("dist", "MyMod.esm"));
        Assert.Equal(2, r.Run(out var o));
        Assert.Contains("run sf-mod-build plugin first", o);
    }

    [Fact]
    public void WwiseThatMakesNoWemFails()
    {
        using var r = new Rig();
        var real = r.Env.ExecResult;
        r.Env.ExecResult = (exe, args, wd) => Path.GetFileNameWithoutExtension(exe) == "WwiseConsole" ? new ProcessResult(1, "error: license") : real(exe, args, wd);
        Assert.Equal(1, r.Run(out var o));
        Assert.Contains("RESULT: FAILED, Wwise made no .wem for 2 line(s)", o);
        Assert.Contains("error: license", o);
    }

    [Fact]
    public void ThePackStepPutsTheVoiceFilesInMainUncompressed()
    {
        using var r = new Rig();
        Assert.Equal(0, r.Run(out _));
        var sw = new StringWriter();
        Assert.Equal(0, App.Run(new[] { "pack", r.Sfmod, "--game", r.Game }, r.Env, sw));
        var archive = r.Env.Execs.Last(e => Path.GetFileNameWithoutExtension(e.Exe) == "Archive2");
        Assert.Contains("MyMod - Main.ba2", archive.Arguments);
        Assert.Contains("-compression=None", archive.Arguments);
        Assert.True(File.Exists(r.Mod(".build", "stage", "pack", "main", "Sound", "Voice", "MyMod.esm", "npcfmyvoice", "0001A2B3.wem")));
        Assert.Contains("staged   4 voice and lip file(s)", sw.ToString());
    }

    [Fact]
    public void ParseListReadsQuotedFieldsAndSkipsRowsThatWouldBeBadPaths()
    {
        var text = Header + "npcfa\t0000000A\t\"Say \"\"hi\"\"\tnow\"\tHappy\t\tfemale\tx\ty\t1\r\n"
                   + Row("..\\evil", "0000000B", "x") + Row("npcfa", "12345", "short number") + Row("npcfa", "0000000C", "");
        var lines = Voice.ParseList(text, out var error, out var skipped)!;
        Assert.Null(error);
        Assert.Equal(2, lines.Count);   // the row with no text stays: it still needs its .wem
        var one = lines[0];
        Assert.Equal("Say \"hi\"\tnow", one.Text);
        Assert.Equal("female", one.Sex);
        Assert.Equal("", lines[1].Text);
        Assert.Equal(2, skipped.Count);
        Assert.Null(Voice.ParseList("VOICE TYPE\tFILENAME\r\n", out error, out _));
        Assert.Contains("RESPONSE TEXT", error);
    }

    [Fact]
    public void VoiceClonersWholeOutputFolderWorksAsTheSource()
    {
        using var t = new TempDir();
        var deep = t.Dir(Path.Combine("output", "Sound", "Voice", "MyMod.esm"));
        Assert.Equal(deep, Voice.WavRoot(Path.Combine(t.Path, "output"), "MyMod.esm"));
        Assert.Equal(Path.Combine(t.Path, "other"), Voice.WavRoot(Path.Combine(t.Path, "other"), "MyMod.esm"));
    }

    [Fact]
    public void FindWwisePrefersVersion2021Point1()
    {
        using var t = new TempDir();
        foreach (var v in new[] { "Wwise2021.1.9.7847", "Wwise2021.1.14.8108", "Wwise2023.1.0.8367" })
            t.File(Path.Combine("Audiokinetic", v, "Authoring", "x64", "Release", "bin", "WwiseConsole.exe"));
        var env = new FakeHostEnv();
        env.Drives.Add(t.Path + Path.DirectorySeparatorChar);
        var found = Voice.FindWwise(null, env, out _)!;
        Assert.Contains("Wwise2021.1.14.8108", found);
        Assert.Equal(new Version(2021, 1, 14, 8108), Voice.WwiseVersion(found));
    }

    [Theory]
    [InlineData("\"xbox\": true,", "can't be used with \"xbox\": true")]
    [InlineData("\"voice\": { \"source\": \"Voice\", \"out\": \"dist/voice\" }, \"x\": 1,", "voice.out")]
    [InlineData("\"voice\": { \"source\": \"Voice\", \"faces\": { \"npcfa\": \"robot\" } }, \"x\": 1,", "isn't \"female\", \"male\" or \"none\"")]
    [InlineData("\"voice\": { \"source\": \"NoSuchFolder\" }, \"x\": 1,", "isn't there")]
    [InlineData("\"voice\": { \"source\": \"Voice\", \"wwiseProject\": \"Voice\" }, \"x\": 1,", "holds no .wproj")]
    [InlineData("\"assets\": { \"source\": \"Voice\" },", "put the WAVs into the archive")]
    [InlineData("\"stage\": \"Voice\",", "the WAVs would be deleted")]
    [InlineData("\"archives\": [ { \"suffix\": \"Voices\", \"source\": \"Voice\" } ],", "put the WAVs into the archive")]
    public void BadVoiceSectionsAreProblems(string insert, string expected)
    {
        using var t = new TempDir();
        t.File(Path.Combine("Original", "MyMod.esm"));
        t.File(Path.Combine("Voice", "npcfa", "00000001.wav"));
        // an inserted voice section replaces the default one (the "x" field is then a known-field error we ignore)
        var json = insert.StartsWith("\"voice\"", StringComparison.Ordinal)
            ? Json.Replace("\"voice\": { \"source\": \"Voice\" }", insert.TrimEnd(',').Replace(", \"x\": 1", ""))
            : Json.Replace("{\n  \"plugin\"", "{\n  " + insert + "\n  \"plugin\"").Replace("{\r\n  \"plugin\"", "{\r\n  " + insert + "\r\n  \"plugin\"");
        var problems = new List<ProjectProblem>();
        Assert.Null(SfModProject.Load(t.File("sfmod.json", json), problems));
        Assert.Contains(problems, x => !x.IsWarning && (x.Problem + x.Field).Contains(expected));
    }

    [Fact]
    public void AVoiceSourceThatOnlyHoldsAPackedFolderIsAllowed()
    {
        using var t = new TempDir();
        t.File(Path.Combine("Original", "MyMod.esm"));
        t.File(Path.Combine("Voice", "npcfa", "00000001.wav"));
        t.File(Path.Combine("Voice", "Assets", "meshes", "a.nif"));
        var json = Json.Replace("\"voice\"", "\"assets\": { \"source\": \"Voice/Assets\" }, \"voice\"");
        var problems = new List<ProjectProblem>();
        Assert.NotNull(SfModProject.Load(t.File("sfmod.json", json), problems));
        Assert.DoesNotContain(problems, x => !x.IsWarning);
    }

    [Fact]
    public void AVoiceTypeWithOnlyLinesWithoutTextGetsNoSexWarning()
    {
        using var r = new Rig();
        r.Tsv = Header + Row("oddvoice", "00000012", "", sex: "");
        r.Wav("oddvoice", "00000012", "a laugh");
        Assert.Equal(0, r.Run(out var o));
        Assert.DoesNotContain("no known sex", o);
        Assert.Contains("1 line(s) have no text", o);
        Assert.True(File.Exists(r.Data("oddvoice", "00000012.wem")));
        Assert.Equal(0, r.Ran("LipGenerator"));
    }

    [Fact]
    public void ValidateShowsTheVoiceSection()
    {
        using var r = new Rig();
        var sw = new StringWriter();
        Assert.Equal(0, App.Run(new[] { "validate", r.Sfmod }, r.Env, sw));
        Assert.Contains("voice:    WAVs from", sw.ToString());
        Assert.Contains("2 line WAV(s)", sw.ToString());
    }
}
