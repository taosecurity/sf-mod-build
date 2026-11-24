# Privacy

sf-mod-build runs on your own computer. It collects no data and sends nothing anywhere.

## What it does not do

- It makes no network connections of its own.
- It has no telemetry, analytics, crash reporting or update checks.
- It never changes a game file, and it refuses to write into the game's folder.

## What it reads

- your project: `sfmod.json`, the Papyrus sources, the assets folders, the builder project and the files `steps` and
  `builder.prebuilt` name;
- the game's folder, to find the Creation Kit's Papyrus compiler, Archive2 and the script sources zip: it looks at the
  Steam registry keys (`HKEY_CURRENT_USER\Software\Valve\Steam` and the `HKEY_LOCAL_MACHINE` Steam keys), Steam's
  `libraryfolders.vdf` and the usual Game Pass folders, or the folder you give with `--game`;
- the folders on `PATH`, to find `dotnet`, `sf-mod-check` and the programs your `builder.command` and `steps` name;
- for `doctor`: the game's Data folder and the INI files in your `Documents\My Games\Starfield` folder (or the folder
  you give with `--my-games`). `doctor` only reads them.

## What it writes

- In your mod's folder: `.build` (compiled scripts and staged files), `dist` (the plugin, the archives and the zips
  that `zip.folder` points at), `creations-out` with `"xbox": true`, the zip, and small made-lists
  (`.sf-mod-build-made`) that name the files it made in `dist` and `creations-out`, so it never deletes a file it didn't
  make. The folder names come from `sfmod.json`.
- The game's script sources, copied once from the Creation Kit's own zip to `%LOCALAPPDATA%\sf-mod-build\vanilla\`
  (on Linux `~/.local/share/sf-mod-build/`). That copy is the game's data from your own install; it stays on your
  computer. Delete the folder at any time and the next compile makes it again.
- A log file, only when you ask for one with `--log <file>`. The log lists the paths of your mod, the game and the
  tools it ran, so it can include your Windows user name; read it before you share it. `--verbose` prints the same
  lines on the console instead.

## Other programs it starts

It starts the programs your project names, and nothing else: the Creation Kit's Papyrus compiler and Archive2, `dotnet`
(for the `builder.project` builder), your `builder.command` program, the commands in `steps`, and `sf-mod-check` for the
last step. They run on your computer with your account's rights. What they do is covered by their own documents:
`dotnet` restores the builder's nuget packages (BuildKit and Mutagen in the starter project) from nuget.org on the
first build, which is a download from nuget.org and not something sf-mod-build sends. A builder or a step you write
yourself can do anything its code does, so read it before you run it.

## Personal data

sf-mod-build handles none. Its output shows the file names and folder paths of your project; if you post the output
somewhere, check those paths first (they can include your Windows user name).

## Downloading it

Downloading sf-mod-build from GitHub is covered by GitHub's own
[privacy statement](https://docs.github.com/site-policy/privacy-policies/github-general-privacy-statement). The tool
and the template install from the downloaded files, with nothing fetched from the network when you install with
`--add-source`. The author receives only the public download counts GitHub shows.

## Questions

Open an issue at https://github.com/taosecurity/sf-mod-build/issues.
