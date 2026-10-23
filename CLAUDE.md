# Slip & Skid: More Maps

BepInEx 6 (Unity Mono) plugin for Slip & Skid adding custom tracks. netstandard2.1, HarmonyX.
The game uses Unity Netcode for GameObjects. The build is stripped. Unity 2022.3.62.
Plugin GUID `slipskid.moremaps` (never change it: it names players' config files). DLL
`SlipSkid.MoreMaps.dll`, installed to `BepInEx/plugins/MoreMaps` with `routes/*.json` beside it.
Sister mod: GoodCards (`E:\ModDev\SlipAndSkipCards\SlipSkid-GoodCards`), same patterns.

## Reference
- The game's decompiled Assembly-CSharp is in GoodCards' `decompiled/`. Read and search it; never copy it here.
- `docs/notes.md`: what we've confirmed about levels and the road kit. GoodCards' `docs/game-notes.md` has the
  rest (levels research, stripped API). Update notes.md whenever you confirm something new.
- Log: `<BepInExDir>/LogOutput.log`, our lines prefixed `Levels:`, `TrackBuilder:`, `KitDump:`, `Preview:`.

## Build and run
- `dotnet build -c Release` (paths from git-ignored `local.props`). Copies the DLL and `assets/**/*.json`.
  The game must not be running (the DLL is locked).
- Launch ONLY through Steam: `Start-Process explorer.exe -ArgumentList "steam://rungameid/2651350"`.
  Never run `Slip & Skid.exe` directly (breaks BepInEx loading for later Steam launches).

## How it works
- `src/Levels/CustomLevels.cs`: one level per `routes/*.json` (sorted by file name), a copy of its base `Level`
  appended to `LevelManager.levels` on every machine.
- `src/Levels/TrackBuilder.cs`: prefix on `LevelDefinition.Init` hides the vanilla track and clones vanilla road
  tiles along the route. `src/Levels/RoadKit.cs` holds the piece shapes (connectors). See docs/notes.md.
- `src/Levels/RoutePlanner.cs` lays a route out without a scene (used by the builder and by level select);
  `src/Levels/LevelSelectInfo.cs` makes the level-select preview ribbon and surface shares, `LevelArt.cs` the
  card and thumbnail pictures. `SceneFit.cs` clears scenery off the road and fits the trails camera.
  `Scenery.cs` stands the route's `scenery` props (clones from the game's biome scenery) off the road.
- Route JSON: `name` (unique level name), `baseLevel` (vanilla `Level.name`), `surface`/`borders` (paint),
  `pieces`: `{type, count, turn: left|right, slope: up|down, finish, surface, borders, from}`, `{type: "gap",
  drop}`, a segment name, or a fork `{type: "R078B111", split: [[...], [...]], merge: {type: "R078B111"}}`. Only types in the base scene and in `RoadKit` can be used.
- `src/Levels/KitLibrary.cs`: pieces borrowed from other tracks (`"from"`, and segments' parts), loaded behind
  the loading transition. Segments (multi-tile pieces lifted from a donor) live in `RoadKit.Segments`. Design routes in tools/route_sim.py first (overlaps, heights), then PreviewRoutes.
- Magma kills after 3 s on it. Keep magma stretches short.
- Debug config (`[Debug]`): `DumpKit` (dump each loaded level to `kit-<scene>.json`), `DumpAllTracks` and
  `PreviewRoutes` (run once at the main menu without any input, then switch off; with `QuitAfterDump` the game
  quits afterwards). PreviewRoutes builds every route, raycasts its centre line and renders
  `preview-<level>-*.png` next to the DLL. Use it to check a route change without driving. `Probe` logs
  level preview data, surface physics and car stats; `Gallery` (comma-separated track names) renders every
  piece type of those tracks to `gallery-<track>.png`, in the order the log lists them; `Snapshot`
  ("Track: x,y,z; ...") renders spots of a vanilla track with its scenery. `DumpBiomes` lists the biome scenery props to
  `biomes-<track>.txt`.
- Plans for the next maps: docs/map-plans.md. MapKitLoader (another map mod) is compared in docs/notes.md.

## Hard rules
- Every player needs the same route files: levels are selected by index and loaded by name. Adding, removing,
  renaming or reordering routes changes the list. Tile ids are hashed from the route name and piece index, so
  editing a route is fine as long as everyone has the same file.
- Prefer Prefix/Postfix; use a Transpiler only if the user agrees first.
- Before using any Unity API, confirm it exists in the stripped DLLs (build to be sure). docs/notes.md lists
  what we've found missing.
- Wrap every patch body in `Guard`; log with the plugin logger, prefixed by system name.
- Don't push; commit only when asked.

## Done means
It builds, the log shows it registered, and you've told the user exactly what to check in game (solo first,
then a two-player online test).
