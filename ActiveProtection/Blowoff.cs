using System;
using System.Collections.Generic;
using HarmonyLib;
using Sprocket.Vehicles;
using Sprocket.Vehicles.AmmunitionStorage;
using Sprocket.Vehicles.Events.Flameouts;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace ActiveProtection;








internal static class Blowoff
{
    

    internal sealed class RackInfo
    {
        public AmmoRack Rack;
        public int Vuid;
        public Vector3 Center;
        public int Caliber, Length, Capacity;
    }

    
    public static List<RackInfo> RacksOf(Vehicle v)
    {
        var list = new List<RackInfo>();
        if (v == null) return list;
        foreach (var r in v.GetComponentsInChildren<AmmoRack>(true))
        {
            if (r == null) continue;
            var info = new RackInfo { Rack = r };
            try { info.Vuid = r.VehicleObject.VUID.Value; }
            catch { continue; }
            try
            {
                var size = r.projectileSizeID.Value;
                info.Caliber = (size >> 16) & 0xFFFF;
                info.Length = size & 0xFFFF;
            }
            catch { }
            try { info.Capacity = r.capacity; } catch { }
            info.Center = CenterOf(r);
            list.Add(info);
        }
        list.Sort((a, b) => a.Vuid.CompareTo(b.Vuid));
        return list;
    }

    
    private static List<RackInfo> _cached;
    private static IntPtr _cachedVehicle;
    private static float _cachedAt = -100f;

    public static List<RackInfo> RacksCached(Vehicle v)
    {
        var ptr = v != null ? v.Pointer : IntPtr.Zero;
        if (_cached == null || ptr != _cachedVehicle || Time.unscaledTime - _cachedAt > 0.25f)
        {
            _cached = RacksOf(v);
            _cachedVehicle = ptr;
            _cachedAt = Time.unscaledTime;
        }
        return _cached;
    }

    
    private static Vector3 CenterOf(AmmoRack r)
    {
        try
        {
            var has = false;
            Vector3 min = default, max = default;
            foreach (var ren in r.transform.GetComponentsInChildren<Renderer>(true))
            {
                if (ren == null) continue;
                var b = ren.bounds;
                var c = b.center;
                var e = b.extents;
                if (e.sqrMagnitude < 1e-8f) continue;
                if (!has) { min = c - e; max = c + e; has = true; }
                else { min = Vector3.Min(min, c - e); max = Vector3.Max(max, c + e); }
            }
            if (has) return (min + max) * 0.5f;
        }
        catch { }
        try { return r.transform.position; }
        catch { return Vector3.zero; }
    }

    
    private const float AutoMargin = 0.15f, AutoDepth = 2.5f;

    public static bool Under(Module panel, Vector3 point)
    {
        var local = Quaternion.Inverse(panel.Rotation) * (point - panel.T.position);
        var half = panel.Size * 0.5f;
        return Mathf.Abs(local.x) <= half.x + AutoMargin && Mathf.Abs(local.y) <= half.y + AutoMargin &&
               local.z <= 0.05f && local.z >= -AutoDepth;
    }

    public static int[] AutoLinked(Module panel, List<RackInfo> racks)
    {
        var list = new List<int>();
        foreach (var r in racks) if (Under(panel, r.Center)) list.Add(r.Vuid);
        return list.ToArray();
    }

    
    public static int MirrorVuid(List<RackInfo> racks, int vuid)
    {
        foreach (var r in racks)
        {
            if (r.Vuid != vuid) continue;
            try
            {
                var mirror = r.Rack.VehicleObject.VehicleTransform?.Mirror;
                var vo = mirror != null ? mirror.VehicleObject : null;
                if (vo != null) return vo.VUID.Value;
            }
            catch { }
            break;
        }
        return vuid;
    }

    public static bool SameList(int[] a, int[] b)
    {
        if (a.Length != b.Length) return false;
        foreach (var x in a) if (Array.IndexOf(b, x) < 0) return false;
        return true;
    }

    
    public static float RoundLitres(int caliber, int length)
    {
        if (caliber <= 0) return 5f;
        var charge = Mathf.Max(caliber, length - 3 * caliber);
        var r = caliber / 200f;                         
        return Mathf.PI * r * r * (charge / 100f);
    }

    

    private static readonly Dictionary<IntPtr, float> LastTake = new();

    [HarmonyPatch(typeof(AmmoRackBehaviour), nameof(AmmoRackBehaviour.TakeShell))]
    private static class TakeShellPatch
    {
        private static void Postfix(AmmoRackBehaviour __instance, bool __result)
        {
            if (!__result) return;
            try { LastTake[__instance.Pointer] = Time.time; }
            catch { }
        }
    }

    

    [HarmonyPatch(typeof(FlameoutControlVehicleModule), nameof(FlameoutControlVehicleModule.OnFlameoutStart))]
    private static class FlameoutPatch
    {
        private static bool Prefix(Il2CppSystem.Object sender)
        {
            try { return !TryVent(sender); }
            catch (Exception e)
            {
                Plugin.Log.LogError("Blow-off panel failed: " + e);
                return true;
            }
        }
    }

    
    private static readonly Dictionary<IntPtr, (IntPtr Vehicle, int Vuid)> ByBehaviour = new();
    private static readonly Dictionary<(IntPtr Vehicle, int Vuid), AmmoRackBehaviour> ByVuid = new();
    private static readonly Dictionary<(IntPtr Vehicle, int Vuid), (int Caliber, int Length)> Sizes = new();

    private static void RebuildCache()
    {
        ByBehaviour.Clear();
        ByVuid.Clear();
        Sizes.Clear();
        foreach (var g in Interceptor.AllGroups)
        {
            if (g.Panels.Count == 0) continue;
            try
            {
                foreach (var r in g.V.GetComponentsInChildren<AmmoRack>(true))
                {
                    var beh = r != null ? r.behaviour : null;
                    if (beh == null) continue;
                    int vuid;
                    try { vuid = r.VehicleObject.VUID.Value; }
                    catch { continue; }
                    ByBehaviour[beh.Pointer] = (g.Ptr, vuid);
                    ByVuid[(g.Ptr, vuid)] = beh;
                    var size = beh.ShellSizeID.Value;
                    Sizes[(g.Ptr, vuid)] = ((size >> 16) & 0xFFFF, size & 0xFFFF);
                }
            }
            catch (Exception e)
            {
                if (Plugin.DebugLogging.Value) Plugin.Log.LogWarning("Blow-off rack lookup failed: " + e.Message);
            }
        }
    }

    private static Interceptor.Group GroupOf(IntPtr vehicle)
    {
        foreach (var g in Interceptor.AllGroups) if (g.Ptr == vehicle) return g;
        return null;
    }

    private static bool TryVent(Il2CppSystem.Object sender)
    {
        var beh = sender?.TryCast<AmmoRackBehaviour>();
        if (beh == null) return false;
        if (!ByBehaviour.TryGetValue(beh.Pointer, out var key))
        {
            RebuildCache();
            if (!ByBehaviour.TryGetValue(beh.Pointer, out key)) return false;
        }
        var g = GroupOf(key.Vehicle);
        if (g == null || g.Panels.Count == 0) return false;

        
        var panels = new List<Module>();
        foreach (var p in g.Panels)
            if (p.InBattle && p.S.Protects(key.Vuid)) panels.Add(p);
        if (panels.Count == 0) return false;
        var racks = new List<int>();
        foreach (var p in panels)
            foreach (var r in p.S.Racks)
                if (!racks.Contains(r)) racks.Add(r);

        
        var now = Time.time;
        var door = 0f;
        foreach (var p in panels) door = Mathf.Max(door, p.S.DoorTime);
        if (door > 0f)
            foreach (var r in racks)
            {
                if (!ByVuid.TryGetValue((g.Ptr, r), out var rb) || rb == null) continue;
                if (!LastTake.TryGetValue(rb.Pointer, out var at) || now - at > door || now < at) continue;
                Plugin.Log.LogInfo($"Blow-off panel: ammo cooked off while the blast door was open (loader took a round " +
                                   $"{now - at:0.0} s ago) — the fire goes into the crew compartment");
                if (EoHud.IsPlayer(g.V)) EoHud.Message("AMMO FIRE  —  BLAST DOOR WAS OPEN");
                return false;
            }

        Vent(g, panels, racks, now);
        return true;
    }

    

    private static void Vent(Interceptor.Group g, List<Module> panels, List<int> racks, float now)
    {
        
        var litres = 0f;
        var rounds = 0;
        foreach (var r in racks)
        {
            if (!ByVuid.TryGetValue((g.Ptr, r), out var rb) || rb == null) continue;
            try
            {
                int n = rb.amountStored;
                if (n <= 0) continue;
                rounds += n;
                var size = Sizes.TryGetValue((g.Ptr, r), out var s) ? s : (0, 0);
                litres += n * RoundLitres(size.Item1, size.Item2);
                rb.amountStored = 0;
                try { rb.StorageValueChanged?.Invoke(rb.Cast<Il2CppSystem.Object>(), Il2CppSystem.EventArgs.Empty); } catch { }
            }
            catch (Exception e)
            {
                if (Plugin.DebugLogging.Value) Plugin.Log.LogWarning("Blow-off rack empty failed: " + e.Message);
            }
        }
        
        if (rounds == 0)
        {
            if (Plugin.DebugLogging.Value) Plugin.Log.LogInfo("Blow-off panel: an empty rack was hit again — nothing left to burn");
            return;
        }
        litres = Mathf.Max(litres, 3f);

        
        var duration = Mathf.Clamp(3f + 0.1f * litres, 4f, 30f);
        var intensity = Mathf.Clamp(Mathf.Sqrt(litres / 60f), 0.4f, 1.8f) / Mathf.Sqrt(panels.Count);
        Vector3 inherit = default;
        try { if (g.V.Rigidbody != null) inherit = g.V.Rigidbody.velocity; } catch { }

        foreach (var p in panels)
        {
            var first = !p.Vented;
            p.Vented = true;
            p.VentedAt = now;
            if (first) Launch(p, inherit, intensity);
            p.Visual?.ApplyVented();
            Fx.Vent(p, intensity, duration);
            Fires.Add(new Fire { M = p, Start = now, Duration = duration });
        }
        Plugin.Log.LogInfo($"Blow-off panel: {rounds} round(s) in {racks.Count} rack(s) cooked off — vented through " +
                           $"{panels.Count} panel(s) ({litres:0} l of propellant, burning ~{duration:0} s); the crew is safe");
        if (EoHud.IsPlayer(g.V)) EoHud.Message($"AMMO FIRE VENTED  ({rounds} ROUNDS LOST)");
    }

    

    private sealed class Fire
    {
        public Module M;
        public float Start, Duration, NextFlare;
        public int JetLoop, BurnLoop;
        public bool JetDone, Done;
    }

    private static readonly List<Fire> Fires = new();

    

    private sealed class Flying
    {
        public Module M;
        public GameObject Go;
        public Material Mat;      
        public Vector3 Vel, AngVel;
        public float Start, Half;
        public bool Landed;
        public IntPtr VehicleRb;
    }

    private static void DestroyFlight(Flying f)
    {
        try { if (f.Go != null && !f.Go.WasCollected) Object.Destroy(f.Go); } catch { }
        try { if (f.Mat != null && !f.Mat.WasCollected) Object.Destroy(f.Mat); } catch { }
        f.Go = null;
        f.Mat = null;
    }

    private static readonly List<Flying> Flights = new();
    private static readonly MaterialPropertyBlock Mpb = new();

    private static void Launch(Module p, Vector3 inherit, float intensity)
    {
        try
        {
            var plate = p.Visual?.Plate;
            if (plate == null) return;
            
            if (p.Vo != null && ModernDayTags.HiddenInBattle(p.Vo.Pointer)) return;
            var go = Object.Instantiate(plate);
            go.name = "Blow-off panel";
            go.SetActive(true);
            var t = go.transform;
            t.SetParent(null, false);
            t.SetPositionAndRotation(plate.transform.position, plate.transform.rotation);
            t.localScale = p.Size;
            var src = plate.GetComponent<MeshRenderer>();
            var dst = go.GetComponent<MeshRenderer>();
            Material own = null;
            if (src != null && dst != null)
            {
                var shared = src.sharedMaterial;
                if (shared != null)
                {
                    own = new Material(shared) { name = "Blow-off panel paint" };
                    dst.sharedMaterial = own;
                }
                Mpb.Clear();
                src.GetPropertyBlock(Mpb);
                dst.SetPropertyBlock(Mpb);
                dst.shadowCastingMode = ShadowCastingMode.On;
            }
            var normal = p.Rotation * Vector3.forward;
            var k = Mathf.Lerp(0.8f, 1.25f, Mathf.InverseLerp(0.4f, 1.8f, intensity));
            var side = UnityEngine.Random.insideUnitSphere * 2.5f;
            var axis = p.Rotation * new Vector3(UnityEngine.Random.Range(-1f, 1f), UnityEngine.Random.Range(-1f, 1f), UnityEngine.Random.Range(-0.3f, 0.3f));
            IntPtr rb = IntPtr.Zero;
            try { rb = Modules.VehicleOf(p)?.Rigidbody?.Pointer ?? IntPtr.Zero; } catch { }
            Flights.Add(new Flying
            {
                M = p, Go = go, Mat = own, Start = Time.time, Half = p.Size.z * 0.5f, VehicleRb = rb,
                Vel = inherit + normal * UnityEngine.Random.Range(11f, 17f) * k + side,
                AngVel = axis.normalized * UnityEngine.Random.Range(5f, 13f)
            });
        }
        catch (Exception e) { Plugin.Log.LogWarning("Blow-off panel launch failed: " + e.Message); }
    }

    
    public static void Tick()
    {
        if (Flights.Count > 0) TickFlights();
        if (Fires.Count > 0) TickFires();
    }

    private static void TickFlights()
    {
        
        for (var i = Flights.Count - 1; i >= 0; i--)
        {
            var f = Flights[i];
            bool keep;
            try { keep = f.Go != null && !f.Go.WasCollected && f.Go && f.M.Alive && f.M.InBattle; }
            catch { keep = false; }
            if (keep) continue;
            DestroyFlight(f);
            Flights.RemoveAt(i);
        }
        var dt = Mathf.Min(Time.deltaTime, 0.05f);
        if (dt <= 0f) return;
        var now = Time.time;
        for (var i = Flights.Count - 1; i >= 0; i--)
        {
            var f = Flights[i];
            if (f.Landed) continue;
            try
            {
                var t = f.Go.transform;
                f.Vel += Physics.gravity * dt;
                f.Vel *= Mathf.Exp(-0.12f * dt);
                var step = f.Vel * dt;
                var pos = t.position;
                
                if (now - f.Start > 0.25f && step.sqrMagnitude > 1e-8f)
                {
                    RaycastHit? best = null;
                    foreach (var h in Physics.RaycastAll(pos, step.normalized, step.magnitude + f.Half))
                    {
                        if (h.collider == null || h.collider.isTrigger) continue;
                        if (h.rigidbody != null && h.rigidbody.Pointer == f.VehicleRb && now - f.Start < 1.5f) continue;
                        if (best == null || h.distance < best.Value.distance) best = h;
                    }
                    if (best != null)
                    {
                        var h = best.Value;
                        var n = t.rotation * Vector3.forward;
                        var up = Vector3.Dot(n, h.normal) >= 0f ? h.normal : -h.normal;
                        t.rotation = Quaternion.FromToRotation(n, up) * t.rotation;
                        t.position = h.point + h.normal * (f.Half + 0.005f);
                        f.Landed = true;
                        if (!SoundBridge.Play("vent.land", h.point, null, 0.9f, UnityEngine.Random.Range(0.9f, 1.05f)))
                            Assets.Play(Assets.BurstClip, h.point, 0.25f, 15f, 0.5f);
                        continue;
                    }
                }
                t.position = pos + step;
                t.rotation = Quaternion.AngleAxis(f.AngVel.magnitude * dt * Mathf.Rad2Deg, f.AngVel.normalized) * t.rotation;
                if (now - f.Start > 30f) f.Landed = true; 
            }
            catch { Flights.RemoveAt(i); }
        }
    }

    private static void TickFires()
    {
        var now = Time.time;
        for (var i = Fires.Count - 1; i >= 0; i--)
        {
            var f = Fires[i];
            var alive = f.M.Alive && f.M.InBattle;
            var age = now - f.Start;
            Transform at = null;
            try { if (alive) at = f.M.T; } catch { alive = false; }
            
            var jetEnd = f.Duration * 0.35f;
            if (!alive || age >= f.Duration)
            {
                SoundBridge.StopLoop(f.JetLoop, 1f);
                SoundBridge.StopLoop(f.BurnLoop, 3f);
                Fires.RemoveAt(i);
                continue;
            }
            if (!f.JetDone && f.JetLoop == 0 && age < jetEnd) f.JetLoop = SoundBridge.Loop("vent.jet", at, 1f);
            if (!f.JetDone && age >= jetEnd)
            {
                f.JetDone = true;
                SoundBridge.StopLoop(f.JetLoop, 2.5f);
                f.JetLoop = 0;
            }
            if (f.BurnLoop == 0 && age >= jetEnd - 1.5f) f.BurnLoop = SoundBridge.Loop("vent.burn", at, 0.8f);
            if (age > 1f && now >= f.NextFlare)
            {
                f.NextFlare = now + UnityEngine.Random.Range(1.2f, 3.5f);
                SoundBridge.Play("vent.flare", f.M.Center, at, 0.7f, UnityEngine.Random.Range(0.85f, 1.1f));
            }
        }
    }

    
    public static void Reset(Module m)
    {
        m.Vented = false;
        m.VentedAt = -100f;
        for (var i = Flights.Count - 1; i >= 0; i--)
        {
            if (Flights[i].M != m) continue;
            DestroyFlight(Flights[i]);
            Flights.RemoveAt(i);
        }
        for (var i = Fires.Count - 1; i >= 0; i--)
        {
            if (Fires[i].M != m) continue;
            SoundBridge.StopLoop(Fires[i].JetLoop, 0.3f);
            SoundBridge.StopLoop(Fires[i].BurnLoop, 0.3f);
            Fires.RemoveAt(i);
        }
        if (LastTake.Count > 256) LastTake.Clear();
        ByBehaviour.Clear();
        ByVuid.Clear();
        Sizes.Clear();
    }

    

    private static Texture2D _icon;

    public static Texture2D Icon()
    {
        if (_icon != null && !_icon.WasCollected) return _icon;
        const int n = 128;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "Blow-off panel icon", wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[n * n];
        for (var y = 0; y < n; y++)
        for (var x = 0; x < n; x++)
        {
            var p = new Vector2((x + 0.5f) / n, (y + 0.5f) / n);
            
            var box = Mathf.Max(Rect(p, 0.12f, 0.14f, 0.88f, 0.2f), Mathf.Max(Rect(p, 0.12f, 0.14f, 0.18f, 0.48f), Rect(p, 0.82f, 0.14f, 0.88f, 0.48f)));
            
            var rounds = 0f;
            for (var i = 0; i < 3; i++) rounds = Mathf.Max(rounds, Rect(p, 0.27f + i * 0.17f, 0.24f, 0.37f + i * 0.17f, 0.44f));
            
            var q = p - new Vector2(0.5f, 0.63f);
            var a = 12f * Mathf.Deg2Rad;
            var r = new Vector2(q.x * Mathf.Cos(a) + q.y * Mathf.Sin(a), -q.x * Mathf.Sin(a) + q.y * Mathf.Cos(a));
            var plate = Rect(r, -0.36f, -0.035f, 0.36f, 0.035f);
            
            var flame = 0f;
            var fy = (p.y - 0.5f) / 0.45f;
            if (fy > 0f && fy < 1f)
            {
                var w = 0.13f * (1f - fy) * (1f - fy) + 0.015f;
                flame = Mathf.Clamp01((w - Mathf.Abs(p.x - 0.5f - 0.03f * Mathf.Sin(fy * 9f))) * n);
                flame *= Mathf.Clamp01((0.75f - Mathf.Abs(r.y)) * 4f) * (Mathf.Abs(r.y) > 0.05f || Mathf.Abs(r.x) > 0.37f ? 1f : 0f);
            }
            var shape = Mathf.Max(Mathf.Max(box, rounds * 0.8f), Mathf.Max(plate, flame * 0.75f));
            var shade = (byte)(205 + 40 * Mathf.Clamp01(p.y));
            px[y * n + x] = new Color32(shade, shade, shade, (byte)Mathf.RoundToInt(Mathf.Clamp01(shape) * 255f));
        }
        tex.SetPixels32(px);
        tex.Apply(false, false);
        tex.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        _icon = tex;
        return tex;
    }

    private static float Rect(Vector2 p, float x0, float y0, float x1, float y1)
    {
        const int n = 128;
        var dx = Mathf.Min(p.x - x0, x1 - p.x);
        var dy = Mathf.Min(p.y - y0, y1 - p.y);
        return Mathf.Clamp01(0.5f + Mathf.Min(dx, dy) * n);
    }
}
