#pragma warning disable CS0649 // set by Newtonsoft.Json

using System.Collections.Generic;

namespace SlipSkid.MoreMaps;

// One custom level, read from routes/<file>.json. Field names match the JSON.
internal sealed class RouteData
{
    public string name;        // level name: unique, shown in level select, and how clients find the level
    public string baseLevel;   // vanilla Level.name or sceneName whose scene (and road kit) we reuse
    public int difficulty;
    public string surface;     // paint every piece's road with this surface (a piece's own `surface` wins)
    public string borders;     // and every piece's borders
    public float startRise;    // lift the vanilla start (line, spawns, cap) this many metres before the route
    public List<PieceData> pieces = new();
    public List<SceneryData> scenery = new();   // background props borrowed from the biomes, placed off the road

    [Newtonsoft.Json.JsonIgnore] public string file;

    public IEnumerable<PieceData> AllPieces() => System.Linq.Enumerable.SelectMany(pieces, p => p.All());
}

// Copies of one prop from the game's biome scenery, e.g. { "prop": "Sand/SandArch", "count": 2, "scale": 0.5 }.
// `prop` is the biome (Asphalt, Dirt, Ice, Magma, Mud, Sand) and the prop's name in it; docs/notes.md lists some.
internal sealed class SceneryData
{
    public string prop;
    public int count = 1;
    public float scale = 1f;
}

internal sealed class PieceData
{
    public string type;        // road kit code, e.g. "R001B101" (see RoadKit for the shapes we know)
    public int count = 1;      // repeat this piece
    public string turn;        // turns: "left" or "right"
    public string slope;       // slopes: "up" or "down"
    public bool finish;        // put the finish line on this piece (use an R000 cap, last)
    public string surface;     // paint this piece's road: Asphalt, Concrete, Dirt, Ice, Mud, Sand, Magma, Reverse
    public string borders;     // paint its borders too
    public float drop;         // type "gap": how much lower the far side is (metres, over the whole gap)
    public string from;        // take the piece from this vanilla level instead of the base (see KitLibrary)
    public int lanes = 1;      // straights: lay this many side by side (10 m apart, centred), e.g. a wide deck
    public float speed;        // type "Cannon": launch speed (m/s, default 60)
    public float pitch;        // type "Cannon": barrel angle up (degrees, default 25)
    public float aim;          // type "Cannon": barrel angle to the right of the road (degrees)
    public List<List<PieceData>> split;   // fork pieces: the left and right paths
    public PieceData merge;                // fork pieces: the piece that joins the paths again (a fork laid backwards)

    // This piece and everything inside its paths and merge, in build order.
    public IEnumerable<PieceData> All()
    {
        yield return this;
        if (split != null)
            foreach (var path in split)
                foreach (var p in path)
                    foreach (var q in p.All())
                        yield return q;
        if (merge != null)
            yield return merge;
    }
}
