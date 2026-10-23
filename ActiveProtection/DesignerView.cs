using System;
using System.Collections.Generic;
using Sprocket.Vehicles;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace ActiveProtection;





internal static class DesignerView
{
    private sealed class Zone
    {
        public GameObject Root;
        public MeshRenderer Front, Back;
        public Mesh Mesh;
        public int Version = -1;
        public Vector3 Size;
        public ModuleKind Kind;
        public bool Used;
    }

    private static readonly Dictionary<IntPtr, Zone> Zones = new();
    
    private static readonly Dictionary<IntPtr, Zone> HostZones = new();
    private static readonly MaterialPropertyBlock Mpb = new();
    private static readonly int AlphaScaleId = Shader.PropertyToID("_AlphaScale");

    private static GameObject _linesGo;
    private static Mesh _linesMesh;
    private static MeshRenderer _linesRenderer;
    private static readonly List<Vector3> LineVerts = new();
    private static readonly List<Color> LineColors = new();
    private static readonly List<Vector2> LineUv = new();
    private static readonly List<int> LineTris = new();

    private static readonly Color LinkColour = new(0.55f, 0.88f, 1f, 0.95f);
    private static readonly Color LinkColourDim = new(0.55f, 0.88f, 1f, 0.45f);
    private static readonly Color EoLinkColour = new(0.85f, 0.65f, 1f, 0.95f);
    private static readonly Color EoLinkColourDim = new(0.85f, 0.65f, 1f, 0.45f);
    private static readonly Color PanelLinkColour = new(1f, 0.55f, 0.2f, 0.95f);
    private static readonly Color PanelLinkColourDim = new(1f, 0.55f, 0.2f, 0.45f);

    public static void Render()
    {
        foreach (var z in Zones.Values) z.Used = false;
        foreach (var z in HostZones.Values) z.Used = false;
        LineVerts.Clear();
        LineColors.Clear();
        LineUv.Clear();
        LineTris.Clear();

        var cam = DesignerState.Active ? ViewCamera() : null;
        
        if (DesignerState.Active && cam != null && Assets.ZoneMat != null && ModernDayTags.PartsVisible)
        {
            try { Draw(cam); }
            catch (Exception e) { if (Plugin.DebugLogging.Value) Plugin.Log.LogWarning("APS zones failed: " + e.Message); }
        }
        
        else if (!DesignerState.Active && Assets.ZoneMat != null && EoPlayer.InControl && BattleOverlayHook.Active)
        {
            var camera = EoPlayer.Camera != null && !EoPlayer.Camera.WasCollected && EoPlayer.Camera.isActiveAndEnabled ? EoPlayer.Camera : ViewCamera();
            if (camera != null)
                try { DrawBattle(camera, EoPlayer.Vehicle); }
                catch (Exception e)
                {
                    if (_battleErrors++ < 3) Plugin.Log.LogWarning("APS zones in the damage view failed: " + e.Message);
                }
        }

        
        List<IntPtr> dead = null;
        foreach (var kv in Zones)
        {
            var z = kv.Value;
            if (z.Used) continue;
            if (Modules.Get(kv.Key) == null)
            {
                (dead ??= new List<IntPtr>()).Add(kv.Key);
                try { if (z.Root != null && !z.Root.WasCollected) Object.Destroy(z.Root); } catch { }
                continue;
            }
            if (z.Root != null && !z.Root.WasCollected && z.Root.activeSelf) z.Root.SetActive(false);
        }
        if (dead != null) foreach (var k in dead) Zones.Remove(k);

        List<IntPtr> deadHosts = null;
        foreach (var kv in HostZones)
        {
            var z = kv.Value;
            if (z.Used) continue;
            if (!EraHosts.Has(kv.Key))
            {
                (deadHosts ??= new List<IntPtr>()).Add(kv.Key);
                try { if (z.Root != null && !z.Root.WasCollected) Object.Destroy(z.Root); } catch { }
                continue;
            }
            if (z.Root != null && !z.Root.WasCollected && z.Root.activeSelf) z.Root.SetActive(false);
        }
        if (deadHosts != null) foreach (var k in deadHosts) HostZones.Remove(k);
        FlushLines();
    }

    private static Material _overlay;

    
    private static Material OverlayMat()
    {
        if (_overlay != null && !_overlay.WasCollected) return _overlay;
        var zone = Assets.ZoneMat;
        if (zone == null) return null;
        _overlay = new Material(zone) { name = "ERA elements overlay" };
        _overlay.SetFloat("_ZTest", (float)CompareFunction.Always);
        _overlay.SetFloat("_Hidden", 1f);
        _overlay.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        return _overlay;
    }

    private static void ShowHost(EraHost h, float alpha)
    {
        if (!HostZones.TryGetValue(h.Ptr, out var z) || z.Root == null || z.Root.WasCollected)
        {
            if (z?.Root != null && !z.Root.WasCollected) Object.Destroy(z.Root);
            z = Create(null);
            if (z == null) return;
            z.Root.name = "ERA elements";
            
            
            var overlay = OverlayMat();
            if (overlay != null)
            {
                z.Front.sharedMaterial = overlay;
                z.Back.enabled = false;
            }
            HostZones[h.Ptr] = z;
        }
        z.Used = true;
        if (z.Version != h.Version)
        {
            z.Mesh = EraLayoutMesh.Build(h.C, z.Mesh);
            z.Version = h.Version;
            z.Front.GetComponent<MeshFilter>().sharedMesh = z.Mesh;
            z.Back.GetComponent<MeshFilter>().sharedMesh = z.Mesh;
        }
        var t = h.T;
        var root = z.Root.transform;
        root.SetPositionAndRotation(t.position, t.rotation);
        root.localScale = t.lossyScale;
        if (!z.Root.activeSelf) z.Root.SetActive(true);
        Mpb.Clear();
        Mpb.SetFloat(AlphaScaleId, alpha);
        z.Front.SetPropertyBlock(Mpb);
        z.Back.SetPropertyBlock(Mpb);
    }

    
    
    [HarmonyLib.HarmonyPatch(typeof(DesignerFramework.DesignPhotographer), nameof(DesignerFramework.DesignPhotographer.Capture))]
    private static class PhotoPatch
    {
        private static void Prefix()
        {
            try { HideAll(); }
            catch { }
        }
    }

    private static void HideAll()
    {
        foreach (var z in Zones.Values)
            if (z.Root != null && !z.Root.WasCollected && z.Root.activeSelf) z.Root.SetActive(false);
        foreach (var z in HostZones.Values)
            if (z.Root != null && !z.Root.WasCollected && z.Root.activeSelf) z.Root.SetActive(false);
        if (_linesGo != null && !_linesGo.WasCollected && _linesGo.activeSelf) _linesGo.SetActive(false);
    }

    private static Camera _camera;
    private static float _cameraSearch;

    
    public static Camera ViewCamera()
    {
        var main = Camera.main;
        if (main != null && main.isActiveAndEnabled) return main;
        if (_camera != null && !_camera.WasCollected && _camera.isActiveAndEnabled) return _camera;
        if (Time.unscaledTime < _cameraSearch) return null;
        _cameraSearch = Time.unscaledTime + 1f;
        try
        {
            foreach (var c in Object.FindObjectsOfType<Camera>())
                if (c != null && c.isActiveAndEnabled && c.targetTexture == null) { _camera = c; return c; }
        }
        catch { }
        return null;
    }

    private static void Draw(Camera cam)
    {
        var selected = DesignerState.Selected();
        var showAll = Plugin.ShowAllZones.Value;
        var all = Modules.Snapshot();

        
        foreach (var m in all)
        {
            if (m.Kind != ModuleKind.Era) continue;
            var v = Modules.VehicleOf(m);
            if (v == null) continue;
            try { if (v.BehaviourEnabled) continue; } catch { }
            ShowZone(m, selected.Count == 0 || selected.Contains(m) ? 1f : 0.6f);
        }

        
        foreach (var h in new List<EraHost>(EraHosts.Snapshot()))
        {
            if (!h.C.On) continue;
            var v = EraHosts.VehicleOf(h);
            if (v == null) continue;
            try { if (v.BehaviourEnabled) continue; } catch { }
            var isSelected = DesignerState.IsSelected(h.Ptr);
            if (!isSelected && !showAll) continue;
            try { ShowHost(h, isSelected ? 1f : 0.45f); }
            catch (Exception e) { if (Plugin.DebugLogging.Value) Plugin.Log.LogWarning("ERA elements view failed: " + e.Message); }
        }
        if (selected.Count == 0 && !showAll) return;

        var groups = new Dictionary<IntPtr, List<Module>>();
        foreach (var m in all)
        {
            var v = Modules.VehicleOf(m);
            if (v == null) continue;
            try { if (v.BehaviourEnabled) continue; } catch { }
            if (!groups.TryGetValue(v.Pointer, out var list)) groups[v.Pointer] = list = new List<Module>();
            list.Add(m);
        }

        foreach (var list in groups.Values)
        {
            List<Blowoff.RackInfo> racks = null;
            foreach (var m in list)
            {
                if (m.Kind == ModuleKind.Era) continue;
                var isSelected = selected.Contains(m);
                if (m.Kind == ModuleKind.Panel)
                {
                    
                    if ((!isSelected && !showAll) || m.S.Racks.Length == 0) continue;
                    racks ??= Blowoff.RacksCached(Modules.VehicleOf(m));
                    foreach (var r in racks)
                        if (m.S.Protects(r.Vuid))
                            AddLine(cam, m.Center, r.Center, isSelected ? PanelLinkColour : PanelLinkColourDim, isSelected ? 3f : 2f);
                    continue;
                }
                if (isSelected || showAll) ShowZone(m, isSelected || selected.Count == 0 ? 1f : 0.45f);
            }
            
            foreach (var l in list)
            {
                if (EoInfo.LinkTarget(l.Kind) == null || l.S.Detector == 0) continue;
                var d = Modules.DetectorOf(l, list);
                if (d == null) continue;
                var bright = selected.Contains(l) || selected.Contains(d);
                if (!bright && !showAll) continue;
                var eo = !EoInfo.IsAps(l.Kind);
                var col = eo ? (bright ? EoLinkColour : EoLinkColourDim) : bright ? LinkColour : LinkColourDim;
                AddLine(cam, d.Center, l.Center, col, bright ? 3f : 2f);
            }
        }
    }

    

    private static int _battleErrors;
    private static int _battleLogged = -1;

    
    
    private static void DrawBattle(Camera cam, Vehicle vehicle)
    {
        if (vehicle == null || vehicle.WasCollected) return;
        var list = new List<Module>();
        foreach (var m in Modules.Snapshot())
        {
            var v = Modules.VehicleOf(m);
            if (v != null && v.Pointer == vehicle.Pointer) list.Add(m);
        }
        var zones = 0;
        List<Blowoff.RackInfo> racks = null;
        foreach (var m in list)
        {
            if (m.Kind == ModuleKind.Panel)
            {
                if (m.S.Racks.Length == 0 || m.Destroyed) continue;
                racks ??= Blowoff.RacksCached(vehicle);
                foreach (var r in racks)
                    if (m.S.Protects(r.Vuid)) AddLine(cam, m.Center, r.Center, PanelLinkColour, 2.5f);
                continue;
            }
            var spent = (m.Kind == ModuleKind.Launcher || m.Kind == ModuleKind.Smoke) && m.ChargesLeft <= 0;
            ShowZone(m, m.Destroyed ? 0.12f : spent ? 0.35f : 0.85f);
            zones++;
        }
        foreach (var l in list)
        {
            if (EoInfo.LinkTarget(l.Kind) == null || l.S.Detector == 0) continue;
            var d = Modules.DetectorOf(l, list);
            if (d == null) continue;
            var working = !l.Destroyed && !d.Destroyed;
            var eo = !EoInfo.IsAps(l.Kind);
            AddLine(cam, d.Center, l.Center, eo ? (working ? EoLinkColour : EoLinkColourDim) : working ? LinkColour : LinkColourDim, working ? 2.5f : 1.5f);
        }
        var hosts = 0;
        foreach (var h in new List<EraHost>(EraHosts.Snapshot()))
        {
            if (!h.C.On || h.BlownOff) continue;
            var v = EraHosts.VehicleOf(h);
            if (v == null || v.Pointer != vehicle.Pointer) continue;
            ShowHost(h, 0.7f);
            hosts++;
        }
        var count = zones * 1000 + hosts;
        if (count != _battleLogged)
        {
            _battleLogged = count;
            Plugin.Log.LogInfo($"Damage view: {zones} APS / EO zone(s), {hosts} ERA part(s) shown for your vehicle");
        }
    }

    private static void ShowZone(Module m, float alpha)
    {
        if (!Zones.TryGetValue(m.Ptr, out var z) || z.Root == null || z.Root.WasCollected || z.Kind != m.Kind)
        {
            if (z?.Root != null && !z.Root.WasCollected) Object.Destroy(z.Root);
            z = Create(m);
            if (z == null) return;
            Zones[m.Ptr] = z;
        }
        z.Used = true;
        
        var size = m.Kind == ModuleKind.Era ? m.Size : Vector3.zero;
        if (z.Version != m.Version || z.Size != size)
        {
            z.Mesh = m.Kind == ModuleKind.Era ? EraMesh.Build(m.S, z.Mesh) : ZoneMesh.Build(m.S, z.Mesh);
            z.Version = m.Version;
            z.Size = size;
            z.Front.GetComponent<MeshFilter>().sharedMesh = z.Mesh;
            z.Back.GetComponent<MeshFilter>().sharedMesh = z.Mesh;
        }
        var t = z.Root.transform;
        t.SetPositionAndRotation(m.Center, m.Rotation);
        t.localScale = Vector3.one;
        if (!z.Root.activeSelf) z.Root.SetActive(true);
        Mpb.Clear();
        Mpb.SetFloat(AlphaScaleId, alpha);
        z.Front.SetPropertyBlock(Mpb);
        z.Back.SetPropertyBlock(Mpb);
    }

    private static Zone Create(Module m)
    {
        var front = Assets.ZoneMat;
        var back = Assets.ZoneHiddenMat;
        if (front == null) return null;
        var z = new Zone { Kind = m?.Kind ?? ModuleKind.Era };
        z.Root = new GameObject("APS zone");
        Object.DontDestroyOnLoad(z.Root);
        z.Front = Layer(z.Root, "visible", front);
        z.Back = Layer(z.Root, "behind armour", back ?? front);
        return z;
    }

    private static MeshRenderer Layer(GameObject root, string name, Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root.transform, false);
        go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        return mr;
    }

    

    private static void AddLine(Camera cam, Vector3 a, Vector3 b, Color col, float pixels)
    {
        var ct = cam.transform;
        var camPos = ct.position;
        var pixelAngle = cam.fieldOfView * Mathf.Deg2Rad / Mathf.Max(240, cam.pixelHeight);
        var dir = b - a;
        if (dir.sqrMagnitude < 1e-6f) return;
        var mid = (a + b) * 0.5f;
        var side = Vector3.Cross(dir, camPos - mid);
        if (side.sqrMagnitude < 1e-8f) return;
        side.Normalize();
        var wa = (a - camPos).magnitude * pixelAngle * pixels * 0.5f;
        var wb = (b - camPos).magnitude * pixelAngle * pixels * 0.5f;
        var s = LineVerts.Count;
        LineVerts.Add(a - side * wa); LineVerts.Add(a + side * wa); LineVerts.Add(b + side * wb); LineVerts.Add(b - side * wb);
        LineUv.Add(new Vector2(0f, 0f)); LineUv.Add(new Vector2(0f, 1f)); LineUv.Add(new Vector2(1f, 1f)); LineUv.Add(new Vector2(1f, 0f));
        for (var i = 0; i < 4; i++) LineColors.Add(col);
        LineTris.Add(s); LineTris.Add(s + 1); LineTris.Add(s + 2);
        LineTris.Add(s); LineTris.Add(s + 2); LineTris.Add(s + 3);
        
        Dot(cam, a, col, pixels * 3f);
        Dot(cam, b, col, pixels * 3f);
    }

    private static void Dot(Camera cam, Vector3 p, Color col, float pixels)
    {
        var ct = cam.transform;
        var pixelAngle = cam.fieldOfView * Mathf.Deg2Rad / Mathf.Max(240, cam.pixelHeight);
        var h = (p - ct.position).magnitude * pixelAngle * pixels * 0.5f;
        var r = ct.right * h;
        var u = ct.up * h;
        var s = LineVerts.Count;
        LineVerts.Add(p - u); LineVerts.Add(p - r); LineVerts.Add(p + u); LineVerts.Add(p + r);
        LineUv.Add(new Vector2(0.5f, 0f)); LineUv.Add(new Vector2(0f, 0.5f)); LineUv.Add(new Vector2(0.5f, 1f)); LineUv.Add(new Vector2(1f, 0.5f));
        for (var i = 0; i < 4; i++) LineColors.Add(col);
        LineTris.Add(s); LineTris.Add(s + 1); LineTris.Add(s + 2);
        LineTris.Add(s); LineTris.Add(s + 2); LineTris.Add(s + 3);
    }

    private static void FlushLines()
    {
        if (LineVerts.Count == 0)
        {
            if (_linesGo != null && !_linesGo.WasCollected && _linesGo.activeSelf) _linesGo.SetActive(false);
            return;
        }
        var mat = Assets.LineMat;
        if (mat == null) return;
        if (_linesGo == null || _linesGo.WasCollected || _linesMesh == null || _linesMesh.WasCollected)
        {
            _linesGo = new GameObject("APS links");
            Object.DontDestroyOnLoad(_linesGo);
            _linesMesh = new Mesh { name = "APS links" };
            _linesMesh.MarkDynamic();
            _linesMesh.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            _linesGo.AddComponent<MeshFilter>().sharedMesh = _linesMesh;
            _linesRenderer = _linesGo.AddComponent<MeshRenderer>();
            _linesRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _linesRenderer.receiveShadows = false;
        }
        _linesRenderer.sharedMaterial = mat;
        if (!_linesGo.activeSelf) _linesGo.SetActive(true);
        _linesMesh.Clear();
        _linesMesh.vertices = LineVerts.ToArray();
        _linesMesh.colors = LineColors.ToArray();
        _linesMesh.uv = LineUv.ToArray();
        _linesMesh.triangles = LineTris.ToArray();
        _linesMesh.bounds = new Bounds(Vector3.zero, new Vector3(1e5f, 1e5f, 1e5f));
    }
}
