using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Levels;
using UnityEngine;
using World;
using Object = UnityEngine.Object;

namespace SlipSkid.MoreMaps;

// What level select shows for our levels, filled in when it first builds its list (by then every manager
// exists):
// - the rotating 3D preview: a ribbon mesh of the route (road and border lips, a submesh per surface) with the
//   vanilla LevelMeshPreview_<Surface> materials, held by a hidden renderer like the vanilla previews;
// - the card and thumbnail pictures, drawn from the route by LevelArt (routes/<file>.preview.png and
//   .thumb.png override them if present);
// - the surface percentages and the colour theme (biomeSurface), from the route's surfaces.
internal static class LevelSelectInfo
{
    private const float HalfRoad = 4f, HalfWidth = 5f, Lip = 0.6f, Skirt = 1.5f;
    private static readonly HashSet<Level> Done = new();

    internal static void Prepare()
    {
        if (!LevelManager.instance)
            return;
        var materials = PreviewMaterials();
        foreach (var level in LevelManager.instance.levels)
        {
            var route = CustomLevels.RouteFor(level);
            if (route == null || Done.Contains(level))
                continue;
            Done.Add(level);
            Guard.Run("LevelSelect", () => Fill(level, route, materials));
        }
    }

    private static void Fill(Level level, RouteData route, Dictionary<string, Material> materials)
    {
        var plan = RoutePlanner.Plan(route, Vector3.zero, Vector3.forward, out var error);
        if (plan == null)
        {
            Plugin.Log.LogError($"LevelSelect: '{route.name}': {error}");
            return;
        }
        var road = new List<string>();
        var counts = new Dictionary<string, int>();
        foreach (var p in plan.Where(p => !p.IsGap))
        {
            var r = RoadSurface(p, route);
            var b = BorderSurface(p, route);
            counts[r] = counts.TryGetValue(r, out var n) ? n + 1 : 1;
            counts[b] = counts.TryGetValue(b, out n) ? n + 2 : 2;
            road.Add(r);
        }

        // Preview mesh.
        var mesh = BuildMesh(plan, route, out var submeshSurfaces);
        var go = new GameObject($"MoreMaps preview {route.name}") { hideFlags = HideFlags.HideAndDontSave };
        Object.DontDestroyOnLoad(go);
        go.SetActive(false);
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.materials = submeshSurfaces.Select(s => MaterialFor(s, materials)).ToArray();
        level.previewMeshes = new[]
        {
            new Level.LevelPreviewMesh(mesh, renderer, submeshSurfaces.Select(Surfaces.Find).Where(s => s).ToArray()),
        };

        // Surface share and theme.
        level.surfaceTileCount = counts.Select(kv => (Surfaces.Find(kv.Key), kv.Value)).Where(x => x.Item1)
            .Select(x => new Level.SurfaceCount(x.Item1, x.Value)).ToList();
        var theme = road.GroupBy(s => s).OrderByDescending(g => g.Count()).Select(g => Surfaces.Find(g.Key)).FirstOrDefault(s => s && IsVanilla(s));
        if (theme)
            level.biomeSurface = theme;

        // Pictures.
        var stem = Path.Combine(Plugin.Dir, "routes", Path.GetFileNameWithoutExtension(route.file));
        var preview = LoadTexture(stem + ".preview.png") ?? LevelArt.Card(plan, route.name);
        var thumb = LoadTexture(stem + ".thumb.png") ?? LevelArt.Thumbnail(plan, route.name);
        level.previewImage = preview;
        level.thumbnailImage = thumb;
        if (Plugin.PreviewRoutes.Value)
            foreach (var (tex, kind) in new[] { (preview, "card"), (thumb, "thumb") })
                if (tex.isReadable)
                    System.IO.File.WriteAllBytes(Path.Combine(Plugin.Dir, $"preview-{route.name}-{kind}.png"),
                        Png.Encode(tex.GetPixels32(), tex.width, tex.height, alpha: true));
        Plugin.Log.LogInfo($"LevelSelect: '{route.name}': preview mesh {mesh.vertexCount} vertices, size {mesh.bounds.size:F0}, " +
                           $"surfaces {string.Join(", ", counts.Select(kv => $"{kv.Key} {kv.Value}"))}, theme {(theme ? theme.name : "-")}, " +
                           $"pictures {preview.name}/{thumb.name}");
    }

    // Segments keep their own look unless the piece paints them (route-wide paint doesn't apply to them).
    private static string RoadSurface(RoutePlanner.Placed p, RouteData route) =>
        p.Data.surface ?? (RoadKit.SegmentFor(p.Data.type) is { } seg ? seg.Road : route.surface ?? "Asphalt");

    private static string BorderSurface(RoutePlanner.Placed p, RouteData route) =>
        p.Data.borders ?? (RoadKit.SegmentFor(p.Data.type) is { } seg ? seg.Walls : route.borders ?? "Concrete");

    private static bool IsVanilla(Surface s) => !s.name.StartsWith("CustomContent_");

    // The vanilla preview materials, by surface ("Asphalt" -> LevelMeshPreview_Asphalt), from the vanilla levels.
    private static Dictionary<string, Material> PreviewMaterials()
    {
        var found = new Dictionary<string, Material>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var level in LevelManager.instance.levels.Where(l => l && CustomLevels.RouteFor(l) == null))
            foreach (var pm in level.previewMeshes ?? new Level.LevelPreviewMesh[0])
                if (pm.renderer)
                    foreach (var m in pm.renderer.sharedMaterials.Where(m => m && m.name.StartsWith("LevelMeshPreview_")))
                        found[m.name.Substring("LevelMeshPreview_".Length)] = m;
        return found;
    }

    private static Material MaterialFor(string surface, Dictionary<string, Material> materials)
    {
        if (materials.TryGetValue(surface, out var m))
            return m;
        // Surfaces without a vanilla preview material (Reverse): a purple copy of concrete.
        if (!materials.TryGetValue("Concrete", out var concrete) && !(concrete = materials.Values.FirstOrDefault()))
            return null;
        var copy = new Material(concrete) { name = "LevelMeshPreview_" + surface, hideFlags = HideFlags.DontUnloadUnusedAsset };
        foreach (var property in new[] { "_Color", "_BaseColor", "_MainColor" })
            if (copy.HasProperty(property))
                copy.SetColor(property, new Color(0.55f, 0.25f, 0.9f));
        materials[surface] = copy;
        return copy;
    }

    // The road as a ribbon following the centre line: the road strip, a raised lip either side for the
    // borders, and skirts down the outside so it reads as a solid track when it spins.
    private static Mesh BuildMesh(List<RoutePlanner.Placed> plan, RouteData route, out List<string> submeshSurfaces)
    {
        var vertices = new List<Vector3>();
        var triangles = new Dictionary<string, List<int>>();
        var order = new List<string>();
        submeshSurfaces = order;
        List<int> Tris(string surface)
        {
            if (!triangles.TryGetValue(surface, out var list))
            {
                triangles[surface] = list = new List<int>();
                order.Add(surface);
            }
            return list;
        }

        // A quad strip between two edge polylines; `both` adds the back faces too.
        void Strip(List<Vector3> left, List<Vector3> right, List<int> tris, bool both = false)
        {
            var start = vertices.Count;
            vertices.AddRange(left);
            vertices.AddRange(right);
            var n = left.Count;
            for (var i = 0; i + 1 < n; i++)
            {
                int l0 = start + i, l1 = l0 + 1, r0 = start + n + i, r1 = r0 + 1;
                tris.AddRange(new[] { l0, l1, r0, l1, r1, r0 });
                if (both)
                    tris.AddRange(new[] { l0, r0, l1, l1, r0, r1 });
            }
        }

        foreach (var s in Sections(plan))
        {
            var p = s.Piece;
            List<Vector3> Edge(float offset, float up) => s.Centre.Select((c, i) => c + s.Right[i] * offset + Vector3.up * up).ToList();

            var road = Tris(RoadSurface(p, route));
            var border = Tris(BorderSurface(p, route));
            Strip(Edge(-HalfRoad, 0f), Edge(HalfRoad, 0f), road);
            Strip(Edge(-HalfWidth, Lip), Edge(-HalfRoad, Lip), border);
            Strip(Edge(HalfRoad, Lip), Edge(HalfWidth, Lip), border);
            Strip(Edge(-HalfRoad, 0f), Edge(-HalfRoad, Lip), border, true);
            Strip(Edge(HalfRoad, 0f), Edge(HalfRoad, Lip), border, true);
            Strip(Edge(-HalfWidth, -Skirt), Edge(-HalfWidth, Lip), border, true);
            Strip(Edge(HalfWidth, -Skirt), Edge(HalfWidth, Lip), border, true);
        }

        // Centred on the origin: level select spins it about the origin and scales it to fit.
        var bounds = new Bounds(vertices[0], Vector3.zero);
        foreach (var v in vertices)
            bounds.Encapsulate(v);
        for (var i = 0; i < vertices.Count; i++)
            vertices[i] -= bounds.center;

        var mesh = new Mesh { name = "MoreMaps " + route.name };
        if (vertices.Count > 65000)
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(vertices);
        mesh.subMeshCount = order.Count;
        for (var i = 0; i < order.Count; i++)
            mesh.SetTriangles(triangles[order[i]], i);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // PreviewRoutes: draws the level-select mesh far above the scene to preview-<level>-select.png, seen from
    // above at an angle as level select shows it.
    internal static System.Collections.IEnumerator RenderPreviewMesh(Level level)
    {
        if (level.previewMeshes == null || level.previewMeshes.Length == 0 || !level.previewMeshes[0].mesh)
            yield break;
        var pm = level.previewMeshes[0];
        var go = new GameObject("MoreMapsSelectPreview");
        go.transform.position = new Vector3(0f, 3000f, 0f);
        go.AddComponent<MeshFilter>().sharedMesh = pm.mesh;
        go.AddComponent<MeshRenderer>().materials = pm.renderer.sharedMaterials;
        var camGo = new GameObject("MoreMapsSelectCamera");
        var cam = camGo.AddComponent<Camera>();
        var size = pm.mesh.bounds.size.magnitude;
        camGo.transform.position = go.transform.position + new Vector3(0f, 0.6f, -0.8f) * size;
        camGo.transform.rotation = Quaternion.LookRotation(go.transform.position - camGo.transform.position);
        cam.farClipPlane = size * 4f;
        yield return new WaitForEndOfFrame();
        var rt = new RenderTexture(800, 600, 24);
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(800, 600, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 800, 600), 0, 0);
        RenderTexture.active = null;
        cam.targetTexture = null;
        var path = Path.Combine(Plugin.Dir, $"preview-{level.name}-select.png");
        File.WriteAllBytes(path, Png.Encode(tex.GetPixels32(), 800, 600));
        Plugin.Log.LogInfo($"LevelSelect: wrote {path}");
        Object.Destroy(tex);
        Object.Destroy(rt);
        Object.Destroy(camGo);
        Object.Destroy(go);
    }

    internal sealed class Section
    {
        public RoutePlanner.Placed Piece;
        public List<Vector3> Centre = new(), Right = new();
    }

    // Each piece's centre line sampled finely enough to draw (turns and slopes get more points), with the
    // road's right-hand direction at each point. Gaps are left out.
    internal static List<Section> Sections(List<RoutePlanner.Placed> plan)
    {
        var sections = new List<Section>();
        foreach (var p in plan.Where(p => !p.IsGap))
            foreach (var line in RoutePlanner.Lines(p))
            {
                var turn = Vector3.Dot(p.EntryHeading, p.ExitHeading) < 0.5f;
                var steps = p.Path != null ? 48 : turn ? 12 : Mathf.Abs(p.Exit.y - p.Entry.y) > 0.1f ? 8 : 1;
                var s = new Section { Piece = p };
                for (var k = 0; k <= steps; k++)
                {
                    var t = k / (float)steps;
                    var ahead = line(Mathf.Min(1f, t + 0.01f)) - line(Mathf.Max(0f, t - 0.01f));
                    ahead.y = 0f;
                    s.Centre.Add(line(t));
                    s.Right.Add(Vector3.Cross(Vector3.up, ahead.normalized));
                }
                sections.Add(s);
            }
        return sections;
    }

    private static Texture2D LoadTexture(string path)
    {
        if (!File.Exists(path))
            return null;
        var tex = new Texture2D(2, 2, TextureFormat.RGB24, false) { name = Path.GetFileNameWithoutExtension(path) };
        if (!ImageConversion.LoadImage(tex, File.ReadAllBytes(path)))
        {
            Plugin.Log.LogWarning($"LevelSelect: can't read {path}");
            return null;
        }
        tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
        return tex;
    }
}
