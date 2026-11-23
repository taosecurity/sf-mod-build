# AGENTS.md: sf-mod-build for AI assistants

Plain steps for an AI assistant helping someone build a Starfield mod with sf-mod-build 0.3.0. The README has the
background; this file has what to run.

## 1. What it is for

- sf-mod-build builds a Starfield mod from one project file, `sfmod.json`: it compiles the mod's Papyrus scripts
  with the Creation Kit's compiler, runs the mod's Mutagen builder to write the plugin, packs the scripts into a BA2
  with the Creation Kit's Archive2, writes a reproducible zip for Vortex or MO2, and runs sf-mod-check's `release`
  check on the zip.
- `dotnet new sfmod` makes a starter mod that builds as is.
- Use it when the person has (or wants) a mod whose plugin is made by a C# Mutagen builder. Don't use it for mods
  made only in the Creation Kit or xEdit: there is no builder to run.

## 2. Prerequisites (exact versions)

1. Windows 10 or 11.
2. Starfield on Steam with the Starfield Creation Kit installed from Steam. Needed files in the game folder:
   `Tools\Papyrus Compiler\PapyrusCompiler.exe`, `Tools\Archive2\Archive2.exe`, `Tools\ContentResources.zip`.
   A Game Pass copy has none of these.
3. .NET SDK 9.0 or later (`dotnet --list-sdks` shows a line starting `9.` or higher).
4. Optional: the checker, sf-mod-check 0.7.0 or later (the first version with the `release` command), from
   https://github.com/taosecurity/sf-mod-check/releases (`sf-mod-check.exe`).
   Versions before it was renamed were called sfmodcheck (`sfmodcheck.exe`), and sf-mod-build finds either name.
   Put it on PATH or pass it with `--checker`.
5. The starter project's builder pins `Taosecurity.Starfield.BuildKit` `[0.4.2]` and `Mutagen.Bethesda.Starfield`
   `[0.54.4]` from nuget.org (the first build downloads them). sf-mod-build 0.3.0 was tested with BuildKit 0.4.2;
   don't change the pin unless the person asks.

## 3. Install

One of:

1. From the GitHub release's `sf-mod-build.<version>.nupkg` (put it in a folder of its own):

   ```
   dotnet tool install --global sf-mod-build --add-source <folder holding the .nupkg>
   ```

2. Unzip `sf-mod-build-<version>-win-x64.zip` from the GitHub release and put the folder on PATH (no .NET runtime
   needed to run it).
3. From source: make the package, then install it as in 1 (`--add-source` needs a .nupkg, not the build output):

   ```
   dotnet pack src\SfModBuild -c Release -o nupkg
   dotnet tool install --global sf-mod-build --add-source nupkg
   ```

   Or run it without installing: `dotnet run --project src\SfModBuild -c Release -- <command>`.

Then the template, from the repo:

```
dotnet new install templates\sfmod
```

Or unzip `sf-mod-build-template-<version>.zip` from the release into a folder of its own and run
`dotnet new install "<that folder>"` (the zip holds the template's files at its top level).

## 4. Prove the install works

```
sf-mod-build doctor
```

Expect a list ending in a `RESULT:` line and exit code 0. Exit 2 means something needed is missing; the output
says what and how to get it. A missing checker is only a WARNING (exit 0). So are the setup checks under
`setup (not needed to build ...)`: StarfieldCustom.ini's Papyrus logging lines, CreationKitCustom.ini and the
unpacked script sources. Each WARNING there prints the exact lines to add; show them to the person and let them
edit the file (doctor never writes it).

## 5. Common tasks

Run these in the mod's folder (the one with `sfmod.json`), or pass that folder or file as the first argument.

- Make a new mod: `dotnet new sfmod -n MyMod --prefix MYM --mod-author "Your Name"` then `cd MyMod`.
- Check the project file: `sf-mod-build validate`
- Build everything (compile, plugin, pack, zip, check): `sf-mod-build build`
- Build with a checker that isn't on PATH: `sf-mod-build build --checker "<path>\sf-mod-check.exe"`
- Fail the build when no checker is found: `sf-mod-build build --require-check`
- Which game is used: the Steam copy when there is one (the Creation Kit's tools are only there), else Game Pass;
  `doctor` lists any other copy as a note. To pick one, or when none is found, add
  `--game "<folder holding Starfield.exe>"`.
- Pack loose files (meshes, materials, textures, sounds): put them in a folder laid out like `Data` (for example
  `Assets\Meshes\MyMod\Box.nif`, `Assets\Textures\MyMod\Box_color.dds`), add `"assets": { "source": "Assets" }` to
  `sfmod.json`, then run `sf-mod-build validate` (it lists each archive's files and any refused file) and
  `sf-mod-build build`. Textures go to `<Plugin> - Textures.ba2`, the rest to `<Plugin> - Main.ba2`.
- Xbox-format textures for the Creations set: add `"xboxTextures": "Assets-xbox"` (a second folder with the same
  layout) to the assets section, with `"xbox": true`. It must hold an Xbox copy of every PC texture (validate and
  pack refuse an empty folder or a missing copy when the mod has PC textures). Without it,
  `<Plugin> - Textures_xbox.ba2` holds the PC textures.
- Check the INI settings of a Mod Organizer 2 profile: `sf-mod-build doctor --my-games "<profile folder>"`.
- One step at a time: `sf-mod-build compile`, `sf-mod-build plugin`, `sf-mod-build pack`, `sf-mod-build zip`,
  `sf-mod-build check` (each needs the steps before it to have run).
- Troubleshoot: add `--verbose`, or `--log sf-mod-build-log.txt`, to any command.
- Help: `sf-mod-build --help`, `sf-mod-build <command> --help`, `sf-mod-build --version`.

## 6. Inputs

- `sfmod.json` (fields in the README, "The project file"). Paths in it are relative to its folder.
- Papyrus sources under `scripts.source` (default `Scripts\Source\<Namespace>\*.psc`).
- The builder project named in `builder.project`, run as
  `dotnet run -c Release --project <builder> -- <builder.args>` with `{data} {dist} {scripts} {stage} {version}`
  filled in as full paths.
- Loose files under `assets.source` (and Xbox textures under `assets.xboxTextures`), when the project has them.
- The game folder (found by itself, or `--game`).
- For `doctor`'s setup checks only: `StarfieldCustom.ini` in `Documents\My Games\Starfield` (or `--my-games`),
  `CreationKitCustom.ini` in the game folder, and `Data\Scripts\Source` (read, never written).

## 7. Outputs

- `<stage>\Scripts\<Namespace>\*.pex` (default `.build\stage`), emptied and refilled by `compile`.
- `<dist>\<Plugin>.esm` and `<dist>\<Plugin> - Main.ba2` (default `dist`), emptied and refilled by `plugin`.
- With loose textures: `<dist>\<Plugin> - Textures.ba2`. A mod with loose files but no scripts may have no Main
  archive. pack copies the loose files into `<stage>\pack\` first.
- With `"xbox": true`: `creations-out\` with dist's files plus `<Plugin> - Main_xbox.ba2` (and
  `<Plugin> - Textures_xbox.ba2` when there are textures).
- The zip, `<name>-<version>.zip` by default, in the sfmod.json folder (or `zip.folder`); its sha256 is printed.
  A `.ba2` in dist that the build doesn't make stops zip (and pack with `"xbox": true`) with exit 2 and a RESULT
  line naming it (pack without xbox only prints a `WARNING` line naming it and exits 0). Ask the person which
  case it is: an old or stray archive is deleted from dist (sf-mod-build ships only the archives it builds and the
  ones `zip.archives` lists); an archive the mod's own builder writes on purpose gets its file name listed in `zip.archives` in sfmod.json (for example `"archives": ["MyMod - Extra.ba2"]`);
  `zip` then ships it, and stops with exit 2, naming it, if it isn't in dist. `zip.archives` can't be used with
  `"xbox": true` (validate reports a PROBLEM, since pack can't make Xbox copies of them). `zip.files` takes no archives, and
  validate reports a `.ba2` there as a PROBLEM. pack deletes the mod's own old
  archives from dist when the mod no longer needs them (and from creations-out when xbox is on and there is nothing
  to pack; the other files there stay as the last build left them).
- A cache of the game's script sources in `%LOCALAPPDATA%\sf-mod-build\vanilla\<sha256>\`.
- Each command ends with one `RESULT:` line on stdout.

## 8. Exit codes

- 0: OK (warnings allowed).
- 1: the build or the check failed (compiler errors, builder failure, checker found problems).
- 2: bad input or setup (wrong option, broken sfmod.json, missing tool, `-release` asked for, unsafe folder, an archive in dist the build doesn't make).

## 9. The log

- `--verbose`: log lines starting `log: ` on stderr.
- `--log <file>`: the same lines with times in that file (it refuses names ending in .esm, .esp, .esl, .ba2, .zip,
  .psc, .pex, .json, .cs or .csproj).
- On an unexpected error the tool prints `RESULT: CANNOT RUN (...)` and the log has the full error.

## 10. Limits

- Never compiles with `-release` and has no option for it (it strips every `Debug.Trace`).
- Never writes into the game folder: it refuses dist or creations-out folders inside it and, in pack, a dist folder
  that is a link or junction (with nothing to pack, a creations-out folder too), and never empties a folder
  that is or holds the mod folder, a source folder, the builder or a drive root.
- Never uploads anything, never changes the game's files, never edits the person's load order or mod manager.
- Never packs an assets folder that is in the game's folder or holds it, the mod's own folder, dist, stage,
  creations-out, the builder or a script source folder; validate refuses those.
- doctor only reads the INI files and never writes them.
- Doesn't convert art: `.tif`, `.fbx` and `.wav` are packed as they are (with a warning). Textures must already be
  `.dds` and sounds `.wem`; the Creation Kit's own tools make those. A PC `.wem` is silent on Xbox and PlayStation 5.
- Ships no game files; uses the Creation Kit's own tools from the person's game folder.
- `compile` and `pack` need Windows (the Creation Kit's tools are Windows programs).
- A fresh compile gives `.pex` files with a new compile time, so BA2 and zip bytes change between compiles; compare
  plugins by bytes and scripts by meaning.
- Don't hand-edit files in dist, stage or creations-out: the next build empties them.
- Archives the builder writes itself aren't supported: their files go in the assets folder, so pack builds
  them into the mod's own archives.
