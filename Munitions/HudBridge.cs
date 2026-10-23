using System;
using System.Reflection;
using UnityEngine;

namespace Munitions;



internal static class HudBridge
{
    private static bool _searched;
    private static MethodInfo _smoke, _smokeKey, _tda, _tdaKey, _fire, _fireKey;
    private static Func<float[]> _apsSegments, _apsFrames;
    private static Func<int> _stabState;
    private static Action _stabTakeOver;
    private static float _nextSearch;

    private static void Find()
    {
        if (_searched && Time.unscaledTime < _nextSearch) return;
        if (_smoke != null && _tda != null && _fire != null && _apsSegments != null && _stabState != null) return;
        _searched = true;
        _nextSearch = Time.unscaledTime + 10f;
        const BindingFlags f = BindingFlags.Public | BindingFlags.Static;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var name = asm.GetName().Name;
                if (name.StartsWith("MD", StringComparison.Ordinal)) name = name.Substring(2);   
                if (name == "ActiveProtection")
                {
                    var api = asm.GetType("ActiveProtection.Api");
                    _smoke ??= api?.GetMethod("SmokeStatus", f, null, Type.EmptyTypes, null);
                    _smokeKey ??= api?.GetMethod("SmokeKeyName", f, null, Type.EmptyTypes, null);
                    var seg = api?.GetMethod("ApsSegments", f, null, Type.EmptyTypes, null);
                    if (_apsSegments == null && seg != null) _apsSegments = (Func<float[]>)Delegate.CreateDelegate(typeof(Func<float[]>), seg);
                    var frames = api?.GetMethod("ApsSegmentFrames", f, null, Type.EmptyTypes, null);   
                    if (_apsFrames == null && frames != null) _apsFrames = (Func<float[]>)Delegate.CreateDelegate(typeof(Func<float[]>), frames);
                }
                else if (name == "GunStabilizer")
                {
                    var api = asm.GetType("GunStabilizer.Api");
                    var st = api?.GetMethod("StabState", f, null, Type.EmptyTypes, null);
                    var take = api?.GetMethod("TakeOverIndicator", f, null, Type.EmptyTypes, null);
                    if (_stabState == null && st != null) _stabState = (Func<int>)Delegate.CreateDelegate(typeof(Func<int>), st);
                    if (_stabTakeOver == null && take != null) _stabTakeOver = (Action)Delegate.CreateDelegate(typeof(Action), take);
                }
                else if (name == "Powertrain")
                {
                    var api = asm.GetType("Powertrain.Api");
                    _tda ??= api?.GetMethod("TdaStatus", f, null, Type.EmptyTypes, null);
                    _tdaKey ??= api?.GetMethod("TdaKeyName", f, null, Type.EmptyTypes, null);
                    _fire ??= api?.GetMethod("FireStatus", f, null, Type.EmptyTypes, null);
                    _fireKey ??= api?.GetMethod("FireKeyName", f, null, Type.EmptyTypes, null);
                }
            }
            catch { }
        }
    }

    
    public static Vector4 Smoke()
    {
        Find();
        if (_smoke == null) return Vector4.zero;
        try { return (Vector4)_smoke.Invoke(null, null); }
        catch { return Vector4.zero; }
    }

    
    public static Vector4 Tda()
    {
        Find();
        if (_tda == null) return Vector4.zero;
        try { return (Vector4)_tda.Invoke(null, null); }
        catch { return Vector4.zero; }
    }

    
    
    public static Vector4 Fire()
    {
        Find();
        if (_fire == null) return Vector4.zero;
        try { return (Vector4)_fire.Invoke(null, null); }
        catch { return Vector4.zero; }
    }

    
    
    public static float[] ApsSegments()
    {
        Find();
        if (_apsSegments == null) return null;
        try { return _apsSegments(); }
        catch { return null; }
    }

    
    
    public static float[] ApsSegmentFrames()
    {
        Find();
        if (_apsFrames == null) return null;
        try { return _apsFrames(); }
        catch { return null; }
    }

    
    public static int StabState()
    {
        Find();
        if (_stabState == null) return -1;
        try { return _stabState(); }
        catch { return -1; }
    }

    
    public static void TakeOverStab()
    {
        try { _stabTakeOver?.Invoke(); }
        catch { }
    }

    public static string SmokeKey() => KeyName(_smokeKey, "G");
    public static string TdaKey() => KeyName(_tdaKey, "H");
    public static string FireKey() => KeyName(_fireKey, "6");

    private static string KeyName(MethodInfo m, string fallback)
    {
        try { return m?.Invoke(null, null) as string ?? fallback; }
        catch { return fallback; }
    }
}
