// Builds MyMod.esm with Mutagen and BuildKit (no Creation Kit needed). sf-mod-build runs it as:
//   dotnet run -c Release --project Builder -- <Starfield Data folder> <output folder> <script source folder>
// (the arguments come from builder.args in sfmod.json).
//
// Records (all new, prefix MYM_; no vanilla record is edited; Starfield.esm is opened for lookups
// and becomes a master once a record points into it):
//   QUST MYM_Quest          starts with the game, runs MYM:StarterQuestScript
//   GMOP MYM_Option_Debug   Settings > Gameplay Options: "Show Debug Messages (PC only)", off by default
//   GPOG MYM_Options        the mod's own "MyMod" group in Gameplay Options
// FormIDs stay the same from build to build through ids.lock.json, next to sfmod.json (commit it).
using BuildKit;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Starfield;

if (args.Length < 3)
{
    Console.Error.WriteLine("usage: MyMod.Builder <Starfield Data folder> <output folder> <script source folder>");
    return 2;
}
var (dataDir, outDir, scriptDir) = (args[0], args[1], args[2]);

// The mod folder is the first folder above the builder's output that holds sfmod.json.
var projectDir = new DirectoryInfo(AppContext.BaseDirectory);
while (projectDir != null && !File.Exists(Path.Combine(projectDir.FullName, "sfmod.json"))) projectDir = projectDir.Parent;
if (projectDir == null)
{
    Console.Error.WriteLine($"MyMod.Builder: no sfmod.json found above {AppContext.BaseDirectory}. Fix: keep the Builder folder inside the mod folder.");
    return 2;
}
var questScriptFile = Path.Combine(scriptDir, "MYM", "StarterQuestScript.psc");
if (!File.Exists(questScriptFile))
{
    Console.Error.WriteLine($"MyMod.Builder: {questScriptFile} is missing. Fix: pass the script source folder (sfmod.json builder.args \"{{scripts}}\").");
    return 2;
}

using var game = new GameData(dataDir);
game.Open("Starfield.esm");
var mod = Plugin.New("MyMod.esm", PluginStyle.Light, "AUTHOR_NAME", "MyMod: a starter mod made with sf-mod-build.");

// ---- quest -------------------------------------------------------------------------------------
var quest = Quests.New(mod, "MYM_Quest");
quest.Name = "MyMod";
quest.Data = new QuestData { Flags = Quest.Flag.StartGameEnabled | Quest.Flag.StartsEnabled };

// ---- gameplay options (options first, group last) ----------------------------------------------
var optDebug = GameplayOptions.Debug(mod, "MYM_Option_Debug", "MyMod's log lines on screen");
GameplayOptions.Group(mod, "MYM_Options", "MyMod", [optDebug]);

// ---- the quest's script and its properties (names match the Property lines in the .psc) --------
var questScript = new ScriptEntry { Name = "MYM:StarterQuestScript", Flags = ScriptEntry.Flag.Local };
questScript.Properties.Add(new ScriptObjectProperty
{
    Name = "MYM_Option_Debug",
    Object = optDebug.ToLink<IStarfieldMajorRecordGetter>(),
});
quest.VirtualMachineAdapter = new QuestAdapter { Version = 6, ObjectFormat = 2, Scripts = { questScript } };

// ---- write (ID lock, ONAM like the Creation Kit, master flags, one thread) ---------------------
Console.WriteLine(IdLock.Apply(mod, Path.Combine(projectDir.FullName, "ids.lock.json")));
var outPath = PluginWriter.Write(mod, outDir, game);
Console.WriteLine($"Wrote {outPath} ({new FileInfo(outPath).Length} bytes): {mod.EnumerateMajorRecords().Count()} records");
return 0;
