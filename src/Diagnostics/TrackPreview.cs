using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Levels;
using UnityEngine;
using World;

namespace SlipSkid.MoreMaps;

// [Debug] PreviewRoutes: checks a freshly built route without driving it.
// - Report: samples the centre line of every piece and raycasts down onto it, logging any gap, wrong height or
//   hit on something that isn't one of our tiles, then looks for anything solid across the road at car height.
// - Capture: renders a top-down and two chase-camera views to preview-<level>-*.png next to the DLL.
internal static class TrackPreview
{
    internal static void Report(LevelDefinition def, RouteData route)
    {
        var problems = 0;
        var samples = 0;
        var ours = new HashSet<GameObject>(TrackBuilder.LastBuild.SelectMany(p => p.Objects()));
        // Cannons stand over their own road; their shot is checked by TraceCannon instead.
        var cannons = new HashSet<GameObject>(TrackBuilder.LastBuild.SelectMany(p => p.Extra).Where(g => g));
        foreach (var p in TrackBuilder.LastBuild.Where(p => !p.IsGap))
        foreach (var line in RoutePlanner.Lines(p))
        {
            var paint = p.Data.surface ?? route.surface;
            var surfacePoints = new List<Vector3>();
            // Ends are kept 0.2 m inside the piece: a ray exactly on the seam between two tiles can miss both.
            var count = p.Path != null ? 40 : 8;
            for (var s = 0; s <= count; s++)
            {
                var t = Mathf.Lerp(0.02f, 0.98f, s / (float)count);
                var point = line(t);
                samples++;
                var expectedY = point.y;
                var hits = Hits(point + Vector3.up * 8f, Vector3.down, 20f, cannons);
                // A ray exactly on the seam between two tiles can miss both: look again a few centimetres aside
                // before calling it a hole.
                foreach (var aside in new[] { new Vector3(0.05f, 0f, 0.05f), new Vector3(-0.05f, 0f, -0.05f) })
                    if (hits.Count == 0)
                        hits = Hits(point + aside + Vector3.up * 8f, Vector3.down, 20f, cannons);
                if (hits.Count == 0)
                {
                    problems++;
                    Plugin.Log.LogWarning($"Preview: #{p.Index} {p.Data.type} t={t:F2}: nothing under {point:F1}");
                    continue;
                }
                var hit = hits[0];
                surfacePoints.Add(hit.point);
                var mine = ours.Contains(hit.collider.transform.root.gameObject);
                var surface = hit.collider.GetComponent<SurfaceInfo>();
                // Painted roads: the road tile under the centre line must carry the paint.
                var wrongPaint = paint != null && hit.collider.gameObject == p.Clone &&
                                 !(surface && surface.surface && Surfaces.Matches(surface.surface.name, paint));
                // Slopes ease in and out slightly differently from our curve, hence the tolerance.
                if (!mine || wrongPaint || Mathf.Abs(hit.point.y - expectedY) > 2.5f)
                {
                    problems++;
                    Plugin.Log.LogWarning($"Preview: #{p.Index} {p.Data.type} t={t:F2}: hit {KitDump.Path(hit.collider.transform)} " +
                                          $"(layer {LayerMask.LayerToName(hit.collider.gameObject.layer)}) at y={hit.point.y:F2}, expected ~{expectedY:F2}, " +
                                          $"ours={mine}, surface={(surface && surface.surface ? surface.surface.name : "-")}");
                }
            }
            // Anything solid across the road at car height, following the road surface.
            for (var s = 0; s + 1 < surfacePoints.Count; s++)
            {
                var a = surfacePoints[s] + Vector3.up * 1.2f;
                var b = surfacePoints[s + 1] + Vector3.up * 1.2f;
                foreach (var wall in Hits(a, b - a, (b - a).magnitude, cannons))
                {
                    // The obstacle-free zones are placement-only boxes that cars pass through.
                    if (KitDump.Path(wall.collider.transform).StartsWith("ObstacleDefinition/"))
                        continue;
                    problems++;
                    Plugin.Log.LogWarning($"Preview: #{p.Index} {p.Data.type}: blocked at {wall.point:F1} by {KitDump.Path(wall.collider.transform)}");
                }
            }
        }
        // Gaps: nothing may stick up into the jump (half a metre above the line from take-off to landing).
        foreach (var p in TrackBuilder.LastBuild.Where(p => p.IsGap))
        {
            var a = p.Entry + Vector3.up * 0.5f;
            var b = p.Exit + Vector3.up * 0.5f;
            foreach (var hit in Hits(a, b - a, (b - a).magnitude, cannons))
            {
                if (KitDump.Path(hit.collider.transform).StartsWith("ObstacleDefinition/"))
                    continue;
                problems++;
                Plugin.Log.LogWarning($"Preview: #{p.Index} gap: {KitDump.Path(hit.collider.transform)} in the way at {hit.point:F1}");
            }
        }
        foreach (var p in TrackBuilder.LastBuild.Where(p => p.IsCannon))
            TraceCannon(p);
        Plugin.Log.LogInfo($"Preview: '{route.name}': {samples} centre-line samples, {problems} problems, " +
                           $"length {Length(TrackBuilder.LastBuild):F0} m");
    }

    // Follows a cannon shot as a point under the map's gravity (no drag, no steering) and reports where it lands
    // and how far that is to the side of the road's line.
    private static void TraceCannon(RoutePlanner.Placed p)
    {
        var launch = p.Extra.SelectMany(g => g.GetComponentsInChildren<CannonLaunch>()).FirstOrDefault();
        if (!launch)
            return;
        var gravity = Object.FindObjectOfType<GravityShapeInfinitePlane>() is { } g ? g.gravity : 40f;
        var pos = launch.transform.position;
        var v = launch.velocity;
        var own = new HashSet<GameObject>(p.Objects());
        const float dt = 0.01f;
        var crossed = false;
        for (var t = 0f; t < 6f; t += dt)
        {
            var next = pos + v * dt;
            if (!crossed && v.y < 0f && next.y <= p.Entry.y + 0.5f)
            {
                crossed = true;
                Plugin.Log.LogInfo($"Preview: cannon #{p.Index}: back down at road height after {t:F2} s, " +
                                   $"{Vector3.Dot(next - p.Entry, p.EntryHeading):F0} m along, " +
                                   $"{Vector3.Dot(next - p.Entry, Vector3.Cross(Vector3.up, p.EntryHeading)):F1} m to the right of the road's line");
            }
            v += Vector3.down * gravity * dt;
            var hit = Physics.RaycastAll(pos, next - pos, (next - pos).magnitude, ~0, QueryTriggerInteraction.Ignore)
                .Where(h => !own.Contains(h.collider.transform.root.gameObject)).OrderBy(h => h.distance).FirstOrDefault();
            if (hit.collider)
            {
                var along = Vector3.Dot(hit.point - p.Entry, p.EntryHeading);
                var side = Vector3.Dot(hit.point - p.Entry, Vector3.Cross(Vector3.up, p.EntryHeading));
                Plugin.Log.LogInfo($"Preview: cannon #{p.Index}: lands on {KitDump.Path(hit.collider.transform)} after {t:F2} s, " +
                                   $"{along:F0} m along the road from the cannon, {side:F1} m to the {(side >= 0 ? "right" : "left")}, " +
                                   $"{hit.point.y - p.Entry.y:F1} m below the cannon's road");
                return;
            }
            pos = next;
            if (pos.y < p.Entry.y - 60f)
                break;
        }
        var a2 = Vector3.Dot(pos - p.Entry, p.EntryHeading);
        var s2 = Vector3.Dot(pos - p.Entry, Vector3.Cross(Vector3.up, p.EntryHeading));
        Plugin.Log.LogInfo($"Preview: cannon #{p.Index}: hits nothing and falls (passing {a2:F0} m along, {s2:F1} m to the side)");
    }

    // Everything a ray hits, nearest first, except colliders under the given roots.
    private static List<RaycastHit> Hits(Vector3 from, Vector3 direction, float distance, HashSet<GameObject> skip) =>
        Physics.RaycastAll(from, direction, distance, ~0, QueryTriggerInteraction.Ignore)
            .Where(h => !skip.Contains(h.collider.transform.root.gameObject)).OrderBy(h => h.distance).ToList();

    internal static float Length(List<RoutePlanner.Placed> plan)
    {
        var total = 0f;
        foreach (var p in plan)
            for (var s = 0; s < 16; s++)
                total += (RoutePlanner.CentreLine(p, (s + 1) / 16f) - RoutePlanner.CentreLine(p, s / 16f)).magnitude;
        return total;
    }

    internal static IEnumerator Capture(LevelDefinition def, RouteData route)
    {
        var build = TrackBuilder.LastBuild;
        if (build.Count == 0)
            yield break;
        // Placement-mode boxes are visible outside a match; hide them for the pictures.
        var obstacleDef = Object.FindObjectOfType<Obstacles.ObstacleLevelDefinition>();
        if (obstacleDef)
            obstacleDef.DisableObstaclePlacementMode();

        var min = build.Select(p => p.Entry).Aggregate(Vector3.Min);
        var max = build.Select(p => p.Entry).Aggregate(Vector3.Max);
        foreach (var p in build)
        {
            min = Vector3.Min(min, p.Exit);
            max = Vector3.Max(max, p.Exit);
        }
        var centre = (min + max) / 2f;
        var size = Mathf.Max(max.x - min.x, max.z - min.z) / 2f + 30f;

        var go = new GameObject("MoreMapsPreviewCamera");
        var cam = go.AddComponent<Camera>();
        cam.farClipPlane = 3000f;

        cam.orthographic = true;
        cam.orthographicSize = size;
        go.transform.SetPositionAndRotation(centre + Vector3.up * 300f, Quaternion.Euler(90f, 0f, 0f));
        yield return Render(cam, 1280, 1280, Path.Combine(Plugin.Dir, $"preview-{route.name}-top.png"));

        cam.orthographic = false;
        cam.fieldOfView = 60f;
        foreach (var (p, label) in new[] { (build[0], "start"), (build[build.Count / 3], "third"), (build[2 * build.Count / 3], "twothirds"), (build[build.Count - 3], "end") })
        {
            go.transform.position = p.Entry - p.EntryHeading * 25f + Vector3.up * 14f;
            go.transform.rotation = Quaternion.LookRotation(p.Entry + p.EntryHeading * 30f - go.transform.position);
            yield return Render(cam, 960, 960, Path.Combine(Plugin.Dir, $"preview-{route.name}-{label}.png"));
        }
        // The start from the front, looking back at the gate and the floor behind it.
        go.transform.position = build[0].Entry + build[0].EntryHeading * 15f + Vector3.up * 9f;
        go.transform.rotation = Quaternion.LookRotation(build[0].Entry - build[0].EntryHeading * 18f - go.transform.position);
        yield return Render(cam, 960, 960, Path.Combine(Plugin.Dir, $"preview-{route.name}-startback.png"));
        foreach (var c in build.Where(p => p.IsCannon))
        {
            go.transform.position = c.Entry - c.EntryHeading * 30f + Vector3.up * 18f - Vector3.Cross(Vector3.up, c.EntryHeading) * 20f;
            go.transform.rotation = Quaternion.LookRotation(c.Entry + c.EntryHeading * 60f - go.transform.position);
            yield return Render(cam, 960, 960, Path.Combine(Plugin.Dir, $"preview-{route.name}-cannon{c.Index}.png"));
        }

        Object.Destroy(go);
    }

    private static IEnumerator Render(Camera cam, int width, int height, string path)
    {
        yield return new WaitForEndOfFrame();
        var rt = new RenderTexture(width, height, 24);
        cam.targetTexture = rt;
        cam.Render();
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        cam.targetTexture = null;
        File.WriteAllBytes(path, Png.Encode(tex.GetPixels32(), width, height));
        Object.Destroy(tex);
        Object.Destroy(rt);
        Plugin.Log.LogInfo($"Preview: wrote {path}");
    }
}
