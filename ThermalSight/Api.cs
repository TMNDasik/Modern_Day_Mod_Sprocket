using System;
using System.Reflection;
using UnityEngine;

namespace ThermalSight;


public static class Api
{
    
    public static int LaserCount;
    public static Vector3 LaserOrigin, LaserDirection;
    public static float LaserRange;

    
    
    public static float LastRange;
    public static float LastRangeTime = -1f;
    public static Vector3 LastRangePoint;
    
    public static string LastRangeNote = "";
    
    public static int LaserRefusedCount;
    public static string LaserRefusedNote = "";

    
    
    public static bool SightToScreen(Vector2 cameraViewport, out Vector2 screenViewport)
    {
        screenViewport = cameraViewport;
        try
        {
            var view = ThermalController.ActiveView;
            return view != null && view.CameraToScreen(cameraViewport, out screenViewport);
        }
        catch { return false; }
    }

    
    public static void AddObscurant(Vector3 center, float radius, float buildTime, float duration)
    {
        try { ThermalEffects.Instance?.AddObscurant(center, radius, buildTime, duration); }
        catch { }
    }

    internal static void RecordLaser(Vector3 origin, Vector3 direction, float range)
    {
        LaserOrigin = origin;
        LaserDirection = direction;
        LaserRange = range;
        LastRange = range;
        LastRangeTime = Time.time;
        LastRangePoint = origin + direction.normalized * range;
        LaserCount++;
    }
}


internal static class ApsBridge
{
    private static bool _searched;
    private static Func<Vector3, Vector3, float, float> _smoke;

    
    public static float SmokeDistance(Vector3 origin, Vector3 direction, float max)
    {
        if (!_searched)
        {
            _searched = true;
            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.GetName().Name is not ("ActiveProtection" or "MDActiveProtection")) continue;
                    var api = asm.GetType("ActiveProtection.Api");
                    var m = api?.GetMethod("SmokeDistance", BindingFlags.Public | BindingFlags.Static, null,
                        new[] { typeof(Vector3), typeof(Vector3), typeof(float) }, null);
                    if (m != null) _smoke = (Func<Vector3, Vector3, float, float>)Delegate.CreateDelegate(typeof(Func<Vector3, Vector3, float, float>), m);
                }
            }
            catch { }
        }
        try { return _smoke != null ? _smoke(origin, direction, max) : -1f; }
        catch { return -1f; }
    }
}
