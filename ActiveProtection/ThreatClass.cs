using System;
using System.Collections.Generic;
using Sprocket.DamageModelling;
using UnityEngine;

namespace ActiveProtection;


internal sealed class DefInfo
{
    public ThreatTypes Type;
    public bool Threat;
    public bool Heat;         
    public float Caliber;     
    public float Mass;        
    public float Diameter;    
    public float Constant = 2000f;
    public string Name;
}

internal static class ThreatClass
{
    private static readonly Dictionary<IntPtr, DefInfo> Cache = new();
    private static int _cachedMinCaliber = -1;

    public static DefInfo Of(IntPtr defPtr)
    {
        if (defPtr == IntPtr.Zero) return null;
        var min = Plugin.MinThreatCaliber.Value;
        if (min != _cachedMinCaliber)
        {
            Cache.Clear();
            _cachedMinCaliber = min;
        }
        if (Cache.TryGetValue(defPtr, out var info)) return info;
        info = Classify(defPtr, min);
        Cache[defPtr] = info;
        if (Plugin.DebugLogging.Value)
            Plugin.Log.LogInfo($"APS projectile type '{info.Name}': {(info.Threat ? ThreatInfo.Name(info.Type) : "not a threat")}, {info.Caliber:0} mm");
        return info;
    }

    private static DefInfo Classify(IntPtr defPtr, int minCaliber)
    {
        var info = new DefInfo();
        try
        {
            var def = new ProjectileDefinition(defPtr);
            info.Name = def.Name;
            info.Mass = def.Mass;
            info.Diameter = def.Diameter * 1000f;
            info.Caliber = info.Diameter;
            bool ap = false, explosive = false;
            var functions = def.Functions;
            if (functions != null)
                for (var i = 0; i < functions.Length; i++)
                {
                    var f = functions[i];
                    if (f == null) continue;
                    var apf = f.TryCast<ArmourPiercingProjectileFunction>();
                    if (apf != null)
                    {
                        ap = true;
                        if (apf.PenetratorConstant > 0) info.Constant = apf.PenetratorConstant;
                    }
                    else if (f.TryCast<ExplosiveProjectileFunction>() != null) explosive = true;
                }

            var typeId = def.ID.Value;
            var kind = MunitionsBridge.Kind(typeId);
            if (kind >= 0)
            {
                info.Type = kind switch { 0 => ThreatTypes.Apfsds, 1 or 4 => ThreatTypes.He, _ => ThreatTypes.Atgm }; 
                info.Heat = kind == 4;
                var cal = MunitionsBridge.Caliber(typeId);
                if (cal > 0) info.Caliber = cal;
            }
            else if (ap) info.Type = ThreatTypes.Ap;
            else if (explosive) info.Type = ThreatTypes.He;
            else return info; 

            info.Threat = info.Caliber >= minCaliber;
        }
        catch (Exception e)
        {
            if (Plugin.DebugLogging.Value) Plugin.Log.LogWarning("APS projectile classification failed: " + e.Message);
        }
        return info;
    }

    
    public static float Danger(DefInfo info, float speed)
    {
        switch (info.Type)
        {
            case ThreatTypes.Atgm:
                return 6.4f * info.Caliber;
            case ThreatTypes.He:
                return 0.2f * info.Caliber + 15f;
            default:
                
                var d = Mathf.Max(1f, info.Diameter);
                var m = Mathf.Max(0.01f, info.Mass);
                var x = speed * Mathf.Sqrt(m) / (Mathf.Pow(d / 100f, 0.75f) * Mathf.Max(100f, info.Constant));
                return 100f * Mathf.Pow(Mathf.Max(0f, x), 1.43f);
        }
    }

    public static float HitChance(ThreatTypes type) => type switch
    {
        ThreatTypes.Atgm => Plugin.ChanceAtgm.Value,
        ThreatTypes.He => Plugin.ChanceHe.Value,
        ThreatTypes.Ap => Plugin.ChanceAp.Value,
        ThreatTypes.Apfsds => Plugin.ChanceApfsds.Value,
        _ => 0f
    };

    public static bool Kinetic(ThreatTypes type) => type == ThreatTypes.Ap || type == ThreatTypes.Apfsds;
}
