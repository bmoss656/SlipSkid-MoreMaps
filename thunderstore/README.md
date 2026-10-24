# More Maps

**Four new tracks for Slip & Skid**, built from the game's own road pieces: jumps, banked corners, half-pipes,
forks, magma, ice, and one very large cannon. They show up in level select after the game's own tracks, with
their own preview pictures, and work in every game mode like any other track.

> **Everyone in the lobby needs More Maps, and the same version.** Tracks are picked by their place in the
> level list, so a player without the mod (or with a different version) can't load the host's track and the
> lobby gets stuck.

## The tracks

| Track | Built on | Length | What's in it |
|---|---|---|---|
| **Biome Blender** | Aqueduc | about 1 km | Every surface in one lap: a sand canyon hairpin, a jump into a mud bog, banked dirt corners, an asphalt climb, an icy descent into a banked ice hairpin, and a final jump onto 80 m of magma. Don't stop on the magma. |
| **Choose Wisely** | Umbrellas | 640 to 870 m | Two forks. First: a half-pipe chute with a jump and an icy run-out, or a longer grippy detour. Then: a short icy run with two jumps and an icy S-bend, or a long easy detour. Pick well. |
| **Kill Me** | Taiga | about 670 m | Ice and reversed steering woven together: banked ice corners, a reversed chicane, a climb, a banked ice hairpin, and a straight that flips between ice and reverse every 10 m. |
| **The Cannon** | Core 7 meltdown | about 880 m | Drive straight into a giant cannon. It fires you 130 m out and 20 m down into a half-pipe, then the road loops round through magma, a jump, a sand canyon and a chute to a finish you could see from the barrel. |

Magma burns you out after about 3 seconds on it: keep moving.

## Requirements

- [BepInExPack for Slip & Skid](https://thunderstore.io/c/slip-skid/p/SlipSkid_Modding/BepInExPack_SlipSkid/)
  (installed automatically by mod managers).
- Optional: [Good Cards](https://thunderstore.io/c/slip-skid/?q=GoodCards). **Kill Me** uses Good
  Cards' reversed-steering road; without Good Cards those stretches are plain ice. If anyone in the lobby has
  Good Cards, everyone should.

## Install

Use a mod manager (r2modman, Gale or the Thunderstore app), or copy the `BepInEx` folder from the zip into the
game's folder.

## Making your own tracks

Each track is a small JSON file in `BepInEx/plugins/MoreMaps/routes/` listing road pieces in order (straights,
turns, slopes, jumps, forks, a cannon...). See the [GitHub page](https://github.com/bmoss656/SlipSkid-MoreMaps)
for the format. Adding, removing or renaming a route changes the track list, so every player needs the same
files.
