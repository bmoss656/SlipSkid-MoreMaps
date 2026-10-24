# SlipSkid-MoreMaps

Custom tracks for Slip & Skid (BepInEx 6 plugin). Published on Thunderstore as MoreMaps.

A custom track is a copy of a vanilla level: same scene, scenery, lighting and music, with the vanilla road
hidden and a new road laid out from the scene's own road pieces, described in a small JSON file in `routes/`.

Levels (the release ships these four; the prototype **Kit Test**, on Pizza pizza, is in `tools/dev-routes/`:
copy it into `routes/` to try it):
- **Biome Blender** (on Aqueduc): about 1 km through sand (with Danibugne's sand canyon hairpin), a mud bog,
  dirt banked corners, an asphalt climb, an ice descent into Nice Ice's banked hairpin, and an 80 m magma
  sprint, with two jumps. Don't stop on the magma: 3 seconds on it
  burns you out.
- **Choose Wisely** (on Umbrellas): two forks. First: a half-pipe chute, a jump and an icy run-out, or a
  longer, grippy detour. Then: a short icy run with two jumps and an icy S-bend, or a long, easy detour. 640 to
  870 m depending on your choices.
- **Kill Me** (on Taiga): about 670 m of ice and reversed-steering road woven together, with banked ice corners,
  a tight reversed chicane and a straight that flips between ice and Reverse every 10 m. Reverse comes from
  the Good Cards mod; without it those tiles are ice.
- **The Cannon** (on Core 7 meltdown): drive straight into a giant cannon that fires you 130 m out and 20 m
  down into a half-pipe, then loop round through a corner, magma, a jump, Danibugne's sand canyon and an
  S-bend, and back onto the cannon's line for Umbrellas' chute down to a finish you could see from the barrel.

Every player in a lobby needs the mod with the same route files. There is no check for that yet: a player
without them makes the lobby hang when the host picks a custom track.

## Route files

```json
{
  "name": "Kit Test",
  "baseLevel": "Pizza pizza",
  "difficulty": 1,
  "pieces": [
    { "type": "R001B101", "count": 3 },
    { "type": "R021B101", "turn": "right" },
    { "type": "R012B101", "slope": "down" },
    { "type": "R000B101", "finish": true }
  ]
}
```

Optional fields: `surface` / `borders` (on the route or a piece) paint the road or its borders: `Asphalt`,
`Concrete`, `Dirt`, `Ice`, `Mud`, `Sand`, `Magma`, or `Reverse` (needs GoodCards; ice without it).
`{ "type": "gap", "drop": 4 }` leaves a 10 m gap to jump, landing 4 m lower.
`"from": "Danibugne"` on a piece borrows it from another track (loaded once, during the level's loading
screen).

Piece types: `R001` straight (10 m), `R017`/`R018`/`R021`/`R024` 90-degree turns over 1-4 cells,
`R019`/`R020`/`R022`/`R026`/`R052`/`R053` climbing or descending turns, `R067`/`R068`/`R069` banked turns,
`R003`/`R004` (1 cell) and `R009`/`R012`/`R013` (2 cells) slopes, `R000` start/finish cap.
Forks: `{ "type": "R078B111", "split": [[...left path...], [...right path...]], "merge": { "type": "R078B111" } }`.
The two paths start 40 m apart and must end exactly 40 m apart side by side, facing the same way, at the same
height (`tools/route_sim.py` checks it).

`{ "type": "Cannon", "speed": 72, "pitch": 22, "aim": 0 }`: a cannon on 20 m of road (launch speed in m/s,
degrees up, degrees to the right). `"lanes": 3` on a straight lays three side by side (a wide deck).
`"scenery": [{ "prop": "Sand/SandArch", "count": 2, "scale": 0.8 }]` on the route stands a few of the game's
background props (from its Asphalt, Dirt, Ice, Magma, Mud or Sand scenery) off to the sides of the road;
docs/notes.md lists some that look good.
`"startRise": 35` on the route lifts the start (line, cap and spawn) 35 m, to make room for a route that goes
down; every track's fog kills about 21 m below its start.

Segments (several tiles as laid out on another track), with `"turn": "left"` or `"right"`: `SandCanyon`,
Danibugne's walled sand hairpin (comes out 60 m to that side); `IceHairpin`, Nice Ice's banked ice hairpin
(70 m); `Chute`, Umbrellas' half-pipe gully (straight, 150 m, down 25 m). Use the `B101` variant
(borders on both sides). The base level's scene must contain that type; docs/notes.md lists which tracks
have which.

Level select shows each route's own spinning model, picture and outline, all drawn from the route file.
To use your own pictures instead, put `routes/<file>.preview.png` (1024x512) and `.thumb.png` (512x512,
both transparent) next to the route.
