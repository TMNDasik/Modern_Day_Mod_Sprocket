using System;
using System.Collections.Generic;
using HarmonyLib;
using Sprocket.Vehicles;
using Sprocket.Vehicles.Cannons;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Munitions;




internal static class PortVisuals
{
    public const float LidSize = 0.22f, LidThickness = 0.015f, LidOffset = 0.008f;

    private sealed class PortState
    {
        public VehicleObject Vo;
        public Transform T;
        public GameObject Lid;
        public readonly Dictionary<IntPtr, (Transform T, Vector3 Scale)> Shrunk = new();
        public float NextCheck;
        public Vector3 Hinge = new(0f, LidSize * 0.5f, LidOffset);   
        public Vector3 LastScale = Vector3.zero, LastLossy = Vector3.zero;
        
        public List<Renderer> Models;
        public bool ModelsBattle;
        public float ModelsAt = -100f;
        
        public Sprocket.Vehicles.VehicleRenderer Base;
        public float BaseAt = -100f;
        public IntPtr Overlay;
        public Material NormalMat;
    }

    private static readonly Dictionary<IntPtr, PortState> Ports = new();
    private static readonly HashSet<int> BattleRoots = new();   
    private static Material _ownMat;
    private static Mesh _box;

    
    public static Transform Lid(Transform port)
    {
        foreach (var s in Ports.Values)
            if (s.T == port && s.Lid != null && !s.Lid.WasCollected) return s.Lid.transform;
        return null;
    }

    
    public static Vector3 HingeLocal(Transform port)
    {
        foreach (var s in Ports.Values) if (s.T == port) return s.Hinge;
        return new Vector3(0f, LidSize * 0.5f, LidOffset);
    }

    [HarmonyPatch(typeof(VehicleObject), nameof(VehicleObject.Initiate))]
    private static class InitiatePatch
    {
        private static void Postfix(VehicleObject __instance)
        {
            try { if (EjectorPart.IsEjector(__instance)) Register(__instance); }
            catch { }
        }
    }

    
    
    
    [HarmonyPatch(typeof(VehicleObjectModel), nameof(VehicleObjectModel.Initiate))]
    private static class ModelInitiatePatch
    {
        private static void Postfix(VehicleObjectModel __instance) => HideModel(__instance);
    }

    [HarmonyPatch(typeof(VehicleObjectModel), nameof(VehicleObjectModel.Build))]
    private static class ModelBuildPatch
    {
        private static void Postfix(VehicleObjectModel __instance) => HideModel(__instance);
    }

    private static void HideModel(VehicleObjectModel model)
    {
        try
        {
            var vo = model.GetComponent<VehicleObject>() ?? model.GetComponentInParent<VehicleObject>();
            if (!EjectorPart.IsEjector(vo)) return;
            model.MarkDynamic();
            model.SetVisible(false);
            Register(vo);
        }
        catch { }
    }

    public static void Register(VehicleObject vo)
    {
        if (vo == null || Ports.ContainsKey(vo.Pointer)) return;
        Ports[vo.Pointer] = new PortState { Vo = vo, T = vo.transform };
    }

    [HarmonyPatch(typeof(Cannon), nameof(Cannon.EnableBehaviour))]
    private static class BattlePatch
    {
        private static void Postfix(Cannon __instance)
        {
            try { BattleRoots.Add(Casings.VehicleRoot(__instance).gameObject.GetInstanceID()); ForceCheck(); }
            catch { }
        }
    }

    [HarmonyPatch(typeof(Cannon), nameof(Cannon.DisableBehaviour))]
    private static class DesignerPatch
    {
        private static void Postfix(Cannon __instance)
        {
            try { BattleRoots.Remove(Casings.VehicleRoot(__instance).gameObject.GetInstanceID()); ForceCheck(); }
            catch { }
        }
    }

    private static void ForceCheck()
    {
        foreach (var s in Ports.Values) s.NextCheck = 0f;
    }

    
    
    public static void Tick()
    {
        if (Ports.Count == 0) return;
        var now = Time.unscaledTime;
        List<IntPtr> dead = null;
        foreach (var kv in Ports)
        {
            var s = kv.Value;
            if (s.Vo == null || s.Vo.WasCollected || s.T == null || s.T.WasCollected)
            {
                (dead ??= new List<IntPtr>()).Add(kv.Key);
                continue;
            }
            if (s.Lid != null && !s.Lid.WasCollected)
                try
                {
                    FitLid(s);
                    SyncOverlay(s);
                }
                catch { }
            if (now < s.NextCheck) continue;
            s.NextCheck = now + 0.5f;
            try { Maintain(s); }
            catch (Exception e) { if (Plugin.DebugLogging.Value) Plugin.Log.LogWarning("Ejection port visuals failed: " + e.Message); }
        }
        if (dead != null)
            foreach (var k in dead)
            {
                var s = Ports[k];
                try { if (s.Lid != null && !s.Lid.WasCollected) Object.Destroy(s.Lid); } catch { }
                Ports.Remove(k);
            }
    }

    private static void Maintain(PortState s)
    {
        try
        {
            var model = s.Vo.GetComponent<VehicleObjectModel>() ?? s.Vo.GetComponentInChildren<VehicleObjectModel>(true);
            if (model != null)
            {
                model.MarkDynamic();
                model.SetVisible(false);
            }
        }
        catch { }
        var models = ModelRenderers(s);
        if (s.Lid == null || s.Lid.WasCollected) s.Lid = MakeLid(s, models);
        if (s.Lid != null) FitLid(s);

        var root = Casings.VehicleRoot(s.T);
        var battle = BattleRoots.Contains(root.gameObject.GetInstanceID());
        
        if (s.Lid != null && !s.Lid.WasCollected)
        {
            var show = !ModernDayTags.HiddenInBattle(s.Vo.Pointer);
            if (s.Lid.activeSelf != show) s.Lid.SetActive(show);
        }
        foreach (var r in models)
        {
            if (!r.forceRenderingOff) r.forceRenderingOff = true;
            var t = r.transform;
            if (battle)
            {
                
                if (!s.Shrunk.ContainsKey(t.Pointer)) s.Shrunk[t.Pointer] = (t, t.localScale);
                if (t.localScale != Vector3.zero) t.localScale = Vector3.zero;
            }
        }
        if (!battle && s.Shrunk.Count > 0)
        {
            foreach (var (t, scale) in s.Shrunk.Values)
                try { if (t != null && !t.WasCollected) t.localScale = scale; } catch { }
            s.Shrunk.Clear();
        }
    }

    
    
    private static void SyncOverlay(PortState s)
    {
        var now = Time.unscaledTime;
        if (s.Base == null || s.Base.WasCollected || now - s.BaseAt > 1f || now < s.BaseAt)
        {
            s.BaseAt = now;
            s.Base = null;
            try
            {
                var model = s.Vo.GetComponent<VehicleObjectModel>() ?? s.Vo.GetComponentInChildren<VehicleObjectModel>(true);
                var lods = model?.model?.TryCast<Sprocket.Vehicles.VehicleRendererGroup>()?.lods;
                if (lods != null && lods.Length > 0) s.Base = lods[0];
            }
            catch { }
        }
        
        
        
        var vr = s.Base;
        var mr = s.Lid.GetComponent<MeshRenderer>();
        if (mr == null) return;
        if (!IsOverlayMaterial(mr.sharedMaterial)) return;
        Material paint = null;
        try { paint = vr?.material?.Material; } catch { }
        if (paint == null || IsOverlayMaterial(paint)) paint = s.NormalMat != null && !s.NormalMat.WasCollected && !IsOverlayMaterial(s.NormalMat) ? s.NormalMat : OwnMaterial();
        if (paint == null) return;
        mr.sharedMaterial = paint;
        mr.forceRenderingOff = false;
        var mpb = new MaterialPropertyBlock();
        try { if (vr?.overlayMaterial == null) vr?.renderer?.GetPropertyBlock(mpb); } catch { }
        mr.SetPropertyBlock(mpb);
    }

    
    private static bool IsOverlayMaterial(Material m)
    {
        if (m == null || m.WasCollected) return false;
        try
        {
            var name = m.name ?? "";
            return name.IndexOf("xray", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("x-ray", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("uninstalled", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("overlay", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        catch { return false; }
    }

    
    
    private static List<Renderer> ModelRenderers(PortState s)
    {
        var battle = false;
        try { battle = BattleRoots.Contains(Casings.VehicleRoot(s.T).gameObject.GetInstanceID()); } catch { }
        var now = Time.unscaledTime;
        if (s.Models != null && s.ModelsBattle == battle && now - s.ModelsAt < 5f && now >= s.ModelsAt)
        {
            var ok = true;
            foreach (var r in s.Models)
                if (r == null || r.WasCollected || !r) { ok = false; break; }
            if (ok) return s.Models;
        }
        s.Models = CollectRenderers(s);
        s.ModelsBattle = battle;
        s.ModelsAt = now + UnityEngine.Random.Range(0f, 1f);
        return s.Models;
    }

    private static List<Renderer> CollectRenderers(PortState s)
    {
        var list = new List<Renderer>();
        var lid = s.Lid != null && !s.Lid.WasCollected ? s.Lid.transform : null;
        foreach (var r in s.T.GetComponentsInChildren<Renderer>(true))
            if (r != null && (lid == null || !r.transform.IsChildOf(lid))) list.Add(r);
        try
        {
            var vehicle = s.T.GetComponentInParent<Vehicle>();
            var history = vehicle != null ? vehicle.transformHeirarchyFlattenHistory?.history : null;
            if (history != null)
                foreach (var rep in history)
                {
                    if (rep.Target == null || rep.Old == null) continue;
                    if (rep.Old != s.T && !rep.Old.IsChildOf(s.T)) continue;
                    foreach (var r in rep.Target.GetComponentsInChildren<Renderer>(true)) if (r != null) list.Add(r);
                }
        }
        catch { }
        return list;
    }

    
    private static void FitLid(PortState s)
    {
        var scale = Vector3.one;
        try
        {
            var vt = s.Vo.VehicleTransform;
            if (vt != null && vt.scale.x > 0f) scale = vt.scale;
        }
        catch { }
        var lossy = s.T.lossyScale;
        if (scale == s.LastScale && lossy == s.LastLossy) return;
        s.LastScale = scale;
        s.LastLossy = lossy;
        float Div(float a, float b) => Mathf.Abs(b) > 1e-5f ? a / b : a;
        var w = LidSize * scale.x;
        var h = LidSize * scale.y;
        var t = LidThickness * Mathf.Sqrt(scale.x * scale.y);
        var lid = s.Lid.transform;
        lid.localPosition = new Vector3(0f, 0f, Div(LidOffset, lossy.z));
        lid.localRotation = Quaternion.identity;
        lid.localScale = new Vector3(Div(w, lossy.x), Div(h, lossy.y), Div(t, lossy.z));
        s.Hinge = new Vector3(0f, Div(h * 0.5f, lossy.y), Div(LidOffset, lossy.z));
    }

    
    private static GameObject MakeLid(PortState s, List<Renderer> models)
    {
        var go = new GameObject("Munitions ejection port lid");
        go.transform.SetParent(s.T, false);
        go.transform.localPosition = new Vector3(0f, 0f, LidOffset);
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = new Vector3(LidSize, LidSize, LidThickness);
        go.layer = s.T.gameObject.layer;
        go.AddComponent<MeshFilter>().sharedMesh = Box();
        var mr = go.AddComponent<MeshRenderer>();
        mr.shadowCastingMode = ShadowCastingMode.On;

        
        Renderer source = null;
        foreach (var r in models)
            if (r != null && r.sharedMaterial != null && !IsOverlayMaterial(r.sharedMaterial)) { source = r; break; }
        if (source != null)
        {
            mr.sharedMaterial = source.sharedMaterial;
            s.NormalMat = source.sharedMaterial;
            try
            {
                var mpb = new MaterialPropertyBlock();
                source.GetPropertyBlock(mpb);
                mr.SetPropertyBlock(mpb);
                mr.renderingLayerMask = source.renderingLayerMask;
            }
            catch { }
        }
        else
        {
            var mat = OwnMaterial();
            if (mat == null) { Object.Destroy(go); return null; }
            mr.sharedMaterial = mat;
        }
        return go;
    }

    private static Material OwnMaterial()
    {
        if (_ownMat != null && !_ownMat.WasCollected) return _ownMat;
        var baseMat = Fx.CasingMat;
        if (baseMat == null) return null;
        _ownMat = new Material(baseMat) { name = "Munitions port lid" };
        _ownMat.SetColor("_BaseColor", new Color(0.33f, 0.35f, 0.28f, 1f));
        _ownMat.SetFloat("_Metallic", 0.25f);
        _ownMat.SetFloat("_Smoothness", 0.3f);
        _ownMat.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        return _ownMat;
    }

    
    private static Mesh Box()
    {
        if (_box != null && !_box.WasCollected) return _box;
        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();
        Vector3[] dirs = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
        foreach (var n in dirs)
        {
            var u = Mathf.Abs(n.y) > 0.5f ? Vector3.right : Vector3.up;
            var v = Vector3.Cross(n, u);
            var s = verts.Count;
            verts.Add((n - u - v) * 0.5f); verts.Add((n - u + v) * 0.5f); verts.Add((n + u + v) * 0.5f); verts.Add((n + u - v) * 0.5f);
            uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(0f, 1f)); uvs.Add(new Vector2(1f, 1f)); uvs.Add(new Vector2(1f, 0f));
            for (var i = 0; i < 4; i++) normals.Add(n);
            
            var face = Vector3.Cross(verts[s + 1] - verts[s], verts[s + 2] - verts[s]);
            if (Vector3.Dot(face, n) > 0f) tris.AddRange(new[] { s, s + 1, s + 2, s, s + 2, s + 3 });
            else tris.AddRange(new[] { s, s + 2, s + 1, s, s + 3, s + 2 });
        }
        _box = new Mesh { name = "Munitions port lid" };
        _box.vertices = verts.ToArray();
        _box.normals = normals.ToArray();
        _box.uv = uvs.ToArray();
        _box.triangles = tris.ToArray();
        _box.RecalculateBounds();
        try { _box.RecalculateTangents(); } catch { }
        _box.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        return _box;
    }
}
