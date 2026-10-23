using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Sprocket.Vehicles.Cannons;
using UnityEngine;

namespace Munitions;






internal static class ApfsdsSettings
{
    public const float MinSpeed = 800f, MaxSpeed = 2200f;
    public const float MinPenetration = 20f, MaxPenetration = 2000f;

    private static readonly Regex Token = new(@"\[apfsds:([0-9.]+),([0-9.]+),([0-9.]+)\]", RegexOptions.Compiled);

    
    private static readonly Dictionary<long, float> Measured = new();

    public static float MinDart(int caliber) => Mathf.Max(3f, Mathf.Round(0.12f * caliber));
    public static float MaxDart(int caliber) => Mathf.Max(MinDart(caliber) + 1f, Mathf.Round(0.5f * caliber));

    public static void Read(string description, out float dart, out float speed, out float penetration)
    {
        dart = speed = penetration = 0f;
        if (string.IsNullOrEmpty(description)) return;
        var m = Token.Match(description);
        if (!m.Success) return;
        float.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out dart);
        float.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out speed);
        float.TryParse(m.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out penetration);
    }

    public static void Read(CannonBlueprint bp, out float dart, out float speed, out float penetration)
    {
        string description = null;
        try { description = bp?.Description; }
        catch { }
        Read(description, out dart, out speed, out penetration);
    }

    public static string With(string description, float dart, float speed, float penetration)
    {
        var d = description ?? "";
        if (dart <= 0f && speed <= 0f && penetration <= 0f)
            return Token.IsMatch(d) ? Token.Replace(d, "", 1).Trim() : d;
        var token = string.Format(CultureInfo.InvariantCulture, "[apfsds:{0:0},{1:0},{2:0}]",
            Mathf.Max(0f, dart), speed > 0f ? Mathf.Clamp(speed, MinSpeed, MaxSpeed) : 0f,
            penetration > 0f ? Mathf.Clamp(penetration, MinPenetration, MaxPenetration) : 0f);
        if (Token.IsMatch(d)) return Token.Replace(d, token, 1);
        return d.Length == 0 ? token : d + " " + token;
    }

    public static float EffectiveDart(int caliber, float setting) =>
        setting > 0f ? Mathf.Clamp(setting, MinDart(caliber), MaxDart(caliber)) : ShellSpec.For(caliber).RodDiameter;

    
    public static float RodLength(int caliber, float dart) => Mathf.Min(23f * dart, 6f * caliber);

    public static float RodMass(int caliber, float dart) =>
        ShellSpec.TungstenDensity * Mathf.PI / 4f * (dart / 1000f) * (dart / 1000f) * (RodLength(caliber, dart) / 1000f) * 1.08f;

    public static float AutoPenetration(int caliber, float dart, float speed)
    {
        var s = ShellSpec.For(caliber);
        return ShellSpec.Penetration(dart, RodMass(caliber, dart), speed, s.RodConstant);
    }

    public static float EffectivePenetration(int caliber, float dart, float speed, float setting) =>
        setting > 0f ? Mathf.Clamp(setting, MinPenetration, MaxPenetration) : AutoPenetration(caliber, dart, speed);

    
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
        if (bp == null) return 1500f;
        if (Measured.TryGetValue(GunKey(bp), out var v)) { measured = true; return v; }
        try
        {
            var s = ShellSpec.For(bp.Caliber);
            var ap = bp.MuzzleVelocity;
            var launchCalibers = Mathf.Max(0.1f, s.LaunchLength * 1000f / Mathf.Max(1, (int)bp.Caliber));
            var est = ap * Mathf.Sqrt(2.58f / launchCalibers);
            return Mathf.Clamp(est, ap, Mathf.Min(MaxSpeed, ap * 2.2f));
        }
        catch { return 1500f; }
    }

    public static float EffectiveSpeed(CannonBlueprint bp, float setting) =>
        setting > 0f ? Mathf.Clamp(setting, MinSpeed, MaxSpeed) : AutoSpeed(bp, out _);

    
    public static CannonBlueprint BlueprintOf(Transform weapon)
    {
        try
        {
            var cannon = weapon != null ? weapon.GetComponentInParent<Cannon>() : null;
            return cannon != null ? cannon.Blueprint : null;
        }
        catch { return null; }
    }
}
