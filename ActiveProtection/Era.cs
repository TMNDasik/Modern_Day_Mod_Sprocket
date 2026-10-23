using System;
using System.Collections.Generic;
using Sprocket.Vehicles;
using UnityEngine;

namespace ActiveProtection;







internal static unsafe class Era
{
    private const int EId = 0x18, EVel = 0x34;

    
    private static readonly Dictionary<(int Id, IntPtr Block), float> Passed = new();
    private static float _nextCleanup;

    public static void Check(Interceptor.Group g, IntPtr e, Vector3 pos, Vector3 vel, float dt, DefInfo info)
    {
        if (info == null || info.Type == ThreatTypes.None || g.Eras.Count == 0) return;
        var step = vel * dt;
        if (step.sqrMagnitude < 1e-8f) return;
        var id = *(int*)(e + EId);
        var now = Time.time;
        Cleanup(now);

        foreach (var m in g.Eras)
        {
            var s = m.SyncScale();
            if (info.Caliber + 0.01f < s.TriggerCaliber) continue;
            var half = s.EraSize * 0.5f;
            var rot = m.Rotation;
            var inv = Quaternion.Inverse(rot);
            var center = m.Center;
            var a = inv * (pos - center);
            var d = inv * step;
            if (!Enter(a, d, half, out var t)) continue;
            if (Passed.ContainsKey((id, m.Ptr))) continue;
            Passed[(id, m.Ptr)] = now;

            var local = a + d * t;
            var tw = s.TileWidth / 1000f;
            var th = s.TileHeight / 1000f;
            var nx = s.TilesX;
            var ix = Mathf.Clamp(Mathf.FloorToInt((local.x + half.x) / tw), 0, nx - 1);
            var iy = Mathf.Clamp(Mathf.FloorToInt((local.y + half.y) / th), 0, s.TilesY - 1);
            if (!m.SpentTiles.Add(iy * nx + ix)) continue; 

            var dir = vel.normalized;
            var normal = rot * Vector3.forward;
            var angle = Mathf.Acos(Mathf.Clamp01(Mathf.Abs(Vector3.Dot(dir, normal)))) * Mathf.Rad2Deg;
            var effect = Mathf.Lerp(s.HeadOn, 1f, Mathf.Clamp01(angle / ApsSettings.FullEffectAngle));
            var cut = CutFor(s, info);
            var left = Mathf.Clamp(1f - cut * effect, 0.02f, 1f);
            if (left < 0.999f)
            {
                var factor = Mathf.Pow(left, 1f / 1.43f);
                if (!MunitionsBridge.ScaleImpact(id, factor))
                {
                    var v = *(Vector3*)(e + EVel);
                    *(Vector3*)(e + EVel) = v * factor;
                }
            }

            
            var tileCenter = new Vector3(-half.x + (ix + 0.5f) * tw, -half.y + (iy + 0.5f) * th, half.z);
            tileCenter.x = Mathf.Min(tileCenter.x, half.x);
            tileCenter.y = Mathf.Min(tileCenter.y, half.y);
            Fx.Era(center + rot * tileCenter, normal, Mathf.Max(tw, th));
            Plugin.Log.LogInfo($"ERA: {Name(info)} ({info.Caliber:0} mm) set off tile {ix + 1},{iy + 1} of a block at {angle:0}° — " +
                               $"{(cut > 0f ? $"penetration ×{left:0.00}" : "no penetration loss for this round")}, " +
                               $"{nx * s.TilesY - m.SpentTiles.Count} tile(s) left");
        }
    }

    
    
    

    public static void CheckHosts(Interceptor.Group g, IntPtr e, Vector3 pos, Vector3 vel, float dt, DefInfo info)
    {
        if (info == null || info.Type == ThreatTypes.None || g.Hosts.Count == 0) return;
        var step = vel * dt;
        if (step.sqrMagnitude < 1e-8f) return;
        var id = *(int*)(e + EId);
        var now = Time.time;
        Cleanup(now);

        foreach (var h in g.Hosts)
        {
            var c = h.C;
            if (h.BlownOff || c == null || !c.On || !c.HasGeometry || info.Caliber + 0.01f < c.Trigger) continue;
            Transform t;
            try { t = h.T; }
            catch { continue; }
            var a = t.InverseTransformPoint(pos);
            var d = t.InverseTransformVector(step);
            var bounds = c.LocalBounds;
            if (!EraConfig.Segment(a - bounds.center, d, bounds.extents + new Vector3(0.01f, 0.01f, 0.01f), out var tt)) continue;
            if (Passed.ContainsKey((id, h.Ptr))) continue;
            Passed[(id, h.Ptr)] = now;

            if (!c.Evaluate(a + d * tt, d.normalized, out var hit, h.BlownMain, h.BlownMirror)) continue;
            var panel = c.Panels[hit.Panel];
            var normal = t.TransformDirection(panel.Out).normalized;
            var angle = Mathf.Acos(Mathf.Clamp01(Mathf.Abs(Vector3.Dot(vel.normalized, normal)))) * Mathf.Rad2Deg;
            var cut = info.Type switch
            {
                ThreatTypes.Atgm => hit.Tandem,
                ThreatTypes.Ap or ThreatTypes.Apfsds => hit.Kinetic,
                ThreatTypes.He => info.Heat ? hit.Heat : 0f,
                _ => 0f
            };
            var left = Mathf.Clamp(1f - cut * EraConfig.AngleEffect(angle), 0.02f, 1f);
            if (left < 0.999f)
            {
                var factor = Mathf.Pow(left, 1f / 1.43f);
                if (!MunitionsBridge.ScaleImpact(id, factor))
                {
                    var v = *(Vector3*)(e + EVel);
                    *(Vector3*)(e + EVel) = v * factor;
                }
            }

            
            var el = c.Elements[hit.Element];
            EraConfig.Box(panel, el, out var ec, out var eh);
            var flash = ec + panel.Out * ((el.Depth + el.T * 0.5f) / 1000f);
            Fx.Era(t.TransformPoint(flash), normal, Mathf.Clamp(Mathf.Max(eh.x, eh.y) * 2f, 0.1f, 1.5f));
            var half = c.HasMirrorBox ? (panel.Mirror ? " (mirrored half)" : " (original half)") : "";
            Plugin.Log.LogInfo($"ERA: {Name(info)} ({info.Caliber:0} mm) set off {hit.Fired} explosive(s) at {angle:0}° — " +
                               $"{(cut > 0f ? $"penetration ×{left:0.00}" : "no penetration loss for this round")}; the part is blown off{half}");
            BlowOff(h, g, panel.Mirror);
        }
    }

    
    
    

    private const int CsObjects = 0x48, CsMeshes = 0x50;                  
    private const int MsSize = 0x58, MsTransform = 0x0, MsVertices = 0x8, MsTriangles = 0x18, MsMaterialMap = 0x30;
    private const int ArrLength = 0x18, ArrData = 0x20;

    
    private static void BlowOff(EraHost h, Interceptor.Group g, bool mirrorHalf)
    {
        var halves = h.C.HasMirrorBox;
        if (!halves) mirrorHalf = false;
        if (mirrorHalf ? h.BlownMirror : h.BlownMain) return;
        if (mirrorHalf) h.BlownMirror = true;
        else h.BlownMain = true;
        var cfg = h.C;
        Func<Vector3, bool> inHalf = halves && !h.BlownOff ? p => cfg.InHalf(p, mirrorHalf, 0.01f) : null;
        try { HideRenderers(h, inHalf); }
        catch (Exception e) { if (Plugin.DebugLogging.Value) Plugin.Log.LogWarning("ERA part hide failed: " + e.Message); }
        try { RemoveArmour(h, g, halves ? p => cfg.InHalf(p, mirrorHalf, 0.01f) : null); }
        catch (Exception e) { Plugin.Log.LogWarning("ERA part armour removal failed: " + e.Message); }
    }

    
    private static void HideRenderers(EraHost h, Func<Vector3, bool> inHalf)
    {
        var vo = h.Vo;
        void Hide(Renderer r)
        {
            if (r == null || r.forceRenderingOff) return;
            r.forceRenderingOff = true;
            h.Hidden.Add(r);
        }
        var plate = EraHosts.PlateOf(vo);
        if (inHalf != null && plate != null)
        {
            var cut = 0;
            cut += CutMesh(h, plate.highDetailRenderer?.Filter, inHalf);
            cut += CutMesh(h, plate.lowDetailRenderer?.Filter, inHalf);
            cut += CutMesh(h, plate.shadowProxyRenderer?.Filter, inHalf);
            if (cut > 0) return;
            
            Plugin.Log.LogWarning("ERA: could not cut the blown half out of the part's mesh — it stays visible until the other half goes too");
            return;
        }
        if (plate != null)
        {
            Hide(plate.highDetailRenderer?.MeshRenderer);
            Hide(plate.lowDetailRenderer?.MeshRenderer);
            Hide(plate.shadowProxyRenderer?.MeshRenderer);
        }
        foreach (var r in vo.GetComponentsInChildren<Renderer>(true))
        {
            var owner = r != null ? r.GetComponentInParent<VehicleObject>() : null;
            if (owner != null && owner.Pointer == vo.Pointer) Hide(r);
        }
    }

    
    private static int CutMesh(EraHost h, MeshFilter f, Func<Vector3, bool> inHalf)
    {
        if (f == null) return 0;
        var mesh = f.sharedMesh;
        if (mesh == null || !mesh.isReadable) return 0;
        Mesh copy = null;
        foreach (var x in h.CutMeshes)
            if (x.Filter.Pointer == f.Pointer) { copy = x.Cut; break; }
        if (copy == null)
        {
            copy = UnityEngine.Object.Instantiate(mesh);
            copy.name = mesh.name + " (ERA blown)";
            h.CutMeshes.Add((f, mesh, copy));
        }
        var vo = h.T;
        var ft = f.transform;
        var verts = copy.vertices;
        var removed = 0;
        for (var sub = 0; sub < copy.subMeshCount; sub++)
        {
            var tris = copy.GetTriangles(sub);
            var keep = new List<int>(tris.Length);
            for (var k = 0; k + 2 < tris.Length; k += 3)
            {
                var c = (verts[tris[k]] + verts[tris[k + 1]] + verts[tris[k + 2]]) / 3f;
                if (inHalf(vo.InverseTransformPoint(ft.TransformPoint(c))))
                {
                    removed++;
                    continue;
                }
                keep.Add(tris[k]);
                keep.Add(tris[k + 1]);
                keep.Add(tris[k + 2]);
            }
            copy.SetTriangles(keep.ToArray(), sub);
        }
        f.sharedMesh = copy;
        return removed;
    }

    
    
    
    private static void RemoveArmour(EraHost h, Interceptor.Group g, Func<Vector3, bool> inHalf)
    {
        var vuid = 0;
        try { vuid = h.Vo.VUID.Value; } catch { }
        var t = h.T;
        var cfg = h.C;
        var removed = 0;
        var how = "";
        foreach (var cs in g.Structures)
        {
            if (cs == null || cs.WasCollected) continue;
            var csPtr = cs.Pointer;
            var objects = *(IntPtr*)(csPtr + CsObjects);
            var meshes = *(IntPtr*)(csPtr + CsMeshes);
            if (objects == IntPtr.Zero || meshes == IntPtr.Zero) continue;
            var objCount = *(int*)(objects + ArrLength);
            var meshCount = *(int*)(meshes + ArrLength);
            if (objCount <= 0 || objCount > 100000 || meshCount <= 0 || meshCount > 4096) continue;

            
            
            var csT = cs.transform;
            var inside = new Dictionary<int, (int In, int All)>[2];
            for (var space = 0; space < 2; space++)
            {
                inside[space] = new Dictionary<int, (int In, int All)>();
                for (var m = 0; m < meshCount; m++)
                {
                    if (!DamageMesh(meshes, m, cs, out var tr, out var verts, out var vertCount, out var tris, out var map, out var triCount)) continue;
                    var frame = space == 0 ? tr : csT;
                    for (var k = 0; k < triCount; k++)
                    {
                        var obj = *(short*)(map + ArrData + k * 4 + 2);
                        var a = *(int*)(tris + ArrData + k * 12);
                        var b = *(int*)(tris + ArrData + k * 12 + 4);
                        var c = *(int*)(tris + ArrData + k * 12 + 8);
                        if ((uint)a >= (uint)vertCount || (uint)b >= (uint)vertCount || (uint)c >= (uint)vertCount) continue;
                        var p = (*(Vector3*)(verts + ArrData + a * 12) + *(Vector3*)(verts + ArrData + b * 12) + *(Vector3*)(verts + ArrData + c * 12)) / 3f;
                        var local = t.InverseTransformPoint(frame.TransformPoint(p));
                        var isIn = cfg.HasPartBox ? cfg.InsidePart(local, 0.01f) : cfg.InsideAnyPanel(local, 0.015f);
                        inside[space].TryGetValue(obj, out var s);
                        inside[space][obj] = (s.In + (isIn ? 1 : 0), s.All + 1);
                    }
                }
            }
            float Share(int obj)
            {
                var best = 0f;
                foreach (var d in inside)
                    if (d.TryGetValue(obj, out var s) && s.All >= 2) best = Mathf.Max(best, s.In / (float)s.All);
                return best;
            }

            float ShareIn(int space, int obj) =>
                inside[space].TryGetValue(obj, out var s) && s.All >= 2 ? s.In / (float)s.All : 0f;

            
            var target = -1;
            var bestSpace = 0;
            for (var i = 0; i < objCount && vuid != 0; i++)
                if (*(int*)(objects + ArrData + i * 8) == vuid && Share(i) >= 0.5f)
                {
                    target = i;
                    bestSpace = ShareIn(1, i) > ShareIn(0, i) ? 1 : 0;
                    how = "by part ID";
                    break;
                }
            if (target < 0)
            {
                var best = 0f;
                for (var space = 0; space < 2; space++)
                    foreach (var kv in inside[space])
                    {
                        if (kv.Value.All < 2) continue;
                        var share = kv.Value.In / (float)kv.Value.All;
                        if (share >= 0.8f && share > best) { best = share; target = kv.Key; bestSpace = space; }
                    }
                if (target >= 0) how = $"by position ({best * 100f:0}% inside)";
            }
            if (target < 0) continue;

            for (var m = 0; m < meshCount; m++)
            {
                if (!DamageMesh(meshes, m, cs, out var tr, out var verts, out var vertCount, out var tris, out var map, out var triCount)) continue;
                var frame = bestSpace == 0 ? tr : csT;
                Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> arr = null;
                for (var k = 0; k < triCount; k++)
                {
                    if (*(short*)(map + ArrData + k * 4 + 2) != target) continue;
                    var a = *(int*)(tris + ArrData + k * 12);
                    var b = *(int*)(tris + ArrData + k * 12 + 4);
                    var c = *(int*)(tris + ArrData + k * 12 + 8);
                    if (b == a && c == a) continue;   
                    if (inHalf != null)
                    {
                        if ((uint)a >= (uint)vertCount || (uint)b >= (uint)vertCount || (uint)c >= (uint)vertCount) continue;
                        var p = (*(Vector3*)(verts + ArrData + a * 12) + *(Vector3*)(verts + ArrData + b * 12) + *(Vector3*)(verts + ArrData + c * 12)) / 3f;
                        if (!inHalf(t.InverseTransformPoint(frame.TransformPoint(p)))) continue;
                    }
                    arr ??= new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int>(tris);   
                    h.Removed.Add((arr, k, a, b, c));
                    arr[k * 3 + 1] = a;
                    arr[k * 3 + 2] = a;
                    removed++;
                }
            }
        }
        if (removed > 0) Plugin.Log.LogInfo($"ERA part blown off: {removed} armour triangle(s) removed ({how})");
        else Plugin.Log.LogWarning("ERA part blown off, but its armour was not found in the damage model — it stays as invisible armour");
    }

    
    private static bool DamageMesh(IntPtr meshes, int m, Component cs, out Transform tr, out IntPtr verts, out int vertCount,
        out IntPtr tris, out IntPtr map, out int triCount)
    {
        var elem = meshes + ArrData + m * MsSize;
        var trPtr = *(IntPtr*)(elem + MsTransform);
        verts = *(IntPtr*)(elem + MsVertices);
        tris = *(IntPtr*)(elem + MsTriangles);
        map = *(IntPtr*)(elem + MsMaterialMap);
        tr = null;
        triCount = 0;
        vertCount = 0;
        if (verts == IntPtr.Zero || tris == IntPtr.Zero || map == IntPtr.Zero) return false;
        vertCount = *(int*)(verts + ArrLength);
        var idx = *(int*)(tris + ArrLength);
        triCount = idx / 3;
        if (triCount <= 0 || *(int*)(map + ArrLength) != triCount || vertCount <= 0) return false;
        tr = trPtr != IntPtr.Zero ? new Transform(trPtr) : cs.transform;
        return tr != null;
    }

    
    public static void Reset(EraHost h)
    {
        h.Spent.Clear();
        h.BlownMain = h.BlownMirror = false;
        foreach (var r in h.Hidden)
            try { if (r != null && !r.WasCollected) r.forceRenderingOff = false; }
            catch { }
        h.Hidden.Clear();
        foreach (var (filter, original, cut) in h.CutMeshes)
            try
            {
                if (filter != null && !filter.WasCollected && original != null && !original.WasCollected) filter.sharedMesh = original;
                if (cut != null && !cut.WasCollected) UnityEngine.Object.Destroy(cut);
            }
            catch { }
        h.CutMeshes.Clear();
        foreach (var (tris, k, a, b, c) in h.Removed)
            try
            {
                tris[k * 3] = a;
                tris[k * 3 + 1] = b;
                tris[k * 3 + 2] = c;
            }
            catch { }
        h.Removed.Clear();
    }

    private static float CutFor(ApsSettings s, DefInfo info) => info.Type switch
    {
        ThreatTypes.Atgm => s.CutTandem,
        ThreatTypes.Ap or ThreatTypes.Apfsds => s.CutKinetic,
        ThreatTypes.He => info.Heat ? s.CutHeat : 0f,
        _ => 0f
    };

    private static string Name(DefInfo info) => info.Type switch
    {
        ThreatTypes.Atgm => "tandem ATGM",
        ThreatTypes.Apfsds => "APFSDS",
        ThreatTypes.Ap => "AP shell",
        ThreatTypes.He => info.Heat ? "HEAT shell" : "HE shell",
        _ => "round"
    };

    
    private static bool Enter(Vector3 a, Vector3 d, Vector3 half, out float tEnter)
    {
        float tMin = 0f, tMax = 1f;
        tEnter = 0f;
        for (var axis = 0; axis < 3; axis++)
        {
            var o = a[axis];
            var v = d[axis];
            var h = half[axis];
            if (Mathf.Abs(v) < 1e-9f)
            {
                if (o < -h || o > h) return false;
                continue;
            }
            var t1 = (-h - o) / v;
            var t2 = (h - o) / v;
            if (t1 > t2) (t1, t2) = (t2, t1);
            tMin = Mathf.Max(tMin, t1);
            tMax = Mathf.Min(tMax, t2);
            if (tMin > tMax) return false;
        }
        tEnter = tMin;
        return true;
    }

    private static void Cleanup(float now)
    {
        if (now < _nextCleanup || Passed.Count == 0) return;
        _nextCleanup = now + 1f;
        List<(int, IntPtr)> old = null;
        foreach (var kv in Passed)
            if (now - kv.Value > 3f || now < kv.Value) (old ??= new List<(int, IntPtr)>()).Add(kv.Key);
        if (old != null) foreach (var k in old) Passed.Remove(k);
    }

    
    public static void Reset(Module m) => m.SpentTiles.Clear();

    

    
    public static void ApplyMass(Module m, VehicleObjectModel model)
    {
        try
        {
            if (m?.Vo == null || m.Vo.WasCollected) return;
            model ??= m.Vo.GetComponent<VehicleObjectModel>() ?? m.Vo.GetComponentInChildren<VehicleObjectModel>(true);
            if (model == null) return;
            model.SetMass(m.SyncScale().PartMass, MassType.Armour);
        }
        catch (Exception ex)
        {
            if (Plugin.DebugLogging.Value) Plugin.Log.LogWarning("ERA mass failed: " + ex.Message);
        }
    }

    

    private static Texture2D _icon;

    public static Texture2D Icon()
    {
        if (_icon != null && !_icon.WasCollected) return _icon;
        const int n = 128;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "ERA icon", wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[n * n];
        for (var y = 0; y < n; y++)
        for (var x = 0; x < n; x++)
        {
            var p = new Vector2((x + 0.5f) / n, (y + 0.5f) / n);
            var a = 0f;
            
            for (var row = 0; row < 3; row++)
            {
                var y0 = 0.16f + row * 0.24f;
                var shift = row == 1 ? 0.13f : 0f;
                for (var col = -1; col < 3; col++)
                {
                    var x0 = 0.1f + shift + col * 0.27f;
                    var bx0 = Mathf.Max(0.1f, x0);
                    var bx1 = Mathf.Min(0.9f, x0 + 0.24f);
                    if (bx1 - bx0 < 0.03f) continue;
                    var brick = Box(p, bx0, y0, bx1, y0 + 0.2f, 0.015f);
                    
                    if (bx1 - bx0 > 0.12f)
                    {
                        var bolt = Mathf.Clamp01(0.5f - ((p - new Vector2((bx0 + bx1) * 0.5f, y0 + 0.1f)).magnitude - 0.02f) * n);
                        brick *= 1f - 0.5f * bolt;
                    }
                    a = Mathf.Max(a, brick);
                }
            }
            var shade = (byte)(200 + 45 * Mathf.Clamp01(p.y));
            px[y * n + x] = new Color32(shade, shade, shade, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
        }
        tex.SetPixels32(px);
        tex.Apply(false, false);
        tex.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        _icon = tex;
        return tex;
    }

    
    private static float Box(Vector2 p, float x0, float y0, float x1, float y1, float r)
    {
        const int n = 128;
        var c = new Vector2((x0 + x1) * 0.5f, (y0 + y1) * 0.5f);
        var h = new Vector2((x1 - x0) * 0.5f - r, (y1 - y0) * 0.5f - r);
        var q = new Vector2(Mathf.Abs(p.x - c.x) - h.x, Mathf.Abs(p.y - c.y) - h.y);
        var d = new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - r;
        return Mathf.Clamp01(0.5f - d * n);
    }
}
