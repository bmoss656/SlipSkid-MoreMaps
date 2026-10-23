using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Game;
using Game.Levels;
using HarmonyLib;
using Obstacles;
using UnityEngine;
using Utils;
using Utils.Networking;
using World;
using Object = UnityEngine.Object;
using Placed = SlipSkid.MoreMaps.RoutePlanner.Placed;

namespace SlipSkid.MoreMaps;

// Lays a route's pieces out on its base scene, just before LevelDefinition.Init. Runs on every machine and
// must give the same result everywhere: same pieces, same poses, same tile ids (hashed from the route name
// and piece index), registered in the same order.
// - Keeps the vanilla start (start line, its spawn points and the R000 cap under it).
// - Hides every other vanilla tile root, the scene's CollisionMesh and the in-scene obstacles.
// - Clones one vanilla tile per piece (children included) and moves the finish line onto the last piece.
internal static class TrackBuilder
{
    // The last build, for TrackPreview and SceneFit.
    internal static List<Placed> LastBuild = new();

    // The route built into the loaded scene, if any (cleared when a new scene loads).
    internal static RouteData BuiltRoute;

    internal static void Build(LevelDefinition def, RouteData route)
    {
        LastBuild = new List<Placed>();
        var scene = def.gameObject.scene;
        var roots = scene.GetRootGameObjects();

        // 1. The start: the vanilla start line stays where it is. Its cap is the tile at the start line's pivot;
        // cars drive out of the cap along the spawn points' forward.
        var startLine = def.startLine ? def.startLine : Object.FindObjectOfType<StartLine>();
        var finishLine = def.finishLine ? def.finishLine : Object.FindObjectOfType<FinishLine>();
        var spawner = Object.FindObjectOfType<RandomPositionPlayerSpawner>();
        if (!startLine || !finishLine || !spawner || spawner.SpawnCount == 0)
        {
            Plugin.Log.LogError($"TrackBuilder: '{route.name}': missing start line, finish line or spawns; keeping the vanilla track");
            return;
        }
        var cap = roots.Where(g => g.GetComponent<LevelTileObject>() && KitDump.Code(g.name) == "R000B101")
            .OrderBy(g => (g.transform.position - startLine.transform.position).sqrMagnitude).FirstOrDefault();
        if (!cap || (cap.transform.position - startLine.transform.position).magnitude > 1f)
        {
            Plugin.Log.LogError($"TrackBuilder: '{route.name}': no R000B101 cap under the start line; keeping the vanilla track");
            return;
        }
        var heading = RoutePlanner.Flat(spawner.GetSpawnPoint(0).forward);
        var capCentre = cap.transform.position + cap.transform.rotation * new Vector3(-5f, 5f, 0f);
        // The route may lift the whole start (start line, spawns, cap) so the track starts high up.
        var cursor = RoadKit.Round(capCentre + heading * 5f + Vector3.up * route.startRise);

        // 2. Plan every piece and find a template for each type before touching the scene, so a bad route
        // leaves the vanilla track playable.
        var plan = RoutePlanner.Plan(route, cursor, heading, out var error);
        if (plan == null || plan.Count == 0)
        {
            Plugin.Log.LogError($"TrackBuilder: '{route.name}': {error ?? "no pieces"}; keeping the vanilla track");
            return;
        }
        var pieces = plan.Where(p => !p.IsGap).ToList();
        var templates = new Dictionary<string, GameObject>();
        foreach (var p in pieces)
        {
            var wanted = p.Parts != null
                ? p.Parts.Select(part => (from: part.From, code: part.Code))
                : new[] { (from: p.Data.from, code: p.Data.type) };
            foreach (var (from, code) in wanted)
            {
                var key = TemplateKey(from, code);
                if (templates.ContainsKey(key))
                    continue;
                var fromBase = string.IsNullOrEmpty(from) || KitLibrary.DonorScene(from) == scene.name;
                var t = fromBase ? FindTemplate(roots, code) : KitLibrary.Get(from, code);
                if (!t)
                {
                    Plugin.Log.LogError($"TrackBuilder: '{route.name}': no '{code}' tile in {from ?? scene.name}; keeping the vanilla track. " +
                                        (from == null ? "Types here: " + string.Join(" ", roots.Select(g => KitDump.Code(g.name)).Where(c => c != null).Distinct().OrderBy(c => c)) : ""));
                    return;
                }
                templates[key] = t;
            }
        }

        // 3. Lift the start if asked (the start line carries its spawns and floor; the no-obstacle zone on it
        // follows), then hide the vanilla track.
        if (route.startRise != 0f)
        {
            var oldStart = startLine.transform.position;
            var lift = Vector3.up * route.startRise;
            startLine.transform.position += lift;
            cap.transform.position += lift;
            // The cap is static-batched: moving it moves its collider but not its drawing. Give it its own mesh.
            foreach (var tile in cap.GetComponentsInChildren<LevelTileObject>(true))
                Unbatch(tile);
            var zones = Object.FindObjectOfType<ObstacleLevelDefinition>();
            if (zones && zones.forbiddenObstacleZones != null)
                foreach (var zone in zones.forbiddenObstacleZones.Where(z => z && (z.transform.position - oldStart).magnitude < 10f))
                    zone.transform.position += lift;
        }
        // Hide the vanilla track.
        var hidden = 0;
        foreach (var g in roots)
        {
            if (g == cap)
                continue;
            var isTrack = g.GetComponentInChildren<LevelTileObject>(true);
            var isCollision = g.name == "CollisionMesh" && g.GetComponent<MeshCollider>() && !g.GetComponent<Renderer>();
            if (!isTrack && !isCollision)
                continue;
            g.SetActive(false);
            hidden++;
        }
        var keptTiles = cap.GetComponentsInChildren<LevelTileObject>(true);
        def.tiles.Clear();
        def.tiles.AddRange(keptTiles);
        foreach (var o in def.inSceneObstacles.Where(o => o))
            Object.Destroy(o.gameObject);
        var obstacles = def.inSceneObstacles.Count;
        def.inSceneObstacles.Clear();

        // 4. Clone the pieces. Templates are inactive now, so clones start inactive and we can fix them up
        // before their Awake/OnEnable run.
        var roadCombined = LayerMask.NameToLayer("RoadCombined");
        foreach (var p in pieces)
        {
            if (p.Parts != null)
            {
                // Several tiles (a segment as on its donor, side-by-side lanes, a cannon's road); segments keep
                // their own look unless the piece paints them.
                for (var k = 0; k < p.Parts.Count; k++)
                {
                    var part = p.Parts[k];
                    var road = part.Paint ? p.Data.surface ?? route.surface : p.Data.surface;
                    var borders = part.Paint ? p.Data.borders ?? route.borders : p.Data.borders;
                    (part.Clone, part.Collision) = Spawn(def, templates[TemplateKey(part.From, part.Code)], part.Position, part.Yaw,
                        $"{part.Code}_MoreMaps{p.Index:D3}_{k:D2}", $"{route.name}/{p.Index}/{k}", road, borders, roadCombined);
                }
                if (p.IsCannon)
                    p.Extra.Add(Cannon.Build(p.Data, p.Entry, p.EntryHeading, $"Cannon_MoreMaps{p.Index:D3}"));
                continue;
            }
            (p.Clone, p.Collision) = Spawn(def, templates[TemplateKey(p.Data.from, p.Data.type)], p.Position, p.Yaw,
                $"{p.Data.type}_MoreMaps{p.Index:D3}", $"{route.name}/{p.Index}", p.Data.surface ?? route.surface,
                p.Data.borders ?? route.borders, roadCombined);
        }

        if (roadCombined >= 0)
            CollisionCopy(cap, cap.transform.position, cap.transform.rotation, roadCombined, cap.name + "_Start");

        // 5. The finish line: centred on the last piece's cell, 1.5 m up, facing along the road (as in vanilla).
        var last = pieces.LastOrDefault(p => p.Data.finish) ?? pieces.Last();
        var oldFinish = finishLine.transform.position;
        var finishPos = last.Entry + last.EntryHeading * 5f + Vector3.up * 1.5f;
        finishLine.transform.SetPositionAndRotation(finishPos, Quaternion.Euler(0f, RoadKit.Snap(RoadKit.Yaw(last.EntryHeading)), 0f));
        // The obstacle-free zone that sat on the old finish follows it.
        var obstacleDef = Object.FindObjectOfType<ObstacleLevelDefinition>();
        if (obstacleDef && obstacleDef.forbiddenObstacleZones != null)
            foreach (var zone in obstacleDef.forbiddenObstacleZones.Where(z => z))
                if ((zone.transform.position - oldFinish).magnitude < 5f)
                    zone.transform.position = finishPos + (zone.transform.position - oldFinish);

        var fogY = def.fogPlane ? def.fogPlane.transform.position.y : float.NegativeInfinity;
        var lowest = plan.Min(p => Mathf.Min(p.Entry.y, p.Exit.y));
        if (lowest < fogY + 5f)
            Plugin.Log.LogWarning($"TrackBuilder: '{route.name}': road goes down to {lowest:F1}, close to the fog plane at {fogY:F1} (cars die 3.5 m below it)");

        LastBuild = plan;
        BuiltRoute = route;
        Plugin.Log.LogInfo($"TrackBuilder: '{route.name}' on {scene.name}: {pieces.Count} pieces, {plan.Count - pieces.Count} gaps, {def.tiles.Count} tiles registered, " +
                           $"hid {hidden} vanilla roots, removed {obstacles} in-scene obstacles, finish at {finishPos:F1}, fog at {fogY:F1}");
        foreach (var p in plan)
            Plugin.Log.LogInfo($"TrackBuilder:   #{p.Index} {p.Data.type}{(p.IsGap ? $" drop {p.Data.drop}" : " " + (p.Data.surface ?? route.surface ?? ""))} at {p.Position:F1} " +
                               $"yaw {p.Yaw:F0}, exit {p.Exit:F1} heading {p.ExitHeading:F0}");
    }

    // A vanilla root tile of this type laid flat like the kit expects. Among those: the most complete (some
    // tracks, like Aqueduc, also use bare copies without their border children), then the most common surface
    // set, so the road matches the base track (some tracks paint a few tiles magma or ice).
    private static string TemplateKey(string from, string code) => (from ?? "") + "|" + code;

    // One clone of a template: fixed up while still inactive (ids, renderer, paint), activated, registered,
    // with its RoadCombined collision copy.
    private static (GameObject clone, GameObject collision) Spawn(LevelDefinition def, GameObject template, Vector3 position,
        float yaw, string name, string idKey, string road, string borders, int roadCombined)
    {
        var rotation = Quaternion.Euler(270f, yaw, 0f);
        var clone = Object.Instantiate(template);
        clone.name = name;
        clone.transform.SetPositionAndRotation(position, rotation);
        var tiles = clone.GetComponentsInChildren<LevelTileObject>(true);
        for (var i = 0; i < tiles.Length; i++)
            SetUpTile(tiles[i], $"{idKey}/{i}");
        Paint(clone, tiles, road, borders);
        clone.SetActive(true);
        def.tiles.AddRange(tiles);
        var collision = roadCombined >= 0 ? CollisionCopy(template, position, rotation, roadCombined, name) : null;
        return (clone, collision);
    }

    internal static GameObject FindTemplate(GameObject[] roots, string code)
    {
        var candidates = roots.Where(g => g.name == code && g.GetComponent<LevelTileObject>()).Where(g =>
        {
            var e = g.transform.eulerAngles;
            return Mathf.Abs(Mathf.DeltaAngle(e.x, 270f)) < 1f && Mathf.Abs(Mathf.DeltaAngle(e.z, 0f)) < 1f &&
                   Mathf.Abs(g.transform.localScale.x - 100f) < 0.5f;
        }).ToList();
        if (candidates.Count == 0)
            return null;
        string Surfaces(GameObject g) => string.Join(",", g.GetComponentsInChildren<SurfaceInfo>(true)
            .Select(s => s.surface ? s.surface.name : "-"));
        var mostTiles = candidates.Max(g => g.GetComponentsInChildren<LevelTileObject>(true).Length);
        var complete = candidates.Where(g => g.GetComponentsInChildren<LevelTileObject>(true).Length == mostTiles).ToList();
        var template = complete.GroupBy(Surfaces).OrderByDescending(gr => gr.Count()).ThenBy(gr => gr.Key, StringComparer.Ordinal)
            .First().OrderBy(g => g.transform.position.x).ThenBy(g => g.transform.position.z).First();
        if (mostTiles == 1 && code.Length >= 8 && code.Substring(5).Contains("1"))
            Plugin.Log.LogInfo($"TrackBuilder: this track only has '{code}' without borders");
        return template;
    }

    internal static void SetUpTile(LevelTileObject tile, string idKey)
    {
        tile.id = new SerializableGuid(HashGuid(idKey));
        Unbatch(tile);
    }

    private static void Unbatch(LevelTileObject tile)
    {
        // Vanilla tiles are static-batched: their MeshFilter holds the scene's combined mesh and their renderer
        // carries batch info that pins the drawing to the original spot. Give the clone the tile's own mesh and
        // a fresh renderer with the same materials (for a clone, or a vanilla tile that moves).
        var go = tile.gameObject;
        var filter = go.GetComponent<MeshFilter>();
        var old = go.GetComponent<MeshRenderer>();
        if (filter && old && tile.nonBatchedMesh)
        {
            // (The stripped build has no getters for the shadow and layer settings; the defaults match the
            // vanilla tiles, and LevelTileObject.Start sets the rendering layers again.)
            var materials = old.sharedMaterials;
            Object.DestroyImmediate(old);
            filter.sharedMesh = tile.nonBatchedMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.materials = materials;
            mr.renderingLayerMask = 3u;
        }
        // LevelDefinition.Init counts tile surfaces through tile.SurfaceInfo, which the tile only sets in its
        // Start (next frame for a clone). Set it now.
        var props = Traverse.Create(tile);
        props.Property("SurfaceInfo").SetValue(go.GetComponent<SurfaceInfo>());
        props.Property("Renderers").SetValue(go.GetComponents<MeshRenderer>());
    }

    // Vanilla scenes merge all road collision into one MeshCollider on the RoadCombined layer, which the car
    // linecasts against every physics step so it can't tunnel through the road (SphereCarController ~966).
    // We hide that mesh with the rest of the track and can't build a new one (MeshCollider.sharedMesh has no
    // setter in this build), so each piece gets a second copy holding only its MeshColliders, on that layer.
    private static GameObject CollisionCopy(GameObject template, Vector3 position, Quaternion rotation, int layer, string name)
    {
        var copy = Object.Instantiate(template);
        copy.name = name + "_Collision";
        copy.transform.SetPositionAndRotation(position, rotation);
        foreach (var c in copy.GetComponentsInChildren<Component>(true))
            if (c is not Transform && c is not MeshCollider)
                Object.DestroyImmediate(c);
        foreach (var t in copy.GetComponentsInChildren<Transform>(true))
            t.gameObject.layer = layer;
        copy.SetActive(true);
        return copy;
    }

    // Paints the piece's road tile (its root) and optionally its border tiles (the children) with a surface,
    // exactly as a surface-swap card would (LevelManager.SwapSurfaceLocal), but locally on every machine.
    private static void Paint(GameObject clone, LevelTileObject[] tiles, string road, string borders)
    {
        foreach (var tile in tiles)
        {
            var name = tile.gameObject == clone ? road : borders;
            if (string.IsNullOrEmpty(name))
                continue;
            var surface = Surfaces.Find(name);
            if (surface)
                SwapSurfaceLocal(tile, surface, false);
        }
    }

    private static readonly Action<LevelTileObject, Surface, bool> SwapSurfaceLocal =
        AccessTools.MethodDelegate<Action<LevelTileObject, Surface, bool>>(AccessTools.Method(typeof(LevelManager), "SwapSurfaceLocal"));

    internal static Guid HashGuid(string key)
    {
        using var md5 = MD5.Create();
        return new Guid(md5.ComputeHash(Encoding.UTF8.GetBytes("SlipSkid.MoreMaps/" + key)));
    }
}
