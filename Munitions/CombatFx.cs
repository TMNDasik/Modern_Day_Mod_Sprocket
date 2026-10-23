using System;
using UnityEngine;

namespace Munitions;







internal static class CombatFx
{
    private static bool _searched;
    private static float _retry;
    private static Func<bool> _hits, _flashes;
    private static Func<Vector3, Vector3, float, int, int, bool> _launch;
    private static Func<Vector3, Vector3, int, int, bool, bool, bool> _detonation;
    private static Func<Vector3, Vector3, bool> _smokeLaunch;
    private static Func<Vector3, float, float, float, bool> _smokeCloud;
    private static Func<Vector3, Vector3, float, bool> _engineSmoke;
    private static Func<Transform, Vector3, Vector3, float, float, float, bool> _ventFire;

    private static void Find()
    {
        if (_searched && (_hits != null || Time.unscaledTime < _retry)) return;
        _searched = true;
        _retry = Time.unscaledTime + 10f;
        try
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (a.GetName().Name is not ("CombatEffects" or "MDCombatEffects")) continue;
                var api = a.GetType("CombatEffects.Api");
                if (api == null) break;
                var hits = api.GetProperty("HitsActive")?.GetGetMethod();
                var flashes = api.GetProperty("FlashesActive")?.GetGetMethod();
                var launch = api.GetMethod("Launch", new[] { typeof(Vector3), typeof(Vector3), typeof(float), typeof(int), typeof(int) });
                var det = api.GetMethod("Detonation", new[] { typeof(Vector3), typeof(Vector3), typeof(int), typeof(int), typeof(bool), typeof(bool) });
                if (hits != null) _hits = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), hits);
                if (flashes != null) _flashes = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), flashes);
                if (launch != null) _launch = (Func<Vector3, Vector3, float, int, int, bool>)Delegate.CreateDelegate(typeof(Func<Vector3, Vector3, float, int, int, bool>), launch);
                if (det != null) _detonation = (Func<Vector3, Vector3, int, int, bool, bool, bool>)Delegate.CreateDelegate(typeof(Func<Vector3, Vector3, int, int, bool, bool, bool>), det);
                var sl = api.GetMethod("SmokeLaunch", new[] { typeof(Vector3), typeof(Vector3) });
                var sc = api.GetMethod("SmokeCloud", new[] { typeof(Vector3), typeof(float), typeof(float), typeof(float) });
                var es = api.GetMethod("EngineSmoke", new[] { typeof(Vector3), typeof(Vector3), typeof(float) });
                if (sl != null) _smokeLaunch = (Func<Vector3, Vector3, bool>)Delegate.CreateDelegate(typeof(Func<Vector3, Vector3, bool>), sl);
                if (sc != null) _smokeCloud = (Func<Vector3, float, float, float, bool>)Delegate.CreateDelegate(typeof(Func<Vector3, float, float, float, bool>), sc);
                if (es != null) _engineSmoke = (Func<Vector3, Vector3, float, bool>)Delegate.CreateDelegate(typeof(Func<Vector3, Vector3, float, bool>), es);
                var vf = api.GetMethod("VentFire", new[] { typeof(Transform), typeof(Vector3), typeof(Vector3), typeof(float), typeof(float), typeof(float) });
                if (vf != null) _ventFire = (Func<Transform, Vector3, Vector3, float, float, float, bool>)Delegate.CreateDelegate(typeof(Func<Transform, Vector3, Vector3, float, float, float, bool>), vf);
                Plugin.Log.LogInfo("Combat Effects found: its hit and launch effects replace this mod's where both exist");
                break;
            }
        }
        catch (Exception e) { Plugin.Log.LogWarning("Combat Effects link failed: " + e.Message); }
    }

    
    public static bool HitsActive
    {
        get
        {
            Find();
            try { return _hits != null && _hits(); }
            catch { return false; }
        }
    }

    
    public static bool FlashesActive
    {
        get
        {
            Find();
            try { return _flashes != null && _flashes(); }
            catch { return false; }
        }
    }

    
    public static bool Launch(Vector3 muzzle, Vector3 dir, float tubeLength, int caliber, int externalKind)
    {
        Find();
        try { return _launch != null && _launch(muzzle, dir, tubeLength, caliber, externalKind); }
        catch { return false; }
    }

    
    public static bool SmokeLaunch(Vector3 muzzle, Vector3 velocity)
    {
        Find();
        try { return _smokeLaunch != null && _smokeLaunch(muzzle, velocity); }
        catch { return false; }
    }

    
    public static bool SmokeCloud(Vector3 center, float radius, float buildTime, float duration)
    {
        Find();
        try { return _smokeCloud != null && _smokeCloud(center, radius, buildTime, duration); }
        catch { return false; }
    }

    
    public static bool EngineSmoke(Vector3 pos, Vector3 dir, float intensity)
    {
        Find();
        try { return _engineSmoke != null && _engineSmoke(pos, dir, intensity); }
        catch { return false; }
    }

    
    public static bool VentFire(Transform anchor, Vector3 localPos, Vector3 localUp, float size, float intensity, float duration)
    {
        Find();
        try { return _ventFire != null && _ventFire(anchor, localPos, localUp, size, intensity, duration); }
        catch { return false; }
    }

    
    public static bool Detonation(Vector3 pos, Vector3 normal, int caliber, int externalKind, bool ground, bool automatic)
    {
        Find();
        try { return _detonation != null && _detonation(pos, normal, caliber, externalKind, ground, automatic); }
        catch { return false; }
    }
}
