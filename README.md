# sf-mod-build

sf-mod-build builds a Starfield mod from one project file: you describe your mod once in `sfmod.json`, and one
command turns its scripts and its plugin builder into a zip that Vortex and Mod Organizer 2 install as is. It also
comes with a starter project (`dotnet new sfmod`), so a new mod builds on the first try.

Download the latest release from [Releases](https://github.com/taosecurity/sf-mod-build/releases); [Install](#install)
has the steps.

## First command

With the tool installed (see [Install](#install)), open a Command Prompt and check that it can find everything it
needs:

```
sf-mod-build doctor
```

It lists the game, the Creation Kit's compiler, Archive2 and script sources, the .NET SDK and the checker, and says
how to get anything that's missing. Then make a mod and build it:

```
dotnet new sfmod -n MyMod --prefix MYM
cd MyMod
sf-mod-build build
```

The last line says `RESULT: OK, built ...\MyMod-0.1.0.zip`. Drop that zip onto Vortex or MO2.

For AI assistants: step-by-step instructions are in [AGENTS.md](AGENTS.md).

## Example

`doctor` on a PC with the Steam game, the Creation Kit and the .NET SDK, but no checker yet. After the tools a build
needs, it checks the settings you want for testing and the Creation Kit, and gives the exact lines to add (output
shortened):

    > sf-mod-build doctor
    FOUND    Starfield: C:\Program Files (x86)\Steam\steamapps\common\Starfield (Steam folder)
    FOUND    Papyrus compiler: C:\Program Files (x86)\Steam\steamapps\common\Starfield\Tools\Papyrus Compiler\PapyrusCompiler.exe
    FOUND    Archive2: C:\Program Files (x86)\Steam\steamapps\common\Starfield\Tools\Archive2\Archive2.exe
    FOUND    Script sources (ContentResources.zip): C:\Program Files (x86)\Steam\steamapps\common\Starfield\Tools\ContentResources.zip
    FOUND    .NET SDK: C:\Program Files\dotnet\dotnet.exe (SDK 9.0.321, 10.0.402, 10.0.403, 10.0.404)
    WARNING  Checker: not found
             what it is for: checks the finished zip (release check); without it the build still works, unchecked
             how to get it: Get sf-mod-check (called sfmodcheck before its rename) from https://github.com/taosecurity/sf-mod-check/releases. Put it on PATH, or give it with --checker <exe>.
    ...
    setup (not needed to build; recommended for testing and the Creation Kit):
    WARNING  CreationKitCustom.ini (Creation Kit settings): C:\Program Files (x86)\Steam\steamapps\common\Starfield\CreationKitCustom.ini: the file isn't there (or this account can't see it)
             what it is for: the Creation Kit's own settings: the Material Editor works from the unpacked materials, scripts are found in Data\Scripts\Source, and loose files win while you test
             fix: make C:\Program Files (x86)\Steam\steamapps\common\Starfield\CreationKitCustom.ini with these lines:
                 [Materials]
                 bUseCompiledDB=0
                 [Papyrus]
                 sScriptSourceFolder="Data/Scripts/Source"
                 [Archive]
                 bInvalidateOlderFiles=1
    ...
    RESULT: OK, ready to build (with 4 WARNINGS: see above)

Then `build` on a small test mod called AssetTest, made from the starter with a mesh, a material and a texture added
in its `Assets` folder, with sf-mod-check 0.9.1 given as the checker (folder names shortened to `C:\Mods\AssetTest`
and `C:\Tools`):

    > sf-mod-build build --checker C:\Tools\sf-mod-check.exe
    == compile
    game script sources: ...\sf-mod-build\vanilla\b5f3f66d6041193c2db139c50a8a4a2da7a80c862a95ba5eec929c7a2c42983c\Scripts\Source
    compiled ATM\StarterQuestScript.psc
    RESULT: OK, 1 script(s) compiled into C:\Mods\AssetTest\.build\stage\Scripts
    == plugin
    running the builder: AssetTest.Builder.csproj (the first run restores and builds it, which can take a few minutes)
    built    C:\Mods\AssetTest\dist\AssetTest.esm (810 bytes)
    RESULT: OK, AssetTest.esm built into C:\Mods\AssetTest\dist
    == pack
    staged   2 general and 1 texture file(s) from C:\Mods\AssetTest\Assets
    packed   C:\Mods\AssetTest\dist\AssetTest - Main.ba2 (871 bytes)
    packed   C:\Mods\AssetTest\dist\AssetTest - Textures.ba2 (122 bytes)
    RESULT: OK, "AssetTest - Main.ba2" and "AssetTest - Textures.ba2" made in C:\Mods\AssetTest\dist
    == zip
    zipped   AssetTest - Main.ba2
    zipped   AssetTest - Textures.ba2
    zipped   AssetTest.esm
    zipped   LICENSE-AssetTest.txt
    zipped   README-AssetTest.md
    sha256   92f912185c08be39cf749d5c5eef451570b5581d68fe4c58cdab24a945c49d17
    RESULT: OK, C:\Mods\AssetTest\AssetTest-0.1.0.zip written (5 files, 3154 bytes)
    == check
    checking AssetTest-0.1.0.zip with sf-mod-check.exe
             sf-mod-check 0.9.1 release (basic profile): AssetTest-0.1.0.zip (5 files, 1 plugins, 2 archives)
             NOTE     [dmmdeps] DMMDeps wasn't given (--dmmdeps), so only sf-mod-check's own check of the files the plugins need ran: attached scripts and model paths. Textures, materials and voice files were not checked.
             NOTE     [plugin-check] AssetTest.esm: OK, 0 errors, 0 warnings (light master, 0 masters, 3 records, 0 overrides (0 ITM), 2 links)
             NOTE     [mutagen-gap] AssetTest.esm: 3 own records, 0 overrides, 0 compared with their master's copy
             RESULT: OK (0 problems, 0 to review, 0 warnings)
             sf-mod-check 0.9.1 by Richard Bejtlich - GPL-3.0 - https://github.com/taosecurity/sf-mod-check
    RESULT: OK, AssetTest-0.1.0.zip passed the release check
    RESULT: OK, built C:\Mods\AssetTest\AssetTest-0.1.0.zip

If you still have a copy of the checker from before its rename, called `sfmodcheck.exe`, sf-mod-build finds and runs
it the same way.

## What it does

One `sf-mod-build build` runs five steps in order and stops at the first one that fails:

1. It compiles your Papyrus scripts with **your own** Creation Kit compiler (never with `-release`, so your
   `Debug.Trace` logging keeps working).
2. It runs your mod's Mutagen builder (for example one made with BuildKit) to write the plugin.
3. It packs the compiled scripts and your loose files (meshes, materials, sounds, terrain) into `<Plugin> - Main.ba2`
   and your textures into `<Plugin> - Textures.ba2`, with **your own** Creation Kit Archive2 (and the Xbox archives
   for Creations, if you ask for them).
4. It writes a zip that Vortex and Mod Organizer 2 install as is, with the same bytes every time from the same inputs.
5. It runs the `release` check of sf-mod-check on that zip, when you have it.

sf-mod-build ships no game files. It uses the Papyrus compiler, Archive2 and script sources that the Creation Kit
installs into your own copy of the game.

## What you need

- Windows 10 or 11 (the Creation Kit's compiler and Archive2 are Windows programs).
- Starfield on **Steam**, with the **Starfield Creation Kit** installed from Steam (Library > Tools). The Creation
  Kit puts `Tools\Papyrus Compiler\PapyrusCompiler.exe`, `Tools\Archive2\Archive2.exe` and
  `Tools\ContentResources.zip` (the game's script sources) into the game folder.
- The **.NET 9 SDK** (or later): https://dotnet.microsoft.com/download. Your mod's builder is built with it.
- Optional: **sf-mod-check** 0.7.0 or later (0.7.0 added the `release` command that sf-mod-build runs), from
  https://github.com/taosecurity/sf-mod-check/releases. Unzip it and put its folder on PATH, or give the full path
  of `sf-mod-check.exe` with `--checker`. sf-mod-build finds it under its new name `sf-mod-check` or its old name
  (`sfmodcheck`, before the rename). Without it the build still works, but the zip isn't checked.

## Install

To open a Command Prompt in a folder: open the folder in File Explorer, click the address bar at the top, type `cmd`
and press Enter. A Command Prompt window opens in that folder, ready for the commands below.

Pick one way to install the tool:

- **As a dotnet tool** (needs the .NET SDK, which you need anyway): download `sf-mod-build.0.2.1.nupkg` from the
  [release](https://github.com/taosecurity/sf-mod-build/releases) into a folder of its own, open a Command Prompt
  in that folder, and run:

  ```
  dotnet tool install --global sf-mod-build --add-source .
  ```

  Update later with `dotnet tool update --global sf-mod-build --add-source <that folder>`.
- **As a program**: download `sf-mod-build-0.2.1-win-x64.zip` from the release and check it against
  `SHA256SUMS` (`certutil -hashfile sf-mod-build-0.2.1-win-x64.zip SHA256` prints the hash; in Git Bash or on Linux,
  `sha256sum -c SHA256SUMS` in the download folder checks every file, from 0.2.1 on). Right-click the zip,
  choose **Extract All...**, and put that folder on PATH (or call `sf-mod-build.exe` by its full path).
- **From source**: `dotnet build src\SfModBuild -c Release`; the program is
  `src\SfModBuild\bin\Release\net9.0\sf-mod-build.exe`.

Then install the starter template. Unzip `sf-mod-build-template-0.2.1.zip` from the release into a folder of its
own, for example `C:\Tools\sfmod-template`, and give that folder to `dotnet new install`:

```
dotnet new install C:\Tools\sfmod-template
```

From a clone of this repo, run `dotnet new install templates\sfmod` in the repo's folder instead.
`dotnet new uninstall <the same full path>` removes it again.

## Words used here

- **Plugin:** a mod's data file (`.esm`, `.esp` or `.esl`). It adds or changes records in the game.
- **Master:** a plugin that another plugin needs loaded before it. Starfield.esm is the master of nearly every mod.
- **Light master:** a master plugin flagged to use a small slot, so it doesn't count toward the limit of about 250
  full plugins. The starter project makes one.
- **Record:** one thing in a plugin, such as a weapon, a quest, a location or a setting.
- **EditorID:** a record's readable name, such as `Ammo_556`, used by the Creation Kit and xEdit.
- **Papyrus:** Starfield's scripting language. Scripts are written as `.psc` source files and compiled into `.pex`
  files that the game runs.
- **BA2:** Bethesda's archive format. Mods pack their scripts, models and textures into `.ba2` files.
- **Creation Kit (CK):** Bethesda's official editor for making Starfield mods. It also installs the Papyrus compiler
  and Archive2, the tool that makes BA2 files.
- **Mutagen:** a .NET library for reading and writing Bethesda plugins. A Mutagen builder is a small C# program that
  writes your plugin.
- **BuildKit:** a library on top of Mutagen that the starter project's builder uses.
- **Creations:** Bethesda's own mod service, which also reaches Xbox; a Creation needs Xbox BA2s as well.
- **Loose files:** game files (meshes, textures, sounds) that sit in folders under `Data` instead of inside a BA2.
  They are handy while you work; a release packs them into archives.

## The starter project

```
dotnet new sfmod -n MyMod --prefix MYM --mod-author "Your Name"
```

- `-n` is the mod's name (also the plugin `MyMod.esm` and the folder).
- `--prefix` is the short prefix for the mod's EditorIDs and its Papyrus namespace (default `MYM`).
- `--mod-author` goes into the plugin header and the LICENSE (default "Your Name").

You get these files:

- `sfmod.json`, the project file.
- A `Builder` project that makes a small light master with one quest. It uses the nuget packages
  `Taosecurity.Starfield.BuildKit` 0.4.2 and `Mutagen.Bethesda.Starfield` 0.54.4, both pinned exactly: 0.4.2 is the
  BuildKit version sf-mod-build 0.2.1 was tested with. The starter plugin it makes is byte for byte the same as
  with BuildKit 0.4.0, which earlier versions of the template used.
- A commented-out `"assets"` line in `sfmod.json`, for when you add meshes, textures or sounds (see
  [Loose files](#loose-files-meshes-textures-and-sounds)).
- One starter script, `Scripts\Source\MYM\StarterQuestScript.psc`, that writes `[MyMod]` lines to the Papyrus log.
- A "Show Debug Messages (PC only)" switch in the mod's own Gameplay Options group.
- A README that says how to turn on Papyrus logging, and a LICENSE to fill in.

The starter mod edits no vanilla records.

## Commands

Every command takes the project file, or the folder that holds it, as its first argument; the default is
`sfmod.json` in the current folder. `--game <folder>` points at the game folder (the one holding `Starfield.exe`)
when it isn't found by itself. Every command has `--help`.

| Command | What it does |
|---|---|
| `sf-mod-build doctor [--game <folder>] [--checker <exe>] [--my-games <folder>]` | Finds the game, the Creation Kit's tools, .NET and the checker, and says how to get anything missing. Then it checks your setup (see [Setup checks](#setup-checks-in-doctor)). |
| `sf-mod-build validate [<sfmod.json>]` | Checks the project file and prints what a build would use. Each problem names the field and the fix. |
| `sf-mod-build compile [<sfmod.json>] [--game <folder>]` | Compiles the scripts into `<stage>\Scripts` (stage is emptied first). Never `-release`: asking for it is refused with the reason. |
| `sf-mod-build plugin [<sfmod.json>] [--game <folder>]` | Runs the builder (`dotnet run -c Release --project <builder> -- <args>`) to write the plugin into dist (dist is emptied first). |
| `sf-mod-build pack [<sfmod.json>] [--game <folder>]` | Packs the compiled scripts and the general loose files into `<plugin> - Main.ba2` and the textures into `<plugin> - Textures.ba2`, in dist; with `"xbox": true` it also fills creations-out with dist's files plus `<plugin> - Main_xbox.ba2` and `<plugin> - Textures_xbox.ba2`. |
| `sf-mod-build zip [<sfmod.json>]` | Writes the zip: dist's files plus `zip.files`, sorted, with fixed timestamps, so the same inputs give the same bytes. Never `.psc`. A `.ba2` in dist that the build doesn't make stops it (exit 2, naming the file); `zip.files` takes no archives. |
| `sf-mod-build check [<sfmod.json>] [--game <folder>] [--checker <exe>] [--require-check]` | Runs `<checker> release <zip> --data <game>\Data`. No checker is a WARNING, or a failure with `--require-check`. |
| `sf-mod-build build [<sfmod.json>] [--game <folder>] [--checker <exe>] [--require-check]` | compile, plugin, pack, zip and check in order; stops at the first step that fails. |
| `sf-mod-build --version` | The version. |

Options for every command: `--verbose` (troubleshooting log on stderr), `--log <file>` (the same log with times, in
a file), `--help`.

The game's script sources are unpacked from `Tools\ContentResources.zip` once (only `Scripts\Source\*.psc` and the
flags file) into `%LOCALAPPDATA%\sf-mod-build\vanilla\<sha256 of the zip>\`, and reused until the Creation Kit
updates that zip. On Linux the folder is `$XDG_DATA_HOME/sf-mod-build` (or `~/.local/share/sf-mod-build`).

## The project file, sfmod.json

Paths are relative to the folder that holds `sfmod.json`. Comments (`//` and `/* */`) and trailing commas are
allowed.

```jsonc
{
  "plugin": "MyMod.esm",                       // the plugin file the builder writes into dist (required)
  "name": "MyMod",                             // the mod's name for the zip (default: the plugin name without .esm)
  "version": "0.1.0",                          // numbers and dots (required)
  "scripts": {                                 // leave out when the mod has no scripts
    "source": "Scripts/Source",                // one folder, or a list of folders (default Scripts/Source)
    "namespaces": ["MYM"],                     // every .psc in <source>/<namespace>/ ...
    "files": ["MYM/MyQuestScript"]             // ... or an explicit list (namespace/name, .psc optional)
  },
  "builder": {
    "project": "Builder",                      // a folder holding one .csproj, or the .csproj itself (required)
    "args": ["{data}", "{dist}", "{version}"]  // what the builder is given (required)
  },
  "dist": "dist",                              // the plugin and the BA2 go here (default dist)
  "stage": ".build/stage",                     // compiled scripts go here (default .build/stage)
  "xbox": false,                               // also make the "_xbox" archives in creations-out
  "creationsOut": "creations-out",             // where the Creations upload set goes (default creations-out)
  "assets": {                                  // leave out when the mod has no loose files
    "source": "Assets",                        // a folder laid out like Data: Meshes\, Materials\, Textures\ ...
    "xboxTextures": "Assets-xbox"              // optional: Xbox-format .dds files for the _xbox texture archive
  },
  "zip": {
    "name": "{name}-{version}.zip",            // the zip's name (default {name}-{version}.zip)
    "folder": ".",                             // where the zip is written (default: the sfmod.json folder)
    "files": { "README.md": "README-{name}.md" }   // extra files: file here -> name inside the zip (never .psc or .ba2)
  },
  "check": {
    "checker": "path/to/sf-mod-check.exe",     // default: sf-mod-check or sfmodcheck on PATH
    "required": false,                         // true: a missing checker fails the build
    "args": []                                 // extra options for the checker's release command
  }
}
```

Builder argument placeholders: `{data}` the game's Data folder, `{dist}`, `{scripts}` the (first) script source
folder, `{stage}`, `{version}`; all paths are full paths. Zip name placeholders: `{name}`, `{plugin}` (without the
extension), `{version}`.

`validate` refuses a plugin name that ends in an official plugin's name (such as `-ShatteredSpace.esm`) or starts
with `BlueprintShips-`, because the game removes such names from Plugins.txt and they stop loading. It warns
about a name that ends in an official Creation's name (such as `-SFBGS003.esm`): that ending hasn't been tested,
but it may be dropped the same way.

## Loose files: meshes, textures and sounds

A mod with art of its own keeps its loose files in one folder laid out like the game's `Data` folder, and names
that folder in `sfmod.json`:

```jsonc
"assets": { "source": "Assets" }
```

For example `Assets\Meshes\MyMod\Box.nif`, `Assets\Materials\MyMod\Box.mat` and
`Assets\Textures\MyMod\Box_color.dds`. Each file keeps that path inside the archive, so the game finds it where the
plugin and the materials point.

- Every `.dds` goes into `<plugin> - Textures.ba2`, a texture archive (Archive2's DDS format).
- Everything else (meshes, materials, particles, terrain `.btd` files, sounds, lip files) goes into
  `<plugin> - Main.ba2`, together with the compiled scripts.
- When Main holds a `.wem` sound or an `.ffxanim` lip file, it is packed uncompressed, because those play silent
  when they are stored compressed.
- Dot files (such as `.gitkeep`), `Thumbs.db`, `desktop.ini`, links and junctions are left out.
- `validate` refuses `.psc` source, plugins, archives and zips in the folder, and files at its top level, where the
  game never looks. It warns about artists' source formats that the game doesn't read, such as `.tif` and `.fbx`.

With `"xbox": true`, the Creations set gets its own `<plugin> - Main_xbox.ba2` and `<plugin> - Textures_xbox.ba2`.
The Xbox texture archive is always a separate archive. By default it holds the same PC textures, which is what
modders do today.

If you export Xbox versions of your textures (the Creation Kit's AssetWatcher does this), put them
in a second folder laid out the same way and name it with `"xboxTextures": "Assets-xbox"`. That folder must then
hold an Xbox copy of every PC texture: `validate` and `pack` refuse a folder with no `.dds` in it (when the mod has
PC textures), and a folder that lacks a PC texture, because the console never reads the PC archive and that texture
would be missing on Xbox. An Xbox texture with no PC copy is only a warning.

A PC `.wem` plays silent on Xbox and PlayStation 5, so `validate` warns about sounds when `xbox` is on.

`validate` shows what each archive will get (folder names shortened):

    > sf-mod-build validate
    project:  C:\Mods\AssetTest\sfmod.json
    mod:      AssetTest 0.1.0
    plugin:   AssetTest.esm (archives "AssetTest - Main.ba2" and "AssetTest - Textures.ba2")
    assets:   from C:\Mods\AssetTest\Assets: 2 general file(s), 1 texture(s)
    ...
    xbox:     yes, "AssetTest - Main_xbox.ba2" and "AssetTest - Textures_xbox.ba2" in C:\Mods\AssetTest\creations-out
    zip:      C:\Mods\AssetTest\AssetTest-0.1.0.zip
              holds AssetTest.esm, AssetTest - Main.ba2, AssetTest - Textures.ba2, README-AssetTest.md, LICENSE-AssetTest.txt
    ...
    RESULT: OK

`pack` copies each archive's files into `<stage>\pack\` first, so each archive has one root folder. A mod with
scripts and no loose files is packed exactly as before. Two loose files whose paths differ only in case (possible on
Linux) are a PROBLEM in `validate` and are refused by `pack`, because the game and Windows treat them as one file.
When the mod no longer has textures or scripts, `pack` deletes the old archive from dist (names match without
case), and with `"xbox": true` and nothing left to pack it also deletes the mod's own old archives from
creations-out.

Any other `.ba2` in dist that the build doesn't make stops `zip` (and `pack` with `"xbox": true`)
with a RESULT line naming it, so a build never passes while silently leaving out an archive. Without `"xbox": true`,
`pack` names each such archive in a WARNING line and goes on, so you hear about it before `zip`. The fix is to delete it
from dist: sf-mod-build ships only the archives it builds. `zip.files` takes no archives either; `validate`
reports a `.ba2` there as a PROBLEM. The mod's own archives go into the zip under their usual names, even when dist
has them spelled in other letter case.

### Limits

- Archives your builder writes itself aren't supported: put those files in the assets folder instead, so `pack`
  builds them into the mod's own archives.
- With `"xbox": true` and nothing left to pack, `pack` deletes only the mod's old archives from creations-out; the
  plugin and other files there stay as the last build left them, so creations-out isn't refreshed on that path.
- creations-out takes dist's files only (the plugin and the archives). Other `zip.files` entries, such as a README
  or LICENSE, go into the zip but not into creations-out, since a Creation takes the plugin and its archives.

## Setup checks in doctor

After the tools a build needs, `doctor` checks the settings from a usual modding setup. None of them stops a build,
so each is OK or a WARNING with the exact lines to add:

- **StarfieldCustom.ini** in `Documents\My Games\Starfield`: `bEnableLogging=1`, `bEnableTrace=1` and
  `bLoadDebugInformation=1` under `[Papyrus]` (the Papyrus log, with your `Debug.Trace` lines and line numbers),
  and `bInvalidateOlderFiles=1` under `[Archive]` (loose files win over archives while you test). A file saved as
  `StarfieldCustom.ini.txt` by mistake is called out.
- **CreationKitCustom.ini** in the game folder: `bUseCompiledDB=0` under `[Materials]`,
  `sScriptSourceFolder="Data/Scripts/Source"` under `[Papyrus]` and `bInvalidateOlderFiles=1` under `[Archive]`.
- **The unpacked script sources** in `Data\Scripts\Source`, which the Creation Kit needs. sf-mod-build itself
  unpacks its own copy, so it doesn't need them.

For loose files, `bInvalidateOlderFiles=1` under `[Archive]` in both files is the one line Bethesda asks for (the
official Creation Kit wiki's "Creation Kit Setup and Installation", "Initialization (.ini) Settings" and "Creating
Overlays and World Spaces" pages). `doctor` doesn't ask for `sResourceDataDirsFinal`: the official INI page says to
avoid it, and that it only works when `bInvalidateOlderFiles` is off. A comment after a value (`; ...` or `# ...`)
is ignored. A `;` or `#` inside double quotes is part of the value; a quote that is never closed doesn't count as one.

`doctor` only reads these files; it never writes an INI file. If Mod Organizer 2 keeps INI files per profile, give
the profile's folder with `--my-games <folder>`. On Linux the default is the game's Proton folder
(`steamapps/compatdata/1716740/pfx/drive_c/users/steamuser/Documents/My Games/Starfield`). When the game isn't
found there, `doctor` says so; give `--game <folder>` or `--my-games <folder>`.

A real run with a StarfieldCustom.ini that turns on only `bEnableLogging` (folder names shortened):

    > sf-mod-build doctor --my-games C:\Mods\mygames
    ...
    setup (not needed to build; recommended for testing and the Creation Kit):
    WARNING  StarfieldCustom.ini (Papyrus log, loose files): C:\Mods\mygames\StarfieldCustom.ini: doesn't set bEnableTrace=1, bLoadDebugInformation=1, bInvalidateOlderFiles=1
             what it is for: turns on the Papyrus log with your Debug.Trace lines and line numbers, and lets loose files win over archives while you test
             fix: add these lines (each under its [section]), then restart the program:
                 [Papyrus]
                 bEnableTrace=1
                 bLoadDebugInformation=1
                 [Archive]
                 bInvalidateOlderFiles=1
    ...
    RESULT: OK, ready to build (with 3 WARNINGS: see above)

## Game installs and mod managers

- **Steam**: found by itself (the Steam registry entry and `libraryfolders.vdf`, so other Steam library drives
  work). This is the only install the Creation Kit goes into, so it's the one sf-mod-build needs.
- **Game Pass / Xbox app**: the game is found (`<drive>\XboxGames\Starfield\Content`), but it has no Creation Kit
  tools, so `compile` and `pack` can't run against it. If you have both, sf-mod-build picks Steam and lists the other
  one as a note. A mod built against Steam's Data folder works on Game Pass too.
- **Another folder**: give `--game <folder>`.
- **Vortex and Mod Organizer 2**: the zip has the plugin and BA2 at its top level, which both install as is. The
  builder reads the game's real `Data` folder (`{data}`), not a mod manager's virtual one, so it sees only the base
  game and the official plugins there. sf-mod-build never writes into the game folder: it refuses a dist or
  creations-out folder inside the game's folder (every Starfield install it finds, not only the one it builds
  against). `pack` also refuses a dist folder that is a link or junction, and it never packs an assets folder
  that is in the folder of the game it builds against.

## Linux

The tool is plain .NET 9 and is meant to run on Linux too (the CI runs its tests on Ubuntu), where `validate`
and `zip` need nothing from Windows. The Creation Kit's compiler and Archive2 are Windows programs, so `compile` and
`pack` fail there with a "couldn't be run" message. Running them under Wine or Proton hasn't been tried.

## Reproducible builds

The same plugin builder and inputs give a byte-identical plugin, and `zip` gives the same zip bytes from the same
files on the same operating system and .NET version (a zip records the system that made it, and another .NET
version may compress a little differently).

The game's Papyrus compiler, though, writes the compile time into every `.pex` and doesn't always put its string
table in the same order, so a fresh compile gives a BA2 (and so a zip) of slightly different bytes with the same
meaning. To compare two builds, compare plugins by bytes and scripts by meaning (for example with `pex_compare`
from sf-pex-tools).

## The log

Add `--verbose` to see what the tool read, what it decided and what it ran (lines starting `log: ` on stderr), or
`--log <file>` to write the same lines, with times, to a file. Send that file when you report a problem. It never
writes over plugins, archives, zips, scripts or project files, so a mistyped `--log` can't destroy your work. If the
tool stops on an unexpected error, it says so in one `RESULT: CANNOT RUN` line and puts the full error in the log.

## Exit codes

| Code | Meaning |
|---|---|
| 0 | OK (a missing checker is only a WARNING, unless you require the check) |
| 1 | the build or the check failed (a script didn't compile, the builder failed, the checker found problems) |
| 2 | bad input or setup (a wrong option, a broken sfmod.json, a missing tool, `-release` asked for, an archive in dist that the build doesn't make) |

## Found a mistake?

Open an issue: https://github.com/taosecurity/sf-mod-build/issues

## Credits and license

sf-mod-build is made by Richard Bejtlich.

- The idea of one project file that drives a Papyrus build comes from **Pyro** by fireundubh
  (https://github.com/fireundubh/pyro), the build tool for Skyrim and Fallout 4 mods.
- Plugins are written by the mod's own builder with **Mutagen** (https://github.com/Mutagen-Modding/Mutagen) and,
  in the starter project, **BuildKit** (https://github.com/taosecurity/BuildKit).
- Starfield, the Creation Kit, its Papyrus compiler and Archive2 belong to Bethesda Softworks. sf-mod-build only runs
  the copies you installed.

The license is GPL-3.0 (see [LICENSE](LICENSE)), the same as Mutagen and BuildKit. Changes are in [CHANGELOG.md](CHANGELOG.md).
