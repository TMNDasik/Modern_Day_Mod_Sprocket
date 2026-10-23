using System;
using System.Collections.Generic;
using Sprocket.Vehicles;
using Sprocket.Vehicles.PlateStructures;
using UnityEngine;
using PmLoop = Sprocket.PlateMesh.Loop;

namespace ActiveProtection;






internal static class EraGeometry
{
    private const float SameNormal = 0.9976f;     
    private const float MainFaceCos = 0.5f;       
    private const int MaxPanels = 16;

    private sealed class Group
    {
        public Vector3 N;
        public float Plane;
        public readonly List<Vector3> Pts = new();
        public float Area, Thick;
    }

    
    public static Vector3 PartMin, PartMax, MirMin, MirMax;

    public static bool FromStructure(VehicleObject vo, PlateStructure plate, List<EraFace> panels)
    {
        PartMin = MirMin = Vector3.positiveInfinity;
        PartMax = MirMax = Vector3.negativeInfinity;
        if (!plate.HasMesh) return false;
        var em = plate.Mesh?.EditMesh;
        var faces = em?.faces;
        if (faces == null || faces.Count == 0) return false;

        var t = vo.transform;
        var vehicle = vo.GetComponentInParent<Vehicle>();
        var vUp = t.InverseTransformDirection(vehicle != null ? vehicle.transform.up : Vector3.up).normalized;
        var vFwd = t.InverseTransformDirection(vehicle != null ? vehicle.transform.forward : Vector3.forward).normalized;
        var reference = t.InverseTransformPoint(vehicle != null ? vehicle.transform.position + vehicle.transform.up : t.root.position);
        float units;
        try { units = PmLoop.ThicknessToWorldUnits; }
        catch { units = 0.001f; }

        
        var raw = new List<(Vector3[] S, float Thick, Vector3 N)>();
        for (var i = 0; i < faces.Count; i++)
        {
            var f = faces[i];
            if (f == null) continue;
            var n = f.vertexCount;
            if (n < 3 || n > 256) continue;
            var pts = new Vector3[n];
            var thick = 0f;
            var loop = f.firstLoop;
            var k = 0;
            for (; k < n && loop != null; k++)
            {
                var v = loop.vertex;
                if (v == null) break;
                pts[k] = v.position;
                thick += loop.thickness;
                loop = loop.next;
            }
            if (k < n) continue;
            var mm = thick / n * units * 1000f;
            if (!(mm > 0.5f && mm < 3000f)) mm = 20f;
            raw.Add((pts, mm, f.normal));
        }
        if (raw.Count == 0) return false;

        Vector3 L(Vector3 s) => t.InverseTransformPoint(plate.StructureToWorldSpace(s));

        
        var groups = Groups(raw, L, null);
        Select(groups, reference, vUp, vFwd, false, panels);

        
        var mirrored = false;
        try { mirrored = plate.Mirrored; } catch { }
        if (mirrored)
        {
            plate.GetMirrorMatrix(out var m);
            var extra = new List<EraFace>();
            Select(Groups(raw, L, m), reference, vUp, vFwd, true, extra);
            foreach (var p in extra)
            {
                var duplicate = false;
                foreach (var q in panels)
                    if ((p.Center - q.Center).sqrMagnitude < 25f && Vector3.Dot(p.Out, q.Out) > 0.999f) { duplicate = true; break; }
                if (!duplicate) panels.Add(p);
            }
        }
        return panels.Count > 0;
    }

    
    private static List<Group> Groups(List<(Vector3[] S, float Thick, Vector3 N)> raw, Func<Vector3, Vector3> toLocal, Matrix4x4? mirror)
    {
        var groups = new List<Group>();
        foreach (var (s, thick, fn) in raw)
        {
            var pts = new Vector3[s.Length];
            for (var i = 0; i < s.Length; i++)
            {
                pts[i] = toLocal(mirror.HasValue ? mirror.Value.MultiplyPoint3x4(s[i]) : s[i]);
                if (mirror.HasValue)
                {
                    MirMin = Vector3.Min(MirMin, pts[i]);
                    MirMax = Vector3.Max(MirMax, pts[i]);
                }
                else
                {
                    PartMin = Vector3.Min(PartMin, pts[i]);
                    PartMax = Vector3.Max(PartMax, pts[i]);
                }
            }
            
            var nrm = Vector3.zero;
            for (var i = 0; i < pts.Length; i++)
            {
                var a = pts[i];
                var b = pts[(i + 1) % pts.Length];
                nrm.x += (a.y - b.y) * (a.z + b.z);
                nrm.y += (a.z - b.z) * (a.x + b.x);
                nrm.z += (a.x - b.x) * (a.y + b.y);
            }
            var area = nrm.magnitude * 0.5f;
            if (area < 1e-6f) continue;
            nrm.Normalize();
            if (mirror.HasValue) nrm = -nrm;
            if (fn.sqrMagnitude > 0.5f)
            {
                var s0 = mirror.HasValue ? mirror.Value.MultiplyPoint3x4(s[0]) : s[0];
                var dirS = mirror.HasValue ? mirror.Value.MultiplyVector(fn) : fn;
                var gameN = toLocal(s0 + dirS * 0.1f) - toLocal(s0);
                if (gameN.sqrMagnitude > 1e-8f && Vector3.Dot(gameN, nrm) < 0f) nrm = -nrm;
            }
            var plane = Vector3.Dot(nrm, pts[0]);
            Group g = null;
            foreach (var x in groups)
                if (Vector3.Dot(x.N, nrm) > SameNormal && Mathf.Abs(x.Plane - plane) < 0.01f) { g = x; break; }
            if (g == null)
            {
                g = new Group { N = nrm, Plane = plane };
                groups.Add(g);
            }
            g.Pts.AddRange(pts);
            g.Area += area;
            g.Thick = Mathf.Max(g.Thick, thick);
        }
        return groups;
    }

    
    private static void Select(List<Group> groups, Vector3 reference, Vector3 vUp, Vector3 vFwd, bool mirror, List<EraFace> panels)
    {
        var outward = new List<(Group G, EraFace P)>();
        foreach (var g in groups)
        {
            var center = Vector3.zero;
            foreach (var p in g.Pts) center += p;
            center /= g.Pts.Count;
            if (Vector3.Dot(g.N, center - reference) <= 0f) continue;
            var up = UpFor(g.N, vUp, vFwd);
            var side = Vector3.Cross(up, g.N).normalized;
            float s0 = float.MaxValue, s1 = float.MinValue, u0 = float.MaxValue, u1 = float.MinValue;
            var origin = g.Pts[0];
            foreach (var p in g.Pts)
            {
                var d = p - origin;
                var s = Vector3.Dot(d, side);
                var u = Vector3.Dot(d, up);
                s0 = Mathf.Min(s0, s);
                s1 = Mathf.Max(s1, s);
                u0 = Mathf.Min(u0, u);
                u1 = Mathf.Max(u1, u);
            }
            var panel = new EraFace
            {
                Out = g.N, Up = up, Mirror = mirror,
                Center = (origin + side * ((s0 + s1) * 0.5f) + up * ((u0 + u1) * 0.5f)) * 1000f,
                W = (s1 - s0) * 1000f, H = (u1 - u0) * 1000f, D = g.Thick
            };
            outward.Add((g, Rounded(panel)));
        }
        if (outward.Count == 0) return;
        outward.Sort((a, b) => b.G.Area.CompareTo(a.G.Area));
        var largest = outward[0].G.Area;
        var main = outward[0].G.N;
        foreach (var (g, p) in outward)
        {
            if (panels.Count >= MaxPanels) break;
            if (g.Area < largest * 0.02f) continue;
            
            if (Vector3.Dot(g.N, main) < MainFaceCos) continue;
            
            if (Mathf.Min(p.W, p.H) < 60f && g.Area < largest * 0.1f) continue;
            if (Mathf.Min(p.W, p.H) < 10f) continue;
            panels.Add(p);
        }
    }

    
    public static Vector3 UpFor(Vector3 n, Vector3 vUp, Vector3 vFwd)
    {
        var u = vUp - n * Vector3.Dot(n, vUp);
        if (u.sqrMagnitude < 0.09f) u = vFwd - n * Vector3.Dot(n, vFwd);
        if (u.sqrMagnitude < 0.01f) u = Vector3.Cross(n, Mathf.Abs(n.x) < 0.9f ? Vector3.right : Vector3.up);
        return u.normalized;
    }

    public static EraFace Rounded(EraFace p)
    {
        static float R(float v, float step) => Mathf.Round(v / step) * step;
        p.Center = new Vector3(R(p.Center.x, 0.1f), R(p.Center.y, 0.1f), R(p.Center.z, 0.1f));
        p.Out = new Vector3(R(p.Out.x, 0.0001f), R(p.Out.y, 0.0001f), R(p.Out.z, 0.0001f)).normalized;
        p.Up = new Vector3(R(p.Up.x, 0.0001f), R(p.Up.y, 0.0001f), R(p.Up.z, 0.0001f)).normalized;
        p.W = R(p.W, 0.1f);
        p.H = R(p.H, 0.1f);
        p.D = R(p.D, 0.1f);
        return p;
    }

    public static bool Same(List<EraFace> a, List<EraFace> b)
    {
        if (a == null || b == null || a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
        {
            var p = a[i];
            var q = b[i];
            if (p.Mirror != q.Mirror || (p.Center - q.Center).sqrMagnitude > 0.25f || Vector3.Dot(p.Out, q.Out) < 0.9995f ||
                Vector3.Dot(p.Up, q.Up) < 0.9995f || Mathf.Abs(p.W - q.W) > 0.5f || Mathf.Abs(p.H - q.H) > 0.5f || Mathf.Abs(p.D - q.D) > 0.5f)
                return false;
        }
        return true;
    }
}
