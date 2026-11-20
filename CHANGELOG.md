# Changelog

## 0.1.0 (unreleased)

First version.

- One project file per mod, `sfmod.json`: plugin, version, script folders and namespaces, the Mutagen builder and
  its arguments (`{data} {dist} {scripts} {stage} {version}`), dist, stage, the Xbox archive for Creations, the
  zip's name, folder and extra files, and the checker. Comments and trailing commas are allowed; every problem names
  the field and the fix.
- Commands: `doctor`, `validate`, `compile`, `plugin`, `pack`, `zip`, `check` and `build` (all five build steps in
  order), each with `--help`, `--verbose` and `--log <file>`. Exit codes 0 OK, 1 failed, 2 bad input or setup.
- `compile` runs the Creation Kit's PapyrusCompiler once per script with the game's script sources, unpacked once
  from `Tools\ContentResources.zip` into a cache. It never compiles with `-release` and refuses it with the reason.
- `pack` makes `<plugin> - Main.ba2` with the Creation Kit's Archive2, and with `"xbox": true` the Creations upload
  set with `<plugin> - Main_xbox.ba2`.
- `zip` writes the same bytes from the same files (sorted entries, fixed timestamps), never with `.psc` files.
- `check` runs sf-mod-check (sfmodcheck) 0.7.0 or later `release` on the zip; without a checker it's a WARNING, or
  a failure with `--require-check`.
- Safety: the folders the tool empties (stage, dist, creations-out) are refused when they are or hold the mod, its
  sources or its builder, a drive root, or anything in the game's folder.
- The `dotnet new sfmod` template: a builder on BuildKit 0.4.0 and Mutagen 0.54.4 (pinned exactly) that makes a
  light master with one quest, a starter script with `Debug.Trace` logging, a "Show Debug Messages (PC only)"
  switch in the mod's own Gameplay Options group, and a README that says how to turn on Papyrus logging. Options
  `--prefix` and `--mod-author`.
- Tested by rebuilding three existing mods; each plugin came out byte-identical to the released one (or the one
  difference was explained), with the scripts the same by meaning.
