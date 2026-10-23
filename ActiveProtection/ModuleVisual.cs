using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Sprocket.Vehicles;
using Sprocket.Vehicles.Colliders;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace ActiveProtection;




internal sealed class ModuleVisual
{
    private readonly Module _m;
    private GameObject _root;
    private readonly List<GameObject> _parts = new();
    private readonly Dictionary<IntPtr, (Transform T, Vector3 Scale)> _shrunk = new();
    private Vector3 _lastSize, _lastLossy;
    private bool _lastHidden;
    private float _nextCheck;
    private bool _loggedBounds;

    private static Mesh _box, _tube;
    private static Material _fallbackMat;

    private ModuleVisual(Module m) { _m = m; }

    
    
    public static void Tick()
    {
        var now = Time.unscaledTime;
        foreach (var m in Modules.Snapshot())
        {
            if (m.Visual == null)
            {
                m.Visual = new ModuleVisual(m);
                m.Visual._nextCheck = now + UnityEngine.Random.Range(0f, 0.25f);
            }
            if (now < m.Visual._nextCheck) continue;
            m.Visual._nextCheck = now + (m.InBattle ? 1f : 0.25f) + UnityEngine.Random.Range(0f, 0.05f);
            try { m.Visual.Maintain(); }
            catch (Exception e) { if (Plugin.DebugLogging.Value) Plugin.Log.LogWarning("APS module visual failed: " + e.Message); }
        }
    }

    
    
    private float _colDue;

    public static void FollowScale()
    {
        var designer = DesignerState.Active;
        var now = Time.unscaledTime;
        foreach (var m in Modules.Snapshot())
        {
            var v = m.Visual;
            if (v == null || v._root == null || v._root.WasCollected || v._parts.Count == 0) continue;
            if (m.InBattle || !designer)
            {
                
                try { v.SyncOverlay(false); }
                catch { }
                continue;
            }
            try
            {
                v.ApplyHidden();
                v.SyncOverlay(false);
                if (v.Fit()) v._colDue = now + 0.15f;
                if (v._colDue > 0f && now >= v._colDue)
                {
                    v._colDue = 0f;
                    v.FitCollider();
                }
            }
            catch { }
        }
    }

    public static void ForceCheck()
    {
        foreach (var m in Modules.Snapshot()) if (m.Visual != null) m.Visual._nextCheck = 0f;
    }

    public void Destroy()
    {
        try { if (_root != null && !_root.WasCollected) Object.Destroy(_root); } catch { }
        _root = null;
        _parts.Clear();
    }

    private void Maintain()
    {
        try
        {
            var model = _m.Vo.GetComponent<VehicleObjectModel>() ?? _m.Vo.GetComponentInChildren<VehicleObjectModel>(true);
            if (model != null)
            {
                model.MarkDynamic();
                model.SetVisible(false);
            }
        }
        catch { }

        var models = ModelRenderers();
        if (_root == null || _root.WasCollected) Build(models);
        if (_root == null) return;
        Fit();
        SyncOverlay(true);
        if (_m.Kind != ModuleKind.Era) SyncLook(models);

        var vehicle = Modules.VehicleOf(_m);
        var battle = false;
        try { battle = vehicle != null && vehicle.BehaviourEnabled; } catch { }
        if (!battle) FitCollider();
        foreach (var r in models)
        {
            if (!r.forceRenderingOff) r.forceRenderingOff = true;
            if (!battle) continue;
            var t = r.transform;
            if (!_shrunk.ContainsKey(t.Pointer)) _shrunk[t.Pointer] = (t, t.localScale);
            if (t.localScale != Vector3.zero) t.localScale = Vector3.zero;
        }
        if (!battle && _shrunk.Count > 0)
        {
            foreach (var (t, scale) in _shrunk.Values)
                try { if (t != null && !t.WasCollected) t.localScale = scale; } catch { }
            _shrunk.Clear();
        }

        
        ApplyHidden();
        if (_m.Kind == ModuleKind.Panel) ApplyVented();
    }

    
    public void ApplyVented()
    {
        if (_root == null || _root.WasCollected || _parts.Count == 0) return;
        var vented = _m.InBattle && _m.Vented;
        var plate = _parts[0];
        if (plate != null && !plate.WasCollected && plate.activeSelf == vented) plate.SetActive(!vented);
        if (_opening != null && !_opening.WasCollected && _opening.activeSelf != vented) _opening.SetActive(vented);
    }

    
    public GameObject Plate => _m.Kind == ModuleKind.Panel && _parts.Count > 0 && _parts[0] != null && !_parts[0].WasCollected ? _parts[0] : null;

    private GameObject _opening;
    private static Material _openingMat;

    private GameObject OpeningPart()
    {
        var mat = OpeningMaterial();
        if (mat == null) return null;
        var go = new GameObject("opening");
        go.transform.SetParent(_root.transform, false);
        go.layer = _root.layer;
        go.AddComponent<MeshFilter>().sharedMesh = Box();
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        go.SetActive(false);
        return go;
    }

    
    private static Material OpeningMaterial()
    {
        if (_openingMat != null && !_openingMat.WasCollected) return _openingMat;
        var shader = Shader.Find("HDRP/Lit");
        if (shader == null) return null;
        _openingMat = new Material(shader) { name = "Blow-off opening" };
        _openingMat.SetColor("_BaseColor", new Color(0.025f, 0.022f, 0.02f, 1f));
        try { _openingMat.SetFloat("_Smoothness", 0.05f); } catch { }
        _openingMat.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        return _openingMat;
    }

    
    
    
    
    private List<Renderer> _models;
    private bool _modelsBattle;
    private float _modelsAt = -100f;

    private List<Renderer> ModelRenderers()
    {
        var battle = _m.InBattle;
        var now = Time.unscaledTime;
        if (_models != null && _modelsBattle == battle && now - _modelsAt < 5f)
        {
            var ok = true;
            foreach (var r in _models)
                if (r == null || r.WasCollected || !r) { ok = false; break; }
            if (ok) return _models;
        }
        _models = CollectRenderers(battle);
        _modelsBattle = battle;
        _modelsAt = now + UnityEngine.Random.Range(0f, 1f); 
        return _models;
    }

    private List<Renderer> CollectRenderers(bool battle)
    {
        var list = new List<Renderer>();
        var own = _root != null && !_root.WasCollected ? _root.transform : null;
        foreach (var r in _m.T.GetComponentsInChildren<Renderer>(true))
            if (r != null && (own == null || !r.transform.IsChildOf(own))) list.Add(r);
        if (!battle) return list;
        try
        {
            var vehicle = Modules.VehicleOf(_m);
            var history = vehicle != null ? vehicle.transformHeirarchyFlattenHistory?.history : null;
            if (history != null)
            {
                var me = _m.T.Pointer;
                foreach (var rep in history)
                {
                    var old = rep.Old;
                    if (old == null) continue;
                    if (old.Pointer != me && !old.IsChildOf(_m.T)) continue;
                    var target = rep.Target;
                    if (target == null) continue;
                    foreach (var r in target.GetComponentsInChildren<Renderer>(true)) if (r != null) list.Add(r);
                }
            }
        }
        catch { }
        return list;
    }

    private void Build(List<Renderer> models)
    {
        Renderer source = null;
        foreach (var r in models) if (r != null && r.sharedMaterial != null) { source = r; break; }
        if (source == null && FallbackMaterial() == null) return;

        if (!_loggedBounds && source != null)
        {
            _loggedBounds = true;
            try
            {
                var b = source.bounds;
                Plugin.Log.LogInfo($"APS {_m.Kind} base model bounds: size {b.size.x:0.###} x {b.size.y:0.###} x {b.size.z:0.###} m");
            }
            catch { }
        }

        _root = new GameObject("APS module visual");
        _root.transform.SetParent(_m.T, false);
        _root.layer = _m.T.gameObject.layer;
        _parts.Clear();
        _glow = null;
        switch (_m.Kind)
        {
            case ModuleKind.RadioJammer:
            case ModuleKind.Detector:
                _parts.Add(Part("body", Box(), source));
                _parts.Add(Part("antenna", Box(), source));
                break;
            case ModuleKind.Launcher:
                _parts.Add(Part("tube", Tube(), source));
                break;
            case ModuleKind.Warner:
                _parts.Add(Part("body", Box(), source));
                _parts.Add(Part("lens", Tube(), source));
                break;
            case ModuleKind.Smoke:
                _parts.Add(Part("body", Box(), source));
                for (var i = 0; i < 3; i++) _parts.Add(Part("tube", Tube(), source));
                break;
            case ModuleKind.Jammer:
                _parts.Add(Part("body", Box(), source));
                _parts.Add(Part("lens", Box(), source));
                _glow = GlowPart();
                break;
            case ModuleKind.Era:
            {
                
                
                var body = Part("body", Box(), source);
                var mr = body.GetComponent<MeshRenderer>();
                if (mr != null)
                {
                    mr.enabled = false;
                    mr.forceRenderingOff = true;
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                }
                _parts.Add(body);
                break;
            }
            case ModuleKind.Panel:
                
                _parts.Add(Part("plate", Box(), source));
                _opening = OpeningPart();
                break;
        }
        _lastSize = Vector3.zero;
    }

    
    private MeshRenderer _glow;
    private static readonly MaterialPropertyBlock GlowMpb = new();
    private static readonly int TintId = Shader.PropertyToID("_Tint");
    private static Mesh _quad;

    private MeshRenderer GlowPart()
    {
        var mat = Assets.FlashMat;
        if (mat == null) return null;
        var go = new GameObject("glow");
        go.transform.SetParent(_root.transform, false);
        go.layer = _root.layer;
        go.AddComponent<MeshFilter>().sharedMesh = Quad();
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        go.SetActive(false);
        return mr;
    }

    
    public static void UpdateGlows()
    {
        var t = Time.time;
        foreach (var m in Modules.Snapshot())
        {
            if (m.Kind != ModuleKind.Jammer || m.Visual?._glow == null) continue;
            var glow = m.Visual._glow;
            if (glow.WasCollected) { m.Visual._glow = null; continue; }
            var on = m.JamOn && m.InBattle && !m.Destroyed;
            if (glow.gameObject.activeSelf != on) glow.gameObject.SetActive(on);
            if (!on) continue;
            
            var flicker = 0.75f + 0.25f * Mathf.Sin(t * 37f + m.Vuid) * Mathf.Sin(t * 11f);
            GlowMpb.Clear();
            GlowMpb.SetVector(TintId, new Vector4(1f, 0.06f, 0.03f, 0.55f * flicker));
            glow.SetPropertyBlock(GlowMpb);
        }
    }

    private static Mesh Quad()
    {
        if (_quad != null && !_quad.WasCollected) return _quad;
        _quad = new Mesh { name = "APS glow quad" };
        _quad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) };
        _quad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
        _quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        _quad.RecalculateBounds();
        _quad.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        return _quad;
    }

    private GameObject Part(string name, Mesh mesh, Renderer source)
    {
        var go = new GameObject(name);
        go.transform.SetParent(_root.transform, false);
        go.layer = _root.layer;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.shadowCastingMode = ShadowCastingMode.On;
        
        
        mr.sharedMaterial = _applied ?? FallbackMaterial();
        if (source != null)
            try { mr.renderingLayerMask = source.renderingLayerMask; }
            catch { }
        return go;
    }

    
    private bool Fit()
    {
        var size = _m.Size;
        var lossy = _m.T.lossyScale;
        if (size == _lastSize && lossy == _lastLossy) return false;
        _lastSize = size;
        _lastLossy = lossy;
        float Inv(float v) => Mathf.Abs(v) > 1e-5f ? 1f / v : 1f;
        var root = _root.transform;
        root.localPosition = Vector3.zero;
        root.localRotation = Quaternion.identity;
        root.localScale = new Vector3(Inv(lossy.x), Inv(lossy.y), Inv(lossy.z));
        switch (_m.Kind)
        {
            case ModuleKind.RadioJammer:
            case ModuleKind.Detector:
            {
                Body(size);
                var antenna = _parts[1].transform;
                antenna.localPosition = new Vector3(0f, 0f, size.z * 1.02f);
                antenna.localScale = new Vector3(size.x * 0.82f, size.y * 0.82f, size.z * 0.05f);
                break;
            }
            case ModuleKind.Launcher:
            {
                var tube = _parts[0].transform;
                tube.localPosition = Vector3.zero;
                tube.localScale = size;
                break;
            }
            case ModuleKind.Warner:
            {
                Body(size);
                
                var lens = _parts[1].transform;
                lens.localPosition = new Vector3(0f, 0f, size.z);
                lens.localScale = new Vector3(size.y * 0.6f, size.y * 0.6f, size.z * 0.35f);
                break;
            }
            case ModuleKind.Smoke:
            {
                Body(size);
                
                var d = size.y * 0.5f;
                for (var i = 0; i < 3; i++)
                {
                    var tube = _parts[1 + i].transform;
                    tube.localPosition = new Vector3((i - 1) * size.x * 0.3f, -size.y * 0.05f, size.z * 0.55f);
                    tube.localRotation = Quaternion.Euler(-12f, 0f, 0f);
                    tube.localScale = new Vector3(d, d, size.z * 0.75f);
                }
                break;
            }
            case ModuleKind.Era:
                Body(size);
                break;
            case ModuleKind.Panel:
            {
                Body(size);
                if (_opening != null && !_opening.WasCollected)
                {
                    var o = _opening.transform;
                    o.localPosition = new Vector3(0f, 0f, 0.003f);
                    o.localScale = new Vector3(size.x * 0.97f, size.y * 0.97f, 0.004f);
                }
                break;
            }
            case ModuleKind.Jammer:
            {
                Body(size);
                var lens = _parts[1].transform;
                lens.localPosition = new Vector3(0f, 0f, size.z * 1.02f);
                lens.localScale = new Vector3(size.x * 0.8f, size.y * 0.8f, size.z * 0.06f);
                if (_glow != null)
                {
                    var g = _glow.transform;
                    g.localPosition = new Vector3(0f, 0f, size.z * 1.06f);
                    g.localRotation = Quaternion.identity;
                    var s = Mathf.Max(size.x, size.y) * 1.6f;
                    g.localScale = new Vector3(s, s, 1f);
                }
                break;
            }
        }
        return true;
    }

    
    
    

    private Sprocket.Vehicles.VehicleRenderer _base;
    private float _baseAt = -100f;
    private IntPtr _overlay;
    private bool _overlayShown = true;

    private Sprocket.Vehicles.VehicleRenderer BaseRenderer()
    {
        var now = Time.unscaledTime;
        if (_base != null && !_base.WasCollected && now - _baseAt < 1f && now >= _baseAt) return _base;
        _baseAt = now;
        _base = null;
        try
        {
            var model = _m.Vo.GetComponent<VehicleObjectModel>() ?? _m.Vo.GetComponentInChildren<VehicleObjectModel>(true);
            var group = model?.model?.TryCast<Sprocket.Vehicles.VehicleRendererGroup>();
            var lods = group?.lods;
            if (lods != null && lods.Length > 0) _base = lods[0];
        }
        catch { }
        return _base;
    }

    
    
    
    
    private void SyncOverlay(bool full)
    {
        if (_overlay == IntPtr.Zero && !full) return;
        var was = _overlay;
        _overlay = IntPtr.Zero;
        _overlayShown = true;
        if (_root == null || _root.WasCollected || !_root.activeSelf) return;
        foreach (var go in _parts)
        {
            var mr = go != null && !go.WasCollected ? go.GetComponent<MeshRenderer>() : null;
            if (mr != null && !IsHiddenPart(go) && mr.forceRenderingOff) mr.forceRenderingOff = false;
        }
        if (was != IntPtr.Zero) SyncLook(ModelRenderers());
    }

    
    internal static bool IsOverlayMaterial(Material m)
    {
        if (m == null) return false;
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

    
    private bool DesignerHidden => !_m.InBattle && DesignerState.Active && !ModernDayTags.PartsVisible;

    
    
    private bool BattleHidden
    {
        get
        {
            try { return _m.Vo != null && ModernDayTags.HiddenInBattle(_m.Vo.Pointer); }
            catch { return false; }
        }
    }

    private void ApplyHidden()
    {
        if (_root == null || _root.WasCollected) return;
        var hidden = (_m.InBattle && _m.Destroyed) || DesignerHidden || BattleHidden;
        if (hidden == _lastHidden && _root.activeSelf != hidden) return;
        _lastHidden = hidden;
        _root.SetActive(!hidden);
    }

    
    private bool IsHiddenPart(GameObject go) => _m.Kind == ModuleKind.Era;

    

    private static readonly MaterialPropertyBlock LookMpb = new();

    
    
    
    
    
    private void SyncLook(List<Renderer> models)
    {
        var (source, mat, index, overlay) = PaintSource();
        if (mat == null || IsOverlayMaterial(mat))
        {
            
            if (_applied != null && !IsOverlayMaterial(_applied)) return;
            mat = FallbackMaterial();
            if (mat == null) return;
        }
        
        
        if (!overlay)
        {
            LookMpb.Clear();
            if (source != null)
            {
                if (index >= 0) source.GetPropertyBlock(LookMpb, index);
                if (LookMpb.isEmpty) source.GetPropertyBlock(LookMpb);
            }
        }
        if (_applied == null || _applied.Pointer != mat.Pointer)
            Plugin.Log.LogInfo($"APS {_m.Kind} module paint: '{mat.name}' from '{(source != null ? source.name : "none")}'" +
                               $"{(source != null && LookMpb.isEmpty ? " (no property block)" : "")}");
        _applied = mat;
        foreach (var go in _parts)
        {
            if (go == null || go.WasCollected) continue;
            var mr = go.GetComponent<MeshRenderer>();
            if (mr == null) continue;
            var current = mr.sharedMaterial;
            if (current == null || current.Pointer != mat.Pointer) mr.sharedMaterial = mat;
            if (!overlay) mr.SetPropertyBlock(LookMpb);
            if (source != null) mr.renderingLayerMask = source.renderingLayerMask;
        }
    }

    private Material _applied;
    private Renderer _paint;
    private Sprocket.Vehicles.PlateStructures.PlateStructure _plate;
    private int _paintIndex;
    private float _nextPaintSearch;

    
    
    
    private (Renderer, Material, int, bool) PaintSource()
    {
        try
        {
            if (_paint != null && !_paint.WasCollected && _plate != null && !_plate.WasCollected)
            {
                var mats = _paint.sharedMaterials;
                if (mats != null && _paintIndex < mats.Length)
                {
                    
                    var overlay = false;
                    try { overlay = _plate.highDetailRenderer?.overlayMaterial != null; } catch { }
                    var current = mats[_paintIndex];
                    var mat = BaseMaterial(_plate, _paintIndex) ?? (IsOverlayMaterial(current) ? null : current);
                    if (mat != null) return (_paint, mat, _paintIndex, overlay);
                }
            }
        }
        catch { }
        _paint = null;
        if (Time.unscaledTime < _nextPaintSearch) return (null, null, -1, false);
        
        _nextPaintSearch = Time.unscaledTime + 5f;
        try
        {
            var plate = HostPlate();
            if (plate == null) return (null, null, -1, false);
            _plate = plate;
            var exterior = BaseMaterial(plate, 0);
            var hi = plate.highDetailRenderer?.renderer;
            if (hi != null && !hi.WasCollected && hi.sharedMaterials != null && hi.sharedMaterials.Length > 0)
            {
                _paint = hi;
                _paintIndex = Math.Max(0, IndexOf(hi.sharedMaterials, exterior));
            }
            else if (exterior != null)
            {
                
                var root = Modules.VehicleOf(_m);
                if (root != null)
                    foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        var i = r != null ? IndexOf(r.sharedMaterials, exterior) : -1;
                        if (i < 0) continue;
                        _paint = r;
                        _paintIndex = i;
                        break;
                    }
                if (_paint == null) return (null, exterior, -1, false);
            }
            if (_paint != null)
            {
                var current = _paint.sharedMaterials[_paintIndex];
                return (_paint, BaseMaterial(plate, _paintIndex) ?? (IsOverlayMaterial(current) ? null : current), _paintIndex, false);
            }
        }
        catch { }
        return (null, null, -1, false);
    }

    
    
    private static Material BaseMaterial(Sprocket.Vehicles.PlateStructures.PlateStructure plate, int index)
    {
        Material m = null;
        try { m = (plate.exteriorMaterial ?? plate.defaultExteriorMaterial)?.Material; } catch { }
        if (m == null || IsOverlayMaterial(m))
            try
            {
                var vms = plate.highDetailRenderer?.materials;
                if (vms != null && index >= 0 && index < vms.Length) m = vms[index]?.Material;
            }
            catch { }
        if (m == null || IsOverlayMaterial(m))
            try { m = plate.highDetailRenderer?.material?.Material; } catch { }
        return m != null && !IsOverlayMaterial(m) ? m : null;
    }

    private static int IndexOf(Il2CppReferenceArray<Material> mats, Material m)
    {
        if (mats == null || m == null) return -1;
        for (var i = 0; i < mats.Length; i++)
            if (mats[i] != null && mats[i].Pointer == m.Pointer) return i;
        return -1;
    }

    
    private Sprocket.Vehicles.PlateStructures.PlateStructure HostPlate()
    {
        try
        {
            var t = _m.Vo.VehicleTransform?.Parent;
            for (var depth = 0; t != null && depth < 16; depth++)
            {
                var plate = PlateOf(t.VehicleObject);
                if (plate != null) return plate;
                t = t.Parent;
            }
        }
        catch { }
        try
        {
            var root = Modules.VehicleOf(_m);
            if (root != null)
                foreach (var vo in root.GetComponentsInChildren<VehicleObject>(true))
                {
                    var plate = PlateOf(vo);
                    if (plate != null) return plate;
                }
        }
        catch { }
        return null;
    }

    private static Sprocket.Vehicles.PlateStructures.PlateStructure PlateOf(VehicleObject vo)
    {
        if (vo == null) return null;
        foreach (var c in vo.behaviours)
        {
            var p = c != null ? c.TryCast<Sprocket.Vehicles.PlateStructures.PlateStructure>() : null;
            if (p != null) return p;
        }
        return null;
    }

    

    private IntPtr _colPtr;
    private int _colCount = -1;
    private Vector3 _colFirst, _colSize, _colLossy;

    
    private static readonly int[] BoxTris = MakeBoxTris();

    private static int[] MakeBoxTris()
    {
        int[][] quads = { new[] { 0, 2, 6, 4 }, new[] { 1, 5, 7, 3 }, new[] { 0, 4, 5, 1 }, new[] { 2, 3, 7, 6 }, new[] { 0, 1, 3, 2 }, new[] { 4, 6, 7, 5 } };
        var list = new List<int>();
        foreach (var q in quads)
        {
            list.AddRange(new[] { q[0], q[1], q[2], q[0], q[2], q[3] });
            list.AddRange(new[] { q[0], q[2], q[1], q[0], q[3], q[2] });
        }
        return list.ToArray();
    }

    
    
    
    private void FitCollider()
    {
        if (_parts.Count == 0) return;
        VehicleObjectModel model = null;
        try { model = _m.Vo.GetComponent<VehicleObjectModel>() ?? _m.Vo.GetComponentInChildren<VehicleObjectModel>(true); } catch { }
        if (model == null) return;
        var col = model.interactionCollider?.TryCast<VehicleMeshCollider>();
        if (col == null) return;
        var tr = col.Transform;
        if (tr == null || tr.WasCollected) return;
        var current = col.Vertices;
        if (col.Pointer == _colPtr && current != null && _colCount > 0 && current.Length == _colCount && current[0] == _colFirst &&
            _colSize == _lastSize && _colLossy == _lastLossy) return;
        var scale = tr.lossyScale;
        if (Mathf.Abs(scale.x * scale.y * scale.z) < 1e-12f) return;

        var toCollider = tr.worldToLocalMatrix;
        var verts = new List<Vector3>();
        var tris = new List<int>();
        foreach (var go in _parts)
        {
            if (go == null || go.WasCollected) continue;
            var mesh = go.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh == null) continue;
            var b = mesh.bounds;
            var m = toCollider * go.transform.localToWorldMatrix;
            var start = verts.Count;
            for (var i = 0; i < 8; i++)
            {
                var c = new Vector3((i & 1) != 0 ? b.max.x : b.min.x, (i & 2) != 0 ? b.max.y : b.min.y, (i & 4) != 0 ? b.max.z : b.min.z);
                verts.Add(m.MultiplyPoint3x4(c));
            }
            foreach (var t in BoxTris) tris.Add(start + t);
        }
        if (verts.Count == 0) return;
        var va = new Il2CppStructArray<Vector3>(verts.Count);
        for (var i = 0; i < verts.Count; i++) va[i] = verts[i];
        var ta = new Il2CppStructArray<int>(tris.Count);
        for (var i = 0; i < tris.Count; i++) ta[i] = tris[i];
        col.SetMesh(va, ta);
        try { col.MarkModified(); } catch { }
        _colPtr = col.Pointer;
        _colCount = verts.Count;
        _colFirst = verts[0];
        _colSize = _lastSize;
        _colLossy = _lastLossy;
        if (Plugin.DebugLogging.Value) Plugin.Log.LogInfo($"APS {_m.Kind} selection collider fitted ({_parts.Count} boxes)");
    }

    private void Body(Vector3 size)
    {
        var body = _parts[0].transform;
        body.localPosition = new Vector3(0f, 0f, size.z * 0.5f);
        body.localScale = size;
    }

    private static Material FallbackMaterial()
    {
        if (_fallbackMat != null && !_fallbackMat.WasCollected) return _fallbackMat;
        var shader = Shader.Find("HDRP/Lit");
        if (shader == null) return null;
        _fallbackMat = new Material(shader) { name = "APS module" };
        _fallbackMat.SetColor("_BaseColor", new Color(0.3f, 0.33f, 0.26f, 1f));
        _fallbackMat.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        return _fallbackMat;
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
        _box = Finish(new Mesh { name = "APS box" }, verts, normals, uvs, tris);
        return _box;
    }

    
    private static Mesh Tube()
    {
        if (_tube != null && !_tube.WasCollected) return _tube;
        
        var profile = new[]
        {
            new Vector2(0f, 0f), new Vector2(0f, 0.5f),
            new Vector2(0.06f, 0.5f), new Vector2(0.06f, 0.535f), new Vector2(0.13f, 0.535f), new Vector2(0.13f, 0.5f),
            new Vector2(0.84f, 0.5f), new Vector2(0.84f, 0.545f), new Vector2(1f, 0.545f),
            new Vector2(1f, 0.4f), new Vector2(0.72f, 0.4f), new Vector2(0.72f, 0f)
        };
        const int seg = 24;
        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();
        var along = 0f;
        for (var p = 0; p + 1 < profile.Length; p++)
        {
            var a = profile[p];
            var b = profile[p + 1];
            var d = b - a;
            var len = d.magnitude;
            if (len < 1e-5f) continue;
            
            var nz = -d.y / len;
            var nr = d.x / len;
            var start = verts.Count;
            for (var i = 0; i <= seg; i++)
            {
                var ang = i / (float)seg * Mathf.PI * 2f;
                var c = Mathf.Cos(ang);
                var s = Mathf.Sin(ang);
                verts.Add(new Vector3(a.y * c, a.y * s, a.x));
                verts.Add(new Vector3(b.y * c, b.y * s, b.x));
                var n = new Vector3(nr * c, nr * s, nz);
                normals.Add(n);
                normals.Add(n);
                uvs.Add(new Vector2(i / (float)seg, along));
                uvs.Add(new Vector2(i / (float)seg, along + len));
            }
            along += len;
            for (var i = 0; i < seg; i++)
            {
                var i0 = start + i * 2;
                var i1 = i0 + 1;
                var i2 = i0 + 2;
                var i3 = i0 + 3;
                var face = Vector3.Cross(verts[i1] - verts[i0], verts[i2] - verts[i0]);
                var nn = normals[i0];
                if (face.sqrMagnitude < 1e-12f) face = Vector3.Cross(verts[i3] - verts[i1], verts[i2] - verts[i1]);
                if (Vector3.Dot(face, nn) >= 0f) tris.AddRange(new[] { i0, i1, i2, i1, i3, i2 });
                else tris.AddRange(new[] { i0, i2, i1, i1, i2, i3 });
            }
        }
        _tube = Finish(new Mesh { name = "APS launcher tube" }, verts, normals, uvs, tris);
        return _tube;
    }

    private static Mesh Finish(Mesh mesh, List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs, List<int> tris)
    {
        mesh.vertices = verts.ToArray();
        mesh.normals = normals.ToArray();
        mesh.uv = uvs.ToArray();
        mesh.triangles = tris.ToArray();
        mesh.RecalculateBounds();
        try { mesh.RecalculateTangents(); } catch { }
        mesh.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        return mesh;
    }
}
