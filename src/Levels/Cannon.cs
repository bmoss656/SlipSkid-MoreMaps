using System.Collections.Generic;
using System.Linq;
using Player;
using Sounds.SplitScreenAudio;
using UnityEngine;

namespace SlipSkid.MoreMaps;

// A giant cannon you drive into: it fires your car along its barrel. Built in code (barrel, bands, carriage,
// wheels) with the game's toon material, so it needs no model files. Sits on two road tiles; the barrel points
// up by `pitch` degrees and `aim` degrees to the right of the road, and the launch follows the barrel.
internal static class Cannon
{
    internal const float Length = 20f;   // road it stands on
    private const float BarrelLength = 16f, OuterRadius = 4f, InnerRadius = 3.2f, BreechZ = 4f;

    internal static float Speed(PieceData d) => d.speed > 0f ? d.speed : 60f;
    internal static float Pitch(PieceData d) => d.pitch != 0f ? d.pitch : 25f;

    // Barrel axis in the cannon's frame (+Z along the road, +Y up).
    internal static Vector3 Axis(PieceData d) =>
        Quaternion.Euler(-Pitch(d), d.aim, 0f) * Vector3.forward;

    // Where the car is launched from, in the cannon's frame: just inside the breech.
    internal static Vector3 LaunchPoint => new(0f, InnerRadius * 0.6f, BreechZ + 1.5f);

    internal static Quaternion Frame(Vector3 heading) => Quaternion.LookRotation(heading, Vector3.up);

    internal static GameObject Build(PieceData d, Vector3 entry, Vector3 heading, string name)
    {
        var go = new GameObject(name);
        go.transform.SetPositionAndRotation(entry, Frame(heading));

        var axis = Axis(d);
        var breech = new Vector3(0f, InnerRadius + 0.3f, BreechZ);
        var yaw = Quaternion.Euler(0f, d.aim, 0f);
        var mb = new MeshBuilder(4);
        // 0: barrel, 1: bands, 2: carriage, 3: wheels. Bands sit just outside the barrel so no faces overlap.
        mb.Tube(breech, axis, BarrelLength, OuterRadius, InnerRadius, 0);
        mb.Tube(breech + axis * 0.2f, axis, 1.4f, OuterRadius + 0.45f, OuterRadius + 0.02f, 1);
        mb.Tube(breech + axis * (BarrelLength * 0.5f - 0.5f), axis, 1f, OuterRadius + 0.3f, OuterRadius + 0.02f, 1);
        mb.Tube(breech + axis * (BarrelLength - 1.6f), axis, 1.6f, OuterRadius + 0.55f, OuterRadius + 0.02f, 1);
        // Carriage: two cheeks either side of the barrel, a base plate, and a big wheel on each side.
        var mid = breech + axis * (BarrelLength * 0.3f);
        foreach (var side in new[] { -1f, 1f })
        {
            var cheek = new Vector3(mid.x + side * (OuterRadius + 0.7f), mid.y * 0.5f + 0.2f, mid.z);
            mb.Box(cheek, new Vector3(1.2f, mid.y + 0.8f, 7f), yaw, 2);
            mb.Cylinder(new Vector3(mid.x + side * (OuterRadius + 2f), 2.8f, mid.z - 1f), yaw * Vector3.right, 0.6f, 2.8f, 3);
        }
        mb.Box(new Vector3(mid.x, 0.35f, mid.z), new Vector3(2f * OuterRadius + 2.6f, 0.7f, 8f), yaw, 2);

        go.AddComponent<MeshFilter>().sharedMesh = mb.ToMesh("MoreMaps Cannon");
        go.AddComponent<MeshRenderer>().materials = new[]
        {
            Toon(new Color32(58, 63, 74, 255)), Toon(new Color32(224, 176, 48, 255)),
            Toon(new Color32(150, 40, 40, 255)), Toon(new Color32(30, 30, 34, 255)),
        };
        AddColliders(go, breech, axis, yaw, mid);

        // The launch trigger: straight in front of the opening only (3 m wide, from 1.5 m before the breech to
        // just inside it). It fires before the car reaches the bore's lip, so the launch carries it clear of the
        // barrel; narrow, so a car near the side isn't pulled in and fired into the bore's wall.
        var trigger = new GameObject("LaunchTrigger");
        trigger.transform.SetParent(go.transform, false);
        // Triggers on Default never see the car; the game's zones (and GoodCards') sit on Obstacle.
        var obstacleLayer = LayerMask.NameToLayer("Obstacle");
        if (obstacleLayer >= 0)
            trigger.layer = obstacleLayer;
        trigger.transform.localPosition = LaunchPoint;
        var box = trigger.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.center = new Vector3(0f, 0f, -0.75f);
        box.size = new Vector3(3f, 2f * InnerRadius, 4.5f);
        var launch = trigger.AddComponent<CannonLaunch>();
        launch.velocity = go.transform.rotation * axis * Speed(d);
        launch.exitPoint = go.transform.TransformPoint(breech + axis * (BarrelLength + 1f));
        return go;
    }

    // Solid boxes: the barrel as 16 slats round an open bore, reaching out over the bands, plus the carriage and
    // wheels. Like the road, each is on RoadCombined (what the car's body collides with, and its anti-tunnel
    // check) with a twin on Default (what its floor ray sees); the car ignores Default colliders. Boxes, as this
    // build can't give a MeshCollider a mesh.
    private static void AddColliders(GameObject go, Vector3 breech, Vector3 axis, Quaternion yaw, Vector3 mid)
    {
        var layers = new[] { LayerMask.NameToLayer("RoadCombined"), 0 }.Where(l => l >= 0).Distinct().ToArray();
        void Box(string name, Vector3 centre, Quaternion rotation, Vector3 size)
        {
            foreach (var layer in layers)
            {
                var child = new GameObject(name);
                child.layer = layer;
                child.transform.SetParent(go.transform, false);
                child.transform.localPosition = centre;
                child.transform.localRotation = rotation;
                child.AddComponent<BoxCollider>().size = size;
            }
        }

        const int slats = 16;
        const float outer = OuterRadius + 0.3f;
        var right = Vector3.Cross(Vector3.up, axis).normalized;
        var up = Vector3.Cross(axis, right);
        var width = 2f * outer * Mathf.Tan(3.14159265f / slats) + 0.1f;
        for (var i = 0; i < slats; i++)
        {
            var a = i * 2f * 3.14159265f / slats;
            var radial = right * Mathf.Cos(a) + up * Mathf.Sin(a);
            Box($"BarrelSlat{i:D2}", breech + axis * (BarrelLength / 2f) + radial * ((InnerRadius + outer) / 2f),
                Quaternion.LookRotation(axis, radial), new Vector3(width, outer - InnerRadius, BarrelLength));
        }
        foreach (var side in new[] { -1f, 1f })
        {
            Box(side < 0 ? "CheekLeft" : "CheekRight", new Vector3(mid.x + side * (OuterRadius + 0.7f), mid.y * 0.5f + 0.2f, mid.z), yaw,
                new Vector3(1.2f, mid.y + 0.8f, 7f));
            Box(side < 0 ? "WheelLeft" : "WheelRight", new Vector3(mid.x + side * (OuterRadius + 2f), 2.8f, mid.z - 1f), yaw,
                new Vector3(1.2f, 4.8f, 4.8f));
        }
        Box("Base", new Vector3(mid.x, 0.35f, mid.z), yaw, new Vector3(2f * OuterRadius + 2.6f, 0.7f, 8f));
    }

    // Copies of the game's ToonWhite with a colour, cached; falls back to any toon material.
    private static readonly Dictionary<Color32, Material> Cache = new();
    private static Material _toonWhite;

    private static Material Toon(Color32 c)
    {
        if (Cache.TryGetValue(c, out var m) && m)
            return m;
        if (!_toonWhite)
            _toonWhite = Resources.FindObjectsOfTypeAll<Material>().FirstOrDefault(x => x && x.name == "ToonWhite")
                         ?? Resources.FindObjectsOfTypeAll<Material>().FirstOrDefault(x => x && x.name.StartsWith("Toon"));
        if (!_toonWhite)
            return null;
        m = new Material(_toonWhite) { name = "ToonWhite", hideFlags = HideFlags.DontUnloadUnusedAsset };
        if (m.HasProperty("_Color"))
            m.SetColor("_Color", c);
        Cache[c] = m;
        return m;
    }

    // A few primitives into one mesh with a submesh per material. Every face is drawn once, wound to face
    // outward; curved sides share vertices (smooth), flat faces get their own (sharp edges).
    private sealed class MeshBuilder
    {
        private readonly List<Vector3> _v = new();
        private readonly List<int>[] _t;

        public MeshBuilder(int submeshes)
        {
            _t = Enumerable.Range(0, submeshes).Select(_ => new List<int>()).ToArray();
        }

        // Tube along `dir` from `start`: outer and inner walls plus the two end rings. inner <= 0 makes a solid
        // cylinder with flat end caps.
        public void Tube(Vector3 start, Vector3 dir, float length, float outer, float inner, int sub, int segments = 32)
        {
            dir = dir.normalized;
            var right = Vector3.Cross(Vector3.up, dir).normalized;
            if (right.sqrMagnitude < 0.01f)
                right = Vector3.right;
            var up = Vector3.Cross(dir, right);
            Vector3 Radial(float a) => right * Mathf.Cos(a) + up * Mathf.Sin(a);
            var end = start + dir * length;

            // Walls: a ring of vertices at each end, shared round the circumference for smooth shading.
            void Wall(float r, bool outward)
            {
                var b = _v.Count;
                for (var i = 0; i <= segments; i++)
                {
                    var n = Radial(i / (float)segments * 2f * 3.14159265f);
                    _v.Add(start + n * r);
                    _v.Add(end + n * r);
                }
                for (var i = 0; i < segments; i++)
                {
                    int a0 = b + i * 2, a1 = a0 + 1, b0 = a0 + 2, b1 = a0 + 3;
                    var n = Radial((i + 0.5f) / segments * 2f * 3.14159265f) * (outward ? 1f : -1f);
                    Quad(a0, a1, b1, b0, n, sub);
                }
            }
            Wall(outer, true);
            if (inner > 0.01f)
                Wall(inner, false);

            // End faces: annuli (or discs), flat.
            void Cap(Vector3 centre, Vector3 normal)
            {
                var b = _v.Count;
                for (var i = 0; i <= segments; i++)
                {
                    var n = Radial(i / (float)segments * 2f * 3.14159265f);
                    _v.Add(centre + n * outer);
                    _v.Add(inner > 0.01f ? centre + n * inner : centre);
                }
                for (var i = 0; i < segments; i++)
                {
                    int a0 = b + i * 2, a1 = a0 + 1, b0 = a0 + 2, b1 = a0 + 3;
                    Quad(a0, b0, b1, a1, normal, sub);
                }
            }
            Cap(start, -dir);
            Cap(end, dir);
        }

        // Solid wheel along `axis`.
        public void Cylinder(Vector3 centre, Vector3 axis, float halfWidth, float radius, int sub)
        {
            axis = axis.normalized;
            Tube(centre - axis * halfWidth, axis, 2f * halfWidth, radius, 0f, sub, 24);
        }

        public void Box(Vector3 centre, Vector3 size, Quaternion rotation, int sub)
        {
            var h = size / 2f;
            Vector3 P(float x, float y, float z) => centre + rotation * new Vector3(x * h.x, y * h.y, z * h.z);
            void Face(Vector3 n, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                var i = _v.Count;
                _v.AddRange(new[] { a, b, c, d });
                Quad(i, i + 1, i + 2, i + 3, rotation * n, sub);
            }
            Face(Vector3.forward, P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1));
            Face(Vector3.back, P(-1, -1, -1), P(1, -1, -1), P(1, 1, -1), P(-1, 1, -1));
            Face(Vector3.right, P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), P(1, -1, 1));
            Face(Vector3.left, P(-1, -1, -1), P(-1, 1, -1), P(-1, 1, 1), P(-1, -1, 1));
            Face(Vector3.up, P(-1, 1, -1), P(1, 1, -1), P(1, 1, 1), P(-1, 1, 1));
            Face(Vector3.down, P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1));
        }

        // Two triangles, wound so their face normal points along `facing`.
        private void Quad(int a, int b, int c, int d, Vector3 facing, int sub)
        {
            var n = Vector3.Cross(_v[b] - _v[a], _v[c] - _v[a]);
            if (Vector3.Dot(n, facing) >= 0f)
                _t[sub].AddRange(new[] { a, b, c, a, c, d });
            else
                _t[sub].AddRange(new[] { a, c, b, a, d, c });
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(_v);
            mesh.subMeshCount = _t.Length;
            for (var i = 0; i < _t.Length; i++)
                mesh.SetTriangles(_t[i], i);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}

// The cannon's breech trigger: fires the local player's own car along the barrel (like the game's zones, a car is
// only moved on the machine that owns it; the others see it through the car's network sync). Same settings on
// every machine.
internal sealed class CannonLaunch : MonoBehaviour
{
    public Vector3 velocity;
    public Vector3 exitPoint;
    private readonly Dictionary<SphereCarController, float> _last = new();

    private static readonly HashSet<string> Seen = new();

    private void OnTriggerEnter(Collider other) => Fire(other);

    // Also while inside, in case the car was already touching the trigger when it spawned or enter was missed.
    private void OnTriggerStay(Collider other) => Fire(other);

    private void Fire(Collider other)
    {
        try
        {
            if (Seen.Add(other.name + "/" + LayerMask.LayerToName(other.gameObject.layer)))
                Plugin.Log.LogInfo($"Cannon: trigger touched by {other.name} (layer {LayerMask.LayerToName(other.gameObject.layer)})");
            var player = other.GetComponentInParent<PlayerNetworking>();
            var car = player ? player.carController : null;
            if (!player || !car || !car.sphereRigidbody)
                return;
            if (_last.TryGetValue(car, out var t) && Time.time - t < 1f)
                return;
            _last[car] = Time.time;
            // Everyone hears the shot (remote cars pass through the trigger too); only the owner moves the car.
            Boom(car);
            if (!player.IsOwner)
                return;
            car.sphereRigidbody.velocity = velocity;
            car.sphereRigidbody.angularVelocity = Vector3.zero;
            var at = transform.InverseTransformPoint(car.sphereRigidbody.position);
            Plugin.Log.LogInfo($"Cannon: fired {player.name} at {velocity.magnitude:F0} m/s " +
                               $"({at.x:F2} m off centre, {at.z:F2} m along the trigger, sphere radius {car.SphereRadius:F2})");
        }
        catch (System.Exception e)
        {
            Guard.Report("Cannon", e);
        }
    }

    // The game's own car explosion, pitched down into a cannon's boom, played at the breech through the game's
    // split-screen audio.
    private void Boom(SphereCarController car)
    {
        var clips = car.effects ? car.effects.deathAudioClipCollection : null;
        if (!clips)
            return;
        SplitScreenAudioManager.PlayClipAtPoint(transform.position, clips, 1f, 0.7f);
    }
}
