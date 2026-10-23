using System;
using System.Collections.Generic;
using System.Diagnostics;
using Sprocket.Vehicles.Cannons;
using Sprocket.Vehicles.Weapons;
using UnityEngine;

namespace Munitions;








internal sealed class Silhouette
{
    private const int HullN = 384, TurretN = 256;   
    private const float OutlinePx = 1.7f, InnerPx = 0.9f, StepMeters = 0.04f;

    public readonly IntPtr Vehicle;
    public readonly Transform Hull, Turret;
    public bool Ready, Failed;
    public Texture2D HullTex, TurretTex;
    
    public Vector2 HullCenter, TurretCenter;
    public float HullSpan, TurretSpan;
    public Vector2 HullMin, HullMax, TurretMin, TurretMax;
    public float Scale;          
    public int Triangles, Boxes, RendererCount, TurretByTransform, TurretByParts;

    private readonly float _radius, _box;
    private enum Stage { Collect, Bounds, Raster, Finish }
    private Stage _stage;
    private readonly List<Renderer> _renderers = new();
    private readonly List<bool> _onTurret = new();
    private int _next;
    private float[] _hullH, _turretH;
    private int[] _hullId, _turretId;
    private readonly Stopwatch _sw = new();

    
    public Silhouette(IntPtr vehicle, Transform hull, Transform turret, float radius, float box)
    {
        Vehicle = vehicle;
        Hull = hull;
        Turret = turret;
        _radius = radius;
        _box = box;
        HullMin = TurretMin = new Vector2(float.MaxValue, float.MaxValue);
        HullMax = TurretMax = new Vector2(float.MinValue, float.MinValue);
    }

    
    public static Transform MainTurret(Transform hull, Cannon current)
    {
        Transform Of(Cannon c)
        {
            try
            {
                var t = c?.HorizontalAimingMechanism?.TryCast<TurretBehaviour>()?.transform;
                return t != null && !t.WasCollected ? t : null;
            }
            catch { return null; }
        }
        var best = Of(current);
        if (best != null) return best;
        var bestCal = -1;
        try
        {
            foreach (var c in hull.GetComponentsInChildren<Cannon>(true))
            {
                var t = Of(c);
                if (t == null) continue;
                var cal = 0;
                try { cal = c.Blueprint != null ? c.Blueprint.Caliber : 0; } catch { }
                if (cal > bestCal) { bestCal = cal; best = t; }
            }
        }
        catch { }
        return best;
    }

    
    public Vector2 TurretOrigin()
    {
        if (Turret == null || Turret.WasCollected || TurretSpan <= 0f) return HullCenter;
        var m = FrameOf(Hull);
        var p = Turret.position;
        return new Vector2(m.m00 * p.x + m.m01 * p.y + m.m02 * p.z + m.m03, m.m20 * p.x + m.m21 * p.y + m.m22 * p.z + m.m23);
    }

    
    public bool Step(float budgetMs)
    {
        if (Ready || Failed) return true;
        _sw.Restart();
        try
        {
            while (_sw.Elapsed.TotalMilliseconds < budgetMs)
            {
                switch (_stage)
                {
                    case Stage.Collect:
                        Collect();
                        _stage = Stage.Bounds;
                        _next = 0;
                        break;
                    case Stage.Bounds:
                        if (_next >= _renderers.Count)
                        {
                            if (!PrepareMasks()) { Failed = true; return true; }
                            _stage = Stage.Raster;
                            _next = 0;
                            break;
                        }
                        AddBounds(_next++);
                        break;
                    case Stage.Raster:
                        if (_next >= _renderers.Count) { _stage = Stage.Finish; break; }
                        Rasterize(_next);
                        _next++;
                        break;
                    case Stage.Finish:
                        HullTex = BuildTexture(_hullH, _hullId, HullN, HullN / HullSpan / Scale);
                        TurretTex = _turretH != null ? BuildTexture(_turretH, _turretId, TurretN, TurretN / TurretSpan / Scale) : null;
                        _hullH = _turretH = null;
                        _hullId = _turretId = null;
                        _renderers.Clear();
                        Ready = true;
                        return true;
                }
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("Vehicle silhouette failed: " + e.Message);
            Failed = true;
            return true;
        }
        return false;
    }

    public int SkippedCombined, BakedOriginals, SkippedHidden, SkippedCrew, SkippedFar;
    private readonly List<bool> _far = new();

    
    private static Matrix4x4 FrameOf(Transform t) => FrameOf(t.position, t.rotation);

    
    
    
    private int _yawAxis = -1;      
    public float Yaw0;              

    public float TurretYaw()
    {
        if (Turret == null || Turret.WasCollected) return Yaw0;
        var h = FrameOf(Hull);
        Vector3 Local(Vector3 v) => new(h.m00 * v.x + h.m01 * v.y + h.m02 * v.z, h.m10 * v.x + h.m11 * v.y + h.m12 * v.z,
            h.m20 * v.x + h.m21 * v.y + h.m22 * v.z);
        if (_yawAxis < 0)
        {
            var best = 2f;
            var axes = new[] { Turret.forward, Turret.right, Turret.up };
            for (var i = 0; i < 3; i++)
            {
                var vertical = Mathf.Abs(Local(axes[i]).y);
                if (vertical < best) { best = vertical; _yawAxis = i; }
            }
        }
        var a = Local(_yawAxis == 0 ? Turret.forward : _yawAxis == 1 ? Turret.right : Turret.up);
        return Mathf.Atan2(a.x, a.z) * Mathf.Rad2Deg;
    }

    private Matrix4x4 TurretFrame()
    {
        var delta = (TurretYaw() - Yaw0) * Mathf.Deg2Rad;
        var c = Mathf.Cos(delta);
        var s = Mathf.Sin(delta);
        var rot = new Matrix4x4();
        rot.m00 = c; rot.m02 = -s;
        rot.m11 = 1f;
        rot.m20 = s; rot.m22 = c;
        rot.m33 = 1f;
        var m = Mul(rot, FrameOf(Turret.position, Hull.rotation));
        m.m33 = 1f;
        return m;
    }

    private static Matrix4x4 FrameOf(Vector3 p, Quaternion q)
    {
        
        float x = q.x, y = q.y, z = q.z, w = q.w;
        float r00 = 1f - 2f * (y * y + z * z), r01 = 2f * (x * y - z * w), r02 = 2f * (x * z + y * w);
        float r10 = 2f * (x * y + z * w), r11 = 1f - 2f * (x * x + z * z), r12 = 2f * (y * z - x * w);
        float r20 = 2f * (x * z - y * w), r21 = 2f * (y * z + x * w), r22 = 1f - 2f * (x * x + y * y);
        var m = new Matrix4x4();
        m.m00 = r00; m.m01 = r10; m.m02 = r20;
        m.m10 = r01; m.m11 = r11; m.m12 = r21;
        m.m20 = r02; m.m21 = r12; m.m22 = r22;
        m.m03 = -(m.m00 * p.x + m.m01 * p.y + m.m02 * p.z);
        m.m13 = -(m.m10 * p.x + m.m11 * p.y + m.m12 * p.z);
        m.m23 = -(m.m20 * p.x + m.m21 * p.y + m.m22 * p.z);
        m.m33 = 1f;
        return m;
    }

    
    
    
    
    private void Collect()
    {
        if (Turret != null) Yaw0 = TurretYaw();
        foreach (var r in Hull.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null || r.forceRenderingOff) continue;
            var go = r.gameObject;
            if (go == null || !go.activeInHierarchy) continue;
            
            if (r.TryCast<MeshRenderer>() == null) { if (r.TryCast<SkinnedMeshRenderer>() != null) SkippedCrew++; continue; }
            string name;
            try { name = go.name ?? ""; } catch { name = ""; }
            if (name.Contains("Combined") || r.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly)
            {
                SkippedCombined++;
                continue;
            }
            if (!r.enabled)
            {
                if (!HiddenByBaking(r)) { SkippedHidden++; continue; }
                BakedOriginals++;
            }
            var e = r.bounds.extents;
            if (e.sqrMagnitude < 1e-6f || e.sqrMagnitude > 400f) continue;
            _renderers.Add(r);
            _onTurret.Add(OnTurret(r));
            _far.Add(false);
        }
        RendererCount = _renderers.Count;
        LogStructure();
    }

    
    private static readonly HashSet<IntPtr> Logged = new();

    private void LogStructure()
    {
        if (!Logged.Add(Vehicle)) return;
        try
        {
            var axis = _yawAxis switch { 0 => "forward", 1 => "right", 2 => "up", _ => "-" };
            Plugin.Log.LogInfo($"Silhouette: turret transform '{Path(Turret)}' (heading from its {axis} axis, {Yaw0:0}°), turret root '{Path(TurretRoot())}', " +
                               $"turret part '{(_turretVtSearched ? Path(_turretVt != null ? _turretVt.transform : null) : "not needed")}'");
            var order = new List<int>();
            for (var i = 0; i < _renderers.Count; i++) order.Add(i);
            float Size(int i)
            {
                try { var e = _renderers[i].bounds.extents; return e.x * e.z; } catch { return 0f; }
            }
            order.Sort((a, b) => Size(b).CompareTo(Size(a)));
            for (var k = 0; k < Math.Min(14, order.Count); k++)
            {
                var r = _renderers[order[k]];
                var e = r.bounds.extents;
                Mesh mesh = null;
                try { mesh = r.GetComponent<MeshFilter>()?.sharedMesh; } catch { }
                Plugin.Log.LogInfo($"Silhouette:  {(_onTurret[order[k]] ? "TURRET" : "hull  ")} {(r.enabled ? "on " : "off")} {e.x * 2f:0.0}×{e.z * 2f:0.0} m " +
                                   $"mesh {(mesh == null ? "-" : (mesh.isReadable ? "readable" : "locked"))} '{Path(r.transform)}'");
            }
        }
        catch (Exception e) { Plugin.Log.LogWarning("Silhouette structure log failed: " + e.Message); }
    }

    private string Path(Transform t)
    {
        if (t == null) return "none";
        try
        {
            var s = t.name;
            var p = t.parent;
            for (var d = 0; d < 4 && p != null && p.Pointer != Hull.Pointer; d++, p = p.parent) s = p.name + "/" + s;
            return s;
        }
        catch { return "?"; }
    }

    
    private static bool HiddenByBaking(Renderer r)
    {
        const int baked = (int)Sprocket.Vehicles.VehicleRendererHideFlags.Baked;
        const int other = (int)(Sprocket.Vehicles.VehicleRendererHideFlags.User | Sprocket.Vehicles.VehicleRendererHideFlags.DetailLevel |
                                Sprocket.Vehicles.VehicleRendererHideFlags.MarkedForDestroy);
        try
        {
            Sprocket.Vehicles.VehicleRenderer vr = null;
            var g = r.GetComponentInParent<Sprocket.Vehicles.VehicleRendererGroup>();
            var lods = g != null ? g.lods : null;
            if (lods != null)
                foreach (var l in lods)
                    if (l != null && l.Renderer != null && l.Renderer.Pointer == r.Pointer) { vr = l; break; }
            if (vr == null)
            {
                var ps = r.GetComponentInParent<Sprocket.Vehicles.PlateStructures.PlateStructure>();
                var hd = ps != null ? ps.highDetailRenderer : null;
                if (hd != null && hd.Renderer != null && hd.Renderer.Pointer == r.Pointer) vr = hd;
            }
            if (vr == null) return false;
            var flags = (int)vr.HideFlags;
            return (flags & baked) != 0 && (flags & other) == 0;
        }
        catch { return false; }
    }

    
    
    
    private Sprocket.Vehicles.VehicleTransform _turretVt;
    private bool _turretVtSearched;
    private Dictionary<int, Sprocket.Vehicles.VehicleObject> _byVuid;

    
    
    
    private Transform _turretRoot;
    private bool _turretRootSearched;

    private Transform TurretRoot()
    {
        if (_turretRootSearched) return _turretRoot;
        _turretRootSearched = true;
        _turretRoot = Turret;
        try
        {
            var t = Turret;
            for (var d = 0; t != null && d < 32; d++)
            {
                var p = t.parent;
                if (p == null) { _turretRoot = Turret; break; }      
                if (p.Pointer == Hull.Pointer) { _turretRoot = t; break; }
                t = p;
            }
        }
        catch { _turretRoot = Turret; }
        return _turretRoot;
    }

    private bool OnTurret(Renderer r)
    {
        if (Turret == null) return false;
        var root = TurretRoot();
        if (root != null && r.transform.IsChildOf(root)) { TurretByTransform++; return true; }
        if (!_turretVtSearched)
        {
            _turretVtSearched = true;
            try { _turretVt = Turret.GetComponentInParent<Sprocket.Vehicles.VehicleTransform>(); } catch { }
        }
        if (_turretVt == null) return false;
        try
        {
            var vo = r.GetComponentInParent<Sprocket.Vehicles.VehicleObject>();
            if (vo == null)
            {
                var g = r.GetComponentInParent<Sprocket.Vehicles.VehicleRendererGroup>();
                if (g != null)
                {
                    if (_byVuid == null)
                    {
                        _byVuid = new Dictionary<int, Sprocket.Vehicles.VehicleObject>();
                        foreach (var o in Hull.GetComponentsInChildren<Sprocket.Vehicles.VehicleObject>(true))
                            if (o != null) _byVuid[o.VUID.Value] = o;
                    }
                    _byVuid.TryGetValue(g.id.Value, out vo);
                }
            }
            var vt = vo != null ? vo.VehicleTransform : null;
            for (var d = 0; vt != null && d < 64; d++, vt = vt.Parent)
                if (vt.Pointer == _turretVt.Pointer) { TurretByParts++; return true; }
        }
        catch { }
        return false;
    }

    

    private void AddBounds(int i)
    {
        var r = _renderers[i];
        if (r == null || r.WasCollected) return;
        var turret = _onTurret[i];
        var frame = turret ? TurretFrame() : FrameOf(Hull);
        
        Bounds b;
        Matrix4x4 m;
        Mesh mesh = null;
        try
        {
            var smr = r.TryCast<SkinnedMeshRenderer>();
            if (smr != null) mesh = smr.sharedMesh;
            else
            {
                var mf = r.GetComponent<MeshFilter>();
                mesh = mf != null ? mf.sharedMesh : null;
            }
        }
        catch { mesh = null; }
        if (mesh != null && !mesh.WasCollected) { b = mesh.bounds; m = Mul(frame, r.localToWorldMatrix); }
        else { b = r.bounds; m = frame; }
        var c = b.center;
        var e = b.extents;
        var lx = m.m00 * c.x + m.m01 * c.y + m.m02 * c.z + m.m03;
        var lz = m.m20 * c.x + m.m21 * c.y + m.m22 * c.z + m.m23;
        var ex = Mathf.Abs(m.m00) * e.x + Mathf.Abs(m.m01) * e.y + Mathf.Abs(m.m02) * e.z;
        var ez = Mathf.Abs(m.m20) * e.x + Mathf.Abs(m.m21) * e.y + Mathf.Abs(m.m22) * e.z;
        
        if (Mathf.Abs(lx) > 12f || Mathf.Abs(lz) > 12f || ex > 10f || ez > 10f || float.IsNaN(lx + lz + ex + ez))
        {
            _far[i] = true;
            SkippedFar++;
            if (SkippedFar <= 3)
                Plugin.Log.LogInfo($"Silhouette: skipped far part '{Path(r.transform)}' at {lx:0.0}, {lz:0.0} m, size {ex * 2f:0.0}×{ez * 2f:0.0} m");
            return;
        }
        if (turret)
        {
            TurretMin = new Vector2(Math.Min(TurretMin.x, lx - ex), Math.Min(TurretMin.y, lz - ez));
            TurretMax = new Vector2(Math.Max(TurretMax.x, lx + ex), Math.Max(TurretMax.y, lz + ez));
        }
        else
        {
            HullMin = new Vector2(Math.Min(HullMin.x, lx - ex), Math.Min(HullMin.y, lz - ez));
            HullMax = new Vector2(Math.Max(HullMax.x, lx + ex), Math.Max(HullMax.y, lz + ez));
        }
    }

    private bool PrepareMasks()
    {
        if (HullMax.x <= HullMin.x || HullMax.y <= HullMin.y) return false;
        HullCenter = new Vector2((HullMin.x + HullMax.x) * 0.5f, (HullMin.y + HullMax.y) * 0.5f);
        HullSpan = Mathf.Max(HullMax.x - HullMin.x, HullMax.y - HullMin.y) * 1.06f;
        _hullH = NewHeights(HullN);
        _hullId = NewIds(HullN);
        if (Turret != null && TurretMax.x > TurretMin.x)
        {
            TurretCenter = new Vector2((TurretMin.x + TurretMax.x) * 0.5f, (TurretMin.y + TurretMax.y) * 0.5f);
            TurretSpan = Mathf.Max(TurretMax.x - TurretMin.x, TurretMax.y - TurretMin.y) * 1.06f;
            _turretH = NewHeights(TurretN);
            _turretId = NewIds(TurretN);
        }
        
        var w = HullMax.x - HullMin.x;
        var l = HullMax.y - HullMin.y;
        var reach = 0.5f * Mathf.Sqrt(w * w + l * l);
        if (_turretH != null)
        {
            var tr = 0f;
            foreach (var c in new[] { TurretMin, TurretMax, new Vector2(TurretMin.x, TurretMax.y), new Vector2(TurretMax.x, TurretMin.y) })
                tr = Mathf.Max(tr, c.magnitude);
            var o = TurretOrigin();
            reach = Mathf.Max(reach, new Vector2(o.x - HullCenter.x, o.y - HullCenter.y).magnitude + tr);
        }
        Scale = Mathf.Min(_radius / Mathf.Max(0.5f, reach), _box / Mathf.Max(0.5f, Mathf.Max(w, l)));
        return true;
    }

    private static float[] NewHeights(int n)
    {
        var h = new float[n * n];
        for (var i = 0; i < h.Length; i++) h[i] = float.NegativeInfinity;
        return h;
    }

    private static int[] NewIds(int n)
    {
        var a = new int[n * n];
        for (var i = 0; i < a.Length; i++) a[i] = -1;
        return a;
    }

    

    private void Rasterize(int index)
    {
        var r = _renderers[index];
        if (r == null || r.WasCollected || _far[index]) return;
        var turret = _onTurret[index] && _turretH != null;
        var hgt = turret ? _turretH : _hullH;
        var ids = turret ? _turretId : _hullId;
        var n = turret ? TurretN : HullN;
        var center = turret ? TurretCenter : HullCenter;
        var span = turret ? TurretSpan : HullSpan;
        var k = n / span;
        var ox = center.x - span * 0.5f;
        var oz = center.y - span * 0.5f;

        var rendererToWorld = r.localToWorldMatrix;
        var mf = r.GetComponent<MeshFilter>();
        var mesh = mf != null ? mf.sharedMesh : null;
        var f = turret ? TurretFrame() : FrameOf(Hull);
        if (mesh != null && !mesh.WasCollected && mesh.isReadable)
        {
            var m = Mul(f, rendererToWorld);
            var verts = mesh.vertices;
            var tris = mesh.triangles;
            var count = verts.Length;
            var px = new float[count];
            var pz = new float[count];
            var py = new float[count];
            for (var v = 0; v < count; v++)
            {
                var p = verts[v];
                px[v] = (m.m00 * p.x + m.m01 * p.y + m.m02 * p.z + m.m03 - ox) * k;
                py[v] = m.m10 * p.x + m.m11 * p.y + m.m12 * p.z + m.m13;
                pz[v] = (m.m20 * p.x + m.m21 * p.y + m.m22 * p.z + m.m23 - oz) * k;
            }
            var tc = tris.Length;
            for (var t = 0; t + 2 < tc; t += 3)
            {
                int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                if ((uint)a >= (uint)count || (uint)b >= (uint)count || (uint)c >= (uint)count) continue;
                Triangle(hgt, ids, n, index, px[a], pz[a], py[a], px[b], pz[b], py[b], px[c], pz[c], py[c]);
            }
            Triangles += tc / 3;
            return;
        }
        
        if (mesh == null || mesh.WasCollected) return;
        Boxes++;
        var lb = mesh.bounds;
        var bm = Mul(f, rendererToWorld);
        var corners = new Vector3[8];
        var ci = 0;
        var bc = lb.center;
        var be = lb.extents;
        for (var sx = -1; sx <= 1; sx += 2)
        for (var sy = -1; sy <= 1; sy += 2)
        for (var sz = -1; sz <= 1; sz += 2)
        {
            float x = bc.x + be.x * sx, y = bc.y + be.y * sy, z = bc.z + be.z * sz;
            corners[ci++] = new Vector3((bm.m00 * x + bm.m01 * y + bm.m02 * z + bm.m03 - ox) * k,
                                        bm.m10 * x + bm.m11 * y + bm.m12 * z + bm.m13,
                                        (bm.m20 * x + bm.m21 * y + bm.m22 * z + bm.m23 - oz) * k);
        }
        
        for (var a = 0; a < 8; a++)
        for (var b = a + 1; b < 8; b++)
        for (var c = b + 1; c < 8; c++)
            Triangle(hgt, ids, n, index, corners[a].x, corners[a].z, corners[a].y, corners[b].x, corners[b].z, corners[b].y,
                corners[c].x, corners[c].z, corners[c].y);
    }

    
    private static Matrix4x4 Mul(Matrix4x4 a, Matrix4x4 b)
    {
        var r = new Matrix4x4();
        r.m00 = a.m00 * b.m00 + a.m01 * b.m10 + a.m02 * b.m20 + a.m03 * b.m30;
        r.m01 = a.m00 * b.m01 + a.m01 * b.m11 + a.m02 * b.m21 + a.m03 * b.m31;
        r.m02 = a.m00 * b.m02 + a.m01 * b.m12 + a.m02 * b.m22 + a.m03 * b.m32;
        r.m03 = a.m00 * b.m03 + a.m01 * b.m13 + a.m02 * b.m23 + a.m03 * b.m33;
        r.m10 = a.m10 * b.m00 + a.m11 * b.m10 + a.m12 * b.m20 + a.m13 * b.m30;
        r.m11 = a.m10 * b.m01 + a.m11 * b.m11 + a.m12 * b.m21 + a.m13 * b.m31;
        r.m12 = a.m10 * b.m02 + a.m11 * b.m12 + a.m12 * b.m22 + a.m13 * b.m32;
        r.m13 = a.m10 * b.m03 + a.m11 * b.m13 + a.m12 * b.m23 + a.m13 * b.m33;
        r.m20 = a.m20 * b.m00 + a.m21 * b.m10 + a.m22 * b.m20 + a.m23 * b.m30;
        r.m21 = a.m20 * b.m01 + a.m21 * b.m11 + a.m22 * b.m21 + a.m23 * b.m31;
        r.m22 = a.m20 * b.m02 + a.m21 * b.m12 + a.m22 * b.m22 + a.m23 * b.m32;
        r.m23 = a.m20 * b.m03 + a.m21 * b.m13 + a.m22 * b.m23 + a.m23 * b.m33;
        return r;
    }

    
    
    private static void Triangle(float[] hgt, int[] ids, int n, int id,
        float ax, float ay, float ah, float bx, float by, float bh, float cx, float cy, float ch)
    {
        var minX = (int)Math.Floor(Math.Min(ax, Math.Min(bx, cx)));
        var maxX = (int)Math.Ceiling(Math.Max(ax, Math.Max(bx, cx)));
        var minY = (int)Math.Floor(Math.Min(ay, Math.Min(by, cy)));
        var maxY = (int)Math.Ceiling(Math.Max(ay, Math.Max(by, cy)));
        if (maxX < 0 || maxY < 0 || minX >= n || minY >= n) return;
        Line(hgt, ids, n, id, ax, ay, ah, bx, by, bh);
        Line(hgt, ids, n, id, bx, by, bh, cx, cy, ch);
        Line(hgt, ids, n, id, cx, cy, ch, ax, ay, ah);
        var area = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
        if (Math.Abs(area) < 1e-4f) return;
        minX = Math.Max(minX, 0); minY = Math.Max(minY, 0);
        maxX = Math.Min(maxX, n - 1); maxY = Math.Min(maxY, n - 1);
        var sign = area > 0 ? 1f : -1f;
        var inv = 1f / Math.Abs(area);
        for (var y = minY; y <= maxY; y++)
        {
            var py = y + 0.5f;
            var row = y * n;
            for (var x = minX; x <= maxX; x++)
            {
                var px = x + 0.5f;
                var w0 = ((cx - bx) * (py - by) - (cy - by) * (px - bx)) * sign;
                if (w0 < 0f) continue;
                var w1 = ((ax - cx) * (py - cy) - (ay - cy) * (px - cx)) * sign;
                if (w1 < 0f) continue;
                var w2 = ((bx - ax) * (py - ay) - (by - ay) * (px - ax)) * sign;
                if (w2 < 0f) continue;
                var h = (w0 * ah + w1 * bh + w2 * ch) * inv;
                var i = row + x;
                if (h > hgt[i]) { hgt[i] = h; ids[i] = id; }
            }
        }
    }

    private static void Line(float[] hgt, int[] ids, int n, int id, float ax, float ay, float ah, float bx, float by, float bh)
    {
        var steps = (int)Math.Ceiling(Math.Max(Math.Abs(bx - ax), Math.Abs(by - ay)));
        if (steps > 4 * n) return;
        for (var s = 0; s <= steps; s++)
        {
            var t = steps == 0 ? 0f : s / (float)steps;
            var x = (int)(ax + (bx - ax) * t);
            var y = (int)(ay + (by - ay) * t);
            if ((uint)x >= (uint)n || (uint)y >= (uint)n) continue;
            var i = y * n + x;
            var h = ah + (bh - ah) * t;
            if (h > hgt[i]) { hgt[i] = h; ids[i] = id; }
        }
    }

    
    private static Texture2D BuildTexture(float[] hgt, int[] ids, int n, float perDisp)
    {
        var total = n * n;
        
        var dist = new int[total];
        const int Far = 1 << 20;
        for (var i = 0; i < total; i++) dist[i] = ids[i] >= 0 ? Far : 0;
        for (var y = 0; y < n; y++)
        for (var x = 0; x < n; x++)
        {
            var i = y * n + x;
            if (dist[i] == 0) continue;
            var d = Math.Min(x > 0 ? dist[i - 1] + 1 : 1, y > 0 ? dist[i - n] + 1 : 1);
            if (d < dist[i]) dist[i] = d;
        }
        for (var y = n - 1; y >= 0; y--)
        for (var x = n - 1; x >= 0; x--)
        {
            var i = y * n + x;
            if (dist[i] == 0) continue;
            var d = Math.Min(x < n - 1 ? dist[i + 1] + 1 : 1, y < n - 1 ? dist[i + n] + 1 : 1);
            if (d < dist[i]) dist[i] = d;
        }
        var outer = Math.Max(1, (int)Math.Round(OutlinePx * perDisp));
        var inner = Math.Max(1, (int)Math.Round(InnerPx * perDisp));

        
        float hmin = float.MaxValue, hmax = float.MinValue;
        for (var i = 0; i < total; i++)
            if (ids[i] >= 0) { hmin = Math.Min(hmin, hgt[i]); hmax = Math.Max(hmax, hgt[i]); }
        var hr = Math.Max(0.05f, hmax - hmin);

        
        var line = new byte[total];
        for (var y = 1; y < n - 1; y++)
        for (var x = 1; x < n - 1; x++)
        {
            var i = y * n + x;
            if (ids[i] < 0) continue;
            var h = hgt[i];
            if (Higher(i - 1) || Higher(i + 1) || Higher(i - n) || Higher(i + n)) line[i] = 1;

            bool Higher(int j) => ids[j] >= 0 && ids[j] != ids[i] && h - hgt[j] > StepMeters;
        }
        for (var pass = 1; pass < inner; pass++)
        {
            var grown = (byte[])line.Clone();
            for (var y = 1; y < n - 1; y++)
            for (var x = 1; x < n - 1; x++)
            {
                var i = y * n + x;
                if (line[i] != 0 || ids[i] < 0) continue;
                if (line[i - 1] != 0 || line[i + 1] != 0 || line[i - n] != 0 || line[i + n] != 0) grown[i] = 1;
            }
            line = grown;
        }

        var m = n / 2;
        var px = new Color32[m * m];
        for (var y = 0; y < m; y++)
        for (var x = 0; x < m; x++)
        {
            var a = 0f;
            for (var dy = 0; dy < 2; dy++)
            for (var dx = 0; dx < 2; dx++)
            {
                var i = (y * 2 + dy) * n + x * 2 + dx;
                if (ids[i] < 0) continue;
                var v = 0.14f + 0.3f * Mathf.Clamp01((hgt[i] - hmin) / hr);
                if (line[i] != 0) v = Math.Max(v, 0.6f);
                if (dist[i] <= outer) v = 1f;
                a += v;
            }
            px[y * m + x] = new Color32(255, 255, 255, (byte)(255 * Math.Min(1f, a * 0.25f)));
        }
        var t = new Texture2D(m, m, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "Vehicle silhouette" };
        t.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        t.SetPixels32(px);
        t.Apply(false);
        return t;
    }
}
