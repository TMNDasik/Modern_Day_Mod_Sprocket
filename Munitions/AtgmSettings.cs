using System;
using System.Globalization;
using System.Text.RegularExpressions;
using Sprocket.Vehicles.Cannons;
using UnityEngine;

namespace Munitions;




internal static class AtgmSettings
{
    public const float MinSpeed = 115f, MaxSpeed = 850f;
    public const float MinG = 1f, MaxG = 40f;
    public const float MinPenetration = 50f, MaxPenetration = 2500f;
    public const float Gravity = 9.81f;
    public const float MaxFlightCap = 60f; 

    private static readonly Regex Token = new(@"\[atgm:([0-9.]+),([0-9.]+)(?:,([0-9.]+))?(?:,([0-9]))?(?:,([0-9]))?\]", RegexOptions.Compiled);

    
    
    public static readonly string[] LinkNames = { "Wireless", "Wire-guided" };

    public static bool Wire(string description)
    {
        if (string.IsNullOrEmpty(description)) return false;
        var m = Token.Match(description);
        return m.Success && m.Groups[5].Success && m.Groups[5].Value == "1";
    }

    public static bool Wire(CannonBlueprint bp)
    {
        try { return Wire(bp?.Description); }
        catch { return false; }
    }

    
    
    
    public const int GuideLaserBeam = 0, GuideSaclos = 1;
    public static readonly string[] GuideNames = { "Laser beam-riding", "SACLOS (IR tracker)" };

    public static int Guide(string description)
    {
        if (string.IsNullOrEmpty(description)) return GuideLaserBeam;
        var m = Token.Match(description);
        return m.Success && m.Groups[4].Success && m.Groups[4].Value == "1" ? GuideSaclos : GuideLaserBeam;
    }

    public static int Guide(CannonBlueprint bp)
    {
        try { return Guide(bp?.Description); }
        catch { return GuideLaserBeam; }
    }

    
    
    public static readonly string[] WarheadNames = { "Tandem HEAT", "HE-fragmentation", "Thermobaric" };
    private static readonly Regex WarheadToken = new(@"\s?\[atgmw:([0-9])\]", RegexOptions.Compiled);

    public static int Warhead(string description)
    {
        if (string.IsNullOrEmpty(description)) return Kind.WarheadHeat;
        var m = WarheadToken.Match(description);
        if (!m.Success) return Kind.WarheadHeat;
        return m.Groups[1].Value switch { "1" => Kind.WarheadHe, "2" => Kind.WarheadTb, _ => Kind.WarheadHeat };
    }

    public static int Warhead(CannonBlueprint bp)
    {
        try { return Warhead(bp?.Description); }
        catch { return Kind.WarheadHeat; }
    }

    
    public static string WithWarhead(string description, int warhead)
    {
        var d = WarheadToken.Replace(description ?? "", "", 1);
        if (warhead != Kind.WarheadHe && warhead != Kind.WarheadTb) return d;
        return d + $" [atgmw:{warhead}]";
    }

    public static float ClampSpeed(float v) => Mathf.Clamp(v, MinSpeed, MaxSpeed);
    public static float ClampG(float v) => Mathf.Clamp(v, MinG, MaxG);
    public static float ClampPenetration(float v) => Mathf.Clamp(v, MinPenetration, MaxPenetration);

    public static float DefaultSpeed(int kind) =>
        ClampSpeed(Kind.IsLauncher(kind) ? Plugin.LauncherCruiseSpeed.Value : Plugin.AtgmCruiseSpeed.Value);

    public static float DefaultG => ClampG((float)Math.Round(Plugin.AtgmMaxAccel.Value / Gravity * 2f) / 2f);

    
    public static float DefaultPenetration(int kind, int caliber)
    {
        var s = ShellSpec.For(caliber);
        return ClampPenetration((float)Math.Round(Kind.IsLauncher(kind) ? s.LauncherPenetration : s.HeatPenetration));
    }

    public static float EffectivePenetration(int kind, int caliber, float setting) =>
        setting > 0f ? ClampPenetration(setting) : DefaultPenetration(kind, caliber);

    
    
    public static float Range(int kind) => Kind.IsLauncher(kind)
        ? Mathf.Max(50f, Plugin.LauncherCruiseSpeed.Value) * Mathf.Max(3f, Plugin.LauncherMaxFlight.Value)
        : Mathf.Max(50f, Plugin.AtgmCruiseSpeed.Value) * Mathf.Max(3f, Plugin.AtgmMaxFlight.Value);

    public static float MaxFlight(int kind, float speed) => Mathf.Clamp(Range(kind) / Mathf.Max(speed, 50f), 3f, MaxFlightCap);

    
    public static void Read(string description, int kind, out float speed, out float g, out float penetration)
    {
        speed = DefaultSpeed(kind);
        g = DefaultG;
        penetration = 0f;
        if (string.IsNullOrEmpty(description)) return;
        var m = Token.Match(description);
        if (!m.Success) return;
        if (float.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var s)) speed = ClampSpeed(s);
        if (float.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var a)) g = ClampG(a);
        if (m.Groups[3].Success && float.TryParse(m.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var p) && p > 0f)
            penetration = ClampPenetration(p);
    }

    public static void Read(CannonBlueprint bp, int kind, out float speed, out float g, out float penetration)
    {
        string description = null;
        try { description = bp?.Description; }
        catch { }
        Read(description, kind, out speed, out g, out penetration);
    }

    
    public static string With(string description, float speed, float g, float penetration, int guide = -1, int wire = -1)
    {
        if (guide < 0) guide = Guide(description);
        if (wire < 0) wire = Wire(description) ? 1 : 0;
        var token = string.Format(CultureInfo.InvariantCulture, "[atgm:{0:0},{1:0.0},{2:0},{3},{4}]",
            ClampSpeed(speed), ClampG(g), penetration > 0f ? ClampPenetration(penetration) : 0f, guide == GuideSaclos ? 1 : 0, wire > 0 ? 1 : 0);
        if (string.IsNullOrEmpty(description)) return token;
        return Token.IsMatch(description) ? Token.Replace(description, token, 1) : description + token;
    }

    
    public static void ForWeapon(Transform weapon, int kind, out float speed, out float g, out float penetration)
    {
        CannonBlueprint bp = null;
        try
        {
            var cannon = weapon != null ? weapon.GetComponentInParent<Cannon>() : null;
            bp = cannon != null ? cannon.Blueprint : null;
        }
        catch { }
        Read(bp, kind, out speed, out g, out penetration);
    }
}
