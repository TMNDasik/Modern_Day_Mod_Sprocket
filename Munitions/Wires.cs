using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Munitions;








internal sealed class Wire
{
    public readonly List<Vector3> P = new();
    public readonly List<Vector3> V = new();
    public readonly List<Vector3> Old = new();
    public readonly List<float> Ground = new();
    public readonly List<float> NextGround = new();
    public readonly List<bool> OnGround = new();
    public Transform Anchor;
    public Vector3 AnchorLocal;
    public bool Attached = true, Snapped;
    public Vector3 Head, HeadVel;
    public float HeadTime;
    public Vector3 LastLaid;
    public float Laid, MaxLength;
    public float DetachTime = -1f;
}

internal static class Wires
{
    private const float Spacing = 4f;           
    private const float Rest = Spacing * 1.03f; 
    private const float Terminal = 4.5f;        
    private const float Lift = 0.03f;           
    private const float Radius = 0.0006f;       
    private const float FadeTime = 5f;
    private const int Mask = ~(1 << 2);         

    private static readonly List<Wire> All = new();
    private static readonly Color Colour = new(0.11f, 0.105f, 0.095f, 1f);

    public static int Count => All.Count;

    public static Wire Start(Transform anchor, Vector3 launchPos, float maxLength)
    {
        var w = new Wire
        {
            Anchor = anchor,
            MaxLength = Mathf.Max(50f, maxLength),
            LastLaid = launchPos,
            Head = launchPos,
            HeadTime = Time.time
        };
        try { if (anchor != null) w.AnchorLocal = anchor.InverseTransformPoint(launchPos); }
        catch { w.Anchor = null; }
        AddNode(w, launchPos);
        All.Add(w);
        return w;
    }

    
    public static void Feed(Wire w, Vector3 pos, Vector3 vel)
    {
        if (w == null || !w.Attached) return;
        w.Head = pos;
        w.HeadVel = vel;
        w.HeadTime = Time.time;
    }

    
    public static void Detach(Wire w)
    {
        if (w == null || !w.Attached) return;
        Lay(w, w.Head);
        if ((w.Head - w.LastLaid).sqrMagnitude > 0.25f && !w.Snapped) AddNode(w, w.Head);
        w.Attached = false;
        w.DetachTime = Time.time;
    }

    public static void Clear() => All.Clear();

    private static void AddNode(Wire w, Vector3 p)
    {
        w.P.Add(p);
        w.V.Add(Vector3.zero);
        w.Old.Add(p);
        w.Ground.Add(float.NegativeInfinity);
        w.NextGround.Add(0f);
        w.OnGround.Add(false);
    }

    
    private static void Lay(Wire w, Vector3 head)
    {
        var d = head - w.LastLaid;
        var len = d.magnitude;
        if (len < Spacing) return;
        var dir = d / len;
        var n = Math.Min((int)(len / Spacing), 500);
        for (var k = 0; k < n; k++)
        {
            if (w.Laid + Spacing > w.MaxLength)
            {
                w.Snapped = true;
                w.Attached = false;
                w.DetachTime = Time.time;
                if (Plugin.DebugLogging.Value) Plugin.Log.LogInfo($"ATGM wire ran out at {w.Laid:0} m and snapped");
                return;
            }
            w.LastLaid += dir * Spacing;
            w.Laid += Spacing;
            AddNode(w, w.LastLaid);
        }
        if (n == 500) w.LastLaid = head; 
    }

    

    public static void Tick(Camera cam)
    {
        if (All.Count == 0)
        {
            if (_renderer != null && !_renderer.WasCollected && _renderer.enabled) _renderer.enabled = false;
            return;
        }
        var now = Time.time;
        var dt = Mathf.Min(Time.deltaTime, 0.05f);
        var life = Mathf.Max(5f, Plugin.AtgmWireLifetime.Value);
        for (var i = All.Count - 1; i >= 0; i--)
        {
            var w = All[i];
            if (!w.Attached && w.DetachTime >= 0f && now - w.DetachTime > life + FadeTime) { All.RemoveAt(i); continue; }
            try
            {
                if (w.Attached) Lay(w, HeadNow(w));
                if (dt > 0f) Simulate(w, dt, now);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("ATGM wire failed: " + e.Message);
                All.RemoveAt(i);
            }
        }
        try { Render(cam, now, life); }
        catch (Exception e) { Plugin.Log.LogWarning("ATGM wire render failed: " + e.Message); }
    }

    private static Vector3 HeadNow(Wire w) => w.Head + w.HeadVel * Mathf.Clamp(Time.time - w.HeadTime, 0f, 0.05f);

    private static void Simulate(Wire w, float dt, float now)
    {
        var n = w.P.Count;
        var g = Mathf.Abs(Physics.gravity.y);
        var pinned = false;
        if (w.Anchor != null)
        {
            try
            {
                if (!w.Anchor.WasCollected && w.Anchor) { w.P[0] = w.Anchor.TransformPoint(w.AnchorLocal); pinned = true; }
                else w.Anchor = null;
            }
            catch { w.Anchor = null; }
        }

        
        for (var i = 0; i < n; i++)
        {
            w.Old[i] = w.P[i];
            if (i == 0 && pinned) continue;
            if (w.OnGround[i]) continue;
            var v = w.V[i];
            v.y -= g * dt;
            var sp = v.magnitude;
            if (sp > 1e-5f)
            {
                var drag = g * sp * sp / (Terminal * Terminal) * dt;
                v -= v * Mathf.Min(1f, drag / sp);
            }
            w.V[i] = v;
            w.P[i] += v * dt;
        }

        
        for (var iter = 0; iter < 2; iter++)
            for (var i = 0; i + 1 < n; i++)
            {
                var a = w.P[i];
                var b = w.P[i + 1];
                var d = b - a;
                var len = d.magnitude;
                if (len <= Rest || len < 1e-5f) continue;
                var wa = i == 0 && pinned ? 0f : w.OnGround[i] ? 0.25f : 1f;
                var wb = w.OnGround[i + 1] ? 0.25f : 1f;
                var sum = wa + wb;
                if (sum <= 0f) continue;
                var corr = d * ((len - Rest) / len / sum);
                w.P[i] = a + corr * wa;
                w.P[i + 1] = b - corr * wb;
            }

        
        for (var i = 0; i < n; i++)
        {
            if (i == 0 && pinned) { w.V[i] = Vector3.zero; continue; }
            var p = w.P[i];
            if (now >= w.NextGround[i])
            {
                w.Ground[i] = GroundAt(p);
                w.NextGround[i] = now + (w.OnGround[i] ? 3f : 0.15f) + (i % 7) * 0.01f;
            }
            var ground = w.Ground[i] + Lift;
            if (p.y <= ground)
            {
                p.y = ground;
                w.P[i] = p;
                w.V[i] = Vector3.zero;
                w.OnGround[i] = true;
                continue;
            }
            
            if (w.OnGround[i] && p.y > ground + 0.05f) w.OnGround[i] = false;
            if (!w.OnGround[i]) w.V[i] = (p - w.Old[i]) / dt;
        }
    }

    
    private static readonly List<Terrain> Terrains = new();
    private static float _nextTerrainScan;

    private static float GroundAt(Vector3 p)
    {
        if (Time.unscaledTime >= _nextTerrainScan)
        {
            _nextTerrainScan = Time.unscaledTime + 2f;
            Terrains.Clear();
            try { foreach (var t in Terrain.activeTerrains) if (t != null && t.terrainData != null) Terrains.Add(t); }
            catch { }
        }
        foreach (var t in Terrains)
        {
            try
            {
                if (t == null || t.WasCollected) continue;
                var tp = t.transform.position;
                var size = t.terrainData.size;
                if (p.x < tp.x || p.z < tp.z || p.x > tp.x + size.x || p.z > tp.z + size.z) continue;
                var h = t.SampleHeight(p) + tp.y;
                
                if (Physics.Raycast(p + Vector3.up * 2f, Vector3.down, out var hit, p.y - h + 2.5f, Mask, QueryTriggerInteraction.Ignore))
                    return Mathf.Max(h, hit.point.y);
                return h;
            }
            catch { }
        }
        try
        {
            if (Physics.Raycast(p + Vector3.up * 2f, Vector3.down, out var hit, 500f, Mask, QueryTriggerInteraction.Ignore)) return hit.point.y;
        }
        catch { }
        return p.y - 1000f;
    }

    

    private static GameObject _go;
    private static Mesh _mesh;
    private static MeshRenderer _renderer;
    private static Material _material;
    private static Il2CppStructArray<Vector3> _verts;
    private static Il2CppStructArray<Color32> _colors;
    private static Il2CppStructArray<int> _indices;
    private static int _capacity;
    private static readonly List<Vector3> Points = new();

    private static void Render(Camera cam, float now, float life)
    {
        if (cam == null || cam.WasCollected || !EnsureObjects()) return;
        var camPos = cam.transform.position;
        var pixel = 2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1, Screen.height);

        var verts = 0;
        foreach (var w in All) verts += (w.P.Count + (w.Attached ? 1 : 0)) * 2;
        if (verts < 4)
        {
            if (_renderer.enabled) _renderer.enabled = false;
            return;
        }
        Ensure(verts);

        var v = 0;
        var idx = 0;
        foreach (var w in All)
        {
            var fade = 1f;
            if (!w.Attached && w.DetachTime >= 0f) fade = Mathf.Clamp01(1f - (now - w.DetachTime - life) / FadeTime);
            if (fade <= 0f) continue;
            Points.Clear();
            Points.AddRange(w.P);
            if (w.Attached) Points.Add(HeadNow(w));
            var m = Points.Count;
            if (m < 2) continue;
            var start = v;
            for (var j = 0; j < m; j++)
            {
                var p = Points[j];
                var tangent = Points[Math.Min(j + 1, m - 1)] - Points[Math.Max(j - 1, 0)];
                var toCam = camPos - p;
                var dist = toCam.magnitude;
                var side = Vector3.Cross(tangent, toCam);
                side = side.sqrMagnitude > 1e-10f ? side.normalized : cam.transform.right;
                var half = Mathf.Max(Radius, 0.6f * pixel * dist);
                var cover = Radius / half;
                var far = Mathf.Clamp01(1f - (dist - 1500f) / 1500f);
                var a = Mathf.Clamp01(0.35f + 0.65f * cover) * fade * far;
                var c = new Color32((byte)(Colour.r * 255f), (byte)(Colour.g * 255f), (byte)(Colour.b * 255f), (byte)(a * 255f));
                _verts[v] = p - side * half;
                _verts[v + 1] = p + side * half;
                _colors[v] = c;
                _colors[v + 1] = c;
                v += 2;
            }
            for (var j = 0; j + 1 < m; j++)
            {
                var b = start + j * 2;
                _indices[idx++] = b;
                _indices[idx++] = b + 1;
                _indices[idx++] = b + 3;
                _indices[idx++] = b;
                _indices[idx++] = b + 3;
                _indices[idx++] = b + 2;
            }
        }
        if (idx == 0)
        {
            if (_renderer.enabled) _renderer.enabled = false;
            return;
        }
        
        for (var k = v; k < _capacity; k++) _verts[k] = Vector3.zero;
        _mesh.vertices = _verts;
        _mesh.colors32 = _colors;
        _mesh.SetIndices(_indices, 0, idx, MeshTopology.Triangles, 0, false, 0);
        _mesh.bounds = new Bounds(camPos, Vector3.one * 20000f);
        if (!_renderer.enabled) _renderer.enabled = true;
    }

    private static void Ensure(int verts)
    {
        if (verts <= _capacity) return;
        var cap = Math.Max(256, _capacity);
        while (cap < verts) cap *= 2;
        _capacity = cap;
        _verts = new Il2CppStructArray<Vector3>(cap);
        _colors = new Il2CppStructArray<Color32>(cap);
        _indices = new Il2CppStructArray<int>(cap * 3);
        _mesh.Clear();
        _mesh.indexFormat = IndexFormat.UInt32;
        _mesh.vertices = _verts;
        _mesh.uv = new Il2CppStructArray<Vector2>(cap);
    }

    private static bool EnsureObjects()
    {
        if (_go != null && !_go.WasCollected && _mesh != null && !_mesh.WasCollected && _renderer != null && !_renderer.WasCollected)
            return _material != null && !_material.WasCollected || MakeMaterial();
        try
        {
            if (!MakeMaterial()) return false;
            _go = new GameObject("Munitions ATGM wires");
            Object.DontDestroyOnLoad(_go);
            _mesh = new Mesh { name = "Munitions ATGM wires" };
            _mesh.MarkDynamic();
            _mesh.hideFlags = HideFlags.DontUnloadUnusedAsset;
            _go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer = _go.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = _material;
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _capacity = 0;
            return true;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("ATGM wire renderer failed: " + e.Message);
            return false;
        }
    }

    
    private static bool MakeMaterial()
    {
        var src = Fx.SmokeMat;
        if (src == null) return false;
        _material = new Material(src) { name = "Munitions ATGM wire" };
        _material.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        _material.SetTexture("_MainTex", Texture2D.whiteTexture);
        _material.SetColor("_Tint", Color.white);
        _material.SetFloat("_Brightness", 1f);
        _material.SetFloat("_SoftDepth", 0.05f);
        if (_renderer != null && !_renderer.WasCollected) _renderer.sharedMaterial = _material;
        return true;
    }
}
