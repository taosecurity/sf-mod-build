# AGENTS.md: sf-mod-build for AI assistants

Plain steps for an AI assistant helping someone build a Starfield mod with sf-mod-build 0.1.0. The README has the
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
5. The starter project's builder pins `Taosecurity.Starfield.BuildKit` `[0.4.0]` and `Mutagen.Bethesda.Starfield`
   `[0.54.4]` from nuget.org (the first build downloads them). sf-mod-build 0.1.0 was tested with BuildKit 0.4.0;
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
says what and how to get it. A missing checker is only a WARNING (exit 0).

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
- The game folder (found by itself, or `--game`).

## 7. Outputs

- `<stage>\Scripts\<Namespace>\*.pex` (default `.build\stage`), emptied and refilled by `compile`.
- `<dist>\<Plugin>.esm` and `<dist>\<Plugin> - Main.ba2` (default `dist`), emptied and refilled by `plugin`.
- With `"xbox": true`: `creations-out\` with dist's files plus `<Plugin> - Main_xbox.ba2`.
- The zip, `<name>-<version>.zip` by default, in the sfmod.json folder (or `zip.folder`); its sha256 is printed.
- A cache of the game's script sources in `%LOCALAPPDATA%\sf-mod-build\vanilla\<sha256>\`.
- Each command ends with one `RESULT:` line on stdout.

## 8. Exit codes

- 0: OK (warnings allowed).
- 1: the build or the check failed (compiler errors, builder failure, checker found problems).
- 2: bad input or setup (wrong option, broken sfmod.json, missing tool, `-release` asked for, unsafe folder).

## 9. The log

- `--verbose`: log lines starting `log: ` on stderr.
- `--log <file>`: the same lines with times in that file (it refuses names ending in .esm, .esp, .esl, .ba2, .zip,
  .psc, .pex, .json, .cs or .csproj).
- On an unexpected error the tool prints `RESULT: CANNOT RUN (...)` and the log has the full error.

## 10. Limits

- Never compiles with `-release` and has no option for it (it strips every `Debug.Trace`).
- Never writes into the game folder: it refuses dist or creations-out folders inside it, and never empties a folder
  that is or holds the mod folder, a source folder, the builder or a drive root.
- Never uploads anything, never changes the game's files, never edits the person's load order or mod manager.
- Ships no game files; uses the Creation Kit's own tools from the person's game folder.
- `compile` and `pack` need Windows (the Creation Kit's tools are Windows programs).
- A fresh compile gives `.pex` files with a new compile time, so BA2 and zip bytes change between compiles; compare
  plugins by bytes and scripts by meaning.
- Don't hand-edit files in dist, stage or creations-out: the next build empties them.
