using System;
using Sprocket.Blueprints;
using Sprocket.Vehicles;
using Sprocket.Vehicles.Turrets;
using Sprocket.Vehicles.Weapons;

namespace GunStabilizer;






internal static class StabSettings
{
    public const string Token = "[stabilizer]";

    public static bool Has(Blueprint bp)
    {
        if (bp == null) return false;
        var d = bp.Description;
        return d != null && d.Contains(Token, StringComparison.Ordinal);
    }

    public static string With(string description, bool on)
    {
        var d = description ?? "";
        var has = d.Contains(Token, StringComparison.Ordinal);
        if (on == has) return description;
        if (on) return d.Length == 0 ? Token : d + " " + Token;
        d = d.Replace(" " + Token, "").Replace(Token, "");
        return d.Length == 0 ? null : d;
    }

    public static Blueprint Blueprint(TraverseMotor motor)
    {
        try { return motor?.TraverseBlueprintSlot?.Blueprint; }
        catch { return null; }
    }

    public static Blueprint Blueprint(LayingDrive drive)
    {
        try { return drive?.BlueprintSlot?.Blueprint; }
        catch { return null; }
    }

    public static bool Horizontal(TraverseMotor motor) => motor != null && Has(Blueprint(motor));

    public static bool Vertical(LayingDrive drive) => drive != null && Has(Blueprint(drive));

    
    public static bool PoweredTraverse(TraverseMotor motor)
    {
        try
        {
            var bp = motor?.MotorBlueprintSlot?.Blueprint;
            return bp != null && bp.Type == MotorType.Electric;
        }
        catch { return false; }
    }
}


internal static class StabSpec
{
    public const float MradToDeg = 0.0572958f;

    
    
    
    
    
    public const float VerticalMaxRate = 40f;       
    public const float VerticalMaxAccel = 720f;     
    public const float VerticalLag = 0.006f;        
    public const float VerticalDrift = 0.12f * MradToDeg; 

    
    
    public const float HorizontalMaxAccel = 720f;   
    public const float HorizontalLag = 0.012f;
    public const float HorizontalDrift = 0.25f * MradToDeg;

    
    public const float NoiseBase = 0.02f * MradToDeg;            
    public const float NoisePerRate = 0.05f * MradToDeg;         
    public const float NoiseMax = 1.0f * MradToDeg;

    public const float NoiseTime = 0.08f;           
    public const float DriftTime = 20f;             
    public const float EngageDelay = 0.5f;          
    public const float MinHealth = 0.3f;            

    public static string VerticalInfo =>
        "Vertical channel of a two-plane gun stabilizer. A gyroscope on the gun measures its elevation in space and an " +
        $"electro-hydraulic drive (up to {VerticalMaxRate:0}°/s) holds it while the hull pitches. The error on the move is " +
        "about 0.2-0.5 mrad and grows on rough ground.";

    public static string HorizontalInfo =>
        "Horizontal channel of a two-plane gun stabilizer. A gyroscope on the turret holds its azimuth while the hull turns " +
        "and yaws. This traverse motor does the work, so its speed limits how well fast turns are compensated. " +
        "On a turret ring mounted on its side (used instead of a laying drive to elevate a gun) this is the vertical channel.";

    public static string CommonInfo =>
        $"In battle press {Plugin.ToggleKeyName} to switch the stabilizer on or off.";

    public const string OffInfo =
        "Without a stabilizer the gun is locked to the hull by its drives: on the move the sight picture jumps with every bump " +
        "and the gunner has to keep correcting the aim. For a two-plane stabilizer switch it on for the turret and for the " +
        "elevation drive (laying drive, or a turret ring mounted on its side).";

    public const string NoPowerTraverse =
        "Needs a powered (electric) traverse motor. With a hand-cranked traverse the horizontal channel cannot work.";
}
