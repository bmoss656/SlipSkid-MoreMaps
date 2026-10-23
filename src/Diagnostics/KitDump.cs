using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Game;
using Game.Levels;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Obstacles;
using UnityEngine;
using Utils.Networking;
using World;
using Object = UnityEngine.Object;

namespace SlipSkid.MoreMaps;

// Writes everything a custom track needs to know about the loaded level scene: every road tile (code, id,
// mesh, transform, colliders, surface), the start/finish lines, checkpoints, spawns, fog plane, end-game
// platform and the scene's root hierarchy. Runs just before LevelDefinition.Init, so it sees the scene as
// built (before any of our changes). Output: kit-<scene>.json next to the DLL, plus a summary in the log.
internal static class KitDump
{
    private static readonly Regex TileName = new("R([0-9]{3})B([0-9]{3})", RegexOptions.IgnoreCase);

    internal static string Code(string name)
    {
        var m = TileName.Match(name ?? "");
        return m.Success ? $"R{m.Groups[1].Value}B{m.Groups[2].Value}" : null;
    }

    internal static void Run(LevelDefinition def, string label)
    {
        var scene = def.gameObject.scene.name;
        var root = new JObject
        {
            ["scene"] = scene,
            ["label"] = label,
        };

        root["levelDefinition"] = new JObject
        {
            ["startLine"] = Pose(def.startLine ? def.startLine.transform : null),
            ["startLineFinishOffset"] = Pose(def.startLine ? def.startLine.finishLineRootOffset : null),
            ["finishLine"] = Pose(def.finishLine ? def.finishLine.transform : null),
            ["finishLineStartOffset"] = Pose(def.finishLine ? def.finishLine.startLineRootOffset : null),
            ["finishSnapshot"] = Pose(def.finishLine ? def.finishLine.snapShotPos : null),
            ["finishColliders"] = def.finishLine ? Colliders(def.finishLine.gameObject) : null,
            ["startLineChildren"] = def.startLine ? Children(def.startLine.transform) : null,
            ["finishLineChildren"] = def.finishLine ? Children(def.finishLine.transform) : null,
            ["endGamePlatform"] = Pose(def.endGamePlatform ? def.endGamePlatform.transform : null),
            ["environmentVisualRoot"] = Pose(def.environmentVisualRoot),
            ["fogPlane"] = Pose(def.fogPlane ? def.fogPlane.transform : null),
            ["fogPlaneBounds"] = def.fogPlane ? Box(def.fogPlane.bounds) : null,
            ["levelLighting"] = Pose(def.levelLighting ? def.levelLighting.transform : null),
            ["environmentBiomes"] = def.environmentBiomes?.Count ?? 0,
            ["reflectionProbes"] = def.reflectionProbes?.Count ?? 0,
            ["inSceneObstacles"] = new JArray((def.inSceneObstacles ?? new List<Obstacle>()).Where(o => o)
                .Select(o => new JObject { ["name"] = o.name, ["pose"] = Pose(o.transform) })),
            ["checkpoints"] = new JArray((def.checkpoints ?? new List<CheckPoint>()).Where(c => c)
                .Select(c => new JObject
                {
                    ["name"] = c.name,
                    ["path"] = Path(c.transform),
                    ["index"] = c.index,
                    ["pose"] = Pose(c.transform),
                    ["colliders"] = Colliders(c.gameObject),
                    ["gates"] = c.splitScreenGates ? Path(c.splitScreenGates.transform) : null,
                })),
            ["tileCount"] = def.tiles?.Count ?? 0,
        };

        var spawner = Object.FindObjectOfType<RandomPositionPlayerSpawner>();
        if (spawner)
        {
            var spawns = Traverse.Create(spawner).Field("spawnPositions").GetValue<List<Transform>>();
            root["spawner"] = new JObject
            {
                ["pose"] = Pose(spawner.transform),
                ["path"] = Path(spawner.transform),
                ["spawns"] = new JArray((spawns ?? new List<Transform>()).Select(t => (JToken)Pose(t))),
                ["spawnPaths"] = new JArray((spawns ?? new List<Transform>()).Select(t => t ? Path(t) : null)),
                ["timeTrial"] = Pose(spawner.timeTrialSpawn),
            };
        }

        var obstacleDef = Object.FindObjectOfType<ObstacleLevelDefinition>();
        if (obstacleDef)
            root["forbiddenZones"] = new JArray((obstacleDef.forbiddenObstacleZones ?? new BoxCollider[0])
                .Where(b => b).Select(b => new JObject
                {
                    ["path"] = Path(b.transform),
                    ["pose"] = Pose(b.transform),
                    ["bounds"] = Box(b.bounds),
                }));

        root["gravityPlanes"] = new JArray(Object.FindObjectsOfType<GravityShapeInfinitePlane>()
            .Select(g => new JObject { ["path"] = Path(g.transform), ["pose"] = Pose(g.transform), ["gravity"] = g.gravity }));

        // Tiles: the registered list, plus any LevelTileObject in the scene that isn't in it.
        var tiles = new List<LevelTileObject>(def.tiles ?? new List<LevelTileObject>());
        foreach (var t in Object.FindObjectsOfType<LevelTileObject>(true))
            if (!tiles.Contains(t))
                tiles.Add(t);
        var tileArray = new JArray();
        var meshes = new Dictionary<string, JObject>();
        foreach (var t in tiles.Where(t => t))
            tileArray.Add(Tile(t, def, meshes));
        root["tiles"] = tileArray;
        root["meshes"] = new JObject(meshes.Select(kv => new JProperty(kv.Key, kv.Value)));

        // Scene roots two levels deep, to see what is track and what is scenery.
        root["hierarchy"] = new JArray(def.gameObject.scene.GetRootGameObjects().Select(g => Node(g.transform, 2)));

        var file = System.IO.Path.Combine(Plugin.Dir, $"kit-{scene}.json");
        File.WriteAllText(file, root.ToString(Formatting.Indented));

        // Log summary: one line per piece type.
        Plugin.Log.LogInfo($"KitDump: {scene} ({label}): {tiles.Count} tiles ({def.tiles?.Count ?? 0} registered), " +
                           $"{def.checkpoints?.Count ?? 0} checkpoints, wrote {file}");
        foreach (var g in tiles.Where(t => t).GroupBy(t => Code(t.name) ?? "(no code)").OrderBy(g => g.Key))
        {
            var first = g.First();
            var mesh = MeshOf(first);
            Plugin.Log.LogInfo($"KitDump:   {g.Key} x{g.Count()} mesh={mesh?.name} size={(mesh ? $"{mesh.bounds.size:F2}" : "-")}");
        }
        Plugin.Log.LogInfo($"KitDump:   start={Fmt(def.startLine?.transform)} finish={Fmt(def.finishLine?.transform)} " +
                           $"fogY={(def.fogPlane ? def.fogPlane.transform.position.y.ToString("F2") : "-")}");
    }

    internal static Mesh MeshOf(LevelTileObject t)
    {
        if (t.nonBatchedMesh)
            return t.nonBatchedMesh;
        var mf = t.GetComponent<MeshFilter>();
        return mf ? mf.sharedMesh : null;
    }

    private static JObject Tile(LevelTileObject t, LevelDefinition def, Dictionary<string, JObject> meshes)
    {
        var mf = t.GetComponent<MeshFilter>();
        var mr = t.GetComponent<MeshRenderer>();
        var o = new JObject
        {
            ["name"] = t.name,
            ["code"] = Code(t.name),
            ["id"] = t.id?.ToString(),
            ["registered"] = def.tiles != null && def.tiles.Contains(t),
            ["active"] = t.gameObject.activeInHierarchy,
            ["static"] = t.gameObject.isStatic,
            ["layer"] = LayerMask.LayerToName(t.gameObject.layer),
            ["path"] = Path(t.transform),
            ["pose"] = Pose(t.transform),
            ["filterMesh"] = mf && mf.sharedMesh ? mf.sharedMesh.name : null,
            ["nonBatchedMesh"] = t.nonBatchedMesh ? t.nonBatchedMesh.name : null,
            ["rendererBounds"] = mr ? Box(mr.bounds) : null,
            ["materials"] = mr ? new JArray(mr.sharedMaterials.Select(m => m ? m.name : null)) : null,
            ["surfaces"] = new JArray(t.GetComponents<SurfaceInfo>().Select(s => s.surface ? s.surface.name : null)),
            ["colliders"] = Colliders(t.gameObject),
            ["children"] = Children(t.transform),
        };
        var mesh = MeshOf(t);
        if (mesh && !meshes.ContainsKey(mesh.name))
            meshes[mesh.name] = MeshInfo(mesh);
        return o;
    }

    // Bounds plus, for readable meshes, the vertices on each side face of the bounding box: where the
    // openings are (a straight's two ends sit on opposite faces, a 90-degree turn's on adjacent ones).
    private static JObject MeshInfo(Mesh mesh)
    {
        var o = new JObject
        {
            ["bounds"] = Box(mesh.bounds),
            ["vertexCount"] = mesh.vertexCount,
            ["readable"] = mesh.isReadable,
        };
        if (!mesh.isReadable)
            return o;
        var v = mesh.vertices;
        var b = mesh.bounds;
        const float eps = 0.05f;
        var faces = new JObject();
        void Face(string name, System.Func<Vector3, float> axis, float value, System.Func<Vector3, float> across)
        {
            var on = v.Where(p => Mathf.Abs(axis(p) - value) < eps).ToArray();
            if (on.Length == 0)
                return;
            faces[name] = new JObject
            {
                ["count"] = on.Length,
                ["across"] = new JArray(R(on.Min(across)), R(on.Max(across))),
                ["y"] = new JArray(R(on.Min(p => p.y)), R(on.Max(p => p.y))),
            };
        }
        Face("minX", p => p.x, b.min.x, p => p.z);
        Face("maxX", p => p.x, b.max.x, p => p.z);
        Face("minZ", p => p.z, b.min.z, p => p.x);
        Face("maxZ", p => p.z, b.max.z, p => p.x);
        o["faces"] = faces;
        return o;
    }

    private static JArray Colliders(GameObject go) =>
        new(go.GetComponentsInChildren<Collider>(true).Select(c =>
        {
            var j = new JObject
            {
                ["type"] = c.GetType().Name,
                ["on"] = c.gameObject == go ? "." : c.name,
                ["trigger"] = c.isTrigger,
                ["enabled"] = c.enabled,
                ["layer"] = LayerMask.LayerToName(c.gameObject.layer),
                ["bounds"] = Box(c.bounds),
            };
            if (c is MeshCollider mc)
                j["convex"] = mc.convex;
            if (c is BoxCollider bc)
            {
                j["center"] = V(bc.center);
                j["size"] = V(bc.size);
            }
            return j;
        }));

    private static JArray Children(Transform t)
    {
        var a = new JArray();
        foreach (Transform c in t)
            a.Add(new JObject
            {
                ["name"] = c.name,
                ["active"] = c.gameObject.activeSelf,
                ["local"] = new JArray(V(c.localPosition), V(c.localEulerAngles)),
                ["components"] = new JArray(c.GetComponents<Component>().Where(x => x && !(x is Transform)).Select(x => x.GetType().Name)),
            });
        return a;
    }

    private static JObject Node(Transform t, int depth)
    {
        var o = new JObject
        {
            ["name"] = t.name,
            ["active"] = t.gameObject.activeSelf,
            ["layer"] = LayerMask.LayerToName(t.gameObject.layer),
            ["pos"] = V(t.position),
            ["collider"] = t.GetComponent<Collider>() is { } col ? Box(col.bounds) : null,
            ["mesh"] = t.GetComponent<MeshFilter>() is { } mf && mf.sharedMesh ? mf.sharedMesh.name : null,
            ["children"] = t.childCount,
            ["renderers"] = t.GetComponentsInChildren<Renderer>(true).Length,
            ["tiles"] = t.GetComponentsInChildren<LevelTileObject>(true).Length,
            ["components"] = new JArray(t.GetComponents<Component>().Where(x => x && !(x is Transform)).Select(x => x.GetType().Name)),
        };
        if (depth > 0 && t.childCount > 0)
        {
            var a = new JArray();
            foreach (Transform c in t)
                a.Add(Node(c, depth - 1));
            o["kids"] = a;
        }
        return o;
    }

    internal static string Path(Transform t)
    {
        var parts = new List<string>();
        for (; t; t = t.parent)
            parts.Add(t.name);
        parts.Reverse();
        return string.Join("/", parts);
    }

    private static JObject Pose(Transform t) => t
        ? new JObject { ["pos"] = V(t.position), ["rot"] = V(t.eulerAngles), ["scale"] = V(t.lossyScale) }
        : null;

    private static JObject Box(Bounds b) => new() { ["center"] = V(b.center), ["size"] = V(b.size) };

    private static JArray V(Vector3 v) => new(R(v.x), R(v.y), R(v.z));

    private static float R(float f) => Mathf.Round(f * 1000f) / 1000f;

    private static string Fmt(Transform t) => t ? $"{t.position:F1} rot {t.eulerAngles:F0}" : "-";
}
