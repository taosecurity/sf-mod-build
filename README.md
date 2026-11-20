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

`doctor` on a PC with the Steam game, the Creation Kit and the .NET SDK, but no checker yet:

    > sf-mod-build doctor
    FOUND    Starfield: C:\Program Files (x86)\Steam\steamapps\common\Starfield (Steam folder)
    FOUND    Papyrus compiler: C:\Program Files (x86)\Steam\steamapps\common\Starfield\Tools\Papyrus Compiler\PapyrusCompiler.exe
    FOUND    Archive2: C:\Program Files (x86)\Steam\steamapps\common\Starfield\Tools\Archive2\Archive2.exe
    FOUND    Script sources (ContentResources.zip): C:\Program Files (x86)\Steam\steamapps\common\Starfield\Tools\ContentResources.zip
    FOUND    .NET SDK: C:\Program Files\dotnet\dotnet.exe (SDK 9.0.321, 10.0.402, 10.0.403, 10.0.404)
    WARNING  Checker: not found
             what it is for: checks the finished zip (release check); without it the build still works, unchecked
             how to get it: Get sf-mod-check (called sfmodcheck before its rename) from https://github.com/taosecurity/sfmodcheck/releases. Put it on PATH, or give it with --checker <exe>.
    note: another Starfield install was found (Game Pass (XboxGames folder)): C:\XboxGames\Starfield\Content. To use it instead, give --game "C:\XboxGames\Starfield\Content"
    RESULT: OK, ready to build (with a WARNING: see above)

The link in that message uses the checker's old name; it still works, because GitHub forwards it to
[sf-mod-check](https://github.com/taosecurity/sf-mod-check/releases).

Then `build` on a starter mod called TestMod, with the checker given (folder names shortened to `C:\Mods\TestMod`).
The checker in this run was version 0.7.0, from before it was renamed, so it prints its old name:

    > sf-mod-build build --checker C:\Tools\sfmodcheck.exe
    == compile
    game script sources: ...\sf-mod-build\vanilla\b5f3f66d6041193c2db139c50a8a4a2da7a80c862a95ba5eec929c7a2c42983c\Scripts\Source
    compiled TMD\StarterQuestScript.psc
    RESULT: OK, 1 script(s) compiled into C:\Mods\TestMod\.build\stage\Scripts
    == plugin
    running the builder: TestMod.Builder.csproj (the first run restores and builds it, which can take a few minutes)
    built    C:\Mods\TestMod\dist\TestMod.esm (800 bytes)
    RESULT: OK, TestMod.esm built into C:\Mods\TestMod\dist
    == pack
    packed   C:\Mods\TestMod\dist\TestMod - Main.ba2 (686 bytes)
    RESULT: OK, C:\Mods\TestMod\dist\TestMod - Main.ba2 made
    == zip
    zipped   LICENSE-TestMod.txt
    zipped   README-TestMod.md
    zipped   TestMod - Main.ba2
    zipped   TestMod.esm
    sha256   02fe5a5ddfd6575002ca129f2676a7360c89213c538605741f07130da42412df
    RESULT: OK, C:\Mods\TestMod\TestMod-0.1.0.zip written (4 files, 2775 bytes)
    == check
    checking TestMod-0.1.0.zip with sfmodcheck.exe
    ...
             NOTE     [plugin-check] TestMod.esm: OK, 0 errors, 0 warnings (light master, 0 masters, 3 records, 0 overrides (0 ITM), 2 links)
             RESULT: OK (0 problems, 0 to review, 0 warnings)
    RESULT: OK, TestMod-0.1.0.zip passed the release check
    RESULT: OK, built C:\Mods\TestMod\TestMod-0.1.0.zip

## What it does

One `sf-mod-build build` runs five steps in order and stops at the first one that fails:

1. It compiles your Papyrus scripts with **your own** Creation Kit compiler (never with `-release`, so your
   `Debug.Trace` logging keeps working).
2. It runs your mod's Mutagen builder (for example one made with BuildKit) to write the plugin.
3. It packs the compiled scripts into `<Plugin> - Main.ba2` with **your own** Creation Kit Archive2 (and the Xbox
   archive for Creations, if you ask for it).
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

- **As a dotnet tool** (needs the .NET SDK, which you need anyway): download `sf-mod-build.0.1.0.nupkg` from the
  [release](https://github.com/taosecurity/sf-mod-build/releases) into a folder of its own, open a Command Prompt
  in that folder, and run:

  ```
  dotnet tool install --global sf-mod-build --add-source .
  ```

  Update later with `dotnet tool update --global sf-mod-build --add-source <that folder>`.
- **As a program**: download `sf-mod-build-0.1.0-win-x64.zip` from the release and check it against
  `SHA256SUMS` (`certutil -hashfile sf-mod-build-0.1.0-win-x64.zip SHA256` prints the hash). Right-click the zip,
  choose **Extract All...**, and put that folder on PATH (or call `sf-mod-build.exe` by its full path).
- **From source**: `dotnet build src\SfModBuild -c Release`; the program is
  `src\SfModBuild\bin\Release\net9.0\sf-mod-build.exe`.

Then install the starter template. Unzip `sf-mod-build-template-0.1.0.zip` from the release into a folder of its
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
- **Creations:** Bethesda's own mod service, which also reaches Xbox; a Creation needs an Xbox BA2 as well.

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
  `Taosecurity.Starfield.BuildKit` 0.4.0 and `Mutagen.Bethesda.Starfield` 0.54.4, both pinned exactly: 0.4.0 is the
  BuildKit version sf-mod-build 0.1.0 was tested with. (BuildKit 0.4.1 has come out since; it hasn't been tested
  with this template yet.)
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
| `sf-mod-build doctor [--game <folder>] [--checker <exe>]` | Finds the game, the Creation Kit's tools, .NET and the checker, and says how to get anything missing. |
| `sf-mod-build validate [<sfmod.json>]` | Checks the project file and prints what a build would use. Each problem names the field and the fix. |
| `sf-mod-build compile [<sfmod.json>] [--game <folder>]` | Compiles the scripts into `<stage>\Scripts` (stage is emptied first). Never `-release`: asking for it is refused with the reason. |
| `sf-mod-build plugin [<sfmod.json>] [--game <folder>]` | Runs the builder (`dotnet run -c Release --project <builder> -- <args>`) to write the plugin into dist (dist is emptied first). |
| `sf-mod-build pack [<sfmod.json>] [--game <folder>]` | Packs the compiled scripts into `<plugin> - Main.ba2` in dist; with `"xbox": true` it also fills creations-out with dist's files plus `<plugin> - Main_xbox.ba2`. |
| `sf-mod-build zip [<sfmod.json>]` | Writes the zip: dist's files plus `zip.files`, sorted, with fixed timestamps, so the same inputs give the same bytes. Never `.psc`. |
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
  "xbox": false,                               // also make "<plugin> - Main_xbox.ba2" in creations-out
  "creationsOut": "creations-out",             // where the Creations upload set goes (default creations-out)
  "zip": {
    "name": "{name}-{version}.zip",            // the zip's name (default {name}-{version}.zip)
    "folder": ".",                             // where the zip is written (default: the sfmod.json folder)
    "files": { "README.md": "README-{name}.md" }   // extra files: file here -> name inside the zip (never .psc)
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
  creations-out folder inside the game's folder.

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
| 2 | bad input or setup (a wrong option, a broken sfmod.json, a missing tool, `-release` asked for) |

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
