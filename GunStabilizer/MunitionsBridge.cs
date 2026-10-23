using System;
using UnityEngine;

namespace GunStabilizer;



internal static class MunitionsBridge
{
    private static Func<IntPtr, bool> _gunLocked;
    private static bool _searched;

    public static bool GunLocked(IntPtr weapon)
    {
        if (_gunLocked == null)
        {
            if (_searched) return false;
            _searched = true;
            try
            {
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (a.GetName().Name is not ("Munitions" or "MDMunitions")) continue;
                    var m = a.GetType("Munitions.Api")?.GetMethod("GunLocked", new[] { typeof(IntPtr) });
                    if (m != null) _gunLocked = (Func<IntPtr, bool>)Delegate.CreateDelegate(typeof(Func<IntPtr, bool>), m);
                    break;
                }
            }
            catch (Exception e) { if (Plugin.DebugLogging.Value) Plugin.Log.LogWarning("Munitions link failed: " + e.Message); }
            if (_gunLocked == null) return false;
            Plugin.Log.LogInfo("Linked to Munitions: the sight stays on target while the autoloader loads");
        }
        try { return _gunLocked(weapon); }
        catch { return false; }
    }

    
    
    private static Func<IntPtr, int, float, float> _launchSpeed;
    private static bool _launchSearched;

    public static float LaunchSpeed(IntPtr weapon, int typeId, float natural)
    {
        if (_launchSpeed == null)
        {
            if (_launchSearched) return natural;
            _launchSearched = true;
            try
            {
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (a.GetName().Name is not ("Munitions" or "MDMunitions")) continue;
                    var m = a.GetType("Munitions.Api")?.GetMethod("LaunchSpeed", new[] { typeof(IntPtr), typeof(int), typeof(float) });
                    if (m != null) _launchSpeed = (Func<IntPtr, int, float, float>)Delegate.CreateDelegate(typeof(Func<IntPtr, int, float, float>), m);
                    break;
                }
            }
            catch { }
            if (_launchSpeed == null) return natural;
        }
        try { return _launchSpeed(weapon, typeId, natural); }
        catch { return natural; }
    }

    
    private static Func<int, int> _kind;
    private static bool _kindSearched;

    public static int ProjectileKind(int typeId)
    {
        if (_kind == null)
        {
            if (_kindSearched) return -1;
            _kindSearched = true;
            try
            {
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (a.GetName().Name is not ("Munitions" or "MDMunitions")) continue;
                    var m = a.GetType("Munitions.Api")?.GetMethod("ProjectileKind", new[] { typeof(int) });
                    if (m != null) _kind = (Func<int, int>)Delegate.CreateDelegate(typeof(Func<int, int>), m);
                    break;
                }
            }
            catch { }
            if (_kind == null) return -1;
        }
        try { return _kind(typeId); }
        catch { return -1; }
    }
}
