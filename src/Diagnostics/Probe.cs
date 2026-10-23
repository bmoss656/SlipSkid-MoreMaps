using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Levels;
using Player;
using UnityEngine;
using World;
using Object = UnityEngine.Object;
using UnitySceneManager = UnityEngine.SceneManagement.SceneManager;

namespace SlipSkid.MoreMaps;

// [Debug] Probe: logs what map design needs to know (level preview data, every surface's physics, car stats).
// [Debug] Gallery: for each listed track, renders every road piece type to gallery-<scene>.png (a contact
// sheet, 6 per row, in the order logged) so the unmapped pieces can be identified by eye.
// Both run once at the main menu (from AutoDump) and switch themselves off.
internal static class Probe
{
    internal static void Run()
    {
        foreach (var level in LevelManager.instance.levels.Where(l => l))
        {
            var meshes = level.previewMeshes ?? new Level.LevelPreviewMesh[0];
            Plugin.Log.LogInfo($"Probe: level '{level.name}' preview {Tex(level.previewImage)} thumb {Tex(level.thumbnailImage)} " +
                               $"biome {(level.biomeSurface ? level.biomeSurface.name : "-")} difficulty {level.difficulty} previewMeshes {meshes.Length}");
            foreach (var pm in meshes)
                Plugin.Log.LogInfo($"Probe:   mesh {(pm.mesh ? $"{pm.mesh.name} v{pm.mesh.vertexCount} sub{pm.mesh.subMeshCount} bounds {pm.mesh.bounds.center:F1}/{pm.mesh.bounds.size:F1} readable {pm.mesh.isReadable}" : "-")} " +
                                   $"renderer {(pm.renderer ? pm.renderer.name : "-")} mats [{(pm.renderer ? string.Join(",", pm.renderer.sharedMaterials.Select(m => m ? $"{m.name}/{m.shader.name}" : "-")) : "")}] " +
                                   $"surfaces [{string.Join(",", (pm.surfaces ?? new Surface[0]).Select(s => s ? s.name : "-"))}]");
        }
        var surfaces = SurfaceManager.instance ? SurfaceManager.instance.surfaces : new Surface[0];
        for (var i = 0; i < surfaces.Length; i++)
        {
            var s = surfaces[i];
            if (!s)
                continue;
            Plugin.Log.LogInfo($"Probe: surface #{i} {s.name} friction {s.friction} accel {s.accelerationModifier} sticky {s.stickyness} drift {s.driftFriction} " +
                               $"bounce {s.bouncyness} wet {s.wetness} mat {(s.effects?.material ? s.effects.material.name : "-")} " +
                               $"modifiers [{string.Join(",", (s.carModifiers ?? new CarModifier[0]).Where(m => m).Select(m => $"{m.name}:{m.GetType().Name}"))}]");
            foreach (var m in (s.carModifiers ?? new CarModifier[0]).Where(m => m))
                Plugin.Log.LogInfo($"Probe:   {m.name}: " + string.Join(", ", m.GetType().GetFields().Select(f => $"{f.Name}={Describe(f.GetValue(m))}")));
        }
        foreach (var car in Resources.FindObjectsOfTypeAll<SphereCarController>())
            Plugin.Log.LogInfo($"Probe: car '{car.name}' speed {car.speed} maxSpeed {car.maxSpeed} reverse {car.reverseSpeed} drift {car.driftSpeed} floorRay {car.floorRaycastDistance}");
    }

    private static string Describe(object value)
    {
        if (value == null)
            return "null";
        var type = value.GetType();
        if (type.IsValueType && type.Namespace == "Game" || type.Name.StartsWith("ModifierValue"))
            return "{" + string.Join(" ", type.GetFields().Where(f => f.FieldType != typeof(AnimationCurve)).Select(f => $"{f.Name}={f.GetValue(value)}")) + "}";
        return value.ToString();
    }

    private static string Tex(Texture t) => t ? $"{t.name} {t.width}x{t.height}" : "-";

    internal static IEnumerator Gallery(string scene)
    {
        var op = UnitySceneManager.LoadSceneAsync(scene);
        while (!op.isDone)
            yield return null;
        yield return null;
        var roots = UnitySceneManager.GetActiveScene().GetRootGameObjects();
        var byCode = new SortedDictionary<string, GameObject>(System.StringComparer.Ordinal);
        foreach (var g in roots)
        {
            var code = KitDump.Code(g.name);
            if (code == null || !g.GetComponent<LevelTileObject>() || g.name != code)
                continue;
            // Prefer a flat-laid one.
            var flat = Mathf.Abs(Mathf.DeltaAngle(g.transform.eulerAngles.x, 270f)) < 1f;
            if (!byCode.TryGetValue(code, out var have) || (flat && Mathf.Abs(Mathf.DeltaAngle(have.transform.eulerAngles.x, 270f)) >= 1f))
                byCode[code] = g;
        }
        // Hide the track (the pieces are rendered far away from it anyway); keep lights and sky.
        foreach (var g in roots)
            if (g.GetComponentInChildren<LevelTileObject>(true) || g.name == "CollisionMesh")
                g.SetActive(false);

        const int cell = 320, columns = 6;
        var rows = (byCode.Count + columns - 1) / columns;
        var sheet = new Texture2D(cell * columns, cell * rows, TextureFormat.RGB24, false);
        var rt = new RenderTexture(cell, cell, 24);
        var camGo = new GameObject("MoreMapsGalleryCamera");
        var cam = camGo.AddComponent<Camera>();
        cam.targetTexture = rt;
        cam.fieldOfView = 40f;

        var n = 0;
        foreach (var kv in byCode)
        {
            var template = kv.Value;
            var clone = Object.Instantiate(template);   // template is inactive, so the clone is too
            clone.transform.SetPositionAndRotation(new Vector3(5000f + n * 600f, 500f, 5000f), template.transform.rotation);
            var tiles = clone.GetComponentsInChildren<LevelTileObject>(true);
            for (var i = 0; i < tiles.Length; i++)
                TrackBuilder.SetUpTile(tiles[i], $"gallery/{n}/{i}");
            clone.SetActive(true);
            yield return null;
            var bounds = new Bounds(clone.transform.position, Vector3.zero);
            foreach (var r in clone.GetComponentsInChildren<Renderer>())
                bounds.Encapsulate(r.bounds);
            var size = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z, 5f);
            // Looking from the tile's local -y/+x corner, so the frame matches the docs' tile frame: road
            // runs along local x (world x before yaw), seen from above at 40 degrees.
            var dir = (clone.transform.rotation * new Vector3(-0.6f, -1f, 0.9f)).normalized;
            camGo.transform.position = bounds.center - dir * size * 1.6f;
            camGo.transform.rotation = Quaternion.LookRotation(dir);
            cam.farClipPlane = size * 4f;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var col = n % columns;
            var row = rows - 1 - n / columns;
            sheet.ReadPixels(new Rect(0, 0, cell, cell), col * cell, row * cell);
            RenderTexture.active = prev;
            var e = template.transform.eulerAngles;
            Plugin.Log.LogInfo($"Gallery: {scene} #{n} {kv.Key} size {bounds.size:F0} rot ({e.x:F0},{e.y:F0},{e.z:F0}) " +
                               $"kids [{string.Join(",", template.GetComponentsInChildren<Transform>(true).Skip(1).Select(t => t.name).Take(6))}] " +
                               $"surfaces [{string.Join(",", template.GetComponentsInChildren<SurfaceInfo>(true).Select(s => s.surface ? s.surface.name.Replace("Surface", "") : "-").Distinct())}]");
            Object.Destroy(clone);
            n++;
        }
        sheet.Apply();
        var path = Path.Combine(Plugin.Dir, $"gallery-{scene}.png");
        File.WriteAllBytes(path, Png.Encode(sheet.GetPixels32(), sheet.width, sheet.height));
        Plugin.Log.LogInfo($"Gallery: wrote {path} ({n} pieces)");
        Object.Destroy(camGo);
        Object.Destroy(rt);
        Object.Destroy(sheet);
    }

    // [Debug] Snapshot "Track: x,y,z; x,y,z": pictures of spots on a vanilla track, scenery included.
    internal static IEnumerator Snapshot(string spec)
    {
        var parts = spec.Split(':');
        var scene = parts[0].Trim();
        var points = parts[1].Split(';').Select(p => p.Split(',').Select(v => float.Parse(v.Trim(), System.Globalization.CultureInfo.InvariantCulture)).ToArray())
            .Where(v => v.Length == 3).Select(v => new Vector3(v[0], v[1], v[2])).ToList();
        yield return AutoDump.Load(scene);
        var def = Object.FindObjectOfType<LevelDefinition>();
        if (def)
            AutoDump.ShowScenery(def);
        var obstacleDef = Object.FindObjectOfType<Obstacles.ObstacleLevelDefinition>();
        if (obstacleDef)
            obstacleDef.DisableObstaclePlacementMode();
        yield return null;
        var go = new GameObject("MoreMapsSnapshotCamera");
        var cam = go.AddComponent<Camera>();
        cam.farClipPlane = 2000f;
        cam.fieldOfView = 60f;
        for (var i = 0; i < points.Count; i++)
        {
            var p = points[i];
            foreach (var yaw in new[] { 0f, 90f, 180f, 270f })
            {
                var dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                go.transform.position = p - dir * 45f + Vector3.up * 18f;
                go.transform.rotation = Quaternion.LookRotation(p - go.transform.position);
                yield return RenderTo(cam, Path.Combine(Plugin.Dir, $"snapshot-{scene}-{i}-{yaw:0}.png"));
            }
            go.transform.SetPositionAndRotation(p + Vector3.up * 120f, Quaternion.Euler(90f, 0f, 0f));
            yield return RenderTo(cam, Path.Combine(Plugin.Dir, $"snapshot-{scene}-{i}-top.png"));
        }
        Object.Destroy(go);
    }

    private static IEnumerator RenderTo(Camera cam, string path)
    {
        yield return new WaitForEndOfFrame();
        var rt = new RenderTexture(800, 600, 24);
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(800, 600, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 800, 600), 0, 0);
        RenderTexture.active = null;
        cam.targetTexture = null;
        File.WriteAllBytes(path, Png.Encode(tex.GetPixels32(), 800, 600));
        Object.Destroy(tex);
        Object.Destroy(rt);
        Plugin.Log.LogInfo($"Snapshot: wrote {path}");
    }
}
