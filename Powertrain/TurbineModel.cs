using System;

namespace Powertrain;








internal static class TurbineModel
{
    public const float PowerTurbineRpm = 22500f;
    public const float RpmToRad = MathF.PI / 30f;
    public const float RadToRpm = 30f / MathF.PI;
    
    public const float IdleFloor = 0.05f;
    
    public const byte EquivalentCylinders = 12;
    
    public const float IdleFuelFraction = 0.12f;

    public static float PeakRpm(TurbineSpec s) => PowerTurbineRpm / s.Ratio;
    public static float RunawayRpm(TurbineSpec s) => 2f * PeakRpm(s);
    public static float RatedTorque(TurbineSpec s) => s.Power * 1000f / (PeakRpm(s) * RpmToRad);
    public static float StallTorque(TurbineSpec s) => 2f * RatedTorque(s);

    public static float TorqueAt(TurbineSpec s, float rpm) => StallTorque(s) * MathF.Max(0f, 1f - rpm / RunawayRpm(s));
    public static float PowerAt(TurbineSpec s, float rpm) => TorqueAt(s, rpm) * MathF.Max(0f, rpm) * RpmToRad / 1000f;
    public static float MaxPowerUpTo(TurbineSpec s, float rpm) => PowerAt(s, MathF.Min(PeakRpm(s), rpm));

    public static int RevLimit(TurbineSpec s) => s.Override ? s.Limit : Math.Min(s.Upshift + 50, s.Safe);

    
    
    public static float Mass(TurbineSpec s) => 100f + 1.2f * s.Power + 8f * s.Ratio;
    public static float Volume(TurbineSpec s) => 0.05f + s.Power / 1400f;

    
    private const float BlockLengthPerWidth = 0.15927f * (EquivalentCylinders / 2 + 2.0032f);

    
    public static float EquivalentCylinderVolume(TurbineSpec s)
    {
        var width = MathF.Cbrt(Volume(s) / BlockLengthPerWidth);
        var vc = 1.85f * (width / 0.879f) * (width / 0.879f);
        return Math.Clamp(vc, 0.5f, 30f);
    }

    
    public static float FuelFullPerSecond(TurbineSpec s) => s.Power * 0.85f / 3600f * Math.Max(0f, Plugin.FuelMultiplier.Value);
    public static float FuelPerSecond(TurbineSpec s, float n1) =>
        FuelFullPerSecond(s) * (IdleFuelFraction + (1f - IdleFuelFraction) * Math.Clamp(n1, 0f, 1f));

    
    public static float DragPerRad(TurbineSpec s) => 0.05f * RatedTorque(s) / (PeakRpm(s) * RpmToRad);

    
    public const float SpoolA = 0.35f, SpoolB = 0.65f;
    public static float SpoolBase()
    {
        var t = Math.Max(0.2f, Plugin.SpoolUpTime.Value);
        return t * SpoolB / MathF.Log((SpoolA + SpoolB) / SpoolA);
    }
}
