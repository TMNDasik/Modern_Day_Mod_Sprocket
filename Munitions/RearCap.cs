using System;
using System.Collections.Generic;
using Sprocket.Vehicles.Cannons;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Munitions;






internal static class RearCap
{
    private sealed class Cap
    {
        public GameObject Go;
        public MeshFilter Filter;
        public MeshRenderer Renderer;
        public IntPtr SourceMesh;
        public Bounds SourceBounds;
        public float Ratio;
    }

    private static readonly Dictionary<IntPtr, Cap> Caps = new();
    private static readonly MaterialPropertyBlock Mpb = new();
    private static bool _logged;

    public static void Ensure(Cannon cannon, Transform bore)
    {
        if (cannon == null || bore == null) return;
        MeshRenderer source = null;
        MeshFilter filter = null;
        foreach (var r in bore.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (r == null || r.name.StartsWith("Munitions", StringComparison.Ordinal)) continue;
            var f = r.GetComponent<MeshFilter>();
            if (f == null || f.sharedMesh == null) continue;
            source = r;
            filter = f;
            break;
        }
        if (source == null) return; 
        var mesh = filter.sharedMesh;
        var ratio = OuterRatio(cannon);

        if (!Caps.TryGetValue(cannon.Pointer, out var cap) || cap.Go == null || cap.Go.WasCollected || cap.Go.transform.parent != source.transform)
        {
            if (cap?.Go != null && !cap.Go.WasCollected) Object.Destroy(cap.Go);
            cap = Create(source);
            if (cap == null) return;
            Caps[cannon.Pointer] = cap;
        }
        if (cap.SourceMesh != mesh.Pointer || cap.SourceBounds != mesh.bounds || Math.Abs(cap.Ratio - ratio) > 1e-4f)
        {
            cap.SourceMesh = mesh.Pointer;
            cap.SourceBounds = mesh.bounds;
            cap.Ratio = ratio;
            Build(cap, mesh.bounds, ratio, cannon, source.transform);
        }
        Sync(cap, source);
        if (Caps.Count > 512) Cleanup();
    }

    
    private static float OuterRatio(Cannon cannon)
    {
        try
        {
            var bp = cannon.Blueprint;
            var cal = Mathf.Max(1f, bp.Caliber);
            var wall = bp.GetSegment(0).Segments[0].StartThickness;
            return (cal * 0.5f + wall) / (cal * 0.5f);
        }
        catch { return 1.15f; }
    }

    private static Cap Create(MeshRenderer source)
    {
        try
        {
            var go = new GameObject("Munitions tube rear cap");
            go.transform.SetParent(source.transform, false);
            go.layer = source.gameObject.layer;
            var cap = new Cap { Go = go, Filter = go.AddComponent<MeshFilter>(), Renderer = go.AddComponent<MeshRenderer>() };
            cap.Filter.sharedMesh = new Mesh { name = "Munitions tube rear cap" };
            return cap;
        }
        catch (Exception e)
        {
            if (Plugin.DebugLogging.Value) Plugin.Log.LogWarning("Tube rear cap failed: " + e.Message);
            return null;
        }
    }

    
    
    private static void Build(Cap cap, Bounds b, float ratio, Cannon cannon, Transform space)
    {
        var e = b.extents;
        var axis = e.x >= e.y && e.x >= e.z ? 0 : e.y >= e.z ? 1 : 2;
        Vector3 Axis(int i) => i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward;
        var a = Axis(axis);
        var u = Axis((axis + 1) % 3);
        var v = Axis((axis + 2) % 3);
        var len = axis == 0 ? e.x : axis == 1 ? e.y : e.z;
        var ru = (axis + 1) % 3 == 0 ? e.x : (axis + 1) % 3 == 1 ? e.y : e.z;
        var rv = (axis + 2) % 3 == 0 ? e.x : (axis + 2) % 3 == 1 ? e.y : e.z;
        var endA = b.center + a * len;
        var endB = b.center - a * len;
        var fwd = cannon.transform.forward;
        var rearIsA = Vector3.Dot(space.TransformPoint(endA) - space.TransformPoint(endB), fwd) < 0f;
        var rear = rearIsA ? endA : endB;
        var outward = rearIsA ? a : -a;     
        rear += outward * (len * 0.002f);   

        const int n = 32;
        var verts = new Vector3[(n + 1) * 2];
        var normals = new Vector3[verts.Length];
        var uvs = new Vector2[verts.Length];
        var tris = new int[n * 6];
        for (var side = 0; side < 2; side++)
        {
            var o = side * (n + 1);
            var normal = side == 0 ? outward : -outward;
            verts[o] = rear;
            normals[o] = normal;
            uvs[o] = new Vector2(0.5f, 0.5f);
            for (var i = 0; i < n; i++)
            {
                var ang = i * Mathf.PI * 2f / n;
                var c = Mathf.Cos(ang);
                var s = Mathf.Sin(ang);
                verts[o + 1 + i] = rear + u * (c * ru * ratio) + v * (s * rv * ratio);
                normals[o + 1 + i] = normal;
                uvs[o + 1 + i] = new Vector2(0.5f + 0.5f * c, 0.5f + 0.5f * s);
            }
            for (var i = 0; i < n; i++)
            {
                var t = (side * n + i) * 3;
                var i0 = o + 1 + i;
                var i1 = o + 1 + (i + 1) % n;
                
                var faceOut = Vector3.Dot(Vector3.Cross(verts[i0] - rear, verts[i1] - rear), normal) > 0f;
                tris[t] = o;
                tris[t + 1] = faceOut ? i0 : i1;
                tris[t + 2] = faceOut ? i1 : i0;
            }
        }
        var mesh = cap.Filter.sharedMesh;
        mesh.Clear();
        mesh.vertices = verts;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.triangles = tris;
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        if (!_logged)
        {
            _logged = true;
            Plugin.Log.LogInfo($"Launcher tube rear cap added (bore axis {"xyz"[axis]}, outer/bore radius {ratio:0.00})");
        }
    }

    
    private static void Sync(Cap cap, MeshRenderer source)
    {
        var r = cap.Renderer;
        var mat = source.sharedMaterial;
        if (mat != null && (r.sharedMaterial == null || r.sharedMaterial.Pointer != mat.Pointer)) r.sharedMaterial = mat;
        r.shadowCastingMode = source.shadowCastingMode;
        r.receiveShadows = source.receiveShadows;
        r.lightProbeUsage = source.lightProbeUsage;
        r.reflectionProbeUsage = source.reflectionProbeUsage;
        r.motionVectorGenerationMode = source.motionVectorGenerationMode;
        r.renderingLayerMask = source.renderingLayerMask;
        cap.Go.layer = source.gameObject.layer;
        Mpb.Clear();
        source.GetPropertyBlock(Mpb);
        r.SetPropertyBlock(Mpb);
    }

    private static void Cleanup()
    {
        var dead = new List<IntPtr>();
        foreach (var kv in Caps) if (kv.Value.Go == null || kv.Value.Go.WasCollected) dead.Add(kv.Key);
        foreach (var k in dead) Caps.Remove(k);
    }
}
