# MyMod

MyMod is a starter Starfield mod made with `dotnet new sfmod`. It adds one quest that starts with the game and writes
a line to the Papyrus log, plus a "Show Debug Messages (PC only)" switch in its own Gameplay Options group. It edits
no vanilla records. The builder opens Starfield.esm, but a master is listed in the plugin only once a record
points into it, so the starter plugin has no masters until you use something from the game.

## Build

You need the .NET 9 SDK, Starfield on Steam with the Creation Kit installed (for its Papyrus compiler and
Archive2), and sf-mod-build. Then, in this folder:

```
sf-mod-build doctor
sf-mod-build build
```

`build` compiles the scripts in `Scripts\Source`, runs the builder in `Builder` (which writes `dist\MyMod.esm`),
packs the scripts into `dist\MyMod - Main.ba2`, makes `MyMod-0.1.0.zip` here and checks it with sf-mod-check when
it's installed. Install the zip with Vortex or Mod Organizer 2.

To ship meshes, materials, textures or sounds of your own, put them in an `Assets` folder laid out like the game's
`Data` folder (for example `Assets\Meshes\MyMod\Box.nif` and `Assets\Textures\MyMod\Box_color.dds`) and turn on the
`"assets"` line in `sfmod.json`. The build then packs the textures into `dist\MyMod - Textures.ba2` and the rest
into `dist\MyMod - Main.ba2` with the scripts.

- The plugin's records are made in `Builder\Program.cs`. Each record keeps its FormID from build to build through
  `ids.lock.json`; commit that file and `Builder\packages.lock.json`.
- A script property is filled in `Program.cs` by name; the name must match the `Property` line in the `.psc`.
- Change the version in `sfmod.json`.

## Turning on Papyrus logging

The script's lines go to the Papyrus log only when logging is on. Add these lines to `StarfieldCustom.ini` in
`Documents\My Games\Starfield` (make the file if it isn't there):

```
[Papyrus]
bEnableLogging=1
bEnableTrace=1
bLoadDebugInformation=1
```

Start the game, load a save, then quit. The log is `Documents\My Games\Starfield\Logs\Script\Papyrus.0.log`; look
for lines starting `[MyMod]`. To see the same lines on screen, turn on Settings > Gameplay Options > MyMod >
Show Debug Messages (PC only). Xbox and PlayStation can't show debug lines.

## License

See LICENSE.
