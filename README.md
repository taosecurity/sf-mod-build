# sf-mod-build

sf-mod-build builds a Starfield mod from one project file. You describe your mod once in `sfmod.json`, then one
command does the whole build:

1. compiles your Papyrus scripts with **your own** Creation Kit compiler (never with `-release`, so your
   `Debug.Trace` logging keeps working),
2. runs your mod's Mutagen builder (for example one made with BuildKit) to write the plugin,
3. packs the compiled scripts into `<Plugin> - Main.ba2` with **your own** Creation Kit Archive2 (and the Xbox
   archive for Creations, if you ask for it),
4. writes a zip that Vortex and Mod Organizer 2 install as is, with the same bytes every time from the same inputs,
5. runs the `release` check of sf-mod-check (sfmodcheck) on that zip, when you have it.

It also comes with a starter project (`dotnet new sfmod`), so a new mod builds on the first try.

sf-mod-build ships no game files. It uses the Papyrus compiler, Archive2 and script sources that the Creation Kit
installs into your own copy of the game.

For AI assistants: read [AGENTS.md](AGENTS.md) for step-by-step instructions.

## First command

With the tool installed (see below), check that it can find everything it needs:

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

## What you need

- Windows 10 or 11 (the Creation Kit's compiler and Archive2 are Windows programs).
- Starfield on **Steam**, with the **Starfield Creation Kit** installed from Steam (Library > Tools). The Creation
  Kit puts `Tools\Papyrus Compiler\PapyrusCompiler.exe`, `Tools\Archive2\Archive2.exe` and
  `Tools\ContentResources.zip` (the game's script sources) into the game folder.
- The **.NET 9 SDK** (or later): https://dotnet.microsoft.com/download. Your mod's builder is built with it.
- Optional: **sf-mod-check** 0.7.0 or later (called sfmodcheck before its rename; 0.7.0 added the `release`
  command that sf-mod-build runs) from
  https://github.com/taosecurity/sfmodcheck/releases, on PATH or given with `--checker`. Without it the build still
  works, but the zip isn't checked.

## Install

Pick one.

- **As a dotnet tool** (needs the .NET SDK, which you need anyway): download `sf-mod-build.<version>.nupkg` from
  the GitHub release into a folder, then

  ```
  dotnet tool install --global sf-mod-build --add-source <that folder>
  ```

  Update later with `dotnet tool update --global sf-mod-build --add-source <that folder>`.
- **As a program**: download `sf-mod-build-<version>-win-x64.zip` from the release, check it against
  `SHA256SUMS`, unzip it anywhere and put that folder on PATH (or call `sf-mod-build.exe` by its full path).
- **From source**: `dotnet build src\SfModBuild -c Release`; the program is
  `src\SfModBuild\bin\Release\net9.0\sf-mod-build.exe`.

Then install the starter template. From a clone of this repo (or the unzipped `sf-mod-build-template-<version>.zip`
from the release):

```
dotnet new install templates\sfmod
```

`dotnet new uninstall <the same full path>` removes it again.

## The starter project

```
dotnet new sfmod -n MyMod --prefix MYM --mod-author "Your Name"
```

- `-n` is the mod's name (also the plugin `MyMod.esm` and the folder).
- `--prefix` is the short prefix for the mod's EditorIDs and its Papyrus namespace (default `MYM`).
- `--mod-author` goes into the plugin header and the LICENSE (default "Your Name").

You get `sfmod.json`, a `Builder` project (on the nuget packages `Taosecurity.Starfield.BuildKit` 0.4.0 and
`Mutagen.Bethesda.Starfield` 0.54.4, both pinned exactly) that makes a small light master with one quest, one
starter script `Scripts\Source\MYM\StarterQuestScript.psc` that writes `[MyMod]` lines to the Papyrus log, a "Show
Debug Messages (PC only)" switch in the mod's own Gameplay Options group, a README that says how to turn on Papyrus
logging, and a LICENSE to fill in. It edits no vanilla records.

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
version may compress a little differently). The game's Papyrus compiler, though, writes the compile time into every `.pex` and doesn't always put its
string table in the same order, so a fresh compile gives a BA2 (and so a zip) of slightly different bytes with the
same meaning. To compare two builds, compare plugins by bytes and scripts by meaning (for example with
`pex_compare` from sf-pex-tools).

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

## Credits

- The idea of one project file that drives a Papyrus build comes from **Pyro** by fireundubh
  (https://github.com/fireundubh/pyro), the build tool for Skyrim and Fallout 4 mods.
- Plugins are written by the mod's own builder with **Mutagen** (https://github.com/Mutagen-Modding/Mutagen) and,
  in the starter project, **BuildKit** (https://github.com/taosecurity/BuildKit).
- Starfield, the Creation Kit, its Papyrus compiler and Archive2 belong to Bethesda Softworks. sf-mod-build only runs
  the copies you installed.

## License

GPL-3.0 (see [LICENSE](LICENSE)), the same as Mutagen and BuildKit. Changes are in [CHANGELOG.md](CHANGELOG.md).
