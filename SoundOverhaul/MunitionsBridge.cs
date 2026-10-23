using System;
using UnityEngine;

namespace SoundOverhaul;


internal static class MunitionsBridge
{
    public const int None = 0, MachineGun = 1, Autocannon = 2;

    private static Func<Transform, int> _type;
    private static bool _missing;
    private static float _nextTry;

    public static int AutoType(Transform t)
    {
        if (t == null) return None;
        if (_type == null)
        {
            if (_missing && Time.unscaledTime < _nextTry) return None;
            _nextTry = Time.unscaledTime + 5f;
            _missing = true;
            try
            {
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (a.GetName().Name is not ("Munitions" or "MDMunitions")) continue;
                    var m = a.GetType("Munitions.Api")?.GetMethod("AutoWeaponType", new[] { typeof(Transform) });
                    if (m != null) _type = (Func<Transform, int>)Delegate.CreateDelegate(typeof(Func<Transform, int>), m);
                    break;
                }
            }
            catch { }
            if (_type == null) return None;
            _missing = false;
        }
        try { return _type(t); }
        catch { return None; }
    }

    
    
    public const int KindHe = 1, KindGunAtgm = 2, KindTubeAtgm = 3, KindHeat = 4, KindRocketHe = 5, KindRocketTb = 6;

    private static Func<Vector3, float, int> _near;
    private static Func<float, Vector4> _unclaimed;
    private static bool _detSearched;
    private static float _detRetry;

    private static void FindDetonations()
    {
        if (_detSearched && (_near != null || Time.unscaledTime < _detRetry)) return;
        _detSearched = true;
        _detRetry = Time.unscaledTime + 10f;
        try
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (a.GetName().Name is not ("Munitions" or "MDMunitions")) continue;
                var api = a.GetType("Munitions.Api");
                var near = api?.GetMethod("DetonationNear", new[] { typeof(Vector3), typeof(float) });
                var un = api?.GetMethod("UnclaimedDetonation", new[] { typeof(float) });
                if (near != null) _near = (Func<Vector3, float, int>)Delegate.CreateDelegate(typeof(Func<Vector3, float, int>), near);
                if (un != null) _unclaimed = (Func<float, Vector4>)Delegate.CreateDelegate(typeof(Func<float, Vector4>), un);
                break;
            }
        }
        catch { }
    }

    
    public static bool DetonationNear(Vector3 pos, float radius, out int kind, out int caliber)
    {
        kind = -1;
        caliber = 0;
        FindDetonations();
        if (_near == null) return false;
        int code;
        try { code = _near(pos, radius); }
        catch { return false; }
        if (code < 0) return false;
        kind = code >> 16;
        caliber = code & 0xFFFF;
        return true;
    }

    
    public static bool Unclaimed(float minAge, out Vector3 pos, out int kind, out int caliber)
    {
        pos = default;
        kind = -1;
        caliber = 0;
        FindDetonations();
        if (_unclaimed == null) return false;
        Vector4 v;
        try { v = _unclaimed(minAge); }
        catch { return false; }
        if (v.w < 0f) return false;
        var code = (int)v.w;
        pos = new Vector3(v.x, v.y, v.z);
        kind = code >> 16;
        caliber = code & 0xFFFF;
        return true;
    }
}
