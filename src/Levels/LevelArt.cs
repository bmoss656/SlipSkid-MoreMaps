using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SlipSkid.MoreMaps;

// Level-select pictures drawn from the route, in the vanilla style (transparent; the menu card supplies the
// light background):
// - card (previewImage, 1024x512): the track as a navy isometric model with a pale shadow;
// - thumbnail (512x512): the route's outline as a lavender line, seen from above.
// Drawn by a tiny software rasteriser at 2x and averaged down, so it needs no camera or scene.
internal static class LevelArt
{
    private static readonly Color Top = new Color32(78, 76, 128, 255);
    private static readonly Color Side = new Color32(46, 44, 80, 255);
    private static readonly Color Shadow = new Color32(206, 206, 228, 150);
    private static readonly Color Outline = new Color32(198, 198, 228, 255);
    private const float Elevation = 32f;

    internal static Texture2D Card(List<RoutePlanner.Placed> plan, string name)
    {
        const int w = 1024, h = 512, ss = 2;
        var sections = LevelSelectInfo.Sections(plan);
        var points = sections.SelectMany(s => s.Centre).ToList();
        var yaw = BestYaw(points, (float)w / h, p => Project(p).Item1);
        var minY = points.Min(p => p.y);

        // Triangles in world space with a colour, drawn far to near.
        var tris = new List<(Vector3 a, Vector3 b, Vector3 c, Color colour)>();
        foreach (var s in sections)
            for (var i = 0; i + 1 < s.Centre.Count; i++)
            {
                Vector3 L(int k, float up) => s.Centre[k] - s.Right[k] * 5f + Vector3.up * up;
                Vector3 R(int k, float up) => s.Centre[k] + s.Right[k] * 5f + Vector3.up * up;
                // Shadow on the ground under the track.
                var g = minY - 8f;
                Vector3 G(Vector3 v) => new(v.x, g, v.z);
                tris.Add((G(L(i, 0)), G(L(i + 1, 0)), G(R(i, 0)), Shadow));
                tris.Add((G(L(i + 1, 0)), G(R(i + 1, 0)), G(R(i, 0)), Shadow));
                // Sides, then the road surface.
                foreach (var (a0, a1, b0, b1) in new[] { (L(i, -2.5f), L(i + 1, -2.5f), L(i, 0.6f), L(i + 1, 0.6f)), (R(i, -2.5f), R(i + 1, -2.5f), R(i, 0.6f), R(i + 1, 0.6f)) })
                {
                    tris.Add((a0, a1, b0, Side));
                    tris.Add((a1, b1, b0, Side));
                }
                tris.Add((L(i, 0.6f), L(i + 1, 0.6f), R(i, 0.6f), Shade(L(i, 0.6f), L(i + 1, 0.6f), R(i, 0.6f))));
                tris.Add((L(i + 1, 0.6f), R(i + 1, 0.6f), R(i, 0.6f), Shade(L(i + 1, 0.6f), R(i + 1, 0.6f), R(i, 0.6f))));
            }

        var canvas = new Canvas(w * ss, h * ss);
        var fit = Fit(tris.SelectMany(t => new[] { t.a, t.b, t.c }).Select(p => Project(Rotate(p, yaw)).Item1), w * ss, h * ss, 0.08f, 380f);
        // Shadow first (always behind), then the rest by depth.
        var shadowTris = tris.Where(t => t.colour == Shadow);
        var solid = tris.Where(t => t.colour != Shadow)
            .OrderByDescending(t => Project(Rotate((t.a + t.b + t.c) / 3f, yaw)).Item2);
        foreach (var t in shadowTris.Concat(solid))
            canvas.Triangle(fit(Project(Rotate(t.a, yaw)).Item1), fit(Project(Rotate(t.b, yaw)).Item1),
                fit(Project(Rotate(t.c, yaw)).Item1), t.colour);
        return canvas.ToTexture(ss, name + "_card");
    }

    internal static Texture2D Thumbnail(List<RoutePlanner.Placed> plan, string name)
    {
        const int size = 512, ss = 2;
        var sections = LevelSelectInfo.Sections(plan);
        var points = sections.SelectMany(s => s.Centre).ToList();
        var yaw = BestYaw(points, 1f, p => new Vector2(p.x, p.z));
        var fit = Fit(points.Select(p => { var r = Rotate(p, yaw); return new Vector2(r.x, r.z); }), size * ss, size * ss, 0.1f, 260f);
        var canvas = new Canvas(size * ss, size * ss);
        var radius = 7f * ss;
        foreach (var s in sections)
            for (var i = 0; i + 1 < s.Centre.Count; i++)
            {
                var a = Rotate(s.Centre[i], yaw);
                var b = Rotate(s.Centre[i + 1], yaw);
                canvas.Line(fit(new Vector2(a.x, a.z)), fit(new Vector2(b.x, b.z)), radius, Outline);
            }
        return canvas.ToTexture(ss, name + "_thumb");
    }

    // Light from the upper left: faces turned towards it are lighter.
    private static Color Shade(Vector3 a, Vector3 b, Vector3 c)
    {
        var n = Vector3.Cross(b - a, c - a).normalized;
        if (n.y < 0)
            n = -n;
        var light = Mathf.Clamp01(Vector3.Dot(n, new Vector3(-0.3f, 0.9f, 0.3f).normalized));
        return Color.Lerp(Side, Top, light);
    }

    private static Vector3 Rotate(Vector3 p, float yaw) => Quaternion.Euler(0f, -yaw, 0f) * p;

    // Orthographic camera looking along +z, tilted down by Elevation. Returns the screen point (y up) and depth.
    private static (Vector2, float) Project(Vector3 p)
    {
        var e = Elevation * (3.14159265f / 180f);   // (Mathf.Deg2Rad is stripped)
        var sy = p.y * Mathf.Cos(e) + p.z * Mathf.Sin(e);
        var depth = p.z * Mathf.Cos(e) - p.y * Mathf.Sin(e);
        return (new Vector2(p.x, sy), depth);
    }

    // The yaw (15-degree steps) whose view of the points best fills a frame of this aspect.
    private static float BestYaw(List<Vector3> points, float aspect, System.Func<Vector3, Vector2> view)
    {
        var best = 0f;
        var bestScale = 0f;
        for (var yaw = 0f; yaw < 360f; yaw += 15f)
        {
            var ps = points.Select(p => view(Rotate(p, yaw))).ToList();
            var w = ps.Max(p => p.x) - ps.Min(p => p.x) + 1f;
            var h = ps.Max(p => p.y) - ps.Min(p => p.y) + 1f;
            var scale = Mathf.Min(aspect / w, 1f / h);
            if (scale > bestScale * 1.001f)
            {
                bestScale = scale;
                best = yaw;
            }
        }
        return best;
    }

    // Maps points into a w x h pixel frame (y up), keeping proportions, with a margin. A short track isn't
    // blown up past `minSpan` metres across the frame, so its road keeps a sensible thickness.
    private static System.Func<Vector2, Vector2> Fit(IEnumerable<Vector2> points, int w, int h, float margin, float minSpan)
    {
        var list = points.ToList();
        var min = new Vector2(list.Min(p => p.x), list.Min(p => p.y));
        var max = new Vector2(list.Max(p => p.x), list.Max(p => p.y));
        var size = max - min;
        var scale = Mathf.Min(w * (1f - 2f * margin) / Mathf.Max(size.x, 1f), h * (1f - 2f * margin) / Mathf.Max(size.y, 1f));
        scale = Mathf.Min(scale, w * (1f - 2f * margin) / minSpan);
        var offset = new Vector2(w, h) / 2f - (min + size / 2f) * scale;
        return p => p * scale + offset;
    }

    private sealed class Canvas
    {
        private readonly int _w, _h;
        private readonly Color[] _px;

        public Canvas(int w, int h)
        {
            _w = w;
            _h = h;
            _px = new Color[w * h];
        }

        public void Triangle(Vector2 a, Vector2 b, Vector2 c, Color colour)
        {
            var minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x))));
            var maxX = Mathf.Min(_w - 1, Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))));
            var minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y))));
            var maxY = Mathf.Min(_h - 1, Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))));
            var area = Edge(a, b, c);
            if (Mathf.Abs(area) < 1e-6f)
                return;
            for (var y = minY; y <= maxY; y++)
                for (var x = minX; x <= maxX; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    var w0 = Edge(b, c, p) / area;
                    var w1 = Edge(c, a, p) / area;
                    var w2 = Edge(a, b, p) / area;
                    if (w0 >= 0 && w1 >= 0 && w2 >= 0)
                        Blend(x, y, colour);
                }
        }

        public void Line(Vector2 a, Vector2 b, float radius, Color colour)
        {
            var minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, b.x) - radius));
            var maxX = Mathf.Min(_w - 1, Mathf.CeilToInt(Mathf.Max(a.x, b.x) + radius));
            var minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, b.y) - radius));
            var maxY = Mathf.Min(_h - 1, Mathf.CeilToInt(Mathf.Max(a.y, b.y) + radius));
            var ab = b - a;
            var len2 = Mathf.Max(ab.sqrMagnitude, 1e-6f);
            for (var y = minY; y <= maxY; y++)
                for (var x = minX; x <= maxX; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    var t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
                    if ((a + ab * t - p).sqrMagnitude <= radius * radius)
                        _px[y * _w + x] = colour;
                }
        }

        private void Blend(int x, int y, Color c)
        {
            ref var d = ref _px[y * _w + x];
            var a = c.a + d.a * (1f - c.a);
            if (a <= 0f)
                return;
            d = new Color((c.r * c.a + d.r * d.a * (1f - c.a)) / a, (c.g * c.a + d.g * d.a * (1f - c.a)) / a,
                (c.b * c.a + d.b * d.a * (1f - c.a)) / a, a);
        }

        private static float Edge(Vector2 a, Vector2 b, Vector2 p) => (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);

        // Averages ss x ss blocks (alpha-weighted, so edges fade instead of darkening).
        public Texture2D ToTexture(int ss, string name)
        {
            int w = _w / ss, h = _h / ss;
            var outPx = new Color32[w * h];
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    float r = 0, g = 0, b = 0, a = 0;
                    for (var j = 0; j < ss; j++)
                        for (var i = 0; i < ss; i++)
                        {
                            var c = _px[(y * ss + j) * _w + x * ss + i];
                            r += c.r * c.a;
                            g += c.g * c.a;
                            b += c.b * c.a;
                            a += c.a;
                        }
                    outPx[y * w + x] = a > 0f ? new Color(r / a, g / a, b / a, a / (ss * ss)) : new Color(0f, 0f, 0f, 0f);
                }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = name, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontUnloadUnusedAsset };
            tex.SetPixels32(outPx);
            tex.Apply();
            return tex;
        }
    }
}
