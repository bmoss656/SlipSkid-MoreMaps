using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Effects;
using Game.Levels;
using HarmonyLib;
using UnityEngine;

namespace SlipSkid.MoreMaps;

// Fits the base scene around our road once LevelManager.InitLevel has run (it instantiates the scenery).
// Both ideas come from MapKitLoader, which does the same for its maps:
// - Scenery: the biome scenery was laid out around the vanilla road, so pieces of it can stand on ours. Hide
//   any scenery mesh that reaches into the space just around and above the road (all biomes, since surface
//   swaps change which one shows).
// - Tyre trails: an orthographic camera over the vanilla track renders the trail texture the road shader reads;
//   outside its square the road draws flat green. Recentre it on our road, never smaller than before.
internal static class SceneFit
{
    private const float Pad = 6f, Above = 20f, Below = 1f;

    internal static void Apply(LevelDefinition def, List<RoutePlanner.Placed> build)
    {
        if (!def || build == null || build.Count == 0)
            return;
        var road = new List<Bounds>();
        foreach (var p in build)
            foreach (var r in p.Objects().Where(o => o.layer != LayerMask.NameToLayer("RoadCombined")).SelectMany(o => o.GetComponentsInChildren<Renderer>()))
            {
                var b = r.bounds;
                road.Add(new Bounds(b.center + Vector3.up * (Above - Below) / 2f, b.size + new Vector3(2f * Pad, Above + Below, 2f * Pad)));
            }
        if (road.Count == 0)
            return;

        var hidden = 0;
        foreach (var root in BiomeRoots(def))
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!filter.sharedMesh || !filter.gameObject.activeSelf)
                    continue;
                var b = WorldBounds(filter.sharedMesh.bounds, filter.transform.localToWorldMatrix);
                if (road.Any(r => Overlaps(r, b)))
                {
                    filter.gameObject.SetActive(false);
                    hidden++;
                }
            }

        var all = road[0];
        foreach (var b in road)
            all.Encapsulate(b);
        var trails = Object.FindObjectOfType<TrailCameraShaderValuesAssigner>();
        if (trails && trails.trailCamera)
        {
            var cam = trails.trailCamera;
            var y = Mathf.Max(cam.transform.position.y, all.max.y + 50f);
            cam.transform.position = new Vector3(all.center.x, y, all.center.z);
            cam.orthographicSize = Mathf.Max(cam.orthographicSize, Mathf.Max(all.extents.x, all.extents.z) + 20f);
            cam.farClipPlane = Mathf.Max(cam.farClipPlane, y - all.min.y + 100f);
            Shader.SetGlobalVector("_TrailsOrthoCamPosition", cam.transform.position);
            Shader.SetGlobalFloat("_TrailsOrthoCam", cam.orthographicSize);
        }
        Plugin.Log.LogInfo($"SceneFit: hid {hidden} scenery meshes on the road; trails camera " +
                           (trails && trails.trailCamera ? $"at {trails.trailCamera.transform.position:F0}, size {trails.trailCamera.orthographicSize:F0}" : "not found"));
    }

    internal static IEnumerable<GameObject> BiomeRoots(LevelDefinition def)
    {
        var t = Traverse.Create(def);
        if (t.Field("_environmentBiomes").GetValue() is IDictionary biomes)
            foreach (var entry in biomes.Values)
                if (entry.GetType().GetField("Item2")?.GetValue(entry) is GameObject go && go)
                    yield return go;
        if (t.Field("_rushModeEnvironmentBiome").GetValue() is Component rush && rush)
            yield return rush.gameObject;
    }

    // (Bounds.Intersects is stripped.)
    private static bool Overlaps(Bounds a, Bounds b) =>
        a.min.x <= b.max.x && a.max.x >= b.min.x && a.min.y <= b.max.y && a.max.y >= b.min.y &&
        a.min.z <= b.max.z && a.max.z >= b.min.z;

    private static Bounds WorldBounds(Bounds b, Matrix4x4 m)
    {
        var centre = m.MultiplyPoint3x4(b.center);
        var e = b.extents;
        var x = m.MultiplyVector(new Vector3(e.x, 0f, 0f));
        var y = m.MultiplyVector(new Vector3(0f, e.y, 0f));
        var z = m.MultiplyVector(new Vector3(0f, 0f, e.z));
        var size = new Vector3(Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x), Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y),
            Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));
        return new Bounds(centre, size * 2f);
    }
}
