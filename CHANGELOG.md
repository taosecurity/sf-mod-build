# Changelog

## 0.1.0 (2026-11-19)

### What's new

This is the first version. sf-mod-build builds a Starfield mod from one project file, `sfmod.json`: one `sf-mod-build build` compiles the mod's Papyrus scripts with your own Creation Kit compiler, runs the mod's Mutagen builder to write the plugin, packs the scripts into a BA2 with the Creation Kit's Archive2, writes a zip that Vortex and Mod Organizer 2 install as is, and checks it with sf-mod-check when you have it. A starter project (`dotnet new sfmod`) builds on the first try.

### Example

`doctor` checks that everything the build needs is there:

    > sf-mod-build doctor
    FOUND    Starfield: C:\Program Files (x86)\Steam\steamapps\common\Starfield (Steam folder)
    FOUND    Papyrus compiler: C:\Program Files (x86)\Steam\steamapps\common\Starfield\Tools\Papyrus Compiler\PapyrusCompiler.exe
    FOUND    Archive2: C:\Program Files (x86)\Steam\steamapps\common\Starfield\Tools\Archive2\Archive2.exe
    FOUND    Script sources (ContentResources.zip): C:\Program Files (x86)\Steam\steamapps\common\Starfield\Tools\ContentResources.zip
    FOUND    .NET SDK: C:\Program Files\dotnet\dotnet.exe (SDK 9.0.321, 10.0.402, 10.0.403, 10.0.404)
    WARNING  Checker: not found
    ...
    RESULT: OK, ready to build (with a WARNING: see above)

Then a new mod builds in three commands, and the last line names the zip (folder name shortened):

    > dotnet new sfmod -n MyMod --prefix MYM
    > cd MyMod
    > sf-mod-build build
    ...
    RESULT: OK, built C:\Mods\MyMod\MyMod-0.1.0.zip

### How it works

- One project file per mod, `sfmod.json`: plugin, version, script folders and namespaces, the Mutagen builder and its arguments (`{data} {dist} {scripts} {stage} {version}`), dist, stage, the Xbox archive for Creations, the zip's name, folder and extra files, and the checker. Comments and trailing commas are allowed; every problem names the field and the fix.
- Commands: `doctor`, `validate`, `compile`, `plugin`, `pack`, `zip`, `check` and `build` (all five build steps in order), each with `--help`, `--verbose` and `--log <file>`. Exit codes are 0 OK, 1 failed, 2 bad input or setup.
- `compile` runs the Creation Kit's PapyrusCompiler once per script with the game's script sources, unpacked once from `Tools\ContentResources.zip` into a cache. It never compiles with `-release` and refuses it with the reason.
- `pack` makes `<plugin> - Main.ba2` with the Creation Kit's Archive2, and with `"xbox": true` the Creations upload set with `<plugin> - Main_xbox.ba2`.
- `zip` writes the same bytes from the same files (sorted entries, fixed timestamps), never with `.psc` files.
- `check` runs the `release` command of sf-mod-check 0.7.0 or later (called sfmodcheck before it was renamed) on the zip; without a checker it's a WARNING, or a failure with `--require-check`.
- Safety: the folders the tool empties (stage, dist, creations-out) are refused when they are or hold the mod, its sources or its builder, a drive root, or anything in the game's folder.
- The `dotnet new sfmod` template: a builder on BuildKit 0.4.0 and Mutagen 0.54.4 (pinned exactly) that makes a light master with one quest, a starter script with `Debug.Trace` logging, a "Show Debug Messages (PC only)" switch in the mod's own Gameplay Options group, and a README that says how to turn on Papyrus logging. Options `--prefix` and `--mod-author`.
- Tested by rebuilding three existing mods; each plugin came out byte-identical to the released one (or the one difference was explained), with the scripts the same by meaning.

### Download

- `sf-mod-build.0.1.0.nupkg`: install it as a dotnet tool with `dotnet tool install --global sf-mod-build --add-source <folder holding the .nupkg>`.
- `sf-mod-build-0.1.0-win-x64.zip`: the program on its own; unzip it and put the folder on PATH.
- `sf-mod-build-template-0.1.0.zip`: the starter project; unzip it into a folder of its own and run `dotnet new install "<that folder>"`.
- Check the downloads against `SHA256SUMS`. The [README](https://github.com/taosecurity/sf-mod-build#install) has the steps.
