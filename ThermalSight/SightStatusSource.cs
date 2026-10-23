using System;
using System.Collections.Generic;
using Sprocket.DamageModelling;
using Sprocket.Vehicles.Cannons;
using Sprocket.Vehicles.Weapons.Cannons;
using Sprocket.WeaponFramework;
using UnityEngine;

namespace ThermalSight;


internal static class SightStatusSource
{
    

    public static SightStatus Build(Sprocket.Gameplay.VehicleControl.VehicleController vc, int range)
    {
        try
        {
            var gunner = vc != null ? vc.activeGunner : null;
            var weaponGo = gunner != null ? gunner.WeaponGameObject : null;
            Cannon cannon = null;
            if (weaponGo != null)
            {
                cannon = weaponGo.GetComponent<Cannon>();
                if (cannon == null) cannon = weaponGo.GetComponentInParent<Cannon>();
            }
            var beh = cannon != null ? cannon.Behaviour : null;
            if (beh == null) return new SightStatus(false, null, range);
            var ready = beh.LoadState == LoadState.Loaded;
            return new SightStatus(ready, AmmoKey(beh), range);
        }
        catch { return new SightStatus(false, null, range); }
    }

    
    private static string AmmoKey(CannonBehaviour beh)
    {
        if (beh.caliber < 20) return "p";
        switch (MunitionsBridge.DisplayKind(beh.Pointer))
        {
            case 0: return "b";
            case 1: return "o";
            case 2: return "u";
            case 3: return "u";
            case 4: return "k";
        }
        return KeyFromTypeName(beh._LoadedProjectileTypeID_k__BackingField.Value);
    }

    private static readonly Dictionary<int, string> NameCache = new();

    
    private static string KeyFromTypeName(int typeId)
    {
        if (NameCache.TryGetValue(typeId, out var cached)) return cached;
        var key = "b";
        try
        {
            var reg = IProjectileTypeRegister.Instance;
            if (reg != null && reg.TryGet(new ProjectileTypeID(typeId), out var type) && type != null)
            {
                var name = (type.Name ?? "").ToUpperInvariant();
                var i = name.IndexOf("MM ", StringComparison.Ordinal);
                var tag = (i >= 0 ? name.Substring(i + 3) : name).Trim();
                if (tag.Contains("ATGM") || tag.Contains("MISSILE")) key = "u";
                else if (tag.Contains("HEAT")) key = "k";
                else if (tag == "HE" || tag.StartsWith("HE ", StringComparison.Ordinal) || tag.StartsWith("HE-", StringComparison.Ordinal)) key = "o";
                NameCache[typeId] = key;
            }
        }
        catch { }
        return key;
    }

    
    
    
    
    
    

    private static float _laserReadyAt;
    private static readonly System.Random Noise = new();

    public static int Lase(Camera cam, GameObject ownVehicle, out string note)
    {
        note = null;
        if (cam == null) return 0;
        var spec = FcsBridge.Rangefinder(ownVehicle);
        if (spec != null && spec[0] < 0.5f) return Refuse("no laser rangefinder on this vehicle", "NO LASER", out note);
        var now = Time.unscaledTime;
        
        if (spec != null && now < _laserReadyAt - 0.15f)
        {
            var left = Mathf.Max(0.1f, _laserReadyAt - now);
            return Refuse($"the laser recharges ({left:0.0} s)", $"LASER RECHARGING {left:0.0} s", out note);
        }

        var t = cam.transform;
        var max = spec != null ? Mathf.Clamp(spec[2], 100f, 20000f) : Mathf.Clamp(Plugin.RangefinderMax.Value, 100f, 20000f);
        var ownRoot = ownVehicle != null ? ownVehicle.transform : null;
        var ownBody = ownVehicle != null ? ownVehicle.GetComponent<Rigidbody>() : null;
        if (ownVehicle != null && ownBody == null) ownBody = ownVehicle.GetComponentInChildren<Rigidbody>();

        var best = FirstHit(t.position, t.forward, max, ownRoot, ownBody);
        if (spec != null)
        {
            
            const float a = 0.00025f;
            var last = spec[5] > 0.5f;
            foreach (var o in new[] { new Vector2(a, 0f), new Vector2(-a, 0f), new Vector2(0f, a), new Vector2(0f, -a) })
            {
                var d = FirstHit(t.position, (t.rotation * new Vector3(o.x, o.y, 1f)).normalized, max, ownRoot, ownBody);
                if (d == float.MaxValue) continue;
                if (best == float.MaxValue || (last ? d > best : d < best)) best = d;
            }
            _laserReadyAt = now + Mathf.Max(0f, spec[4]);
        }
        
        var smoke = ApsBridge.SmokeDistance(t.position, t.forward, best < float.MaxValue ? best : max);
        if (smoke > 0f && smoke < best) best = smoke;
        Api.LastRangeNote = "";
        if (spec != null && best < float.MaxValue)
        {
            if (best < spec[1])
            {
                note = $"closer than the minimum range ({spec[1]:0} m)";
                Api.LastRangeNote = $"MIN {spec[1]:0} m";
                best = float.MaxValue;
            }
            else
            {
                
                var u1 = 1.0 - Noise.NextDouble();
                var u2 = Noise.NextDouble();
                var g = (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
                best = Mathf.Round((best + g * spec[3]) / 5f) * 5f;
            }
        }
        var range = best < float.MaxValue ? Mathf.Clamp(Mathf.RoundToInt(best), 0, 9999) : 0;
        Api.RecordLaser(t.position, t.forward, range);
        return range;
    }

    private static int Refuse(string log, string shown, out string note)
    {
        note = log;
        Api.LaserRefusedNote = shown;
        Api.LaserRefusedCount++;
        return -1;
    }

    
    private static float FirstHit(Vector3 origin, Vector3 dir, float max, Transform ownRoot, Rigidbody ownBody)
    {
        var hits = Physics.RaycastAll(origin, dir, max, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        if (hits == null) return float.MaxValue;
        var best = float.MaxValue;
        for (var i = 0; i < hits.Length; i++)
        {
            var h = hits[i];
            if (h.distance < 1.5f || h.distance >= best) continue;
            var col = h.collider;
            if (col == null) continue;
            if (ownRoot != null && col.transform.IsChildOf(ownRoot)) continue;
            var rb = col.attachedRigidbody;
            if (rb != null && ownBody != null && rb.Pointer == ownBody.Pointer) continue;
            best = h.distance;
        }
        return best;
    }
}


internal static class FcsBridge
{
    private static Func<GameObject, float[]> _spec;
    private static float _nextTry;

    public static float[] Rangefinder(GameObject vehicle)
    {
        if (_spec == null)
        {
            if (Time.unscaledTime < _nextTry) return null;
            _nextTry = Time.unscaledTime + 10f;
            try
            {
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (a.GetName().Name is not ("GunStabilizer" or "MDGunStabilizer")) continue;
                    var m = a.GetType("GunStabilizer.Api")?.GetMethod("Rangefinder", new[] { typeof(GameObject) });
                    if (m != null) _spec = (Func<GameObject, float[]>)Delegate.CreateDelegate(typeof(Func<GameObject, float[]>), m);
                    break;
                }
            }
            catch { }
            if (_spec == null) return null;
        }
        try
        {
            var s = _spec(vehicle);
            return s != null && s.Length >= 6 ? s : null;
        }
        catch { return null; }
    }
}


internal static class MunitionsBridge
{
    private static Func<IntPtr, int> _displayKind;
    private static float _nextTry;

    public static int DisplayKind(IntPtr weapon)
    {
        if (_displayKind == null)
        {
            if (Time.unscaledTime < _nextTry) return -1;
            _nextTry = Time.unscaledTime + 10f;
            try
            {
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (a.GetName().Name is not ("Munitions" or "MDMunitions")) continue;
                    var m = a.GetType("Munitions.Api")?.GetMethod("DisplayKind", new[] { typeof(IntPtr) });
                    if (m != null) _displayKind = (Func<IntPtr, int>)Delegate.CreateDelegate(typeof(Func<IntPtr, int>), m);
                    break;
                }
            }
            catch (Exception e)
            {
                if (Plugin.DebugLogging.Value) Plugin.Log.LogWarning("Munitions link failed: " + e.Message);
            }
            if (_displayKind == null) return -1;
            Plugin.Log.LogInfo("Linked to Munitions: sight shows the selected ammo type");
        }
        try { return _displayKind(weapon); }
        catch { return -1; }
    }
}
