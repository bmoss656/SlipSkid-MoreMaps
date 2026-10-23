using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SlipSkid.MoreMaps;

// Lays a route out piece by piece from a start cursor (a point on the road's centre line, on the surface)
// and a heading, without touching any scene. TrackBuilder uses it on the base scene's start; the
// level-select preview uses it from the origin.
internal static class RoutePlanner
{
    internal sealed class Placed
    {
        public int Index;
        public PieceData Data;
        public bool IsGap;
        public Vector3 Position;   // tile pivot (not for gaps)
        public float Yaw;
        public Vector3 Entry, EntryHeading, Exit, ExitHeading;
        public GameObject Clone, Collision;
        public bool IsCannon;
        public List<GameObject> Extra = new();   // anything else built for the piece (the cannon)
        public List<Part> Parts;   // segments, side-by-side lanes, cannons: their tiles (Position/Yaw/Clone unused)
        public Vector3[] Path;     // segments, forks: the centre line from entry to exit
        public Vector3[] Path2;    // forks: the centre line of the other branch

        // Every scene object this piece made.
        public IEnumerable<GameObject> Objects()
        {
            if (Clone) yield return Clone;
            if (Collision) yield return Collision;
            if (Parts != null)
                foreach (var part in Parts)
                {
                    if (part.Clone) yield return part.Clone;
                    if (part.Collision) yield return part.Collision;
                }
            foreach (var go in Extra)
                if (go) yield return go;
        }
    }

    internal sealed class Part
    {
        public string Code;
        public string From;    // donor level, or null for the base track
        public bool Paint;     // apply the piece's (or route's) paint
        public Vector3 Position;
        public float Yaw;
        public GameObject Clone, Collision;
    }

    internal const string Gap = "gap";

    // Null and an error message if the route uses a piece we don't know, or its branches don't meet.
    internal static List<Placed> Plan(RouteData route, Vector3 cursor, Vector3 heading, out string error)
    {
        var plan = new List<Placed>();
        var index = 0;
        error = PlanList(route.pieces, ref cursor, ref heading, plan, ref index);
        return error == null ? plan : null;
    }

    private static string PlanList(List<PieceData> pieces, ref Vector3 cursor, ref Vector3 heading, List<Placed> plan, ref int index)
    {
        foreach (var piece in pieces)
            for (var i = 0; i < Math.Max(1, piece.count); i++)
            {
                Placed p;
                if (piece.type == Gap)
                {
                    // An empty cell to jump over, landing `drop` metres lower.
                    var exit = RoadKit.Round(cursor + heading * 10f + Vector3.down * (piece.drop / Math.Max(1, piece.count)));
                    p = new Placed { IsGap = true, Entry = cursor, EntryHeading = heading, Exit = exit, ExitHeading = heading };
                }
                else if (RoadKit.SegmentFor(piece.type) is { } segment)
                    p = PlaceSegment(segment, piece, cursor, heading);
                else if (piece.type == "Cannon")
                    p = PlaceCannon(piece, cursor, heading);
                else if (piece.split != null)
                {
                    var error = PlanSplit(piece, ref cursor, ref heading, plan, ref index);
                    if (error != null)
                        return error;
                    continue;
                }
                else
                {
                    var shape = RoadKit.Shape(piece.type);
                    if (shape == null)
                        return $"unknown piece type '{piece.type}'";
                    p = Place(shape, piece, cursor, heading);
                    if (piece.lanes > 1)
                        p.Parts = Lanes(p, piece);
                }
                p.Index = index++;
                p.Data = piece;
                plan.Add(p);
                cursor = p.Exit;
                heading = p.ExitHeading;
            }
        return null;
    }

    // A fork, its two paths, and the merge that joins them: the merge goes on the end of the left path, and the
    // right path must arrive exactly at the merge's other end.
    private static string PlanSplit(PieceData piece, ref Vector3 cursor, ref Vector3 heading, List<Placed> plan, ref int index)
    {
        var fork = RoadKit.Fork(piece.type);
        var merge = piece.merge != null ? RoadKit.Fork(piece.merge.type) : null;
        if (fork == null || merge == null || piece.split.Count != 2)
            return $"'{piece.type}': a split needs a fork piece, two paths and a merge fork piece";

        // Fork: driving in at the stem (inward -X in the tile frame).
        var yaw = RoadKit.Snap(RoadKit.Yaw(heading) - RoadKit.Yaw(RoadKit.ToWorld(0f, Vector3.left)));
        var pos = RoadKit.Round(cursor - RoadKit.ToWorld(yaw, fork.Stem));
        var left = RoadKit.Round(pos + RoadKit.ToWorld(yaw, fork.Left));
        var right = RoadKit.Round(pos + RoadKit.ToWorld(yaw, fork.Right));
        plan.Add(new Placed
        {
            Index = index++, Data = piece, Position = pos, Yaw = yaw,
            Entry = cursor, EntryHeading = heading, Exit = left, ExitHeading = heading,
            Path = Junction(cursor, heading, left), Path2 = Junction(cursor, heading, right),
        });

        var lc = left;
        var lh = heading;
        var error = PlanList(piece.split[0], ref lc, ref lh, plan, ref index);
        if (error != null)
            return error;
        var rc = right;
        var rh = heading;
        error = PlanList(piece.split[1], ref rc, ref rh, plan, ref index);
        if (error != null)
            return error;

        // Merge: the fork laid backwards. Driving into it the ends are mirrored: the left path arrives at the
        // merge's Right end and the right path at its Left end.
        if (Vector3.Dot(lh, rh) < 0.99f)
            return $"split paths end facing different ways ({lh} vs {rh})";
        var myaw = RoadKit.Snap(RoadKit.Yaw(lh) - RoadKit.Yaw(RoadKit.ToWorld(0f, Vector3.right)));
        var mpos = RoadKit.Round(lc - RoadKit.ToWorld(myaw, merge.Right));
        var other = RoadKit.Round(mpos + RoadKit.ToWorld(myaw, merge.Left));
        if ((other - rc).magnitude > 0.05f)
            return $"split paths don't meet: the right path ends at {rc:F1}, the merge needs {other:F1} (off by {rc - other:F1})";
        var stem = RoadKit.Round(mpos + RoadKit.ToWorld(myaw, merge.Stem));
        plan.Add(new Placed
        {
            Index = index++, Data = piece.merge, Position = mpos, Yaw = myaw,
            Entry = lc, EntryHeading = lh, Exit = stem, ExitHeading = lh,
            Path = Enumerable.Reverse(Junction(stem, -lh, lc)).ToArray(), Path2 = Enumerable.Reverse(Junction(stem, -lh, rc)).ToArray(),
        });
        cursor = stem;
        heading = lh;
        return null;
    }

    // An S from the stem to a branch end 40 m ahead and 20 m to the side.
    private static Vector3[] Junction(Vector3 stem, Vector3 heading, Vector3 end)
    {
        var a = stem + heading * 15f;
        var b = end - heading * 15f;
        var points = new Vector3[13];
        for (var i = 0; i <= 12; i++)
        {
            var t = i / 12f;
            var u = 1f - t;
            points[i] = u * u * u * stem + 3f * u * u * t * a + 3f * u * t * t * b + t * t * t * end;
        }
        return points;
    }

    // Where a piece goes when its entry connector meets the cursor.
    private static Placed Place(PieceShape shape, PieceData piece, Vector3 cursor, Vector3 heading)
    {
        // Turns: A turns left, B turns right. Slopes: B is the low end, so climbing enters at B.
        var enterB = RoadKit.IsTurn(piece.type) ? piece.turn == "right" : piece.slope == "up";
        var (e, eOut, x, xOut) = enterB ? (shape.B, shape.BOut, shape.A, shape.AOut) : (shape.A, shape.AOut, shape.B, shape.BOut);
        // Yaw that turns the inward direction at the entry (-eOut) onto the heading.
        var yaw = RoadKit.Snap(RoadKit.Yaw(heading) - RoadKit.Yaw(RoadKit.ToWorld(0f, -eOut)));
        var pos = RoadKit.Round(cursor - RoadKit.ToWorld(yaw, e));
        return new Placed
        {
            Position = pos,
            Yaw = yaw,
            Entry = cursor,
            EntryHeading = heading,
            Exit = RoadKit.Round(pos + RoadKit.ToWorld(yaw, x)),
            ExitHeading = Flat(RoadKit.ToWorld(yaw, xOut)),
        };
    }

    // Copies of a straight side by side, 10 m apart, centred on the route.
    private static List<Part> Lanes(Placed p, PieceData piece)
    {
        var right = Vector3.Cross(Vector3.up, p.EntryHeading).normalized;
        var parts = new List<Part>();
        for (var k = 0; k < piece.lanes; k++)
        {
            var offset = (k - (piece.lanes - 1) / 2f) * 10f;
            parts.Add(new Part { Code = piece.type, From = piece.from, Paint = true, Position = RoadKit.Round(p.Position + right * offset), Yaw = p.Yaw });
        }
        return parts;
    }

    // A cannon: two straight road tiles (R001B101 from the base track) with the cannon on top.
    private static Placed PlaceCannon(PieceData piece, Vector3 cursor, Vector3 heading)
    {
        var road = new PieceData { type = "R001B101" };
        var shape = RoadKit.Shape(road.type);
        var a = Place(shape, road, cursor, heading);
        var b = Place(shape, road, a.Exit, a.ExitHeading);
        return new Placed
        {
            IsCannon = true,
            Entry = cursor, EntryHeading = heading, Exit = b.Exit, ExitHeading = b.ExitHeading,
            Parts = new List<Part>
            {
                new() { Code = road.type, Paint = true, Position = a.Position, Yaw = a.Yaw },
                new() { Code = road.type, Paint = true, Position = b.Position, Yaw = b.Yaw },
            },
        };
    }

    // A segment turned and moved so its entry meets the cursor. "right" enters at B.
    private static Placed PlaceSegment(Segment seg, PieceData piece, Vector3 cursor, Vector3 heading)
    {
        var enterB = piece.turn == "right";
        var (e, eOut, x, xOut) = enterB ? (seg.B, seg.BOut, seg.A, seg.AOut) : (seg.A, seg.AOut, seg.B, seg.BOut);
        var path = enterB ? Enumerable.Reverse(seg.Path).ToArray() : seg.Path;
        var turn = RoadKit.Snap(RoadKit.Yaw(heading) - RoadKit.Yaw(-eOut));
        var r = Quaternion.Euler(0f, turn, 0f);
        Vector3 T(Vector3 v) => cursor + r * (v - e);
        return new Placed
        {
            Entry = cursor,
            EntryHeading = heading,
            Exit = RoadKit.Round(T(x)),
            ExitHeading = Flat(r * xOut),
            Path = path.Select(T).ToArray(),
            Parts = seg.Parts.Select(part => new Part
            {
                Code = part.Code,
                From = seg.Donor,
                Position = RoadKit.Round(T(part.Pos)),
                Yaw = ((part.Yaw + turn) % 360f + 360f) % 360f,
            }).ToList(),
        };
    }

    // A point on the road's centre line at t (0 = entry, 1 = exit): straight, or a quadratic curve through the
    // corner for a turn (close to the 90-degree arc). Heights ease in and out like the kit's slopes.
    internal static Vector3 CentreLine(Placed p, float t)
    {
        if (p.Path != null)
            return AlongPath(p.Path, t);
        Vector3 point;
        if (Vector3.Dot(p.EntryHeading, p.ExitHeading) > 0.5f)
            point = Vector3.Lerp(p.Entry, p.Exit, t);
        else
        {
            var corner = p.Entry + p.EntryHeading * Vector3.Dot(p.Exit - p.Entry, p.EntryHeading);
            var u = 1f - t;
            point = u * u * p.Entry + 2f * u * t * corner + t * t * p.Exit;
        }
        point.y = Mathf.Lerp(p.Entry.y, p.Exit.y, t * t * (3f - 2f * t));
        return point;
    }

    // The point a fraction t of the way along a polyline, by length.
    // A piece's centre lines: one, or two for a fork or merge (its second branch).
    internal static IEnumerable<Func<float, Vector3>> Lines(Placed p)
    {
        yield return t => CentreLine(p, t);
        if (p.Path2 != null)
            yield return t => AlongPath(p.Path2, t);
    }

    internal static Vector3 AlongPath(Vector3[] path, float t)
    {
        var total = 0f;
        for (var i = 1; i < path.Length; i++)
            total += (path[i] - path[i - 1]).magnitude;
        var target = Mathf.Clamp01(t) * total;
        for (var i = 1; i < path.Length; i++)
        {
            var step = (path[i] - path[i - 1]).magnitude;
            if (target <= step || i == path.Length - 1)
                return Vector3.Lerp(path[i - 1], path[i], step > 0f ? Mathf.Clamp01(target / step) : 0f);
            target -= step;
        }
        return path[path.Length - 1];
    }

    internal static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        // Snap to the grid's four directions.
        var yaw = RoadKit.Snap(RoadKit.Yaw(v));
        return RoadKit.Round(Quaternion.Euler(0f, yaw, 0f) * Vector3.forward);
    }
}
