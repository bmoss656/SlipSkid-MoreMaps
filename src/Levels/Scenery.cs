using System;
using System.Collections.Generic;
using System.Linq;
using Game.Levels;
using UnityEngine;
using Object = UnityEngine.Object;
using Placed = SlipSkid.MoreMaps.RoutePlanner.Placed;

namespace SlipSkid.MoreMaps;

// A route's `scenery`: copies of props from the game's biome scenery (every level instantiates all six biomes,
// hidden but one), standing in the fog off to the sides of our road. The biomes' own props are hundreds of metres
// out, a horizon around the vanilla tracks; these fill in nearer. Purely visual (colliders removed), always shown
// whatever the biome, and laid out from the route name so every player sees the same.
internal static class Scenery
{
    private const float Clearance = 30f;   // from the road's centre line to a prop's edge
    private const float Spread = 70f;      // how much further out a prop may stand
    private const float Sink = 6f;         // how far a prop's foot sits below the fog plane

    private static GameObject _holder;

    internal static void Place(LevelDefinition def, RouteData route, List<Placed> build)
    {
        if (_holder || route?.scenery == null || route.scenery.Count == 0 || build == null)
            return;
        var road = build.Where(p => !p.IsGap)
            .SelectMany(p => RoutePlanner.Lines(p).SelectMany(line => Enumerable.Range(0, 5).Select(i => line(i / 4f))))
            .ToList();
        if (road.Count == 0)
            return;
        var fogY = def.fogPlane ? def.fogPlane.transform.position.y : road.Min(p => p.y) - 20f;
        var roots = SceneFit.BiomeRoots(def).ToList();
        var rng = new System.Random(BitConverter.ToInt32(TrackBuilder.HashGuid(route.name + "/scenery").ToByteArray(), 0));
        _holder = new GameObject("MoreMaps Scenery");
        var placed = new List<(Vector3 centre, float radius)>();
        var log = new List<string>();
        foreach (var entry in route.scenery)
        {
            var source = Find(roots, entry.prop);
            if (!source)
            {
                Plugin.Log.LogWarning($"Scenery: '{route.name}': no prop '{entry.prop}' in the biomes");
                continue;
            }
            for (var k = 0; k < entry.count; k++)
            {
                var clone = Object.Instantiate(source.gameObject, _holder.transform);
                clone.name = $"{source.name}_MoreMaps{placed.Count:D2}";
                foreach (var c in clone.GetComponentsInChildren<Collider>(true))
                    Object.Destroy(c);
                clone.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f) * source.rotation);
                clone.transform.localScale = source.lossyScale * (entry.scale > 0f ? entry.scale : 1f);
                clone.SetActive(true);
                var bounds = Bounds(clone);
                var offset = new Vector3(bounds.center.x, 0f, bounds.center.z);
                var radius = Mathf.Max(bounds.extents.x, bounds.extents.z);
                var spot = Spot(rng, road, placed, radius);
                if (spot == null)
                {
                    Object.Destroy(clone);
                    Plugin.Log.LogWarning($"Scenery: '{route.name}': no room for {entry.prop} #{k + 1} (radius {radius:F0} m)");
                    continue;
                }
                clone.transform.position = spot.Value - offset + Vector3.up * (fogY - Sink - bounds.min.y);
                placed.Add((spot.Value, radius));
                log.Add($"{source.name} at {spot.Value:F0}");
            }
        }
        Plugin.Log.LogInfo($"Scenery: '{route.name}': {placed.Count} props: {string.Join(", ", log)}");
    }

    // A clear spot (centre, on the ground plane) near some part of the road, or null after enough tries.
    private static Vector3? Spot(System.Random rng, List<Vector3> road, List<(Vector3 centre, float radius)> placed, float radius)
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            var near = road[rng.Next(road.Count)];
            var angle = (float)rng.NextDouble() * 2f * 3.14159265f;
            var distance = Clearance + radius + (float)rng.NextDouble() * Spread;
            var c = new Vector3(near.x + Mathf.Cos(angle) * distance, 0f, near.z + Mathf.Sin(angle) * distance);
            if (road.Any(p => Flat(p - c) < Clearance + radius))
                continue;
            if (placed.Any(o => Flat(o.centre - c) < o.radius + radius + 10f))
                continue;
            return c;
        }
        return null;
    }

    // "Sand/SandArch": the shallowest object of that name in that biome's scenery.
    private static Transform Find(List<GameObject> roots, string prop)
    {
        var parts = (prop ?? "").Split(new[] { '/' }, 2);
        if (parts.Length != 2)
            return null;
        var root = roots.FirstOrDefault(r => r.name.StartsWith(parts[0] + "EnvironmentBiomeVisuals", StringComparison.OrdinalIgnoreCase));
        if (!root)
            return null;
        return root.GetComponentsInChildren<Transform>(true).Where(t => t.name == parts[1])
            .OrderBy(t => Depth(t)).FirstOrDefault();
    }

    private static int Depth(Transform t)
    {
        var d = 0;
        for (; t.parent; t = t.parent)
            d++;
        return d;
    }

    private static Bounds Bounds(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>(true).Where(r => r is MeshRenderer || r is SkinnedMeshRenderer).ToList();
        if (renderers.Count == 0)
            return new Bounds(go.transform.position, Vector3.zero);
        var b = renderers[0].bounds;
        foreach (var r in renderers)
            b.Encapsulate(r.bounds);
        return b;
    }

    private static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;
}
