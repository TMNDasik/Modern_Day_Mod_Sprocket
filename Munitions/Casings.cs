using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using HarmonyLib;
using Sprocket.Vehicles;
using Sprocket.Vehicles.Cannons;
using Sprocket.Vehicles.Weapons;
using Sprocket.Vehicles.Weapons.Cannons;
using Sprocket.WeaponFramework;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Munitions;



internal static class EjectBinding
{
    private static readonly Regex Token = new(@"\s?\[eject:(-?\d+)\]", RegexOptions.Compiled);

    public static int Read(Cannon cannon)
    {
        try
        {
            var d = cannon?.InstanceBlueprint?.Description;
            if (string.IsNullOrEmpty(d)) return 0;
            var m = Token.Match(d);
            return m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
        }
        catch { return 0; }
    }

    public static string With(string description, int portVuid)
    {
        var d = Token.Replace(description ?? "", "");
        if (portVuid == 0) return d.Length == 0 ? null : d;
        var token = $"[eject:{portVuid}]";
        return d.Length == 0 ? token : d + " " + token;
    }
}





internal static class Casings
{
    
    private sealed class Casing
    {
        public GameObject Go;
        public Collider Col;
        public Rigidbody Body;
        public Vector3 Scale;
        public float Age;
        public List<Collider> Ignored;   
        public Vector3 LastVelocity;
        public float LastSound = -10f;   
        public bool Sounded;
        public float Pitch;              
    }

    
    private const float HitThreshold = 1.2f, RepeatHitThreshold = 2.5f, RepeatGap = 1.2f;

    private const float PassThroughOwn = 0.6f, FadeTime = 1.5f;

    
    private sealed class Ejection
    {
        public Cannon Cannon;
        public Transform Port;
        public bool Stub;
        public float At;
    }

    
    private sealed class PortAnim
    {
        public Transform Port;
        public readonly List<(Transform T, Vector3 Pos, Quaternion Rot)> Parts = new();
        public Vector3 PivotLocal;       
        public float OpenAt, CloseAt;
    }

    private const int MaxCasings = 80;
    private const float OpenAngle = 100f, OpenTime = 0.35f, CloseTime = 0.5f;
    private const float OpenLead = 0.6f, HoldAfter = 0.5f;   

    private static readonly List<Casing> Live = new();
    private static readonly List<Ejection> Scheduled = new();
    private static readonly Dictionary<IntPtr, PortAnim> Anims = new();
    private static readonly HashSet<IntPtr> Pending = new();   
    private static readonly System.Random Rng = new();
    private static Mesh _cylinder;

    private static float R(float a, float b) => a + (float)Rng.NextDouble() * (b - a);

    

    [HarmonyPatch(typeof(CannonBehaviour), nameof(CannonBehaviour.FireInternal))]
    private static class FirePatch
    {
        private static void Postfix(CannonBehaviour __instance)
        {
            try
            {
                var ptr = __instance.Pointer;
                if (!Launchers.IsLauncher(ptr) && !AutoFeed.IsAuto(ptr) && __instance.caliber >= Plugin.MinCaliber.Value) Pending.Add(ptr);
            }
            catch { }
        }
    }

    
    [HarmonyPatch(typeof(LoadTask), nameof(LoadTask.SetState))]
    private static class LoadingPatch
    {
        private static void Postfix(LoadTask __instance)
        {
            if (Pending.Count == 0) return;
            try
            {
                if ((int)__instance.state != (int)LoadState.Loading) return;
                var target = __instance.target;
                if (target == null || !Pending.Contains(target.Pointer)) return;
                var weapon = new WeaponBehaviour(target.Pointer);
                var cannon = weapon.transform != null ? weapon.transform.GetComponentInParent<Cannon>() : null;
                OnLoadingStarted(cannon, target.Pointer, false, OpenLead);
            }
            catch { }
        }
    }

    
    
    public static void OnLoadingStarted(Cannon cannon, IntPtr weapon, bool stub, float delay)
    {
        if (!Pending.Remove(weapon)) return;
        if (!Plugin.CasingsEnabled.Value || cannon == null) return;
        try
        {
            var port = PortFor(cannon);
            if (port == null) return;
            var at = Time.time + Math.Max(0f, delay);
            Scheduled.Add(new Ejection { Cannon = cannon, Port = port, Stub = stub, At = at });
            var cal = cannon.Blueprint != null ? cannon.Blueprint.Caliber / 1000f : 0.1f;
            Animate(port, at - OpenLead, at + HoldAfter, cal);
        }
        catch (Exception e) { Plugin.Log.LogWarning("Case ejection failed: " + e.Message); }
    }

    

    private static readonly Dictionary<int, (float Time, List<VehicleObject> Ports)> PortCache = new();

    public static Transform VehicleRoot(Component c)
    {
        var v = c.GetComponentInParent<Vehicle>();
        return v != null ? v.transform : c.transform.root;
    }

    
    public static List<VehicleObject> PortsOf(Transform root)
    {
        var key = root.gameObject.GetInstanceID();
        if (PortCache.TryGetValue(key, out var cached) && Time.unscaledTime - cached.Time < 1f && Time.unscaledTime >= cached.Time) return cached.Ports;
        var ports = new List<VehicleObject>();
        foreach (var vo in root.GetComponentsInChildren<VehicleObject>(true))
            if (EjectorPart.IsEjector(vo))
            {
                ports.Add(vo);
                PortVisuals.Register(vo);
            }
        ports.Sort((a, b) => Vuid(a).CompareTo(Vuid(b)));
        PortCache[key] = (Time.unscaledTime, ports);
        return ports;
    }

    public static int Vuid(VehicleObject vo)
    {
        try { return vo.VUID.Value; }
        catch { return 0; }
    }

    
    public static List<Cannon> GunsOf(Transform root)
    {
        var guns = new List<Cannon>();
        foreach (var c in root.GetComponentsInChildren<Cannon>(true))
            if (c != null && !LauncherPart.IsLauncher(c) && c.Blueprint != null && c.Blueprint.Caliber >= Plugin.MinCaliber.Value) guns.Add(c);
        return guns;
    }

    
    
    public static Transform PortFor(Cannon cannon)
    {
        var root = VehicleRoot(cannon);
        var ports = PortsOf(root);
        if (ports.Count == 0) return null;

        var bound = EjectBinding.Read(cannon);
        if (bound != 0)
            foreach (var p in ports)
                if (p != null && !p.WasCollected && Vuid(p) == bound) return p.transform;

        var guns = GunsOf(root);
        var taken = new HashSet<int>();
        foreach (var g in guns)
        {
            var b = EjectBinding.Read(g);
            if (b != 0) taken.Add(b);
        }
        Transform best = null;
        var bestDist = float.MaxValue;
        foreach (var p in ports)
        {
            if (p == null || p.WasCollected || taken.Contains(Vuid(p))) continue;
            var pos = p.transform.position;
            Cannon nearest = null;
            var nd = float.MaxValue;
            foreach (var g in guns)
            {
                if (EjectBinding.Read(g) != 0) continue; 
                var d = (g.transform.position - pos).sqrMagnitude;
                if (d < nd) { nd = d; nearest = g; }
            }
            if (nearest == null || nearest.Pointer != cannon.Pointer) continue;
            if (nd < bestDist) { bestDist = nd; best = p.transform; }
        }
        return best;
    }

    

    private static void Eject(Ejection e)
    {
        var cannon = e.Cannon;
        var port = e.Port;
        if (cannon == null || cannon.WasCollected || port == null || port.WasCollected) return;
        var mat = Fx.CasingMat;
        if (mat == null) return;
        
        var bp = cannon.Blueprint;
        var diameter = bp != null ? bp.Caliber / 1000f : 0.1f;
        var caseLength = bp != null ? bp.PropellantLength / 1000f : 0.5f;
        var length = e.Stub ? Mathf.Max(0.04f, 0.8f * diameter) : Mathf.Clamp(caseLength, 0.05f, 1.5f);
        var root = VehicleRoot(cannon);

        var dir = port.forward;
        var pos = port.position + dir * (0.05f + diameter * 0.5f);
        
        var rot = Quaternion.LookRotation(dir) * Quaternion.Euler(90f, 0f, 0f) * Quaternion.Euler(R(-20f, 20f), 0f, R(-20f, 20f));
        var scale = new Vector3(diameter, length * 0.5f, diameter); 

        var go = new GameObject("Munitions casing");
        Object.DontDestroyOnLoad(go);
        go.transform.SetPositionAndRotation(pos, rot);
        go.transform.localScale = scale;
        go.AddComponent<MeshFilter>().sharedMesh = Cylinder();
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.On;

        var own = new List<Collider>();
        foreach (var oc in root.GetComponentsInChildren<Collider>())
            if (oc != null && oc.enabled && !oc.isTrigger) own.Add(oc);
        go.layer = CasingLayer(pos, root, own);

        var col = go.AddComponent<MeshCollider>();
        col.sharedMesh = Cylinder();
        col.convex = true;
        foreach (var oc in own) Physics.IgnoreCollision(col, oc, true);

        var rb = go.AddComponent<Rigidbody>();
        
        rb.mass = Mathf.Clamp(8500f * (Mathf.PI * diameter * length * 0.003f + Mathf.PI * 0.25f * diameter * diameter * 0.01f), 0.05f, 25f);
        rb.linearDamping = 0.05f;
        rb.angularDamping = 0.3f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        var body = port.GetComponentInParent<Rigidbody>();
        var inherited = body != null ? body.GetPointVelocity(port.position) : Vector3.zero;
        rb.linearVelocity = inherited + dir * R(2.5f, 4.5f) + Vector3.up * R(0.5f, 1.5f) + Random3() * 0.4f;
        rb.angularVelocity = Random3() * R(5f, 14f);

        
        var pitch = Mathf.Clamp(Mathf.Pow(0.125f / Mathf.Max(0.02f, diameter), 0.35f), 0.75f, 1.35f) * (e.Stub ? 0.92f : 1f) * R(0.95f, 1.05f);
        Live.Add(new Casing { Go = go, Col = col, Body = rb, Scale = scale, Ignored = own, LastVelocity = rb.linearVelocity, Pitch = pitch });
        while (Live.Count > MaxCasings) Remove(0);
    }

    private static int _casingLayer = -1;

    
    private static int CasingLayer(Vector3 pos, Transform root, List<Collider> own)
    {
        if (_casingLayer >= 0) return _casingLayer;
        var ground = 0;
        try
        {
            foreach (var h in Physics.RaycastAll(pos + Vector3.up, Vector3.down, 50f, ~0, QueryTriggerInteraction.Ignore))
                if (h.collider != null && !h.collider.transform.IsChildOf(root)) { ground = h.collider.gameObject.layer; break; }
        }
        catch { }
        var vehicle = own.Count > 0 ? own[0].gameObject.layer : 0;
        const int ignoreRaycast = 2;
        var layer = !Physics.GetIgnoreLayerCollision(ignoreRaycast, ground) && !Physics.GetIgnoreLayerCollision(ignoreRaycast, vehicle)
            ? ignoreRaycast
            : !Physics.GetIgnoreLayerCollision(0, ground) ? 0 : ground;
        _casingLayer = layer;
        if (Plugin.DebugLogging.Value) Plugin.Log.LogInfo($"Casing physics layer {layer} (ground {ground}, vehicles {vehicle})");
        return layer;
    }

    private static Vector3 Random3() => new Vector3(R(-1f, 1f), R(-1f, 1f), R(-1f, 1f)).normalized;

    
    private static Mesh Cylinder()
    {
        if (_cylinder != null && !_cylinder.WasCollected) return _cylinder;
        const int n = 20;
        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var tris = new List<int>();
        
        for (var i = 0; i <= n; i++)
        {
            var a = i * Mathf.PI * 2f / n;
            var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            verts.Add(d * 0.5f + Vector3.down); normals.Add(d);
            verts.Add(d * 0.5f + Vector3.up); normals.Add(d);
        }
        for (var i = 0; i < n; i++)
        {
            var v = i * 2;
            tris.AddRange(new[] { v, v + 1, v + 2, v + 1, v + 3, v + 2 });
        }
        
        for (var cap = 0; cap < 2; cap++)
        {
            var y = cap == 0 ? -1f : 1f;
            var normal = cap == 0 ? Vector3.down : Vector3.up;
            var center = verts.Count;
            verts.Add(new Vector3(0f, y, 0f)); normals.Add(normal);
            for (var i = 0; i <= n; i++)
            {
                var a = i * Mathf.PI * 2f / n;
                verts.Add(new Vector3(Mathf.Cos(a) * 0.5f, y, Mathf.Sin(a) * 0.5f)); normals.Add(normal);
            }
            for (var i = 0; i < n; i++)
            {
                if (cap == 0) tris.AddRange(new[] { center, center + 1 + i, center + 2 + i });
                else tris.AddRange(new[] { center, center + 2 + i, center + 1 + i });
            }
        }
        _cylinder = new Mesh { name = "Munitions casing" };
        _cylinder.vertices = verts.ToArray();
        _cylinder.normals = normals.ToArray();
        _cylinder.triangles = tris.ToArray();
        _cylinder.RecalculateBounds();
        _cylinder.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        return _cylinder;
    }

    

    private static int _sceneHandle;

    public static void Tick(float dt)
    {
        var now = Time.time;
        
        try
        {
            var handle = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
            if (handle != _sceneHandle)
            {
                if (_sceneHandle != 0) ClearAll("scene changed");
                _sceneHandle = handle;
            }
        }
        catch { }
        for (var i = Scheduled.Count - 1; i >= 0; i--)
        {
            if (now < Scheduled[i].At) continue;
            var e = Scheduled[i];
            Scheduled.RemoveAt(i);
            try { Eject(e); }
            catch (Exception ex) { Plugin.Log.LogWarning("Case ejection failed: " + ex.Message); }
        }
        if (Anims.Count > 0) UpdateAnims(now);

        if (Live.Count == 0 || dt <= 0f) return;
        var life = Mathf.Max(2f, Plugin.CasingLifetime.Value);
        for (var i = Live.Count - 1; i >= 0; i--)
        {
            var c = Live[i];
            if (c.Go == null || c.Go.WasCollected) { Live.RemoveAt(i); continue; }
            c.Age += dt;
            try
            {
                
                if (c.Ignored != null && c.Age > PassThroughOwn)
                {
                    foreach (var oc in c.Ignored)
                        if (oc != null && !oc.WasCollected) Physics.IgnoreCollision(c.Col, oc, false);
                    c.Ignored = null;
                }
                Impact(c, dt);
                
                if (c.Age > life)
                {
                    var k = 1f - (c.Age - life) / FadeTime;
                    if (k <= 0f || c.Go.transform.position.y < -2000f) { Remove(i); continue; }
                    c.Go.transform.localScale = c.Scale * Mathf.SmoothStep(0f, 1f, k);
                }
            }
            catch { Remove(i); }
        }
    }

    
    private static void Impact(Casing c, float dt)
    {
        var rb = c.Body;
        if (rb == null || rb.WasCollected) return;
        var v = rb.linearVelocity;
        var jolt = (v - c.LastVelocity - Physics.gravity * dt).magnitude;
        c.LastVelocity = v;
        if (c.Age < 0.05f) return;
        var first = !c.Sounded && jolt > HitThreshold;
        var again = c.Sounded && jolt > RepeatHitThreshold && c.Age - c.LastSound > RepeatGap;
        if (!first && !again) return;
        c.Sounded = true;
        c.LastSound = c.Age;
        
        var strength = Mathf.Clamp01((jolt - 0.8f) / 4f);
        var volume = (first ? 0.45f + 0.55f * strength : 0.25f + 0.45f * strength) * Mathf.Max(0f, Plugin.CasingSoundVolume.Value);
        Fx.PlayCasingDrop(c.Go.transform, c.Go.transform.position, volume, c.Pitch, first);
    }

    
    public static void ClearAll(string why)
    {
        var n = Live.Count;
        for (var i = Live.Count - 1; i >= 0; i--) Remove(i);
        Scheduled.Clear();
        Pending.Clear();
        foreach (var a in Anims.Values)
            foreach (var (t, pos, rot) in a.Parts)
                try
                {
                    if (t == null || t.WasCollected) continue;
                    t.localPosition = pos;
                    t.localRotation = rot;
                }
                catch { }
        Anims.Clear();
        Wires.Clear(); 
        Effects.ClearEmitters(); 
        if (n > 0 && Plugin.DebugLogging.Value) Plugin.Log.LogInfo($"Casings cleared ({why}): {n}");
    }

    
    [HarmonyPatch(typeof(Sprocket.Gameplay.VehicleDesignerPlayerState), nameof(Sprocket.Gameplay.VehicleDesignerPlayerState.Enter))]
    private static class EditorEnterPatch
    {
        private static void Postfix()
        {
            try { ClearAll("vehicle editor"); }
            catch (Exception e) { Plugin.Log.LogWarning("Casing cleanup failed: " + e.Message); }
        }
    }

    private static void Remove(int i)
    {
        try { if (Live[i].Go != null && !Live[i].Go.WasCollected) Object.Destroy(Live[i].Go); } catch { }
        Live.RemoveAt(i);
    }

    
    

    private static void Animate(Transform port, float openAt, float closeAt, float caliber)
    {
        if (!Anims.TryGetValue(port.Pointer, out var a))
        {
            var lid = PortVisuals.Lid(port);
            if (lid == null) return;
            a = new PortAnim { Port = port };
            a.Parts.Add((lid, lid.localPosition, lid.localRotation));
            a.PivotLocal = PortVisuals.HingeLocal(port);
            Anims[port.Pointer] = a;
        }
        
        a.OpenAt = Time.time < a.CloseAt && Time.time > a.OpenAt ? a.OpenAt : openAt;
        a.CloseAt = Math.Max(closeAt, a.OpenAt + OpenTime);
    }

    private static void UpdateAnims(float now)
    {
        List<IntPtr> done = null;
        foreach (var kv in Anims)
        {
            var a = kv.Value;
            if (a.Port == null || a.Port.WasCollected) { (done ??= new List<IntPtr>()).Add(kv.Key); continue; }
            float open;
            if (now < a.OpenAt) open = 0f;
            else if (now < a.CloseAt) open = Mathf.Clamp01((now - a.OpenAt) / OpenTime);
            else open = 1f - Mathf.Clamp01((now - a.CloseAt) / CloseTime);
            
            open = Mathf.SmoothStep(0f, 1f, open);
            var angle = -OpenAngle * open; 
            var pivot = a.Port.TransformPoint(a.PivotLocal);
            var axis = a.Port.right;
            foreach (var (t, pos, rot) in a.Parts)
            {
                if (t == null || t.WasCollected) continue;
                t.localPosition = pos;
                t.localRotation = rot;
                if (angle != 0f) t.RotateAround(pivot, axis, angle);
            }
            if (now > a.CloseAt + CloseTime) (done ??= new List<IntPtr>()).Add(kv.Key);
        }
        if (done != null) foreach (var k in done) Anims.Remove(k);
    }
}