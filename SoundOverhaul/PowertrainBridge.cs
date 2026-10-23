using System;
using UnityEngine;

namespace SoundOverhaul;



internal static class PowertrainBridge
{
    private static Func<IntPtr, bool> _isTurbine;
    private static Func<IntPtr, string> _engineSound;
    private static bool _searched;
    private static float _nextTry;

    private static bool Find()
    {
        if (_isTurbine != null) return true;
        if (_searched && Time.unscaledTime < _nextTry) return false;
        _searched = true;
        _nextTry = Time.unscaledTime + 5f;
        try
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (a.GetName().Name is not ("Powertrain" or "MDPowertrain")) continue;
                var api = a.GetType("Powertrain.Api");
                var m = api?.GetMethod("IsTurbine", new[] { typeof(IntPtr) });
                if (m != null) _isTurbine = (Func<IntPtr, bool>)Delegate.CreateDelegate(typeof(Func<IntPtr, bool>), m);
                var s = api?.GetMethod("EngineSound", new[] { typeof(IntPtr) });
                if (s != null) _engineSound = (Func<IntPtr, string>)Delegate.CreateDelegate(typeof(Func<IntPtr, string>), s);
                break;
            }
        }
        catch { }
        return _isTurbine != null;
    }

    public static bool IsTurbine(IntPtr engineBehaviour)
    {
        if (engineBehaviour == IntPtr.Zero || !Find()) return false;
        try { return _isTurbine(engineBehaviour); }
        catch { return false; }
    }

    
    public static string EngineSound(IntPtr engineBehaviour)
    {
        if (engineBehaviour == IntPtr.Zero || !Find() || _engineSound == null) return "auto";
        try { return _engineSound(engineBehaviour) ?? "auto"; }
        catch { return "auto"; }
    }
}
