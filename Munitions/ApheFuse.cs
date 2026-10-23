using System;
using UnityEngine;

namespace Munitions;







internal static class ApheFuse
{
    public const float Lifetime = 0.08f;   
    private const float Speed = 50f;
    private const int Penetrated = 2, Deflected = 4;
    private static int _errors;

    
    
    public static void OnHit(SizeTypes types, Vector3 prev, Vector3 hit, Vector3 dir, int hitType, bool ended)
    {
        if (types == null || dir.sqrMagnitude < 1e-6f) return;
        dir.Normalize();
        var spec = types.Spec;
        var cal = types.Caliber;

        
        var entry = hit;
        var span = Vector3.Dot(hit - prev, dir);
        try
        {
            if (span > 0.05f && Guidance.DamageModelRaycast(prev, dir, span + 0.5f, out var d, out _) && d < span + 0.5f)
                entry = prev + dir * d;
        }
        catch (Exception e) { if (_errors++ < 3) Plugin.Log.LogWarning("APHE fuse raycast failed: " + e.Message); }

        var inside = Vector3.Dot(hit - entry, dir);
        var penetrated = inside > 0.15f || (hitType & Penetrated) != 0;
        if (!penetrated)
        {
            if ((hitType & Deflected) != 0) return;
            Burst(entry - dir * 0.3f, dir, spec.ApheBlast, cal, "on the armour (no penetration)");
            return;
        }
        var fuse = Mathf.Clamp(Plugin.ApheFuseDistance.Value, 0.2f, 4f);
        var depth = ended && inside > 0.15f ? Mathf.Min(fuse, inside - 0.1f) : fuse;
        depth = Mathf.Max(0.15f, depth);
        Burst(entry + dir * (depth - 0.25f), dir, spec.ApheBlast, cal, $"{depth:0.0} m behind the armour");
    }

    private static void Burst(Vector3 from, Vector3 dir, float power, int caliber, string where)
    {
        if (!Warheads.Fuse(from, dir * Speed, caliber, power)) return;
        if (Plugin.DebugLogging.Value)
            Plugin.Log.LogInfo($"{caliber} mm APHE burst {where}, blast radius {Mathf.Sqrt(power / 10f):0.0} m");
    }
}
