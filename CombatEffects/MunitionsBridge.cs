using System;
using UnityEngine;

namespace CombatEffects;



internal static class MunitionsBridge
{
    private static bool _searched;
    private static float _retry;
    private static Func<Vector3, float, int> _kindNear;
    private static Func<Transform, int> _autoType;
    private static Func<Vector3> _drift;

    private static void Find()
    {
        if (_searched && (_kindNear != null || Time.unscaledTime < _retry)) return;
        _searched = true;
        _retry = Time.unscaledTime + 10f;
        try
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (a.GetName().Name is not ("Munitions" or "MDMunitions")) continue;
                var api = a.GetType("Munitions.Api");
                var near = api?.GetMethod("DetonationKindNear", new[] { typeof(Vector3), typeof(float) });
                var auto = api?.GetMethod("AutoWeaponType", new[] { typeof(Transform) });
                if (near != null) _kindNear = (Func<Vector3, float, int>)Delegate.CreateDelegate(typeof(Func<Vector3, float, int>), near);
                if (auto != null) _autoType = (Func<Transform, int>)Delegate.CreateDelegate(typeof(Func<Transform, int>), auto);
                var drift = api?.GetMethod("SmokeDrift", Type.EmptyTypes);
                if (drift != null) _drift = (Func<Vector3>)Delegate.CreateDelegate(typeof(Func<Vector3>), drift);
                break;
            }
        }
        catch { }
    }

    
    
    public static int KindNear(Vector3 pos, float radius, out int caliber, out bool automatic)
    {
        caliber = 0;
        automatic = false;
        Find();
        if (_kindNear == null) return -1;
        try
        {
            var code = _kindNear(pos, radius);
            if (code < 0) return -1;
            caliber = code & 0xFFFF;
            automatic = (code >> 24 & 1) != 0;
            return code >> 16 & 0xFF;
        }
        catch { return -1; }
    }

    
    public static Vector3? Wind()
    {
        Find();
        if (_drift == null) return null;
        try { return _drift(); }
        catch { return null; }
    }

    
    public static int AutoType(Transform t)
    {
        Find();
        if (_autoType == null || t == null) return 0;
        try { return _autoType(t); }
        catch { return 0; }
    }
}
