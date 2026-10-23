using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SlipSkid.MoreMaps;

// The vanilla road kit's geometry, worked out from the kit dumps (docs/road-kit.md).
// Every tile is a Blender export: rotation (270, yaw, 0), scale 100, pivot at a corner of its footprint, on
// the road surface. In the tile's own frame (metres, before the scale) the footprint is x in [-length, 0],
// y in [0, width] and z is up; a 10 m grid cell holds one lane. Each piece has two connectors A and B, given
// as a point on the road's centre line at the footprint's edge plus the outward direction.
internal sealed class PieceShape
{
    public Vector3 A, AOut, B, BOut;
}

// A run of several vanilla tiles lifted as one piece, exactly as laid out on its donor track. Poses and
// connectors are in the donor's world space; A/B are on the road surface at the centre line, with the
// outward direction, and Path runs along the centre line from A to B.
internal sealed class Segment
{
    public string Donor;
    public (string Code, Vector3 Pos, float Yaw)[] Parts;
    public Vector3 A, AOut, B, BOut;
    public Vector3[] Path;
    public string Road = "Asphalt", Walls = "Concrete";   // what it looks like, for the level-select model
}

// A Y-junction: one stem, two branches 40 m apart at the far end, flat. Used forwards to split the road and
// backwards to join it. In the tile frame (metres): stem at Stem (outward +X), branch ends at Left/Right
// (outward -X). Driving in at the stem, Left is on your left.
internal sealed class ForkShape
{
    public Vector3 Stem, Left, Right;
}

internal static class RoadKit
{
    // R078B111 (Umbrellas, Nice Ice): 40 m long.
    private static readonly Dictionary<string, ForkShape> Forks = new()
    {
        ["R078"] = new ForkShape { Stem = new Vector3(0f, 5f, 0f), Left = new Vector3(-40f, 25f, 0f), Right = new Vector3(-40f, -15f, 0f) },
    };

    internal static ForkShape Fork(string code) =>
        code != null && code.Length >= 4 && Forks.TryGetValue(code.Substring(0, 4), out var f) ? f : null;

    private static PieceShape Straight(float length, float rise = 0f) => new()
    {
        // A is the high end of a slope.
        A = new Vector3(-length, 5f, rise), AOut = Vector3.left,
        B = new Vector3(0f, 5f, 0f), BOut = Vector3.right,
    };

    // 90-degree turn over n x n cells, centred on the footprint corner (0, 10n): radius 10n - 5.
    // Entering at A turns left; entering at B turns right. `rise`: how much higher B is than A.
    private static PieceShape Turn(int cells, float rise = 0f) => new()
    {
        A = new Vector3(0f, 5f, 0f), AOut = Vector3.right,
        B = new Vector3(-(10f * cells - 5f), 10f * cells, rise), BOut = Vector3.up,
    };

    // By kit number (the R### part); the B### border variant doesn't change the shape.
    private static readonly Dictionary<string, PieceShape> Shapes = new()
    {
        ["R000"] = Straight(10f),       // start/finish cap
        ["R001"] = Straight(10f),
        ["R003"] = Straight(10f, 2f),
        ["R004"] = Straight(10f, 5f),
        ["R009"] = Straight(20f, 4f),
        ["R012"] = Straight(20f, 5f),
        ["R013"] = Straight(20f, 5f),
        ["R163"] = Straight(10f),       // half-pipe straight (Core 7, Umbrellas, Pizza pizza...): curved walls, flat floor
        ["R063"] = Straight(20f, 9f),   // steep: 9 m over 20 m
        ["R064"] = Straight(20f, 9f),
        ["R017"] = Turn(1),
        ["R018"] = Turn(2),
        ["R021"] = Turn(3),
        ["R024"] = Turn(4),
        ["R019"] = Turn(2, 4f),         // climbing and descending turns (Taiga, Danibugne, Core 7, Aqueduc)
        ["R020"] = Turn(2, 2f),
        ["R022"] = Turn(3, 4f),
        ["R026"] = Turn(4, 2f),
        ["R052"] = Turn(2, -4f),
        ["R053"] = Turn(2, -2f),
        ["R067"] = Turn(3),             // banked turns with a curved 10 m wall outside (Dermz city, Danibugne)
        ["R068"] = Turn(3),
        ["R069"] = Turn(4),
    };

    private static readonly Dictionary<string, Segment> Segments = new(System.StringComparer.OrdinalIgnoreCase)
    {
        // Danibugne's sand canyon hairpin, just after its start: a funnel into a 20 m wide sand half-pipe with an
        // asphalt strip down the middle (R063/R064 halves), a 180-degree turn made of the banked R068 (inner) and
        // R069 (outer) halves twice, and back out 60 m to the side. Entered at A it turns left, at B right.
        ["SandCanyon"] = new Segment
        {
            Donor = "Danibugne",
            Parts = new (string, Vector3, float)[]
            {
                ("R114B101", new Vector3(-20f, 0f, -20f), 0f),
                ("R007B101", new Vector3(-30f, 0f, -20f), 0f),
                ("R007B101", new Vector3(-50f, 0f, -30f), 180f),
                ("R063B101", new Vector3(-40f, 0f, -15f), 0f),
                ("R064B101", new Vector3(-40f, 0f, -25f), 0f),
                ("R068B101", new Vector3(-60f, 0f, -25f), 0f),
                ("R069B101", new Vector3(-60f, 0f, -15f), 0f),
                ("R068B101", new Vector3(-90f, 0f, -55f), 270f),
                ("R069B101", new Vector3(-100f, 0f, -55f), 270f),
                ("R063B101", new Vector3(-40f, 0f, -75f), 0f),
                ("R064B101", new Vector3(-40f, 0f, -85f), 0f),
                ("R007B101", new Vector3(-50f, 0f, -90f), 180f),
                ("R007B101", new Vector3(-30f, 0f, -80f), 0f),
                ("R114B101", new Vector3(-20f, 0f, -80f), 0f),
            },
            A = new Vector3(-20f, 0f, -25f), AOut = Vector3.right,
            B = new Vector3(-20f, 0f, -85f), BOut = Vector3.right,
            Path = HairpinPath(new Vector3(-20f, 0f, -25f), new Vector3(-60f, 0f, -55f), 30f),
            Walls = "Sand",
        },
        // Nice Ice's west hairpin: an asphalt transition banking up (R063), a banked 3-cell turn (R152), two banked
        // straights (R016B020), a banked turn back (R153) and an asphalt transition out (R064). Ice with tall concrete
        // walls outside; comes out 70 m to the side.
        ["IceHairpin"] = new Segment
        {
            Donor = "Nice Ice",
            Parts = new (string, Vector3, float)[]
            {
                ("R063B101", new Vector3(-40f, -4f, 0f), 0f),
                ("R152B101", new Vector3(-60f, -4f, 0f), 0f),
                ("R016B020", new Vector3(-80f, 0f, -30f), 0f),
                ("R016B020", new Vector3(-80f, 0f, -40f), 0f),
                ("R153B101", new Vector3(-90f, 0f, -50f), 270f),
                ("R064B101", new Vector3(-40f, -4f, -70f), 0f),
            },
            A = new Vector3(-40f, -4f, -5f), AOut = Vector3.right,
            B = new Vector3(-40f, -4f, -75f), BOut = Vector3.right,
            // The floor rises about 5 m round the first turn to the banked straight and drops back after it.
            Path = Join(new[] { new Vector3(-40f, -4f, -5f) },
                Arc(new Vector3(-60f, -4f, -30f), 25f, 90f, 180f, 0.8f),
                Arc(new Vector3(-60f, 0.8f, -50f), 25f, 180f, 270f, -4f),
                new[] { new Vector3(-40f, -4f, -75f) }),
            Road = "Ice",
        },
        // Umbrellas' half-pipe chute: a funnel (R162), three straight half-pipe pieces (R163), then down 25 m (R165,
        // three R164, R166) and four more straight pieces. Straight, 150 m. "turn" doesn't apply.
        ["Chute"] = new Segment
        {
            Donor = "Umbrellas",
            Parts = new (string, Vector3, float)[]
            {
                ("R162B101", new Vector3(190f, 55f, -63f), 180f),
                ("R163B101", new Vector3(210f, 55f, -53f), 0f),
                ("R163B101", new Vector3(220f, 55f, -53f), 0f),
                ("R163B101", new Vector3(230f, 55f, -53f), 0f),
                ("R165B101", new Vector3(250f, 50f, -53f), 0f),
                ("R164B101", new Vector3(260f, 45f, -53f), 0f),
                ("R164B101", new Vector3(270f, 40f, -53f), 0f),
                ("R164B101", new Vector3(280f, 35f, -53f), 0f),
                ("R166B101", new Vector3(300f, 30f, -53f), 0f),
                ("R163B101", new Vector3(310f, 30f, -53f), 0f),
                ("R163B101", new Vector3(320f, 30f, -53f), 0f),
                ("R163B101", new Vector3(330f, 30f, -53f), 0f),
                ("R163B101", new Vector3(340f, 30f, -53f), 0f),
            },
            A = new Vector3(190f, 55f, -58f), AOut = Vector3.left,
            B = new Vector3(340f, 30f, -58f), BOut = Vector3.right,
            Path = new[]
            {
                new Vector3(190f, 55f, -58f), new Vector3(230f, 55f, -58f), new Vector3(240f, 54f, -58f), new Vector3(250f, 50f, -58f),
                new Vector3(260f, 45f, -58f), new Vector3(270f, 40f, -58f), new Vector3(280f, 35f, -58f), new Vector3(290f, 31f, -58f),
                new Vector3(300f, 30f, -58f), new Vector3(340f, 30f, -58f),
            },
            Walls = "Mud",
        },
    };

    // Points on an arc round `centre`, angles in degrees measured in the XZ plane from +X towards +Z, going from
    // the centre's height to `endY` (eased).
    private static Vector3[] Arc(Vector3 centre, float radius, float from, float to, float endY)
    {
        var points = new List<Vector3>();
        for (var i = 0; i <= 9; i++)
        {
            var t = i / 9f;
            var a = Mathf.Lerp(from, to, t) * (3.14159265f / 180f);
            var p = centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
            p.y = Mathf.Lerp(centre.y, endY, t * t * (3f - 2f * t));
            points.Add(p);
        }
        return points.ToArray();
    }

    private static Vector3[] Join(params Vector3[][] parts) => parts.SelectMany(p => p).ToArray();

    // Straight in, half circle round `centre`, straight back out.
    private static Vector3[] HairpinPath(Vector3 start, Vector3 centre, float radius)
    {
        var points = new List<Vector3> { start };
        for (var i = 0; i <= 18; i++)
        {
            var a = i * 10f * (3.14159265f / 180f);
            points.Add(centre + new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)) * radius);
        }
        points.Add(new Vector3(start.x, start.y, 2f * centre.z - start.z));
        return points.ToArray();
    }

    internal static Segment SegmentFor(string type) =>
        type != null && Segments.TryGetValue(type, out var s) ? s : null;

    internal static PieceShape Shape(string code) =>
        code != null && code.Length >= 4 && Shapes.TryGetValue(code.Substring(0, 4), out var s) ? s : null;

    internal static bool IsTurn(string code) => Shape(code) is { } s && s.BOut == Vector3.up;

    // Tile-frame vector to world, for a tile with yaw `yaw`.
    internal static Vector3 ToWorld(float yaw, Vector3 local) => Quaternion.Euler(270f, yaw, 0f) * local;

    internal static float Yaw(Vector3 v) => Mathf.Atan2(v.x, v.z) * (180f / 3.14159265f);

    internal static float Snap(float degrees, float step = 90f) =>
        ((Mathf.Round(degrees / step) * step) % 360f + 360f) % 360f;

    internal static Vector3 Round(Vector3 v) =>
        new(Mathf.Round(v.x * 100f) / 100f, Mathf.Round(v.y * 100f) / 100f, Mathf.Round(v.z * 100f) / 100f);
}
