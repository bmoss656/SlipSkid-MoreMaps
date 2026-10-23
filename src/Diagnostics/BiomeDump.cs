using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Game.Levels;
using HarmonyLib;
using UnityEngine;

namespace SlipSkid.MoreMaps;

// [Debug] DumpBiomes: lists a vanilla track's biome scenery (one prefab per surface, instantiated by
// LevelDefinition.Init) to biomes-<scene>.txt next to the DLL: the tree three levels deep with each node's
// renderer count and world bounds, then every mesh with how often it's used. Used to pick props to reuse.
internal static class BiomeDump
{
    internal static void Run(LevelDefinition def, string scene)
    {
        var sb = new StringBuilder();
        var road = def.tiles.Where(t => t).Select(t => t.transform.position).ToList();
        if (road.Count > 0)
            sb.AppendLine($"road from {Min(road):F0} to {Max(road):F0}");
        sb.AppendLine($"environmentVisualRoot at {(def.environmentVisualRoot ? def.environmentVisualRoot.position : Vector3.zero):F0}");
        if (Traverse.Create(def).Field("_environmentBiomes").GetValue() is IDictionary biomes)
            foreach (DictionaryEntry entry in biomes)
            {
                var root = entry.Value.GetType().GetField("Item2")?.GetValue(entry.Value) as GameObject;
                if (!root)
                    continue;
                sb.AppendLine();
                sb.AppendLine($"== {(entry.Key as Object)?.name} biome: {root.name} at {root.transform.position:F0}");
                Tree(sb, root.transform, 0);
                sb.AppendLine("-- meshes:");
                foreach (var g in root.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh)
                             .GroupBy(f => f.sharedMesh.name).OrderByDescending(g => g.Count()))
                {
                    var b = g.First().GetComponent<Renderer>() is { } r ? r.bounds.size : Vector3.zero;
                    sb.AppendLine($"   {g.Key} x{g.Count()} size {b:F0} e.g. {KitDump.Path(g.First().transform)}");
                }
            }
        var path = Path.Combine(Plugin.Dir, $"biomes-{scene}.txt");
        File.WriteAllText(path, sb.ToString());
        Plugin.Log.LogInfo($"BiomeDump: wrote {path}");
    }

    private static void Tree(StringBuilder sb, Transform t, int depth)
    {
        var renderers = t.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            return;
        var b = renderers[0].bounds;
        foreach (var r in renderers)
            b.Encapsulate(r.bounds);
        sb.AppendLine($"{new string(' ', depth * 2)}{t.name}{(t.gameObject.activeSelf ? "" : " (off)")}: {renderers.Length} renderers, " +
                      $"{b.min:F0}..{b.max:F0}");
        if (depth >= 3 || t.childCount > 60)
            return;
        foreach (Transform c in t)
            Tree(sb, c, depth + 1);
    }

    private static Vector3 Min(List<Vector3> v) => new(v.Min(p => p.x), v.Min(p => p.y), v.Min(p => p.z));
    private static Vector3 Max(List<Vector3> v) => new(v.Max(p => p.x), v.Max(p => p.y), v.Max(p => p.z));
}
