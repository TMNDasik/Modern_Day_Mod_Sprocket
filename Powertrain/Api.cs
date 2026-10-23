using System;
using UnityEngine;

namespace Powertrain;


public static class Api
{
    
    public static bool IsTurbine(IntPtr engineBehaviour)
    {
        try { return Turbines.Get(engineBehaviour) != null; }
        catch { return false; }
    }

    
    public static string EngineSound(IntPtr engineBehaviour)
    {
        try { return EngineSoundChoice.For(engineBehaviour); }
        catch { return "auto"; }
    }

    
    public static Vector4 TdaStatus()
    {
        try { return Tda.Status(); }
        catch { return Vector4.zero; }
    }

    
    public static string TdaKeyName() => Plugin.TdaKey.ToString();

    
    
    public static Vector4 FireStatus()
    {
        try { return FireSuppression.Status(); }
        catch { return Vector4.zero; }
    }

    
    public static string FireKeyName() => FireSuppression.KeyName;
}
