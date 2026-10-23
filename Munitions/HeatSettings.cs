using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Sprocket.Vehicles.Cannons;
using UnityEngine;

namespace Munitions;






internal static class HeatSettings
{
    public const float MinSpeed = 300f, MaxSpeed = 1800f;
    public const float MinPenetration = 20f, MaxPenetration = 1500f;

    private static readonly Regex Token = new(@"\s?\[heat:([0-9.]+),([0-9.]+)\]", RegexOptions.Compiled);

    
    private static readonly Dictionary<long, float> Measured = new();

    public static void Read(string description, out float penetration, out float speed)
    {
        penetration = speed = 0f;
        if (string.IsNullOrEmpty(description)) return;
        var m = Token.Match(description);
        if (!m.Success) return;
        float.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out penetration);
        float.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out speed);
    }

    public static void Read(CannonBlueprint bp, out float penetration, out float speed)
    {
        string description = null;
        try { description = bp?.Description; }
        catch { }
        Read(description, out penetration, out speed);
    }

    public static string With(string description, float penetration, float speed)
    {
        var d = Token.Replace(description ?? "", "", 1);
        if (penetration <= 0f && speed <= 0f) return d;
        return d + string.Format(CultureInfo.InvariantCulture, " [heat:{0:0},{1:0}]",
            penetration > 0f ? Mathf.Clamp(penetration, MinPenetration, MaxPenetration) : 0f,
            speed > 0f ? Mathf.Clamp(speed, MinSpeed, MaxSpeed) : 0f);
    }

    public static float DefaultPenetration(int caliber) => ShellSpec.For(caliber).HeatShellPenetration;

    public static float EffectivePenetration(int caliber, float setting) =>
        setting > 0f ? Mathf.Clamp(setting, MinPenetration, MaxPenetration) : DefaultPenetration(caliber);

    
    
    public static float JetSpeed(int caliber, float penetration)
    {
        var basePen = DefaultPenetration(caliber);
        if (basePen < 1f || penetration <= 0f) return ShellSpec.JetSpeed;
        return Mathf.Clamp(ShellSpec.JetSpeed * Mathf.Pow(penetration / basePen, 1f / 1.43f), 150f, 20000f);
    }

    private static long GunKey(CannonBlueprint bp)
    {
        try { return ((long)bp.Caliber << 32) | (uint)Mathf.RoundToInt(bp.MuzzleVelocity * 10f); }
        catch { return 0; }
    }

    public static void RecordMeasured(CannonBlueprint bp, float speed)
    {
        if (bp == null || speed < 100f) return;
        Measured[GunKey(bp)] = speed;
    }

    
    public static float AutoSpeed(CannonBlueprint bp, out bool measured)
    {
        measured = false;
        if (bp == null) return 900f;
        if (Measured.TryGetValue(GunKey(bp), out var v)) { measured = true; return v; }
        try
        {
            var s = ShellSpec.For(bp.Caliber);
            return Mathf.Clamp(ShellSpec.EstimateSpeed(bp.MuzzleVelocity, s.HeatShellLength, bp.Caliber), MinSpeed, MaxSpeed);
        }
        catch { return 900f; }
    }

    public static float EffectiveSpeed(CannonBlueprint bp, float setting) =>
        setting > 0f ? Mathf.Clamp(setting, MinSpeed, MaxSpeed) : AutoSpeed(bp, out _);
}
