# More Maps: what we know

Confirmed in game (2026-10-23, game build with Unity 2022.3.62) unless marked otherwise. The general level
research is in GoodCards' `docs/game-notes.md` ("Levels", "Levels in detail"); this file adds what the kit
dumps and the prototype confirmed.

## Levels list
- `LevelManager.instance.levels` holds the 16 tracks, in build order: Snake, Bridge, Bobsleigh, Aqueduc,
  Danibugne, Ribambelle, Core 7 meltdown, Umbrellas, Swing, Pizza pizza, Taiga, Sand storm, Nice Ice, Drazgon,
  Dermz city, Ozone (scenes `level6`..`level21`; `Level.name` == `sceneName`). So "level15" is Pizza pizza.
- Appending a copy of a vanilla `Level` in a `LevelManager.Awake` postfix works: it shows up in level select
  (index 16, before the random entry). The copy keeps the base level's preview image, biome surface,
  difficulty and surface counts; we clear `previewMeshes`, `ghostUnlockableContent` and `isReversible`.
- Selection is synced by index, loading by name. A client without the mod (or with different route files)
  sees index 16 as "random" and can't find the level by name, so the host waits forever. Every player needs
  the same `routes/*.json`. No sync or mismatch check exists yet.

## Level scenes (from the kit dumps)
- Scenes are flat: tile roots at the scene root, plus `LevelDefinition` (with `FogPlane`,
  `EnvironmentVisualRoot`), `TriPointLightingClassic`, `GravityPlane`, `StartLine`, `FinishLine`,
  `ObstacleDefinition` (forbidden zones), `CameraManager`, `EndGamePlatform`, `EffectsManager`, reflection
  probes and a `CollisionMesh`.
- **No track has in-scene checkpoints** (`LevelDefinition.checkpoints` is empty everywhere). Checkpoints only
  come from the Checkpoint card (`ObstacleCheckpoint` registers itself). No track has `inSceneObstacles`.
- The fog plane is at y = -17.5 on every track (cars die 3.5 m below it). Scenery comes from the biome
  prefabs `LevelDefinition.Init` instantiates at `EnvironmentVisualRoot`, not from the scene.
- `CollisionMesh`: a renderer-less MeshCollider on layer `RoadCombined` with one merged mesh of the whole road
  (`<Track>_CollisionMesh`). The car linecasts against `RoadCombined` every physics step and snaps back if it
  passed through (anti-tunnelling, `SphereCarController` ~966). The car's floor ray mask is
  `Default, TransparentFX, Ignore Raycast, Obstacle, MapObstacle` (tiles are on `Default`).
- Start: `StartLine` sits exactly on an `R000B101` cap tile's pivot with the same rotation. Its children are
  `StartLineGate`, `Collider` (its own floor, MeshCollider + SurfaceInfo), `SpawnPoints` (the
  `RandomPositionPlayerSpawner`, 4 spawn points + time-trial spawn, 2 m apart, facing along the road),
  `FinishLineRootOffset`, `CountDownObject`. Moving the StartLine moves the spawns.
- Finish: `FinishLine` (BoxCollider trigger 10x5x10, layer FinishLine) sits in the centre of an `R000B101` cap
  cell, 1.5 m above the road, facing the driving direction; `FinishLineGate` arch 5 m behind it (at the cell's
  entry edge). `ForbiddenObstacleZone_Start` (sic) is a 20 m box on the finish, `ForbiddenObstacleZone_End` on
  the start.
- `EndGamePlatform` is a podium off to the side with its own camera; it doesn't need to be near the road.

## The road kit
- Tiles are Blender exports: rotation `(270, yaw, 0)` (a few are stood up, `(0, yaw, 90)`), scale 100, yaw in
  90-degree steps (a handful at 45). Positions are on a 10 m grid (each track has its own offset) with 1 m
  height steps. Pieces are 1 to 4 cells (10 to 40 m) per side.
- In the tile's own frame (metres, before the scale): footprint x in [-length, 0], y in [0, width], z up, pivot
  at a corner on the road surface. Rx(270) maps local x to world x, local y to world -z, local z to up.
- `B###` = which side borders the piece has: B101 = both sides, B100 = the y-high side only, B001 = the y-low
  side, B000 none, B010/B020 a middle divider. Borders are child tiles (`Border<RRR>B<BBB>I00n`) with their
  own `LevelTileObject` and `SurfaceInfo` (concrete on most tracks, magma on Pizza pizza).
- Tiles are static-batched: `MeshFilter.sharedMesh` is `Combined Mesh (root: scene) N`; the tile's own mesh is
  `LevelTileObject.nonBatchedMesh`. Meshes are not readable. MeshColliders keep the real mesh.
- Connectors (centre of the road at the footprint edge), worked out from which neighbours touch which faces
  across all 16 tracks (`tools/kit_analysis.py`):

| Piece | Shape | Connector A | Connector B |
|---|---|---|---|
| R000 | start/finish cap, 1 cell, round closed end | (-10, 5, 0) out -X | (0, 5, 0) out +X |
| R001 | straight, 1 cell | (-10, 5, 0) out -X | (0, 5, 0) out +X |
| R003 / R004 | slope, 1 cell, +2 / +5 m | (-10, 5, rise) out -X (high) | (0, 5, 0) out +X (low) |
| R009 / R012 | slope, 2 cells, +4 / +5 m (eased) | (-20, 5, rise) out -X (high) | (0, 5, 0) out +X (low) |
| R017 / R018 / R021 / R024 | 90-degree turn, n = 1..4 cells, radius 10n-5 | (0, 5, 0) out +X | (-(10n-5), 10n, 0) out +Y |

  Entering a turn at A turns left, at B right. Other families seen (not mapped yet): R005/R008/R014 (straight
  variants), R016/R060/R062/R176/R177 (9-14 m tall pieces: walls/jumps?), R027-R038 and R071-R089 (lane
  shifts, splits, wide pieces), R063-R069 and R140-R159 (steep ramps and climbing turns), R104-R106 (half
  pipes?), R109/R110 (5 m strips, 200+ each on some tracks), R111-R114 (S-bends), R163-R174 (thin rails, side
  barriers, magma borders on Pizza pizza).
- Piece shapes we can place, per track (B101 variants):

| Track | tiles | root types | B101 shapes | main surfaces | start (pos, yaw) |
|---|---|---|---|---|---|
| Aqueduc | 609 | 30 | R001 R004 R009 R012 R017 R018 R021 | Concrete, Dirt, Asphalt | (205, 3, 132), 0 |
| Bobsleigh | 168 | 11 | R001 R009 R018 R021 | Concrete, Ice, Asphalt | (-230, 0, 0), 270 |
| Bridge | 578 | 16 | R001 R004 R009 | Concrete, Asphalt | (255, 51, 10), 0 |
| Core 7 meltdown | 770 | 40 | R001 R004 R021 R024 | Concrete, Asphalt, Magma | (-64.75, 0, 301), 270 |
| Danibugne | 524 | 21 | R001 R018 | Sand, Asphalt | (0, 0, 0), 270 |
| Dermz city | 280 | 32 | R001 R012 R018 | Concrete, Mud, Asphalt | (0, 40, 0), 0 |
| Drazgon | 256 | 16 | R001 | Magma, Asphalt | (0, 26, 0), 90 |
| Nice Ice | 133 | 19 | R001 R004 R009 | Concrete, Ice, Asphalt | (0, 0, 0), 0 |
| Ozone | 467 | 17 | R001 R003 R004 R017 | Concrete, Asphalt | (370, 62, -115), 0 |
| Pizza pizza | 278 | 33 | R001 R012 R017 R018 R021 | Magma, Asphalt, Concrete | (-109, 37, -209), 90 |
| Ribambelle | 181 | 15 | R001 R009 R017 R018 | Concrete, Asphalt | (0, 0, 0), 270 |
| Sand storm | 263 | 21 | R001 R004 R012 R018 | Sand, Asphalt | (185, 90, -269), 0 |
| Snake | 199 | 21 | R001 R024 | Concrete, Asphalt | (-10, 30, -165), 180 |
| Swing | 259 | 27 | R001 | Concrete, Asphalt | (20, 104, -200), 180 |
| Taiga | 518 | 28 | R001 R003 R012 R017 R018 | Ice, Asphalt, Concrete | (60, 2, -20), 0 |
| Umbrellas | 319 | 29 | R001 R003 R004 R012 R017 R018 | Concrete, Mud, Asphalt | (-200, 61, 97), 180 |

  Best bases: Aqueduc (most placeable shapes: three turn sizes and three slopes), Umbrellas, Pizza pizza
  (three turn sizes and a slope, compact map). "Root types" counts every tile root name, borders and rails
  included; the GoodCards scene-file scan (12-29 types, level15 most) counted differently.

## How the prototype builds a track (`src/Levels/TrackBuilder.cs`)
- Prefix on `LevelDefinition.Init`, only when the level being loaded (`SceneManager.LoadLevel(Level)` prefix) is
  ours and its scene matches. Runs on every machine.
- Keeps the vanilla start line, spawns and the R000 cap under the start; the route starts at the cap's far edge
  along the spawns' forward.
- Hides every other tile root and `CollisionMesh` (SetActive false), clears `tiles` and `inSceneObstacles`.
- Clones an inactive template per piece (`Object.Instantiate`), so it can fix the clone before Awake:
  replaces the static-batched MeshRenderer with a fresh one using `nonBatchedMesh`, gives every tile in it a
  deterministic id (MD5 of `<route name>/<piece index>/<tile index>`), sets the tile's `SurfaceInfo` and
  `Renderers` properties (normally set in Start, but `Init` reads them first), then activates it and adds its
  tiles to `def.tiles`.
- Adds a collider-only copy of each piece on layer `RoadCombined` for the anti-tunnel check (we can't build a
  merged mesh: no `MeshCollider.sharedMesh` setter).
- Moves the finish line (and the forbidden zone on it) onto the last piece. Fog, gravity plane, lighting,
  biome, music and the end-game platform stay vanilla.

## Level select, surfaces, car (Probe, 2026-10-23)
- Vanilla `previewMeshes`: one unreadable mesh per track (10k-88k vertices, roughly centred on the origin, a
  submesh per surface) and the track scene's renderer holding `LevelMeshPreview_<Surface>` materials
  (`Shader Graphs/LevelMeshPreview`). `LevelSelect.Update` draws it rotating, scaled to fit, with
  `Graphics.RenderMesh`; with no preview meshes it shows `previewImage` instead. `previewImage` is 1024x512
  (`<Track>_preview`), `thumbnailImage` 512x512.
- `SurfaceManager.surfaces`: Asphalt, Concrete, Dirt, Ice, Mud, Default, Sand, Magma (+ GoodCards'
  `CustomContent_Reverse` at #8 when installed). Physics values are in map-plans.md. Magma alone has a car
  modifier: `BurnModifier`, 3 s, `killOnEnd`, unique. Leaving the surface removes it (the car drops the old
  surface's modifiers on a surface change), so **more than 3 s on magma kills**.
- Dying (falling 3.5 m below the fog, or a kill) is `PlayerData.Die`: out for the round, as in vanilla.
- Car prefab `PlayerInGameCar`: speed 450, maxSpeed 45, reverse 335. `maxSpeed` only caps engine force; the
  rigidbody velocity isn't clamped, so launches can be faster. A clean Kit Test run (about 270 m) took 17.1 s.
- Piece galleries (`gallery-<track>.png`) identify the unmapped families:
  - forks: R071/R078/R080/R085 B111 (Y-splits);
  - steep climbs: R107/R108, R063/R064;
  - 14 m drops and climbing turns: R140-R159 (Swing, Dermz city);
  - banked quarter-pipe turns: R067/R068;
  - half-pipes: R162-R166; bowl: R167; tubes: R172-R174;
  - wide curves: R126 (50x50), R139 (70x70).
- Fork connectors: R085B111 enters at +X (y 0..10) and exits at +Y (x -20..-10, a 90-degree branch) and at
  -X (y -20..-10, straight on, two cells over). R071B111 splits into two -X exits 20 m apart; R078/R080 into
  two -X exits about 40 m apart.

## Painting, templates and level select (2026-10-23)
- Painting a tile = `LevelManager.SwapSurfaceLocal(tile, surface, false)` (private, called by reflection), the
  same code a surface-swap card runs, on every machine before the tile activates. The road tile is the
  piece's root; borders are its child tiles. The scenery follows the most common surface that
  `surfaceToIgnoreDuringCount` doesn't skip (asphalt and concrete are skipped there: Biome Blender gets Sand's
  desert scenery).
- Not every track has every piece complete. Aqueduc has no R004B101 road at all (16 loose `Border004B101`
  roots used as scenery), only bare R009/R021, and uses many R001B101 without border children (their
  "borders" are loose stood-up roots elsewhere). Templates now prefer the copy with the most child tiles;
  the build log says when a type only exists without borders.
- Level select: `LevelSelectInfo` builds our preview ribbon, surface shares and theme in a
  `LevelSelect.BuildUI` prefix. The card (`previewImage`) and thumbnail are transparent pictures on the menu's
  light card: the vanilla style is a navy isometric model with a pale shadow, and a lavender outline.
  `LevelArt` draws both from the route (software rasteriser, no scene needed); `routes/<file>.preview.png` /
  `.thumb.png` override them if present. PreviewRoutes saves `preview-<level>-card/thumb/select.png`.
- The flat green on far road in our renders is the tyre-trail texture: an orthographic camera
  (`TrailCameraShaderValuesAssigner.trailCamera`) covers the vanilla track and the road shader reads it by
  world position; outside it the road draws flat. `SceneFit` recentres it on our road.

## Borrowed pieces and more shapes (2026-10-23)
- `KitLibrary`: a route piece with `"from": "<vanilla level>"` is cloned from that track. When a level that
  needs donors loads, the LoadAsyncScene patch first loads each donor scene (single mode, behind the
  transition), copies the needed pieces under an inactive DontDestroyOnLoad holder, then loads the base scene.
  Once per session per donor. Confirmed in preview: Danibugne and Nice Ice load, their pieces build on Aqueduc.
- Connector heights, from flat neighbours (scratch `heights.py`): R052 2-cell turn, B end 4 m lower; R053 2 m
  lower; R019 4 m higher, R020 2 m higher (2 cells); R022 3 cells +4; R026 4 cells +2. A climbing turn ridden
  the other way is a descending turn the other way (R052: left descends, right climbs).
- Banked turns with a 10 m curved outer wall: R067B101 (Dermz city), R068B101 (Danibugne), 3 cells;
  R069B101 (Danibugne), 4 cells. Flat at both ends; the centre line passes the preview's height checks.
- R063B101/R064B101 are NOT road slopes on Danibugne: there they're the two halves of a 20 m wide sand
  half-pipe (8 m wide banks rising to 10 m walls, with an asphalt border strip where they meet). On other tracks
  (Nice Ice, Dermz city) they may be steep slopes; the kit table's 9 m rise is unverified.
- Segments (`RoadKit.Segments`): several tiles lifted with their exact relative poses from a donor track, with
  entry/exit connectors and a centre-line path. `SandCanyon` is Danibugne's start section: R114 funnel, R007
  pads, R063/R064 half-pipe, a 180-degree turn of R068 (inner) + R069 (outer) twice (both centred on the same
  point, radius 30 at the join), and back out 60 m to the side. Route paint doesn't apply to segments.
- `IceHairpin` is Nice Ice's west hairpin: R063 (asphalt transition, banking up), R152 banked 3-cell turn, two
  R016B020 banked straights, R153 banked turn, R064 transition out. Its floor climbs about 5 m into the middle
  (the straights sit 4 m above the ends) and back down. On Nice Ice, R063/R064 are those banking transitions.
- Magma, confirmed by driving: 3 s on it and you blow up; leaving it resets the timer.
- Forks (Umbrellas): R078B111 is a 2-way Y, 40 m long: stem at (0,5,0), branch ends at (-40,25,0) and
  (-40,-15,0), 40 m apart, flat. R080B111 is a 3-way split (the same plus a centre end at (-40,5,0)); vanilla
  Umbrellas splits with R080 just after its start and joins again with R078. R071B111 is a 20 m split (one end
  measured at (-20,15)). A fork laid backwards is a merge.
- Routes split with `"split": [[left path], [right path]]` and `"merge": {...}` on a fork piece. The planner
  places the merge on the left path's end and refuses the route if the right path doesn't arrive at the
  merge's other end (to 5 cm), saying by how much. `tools/route_sim.py` does the same check.
- `Chute` is Umbrellas' half-pipe gully: R162 funnel, 3 x R163, R165, 3 x R164, R166, 4 x R163: straight,
  150 m, down 25 m. Segments whose donor is the base track are cloned from the loaded scene.
- Cannon (`src/Levels/Cannon.cs`): built in code (tube, bands, carriage, wheels; ToonWhite copies coloured
  through `_Color`), standing on two R001B101 tiles. A trigger just inside the breech (on the Obstacle layer:
  a trigger on Default never fired for the car) sets the local owner's car
  velocity along the barrel (`speed`, `pitch`, `aim`), like GoodCards' launch zones. The trigger covers only
  the 3 m straight in front of the opening, from 1.5 m before the breech, so the car is launched before it
  reaches the bore's lip and a car at the side isn't pulled in. Solid parts are BoxColliders (16 slats round the
  open bore, cheeks, wheels, base; no `MeshCollider.sharedMesh` setter for a mesh collider), each on
  `RoadCombined` with a twin on `Default`. Boxes on Default alone were driven straight through: the car's body
  ignores Default (as do its triggers) and collides with RoadCombined, like the road; Default is only for its
  floor ray, which falls back to the default surface on colliders without `SurfaceInfo`.
- Background scenery is six shared biome prefabs (`AsphaltEnvironmentBiomeVisuals`, Dirt, Ice, Magma, Mud,
  Sand), the same on every track (`[Debug] DumpBiomes` lists them to biomes-<track>.txt). `LevelDefinition.Init`
  instantiates all six at `environmentVisualRoot` (0,0,0 on every track), hidden, and shows the one matching the
  surface on most tiles; a surface with no biome (GoodCards' Reverse) leaves none showing. Their props are 400 m
  to 4 km out, a horizon; nothing stands near the road on vanilla tracks either. The road kit has no props.
- Route `scenery` (`src/Levels/Scenery.cs`): clones biome props (colliders removed) into a "MoreMaps Scenery"
  root after InitLevel, 30-100 m off the road, foot 6 m under the fog plane, random yaw, laid out by a
  System.Random seeded from the route name (same on every machine, checked across runs). Props that read well:
  Asphalt/CitySkyScraper (scaffold tower, 140x500 m, scale ~0.25), Asphalt/CityCrane (~0.3),
  Asphalt/CitySkyScraper_003 (~0.2), Sand/SandArch, Sand/SandStone_009, Sand/Cactus_008 or _015,
  Ice/Iceberg_003 and _010 (~0.3), Dirt/Tree_007, Dirt/Moai, Magma/CarSkin_Accessory_Tank_002 (a tank).
  Ice/Cube.011 is a bare grey slab.
- Sound: the game routes audio through `Sounds.SplitScreenAudio.SplitScreenAudioManager` (`PlayClipAtPoint`,
  `PlayStaticClip`; needs a reference to `UnityEngine.AudioModule`). The cannon plays the car's
  `CarEffects.deathAudioClipCollection` (its explosion) at 0.7 pitch at the trigger, on every machine whose
  copy of a car passes through it; only the owner launches the car. PreviewRoutes traces the shot as a point under the map's gravity and logs where it comes back down.
- Mesh building: draw each face once, wound outward (a face drawn both ways round makes RecalculateNormals
  average to nothing: black, flickering toon shading), and never let two surfaces coincide.
- `lanes` on a straight lays copies side by side 10 m apart (a wide deck from borderless R001B000).
- PreviewRoutes' "nothing under" at a segment's apex was a ray landing exactly on a seam between two tiles
  (it depends on the segment's rotation); the check now looks again 5 cm aside before calling it a hole.
- `startRise` (route): the cursor starts that much higher, and TrackBuilder moves the start line, the start cap
  and the forbidden zones within 10 m of the old start up by the same amount. The cap (and its border, which
  is the rounded start pad with its rail) is static-batched: moving it moved its collider but left it drawn at
  the old height (an invisible start in game), so a lifted cap gets its own mesh and renderer like a clone. The fog plane (and its death
  height) stays where it is, so a route can go lower than the vanilla start.
- `[Debug] Snapshot` ("Track: x,y,z; ...") renders spots of a vanilla track with its scenery: the way to see
  what a group of tiles looks like before lifting it.
- Danibugne's banked walls are borders with the Sand surface; paint borders to match the base track.
- Driven: mud (0.3 accel, 0.3 friction) can't be climbed; it kills your speed. Use it flat or downhill.
- Driven: on ice (friction 0.05) you can't turn at the bottom of a downslope. Give ice slopes a straight
  runout, and make ice corners banked (the wall catches a sliding car) or put them where you arrive slowly.
- Driven: a gap at the top of a climb can't be seen coming. Put jumps where the take-off is visible (a kicker
  on the flat) or use a slope piece laid downhill instead.

## MapKitLoader / MegaStunt (LesAmisDeIronWings, read 2026-10-23)
- Same idea as ours: a copy of a stock `Level` appended in `LevelManager.Awake` (and later, after its bundles
  load), the host scene's road hidden in `sceneLoaded`, start/finish/podium/no-trap zones moved, tile ids
  from an MD5 of map id + path, the road painted with the host's asphalt/concrete materials so surface cards
  work. No network sync: everyone needs the same maps, like us.
- The difference: their road is custom geometry from a Unity AssetBundle made with their Map Kit (Unity
  2022.3.62f3 project): smooth road meshes, loops (the game's `GravityShapeSimpleSphere`), boost pads
  (`AccelerationZone`), decor. Ours clones the game's own pieces from JSON with no Unity step.
- Adopted from it: reload the scene when `SceneManager.LoadAsyncScene` would skip it (same scene already
  active, custom level involved); skip `PlayerGhostUtils.LoadGameGhost(string, ...)` for our names (no
  developer ghost in Addressables); after `LevelManager.InitLevel`, hide biome scenery meshes over the road
  and fit the trails camera (`SceneFit`).
- Running both: no conflict (each only acts on its own levels). Their levels register after their bundles
  load (a coroutine), ours in `LevelManager.Awake`, so the list order can differ between machines. Selection
  highlights by index but loading goes by name, so a mismatch would only highlight the wrong card.

## Stripped Unity API found here (in addition to GoodCards' list)
Missing: `Vector3.ToString(string)` (use string interpolation with a format), `Texture2D.EncodeToPNG` /
`ImageConversion.EncodeToPNG` (only `EncodeNativeArrayToPNG`, `LoadImage` exist; we write PNGs ourselves),
`Physics.SyncTransforms`, `Physics.GetIgnoreLayerCollision`, `Bounds.Intersects`, the getters of `Renderer.shadowCastingMode`,
`receiveShadows`, `renderingLayerMask` (setters exist), the setters of `Camera.clearFlags`, `Light.type` and `RenderSettings.fog`, the getters of
`RenderSettings.fogStartDistance`/`fogEndDistance`, `ImageConversion.EncodeToPNG`.
Present: `Mesh.isReadable`, `Mesh.vertices`, `MeshFilter.sharedMesh` setter, `Renderer.materials` setter,
`Object.DestroyImmediate`, `Camera.Render`, `RenderTexture`, `Texture2D.ReadPixels`/`GetPixels32`,
`Physics.Raycast`/`RaycastAll`, `Collider.bounds`, `Collider.excludeLayers`, `Resources.FindObjectsOfTypeAll`.

## Open
- Not yet driven: physics on the cloned road (seams between MeshColliders, the RoadCombined copies), surface
  swaps on cloned tiles, obstacle placement, the finish, online play. See README's test list.
- Static-batch fix: does a fresh MeshRenderer lose anything (lightmaps, probes)? The preview renders look
  right.
- Checkpoints: none on vanilla tracks, so none here. A loop track would need one (a non-network `CheckPoint`
  needs a `SplitScreenObject`); point-to-point routes don't.
