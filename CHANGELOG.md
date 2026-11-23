# Changelog

## 0.4.0 (not released yet)

### What's new

- Two more kinds of builder in sfmod.json. Give exactly one of `builder.project` (a .NET project, as before), `builder.command` or `builder.prebuilt`; `validate` reports none or two as a PROBLEM.
- `builder.command` runs any program that writes the plugin: a list with the program first and its first arguments after it, such as `["python", "tools/build.py"]` or `["dotnet", "run", "build.cs", "--"]`. `builder.args` are added after it, and the builder placeholders work in both. A program with a `/` or `\` is a path relative to sfmod.json; a bare name is found on PATH. It runs in the sfmod.json folder without a shell, and doesn't need .NET.
- `builder.prebuilt` copies a ready plugin (made in the Creation Kit or by hand) into dist under the `plugin` name. It takes no `args`, and `plugin` needs neither the game nor .NET for it.
- `validate` now prints a `runs:` line with the command a build would run. Read it before you build a mod someone else made: `builder.command` runs whatever program it names.
- `steps.afterPlugin` and `steps.afterPack` in sfmod.json: lists of your own commands, each written like `builder.command`, run in order at the end of `plugin` and at the end of `pack`. Running a step on its own runs its commands too, so `build` and one step at a time give the same result. They take the builder placeholders plus `{name}` and `{plugin}`. A missing program stops the step before any work (exit 2); a command that exits non-zero stops it (exit 1) with a RESULT line naming the command. `validate` prints one `runs:` line per command.
- A top-level `archives` list in sfmod.json: more archives that `pack` makes in dist, each named `<plugin> - <suffix>.ba2`. An entry either packs a `source` folder laid out like Data (with `"compression": "none"` for voice files, which play silent when stored compressed, and `"format": "dds"` for textures) or copies a ready archive from `file` as it is. These archives ship in the zip, `pack` deletes their old copies, and they never count as stray. The suffix may be `Main` or `Textures` only when `pack` doesn't make that archive itself. `pack` checks a source folder's files (a `steps.afterPlugin` command may fill it) and stops with exit 2 before Archive2 runs when it is missing, empty or holds a file that doesn't belong (a dot file, `Thumbs.db` and `desktop.ini` included, since Archive2 packs the folder as it is). `validate` refuses a source that is, holds or is inside `assets.source`, `assets.xboxTextures` or another entry's source, so no file ships twice. `validate` prints an `archive:` line for each entry. In this version the list can't be used with `"xbox": true`.
- `parts` in sfmod.json: the other plugins your builder writes into dist, each `{ "plugin": "MyMod-Extra.esm" }` with optional `scripts` of its own (the same shape as the top-level `scripts`; `source` defaults to the top-level source folders). `compile` compiles a part's scripts into `<stage>/parts/<part>/Scripts`, `pack` packs them into `<part> - Main.ba2`, `plugin` stops (exit 1) when a part plugin isn't in dist, and `zip` ships every plugin and archive and stops (exit 2) when one is missing. `validate` prints a `part:` line for each and refuses a part named like the main plugin or another part, a name that breaks the plugin-name rules, a script in two places, and `parts` with `builder.prebuilt`. In this version a part with scripts can't be used with `"xbox": true`.
- `check` and `build` take `--masters-dir <folder>`, which can be given more than once, and pass each folder to sf-mod-check's `release`. Use it for a mod whose master plugin isn't in the Data folder, such as another mod's folder in your mod manager. A folder that isn't there stops `check`, or `build` before it starts, with exit 2.
- `scripts.optimize` in sfmod.json (default `true`): set it to `false` to compile without the compiler's `-optimize` option, for a mod whose scripts were tested in game that way. A part's `scripts` follow the top-level value unless they set their own. `-release` stays refused either way.
- The release now has a `<file>.sha256` beside each download, as well as `SHA256SUMS`, so a tool can check one download on its own.

### Example

A made-up mod whose builder is a Python script, with `"builder": { "command": ["python", "tools/build.py", "--out", "{dist}"], "args": ["{version}"] }` (folder names are shortened):

    > sf-mod-build validate
    ...
    builder:  command python
              args {version}
    runs:     python tools/build.py --out {dist} {version}
    ...
    RESULT: OK

    > sf-mod-build plugin
    running the builder: python.exe (builder.command)
    built    C:\Mods\MyMod\dist\MyMod.esm (20 bytes)
    RESULT: OK, MyMod.esm built into C:\Mods\MyMod\dist

and a mod with `"builder": { "prebuilt": "Original/MyOtherMod.esm" }`:

    > sf-mod-build plugin
    copied   C:\Mods\MyOtherMod\Original\MyOtherMod.esm to C:\Mods\MyOtherMod\dist\MyOtherMod.esm (21 bytes)
    RESULT: OK, MyOtherMod.esm copied into C:\Mods\MyOtherMod\dist

and a made-up mod with one step after pack that fails, `"steps": { "afterPack": [["python", "tools/after.py", "{dist}/missing.txt", "fail"]] }`:

    > sf-mod-build pack
    running  steps.afterPack[0]: python.exe tools/after.py C:\Mods\StepsMod\dist/missing.txt fail
    FAILED   steps.afterPack[0] (python.exe) exited with code 3
             after.py: C:\Mods\StepsMod\dist/missing.txt is missing
    RESULT: FAILED at steps.afterPack[0] (python.exe exited with code 3). Fix: fix the error shown above (the full output is in the log: --log <file>), or the command in C:\Mods\StepsMod\sfmod.json, and run again.

and a made-up mod with a voice folder and a ready archive, `"archives": [ { "suffix": "Voices", "source": "Voices", "compression": "none" }, { "suffix": "Extra", "file": "Original/Other - Extra.ba2" } ]`:

    > sf-mod-build validate
    ...
    archive:  "VoiceMod - Voices.ba2" packed from C:\Mods\VoiceMod\Voices, uncompressed
    archive:  "VoiceMod - Extra.ba2" copied from C:\Mods\VoiceMod\Original\Other - Extra.ba2
    ...
              holds VoiceMod.esm, VoiceMod - Voices.ba2, VoiceMod - Extra.ba2
    RESULT: OK

    > sf-mod-build pack
    packed   C:\Mods\VoiceMod\dist\VoiceMod - Voices.ba2 (127 bytes)
    copied   C:\Mods\VoiceMod\dist\VoiceMod - Extra.ba2 from C:\Mods\VoiceMod\Original\Other - Extra.ba2 (23 bytes)
    RESULT: OK, "VoiceMod - Voices.ba2" and "VoiceMod - Extra.ba2" made in C:\Mods\VoiceMod\dist

and a made-up mod whose Python builder writes three plugins, with `"parts": [ { "plugin": "PartsMod-Extra.esm", "scripts": { "namespaces": ["PTMX"] } }, { "plugin": "PartsMod-Patch.esm" } ]`:

    > sf-mod-build build
    == compile
    compiled PTM\PTM_Main.psc into C:\Mods\PartsMod\.build\stage\Scripts
    compiled PTMX\PTMX_Extra.psc into C:\Mods\PartsMod\.build\stage\parts\PartsMod-Extra\Scripts
    ...
    == plugin
    running the builder: python.exe (builder.command)
    built    C:\Mods\PartsMod\dist\PartsMod.esm (16 bytes)
    built    C:\Mods\PartsMod\dist\PartsMod-Extra.esm (16 bytes)
    built    C:\Mods\PartsMod\dist\PartsMod-Patch.esm (16 bytes)
    RESULT: OK, PartsMod.esm and 2 part plugin(s) built into C:\Mods\PartsMod\dist
    == pack
    packed   C:\Mods\PartsMod\dist\PartsMod - Main.ba2 (331 bytes)
    packed   C:\Mods\PartsMod\dist\PartsMod-Extra - Main.ba2 (336 bytes)
    RESULT: OK, "PartsMod - Main.ba2" and "PartsMod-Extra - Main.ba2" made in C:\Mods\PartsMod\dist
    == zip
    zipped   PartsMod - Main.ba2
    zipped   PartsMod-Extra - Main.ba2
    zipped   PartsMod-Extra.esm
    zipped   PartsMod-Patch.esm
    zipped   PartsMod.esm
    ...

and a made-up mod whose dist holds a folder of old copies:

    > sf-mod-build plugin
    RESULT: CANNOT BUILD THE PLUGIN (the dist folder C:\Mods\MyMod\dist holds "old" (a folder), which no earlier build listed as its own, and sf-mod-build deletes only the plugins and archives in dist and what earlier builds made there, so nothing was deleted. Fix: move it out of dist and run again (a build by sf-mod-build 0.3.0 or earlier left no list of what it made, so its builder's other files are named here once), or set "dist" in C:\Mods\MyMod\sfmod.json to a folder of its own).

### Fixed

- `plugin` no longer deletes the folders and other files you keep in dist (plugins and archives there are still deleted). Before, it emptied dist on every build, so a folder you kept there (old copies, notes) was deleted without a word. Now it deletes only what earlier builds made there (the names in dist's new `.sf-mod-build-made` list, plugins and `.ba2` archives) and keeps zips and a `zip.folder` inside dist. When dist holds anything else, it deletes nothing and stops with exit 2, naming each one and the fix. The first build after an update from 0.3.0 or earlier may name your builder's own extra files this way once, because those versions kept no list; delete them and run again. A dist folder that is a link or junction is refused too. When a delete fails (a plugin open in the Creation Kit, say), the list stays for the next run. 0.3.0 and earlier have this problem.
- A build no longer fails the second time when a loose file it copies is read-only: the copies `pack` makes in stage and creations-out (and, new in 0.4.0, the prebuilt plugin and ready archives `plugin` and `pack` copy into dist) are no longer read-only, so the next build can delete them. 0.3.0 and earlier have this problem for read-only loose files.
- AGENTS.md no longer says that archives your builder writes aren't supported: they have shipped through `zip.archives` since 0.3.0.
- When a `zip.files` entry isn't there, `validate`'s fix now says that a file the builder or a step makes goes in dist instead, because `zip.files` takes only files that are there before the build.

## 0.3.0 (2026-11-23)

### What's new

- `zip.archives` in sfmod.json: a list of archive file names that your own builder writes into dist. `zip` ships them, `pack` and the stray-archive check leave them alone, and `validate` lists them under "holds". A listed archive that isn't in dist stops `zip` (exit 2) and names the file and the fix. Any `.ba2` in dist that is neither made by `pack` nor listed is still refused. A listed name can't be one of the four names `pack` uses (`<plugin> - Main.ba2`, `<plugin> - Textures.ba2` and their `_xbox` versions), can't hold a folder, and can't repeat. `zip.archives` can't be used with `"xbox": true` (`validate` reports it as a PROBLEM), because `pack` can't make the Xbox copies of archives your builder writes.
- The message for an archive in dist that the build doesn't make now gives both fixes: delete it, or list it in `zip.archives` when your builder writes it on purpose. `validate`'s PROBLEM for a `.ba2` in `zip.files` now points to `zip.archives` too.

### Example

This is `zip` on a made-up mod whose builder writes `MyMod - Extra.ba2` into dist, with `"zip": { "archives": ["MyMod - Extra.ba2"] }` in sfmod.json (folder names are shortened):

    > sf-mod-build zip sfmod.json
    zipped   MyMod - Extra.ba2
    zipped   MyMod.esm
    sha256   cc997ed91f61bb8e2b12de86a292fd45fc3076f6bd0e5ebfa45ea34de79f1b62
    RESULT: OK, C:\Mods\MyMod\MyMod-0.1.0.zip written (2 files, 237 bytes)

and the same mod without the `archives` line:

    RESULT: CANNOT ZIP (C:\Mods\MyMod\dist\MyMod - Extra.ba2 is an archive in dist that this build doesn't make (the mod has no archives), so the zip can't take it. Fix: delete it from dist, or, if your builder writes it on purpose, list its name in zip.archives in sfmod.json; sf-mod-build ships only the archives it builds and the ones listed there).

## 0.2.1 (2026-11-22)

### Changed

- `pack` without `xbox` now prints a `WARNING` line for each `.ba2` in dist that the build doesn't make, so you hear about it before `zip` stops on it. The exit code is unchanged (0).
- The README's limits now say that creations-out isn't refreshed when there is nothing to pack, and that `zip.files` entries such as a README or LICENSE go into the zip but not into creations-out.

### Fixed

- The release's `SHA256SUMS` file now has LF line endings. From 0.1.1 to 0.2.0 it had Windows line endings, so `sha256sum -c SHA256SUMS` on Linux or in Git Bash reported every file as missing. The release workflow now runs `sha256sum -c SHA256SUMS` itself before it makes the release.
- `doctor` reads INI comments more carefully. A `;` or `#` inside double quotes is part of the value, and a quote that is never closed doesn't count as one. Before, a closed quote followed by an unclosed one (`"a;b" "c`) cut the value at the `;` inside the closed quote.
- `pack`, `plugin` and `compile` now refuse a creations-out, dist or stage folder that is, holds or is inside any Starfield install they find, not only the one they build against. Before, a mod kept inside a second install (Game Pass while `--game` named Steam, for example) could have that install's `Data` folder emptied. `pack` now checks dist too: before, it deleted old archives there and wrote new ones without checking it against any install.
- `pack` with nothing to pack now also refuses a dist folder (and, with `xbox` on, a creations-out folder) that is, holds or is inside the game's folder before it deletes the mod's old archives there, as a full `pack` does. It checks every Starfield install it finds, plus the `--game` folder; a `--game` folder that isn't a game gets a `WARNING`, and the installs found without it are still checked. When no game is found, this path still works without one.
- `pack` now refuses a dist folder that is itself a link or junction, with a full pack and with nothing to pack. It deletes and writes archives in dist, so through a link they would land wherever the link points, a game's `Data` folder included. A link further up the path was already refused. With nothing to pack, a creations-out folder that is a link or junction is refused the same way, before it is read.
- A `dist`, `stage` or `creationsOut` folder written with a trailing slash (`"dist/"`) is now the same path as without it to every check. On Linux a link spelled that way could be missed, and a recursive delete would follow it.
- The release workflow stops with a clear message when it has no files to checksum, instead of failing later in `sha256sum -c`.

### Example

This is `pack` on a made-up mod with one mesh, after an old archive was copied into dist by hand (folder names are shortened):

    > sf-mod-build pack sfmod.json
    WARNING  C:\Mods\MyMod\dist\Old - Main.ba2 is an archive in dist that this build doesn't make (the mod's archives are "MyMod - Main.ba2"), so zip will stop on it. Fix: delete it from dist; sf-mod-build ships only the archives it builds
    staged   1 general and 0 texture file(s) from C:\Mods\MyMod\Assets
    packed   C:\Mods\MyMod\dist\MyMod - Main.ba2 (103 bytes)
    RESULT: OK, C:\Mods\MyMod\dist\MyMod - Main.ba2 made

This is the new check of the downloads, in Git Bash in the folder holding the release's files:

    $ sha256sum -c SHA256SUMS
    sf-mod-build-0.2.1-win-x64.zip: OK
    sf-mod-build-template-0.2.1.zip: OK
    sf-mod-build.0.2.1.nupkg: OK

## 0.2.0 (2026-11-20)

### What's new

sf-mod-build now packs a mod's own art. Put your meshes, materials, textures and sounds in one folder laid out like the game's Data folder, name it in `sfmod.json` with `"assets": { "source": "Assets" }`, and `build` packs the textures into `<plugin> - Textures.ba2` and everything else into `<plugin> - Main.ba2` with your scripts. For Creations it makes separate `_xbox` archives of both, and it can take Xbox-format textures from a folder of their own.

`doctor` now also checks your setup: the Papyrus logging lines in StarfieldCustom.ini, the Creation Kit's CreationKitCustom.ini and the unpacked script sources. Each is only a warning, with the exact lines to add.

The starter template moves to BuildKit 0.4.2. Its plugin is byte for byte the same as with BuildKit 0.4.0.

### Example

This is a starter mod with a mesh, a material and a texture added in its `Assets` folder (folder names are shortened):

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

This is `doctor` with a StarfieldCustom.ini that turns on only `bEnableLogging`:

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

- A new `assets` section in `sfmod.json` holds `source` (a folder laid out like Data) and an optional `xboxTextures` folder of Xbox-format `.dds` files.
- `pack` makes `<plugin> - Textures.ba2` (Archive2's DDS format) from every `.dds`, and puts the other loose files in `<plugin> - Main.ba2` with the compiled scripts. Each archive's files are copied into `<stage>\pack\` first, so each has one root folder.
- A Main archive that holds a `.wem` or `.ffxanim` is packed with `-compression=None`, because compressed sound and lip files play silent. sf-mod-check's voice-compressed check confirms it.
- With `"xbox": true`, `pack` makes `<plugin> - Main_xbox.ba2` and a separate `<plugin> - Textures_xbox.ba2` in creations-out. The Xbox texture archive takes `assets.xboxTextures` when it is given, else the PC textures (with a note that only a console test proves they look right).
- `validate` lists each archive's files, refuses `.psc` source, plugins, archives and zips in the assets folder, files at its top level, and an assets folder that is or holds the mod, dist, stage, creations-out, the builder or a script source. It warns about source art (`.tif`, `.fbx` and so on), sounds with `xbox` on (a PC `.wem` is silent on consoles), and an Xbox texture with no PC copy. It also refuses two loose files whose paths differ only in case. With `xbox` on it refuses an `xboxTextures` folder that holds no `.dds` when the mod has PC textures, and one that lacks an Xbox copy of a PC texture (that texture would be missing on Xbox, since the console never reads the PC archive).
- `pack` refuses an assets folder in the game's folder, a loose copy of a compiled script, and two loose files whose paths differ only in case. It deletes an archive the build doesn't make any more (the mod lost its textures or its scripts), even when there is nothing left to pack, and then, with `xbox` on, the mod's own old archives in creations-out too. `zip` needs the texture archive when the mod has textures, and stops (exit 2) at any other `.ba2` in dist, naming it, with the fix to delete it (0.2.0 ships only the archives it builds); so does `pack` with `xbox` on, before it makes the Creations set. `validate` reports a `.ba2` in `zip.files` as a PROBLEM; other `zip.files` entries work as before. Archive names in dist match without case, and `zip` puts the mod's own archives in under their usual names however dist spells them. A mod with loose files but no scripts gets only the archives it needs.
- A mod with scripts and no loose files is packed with exactly the same Archive2 command line as before.
- `doctor` has a new setup section and a `--my-games <folder>` option (the folder that holds StarfieldCustom.ini, such as a Mod Organizer 2 profile). On Linux it looks in the game's Proton folder, and when no game is found it says so and names `--game` and `--my-games`. For loose files it asks only for `bInvalidateOlderFiles=1` under `[Archive]`, the line Bethesda's Creation Kit pages ask for; it doesn't ask for `sResourceDataDirsFinal`, which Bethesda's INI page says to avoid. A comment after a value (`; ...` or `# ...`) is ignored. It only reads the files.
- The `dotnet new sfmod` template pins BuildKit 0.4.2 (was 0.4.0), has a commented-out `"assets"` line in `sfmod.json`, and its README says how to add art.

### Upgrading from 0.1.1

- 0.2.0 ships only the archives it builds. In 0.1.1, `zip` also shipped any other `.ba2` your builder wrote into dist, and `zip.files` could list an extra archive. Both now stop the build with exit 2 and say which file to remove. If your mod ships an archive sf-mod-build doesn't make, stay on 0.1.1 for now.

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

- Each mod has one project file, `sfmod.json`, which holds the plugin, version, script folders and namespaces, the Mutagen builder and its arguments (`{data} {dist} {scripts} {stage} {version}`), dist, stage, the Xbox archive for Creations, the zip's name, folder and extra files, and the checker. Comments and trailing commas are allowed; every problem names the field and the fix.
- The commands are `doctor`, `validate`, `compile`, `plugin`, `pack`, `zip`, `check` and `build` (all five build steps in order), each with `--help`, `--verbose` and `--log <file>`. Exit codes are 0 OK, 1 failed, 2 bad input or setup.
- `compile` runs the Creation Kit's PapyrusCompiler once per script with the game's script sources, unpacked once from `Tools\ContentResources.zip` into a cache. It never compiles with `-release` and refuses it with the reason.
- `pack` makes `<plugin> - Main.ba2` with the Creation Kit's Archive2, and with `"xbox": true` the Creations upload set with `<plugin> - Main_xbox.ba2`.
- `zip` writes the same bytes from the same files (sorted entries, fixed timestamps), never with `.psc` files.
- `check` runs the `release` command of sf-mod-check 0.7.0 or later (called sfmodcheck before it was renamed) on the zip; without a checker it's a WARNING, or a failure with `--require-check`.
- Safety: the folders the tool empties (stage, dist, creations-out) are refused when they are or hold the mod, its sources or its builder, a drive root, or anything in the game's folder.
- The `dotnet new sfmod` template has a builder on BuildKit 0.4.0 and Mutagen 0.54.4 (pinned exactly) that makes a light master with one quest, a starter script with `Debug.Trace` logging, a "Show Debug Messages (PC only)" switch in the mod's own Gameplay Options group, and a README that says how to turn on Papyrus logging. It takes the options `--prefix` and `--mod-author`.
- It was tested by rebuilding three existing mods; each plugin came out byte-identical to the released one (or the one difference was explained), with the scripts the same by meaning.

### Download

- `sf-mod-build.0.1.0.nupkg`: install it as a dotnet tool with `dotnet tool install --global sf-mod-build --add-source <folder holding the .nupkg>`.
- `sf-mod-build-0.1.0-win-x64.zip` is the program on its own; unzip it and put the folder on PATH.
- `sf-mod-build-template-0.1.0.zip` is the starter project; unzip it into a folder of its own and run `dotnet new install "<that folder>"`.
- Check the downloads against `SHA256SUMS`. The [README](https://github.com/taosecurity/sf-mod-build#install) has the steps.
