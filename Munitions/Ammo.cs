using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Munitions;


internal static class Kind
{
    
    
    
    
    
    
    
    
    public const int Apfsds = 0, He = 1, Atgm = 2, Heat = 3, Ap = 4, Aphe = 5, Hesh = 6, Count = 7,
        Launcher = 7, RocketHe = 8, RocketTb = 9, Warhead = 10, Blast = 11, RocketHeat = 12, AtgmHe = 13, AtgmTb = 14,
        LauncherHe = 15, LauncherTb = 16, Fuse = 17, Total = 18;

    public static readonly string[] Names =
    {
        "APFSDS", "HE", "ATGM", "HEAT", "AP", "APHE", "HESH", "ATGM launcher", "HE rocket", "Thermobaric rocket", "Shaped charge", "Blast",
        "HEAT rocket", "HE ATGM", "Thermobaric ATGM", "HE launcher missile", "Thermobaric launcher missile", "APHE burst"
    };

    
    public static readonly int[] Order = { Apfsds, Ap, Aphe, Heat, He, Hesh, Atgm };

    
    public static bool IsMissile(int kind) => kind is Atgm or Launcher or AtgmHe or AtgmTb or LauncherHe or LauncherTb;

    
    public static bool IsLauncher(int kind) => kind is Launcher or LauncherHe or LauncherTb;

    
    public static bool IsRocket(int kind) => kind is RocketHe or RocketTb or RocketHeat;

    
    public static bool IsKinetic(int kind) => kind is Apfsds or Ap or Aphe;

    public static bool IsThermobaric(int kind) => kind is RocketTb or AtgmTb or LauncherTb;

    
    public const int WarheadHeat = 0, WarheadHe = 1, WarheadTb = 2;

    public static int WarheadOf(int kind) => kind switch
    {
        AtgmHe or LauncherHe or RocketHe => WarheadHe,
        AtgmTb or LauncherTb or RocketTb => WarheadTb,
        _ => WarheadHeat
    };

    
    public static int WithWarhead(int missile, int warhead) => Base(missile) == Launcher
        ? warhead switch { WarheadHe => LauncherHe, WarheadTb => LauncherTb, _ => Launcher }
        : warhead switch { WarheadHe => AtgmHe, WarheadTb => AtgmTb, _ => Atgm };

    
    public static int Base(int kind) => kind switch
    {
        AtgmHe or AtgmTb => Atgm,
        LauncherHe or LauncherTb => Launcher,
        _ => kind
    };

    
    public static readonly int[] AutoOrder = { Apfsds, Heat, Aphe, Ap, Hesh, He, Atgm };

    
    
    
    
    public static int External(int kind) => kind switch
    {
        Heat => 4, Launcher or LauncherHe or LauncherTb => 3, RocketHe or RocketHeat => 5, RocketTb => 6, Warhead => 4, Blast or Fuse => 1,
        Ap or Aphe => 0, Hesh => 1, AtgmHe or AtgmTb => 2, _ => kind
    };

    
    
    public static int Burst(int kind) => kind switch
    {
        AtgmHe or LauncherHe => 5, AtgmTb or LauncherTb => 6, RocketHeat => 3, _ => External(kind)
    };

    
    public static string GuidKey(int kind) => kind switch
    {
        Heat => "heat", Launcher => "3", RocketHe => "rhe", RocketTb => "rtb", Warhead => "dw", Blast => "bl",
        Ap => "ap", Aphe => "aphe", Hesh => "hesh", RocketHeat => "rheat", AtgmHe => "atgmhe", AtgmTb => "atgmtb",
        LauncherHe => "lhe", LauncherTb => "ltb", Fuse => "fuse", _ => kind.ToString(CultureInfo.InvariantCulture)
    };

    public static int Sum(int[] d)
    {
        var s = 0;
        for (var k = 0; k < Count && k < d.Length; k++) s += d[k];
        return s;
    }
}




internal static class AmmoToken
{
    private static readonly Regex Pattern = new(@"\[ammo:(\d+),(\d+),(\d+)(?:,(\d+))?(?:,(\d+),(\d+),(\d+))?\]", RegexOptions.Compiled);

    public static bool TryParse(string description, out int[] design)
    {
        design = null;
        if (string.IsNullOrEmpty(description)) return false;
        var m = Pattern.Match(description);
        if (!m.Success) return false;
        design = new int[Kind.Count];
        for (var k = 0; k < Kind.Count; k++)
            if (m.Groups[k + 1].Success) design[k] = int.Parse(m.Groups[k + 1].Value, CultureInfo.InvariantCulture);
        return true;
    }

    public static string With(string description, int[] design)
    {
        var extra = design[Kind.Ap] + design[Kind.Aphe] + design[Kind.Hesh] > 0;
        var token = extra
            ? $"[ammo:{design[0]},{design[1]},{design[2]},{design[3]},{design[4]},{design[5]},{design[6]}]"
            : $"[ammo:{design[0]},{design[1]},{design[2]},{design[3]}]";
        var d = description ?? "";
        if (Pattern.IsMatch(d)) return Pattern.Replace(d, token, 1);
        return d.Length == 0 ? token : d + " " + token;
    }
}


internal static class Design
{
    public static bool AtgmAllowed(int caliber) => caliber >= Plugin.AtgmMinCaliber.Value;

    public static bool Managed(int caliber) => caliber >= Plugin.MinCaliber.Value;

    
    public static int[] Read(string description, int capacity, int caliber, bool forDisplay = false)
    {
        if (!AmmoToken.TryParse(description, out var d))
        {
            if (!forDisplay && !Plugin.ManageUnconfigured.Value) return null;
            d = new int[Kind.Count];
            d[Kind.Apfsds] = capacity;
        }
        return Clamp(d, capacity, caliber);
    }

    public static int[] Clamp(int[] d, int capacity, int caliber)
    {
        var r = new int[Kind.Count];
        for (var k = 0; k < Kind.Count; k++) r[k] = Math.Max(0, d[k]);
        if (!AtgmAllowed(caliber))
        {
            r[Kind.Apfsds] += r[Kind.Atgm];
            r[Kind.Atgm] = 0;
        }
        capacity = Math.Max(0, capacity);
        
        while (Kind.Sum(r) > capacity)
        {
            var big = 0;
            for (var k = 1; k < Kind.Count; k++) if (r[k] > r[big]) big = k;
            r[big]--;
        }
        return r;
    }

    
    
    
    
    public static int[] Set(int[] snapshot, int kind, int value, int capacity, bool atgmAllowed, int seed)
    {
        var d = (int[])snapshot.Clone();
        var others = new System.Collections.Generic.List<int>(Kind.Count);
        for (var k = 0; k < Kind.Count; k++)
            if (k != kind && (k != Kind.Atgm || atgmAllowed)) others.Add(k);
        if (others.Count == 0) return d;

        value = Math.Clamp(value, 0, capacity);
        var delta = value - d[kind];
        d[kind] = value;
        var rng = new Random(seed);
        var free = Math.Max(0, capacity - Kind.Sum(snapshot));

        if (delta > 0)
        {
            var take = delta - Math.Min(free, delta);
            while (take > 0)
            {
                var candidates = others.FindAll(k => d[k] > 0);
                if (candidates.Count == 0) { d[kind] -= take; break; }
                d[candidates[rng.Next(candidates.Count)]]--;
                take--;
            }
            var rest = free - Math.Min(free, delta);
            while (rest-- > 0) d[others[rng.Next(others.Count)]]++;
        }
        else if (delta < 0)
        {
            
            var give = -delta + free;
            while (give-- > 0) d[others[rng.Next(others.Count)]]++;
        }
        else if (free > 0)
        {
            while (free-- > 0) d[others[rng.Next(others.Count)]]++;
        }
        return d;
    }
}


internal sealed class ShellSpec
{
    public const float TungstenDensity = 17600f;
    public const float JetSpeed = 2000f;       
    public const float HeEstimatedSpeed = 850f;

    public int Caliber;
    public float FullBoreMass;                 

    
    public float RodDiameter, RodLength, RodMass;
    public float LaunchLength;                 
    public ushort RodConstant;

    
    public float HeMass, HeFiller, HePower;
    public ushort HeConstant;

    
    public float JetDiameter, JetMass, HeatPenetration, AtgmBlast;
    public ushort AtgmConstant;

    
    public float HeatShellMass, HeatShellLength, HeatShellPenetration, HeatShellBlast;
    public ushort HeatShellConstant;

    
    public float LauncherPenetration, LauncherBlast;
    public ushort LauncherConstant;

    
    
    
    public const float RocketReferenceSpeed = 600f;
    public float RocketMass, RocketFiller, RocketHePower, RocketTbPower, RocketTbRadius;
    public ushort RocketHeConstant, RocketTbConstant;

    
    public float RocketHeatPenetration, RocketHeatBlast;
    public ushort RocketHeatConstant;

    
    
    public const float ApReferenceSpeed = 800f;
    public float ApMass, ApLength, ApPenetration;
    public ushort ApConstant;

    
    public float ApheMass, ApheLength, AphePenetration, ApheFiller, ApheBlast;
    public ushort ApheConstant;

    
    public float HeshMass, HeshLength, HeshFiller, HeshPower;
    public ushort HeshConstant;

    
    public float AtgmHeFiller, AtgmHePower, AtgmTbPower, AtgmTbRadius;
    public ushort AtgmHeConstant, AtgmTbConstant;
    public float LauncherHeFiller, LauncherHePower, LauncherTbPower, LauncherTbRadius;
    public ushort LauncherHeConstant, LauncherTbConstant;

    public static ShellSpec For(int caliber)
    {
        var s = new ShellSpec { Caliber = caliber };
        float cal = caliber;
        s.FullBoreMass = 1.59e-5f * cal * cal * cal;

        s.RodDiameter = Math.Clamp(0.2f * cal, 4f, 40f);
        s.RodLength = Math.Min(23f * s.RodDiameter, 6f * cal);
        s.RodMass = TungstenDensity * MathF.PI / 4f * Sq(s.RodDiameter / 1000f) * (s.RodLength / 1000f) * 1.08f;
        var launchMass = Math.Clamp(1.5f * s.RodMass / s.FullBoreMass, 0.12f, 1f); 
        s.LaunchLength = 3f * cal * launchMass / 1000f;
        s.RodConstant = ToConstant(Plugin.ApfsdsConstant.Value);

        s.HeMass = 0.82f * s.FullBoreMass;
        s.HeFiller = 0.13f * s.HeMass;
        s.HePower = Math.Max(1f, Plugin.HePowerPerKg.Value * s.HeFiller);
        s.HeConstant = ConstantFor(Math.Max(1f, Plugin.HePenetration.Value * cal), cal, s.HeMass, HeEstimatedSpeed);

        s.JetDiameter = 0.3f * cal;
        s.JetMass = Math.Max(0.1f, 0.8f * Cube(cal / 125f));
        s.HeatPenetration = Math.Max(10f, Plugin.AtgmPenetration.Value * cal);
        s.AtgmConstant = ConstantFor(s.HeatPenetration, s.JetDiameter, s.JetMass, JetSpeed);
        s.AtgmBlast = Math.Max(1f, Plugin.HePowerPerKg.Value * 1.2f * Cube(cal / 125f));

        s.HeatShellMass = 0.6f * s.FullBoreMass;
        s.HeatShellLength = 3.2f * cal / 1000f;
        s.HeatShellPenetration = Math.Max(5f, Plugin.HeatShellPenetration.Value * cal);
        s.HeatShellConstant = ConstantFor(s.HeatShellPenetration, cal, s.HeatShellMass, JetSpeed);
        s.HeatShellBlast = Math.Max(1f, 0.4f * s.HePower);

        s.LauncherPenetration = Math.Max(10f, Plugin.LauncherPenetration.Value * cal);
        s.LauncherConstant = ConstantFor(s.LauncherPenetration, s.JetDiameter, s.JetMass, JetSpeed);
        s.LauncherBlast = Math.Max(1f, Plugin.HePowerPerKg.Value * 2f * Cube(cal / 152f));

        s.RocketMass = Math.Max(2f, 11f * Cube(cal / 80f));
        s.RocketFiller = 0.12f * s.RocketMass;
        s.RocketHePower = Math.Max(1f, Plugin.HePowerPerKg.Value * s.RocketFiller);
        var tb = Math.Clamp(Plugin.ThermobaricPower.Value, 1f, 10f);
        s.RocketTbPower = Math.Max(1f, s.RocketHePower * tb);
        s.RocketTbRadius = 3.2f * MathF.Pow(Math.Max(0.05f, s.RocketFiller * tb), 1f / 3f);
        s.RocketHeConstant = ConstantFor(Math.Max(1f, Plugin.HePenetration.Value * cal), cal, s.RocketMass, RocketReferenceSpeed);
        s.RocketTbConstant = ConstantFor(Math.Max(1f, 0.5f * Plugin.HePenetration.Value * cal), cal, s.RocketMass, RocketReferenceSpeed);
        s.RocketHeatPenetration = Math.Max(10f, Plugin.RocketHeatPenetration.Value * cal);
        s.RocketHeatConstant = ConstantFor(s.RocketHeatPenetration, cal, s.RocketMass, JetSpeed);
        s.RocketHeatBlast = Math.Max(1f, 0.5f * s.RocketHePower);

        s.ApMass = s.FullBoreMass;
        s.ApLength = 2.6f * cal / 1000f;
        s.ApPenetration = Math.Max(1f, Plugin.ApPenetration.Value * cal);
        s.ApConstant = ConstantFor(s.ApPenetration, cal, s.ApMass, ApReferenceSpeed);

        s.ApheMass = 0.95f * s.FullBoreMass;
        s.ApheLength = 2.7f * cal / 1000f;
        s.AphePenetration = Math.Max(1f, Plugin.AphePenetration.Value * cal);
        s.ApheFiller = 0.025f * s.ApheMass;
        s.ApheBlast = Math.Max(1f, Plugin.HePowerPerKg.Value * s.ApheFiller);
        s.ApheConstant = ConstantFor(s.AphePenetration, cal, s.ApheMass, ApReferenceSpeed);

        s.HeshMass = 0.75f * s.FullBoreMass;
        s.HeshLength = 3f * cal / 1000f;
        s.HeshFiller = 0.2f * s.HeshMass;
        s.HeshPower = Math.Max(1f, Plugin.HePowerPerKg.Value * s.HeshFiller);
        s.HeshConstant = ConstantFor(Math.Max(1f, 0.3f * cal), cal, s.HeshMass, 700f);

        s.AtgmHeFiller = 4f * Cube(cal / 125f);
        s.AtgmHePower = Math.Max(1f, Plugin.HePowerPerKg.Value * s.AtgmHeFiller);
        s.AtgmTbPower = Math.Max(1f, s.AtgmHePower * tb);
        s.AtgmTbRadius = 3.2f * MathF.Pow(Math.Max(0.05f, s.AtgmHeFiller * tb), 1f / 3f);
        s.AtgmHeConstant = ConstantFor(Math.Max(1f, 0.5f * cal), s.JetDiameter, s.JetMass, JetSpeed);
        s.AtgmTbConstant = ConstantFor(Math.Max(1f, 0.25f * cal), s.JetDiameter, s.JetMass, JetSpeed);
        s.LauncherHeFiller = 6f * Cube(cal / 152f);
        s.LauncherHePower = Math.Max(1f, Plugin.HePowerPerKg.Value * s.LauncherHeFiller);
        s.LauncherTbPower = Math.Max(1f, s.LauncherHePower * tb);
        s.LauncherTbRadius = 3.2f * MathF.Pow(Math.Max(0.05f, s.LauncherHeFiller * tb), 1f / 3f);
        s.LauncherHeConstant = ConstantFor(Math.Max(1f, 0.5f * cal), s.JetDiameter, s.JetMass, JetSpeed);
        s.LauncherTbConstant = ConstantFor(Math.Max(1f, 0.25f * cal), s.JetDiameter, s.JetMass, JetSpeed);
        return s;
    }

    
    public static float Penetration(float diameterMm, float mass, float speed, float constant) =>
        100f * MathF.Pow(speed * MathF.Sqrt(mass) / (MathF.Pow(diameterMm / 100f, 0.75f) * constant), 1.43f);

    
    public float MissileBlast(int kind) => kind switch
    {
        Kind.AtgmHe => AtgmHePower, Kind.AtgmTb => AtgmTbPower, Kind.LauncherHe => LauncherHePower, Kind.LauncherTb => LauncherTbPower,
        Kind.RocketHe => RocketHePower, Kind.RocketTb => RocketTbPower, Kind.RocketHeat => RocketHeatBlast,
        Kind.Launcher => LauncherBlast, _ => AtgmBlast
    };

    public float FireballRadius(int kind) => kind switch
    {
        Kind.AtgmTb => AtgmTbRadius, Kind.LauncherTb => LauncherTbRadius, _ => RocketTbRadius
    };

    
    public float Filler(int kind) => kind switch
    {
        Kind.Hesh => HeshFiller, Kind.Aphe => ApheFiller, Kind.RocketHe => RocketFiller,
        Kind.AtgmHe => AtgmHeFiller, Kind.LauncherHe => LauncherHeFiller, _ => HeFiller
    };

    
    
    public static float EstimateSpeed(float gunMuzzleVelocity, float lengthMeters, int caliber) =>
        gunMuzzleVelocity * MathF.Sqrt(2.58f / Math.Max(0.5f, lengthMeters * 1000f / Math.Max(1, caliber)));

    private static ushort ConstantFor(float penetration, float diameterMm, float mass, float speed)
    {
        var c = speed * MathF.Sqrt(mass) / (MathF.Pow(penetration / 100f, 1f / 1.43f) * MathF.Pow(diameterMm / 100f, 0.75f));
        return ToConstant(c);
    }

    private static ushort ToConstant(float c) => (ushort)Math.Clamp((int)MathF.Round(c), 50, 65000);

    private static float Sq(float x) => x * x;
    private static float Cube(float x) => x * x * x;
}
