using System;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace CombatEffects;




internal static unsafe class ParticleNative
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Module
    {
        public IntPtr System;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SetFloatFn(ref Module module, float value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate float GetFloatFn(ref Module module);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SetVectorFn(IntPtr self, ref Vector3 value);

    private static SetFloatFn _setRate, _setDistance, _setFlipRotation, _setSimSpeed;
    private static GetFloatFn _getRate, _getDistance;
    
    private static SetFloatFn _setVelX, _setVelY, _setVelZ, _setSize, _setSpeed, _setLife;
    private static GetFloatFn _getSize, _getSpeed, _getLife;
    private static SetVectorFn _setFlip, _setPivot;
    private static bool _resolved, _ok;

    private static T Resolve<T>(string name) where T : Delegate
    {
        var p = IL2CPP.il2cpp_resolve_icall(name);
        if (p == IntPtr.Zero) throw new MissingMethodException("icall not found: " + name);
        return Marshal.GetDelegateForFunctionPointer<T>(p);
    }

    private static bool Ready()
    {
        if (_resolved) return _ok;
        _resolved = true;
        try
        {
            _getRate = Resolve<GetFloatFn>("UnityEngine.ParticleSystem/EmissionModule::get_rateOverTimeMultiplier");
            _setRate = Resolve<SetFloatFn>("UnityEngine.ParticleSystem/EmissionModule::set_rateOverTimeMultiplier");
            _getDistance = Resolve<GetFloatFn>("UnityEngine.ParticleSystem/EmissionModule::get_rateOverDistanceMultiplier");
            _setDistance = Resolve<SetFloatFn>("UnityEngine.ParticleSystem/EmissionModule::set_rateOverDistanceMultiplier");
            _setFlipRotation = Resolve<SetFloatFn>("UnityEngine.ParticleSystem/MainModule::set_flipRotation");
            _setFlip = Resolve<SetVectorFn>("UnityEngine.ParticleSystemRenderer::set_flip_Injected");
            _setPivot = Resolve<SetVectorFn>("UnityEngine.ParticleSystemRenderer::set_pivot_Injected");
            _ok = true;
        }
        catch (Exception e) { Plugin.Log.LogWarning("Particle settings unavailable (fires and dust keep a fixed amount): " + e.Message); }
        
        try { _setSimSpeed = Resolve<SetFloatFn>("UnityEngine.ParticleSystem/MainModule::set_simulationSpeed"); }
        catch (Exception e) { Plugin.Log.LogWarning("Particle simulation speed unavailable: " + e.Message); }
        
        try
        {
            _setVelX = Resolve<SetFloatFn>("UnityEngine.ParticleSystem/VelocityOverLifetimeModule::set_xMultiplier");
            _setVelY = Resolve<SetFloatFn>("UnityEngine.ParticleSystem/VelocityOverLifetimeModule::set_yMultiplier");
            _setVelZ = Resolve<SetFloatFn>("UnityEngine.ParticleSystem/VelocityOverLifetimeModule::set_zMultiplier");
        }
        catch (Exception e)
        {
            _setVelX = _setVelY = _setVelZ = null;
            Plugin.Log.LogWarning("Particle velocity settings unavailable: " + e.Message);
        }
        
        try
        {
            _getSize = Resolve<GetFloatFn>("UnityEngine.ParticleSystem/MainModule::get_startSizeMultiplier");
            _setSize = Resolve<SetFloatFn>("UnityEngine.ParticleSystem/MainModule::set_startSizeMultiplier");
            _getSpeed = Resolve<GetFloatFn>("UnityEngine.ParticleSystem/MainModule::get_startSpeedMultiplier");
            _setSpeed = Resolve<SetFloatFn>("UnityEngine.ParticleSystem/MainModule::set_startSpeedMultiplier");
            _getLife = Resolve<GetFloatFn>("UnityEngine.ParticleSystem/MainModule::get_startLifetimeMultiplier");
            _setLife = Resolve<SetFloatFn>("UnityEngine.ParticleSystem/MainModule::set_startLifetimeMultiplier");
        }
        catch (Exception e)
        {
            _setSize = _setSpeed = _setLife = null;
            _getSize = _getSpeed = _getLife = null;
            Plugin.Log.LogWarning("Particle start size settings unavailable: " + e.Message);
        }
        return _ok;
    }

    
    public static Vector3 GetStart(ParticleSystem ps)
    {
        Ready();
        if (ps == null || _getSize == null) return Vector3.one;
        var m = new Module { System = ps.Pointer };
        return new Vector3(_getSize(ref m), _getSpeed(ref m), _getLife(ref m));
    }

    
    public static void SetStart(ParticleSystem ps, Vector3 sizeSpeedLife)
    {
        Ready();
        if (ps == null || _setSize == null) return;
        var m = new Module { System = ps.Pointer };
        _setSize(ref m, sizeSpeedLife.x);
        _setSpeed(ref m, sizeSpeedLife.y);
        _setLife(ref m, sizeSpeedLife.z);
    }

    
    public static void SetVelocity(ParticleSystem ps, Vector3 v)
    {
        Ready();
        if (ps == null || _setVelX == null) return;
        var m = new Module { System = ps.Pointer };
        _setVelX(ref m, v.x);
        _setVelY(ref m, v.y);
        _setVelZ(ref m, v.z);
    }

    
    public static bool SetSimulationSpeed(ParticleSystem ps, float speed)
    {
        Ready();
        if (ps == null || _setSimSpeed == null) return false;
        var m = new Module { System = ps.Pointer };
        _setSimSpeed(ref m, speed);
        return true;
    }

    public static float GetRate(ParticleSystem ps)
    {
        if (ps == null || !Ready()) return 1f;
        var m = new Module { System = ps.Pointer };
        return _getRate(ref m);
    }

    public static float GetDistanceRate(ParticleSystem ps)
    {
        if (ps == null || !Ready()) return 0f;
        var m = new Module { System = ps.Pointer };
        return _getDistance(ref m);
    }

    public static void SetRates(ParticleSystem ps, float perSecond, float perMetre)
    {
        if (ps == null || !Ready()) return;
        var m = new Module { System = ps.Pointer };
        _setRate(ref m, perSecond);
        _setDistance(ref m, perMetre);
    }

    public static void SetFlip(ParticleSystem ps, ParticleSystemRenderer r, bool flip, Vector3 pivot)
    {
        if (!Ready()) return;
        if (ps != null)
        {
            var m = new Module { System = ps.Pointer };
            _setFlipRotation(ref m, flip ? 1f : 0f);
        }
        if (r != null)
        {
            var f = flip ? Vector3.right : Vector3.zero;
            var p = flip ? new Vector3(-pivot.x, pivot.y, pivot.z) : pivot;
            _setFlip(r.m_CachedPtr, ref f);
            _setPivot(r.m_CachedPtr, ref p);
        }
    }
}
