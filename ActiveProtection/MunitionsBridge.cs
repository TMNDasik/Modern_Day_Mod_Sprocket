using System;
using System.Reflection;
using UnityEngine;

namespace ActiveProtection;



internal static class MunitionsBridge
{
    private static bool _searched;
    private static Func<int, int> _kind;
    private static Func<int, int> _caliber;
    private static Action<Vector3, Vector3, Vector3, bool, bool> _effect;
    private static Action<Vector3, Vector3, float> _smokeLaunch;
    private static Action<Vector3, float, float, float> _smokeCloud;
    private static Func<int, float, bool> _scaleImpact;
    private static Func<Vector3> _smokeDrift;
    private static Action<Vector3, Vector3, float> _eraEffect;
    private static Action<Transform, Vector3, Vector3, float, float, float> _ventFire;

    private static void Find()
    {
        if (_searched) return;
        _searched = true;
        try
        {
            Type api = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.GetName().Name is not ("Munitions" or "MDMunitions")) continue;
                api = asm.GetType("Munitions.Api");
                if (api != null) break;
            }
            if (api == null) return;
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
            var kind = api.GetMethod("ProjectileKind", flags, null, new[] { typeof(int) }, null);
            var cal = api.GetMethod("ProjectileCaliber", flags, null, new[] { typeof(int) }, null);
            var fx = api.GetMethod("ApsEffect", flags, null,
                new[] { typeof(Vector3), typeof(Vector3), typeof(Vector3), typeof(bool), typeof(bool) }, null);
            if (kind != null) _kind = (Func<int, int>)Delegate.CreateDelegate(typeof(Func<int, int>), kind);
            if (cal != null) _caliber = (Func<int, int>)Delegate.CreateDelegate(typeof(Func<int, int>), cal);
            if (fx != null)
                _effect = (Action<Vector3, Vector3, Vector3, bool, bool>)Delegate.CreateDelegate(
                    typeof(Action<Vector3, Vector3, Vector3, bool, bool>), fx);
            var launch = api.GetMethod("SmokeLaunch", flags, null, new[] { typeof(Vector3), typeof(Vector3), typeof(float) }, null);
            var cloud = api.GetMethod("SmokeCloud", flags, null, new[] { typeof(Vector3), typeof(float), typeof(float), typeof(float) }, null);
            if (launch != null) _smokeLaunch = (Action<Vector3, Vector3, float>)Delegate.CreateDelegate(typeof(Action<Vector3, Vector3, float>), launch);
            if (cloud != null) _smokeCloud = (Action<Vector3, float, float, float>)Delegate.CreateDelegate(typeof(Action<Vector3, float, float, float>), cloud);
            var scale = api.GetMethod("ScaleImpact", flags, null, new[] { typeof(int), typeof(float) }, null);
            var era = api.GetMethod("EraEffect", flags, null, new[] { typeof(Vector3), typeof(Vector3), typeof(float) }, null);
            if (scale != null) _scaleImpact = (Func<int, float, bool>)Delegate.CreateDelegate(typeof(Func<int, float, bool>), scale);
            if (era != null) _eraEffect = (Action<Vector3, Vector3, float>)Delegate.CreateDelegate(typeof(Action<Vector3, Vector3, float>), era);
            var drift = api.GetMethod("SmokeDrift", flags, null, Type.EmptyTypes, null);
            if (drift != null) _smokeDrift = (Func<Vector3>)Delegate.CreateDelegate(typeof(Func<Vector3>), drift);
            var vent = api.GetMethod("VentFire", flags, null,
                new[] { typeof(Transform), typeof(Vector3), typeof(Vector3), typeof(float), typeof(float), typeof(float) }, null);
            if (vent != null)
                _ventFire = (Action<Transform, Vector3, Vector3, float, float, float>)Delegate.CreateDelegate(
                    typeof(Action<Transform, Vector3, Vector3, float, float, float>), vent);
            Plugin.Log.LogInfo($"Munitions found: projectile kinds {(_kind != null ? "yes" : "no")}, APS effects {(_effect != null ? "yes" : "no")}");
        }
        catch (Exception e) { Plugin.Log.LogWarning("Munitions lookup failed: " + e.Message); }
    }

    
    public static int Kind(int typeId)
    {
        Find();
        try { return _kind != null ? _kind(typeId) : -1; }
        catch { return -1; }
    }

    public static int Caliber(int typeId)
    {
        Find();
        try { return _caliber != null ? _caliber(typeId) : 0; }
        catch { return 0; }
    }

    public static void SmokeLaunch(Vector3 muzzle, Vector3 velocity, float flightTime)
    {
        Find();
        try { _smokeLaunch?.Invoke(muzzle, velocity, flightTime); }
        catch { }
    }

    public static void SmokeCloud(Vector3 center, float radius, float build, float duration)
    {
        Find();
        if (_smokeCloud == null && !_warnedNoSmoke)
        {
            _warnedNoSmoke = true;
            Plugin.Log.LogWarning("Munitions mod not found — aerosol screens work but are invisible");
        }
        try { _smokeCloud?.Invoke(center, radius, build, duration); }
        catch { }
    }

    private static bool _warnedNoSmoke;

    
    public static Vector3 SmokeDrift()
    {
        Find();
        try { return _smokeDrift != null ? _smokeDrift() : Vector3.zero; }
        catch { return Vector3.zero; }
    }

    
    
    public static bool ScaleImpact(int projectileId, float speedFactor)
    {
        Find();
        try { return _scaleImpact != null && _scaleImpact(projectileId, speedFactor); }
        catch { return false; }
    }

    
    public static bool EraEffect(Vector3 point, Vector3 normal, float size)
    {
        Find();
        if (_eraEffect == null) return false;
        try
        {
            _eraEffect(point, normal, size);
            return true;
        }
        catch { return false; }
    }

    
    public static bool VentFire(Transform anchor, Vector3 localPos, Vector3 localUp, float size, float intensity, float duration)
    {
        Find();
        if (_ventFire == null) return false;
        try
        {
            _ventFire(anchor, localPos, localUp, size, intensity, duration);
            return true;
        }
        catch { return false; }
    }

    public static bool Effect(Vector3 launcher, Vector3 point, Vector3 threatDir, bool kinetic, bool success)
    {
        Find();
        if (_effect == null) return false;
        try
        {
            _effect(launcher, point, threatDir, kinetic, success);
            return true;
        }
        catch { return false; }
    }
}
