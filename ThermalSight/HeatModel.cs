using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Sprocket.Vehicles;
using UnityEngine;
using UnityEngine.Rendering;

namespace ThermalSight;








internal sealed unsafe class HeatModel
{
    private enum Part { None, Hull, Vent, Exhaust, Running, Track, Barrel, Ambient }

    private const int ArrayHeader = 0x20;          
    private const int VertexBudget = 60000;        
    private const float ColorInterval = 1f;        

    private sealed class HeatRenderer
    {
        public VehicleHeat Owner;
        public Renderer Renderer;
        public MeshFilter Filter;
        public Part Part;
        public IntPtr SourceMesh;      
        public Mesh HeatMesh;          
        public Vector3[] Vertices, Normals;
        public Color32[] ColorsManaged;               
        public Il2CppStructArray<Color32> Colors;     
        public Vector3 Center;         
        public float MaxR = 1e-3f;
        public bool NoCopy;            
        public GameObject Proxy;       
        public MeshRenderer ProxyRenderer;
        public bool ProxyVisible;
        public float NextColor;
        public bool Dead;
    }

    private sealed class VehicleHeat
    {
        public Vehicle Vehicle;
        public float Engine = 0.85f;    
        public float Running = 0.45f;   
        public float Barrel = 0.08f;    
        public float Burning;           
        public int RendererCount = -1;
        public float NextCount;
        public bool Far;
        public Transform EngineTransform;
        public Transform TransmissionTransform;
        public Vector3 LocalMin, LocalMax;   
        public readonly List<Transform> Cannons = new();
        public float NextColorUpdate;
        public readonly List<HeatRenderer> Renderers = new();
    }

    
    
    public const float TempRange = 1.5f;

    private readonly Dictionary<IntPtr, VehicleHeat> _vehicles = new();
    private readonly List<HeatRenderer> _all = new();   
    private bool _listDirty = true;
    private int _visCursor, _colorCursor;
    private readonly MaterialPropertyBlock _mpb = new();
    private Material _overlay;
    private bool _overlayFailed;
    private float _nextScan, _nextCleanup;
    private bool _scanning;
    private bool _loggedDraw;
    private int _statCopy, _statFlat;
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    public static HeatModel Instance;

    public HeatModel() { Instance = this; }

    
    public void OnCannonFired(Component cannon)
    {
        try
        {
            var v = cannon.GetComponentInParent<Vehicle>();
            if (v == null || !_vehicles.TryGetValue(v.Pointer, out var heat)) return;
            heat.Barrel = Math.Min(1f, heat.Barrel + 0.22f);
            heat.NextColorUpdate = 0; 
            foreach (var hr in heat.Renderers) if (hr.Part == Part.Barrel) hr.NextColor = 0;
        }
        catch (Exception e) { Plugin.Log.LogWarning("OnCannonFired: " + e.Message); }
    }

    
    public void Update(float dt, bool scan)
    {
        if (scan && (!_scanning || Time.unscaledTime > _nextScan))
        {
            _nextScan = Time.unscaledTime + 2f;
            Rescan();
        }
        else if (!scan && Time.unscaledTime > _nextCleanup)
        {
            
            _nextCleanup = Time.unscaledTime + 10f;
            RemoveDead();
        }
        _scanning = scan;

        
        foreach (var h in _vehicles.Values)
        {
            if (!Alive(h.Vehicle)) continue;
            var speed = 0f;
            try { var body = h.Vehicle.body; if (body != null) speed = body.velocity.magnitude; } catch { }
            h.Running += dt * (Math.Min(speed, 15f) * 0.004f - (h.Running - 0.3f) / 400f);
            h.Running = Math.Clamp(h.Running, 0f, 1f);
            h.Barrel -= dt * (h.Barrel - 0.06f) / 300f;
            h.Burning = Math.Max(0f, h.Burning - dt / 900f);
            var target = 0.85f + Math.Min(speed, 12f) / 80f;
            h.Engine += (target - h.Engine) * Math.Min(1f, dt / 60f);
        }
    }

    
    public void Draw(Camera cam)
    {
        var mat = GetOverlay();
        if (mat == null || cam == null) return;

        var camPos = cam.transform.position;
        var scale = Plugin.HeatOverlayScale.Value;
        var draws = 0;
        var now = Time.unscaledTime;

        foreach (var h in _vehicles.Values)
        {
            if (!Alive(h.Vehicle)) continue;
            if (Vector3.Distance(camPos, h.Vehicle.transform.position) > 6000f) continue;

            
            var refresh = now >= h.NextColorUpdate;
            if (refresh) h.NextColorUpdate = now + 1f;

            foreach (var hr in h.Renderers)
            {
                if (hr.Dead || !Alive(hr.Renderer) || !Alive(hr.Filter)) continue;
                
                if (!hr.Renderer.enabled || !hr.Renderer.gameObject.activeInHierarchy) continue;
                var mesh = hr.Filter.sharedMesh;
                if (mesh == null) continue;

                Mesh drawMesh;
                if (!hr.NoCopy && (Alive(hr.HeatMesh) || TryCreateHeatMesh(hr, mesh)))
                {
                    if (refresh || hr.ColorsManaged == null) UpdateColors(h, hr, true);
                    drawMesh = hr.HeatMesh;
                    
                    _mpb.SetVector(ColorId, new Vector4(0f, 0f, scale, 1f));
                    _statCopy++;
                }
                else
                {
                    drawMesh = mesh;
                    _mpb.SetVector(ColorId, new Vector4(0f, 0f, FlatTemperature(h, hr.Part) / TempRange * scale, 1f));
                    _statFlat++;
                }

                
                var world = hr.Renderer.transform.position;
                var toCam = camPos - world;
                var d = toCam.magnitude;
                var shiftLen = Math.Min(0.012f + d * 0.0003f, d * 0.004f);
                var shift = d > 1e-3f ? toCam * (shiftLen / d) : Vector3.zero;
                var matrix = Matrix4x4.Translate(shift) * hr.Renderer.transform.localToWorldMatrix;
                for (var sub = 0; sub < drawMesh.subMeshCount; sub++)
                {
                    Graphics.DrawMesh(drawMesh, matrix, mat, 0, cam, sub, _mpb, false, false);
                    draws++;
                }
            }
        }

        if (!_loggedDraw && Plugin.DebugLogging.Value)
        {
            _loggedDraw = true;
            Plugin.Log.LogInfo($"Heat overlay: {draws} draws for {_vehicles.Count} vehicles (gradient meshes {_statCopy}, flat {_statFlat})");
        }
    }

    
    public struct VehicleInfo
    {
        public IntPtr Key;
        public Transform Transform;
        public float Engine, Running, Width, Length, DeckHeight;
        public Vector3? EnginePos;
    }

    public IEnumerable<VehicleInfo> Vehicles
    {
        get
        {
            foreach (var kv in _vehicles)
            {
                var h = kv.Value;
                if (!Alive(h.Vehicle)) continue;
                var tr = h.Vehicle.transform;
                var size = h.LocalMax - h.LocalMin;
                Vector3? engine = null;
                var deck = 0.8f;
                if (Alive(h.EngineTransform))
                {
                    engine = h.EngineTransform.position;
                    deck = Math.Max(0.2f, h.LocalMax.y - tr.InverseTransformPoint(h.EngineTransform.position).y);
                }
                yield return new VehicleInfo
                {
                    Key = kv.Key, Transform = tr, Engine = h.Engine, Running = h.Running,
                    Width = size.x > 0.5f ? size.x : 3f, Length = size.z > 0.5f ? size.z : 6f,
                    DeckHeight = deck, EnginePos = engine
                };
            }
        }
    }

    
    public bool TryNearestCannonDirection(Vector3 pos, out Vector3 dir)
    {
        dir = Vector3.forward;
        var best = 8f;
        var found = false;
        foreach (var h in _vehicles.Values)
            foreach (var c in h.Cannons)
            {
                if (!Alive(c)) continue;
                var d = Vector3.Distance(c.position, pos);
                if (d >= best) continue;
                best = d;
                dir = Vector3.Dot(pos - c.position, c.forward) >= 0 ? c.forward : -c.forward;
                found = true;
            }
        return found;
    }

    
    public void MarkBurning(Vector3 pos, float dt)
    {
        foreach (var h in _vehicles.Values)
        {
            if (!Alive(h.Vehicle)) continue;
            if (Vector3.Distance(h.Vehicle.transform.position, pos) > 7f) continue;
            h.Burning = Math.Min(1f, h.Burning + dt / 60f);
            return;
        }
    }

    
    public bool TryGetMuzzle(Component cannon, out Vector3 muzzle, out Vector3 dir)
    {
        var t = cannon.transform;
        dir = t.forward;
        muzzle = t.position + dir * 3f;
        try
        {
            var v = cannon.GetComponentInParent<Vehicle>();
            if (v == null || !_vehicles.TryGetValue(v.Pointer, out var h)) return false;
            float best = float.MinValue, bestNeg = float.MinValue;
            foreach (var hr in h.Renderers)
            {
                if (hr.Dead || hr.Part != Part.Barrel || !Alive(hr.Renderer)) continue;
                var b = hr.Renderer.bounds;
                var ext = Math.Abs(Vector3.Dot(b.extents, new Vector3(Math.Abs(dir.x), Math.Abs(dir.y), Math.Abs(dir.z))));
                var along = Vector3.Dot(b.center - t.position, dir);
                best = Math.Max(best, along + ext);
                bestNeg = Math.Max(bestNeg, -along + ext);
            }
            if (best == float.MinValue) return false;
            if (bestNeg > best) { dir = -dir; best = bestNeg; }
            muzzle = t.position + dir * best;
            return true;
        }
        catch { return false; }
    }

    

    private Material _proxyMaterial;
    private bool _proxiesVisible;
    private int _createBudget;
    private bool _pendingCreate;
    private static readonly int TemperatureId = Shader.PropertyToID("_Temperature");
    private static readonly int UseVertexId = Shader.PropertyToID("_UseVertex");

    
    
    public void UpdateProxies(bool active, Camera cam)
    {
        if (!active)
        {
            if (_proxiesVisible) SetAllProxies(false);
            return;
        }
        if (_proxyMaterial == null)
        {
            if (!GpuResources.Available) return;
            _proxyMaterial = new Material(GpuResources.ObjectShader) { name = "ThermalProxy", hideFlags = HideFlags.DontSave };
        }
        if (_listDirty) RebuildList();

        var camPos = cam != null ? cam.transform.position : Vector3.zero;
        foreach (var h in _vehicles.Values)
        {
            if (!Alive(h.Vehicle)) { h.Far = true; continue; }
            var far = Vector3.Distance(camPos, h.Vehicle.transform.position) > 6000f;
            if (far != h.Far)
            {
                h.Far = far;
                foreach (var hr in h.Renderers) if (hr.ProxyVisible && far) SetProxyVisible(hr, false);
            }
        }

        var count = _all.Count;
        if (count == 0) return;
        
        var budget = _proxiesVisible && !_pendingCreate ? Math.Max(200, count / 6) : count;
        _proxiesVisible = true;
        _pendingCreate = false;
        _createBudget = 160; 
        for (var k = 0; k < budget && k < count; k++)
        {
            var hr = _all[_visCursor++ % count];
            if (hr.Dead) continue;
            try { CheckRenderer(hr); }
            catch { Kill(hr); }
        }
        _visCursor %= count;

        
        var now = Time.unscaledTime;
        var vertices = 0;
        for (var k = 0; k < count && vertices < VertexBudget; k++)
        {
            var hr = _all[_colorCursor++ % count];
            if (hr.Dead || !hr.ProxyVisible || now < hr.NextColor) continue;
            hr.NextColor = now + ColorInterval;
            try { vertices += ApplyTemperature(hr); }
            catch { Kill(hr); }
        }
        _colorCursor %= count;
    }

    private void CheckRenderer(HeatRenderer hr)
    {
        if (!Alive(hr.Renderer) || !Alive(hr.Filter)) { Kill(hr); return; }
        var visible = !hr.Owner.Far && hr.Renderer.enabled;
        if (!visible)
        {
            if (hr.ProxyVisible) SetProxyVisible(hr, false);
            return;
        }

        
        var mesh = hr.Filter.sharedMesh;
        var meshPtr = mesh != null ? mesh.Pointer : IntPtr.Zero;
        if (meshPtr != hr.SourceMesh)
        {
            ReleaseCopies(hr);
            hr.SourceMesh = meshPtr;
            hr.NoCopy = false;
        }
        if (mesh == null) return;

        if (!Alive(hr.Proxy))
        {
            if (_createBudget <= 0) { _pendingCreate = true; return; }
            
            if (!hr.Renderer.gameObject.activeInHierarchy) return;
            _createBudget--;
            CreateProxy(hr, mesh);
            if (!Alive(hr.Proxy)) return;
            hr.ProxyVisible = true;
            ApplyTemperature(hr);
            hr.NextColor = Time.unscaledTime + ColorInterval;
            return;
        }
        if (!hr.ProxyVisible) SetProxyVisible(hr, true);
    }

    private static void SetProxyVisible(HeatRenderer hr, bool visible)
    {
        hr.ProxyVisible = visible;
        try { if (Alive(hr.ProxyRenderer)) hr.ProxyRenderer.enabled = visible; } catch { }
    }

    
    private int ApplyTemperature(HeatRenderer hr)
    {
        if (!Alive(hr.ProxyRenderer)) return 0;
        _mpb.Clear();
        var work = 16;
        if (Alive(hr.HeatMesh))
        {
            UpdateColors(hr.Owner, hr, false);
            _mpb.SetFloat(UseVertexId, 1f);
            _mpb.SetFloat(TemperatureId, 0f);
            work += hr.Vertices.Length;
        }
        else
        {
            _mpb.SetFloat(UseVertexId, 0f);
            _mpb.SetFloat(TemperatureId, FlatTemperature(hr.Owner, hr.Part) * Plugin.HeatOverlayScale.Value);
        }
        hr.ProxyRenderer.SetPropertyBlock(_mpb);
        return work;
    }

    private void CreateProxy(HeatRenderer hr, Mesh source)
    {
        try
        {
            var mesh = !hr.NoCopy && (Alive(hr.HeatMesh) || TryCreateHeatMesh(hr, source)) ? hr.HeatMesh : source;

            var go = new GameObject("ThermalProxy") { layer = GpuThermalView.ProxyLayer };
            go.transform.SetParent(hr.Renderer.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _proxyMaterial;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            hr.Proxy = go;
            hr.ProxyRenderer = mr;
        }
        catch (Exception e) { Plugin.Log.LogWarning("Thermal proxy failed: " + e.Message); }
    }

    private void SetAllProxies(bool visible)
    {
        foreach (var hr in _all)
            if (!hr.Dead && Alive(hr.ProxyRenderer))
            {
                hr.ProxyRenderer.enabled = visible;
                hr.ProxyVisible = visible;
            }
        _proxiesVisible = visible;
    }

    private void RebuildList()
    {
        _all.Clear();
        foreach (var h in _vehicles.Values)
            foreach (var hr in h.Renderers)
                if (!hr.Dead) _all.Add(hr);
        _visCursor = _colorCursor = 0;
        _listDirty = false;
    }

    
    private void Kill(HeatRenderer hr)
    {
        if (hr.Dead) return;
        hr.Dead = true;
        ReleaseCopies(hr);
        _listDirty = true;
    }

    private static void ReleaseCopies(HeatRenderer hr)
    {
        try { if (Alive(hr.Proxy)) UnityEngine.Object.Destroy(hr.Proxy); } catch { }
        try { if (Alive(hr.HeatMesh)) UnityEngine.Object.Destroy(hr.HeatMesh); } catch { }
        hr.Proxy = null;
        hr.ProxyRenderer = null;
        hr.ProxyVisible = false;
        hr.HeatMesh = null;
        hr.Vertices = hr.Normals = null;
        hr.Colors = null;
        hr.ColorsManaged = null;
    }

    

    
    
    
    private static float FlatTemperature(VehicleHeat h, Part part) => BaseTemperature(h, part) + (part == Part.None ? 0f : h.Burning * 0.7f);

    private static float BaseTemperature(VehicleHeat h, Part part) => part switch
    {
        
        Part.Vent => 1.05f + 0.25f * h.Engine,
        Part.Exhaust => 1.25f + 0.2f * h.Engine,
        Part.Running => 0.50f + 0.12f * h.Running,
        Part.Track => 0.49f + 0.12f * h.Running,
        Part.Barrel => 0.47f + 0.2f * h.Barrel,
        Part.Hull => 0.47f,
        Part.Ambient => 0.46f,
        _ => 0f
    };

    private void UpdateColors(VehicleHeat h, HeatRenderer hr, bool managed)
    {
        var tr = hr.Renderer.transform;
        var l2w = tr.localToWorldMatrix;
        var enginePos = Alive(h.EngineTransform) ? h.EngineTransform.position : h.Vehicle.transform.position;
        var hasTransmission = Alive(h.TransmissionTransform);
        var transPos = hasTransmission ? h.TransmissionTransform.position : Vector3.zero;
        var baseT = FlatTemperature(h, hr.Part);
        var verts = hr.Vertices;
        var normals = hr.Normals;
        var n = verts.Length;

        Color32* pc = null;
        Color32[] mc = null;
        if (managed) mc = hr.ColorsManaged ??= new Color32[n];
        else
        {
            hr.Colors ??= new Il2CppStructArray<Color32>(n);
            pc = (Color32*)((byte*)hr.Colors.Pointer + ArrayHeader);
        }

        var cannons = h.Cannons;
        for (var i = 0; i < n; i++)
        {
            var t = baseT;
            switch (hr.Part)
            {
                case Part.Hull:
                case Part.Ambient:
                case Part.Vent:
                {
                    
                    var p = l2w.MultiplyPoint3x4(verts[i]);
                    var dist2 = (p - enginePos).sqrMagnitude;
                    t += h.Engine * 0.55f * MathF.Exp(-dist2 / (2f * 0.9f * 0.9f));
                    
                    if (hasTransmission) t += h.Engine * 0.25f * MathF.Exp(-(p - transPos).sqrMagnitude / (2f * 0.6f * 0.6f));
                    break;
                }
                case Part.Barrel:
                {
                    
                    var p = l2w.MultiplyPoint3x4(verts[i]);
                    var s = NearestCannonDistance(cannons, p);
                    t += h.Barrel * 0.35f * MathF.Exp(-s / 2.2f);
                    break;
                }
                case Part.Running:
                {
                    
                    var r = (verts[i] - hr.Center).magnitude / hr.MaxR;
                    t += 0.28f * (1f - r) * (1f - r) * (0.4f + h.Running);
                    if (hasTransmission)
                    {
                        var p = l2w.MultiplyPoint3x4(verts[i]);
                        t += h.Engine * 0.3f * MathF.Exp(-(p - transPos).sqrMagnitude / (2f * 0.8f * 0.8f));
                    }
                    break;
                }
            }

            var c = new Color32(0, 0, (byte)Math.Clamp(t / TempRange * 255f + 0.5f, 0f, 255f), 255);
            if (managed) mc[i] = c;
            else pc[i] = c;
        }
        if (managed) hr.HeatMesh.colors32 = mc;
        else hr.HeatMesh.colors32 = hr.Colors;
    }

    private static float NearestCannonDistance(List<Transform> cannons, Vector3 p)
    {
        var best = float.MaxValue;
        foreach (var c in cannons)
        {
            if (c == null) continue;
            var along = Math.Abs(Vector3.Dot(p - c.position, c.forward));
            var off = Vector3.Distance(p, c.position);
            if (off < best + 0.5f) best = Math.Min(best, along);
        }
        return best == float.MaxValue ? 0f : best;
    }

    private static bool TryCreateHeatMesh(HeatRenderer hr, Mesh source)
    {
        try
        {
            if (!source.isReadable || source.vertexCount == 0 || source.vertexCount > 120000 || hr.Part == Part.Track)
            {
                hr.NoCopy = true;
                return false;
            }
            var copy = UnityEngine.Object.Instantiate(source);
            copy.name = source.name + "_heat";
            hr.Vertices = copy.vertices;
            var normals = copy.normals;
            hr.Normals = normals != null && normals.Length == hr.Vertices.Length ? normals : null;
            hr.HeatMesh = copy;
            hr.SourceMesh = source.Pointer;
            hr.Colors = null;
            hr.ColorsManaged = null;
            if (hr.Part == Part.Running)
            {
                var center = Vector3.zero;
                foreach (var v in hr.Vertices) center += v;
                center /= Math.Max(1, hr.Vertices.Length);
                var maxR = 1e-3f;
                foreach (var v in hr.Vertices) maxR = Math.Max(maxR, (v - center).magnitude);
                hr.Center = center;
                hr.MaxR = maxR;
            }
            return true;
        }
        catch
        {
            hr.NoCopy = true;
            return false;
        }
    }

    private Material GetOverlay()
    {
        if (_overlay != null || _overlayFailed) return _overlay;
        try
        {
            var shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null) throw new InvalidOperationException("Hidden/Internal-Colored not found");
            _overlay = new Material(shader) { name = "ThermalHeatOverlay", hideFlags = HideFlags.DontSave };
            
            
            _overlay.SetFloat("_SrcBlend", (float)BlendMode.One);
            _overlay.SetFloat("_DstBlend", (float)BlendMode.Zero);
            _overlay.SetFloat("_ZWrite", 0f);
            _overlay.SetFloat("_ZTest", (float)CompareFunction.LessEqual);
            _overlay.SetFloat("_Cull", (float)CullMode.Back);
            _overlay.SetFloat("_ZBias", -4f);
            _overlay.renderQueue = 3000;
        }
        catch (Exception e)
        {
            _overlayFailed = true;
            Plugin.Log.LogError("Heat overlay material failed: " + e.Message);
        }
        return _overlay;
    }

    

    
    
    private static bool Alive(UnityEngine.Object o)
    {
        if (ReferenceEquals(o, null)) return false;
        try { return !o.WasCollected && o != null; }
        catch { return false; }
    }

    private VehicleHeat GetOrAdd(Vehicle v)
    {
        var ptr = v.Pointer;
        if (!_vehicles.TryGetValue(ptr, out var h))
        {
            h = new VehicleHeat { Vehicle = v };
            _vehicles[ptr] = h;
        }
        return h;
    }

    
    private static bool IsOwn(Renderer r)
    {
        try { return r.gameObject.layer == GpuThermalView.ProxyLayer; }
        catch { return true; }
    }

    private void Rescan()
    {
        try
        {
            var now = Time.unscaledTime;
            var found = UnityEngine.Object.FindObjectsOfType<Vehicle>();
            var alive = new HashSet<IntPtr>();
            var index = 0;
            foreach (var v in found)
            {
                if (v == null) continue;
                alive.Add(v.Pointer);
                var h = GetOrAdd(v);
                
                if (now < h.NextCount) continue;
                h.NextCount = now + 6f + (index++ % 5) * 0.8f;
                var renderers = v.GetComponentsInChildren<Renderer>(true);
                var own = 0;
                foreach (var r in renderers) if (r == null || IsOwn(r)) own++;
                var count = renderers.Length - own;
                if (count != h.RendererCount) Categorize(h, renderers, count);
            }
            var dead = new List<IntPtr>();
            foreach (var k in _vehicles.Keys) if (!alive.Contains(k)) dead.Add(k);
            foreach (var k in dead) Forget(k);
        }
        catch (Exception e) { Plugin.Log.LogWarning("Heat rescan failed: " + e.Message); }
    }

    private void RemoveDead()
    {
        List<IntPtr> dead = null;
        foreach (var kv in _vehicles) if (!Alive(kv.Value.Vehicle)) (dead ??= new List<IntPtr>()).Add(kv.Key);
        if (dead != null) foreach (var k in dead) Forget(k);
    }

    private void Forget(IntPtr vehicle)
    {
        foreach (var hr in _vehicles[vehicle].Renderers) { ReleaseCopies(hr); hr.Dead = true; }
        _vehicles.Remove(vehicle);
        _listDirty = true;
    }

    
    private void Categorize(VehicleHeat h, Il2CppArrayBase<Renderer> renderers, int count)
    {
        var existing = new Dictionary<IntPtr, HeatRenderer>();
        foreach (var hr in h.Renderers)
            if (!hr.Dead && Alive(hr.Renderer)) existing[hr.Renderer.Pointer] = hr;
            else ReleaseCopies(hr);
        h.Renderers.Clear();
        h.RendererCount = count;
        h.NextColorUpdate = 0;
        _listDirty = true;
        var stats = new int[8];
        var added = 0;

        try
        {
            var engine = h.Vehicle.GetComponentInChildren<Sprocket.Vehicles.Engines.CombustionEngine>(true);
            h.EngineTransform = engine != null ? engine.transform : null;
        }
        catch { h.EngineTransform = null; }
        try
        {
            var tb = h.Vehicle.GetComponentInChildren<Sprocket.Vehicles.Transmissions.TransmissionBlock>(true);
            h.TransmissionTransform = tb != null ? tb.transform : null;
        }
        catch { h.TransmissionTransform = null; }
        
        try
        {
            var tr = h.Vehicle.transform;
            Vector3 min = new(float.MaxValue, float.MaxValue, float.MaxValue), max = -min;
            foreach (var r in h.Vehicle.GetComponentsInChildren<Renderer>(false))
            {
                if (r == null || IsOwn(r) || r.gameObject.name.Contains("Shadow")) continue;
                var b = r.bounds;
                for (var i = 0; i < 8; i++)
                {
                    var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var lc = tr.InverseTransformPoint(corner);
                    min = Vector3.Min(min, lc); max = Vector3.Max(max, lc);
                }
            }
            if (min.x < max.x) { h.LocalMin = min; h.LocalMax = max; }
        }
        catch { }
        h.Cannons.Clear();
        try
        {
            foreach (var cannon in h.Vehicle.GetComponentsInChildren<Sprocket.Vehicles.Cannons.Cannon>(true))
                if (cannon != null) h.Cannons.Add(cannon.transform);
        }
        catch { }

        
        Dictionary<IntPtr, Transform> oldParents = null;

        foreach (var r in renderers)
        {
            if (r == null || IsOwn(r)) continue;
            if (existing.Remove(r.Pointer, out var keep))
            {
                h.Renderers.Add(keep);
                stats[(int)keep.Part]++;
                continue;
            }
            var name = r.gameObject.name;
            if (name.Contains("Shadow")) continue;
            var filter = r.GetComponent<MeshFilter>();
            if (filter == null) continue;
            if (oldParents == null) oldParents = FlattenHistory(h);
            
            var part = name.Contains("PlateStructure") ? Part.Hull : Classify(r.transform, oldParents);
            if (part == Part.None) continue;
            h.Renderers.Add(new HeatRenderer { Owner = h, Renderer = r, Filter = filter, Part = part });
            stats[(int)part]++;
            added++;
        }
        
        foreach (var gone in existing.Values) { ReleaseCopies(gone); gone.Dead = true; }

        if (Plugin.DebugLogging.Value)
            Plugin.Log.LogInfo($"Heat map for {h.Vehicle.name}: hull={stats[1]} vents={stats[2]} exhaust={stats[3]} running={stats[4]} " +
                               $"tracks={stats[5]} barrel={stats[6]} other={stats[7]} engine={(Alive(h.EngineTransform))} (new {added}, removed {existing.Count})");
    }

    private static Dictionary<IntPtr, Transform> FlattenHistory(VehicleHeat h)
    {
        var oldParents = new Dictionary<IntPtr, Transform>();
        try
        {
            var history = h.Vehicle.transformHeirarchyFlattenHistory?.history;
            if (history != null)
                foreach (var rep in history)
                    if (rep.Target != null && rep.Old != null) oldParents[rep.Target.Pointer] = rep.Old;
        }
        catch (Exception e) { Plugin.Log.LogWarning("Reparent history unavailable: " + e.Message); }
        return oldParents;
    }

    
    private static Part Classify(Transform t, Dictionary<IntPtr, Transform> oldParents)
    {
        var guard = 0;
        for (var cur = t; cur != null && guard++ < 32;
             cur = oldParents.TryGetValue(cur.Pointer, out var old) ? old : cur.parent)
        {
            var group = cur.GetComponent<VehicleRendererGroup>();
            var groupPart = group != null ? FromPartType(group.partType) : Part.None;

            var hasVehicleObject = false;
            foreach (var c in cur.GetComponents<Component>())
            {
                if (c == null) continue;
                switch (c.GetIl2CppType().Name)
                {
                    case "EngineVent": return Part.Vent;
                    case "EngineExhaust": return Part.Exhaust;
                    case "TrackAssembly":
                    case "TrackBelt":
                    case "TrackBeltSegmentArray":
                        return Part.Track;
                    case "WheelMount":
                    case "WheelMountArray":
                    case "Suspension":
                    case "Axle":
                        return Part.Running;
                    case "Cannon":
                    case "CannonBarrelSegment":
                    case "CannonBarrelModel":
                        return Part.Barrel;
                    case "PlateStructure": return Part.Hull;
                    case "VehicleObject": hasVehicleObject = true; break;
                    
                    case "Vehicle": return Part.Hull;
                }
            }
            
            if (hasVehicleObject) return Part.Ambient;
            if (groupPart != Part.None && !oldParents.ContainsKey(cur.Pointer)) return groupPart;
        }
        return Part.None;
    }

    private static Part FromPartType(VehiclePartType type)
    {
        if ((type & VehiclePartType.TrackBelt) != 0) return Part.Track;
        if ((type & (VehiclePartType.Wheel | VehiclePartType.Suspension)) != 0) return Part.Running;
        if ((type & VehiclePartType.PlateStructure) != 0) return Part.Hull;
        return Part.None;
    }
}
