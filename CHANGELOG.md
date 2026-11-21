# Changelog

## 0.2.0 (unreleased)

### What's new

sf-mod-build now packs a mod's own art. Put your meshes, materials, textures and sounds in one folder laid out like the game's Data folder, name it in `sfmod.json` with `"assets": { "source": "Assets" }`, and `build` packs the textures into `<plugin> - Textures.ba2` and everything else into `<plugin> - Main.ba2` with your scripts. For Creations it makes separate `_xbox` archives of both, and it can take Xbox-format textures from a folder of their own.

`doctor` now also checks your setup: the Papyrus logging lines in StarfieldCustom.ini, the Creation Kit's CreationKitCustom.ini and the unpacked script sources. Each is only a warning, with the exact lines to add.

The starter template moves to BuildKit 0.4.2. Its plugin is byte for byte the same as with BuildKit 0.4.0.

### Example

A starter mod with a mesh, a material and a texture added in its `Assets` folder (folder names shortened):

    > sf-mod-build build --checker C:\Tools\sf-mod-check.exe
    ...
    == pack
    staged   2 general and 1 texture file(s) from C:\Mods\AssetTest\Assets
    packed   C:\Mods\AssetTest\dist\AssetTest - Main.ba2 (871 bytes)
    packed   C:\Mods\AssetTest\dist\AssetTest - Textures.ba2 (122 bytes)
    RESULT: OK, "AssetTest - Main.ba2" and "AssetTest - Textures.ba2" made in C:\Mods\AssetTest\dist
    ...
    RESULT: OK, AssetTest-0.1.0.zip passed the release check
    RESULT: OK, built C:\Mods\AssetTest\AssetTest-0.1.0.zip

`doctor` with a StarfieldCustom.ini that turns on only `bEnableLogging`:

    > sf-mod-build doctor --my-games C:\Mods\mygames
    ...
    WARNING  StarfieldCustom.ini (Papyrus log, loose files): C:\Mods\mygames\StarfieldCustom.ini: doesn't set bEnableTrace=1, bLoadDebugInformation=1, bInvalidateOlderFiles=1
    ...
             fix: add these lines (each under its [section]), then restart the program:
                 [Papyrus]
                 bEnableTrace=1
                 bLoadDebugInformation=1
                 [Archive]
                 bInvalidateOlderFiles=1
    ...
    RESULT: OK, ready to build (with 3 WARNINGS: see above)

### Changes

- New `assets` section in `sfmod.json`: `source` (a folder laid out like Data) and an optional `xboxTextures` folder of Xbox-format `.dds` files.
- `pack` makes `<plugin> - Textures.ba2` (Archive2's DDS format) from every `.dds`, and puts the other loose files in `<plugin> - Main.ba2` with the compiled scripts. Each archive's files are copied into `<stage>\pack\` first, so each has one root folder.
- A Main archive that holds a `.wem` or `.ffxanim` is packed with `-compression=None`, because compressed sound and lip files play silent. sf-mod-check's voice-compressed check confirms it.
- With `"xbox": true`, `pack` makes `<plugin> - Main_xbox.ba2` and a separate `<plugin> - Textures_xbox.ba2` in creations-out. The Xbox texture archive takes `assets.xboxTextures` when it is given, else the PC textures (with a note that only a console test proves they look right).
- `validate` lists each archive's files, refuses `.psc` source, plugins, archives and zips in the assets folder, files at its top level, and an assets folder that is or holds the mod, dist, stage, creations-out, the builder or a script source. It warns about source art (`.tif`, `.fbx` and so on), sounds with `xbox` on (a PC `.wem` is silent on consoles), and an Xbox texture with no PC copy. With `xbox` on it refuses an `xboxTextures` folder that holds no `.dds`, and one that lacks an Xbox copy of a PC texture (that texture would be missing on Xbox, since the console never reads the PC archive).
- `pack` refuses an assets folder in the game's folder, a loose copy of a compiled script, and two loose files whose paths differ only in case. It deletes an archive the build doesn't make any more (the mod lost its textures or its scripts), even when there is nothing left to pack. `zip` needs the texture archive when the mod has textures, and leaves out any other `.ba2` in dist with a WARNING naming it; so does the Creations set. A mod with loose files but no scripts gets only the archives it needs.
- A mod with scripts and no loose files is packed with exactly the same Archive2 command line as before.
- `doctor` has a new setup section and a `--my-games <folder>` option (the folder that holds StarfieldCustom.ini, such as a Mod Organizer 2 profile). On Linux it looks in the game's Proton folder, and when no game is found it says so and names `--game` and `--my-games`. For loose files it asks only for `bInvalidateOlderFiles=1` under `[Archive]`, the line Bethesda's Creation Kit pages ask for; it doesn't ask for `sResourceDataDirsFinal`, which Bethesda's INI page says to avoid. A comment after a value (`; ...` or `# ...`) is ignored. It only reads the files.
- The `dotnet new sfmod` template pins BuildKit 0.4.2 (was 0.4.0), has a commented-out `"assets"` line in `sfmod.json`, and its README says how to add art.

### Download

Download `sf-mod-build.0.2.0.nupkg` (dotnet tool) or `sf-mod-build-0.2.0-win-x64.zip` (program), plus `sf-mod-build-template-0.2.0.zip` for the starter template, and check them against `SHA256SUMS`. The README's Install section has the steps.

## 0.1.1 (2026-11-20)

### What's new

This is a small fix release. The checker that sf-mod-build runs on your zip was renamed from sfmodcheck to sf-mod-check, and sf-mod-build's own messages now use the new name and link. An old `sfmodcheck.exe` is still found and run, so nothing you set up before stops working.

### Example

When the checker isn't installed, `doctor` now points at the renamed project:

    > sf-mod-build doctor
    ...
    WARNING  Checker: not found
             what it is for: checks the finished zip (release check); without it the build still works, unchecked
             how to get it: Get sf-mod-check (called sfmodcheck before its rename) from https://github.com/taosecurity/sf-mod-check/releases. Put it on PATH, or give it with --checker <exe>.
    ...
    RESULT: OK, ready to build (with a WARNING: see above)

### Fixes

- `doctor` and `check` linked the checker's old GitHub page (taosecurity/sfmodcheck). They now link https://github.com/taosecurity/sf-mod-check/releases.
- The example in error messages about `--checker` now shows `sf-mod-check.exe` instead of `sfmodcheck.exe`.
- The README's Example shows a build checked by sf-mod-check 0.9.0, with its current output.
### Notes

- The `dotnet new sfmod` template is unchanged. It still pins BuildKit 0.4.0 and Mutagen 0.54.4, the versions this release was tested with.

### Download

Download `sf-mod-build.0.1.1.nupkg` (dotnet tool) or `sf-mod-build-0.1.1-win-x64.zip` (program), plus `sf-mod-build-template-0.1.1.zip` for the starter template, and check them against `SHA256SUMS`. The README's Install section has the steps.

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
