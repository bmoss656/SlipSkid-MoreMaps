# Map plans (2026-10-23)

Four maps from Brad's ideas, plus the shared features they need. Numbers come from the kit dumps, the piece
galleries (`[Debug] Gallery`) and the Probe run; see notes.md for the kit itself.

## Pace
Kit Test: about 270 m of centre line in 17.1 s on a clean solo run, so about 15.6 m/s on average from a
standing start, with four turns. For 30-50 s, aim for **about 600 m** (500 m on slow surfaces like sand,
mud and ice), then tune from test-drive times. The car's `maxSpeed` 45 only limits the engine; launches
and drops can go faster. Gravity is 40 m/s². Players also have a jump and a dash.

## Surfaces (Probe)
| Surface | friction | accel | sticky | drift | notes |
|---|---|---|---|---|---|
| Asphalt | 0.95 | 1 | 1 | 1 | |
| Concrete | 0.65 | 1 | 1 | 1 | borders |
| Dirt | 0.95 | 1 | 1 | 0.3 | slides sideways |
| Ice | 0.05 | 0.5 | 1 | 1 | almost no grip |
| Mud | 0.3 | 0.3 | 0.5 | 2.5 | slow and boggy |
| Sand | 0.995 | 0.2 | 0.7 | 1 | very slow to accelerate |
| Magma | 0.99 | 1.2 | 0.6 | 3 | fastest; has a BurnModifier (effect still to check) |
| CustomContent_Reverse | 0.65 | 1 | 1 | 1 | GoodCards' reversed steering |

The level's sky and scenery follow the surface with the most tiles (`LevelDefinition.CountTilesSurfaces`),
so a painted map can pick its look through its surface mix.

## Shared work (in build order)
1. **Level-select preview.** Vanilla shows a rotating 3D model (`Level.previewMeshes`: one mesh, a submesh
   per surface, `LevelMeshPreview_<Surface>` materials); with none it falls back to the static picture, which
   is why Kit Test shows Pizza pizza. Fix:
   - Generate a ribbon mesh of the route at registration (road strip plus border lips, following turns and
     heights, a submesh per surface), with a hidden renderer holding the vanilla preview materials.
   - Replace `previewImage` (1024x512) and `thumbnailImage` (512x512) with pictures PreviewRoutes renders into
     `routes/`, loaded with `ImageConversion.LoadImage` (present in the build).
2. **Surface painting.** A piece's `surface` field ("Ice", "Mud"...) paints its road tiles by surface name,
   the way a surface-swap card does, the same on every machine. Optional `borders` surface.
3. **More pieces in RoadKit.** Each one is worked out with `tools/kit_analysis.py`, then checked with
   PreviewRoutes.
   - Kickers: R004, +5 m in 10 m.
   - Steep climbs: R107/R108 (+10 m in 10 m) and R063/R064 (+10 m in 20 m).
   - Big drops and climbs: R140-R149 (14 m over 20 m) and R154/R155/R158/R159 (14 m climbing turns).
   - Banked quarter-pipe turns: R067/R068 (30x30, 10 m walls).
   - Half-pipes: R162-R166.
   - Forks: R071/R078/R080/R085 B111.
4. **Gaps (jumps).** A `gap` piece leaves N empty cells with a height change. PreviewRoutes checks that
   the landing lines up, but jump length can only be tuned by driving. Rough physics: leaving an R004 at
   25 m/s clears 10-20 m onto a road 5 m lower.
5. **Branches.** Routes become a small graph: a fork piece, two piece lists, then a merge (a fork laid
   backwards). The builder refuses a route whose branches don't end exactly on the merge's connectors, and
   says by how much they miss.
6. **Launch zone** (for the cannon): a trigger that sets the car's velocity. It runs only on the car
   owner's machine, like GoodCards' LaunchZone, with the same settings everywhere.
7. **Level list check.** More maps means more chances of a mismatch. The host sends its custom level names;
   a client that lacks one gets a clear warning, and the host can't start that level while that client is
   in the lobby. Needed before sharing with friends.

## Status
- Done: level-select preview (ribbon, pictures, surface shares), surface painting, gaps, borrowed pieces.
- Biome Blender v3 (about 975 m, preview-checked):
  - asphalt start and downhill;
  - sand straights into Danibugne's sand canyon hairpin (`SandCanyon`), then a sand corner;
  - jump 1: Nice Ice kicker over a gap, landing in a mud bog (muddy downslope, then 20 m flat);
  - dirt: two banked corners;
  - a 15 m asphalt climb, then a steep visible ramp down, an asphalt downslope and an asphalt turn;
  - ice: a straight downslope, 20 m flat, Nice Ice's banked hairpin (`IceHairpin`), 20 m flat;
  - jump 2: a kicker over a gap onto an 80 m magma sprint, 85 m to the finish line (30 m was cleared in 1-1.5 s, too easy).
- Driven so far: mud can't be climbed (the climb was mud, then dirt, now asphalt). A drop with no kicker at
  the top of a climb is invisible (replaced by the ramp). v2 (1.1 km) was too long and its ice too hard:
  v3 is shorter, the climb is 15 m instead of 25 m, and the ice has no 1-cell hairpins. Then: a turn at the
  bottom of an icy downslope is impossible (no grip at speed), so slopes into turns are asphalt now.
  Then the ice hairpin turned left instead of right: its exit ran under the asphalt climb, so you could drop
  from the climb onto the magma run. The ending now comes out 70 m west, with nothing above it.

- Choose Wisely v2 (Umbrellas, preview-checked; the first split was driven and liked): start, a Y-fork (R078B111).
  - Left, "the gamble" (241 m): Umbrellas' half-pipe chute down 25 m, a kicker over a gap, 30 m of mud.
  - Right, "the safe way" (317 m): a detour south with four turns and five downhill slopes (also down 25 m).
  - Merge (R078B111 backwards), then a turn south into a second split:
    - right, "fast but hard" (179 m): a 30 m asphalt run-up, then ice, two kicker jumps over gaps, an icy S-bend;
    - left, "slow but easy" (318 m): an asphalt detour with eight turns.
  - Merge, the finish. A driven lap is about 640 m (both quick paths) to 870 m (both safe ones).
  - v2 tweaks: the gamble's mud bog is now ice; the hard path got the S-bend, the easy path 60 m more.

- Kill Me v2 (Taiga, about 670 m, preview-checked). Needs GoodCards for Reverse (ice without).
  1. Asphalt start, an ice straight, a Reverse S-bend.
  2. Ice into a banked corner (Danibugne R068 painted ice), then a tight Reverse chicane (R017s).
  3. A Reverse climb (+10 m), an ice crest, a straight ice downhill and run-out into `IceHairpin`.
  4. A Reverse zigzag (R018 right, right, left), then a "flicker" straight swapping ice and Reverse every 10 m.
  5. A kicker jump onto Reverse and the finish.
  - Driven: the finish was 50 m from the start (a ramp card could jump it), so after the hairpin the end now
    heads east, away from everything: the finish is about 230 m from the start (673 m route).

- The Cannon v5 (Core 7 meltdown, preview-checked, 884 m). Cannon only, no split. Core 7's start is at 0 and
  the fog kills at -21, so instead of climbing at the start the whole start is lifted 35 m (`"startRise": 35`:
  the start line, cap and spawn move up with it), leaving room for the lower second half. The finish is on the
  cannon's line, about 450 m down range, so you can see it from the barrel:
  1. 50 m of road straight into the cannon (72 m/s, 22 degrees up, aimed straight).
  2. An 80 m gap dropping 20 m, then 80 m of Core 7's concrete half-pipe (R163B101) to land in. The trace lands
     the shot 134 m out, 20 m down, in the half-pipe.
  3. A flat 3-cell right (was a banked Danibugne turn; swapped after driving), 40 m of magma, a kicker jump (R004 up, 10 m gap dropping 5 m).
  4. The Danibugne SandCanyon hairpin to the left (back the way you came), a Danibugne S-bend (two R018), 60 m of
     half-pipe and a 3-cell right back onto the cannon's line.
  5. Umbrellas' Chute (150 m, down 25 m) along that line to the finish at about -8.5.
  - The preview warns "nothing under" at the canyon's apex in this orientation: the ray hits the exact seam
    where its R068/R069 halves meet (same tiles had no warning in other orientations). Worth a look in game.
  - v1/v2 driven: the trigger had to move to the Obstacle layer (now fires: "Cannon: fired ... at 75 m/s"), the
    mesh was rebuilt. Asked for: aim straight, thin far landing (v2), lower and a half-pipe landing (v3),
    cannon only, no climb at the start and a longer second half (v4), then more run-up and the finish in line
    with the cannon, the loop curving the other way (v5). v5 driven: the banked corner after the landing became
    a flat R021, the lifted start was invisible (static-batched cap, now redrawn) and the cannon got colliders.

## Map 1: "Biome Blender" (Environmental mash-up)
Other names: "Climate Chaos", "Four Seasons". Base: **Aqueduc** (it has the most shapes: three turn sizes
and three slopes, kickers, descending and climbing curves). About 600 m, five surface zones with asphalt
links:
1. Start, a short asphalt straight, then a downhill (R012) so you hit the sand with speed.
2. **Sand** flats: about 60 m and a wide R021 sweeper. Sand barely accelerates, so carrying speed in matters.
3. **Dirt** chicane: R018 left/right S-bends, where dirt's low side grip makes you slide.
4. Kicker (R004) over a one-cell gap, landing on a downslope into...
5. **Mud** climb: two R012 uphills in mud (0.3 accel), then a crest.
6. **Ice** descent: R009 down into a wide R021 curve, then an R017 hairpin.
7. **Magma** sprint: a 40 m magma straight to the finish (fastest surface, but it burns).

Mostly asphalt links, so the sky stays the base biome. Paint only the road; borders stay concrete.

## Map 2: "Choose Wisely" (branching paths)
Base: **Umbrellas** (forks R071/R078/R080 B111, half-pipes R162-R166, huge curves R126 50x50 and R139 70x70,
kickers and slopes, mud biome). About 600 m on either path:
1. Start and about 80 m of warm-up with two turns.
2. Fork (R078B111): two lanes about 40 m apart.
   - **Left, "the gamble"** (about 200 m): a half-pipe gully, a mud patch, a kicker over a gap. Faster if
     you nail it; a miss drops you in the fog.
   - **Right, "the long way"** (about 280 m): clean asphalt, the 70 m R139 sweeper, the R126 climbing curve.
3. Merge (an R080B111 laid backwards) about 120 m before the finish, then a downhill run to the line.

Balance both branches to within about a second clean, so the choice comes down to risk and to which
branch other players have blocked with obstacles.

## Map 3: "Kill Me" (ice + reverse)
Base: **Taiga** (ice biome, R017/R018 turns, R003/R012 slopes, R045/R089 lane shifts). About 450 m, since ice
is slow to drive well. Short bands of 20-30 m so the controls keep flipping:
1. Ice straight into a **Reverse** chicane (R018 left/right).
2. Ice downhill (R012) into a pair of R017 hairpins on ice.
3. A Reverse S-bend lane shift (R045/R089), an ice crest (R003), then a Reverse hairpin.
4. Final ice straight to the finish. Optional: magma borders for extra punishment.

Needs GoodCards' Reverse surface (`CustomContent_Reverse`). A player without GoodCards gets ice there instead
(a logged warning, and their steering isn't flipped), so everyone should run GoodCards for a fair race.

## Map 4: "The Cannon"
Base: **Core 7 meltdown** (R085B111 fork, R021/R024 turns, banked R067/R068, magma theme). About 650 m the
long way, about 350 m via the cannon.
1. Start, 30 m, fork (R085B111): straight on is the cannon, the side branch is the road.
2. **Cannon**: a ramp (R004) up into the barrel. A trigger at the breech fires you at about 65 m/s, 30 degrees
   up, toward a wide landing deck about 100 m away, but aimed about 10 degrees off. Untouched, you clip the
   deck's edge and fall into the fog.
   - Obstacles make it work: a bouncer or wall in the flight path, a fan, or a barrier along the deck's far
     edge. Obstacles can be placed in mid-air (placement only checks the fog height and the no-place zones).
   - The deck is three lanes of borderless plates, so a corrected shot lands easily.
3. **Long road**: climbing hairpins, a banked magma quarter-pipe turn (R067/R068), back to the deck.
4. Deck and road merge, then about 120 m to the finish.

Clean times: about 40 s the long way, about 22 s with a working cannon.

The cannon model is new geometry. The recommendation is a Blender model (barrel about 16 m long with an 8 m
bore, on a wheeled carriage), built at runtime from JSON like GoodCards' obstacles: port the mesh part of
`DataObstacleBuilder`, with toon materials. A placeholder from vanilla tube pieces (R172-R174) is possible,
but they're small.

## Risks and checks
- Jumps, the cannon shot and drops need test drives to tune; the preview can't simulate the car.
- Magma's BurnModifier: probe what it does before using long magma stretches.
- Half-pipe and drop pieces may use stood-up rotations; each new shape gets connector analysis and a preview.
- Additive loading of other tracks' scenes (to mix pieces from several tracks) is NOT planned: those scenes
  bring a second camera manager, gravity plane and start/finish lines. Each map uses one base track's
  pieces, and painting covers surfaces.
