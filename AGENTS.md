# AGENTS.md: sf-mod-build for AI assistants

Plain steps for an AI assistant helping someone build a Starfield mod with sf-mod-build 0.5.1. The README has the
background; this file has what to run.

## 1. What it is for

- sf-mod-build builds a Starfield mod from one project file, `sfmod.json`: it compiles the mod's Papyrus scripts
  with the Creation Kit's compiler, runs the mod's Mutagen builder to write the plugin, packs the scripts into a BA2
  with the Creation Kit's Archive2, writes a reproducible zip for Vortex or MO2 (one zip per plugin), and runs
  sf-mod-check's `release` check on each zip.
- Platforms: Tested on PC (Steam) and Xbox. PS5 is not tested.
- `dotnet new sfmod` makes a starter mod that builds as is.
- Use it when the person has (or wants) a mod whose plugin is made by a builder: a C# Mutagen builder project, or any
  other program (`builder.command`). A plugin made by hand in the Creation Kit or xEdit can be shipped as it is with
  `builder.prebuilt`, so the scripts, archives and zip still come from sf-mod-build.

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
5. The starter project's builder pins `Taosecurity.Starfield.BuildKit` `[0.6.0]` and `Mutagen.Bethesda.Starfield`
   `[0.54.4]` from nuget.org (the first build downloads them). sf-mod-build 0.5.1 was tested with BuildKit 0.6.0;
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
- Build everything (compile, plugin, pack, zip, check): `sf-mod-build build`. The plugin step shows BuildKit's gap check
  result (`gap check: RESULT: OK ...`, its ERROR lines, or `gap check: NOT RUN ...`); the other builder lines need
  `--verbose`. The `check` step checks the PC zips only. The Creations check runs on the creations-out folder instead
  (`"xbox": true` makes it): `sf-mod-check release creations-out --profile creations` (or the folder `"creationsOut"` names), which `build` prints at the end
  but doesn't run.
- Build with a checker that isn't on PATH: `sf-mod-build build --checker "<path>\sf-mod-check.exe"`
- Fail the build when no checker is found: `sf-mod-build build --require-check`
- Check a mod whose master plugin isn't in the Data folder (for example another mod's folder in the mod manager):
  `sf-mod-build build --masters-dir "<folder holding the master>"` (or `sf-mod-build check`; give it once per
  folder). It is passed to the checker; a folder that isn't there stops the build with exit 2 before it starts.
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
- Run one more job of the mod's own after the plugin or the archives are made (copy voice files, move Strings
  tables, check the archives): add it to `steps.afterPlugin` or `steps.afterPack` in `sfmod.json` (section 6),
  then `sf-mod-build validate` to see its `runs:` line. A file a step makes and that should ship goes in dist
  (zip takes dist's files); `zip.files` takes only files that are there before the build.
- Ship a voice archive or a ready archive (another author's original): add an entry to the top-level `archives`
  list, `{ "suffix": "Voices", "source": "Voices", "compression": "none" }` (a folder laid out like Data, packed
  uncompressed so the `.wem` files play) or `{ "suffix": "Textures", "file": "Original/MyMod - Textures.ba2" }`
  (copied as it is). pack makes `<Plugin> - <suffix>.ba2` in dist and zip ships it. Not with `"xbox": true`.
- A builder that writes more than one plugin: list the others in `parts`, `[{ "plugin": "MyMod-Extra.esm",
  "scripts": { "namespaces": ["MYMX"] } }]` (scripts optional). Each part's scripts go into `<part plugin> - Main.ba2`,
  and each part gets its own zip, `<name>-<Part>-<version>.zip` (`MyMod-Extra-0.1.0.zip`), beside the main zip.
  `"zipPart": "Extra"` on a part picks `<Part>` (letters and digits only). `"zip": { "split": false }` keeps one zip
  for every plugin instead. A part with scripts is refused with `"xbox": true`.
- One step at a time: `sf-mod-build compile`, `sf-mod-build plugin`, `sf-mod-build pack`, `sf-mod-build zip`,
  `sf-mod-build check` (each needs the steps before it to have run).
- Troubleshoot: add `--verbose`, or `--log sf-mod-build-log.txt`, to any command.
- Help: `sf-mod-build --help`, `sf-mod-build <command> --help`, `sf-mod-build --version`.

## 6. Inputs

- `sfmod.json` (fields in the README, "The project file"). Paths in it are relative to its folder.
- Papyrus sources under `scripts.source` (default `Scripts\Source\<Namespace>\*.psc`). They compile with
  `-optimize` unless `scripts.optimize` is `false` (a part's scripts follow that value unless they set their own).
- The builder, exactly one of:
  - `builder.project`, a .NET project run as `dotnet run -c Release --project <builder> -- <builder.args>`;
  - `builder.command`, a list (program first) run as `<program> <its arguments> <builder.args>` in the sfmod.json
    folder, without a shell; a program with a slash is relative to sfmod.json, a bare name is found on PATH;
  - `builder.prebuilt`, a ready plugin file copied into dist under the `plugin` name (no `args`; `plugin` then needs
    neither the game nor .NET).

  `{data} {dist} {scripts} {stage} {version}` are filled in as full paths (in `builder.args` and in the command's
  arguments). `builder.command` runs whatever it names: before building a mod the person didn't write, run
  `sf-mod-build validate` and show them its `runs:` lines, which list each command the build would run.
- Optional `steps.afterPlugin` and `steps.afterPack`: lists of commands (each a list like `builder.command`) run in
  order at the end of `plugin` and at the end of `pack` (so also in `build`), in the sfmod.json folder, without a
  shell. Placeholders: the builder's plus `{name}` and `{plugin}` (without extension). A missing program stops the
  step before any work with exit 2; a command that exits non-zero stops it with exit 1 and a RESULT line naming it,
  such as `RESULT: FAILED at steps.afterPack[1] (python.exe exited with code 3)`. A failed builder or Archive2 run
  skips the steps. These run whatever they name too, and validate shows each one as a `runs:` line.
- Loose files under `assets.source` (and Xbox textures under `assets.xboxTextures`), when the project has them.
- Optional top-level `archives`: a list of `{ "suffix", "source" or "file", "compression": "default" | "none",
  "format": "general" | "dds" }`. A `source` folder must be inside the mod's folder and apart from dist, stage and
  creations-out, and apart from `assets.source`, `assets.xboxTextures` and the other entries' sources (neither may
  hold the other); validate doesn't look inside it (a `steps.afterPlugin` command may fill it), and pack stops with
  exit 2 before Archive2 runs when it is missing, empty, holds a file at its top, a link, a dot file or folder, `Thumbs.db`,
  `desktop.ini`, or a `.psc`, plugin, archive or zip. A `file` is a ready `.ba2` that must exist (it may be outside the mod; it is only read);
  `compression` and `format` are refused with it. The suffix may be `Main` or `Textures` only when pack doesn't make
  that archive itself. Refused with `"xbox": true`, and a name can't also be in `zip.archives`.
- Optional `parts`: a list of `{ "plugin", "scripts", "zipPart" }`, the other plugins the builder writes into dist.
  `scripts` is optional and written like the top-level one; its `source` defaults to the top-level source folders.
  `zipPart` (optional, letters and digits only) names the part's zip; the default is the part plugin's name
  without the extension, with the mod's `name` taken off the front and only letters and digits kept. validate
  refuses a part plugin whose name without the extension is the main plugin's or another part's, a name that
  breaks the plugin-name rules, a script in two places (the top level and a part, or two parts), `parts` with
  `builder.prebuilt`, a part with scripts with `"xbox": true`, and (with `zip.split` on) two zips with one name
  and a `zip.archives` name that starts with no plugin's name.
- Optional `zip.split` (default `true`): with `parts`, one zip per plugin; `false` keeps one zip for all of them.
- The game folder (found by itself, or `--game`).
- For `doctor`'s setup checks only: `StarfieldCustom.ini` in `Documents\My Games\Starfield` (or `--my-games`),
  `CreationKitCustom.ini` in the game folder, and `Data\Scripts\Source` (read, never written).

## 7. Outputs

- `<stage>\Scripts\<Namespace>\*.pex` (default `.build\stage`), emptied and refilled by `compile`.
- `<dist>\<Plugin>.esm` and `<dist>\<Plugin> - Main.ba2` (default `dist`). Before the builder runs, `plugin` deletes
  only what earlier builds made in dist: the names in `<dist>\.sf-mod-build-made` (what the builder, sf-mod-build
  and the steps wrote last time; it never goes in the zip), plugins and `.ba2` files. Zips stay, and so does a `zip.folder` inside dist. Anything else
  there (a folder or file the person keeps in dist) stops `plugin` with exit 2, naming each one, and nothing is
  deleted: ask the person to move it out of dist. After an update from 0.3.0 or earlier the first build may name
  the builder's own extra files this way once; they can be deleted.
- With loose textures: `<dist>\<Plugin> - Textures.ba2`. A mod with loose files but no scripts may have no Main
  archive. pack copies the loose files into `<stage>\pack\` first.
- With `parts`: each part plugin in dist (written by the builder; `plugin` exits 1 when one is missing), and for a
  part with scripts `<stage>\parts\<Part>\Scripts\<Namespace>\*.pex` and `<dist>\<Part> - Main.ba2`, where
  `<Part>` is the part plugin's name without the extension. zip stops with exit 2 when one is missing.
- With an `archives` list: `<dist>\<Plugin> - <suffix>.ba2` for each entry (packed from its `source`, or copied from
  its `file`). zip ships them, and stops with exit 2, naming the archive, when one isn't in dist.
- With `"xbox": true`: `creations-out\` with dist's files plus `<Plugin> - Main_xbox.ba2` (and
  `<Plugin> - Textures_xbox.ba2` when there are textures).
- The zip, `<name>-<version>.zip` by default, in the sfmod.json folder (or `zip.folder`); its sha256 is printed.
  With `parts` (and `zip.split` not `false`) there is one zip per plugin: the main zip holds the main plugin, its
  archives and every other file (README, LICENSE, the other top-level files in dist); each part's zip,
  `<name>-<Part>-<version>.zip`, holds the part plugin, its `<part plugin> - Main.ba2` and a renamed copy of each
  LICENSE or COPYING file (`LICENSE-MyMod-Extra.txt`). A `zip.archives` entry goes in the zip of the plugin whose
  name starts it. zip stops with exit 2 (nothing written) when a plugin in dist is neither `plugin` nor a part
  (also without parts), when a `zip.archives` name starts with no plugin's name, or when two zips or two files in
  one zip would get the same name; the fix is in its RESULT line. It never deletes an old zip, and prints a `note:`
  line naming the zip of a part that was removed. `check` checks each zip, giving a part's zip `--masters-dir <dist>`.
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
- 1: the build or the check failed (compiler errors, builder failure, a `steps` command that exited non-zero, checker found problems).
- 2: bad input or setup (wrong option, broken sfmod.json, missing tool, `-release` asked for, unsafe folder, an archive in dist the build doesn't make, a file or folder in dist or creations-out that no earlier build made, a `--masters-dir` that isn't a folder).

## 9. The log

- `--verbose`: log lines starting `log: ` on stderr.
- `--log <file>`: the same lines with times in that file (it refuses names ending in .esm, .esp, .esl, .ba2, .zip,
  .psc, .pex, .json, .cs or .csproj).
- On an unexpected error the tool prints `RESULT: CANNOT RUN (...)` and the log has the full error.

## 10. Limits

- Never compiles with `-release` and has no option for it (it strips every `Debug.Trace`).
- Never builds, packs or deletes in the game folder: it refuses dist or creations-out folders inside it and, in pack, a dist or
  creations-out folder that is a link or junction (in plugin, any dist that is a link or junction), and never
  empties a folder that is or holds the mod folder, a source folder, the builder or a drive root.
- Never deletes a file it didn't make (since 0.4.1). In dist (plugin) and creations-out (pack with `"xbox": true`)
  it deletes only the names in that folder's `.sf-mod-build-made` list, the mod's own plugins and archives (the
  names sfmod.json gives: the plugin, each part, the archives pack makes, the `archives` list and `zip.archives`),
  half-written zips in dist, and in creations-out the files it is about to copy there from dist. Zips in dist, zips in creations-out
  when `zip.folder` is creations-out, and the `zip.folder` when it is inside either folder always stay. Anything else
  there (a user's file or folder, a backup plugin such as `MyMod-backup.esm`) stops the step with exit 2, naming
  each entry, before anything is deleted. Ask the person to move it out; don't delete it for them.
- Never uploads anything, never changes the game's files, never edits the person's load order or mod manager.
- Never packs an assets folder that is in the game's folder or holds it, the mod's own folder, dist, stage,
  creations-out, the builder or a script source folder; validate refuses those.
- doctor only reads the INI files and never writes them.
- Doesn't convert art: `.tif`, `.fbx` and `.wav` are packed as they are (with a warning). Textures must already be
  `.dds` and sounds `.wem`; the Creation Kit's own tools make those. A PC `.wem` is silent on Xbox and PlayStation 5.
- Ships no game files; uses the Creation Kit's own tools from the person's game folder.
- `compile` and `pack` need Windows (the Creation Kit's tools are Windows programs). A `pack` with only ready
  archives to copy (`archives` entries with `file`, and no scripts or loose files) needs neither Archive2 nor the game.
- Tested on PC (Steam) and Xbox. PS5 is not tested.
- A fresh compile gives `.pex` files with a new compile time, so BA2 and zip bytes change between compiles; compare
  plugins by bytes and scripts by meaning.
- Don't hand-edit files in dist, stage or creations-out: the next build empties stage and deletes the files
  earlier builds made in dist and creations-out.
- Archives the builder writes itself into dist ship only when their file names are listed in `zip.archives` (since
  0.3.0); they can't be used with `"xbox": true`. Otherwise put their files in the assets folder, so pack builds
  them into the mod's own archives, or use the `archives` list (since 0.4.0) for a folder pack should pack as its
  own archive or a ready archive to copy; that can't be used with `"xbox": true` either.
- With `"xbox": true`, pack fills creations-out before `steps.afterPack` runs, so a file an afterPack command
  adds to dist or changes there reaches the zip but not creations-out. A command whose output must ship on both
  goes in `steps.afterPlugin`. A file an afterPack command writes into creations-out itself stops the next pack
  (it isn't on creations-out's made list).
