using System;
using System.Collections.Generic;
using HarmonyLib;
using Sprocket.PlateMesh;
using Sprocket.Vehicles;
using Sprocket.Vehicles.PlateStructures;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace VehicleMaterials;








internal static class Applier
{
    private sealed class Proxy
    {
        public VehicleObject Vo;
        public PartMaterials M;
        public int MVersion = -1;
        public MeshRenderer Src;
        public MeshFilter SrcF;
        public PlateStructure Plate;
        public IntPtr MeshObj;
        public int[] Tags;           
        public int[] BuiltTags;      
        public readonly List<Renderer> Hidden = new();
        public readonly List<VehicleRenderer> Owners = new();
        public GameObject Go;
        public MeshFilter F;
        public MeshRenderer R;
        public Mesh Mesh;
        public int SrcMeshId, SrcVerts, SrcSubs;
        public Bounds SrcBounds;
        public Material[] SrcMats;
        public int[] OrigIndex;      
        public bool Seen;
        public bool Unreadable;
        public bool HidingOriginals;
        
        public int[][] TriFace;
        public int[] MapTris;
        public int MapFaces = -1;
        public bool SrcDirty;
        public bool ForceMatch;
        public float RefineAt;
    }

    private static readonly Dictionary<IntPtr, Proxy> Proxies = new();   
    private static readonly HashSet<IntPtr> DirtyMeshes = new();
    private static readonly MaterialPropertyBlock Mpb = new();
    private static float _nextScan;
    private static int _errors;

    

    public static void Tick()
    {
        if (DirtyMeshes.Count > 0)
        {
            foreach (var p in Proxies.Values)
                if (p.MeshObj != IntPtr.Zero && DirtyMeshes.Contains(p.MeshObj)) p.SrcDirty = true;
            DirtyMeshes.Clear();
        }
        if (Time.unscaledTime >= _nextScan)
        {
            _nextScan = Time.unscaledTime + 0.25f;
            try { Scan(); }
            catch (Exception e) { if (_errors++ < 10) Plugin.Log.LogError("Vehicle materials scan failed: " + e); }
        }
        foreach (var p in Proxies.Values)
        {
            try
            {
                if (p.SrcDirty && !p.Unreadable) Build(p);
                else if (p.RefineAt > 0f && Time.unscaledTime >= p.RefineAt)
                {
                    p.ForceMatch = true;
                    Build(p);
                }
                Sync(p);
            }
            catch (Exception e) { if (_errors++ < 10) Plugin.Log.LogWarning("Material proxy sync failed: " + e.Message); }
        }
        Panel.Poll();
    }

    
    public static void Changed() => _nextScan = 0f;

    
    public static void MeshRebuilt(IntPtr meshObject) => DirtyMeshes.Add(meshObject);

    private static void Scan()
    {
        foreach (var p in Proxies.Values) p.Seen = false;
        foreach (var (part, m) in Store.Snapshot())
        {
            foreach (var vo in WithAttached(part))
            foreach (var comp in vo.behaviours)
            {
                if (comp == null) continue;
                var plate = comp.TryCast<PlateStructure>();
                if (plate != null)
                {
                    var em = FaceTags.Mesh(plate);
                    if (vo.Pointer == part.Pointer) ApplyPending(part, m, em);
                    var tags = em != null ? FaceTags.Read(em) : null;
                    if (vo.Pointer == part.Pointer && !m.FacesPending && tags != null) m.Faces = tags;
                    if (m.Whole == MaterialKind.Default && !AnyTag(tags)) continue;
                    var hi = plate.highDetailRenderer;
                    if (hi == null) continue;
                    var p = Take(vo, m, hi.renderer, hi.filter);
                    if (p == null) continue;
                    p.Plate = plate;
                    p.Tags = tags;
                    try { p.MeshObj = plate.Mesh != null ? plate.Mesh.Pointer : IntPtr.Zero; } catch { p.MeshObj = IntPtr.Zero; }
                    AddHidden(p, hi);
                    if (plate.lowDetailRenderer != null) AddHidden(p, plate.lowDetailRenderer);
                    continue;
                }
                if (m.Whole == MaterialKind.Default) continue;
                var models = comp.models;
                if (models == null) continue;
                for (var g = 0; g < models.Count; g++)
                {
                    var group = models[g];
                    var lods = group != null ? group.lods : null;
                    if (lods == null || lods.Length == 0) continue;
                    var lod0 = lods[0]?.TryCast<VehicleMeshRenderer>();
                    if (lod0 == null) continue;
                    var p = Take(vo, m, lod0.renderer, lod0.filter);
                    if (p == null) continue;
                    foreach (var lod in lods) if (lod != null) AddHidden(p, lod);
                }
            }
        }
        List<IntPtr> gone = null;
        foreach (var kv in Proxies)
        {
            var p = kv.Value;
            if (!p.Seen) { (gone ??= new List<IntPtr>()).Add(kv.Key); continue; }
            if (NeedsRebuild(p)) Build(p);
        }
        if (gone != null)
            foreach (var k in gone)
            {
                Release(Proxies[k]);
                Proxies.Remove(k);
            }
    }

    
    private static void ApplyPending(VehicleObject vo, PartMaterials m, EditMesh em)
    {
        if (!m.FacesPending || m.Faces == null || em == null) return;
        var n = FaceTags.Count(em);
        if (n == m.Faces.Length)
        {
            FaceTags.WriteAll(em, m.Faces);
            m.FacesPending = false;
            if (Plugin.DebugLogging.Value) Plugin.Log.LogInfo($"Face materials restored on '{vo.name}' ({n} faces)");
        }
        else if (n > 0 && Time.unscaledTime - m.PendingSince > 5f)
        {
            m.FacesPending = false;
            Plugin.Log.LogWarning($"Face materials of '{vo.name}' were saved for {m.Faces.Length} faces, the structure has {n} — skipped");
        }
    }

    private static bool AnyTag(int[] tags)
    {
        if (tags == null) return false;
        foreach (var t in tags) if (t != 0) return true;
        return false;
    }

    
    private const string BarrelSegmentGuid = "c56166b7-89f3-4791-9a22-a9c3c8e8ce16";

    private static List<VehicleObject> WithAttached(VehicleObject part)
    {
        var list = new List<VehicleObject> { part };
        try
        {
            var children = part.VehicleTransform?.children;
            if (children == null) return list;
            for (var i = 0; i < children.Count; i++)
            {
                var child = children[i]?.VehicleObject;
                if (child != null && child.guid == BarrelSegmentGuid) list.Add(child);
            }
        }
        catch { }
        return list;
    }

    
    private static PartMaterials OwnMaterials(VehicleObject vo)
    {
        var m = Store.Get(vo);
        if (m != null && !m.IsDefault) return m;
        try
        {
            if (vo != null && vo.guid == BarrelSegmentGuid) return Store.Get(vo.VehicleTransform?.Parent?.VehicleObject);
        }
        catch { }
        return null;
    }

    private static Proxy Take(VehicleObject vo, PartMaterials m, MeshRenderer src, MeshFilter filter)
    {
        if (src == null || filter == null || src.WasCollected) return null;
        if (!Proxies.TryGetValue(src.Pointer, out var p) || p.Src == null || p.Src.WasCollected)
        {
            p = new Proxy { Src = src, SrcF = filter };
            Proxies[src.Pointer] = p;
        }
        p.Vo = vo;
        p.M = m;
        p.SrcF = filter;
        p.Seen = true;
        return p;
    }

    private static void AddHidden(Proxy p, VehicleRenderer vr)
    {
        var r = vr.renderer;
        if (r == null) return;
        if (!p.Hidden.Exists(x => x != null && x.Pointer == r.Pointer)) p.Hidden.Add(r);
        if (!p.Owners.Exists(x => x != null && x.Pointer == vr.Pointer)) p.Owners.Add(vr);
    }

    private static bool NeedsRebuild(Proxy p)
    {
        if (p.Unreadable) return false;
        if (p.Go == null || p.Go.WasCollected || p.Mesh == null || p.Mesh.WasCollected) return true;
        if (p.MVersion != p.M.Version || p.SrcDirty) return true;
        if (p.Plate != null && !SameTags(p.Tags, p.BuiltTags)) return true;
        var mesh = p.SrcF.sharedMesh;
        if (mesh == null) return false;
        if (mesh.GetInstanceID() != p.SrcMeshId || mesh.vertexCount != p.SrcVerts || mesh.subMeshCount != p.SrcSubs) return true;
        if (mesh.bounds != p.SrcBounds) return true;
        var mats = p.Src.sharedMaterials;
        if (mats == null || p.SrcMats == null || mats.Length != p.SrcMats.Length) return true;
        for (var i = 0; i < mats.Length; i++)
            if ((mats[i] == null) != (p.SrcMats[i] == null) || (mats[i] != null && mats[i].Pointer != p.SrcMats[i].Pointer)) return true;
        return false;
    }

    private static bool SameTags(int[] a, int[] b)
    {
        if (a == null || b == null) return a == b;
        if (a.Length != b.Length) return false;
        for (var i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    

    private static void Build(Proxy p)
    {
        var src = p.SrcF.sharedMesh;
        if (src == null) { p.SrcDirty = false; return; }
        if (!src.isReadable)
        {
            p.Unreadable = true;
            p.SrcDirty = false;
            Plugin.Log.LogWarning($"Mesh '{src.name}' can't be read — material for this part is not available");
            return;
        }
        var mats = p.Src.sharedMaterials;
        if (mats == null || mats.Length == 0) return;
        var srcChanged = p.SrcDirty || src.GetInstanceID() != p.SrcMeshId || src.vertexCount != p.SrcVerts || src.bounds != p.SrcBounds;
        p.SrcDirty = false;
        p.SrcMats = new Material[mats.Length];
        for (var i = 0; i < mats.Length; i++) p.SrcMats[i] = mats[i];
        p.SrcMeshId = src.GetInstanceID();
        p.SrcVerts = src.vertexCount;
        p.SrcSubs = src.subMeshCount;
        p.SrcBounds = src.bounds;
        p.MVersion = p.M.Version;

        var verts = src.vertices;
        var n = verts.Length;
        var subs = src.subMeshCount;
        var subTris = new int[subs][];
        for (var sub = 0; sub < subs; sub++) subTris[sub] = src.GetTriangles(sub);

        
        int[] tags = null;
        if (p.Plate != null)
        {
            var em = FaceTags.Mesh(p.Plate);
            tags = em != null ? FaceTags.Read(em) : null;
            p.Tags = tags;
            var faceCount = tags?.Length ?? 0;
            var mapOk = p.TriFace != null && p.MapFaces == faceCount && SameCounts(p.MapTris, subTris);
            if (em != null && (!mapOk || p.ForceMatch))
            {
                Match(p, em, verts, subTris);
                p.RefineAt = 0f;
            }
            else if (mapOk && srcChanged) p.RefineAt = Time.unscaledTime + 0.5f; 
            p.ForceMatch = false;
        }
        p.BuiltTags = tags;

        
        var orig = new List<int>[Math.Max(mats.Length, subs)];
        var ours = new List<int>[Kinds.Count];
        var whole = p.M.Whole;
        for (var sub = 0; sub < subs; sub++)
        {
            var tris = subTris[sub];
            var map = p.Plate != null && p.TriFace != null && sub < p.TriFace.Length ? p.TriFace[sub] : null;
            var matIndex = Math.Min(sub, mats.Length - 1);
            for (var t = 0; t + 2 < tris.Length; t += 3)
            {
                var kind = whole;
                if (map != null && tags != null)
                {
                    var tri = t / 3;
                    var face = tri < map.Length ? map[tri] : -1;
                    if (face >= 0 && face < tags.Length && Kinds.TryKind(tags[face], out var own)) kind = own;
                }
                var list = kind == MaterialKind.Default || Assets.Get(kind) == null
                    ? orig[matIndex] ??= new List<int>()
                    : ours[(int)kind] ??= new List<int>();
                list.Add(tris[t]);
                list.Add(tris[t + 1]);
                list.Add(tris[t + 2]);
            }
        }

        var partTris = new List<int[]>();
        var partMats = new List<Material>();
        var origIndex = new List<int>();
        for (var i = 0; i < orig.Length; i++)
        {
            if (orig[i] == null || orig[i].Count == 0) continue;
            partTris.Add(orig[i].ToArray());
            partMats.Add(mats[Math.Min(i, mats.Length - 1)]);
            origIndex.Add(Math.Min(i, mats.Length - 1));
        }
        for (var k = 1; k < Kinds.Count; k++)
        {
            if (ours[k] == null || ours[k].Count == 0) continue;
            partTris.Add(ours[k].ToArray());
            partMats.Add(Assets.Get((MaterialKind)k));
            origIndex.Add(-1);
        }

        EnsureObject(p);
        var mesh = p.Mesh;
        mesh.Clear();
        mesh.indexFormat = n > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        mesh.vertices = verts;
        var normals = src.normals;
        if (normals != null && normals.Length == n) mesh.normals = normals;
        var tangents = src.tangents;
        if (tangents != null && tangents.Length == n) mesh.tangents = tangents;
        var colors = src.colors;
        if (colors != null && colors.Length == n) mesh.colors = colors;
        var uv = src.uv;
        if (uv != null && uv.Length == n) mesh.uv = uv;
        var uv2 = src.uv2;
        if (uv2 != null && uv2.Length == n) mesh.uv2 = uv2;
        var uv3 = src.uv3;
        if (uv3 != null && uv3.Length == n) mesh.uv3 = uv3;
        var uv4 = src.uv4;
        if (uv4 != null && uv4.Length == n) mesh.uv4 = uv4;
        mesh.subMeshCount = Math.Max(1, partTris.Count);
        for (var i = 0; i < partTris.Count; i++) mesh.SetTriangles(partTris[i], i);
        mesh.bounds = src.bounds;

        var arr = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Material>(partMats.Count);
        for (var i = 0; i < partMats.Count; i++) arr[i] = partMats[i];
        p.R.sharedMaterials = arr;
        p.OrigIndex = origIndex.ToArray();
    }

    private static bool SameCounts(int[] counts, int[][] subTris)
    {
        if (counts == null || counts.Length != subTris.Length) return false;
        for (var i = 0; i < counts.Length; i++) if (counts[i] != subTris[i].Length / 3) return false;
        return true;
    }

    

    private sealed class FaceGeo
    {
        public Vector3[] O;   
        public Vector3[] I;   
        public Vector3 N;
        public Vector3 Min, Max;
    }

    private const float StrictTol2 = 0.015f * 0.015f;
    private const float LooseTol = 0.06f;
    private const float LooseTol2 = LooseTol * LooseTol;

    private static void Match(Proxy p, EditMesh em, Vector3[] verts, int[][] subTris)
    {
        var started = Time.realtimeSinceStartup;
        var geo = ReadGeometry(em);
        
        var mirrored = false;
        var back = Matrix4x4.identity;
        try
        {
            if (p.Plate.Mirrored)
            {
                p.Plate.GetMirrorMatrix(out var mm);
                back = mm.inverse;
                mirrored = true;
            }
        }
        catch { }

        var map = new int[subTris.Length][];
        var counts = new int[subTris.Length];
        int total = 0, matched = 0;
        for (var sub = 0; sub < subTris.Length; sub++)
        {
            var tris = subTris[sub];
            var m = new int[tris.Length / 3];
            counts[sub] = m.Length;
            for (var t = 0; t < m.Length; t++)
            {
                var a = verts[tris[t * 3]];
                var b = verts[tris[t * 3 + 1]];
                var c = verts[tris[t * 3 + 2]];
                var centre = (a + b + c) / 3f;
                var normal = Vector3.Cross(b - a, c - a);
                var face = Nearest(geo, centre, normal, out var d);
                if (mirrored && d > StrictTol2)
                {
                    var face2 = Nearest(geo, back.MultiplyPoint3x4(centre), back.MultiplyVector(normal), out var d2);
                    if (d2 < d) { face = face2; d = d2; }
                }
                m[t] = d <= LooseTol2 ? face : -1;
                total++;
                if (m[t] >= 0) matched++;
            }
            map[sub] = m;
        }
        p.TriFace = map;
        p.MapTris = counts;
        p.MapFaces = geo.Length;
        if (Plugin.DebugLogging.Value)
            Plugin.Log.LogInfo($"Face map for '{p.Vo?.name}': {matched}/{total} triangles on {geo.Length} faces" +
                               $"{(mirrored ? " (mirrored)" : "")} in {(Time.realtimeSinceStartup - started) * 1000f:0} ms");
    }

    private static FaceGeo[] ReadGeometry(EditMesh em)
    {
        var faces = em.faces;
        var n = faces?.Count ?? 0;
        var geo = new FaceGeo[n];
        for (var i = 0; i < n; i++)
        {
            try
            {
                var f = faces[i];
                if (f == null) continue;
                var vc = f.vertexCount;
                if (vc < 3 || vc > 1024) continue;
                var g = new FaceGeo { O = new Vector3[vc], I = new Vector3[vc] };
                var l = f.firstLoop;
                var ok = true;
                for (var k = 0; k < vc; k++)
                {
                    if (l == null) { ok = false; break; }
                    var v = l.vertex;
                    g.O[k] = v != null ? v.position : Vector3.zero;
                    g.I[k] = l.thickenedPosition;
                    l = l.next;
                }
                if (!ok) continue;
                var nrm = f.normal;
                if (nrm.sqrMagnitude < 1e-12f) nrm = FaceTags.Newell(g.O);
                g.N = nrm.sqrMagnitude > 1e-12f ? nrm.normalized : Vector3.zero;
                var min = g.O[0];
                var max = g.O[0];
                for (var k = 0; k < vc; k++)
                {
                    if ((g.I[k] - g.O[k]).sqrMagnitude > 4f) g.I[k] = g.O[k]; 
                    min = Vector3.Min(min, Vector3.Min(g.O[k], g.I[k]));
                    max = Vector3.Max(max, Vector3.Max(g.O[k], g.I[k]));
                }
                var pad = new Vector3(LooseTol, LooseTol, LooseTol);
                g.Min = min - pad;
                g.Max = max + pad;
                geo[i] = g;
            }
            catch { }
        }
        return geo;
    }

    
    
    private static int Nearest(FaceGeo[] geo, Vector3 c, Vector3 normal, out float bestD)
    {
        var nt = normal.sqrMagnitude > 1e-14f ? normal.normalized : Vector3.zero;
        var best = -1;
        bestD = float.MaxValue;
        for (var i = 0; i < geo.Length; i++)
        {
            var g = geo[i];
            if (g == null || !Inside(g, c)) continue;
            var d = Score(g, c, nt, true);
            if (d < bestD) { bestD = d; best = i; }
        }
        if (bestD <= StrictTol2) return best;
        for (var i = 0; i < geo.Length; i++)
        {
            var g = geo[i];
            if (g == null || !Inside(g, c)) continue;
            var d = Score(g, c, nt, false);
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }

    private static bool Inside(FaceGeo g, Vector3 c) =>
        c.x >= g.Min.x && c.x <= g.Max.x && c.y >= g.Min.y && c.y <= g.Max.y && c.z >= g.Min.z && c.z <= g.Max.z;

    private static float Score(FaceGeo g, Vector3 c, Vector3 nt, bool strict)
    {
        var dn = Vector3.Dot(nt, g.N);
        var best = float.MaxValue;
        if (!strict || dn > 0.25f) best = Math.Min(best, PolyDist(c, g.O));
        if (!strict || dn < -0.25f) best = Math.Min(best, PolyDist(c, g.I));
        if (best <= 1e-10f) return best;
        var n = g.O.Length;
        for (var k = 0; k < n; k++)
        {
            var k2 = k + 1 == n ? 0 : k + 1;
            best = Math.Min(best, SqDistPointTri(c, g.O[k], g.O[k2], g.I[k2]));
            best = Math.Min(best, SqDistPointTri(c, g.O[k], g.I[k2], g.I[k]));
        }
        return best;
    }

    private static float PolyDist(Vector3 c, Vector3[] poly)
    {
        var best = float.MaxValue;
        for (var k = 1; k + 1 < poly.Length; k++)
            best = Math.Min(best, SqDistPointTri(c, poly[0], poly[k], poly[k + 1]));
        return best;
    }

    
    private static float SqDistPointTri(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        var ab = b - a;
        var ac = c - a;
        var ap = p - a;
        var d1 = Vector3.Dot(ab, ap);
        var d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0f && d2 <= 0f) return ap.sqrMagnitude;
        var bp = p - b;
        var d3 = Vector3.Dot(ab, bp);
        var d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0f && d4 <= d3) return bp.sqrMagnitude;
        var vc = d1 * d4 - d3 * d2;
        if (vc <= 0f && d1 >= 0f && d3 <= 0f)
        {
            var v = d1 / (d1 - d3);
            return (p - (a + v * ab)).sqrMagnitude;
        }
        var cp = p - c;
        var d5 = Vector3.Dot(ab, cp);
        var d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0f && d5 <= d6) return cp.sqrMagnitude;
        var vb = d5 * d2 - d1 * d6;
        if (vb <= 0f && d2 >= 0f && d6 <= 0f)
        {
            var w = d2 / (d2 - d6);
            return (p - (a + w * ac)).sqrMagnitude;
        }
        var va = d3 * d6 - d5 * d4;
        if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f)
        {
            var w = (d4 - d3) / (d4 - d3 + (d5 - d6));
            return (p - (b + w * (c - b))).sqrMagnitude;
        }
        var sum = va + vb + vc;
        if (sum <= 1e-20f) return Math.Min(ap.sqrMagnitude, Math.Min(bp.sqrMagnitude, cp.sqrMagnitude));
        var denom = 1f / sum;
        var v2 = vb * denom;
        var w2 = vc * denom;
        return (p - (a + ab * v2 + ac * w2)).sqrMagnitude;
    }

    private static void EnsureObject(Proxy p)
    {
        if (p.Go != null && !p.Go.WasCollected && p.Mesh != null && !p.Mesh.WasCollected) return;
        p.Go = new GameObject("Vehicle material proxy");
        p.Go.transform.SetParent(p.Src.transform, false);
        p.Go.layer = p.Src.gameObject.layer;
        p.Mesh = new Mesh { name = "Vehicle material proxy" };
        p.Mesh.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        p.Mesh.MarkDynamic();
        p.F = p.Go.AddComponent<MeshFilter>();
        p.F.sharedMesh = p.Mesh;
        p.R = p.Go.AddComponent<MeshRenderer>();
    }

    
    private static void Sync(Proxy p)
    {
        if (p.Go == null || p.Go.WasCollected || p.Src == null || p.Src.WasCollected || p.OrigIndex == null) return;
        var overlay = false;
        foreach (var vr in p.Owners)
            try { if (vr != null && vr.appliedOverlay != null) { overlay = true; break; } } catch { }
        var visible = false;
        if (!overlay)
            foreach (var r in p.Hidden)
                if (r != null && !r.WasCollected && r.enabled && r.gameObject.activeInHierarchy) { visible = true; break; }

        var hide = !overlay;
        if (hide != p.HidingOriginals)
        {
            p.HidingOriginals = hide;
            foreach (var r in p.Hidden) if (r != null && !r.WasCollected) r.forceRenderingOff = hide;
        }
        else if (hide)
            foreach (var r in p.Hidden) if (r != null && !r.WasCollected && !r.forceRenderingOff) r.forceRenderingOff = true;

        if (p.R.enabled != visible) p.R.enabled = visible;
        if (!visible) return;
        if (p.Go.layer != p.Src.gameObject.layer) p.Go.layer = p.Src.gameObject.layer;
        p.R.shadowCastingMode = p.Src.shadowCastingMode;
        p.R.receiveShadows = p.Src.receiveShadows;
        p.R.renderingLayerMask = p.Src.renderingLayerMask;
        
        Mpb.Clear();
        p.Src.GetPropertyBlock(Mpb);
        for (var i = 0; i < p.OrigIndex.Length; i++)
            if (p.OrigIndex[i] >= 0) p.R.SetPropertyBlock(Mpb, i);
    }

    private static void Release(Proxy p)
    {
        foreach (var r in p.Hidden)
            try { if (r != null && !r.WasCollected) r.forceRenderingOff = false; } catch { }
        try { if (p.Go != null && !p.Go.WasCollected) Object.Destroy(p.Go); } catch { }
        try { if (p.Mesh != null && !p.Mesh.WasCollected) Object.Destroy(p.Mesh); } catch { }
    }

    

    [HarmonyPatch(typeof(RendererMergerAssemblyStage), "IsValidMergeCandidate")]
    private static class MergePatch
    {
        private static void Postfix(VehicleRenderer renderer, ref bool __result)
        {
            if (!__result) return;
            try
            {
                var r = renderer?.renderer;
                if (r == null) return;
                if (Proxies.ContainsKey(r.Pointer)) { __result = false; return; }
                var vo = r.GetComponentInParent<VehicleObject>();
                var m = OwnMaterials(vo);
                if (m != null && !m.IsDefault) __result = false;
            }
            catch { }
        }
    }

    
    [HarmonyPatch(typeof(VehicleObjectModel), nameof(VehicleObjectModel.Build))]
    private static class ModelBuildPatch
    {
        private static void Postfix(VehicleObjectModel __instance)
        {
            try
            {
                var vo = __instance.VehicleObject;
                var m = OwnMaterials(vo);
                if (m != null && !m.IsDefault) __instance.MarkDynamic();
            }
            catch { }
        }
    }
}
