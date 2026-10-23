using System;
using UnityEngine;

namespace CombatEffects;


public static class Api
{
    
    public static bool HitsActive => Plugin.Enabled != null && Plugin.On(Plugin.Impacts);

    
    public static bool FlashesActive => Plugin.Enabled != null && Plugin.On(Plugin.MuzzleFlashes);

    
    public static bool Play(string effect, Vector3 position, Quaternion rotation, Transform parent)
    {
        try { return Plugin.Enabled != null && Plugin.Enabled.Value && Effects.Play(effect, position, rotation, parent) != null; }
        catch { return false; }
    }

    
    
    
    public static bool Launch(Vector3 muzzle, Vector3 dir, float tubeLength, int caliber, int kind)
    {
        try { return FlashesActive && Launches.Play(muzzle, dir, tubeLength, caliber, kind); }
        catch { return false; }
    }

    
    public static bool SmokeActive => Plugin.Enabled != null && Plugin.On(Plugin.Fires);

    
    public static bool SmokeLaunch(Vector3 muzzle, Vector3 velocity)
    {
        try { return SmokeActive && Smoke.GrenadeLaunch(muzzle, velocity); }
        catch { return false; }
    }

    
    public static bool SmokeCloud(Vector3 center, float radius, float buildTime, float duration)
    {
        try { return SmokeActive && Smoke.GrenadeCloud(center, buildTime, duration); }
        catch { return false; }
    }

    
    public static bool EngineSmoke(Vector3 position, Vector3 direction, float intensity)
    {
        try { return SmokeActive && Smoke.EngineSmoke(position, direction, intensity); }
        catch { return false; }
    }

    
    
    public static bool VentFire(Transform anchor, Vector3 localPos, Vector3 localUp, float size, float intensity, float duration)
    {
        try { return Plugin.Enabled != null && Plugin.On(Plugin.Fires) && FireHooks.Vent(anchor, localPos, localUp, intensity, duration); }
        catch { return false; }
    }

    
    
    public static bool Detonation(Vector3 position, Vector3 normal, int caliber, int kind, bool ground, bool automatic)
    {
        try { return HitsActive && ImpactHooks.Detonation(position, normal, caliber, kind, ground, automatic); }
        catch { return false; }
    }
}
