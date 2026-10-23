using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace ActiveProtection;



internal enum ModuleKind { Detector = 1, Launcher = 2, Warner = 3, Smoke = 4, Jammer = 5, Era = 6, Panel = 7, RadioJammer = 8 }


[Flags]
internal enum EoThreats { None = 0, Rangefinder = 1, Beam = 2, Launch = 4, All = 7 }

internal enum JammerMode { AlwaysOn = 0, OnWarning = 1, Off = 2 }

internal static class EoInfo
{
    public static readonly EoThreats[] Each = { EoThreats.Rangefinder, EoThreats.Beam, EoThreats.Launch };

    public static string Name(EoThreats t) => t switch
    {
        EoThreats.Rangefinder => "Laser rangefinders",
        EoThreats.Beam => "Missile guidance lasers",
        EoThreats.Launch => "Missile launches",
        _ => "?"
    };

    
    public static string Label(EoThreats t) => t switch
    {
        EoThreats.Rangefinder => "Rangefinders",
        EoThreats.Beam => "Guide lasers",
        EoThreats.Launch => "Launches",
        _ => "?"
    };

    public static string Abbrev(ModuleKind k) => k switch
    {
        ModuleKind.Detector => "Detector",
        ModuleKind.Launcher => "Launcher",
        ModuleKind.Warner => "Sensor",
        ModuleKind.Smoke => "Grenades",
        ModuleKind.Jammer => "IR jammer",
        ModuleKind.Era => "ERA",
        ModuleKind.Panel => "Panel",
        ModuleKind.RadioJammer => "EW",
        _ => "?"
    };

    public static string Short(EoThreats t) => t switch
    {
        EoThreats.Rangefinder => "LASER RANGEFINDER",
        EoThreats.Beam => "GUIDANCE LASER",
        EoThreats.Launch => "MISSILE LAUNCH",
        _ => "THREAT"
    };

    public static string Tooltip(EoThreats t) => t switch
    {
        EoThreats.Rangefinder => "A laser rangefinder measuring the range to this vehicle: the player's (thermal sight rangefinder) " +
                                 "or an enemy gunner who keeps his gun laid on the vehicle (modern fire control lases before firing).",
        EoThreats.Beam => "The laser beam of a beam-riding missile (Kornet, Refleks, Svir) pointed at this vehicle.",
        EoThreats.Launch => "The flash of a missile launch (ultraviolet/IR missile warner, like MUSS).",
        _ => ""
    };

    public static readonly string[] JammerModes = { "Always on in battle", "On missile warning", "Off" };

    public static bool IsAps(ModuleKind k) => k == ModuleKind.Detector || k == ModuleKind.Launcher;

    
    public static ModuleKind? LinkTarget(ModuleKind k) => k switch
    {
        ModuleKind.Launcher => ModuleKind.Detector,
        ModuleKind.Smoke => ModuleKind.Warner,
        ModuleKind.Jammer => ModuleKind.Warner,
        _ => null
    };

    public static string Title(ModuleKind k) => k switch
    {
        ModuleKind.Detector => "APS Detector",
        ModuleKind.Launcher => "APS Launcher",
        ModuleKind.Warner => "EO Sensor",
        ModuleKind.Smoke => "Aerosol Grenade Launcher",
        ModuleKind.Jammer => "IR Jammer",
        ModuleKind.Era => "ERA Block",
        ModuleKind.Panel => "Blow-off Panel",
        ModuleKind.RadioJammer => "Radio Jammer",
        _ => "?"
    };
}

[Flags]
internal enum ThreatTypes { None = 0, Atgm = 1, He = 2, Ap = 4, Apfsds = 8, All = 15 }

internal enum TargetPriority { Nearest = 0, Soonest = 1, Dangerous = 2 }

internal static class ThreatInfo
{
    public static readonly ThreatTypes[] Each = { ThreatTypes.Atgm, ThreatTypes.He, ThreatTypes.Ap, ThreatTypes.Apfsds };

    public static string Name(ThreatTypes t) => t switch
    {
        ThreatTypes.Atgm => "ATGM",
        ThreatTypes.He => "HE/HEAT shells",
        ThreatTypes.Ap => "AP shells",
        ThreatTypes.Apfsds => "APFSDS",
        _ => "?"
    };

    public static string Tooltip(ThreatTypes t) => t switch
    {
        ThreatTypes.Atgm => "Guided anti-tank missiles (gun-launched and launcher tubes). Slow, easy to hit; the warhead is destroyed.",
        ThreatTypes.He => "High-explosive and HEAT shells. The shell is set off far from the armour, so the shaped-charge jet does not reach it.",
        ThreatTypes.Ap => "Full-calibre armour-piercing shells (AP, APHE). Too heavy to destroy: a hit yaws and slows the shell, so it penetrates less.",
        ThreatTypes.Apfsds => "Long-rod darts at 1500-1800 m/s. Very hard to catch (raise the launcher's max speed); a hit yaws and slows the rod.",
        _ => ""
    };

    public static string[] PriorityNames = { "Nearest", "First to hit the vehicle", "Most dangerous" };
}





internal sealed class ApsSettings
{
    public ModuleKind Kind;
    public float Azimuth, Elevation;
    public bool Full360;
    public float HArc, VArc;
    public ThreatTypes Types;

    
    public float Range;

    
    public int Detector;          
    public bool LinkSet;          
    public float MinDist, MaxDist, MinSpeed, MaxSpeed, Reaction, Pause;
    public TargetPriority Priority;
    public int Charges, PerTarget;

    
    public EoThreats Eo;          
    public bool Warn;             
    public float ScreenDist, BuildTime, Duration;   
    public JammerMode Mode;       
    public float JamChance;       

    
    public float EraWidth, EraHeight, EraThickness;   
    public float TileWidth, TileHeight;               
    public float CutHeat, CutKinetic, CutTandem;      
    public float TriggerCaliber;                      
    public float HeadOn;                              
    public float ArealMass;                           

    
    public float PanelWidth, PanelLength, PanelThickness;   
    public int[] Racks = Array.Empty<int>();                 
    public bool AutoRacks;                                    
    public float DoorTime;                                    

    public const float PanelSizeMin = 100f, PanelSizeMax = 4000f;
    public const float PanelThicknessMin = 5f, PanelThicknessMax = 100f;
    public const float DoorTimeMax = 6f;
    public const float SteelDensity = 7850f;                  

    public const float EraSizeMin = 50f, EraSizeMax = 3000f;
    public const float EraThicknessMin = 10f, EraThicknessMax = 400f;
    public const float TileMin = 30f, TileMax = 1500f;
    public const float TriggerMin = 5f, TriggerMax = 200f;
    public const float ArealMassMin = 10f, ArealMassMax = 1500f;
    public const float FullEffectAngle = 60f;

    public const float ScreenDistMin = 15f, ScreenDistMax = 150f;
    public const float BuildMin = 0.3f, BuildMax = 8f;
    public const float DurationMin = 3f, DurationMax = 90f;
    public const int GrenadesMax = 12;
    public const float EoRangeMin = 100f, EoRangeMax = 8000f;

    
    public const float RangeMin = 5f, RangeMax = 500f;
    public const float DistMin = 0.5f, DistMax = 150f;
    public const float SpeedMin = 0f, SpeedMax = 3000f;
    public const float ReactionMin = 0.005f, ReactionMax = 2f;
    public const float PauseMin = 0.02f, PauseMax = 10f;
    public const int ChargesMax = 3;

    public static ApsSettings Default(ModuleKind kind) => kind switch
    {
        ModuleKind.RadioJammer => new ApsSettings { Kind = kind, Range = 350, Full360 = true, HArc = 360, VArc = 180, JamChance = 1, Mode = JammerMode.AlwaysOn },
        ModuleKind.Detector => new ApsSettings { Kind = kind, HArc = 120f, VArc = 60f, Types = ThreatTypes.All, Range = 100f },
        
        ModuleKind.Warner => new ApsSettings { Kind = kind, Elevation = 10f, HArc = 120f, VArc = 30f, Range = 5000f, Eo = EoThreats.All, Warn = true },
        
        ModuleKind.Smoke => new ApsSettings
        {
            Kind = kind, Elevation = 3f, HArc = 60f, VArc = 10f, Eo = EoThreats.All, Charges = 4, PerTarget = 1,
            ScreenDist = 60f, BuildTime = 3f, Duration = 20f, Reaction = 1f, Pause = 3f
        },
        
        ModuleKind.Jammer => new ApsSettings
        {
            Kind = kind, HArc = 40f, VArc = 9f, Range = 2500f, Mode = JammerMode.AlwaysOn, JamChance = 0.75f, Duration = 20f
        },
        
        ModuleKind.Era => new ApsSettings
        {
            Kind = kind, EraWidth = 600f, EraHeight = 400f, EraThickness = 100f, TileWidth = 250f, TileHeight = 130f,
            CutHeat = 0.6f, CutKinetic = 0.3f, CutTandem = 0.2f, TriggerCaliber = 20f, HeadOn = 0.35f, ArealMass = 250f
        },
        
        
        ModuleKind.Panel => new ApsSettings
        {
            Kind = kind, PanelWidth = 1200f, PanelLength = 600f, PanelThickness = 20f, AutoRacks = true, DoorTime = 2f
        },
        _ => new ApsSettings
        {
            Kind = kind, HArc = 90f, VArc = 60f, Types = ThreatTypes.Atgm | ThreatTypes.He | ThreatTypes.Ap,
            MinDist = 3f, MaxDist = 15f, MinSpeed = 70f, MaxSpeed = 1200f, Reaction = 0.04f, Pause = 0.3f,
            Priority = TargetPriority.Soonest, Charges = 1, PerTarget = 1
        }
    };

    public ApsSettings Clone() => (ApsSettings)MemberwiseClone();

    public float HalfH => Full360 ? 180f : Mathf.Clamp(HArc, 1f, 360f) * 0.5f;
    public float ElevationLow => Mathf.Clamp(Elevation - VArc * 0.5f, -90f, 90f);
    public float ElevationHigh => Mathf.Clamp(Elevation + VArc * 0.5f, -90f, 90f);
    
    public const float EoZoneShown = 60f;

    public float Near => Kind switch
    {
        ModuleKind.Launcher => MinDist,
        ModuleKind.Smoke => Mathf.Max(1f, ScreenDist - 3f),
        _ => 0f
    };

    public float Far => Kind switch
    {
        ModuleKind.Detector or ModuleKind.RadioJammer => Range,
        ModuleKind.Launcher => MaxDist,
        ModuleKind.Smoke => ScreenDist + 3f,
        _ => Mathf.Min(Range, EoZoneShown)
    };

    public void Sanitize()
    {
        Azimuth = Mathf.Clamp(Mathf.DeltaAngle(0f, Azimuth), -180f, 180f);
        Elevation = Mathf.Clamp(Elevation, -90f, 90f);
        HArc = Mathf.Clamp(HArc, 1f, 360f);
        if (HArc >= 359.5f) { HArc = 360f; Full360 = true; }
        VArc = Mathf.Clamp(VArc, 1f, 180f);
        Types &= ThreatTypes.All;
        Eo &= EoThreats.All;
        Range = Kind is ModuleKind.Warner or ModuleKind.Jammer or ModuleKind.RadioJammer
            ? Mathf.Clamp(Range, EoRangeMin, EoRangeMax)
            : Mathf.Clamp(Range, RangeMin, RangeMax);
        MinDist = Mathf.Clamp(MinDist, DistMin, DistMax - 0.5f);
        MaxDist = Mathf.Clamp(MaxDist, MinDist + 0.5f, DistMax);
        MinSpeed = Mathf.Clamp(MinSpeed, SpeedMin, SpeedMax - 10f);
        MaxSpeed = Mathf.Clamp(MaxSpeed, MinSpeed + 10f, SpeedMax);
        if (Kind == ModuleKind.Smoke)
        {
            Reaction = Mathf.Clamp(Reaction, 0.05f, 5f);
            Pause = Mathf.Clamp(Pause, 0.5f, 30f);
            Charges = Math.Clamp(Charges, 1, GrenadesMax);
            PerTarget = Math.Clamp(PerTarget, 1, Math.Min(4, Charges));
            
            Elevation = 3f;
            VArc = 10f;
        }
        else
        {
            Reaction = Mathf.Clamp(Reaction, ReactionMin, ReactionMax);
            Pause = Mathf.Clamp(Pause, PauseMin, PauseMax);
            Charges = Math.Clamp(Charges, 1, ChargesMax);
            PerTarget = Math.Clamp(PerTarget, 1, Charges);
        }
        if ((int)Priority < 0 || (int)Priority > 2) Priority = TargetPriority.Soonest;
        ScreenDist = Mathf.Clamp(ScreenDist <= 0f ? 60f : ScreenDist, ScreenDistMin, ScreenDistMax);
        BuildTime = Mathf.Clamp(BuildTime <= 0f ? 3f : BuildTime, BuildMin, BuildMax);
        Duration = Mathf.Clamp(Duration <= 0f ? 20f : Duration, DurationMin, DurationMax);
        if ((int)Mode < 0 || (int)Mode > 2) Mode = JammerMode.AlwaysOn;
        JamChance = Mathf.Clamp01(JamChance);
        if (Kind == ModuleKind.Era)
        {
            EraWidth = Mathf.Clamp(EraWidth, EraSizeMin, EraSizeMax);
            EraHeight = Mathf.Clamp(EraHeight, EraSizeMin, EraSizeMax);
            EraThickness = Mathf.Clamp(EraThickness, EraThicknessMin, EraThicknessMax);
            TileWidth = Mathf.Clamp(TileWidth, TileMin, TileMax);
            TileHeight = Mathf.Clamp(TileHeight, TileMin, TileMax);
            CutHeat = Mathf.Clamp01(CutHeat);
            CutKinetic = Mathf.Clamp01(CutKinetic);
            CutTandem = Mathf.Clamp01(CutTandem);
            TriggerCaliber = Mathf.Clamp(TriggerCaliber, TriggerMin, TriggerMax);
            HeadOn = Mathf.Clamp01(HeadOn);
            ArealMass = Mathf.Clamp(ArealMass, ArealMassMin, ArealMassMax);
            Azimuth = 0f;
        }
        if (Kind == ModuleKind.Panel)
        {
            PanelWidth = Mathf.Clamp(PanelWidth, PanelSizeMin, PanelSizeMax);
            PanelLength = Mathf.Clamp(PanelLength, PanelSizeMin, PanelSizeMax);
            PanelThickness = Mathf.Clamp(PanelThickness, PanelThicknessMin, PanelThicknessMax);
            DoorTime = Mathf.Clamp(DoorTime, 0f, DoorTimeMax);
            Racks ??= Array.Empty<int>();
            Azimuth = 0f;
        }
    }

    
    
    public Vector3 Scale = Vector3.one;

    
    public Vector3 PanelSize => new(PanelWidth * Scale.x / 1000f, PanelLength * Scale.y / 1000f, PanelThickness * Scale.z / 1000f);
    public float PanelMass => PanelSize.x * PanelSize.y * PanelSize.z * SteelDensity;

    
    public float PartMass => Kind switch
    {
        ModuleKind.Era => EraMass,
        ModuleKind.Panel => PanelMass,
        _ => 0f
    };

    public bool Protects(int rackVuid)
    {
        foreach (var r in Racks) if (r == rackVuid) return true;
        return false;
    }

    
    public Vector3 EraSize => new(EraWidth * Scale.x / 1000f, EraHeight * Scale.y / 1000f, EraThickness * Scale.z / 1000f);

    
    public int TilesX => Mathf.Max(1, Mathf.CeilToInt(EraWidth * Scale.x / Mathf.Max(1f, TileWidth) - 0.001f));
    public int TilesY => Mathf.Max(1, Mathf.CeilToInt(EraHeight * Scale.y / Mathf.Max(1f, TileHeight) - 0.001f));

    
    public float EraMass => EraSize.x * EraSize.y * ArealMass;

    
    public static Vector3 MinScale(ModuleKind k) => k == ModuleKind.Panel
        ? new Vector3(PanelSizeMin / 1200f, PanelSizeMin / 600f, PanelThicknessMin / 20f)
        : new Vector3(EraSizeMin / 600f, EraSizeMin / 400f, EraThicknessMin / 100f);

    public static Vector3 MaxScale(ModuleKind k) => k == ModuleKind.Panel
        ? new Vector3(PanelSizeMax / 1200f, PanelSizeMax / 600f, PanelThicknessMax / 20f)
        : new Vector3(EraSizeMax / 600f, EraSizeMax / 400f, EraThicknessMax / 100f);

    
    public bool InSector(Vector3 localDir)
    {
        var len = localDir.magnitude;
        if (len < 1e-6f) return true;
        var el = Mathf.Asin(Mathf.Clamp(localDir.y / len, -1f, 1f)) * Mathf.Rad2Deg;
        if (el < ElevationLow - 0.01f || el > ElevationHigh + 0.01f) return false;
        if (Full360) return true;
        
        if (Mathf.Abs(el) > 89.9f) return true;
        var az = Mathf.Atan2(localDir.x, localDir.z) * Mathf.Rad2Deg;
        return Mathf.Abs(Mathf.DeltaAngle(Azimuth, az)) <= HalfH + 0.01f;
    }

    
    public static Vector3 Dir(float azDeg, float elDeg)
    {
        var a = azDeg * Mathf.Deg2Rad;
        var e = elDeg * Mathf.Deg2Rad;
        var c = Mathf.Cos(e);
        return new Vector3(c * Mathf.Sin(a), Mathf.Sin(e), c * Mathf.Cos(a));
    }

    
    public ApsSettings Mirrored()
    {
        var c = Clone();
        c.Azimuth = Mathf.Clamp(Mathf.DeltaAngle(0f, -Azimuth), -180f, 180f);
        return c;
    }

    

    public string ToToken()
    {
        var sb = new StringBuilder("aps1");
        void Add(string key, float v) => sb.Append(';').Append(key).Append('=').Append(v.ToString("0.###", CultureInfo.InvariantCulture));
        void AddI(string key, int v) => sb.Append(';').Append(key).Append('=').Append(v.ToString(CultureInfo.InvariantCulture));
        sb.Append(";k=").Append(KindCode(Kind));
        Add("az", Azimuth);
        Add("el", Elevation);
        AddI("full", Full360 ? 1 : 0);
        Add("h", HArc);
        Add("v", VArc);
        if (EoInfo.IsAps(Kind)) AddI("t", (int)Types);
        if (Kind == ModuleKind.Detector)
        {
            Add("r", Range);
        }
        else if (Kind == ModuleKind.Warner)
        {
            Add("r", Range);
            AddI("eo", (int)Eo);
            AddI("wn", Warn ? 1 : 0);
        }
        else if (Kind == ModuleKind.Smoke)
        {
            AddI("det", Detector);
            AddI("lk", LinkSet ? 1 : 0);
            AddI("eo", (int)Eo);
            AddI("ch", Charges);
            AddI("pt", PerTarget);
            Add("sd", ScreenDist);
            Add("bt", BuildTime);
            Add("du", Duration);
            Add("re", Reaction);
            Add("pa", Pause);
        }
        else if (Kind is ModuleKind.Jammer or ModuleKind.RadioJammer)
        {
            AddI("det", Detector);
            AddI("lk", LinkSet ? 1 : 0);
            Add("r", Range);
            AddI("md", (int)Mode);
            Add("jc", JamChance);
            Add("du", Duration);
        }
        else if (Kind == ModuleKind.Era)
        {
            Add("ew", EraWidth);
            Add("eh", EraHeight);
            Add("et", EraThickness);
            Add("tw", TileWidth);
            Add("th", TileHeight);
            Add("rh", CutHeat);
            Add("rk", CutKinetic);
            Add("rt", CutTandem);
            Add("tc", TriggerCaliber);
            Add("ho", HeadOn);
            Add("kg", ArealMass);
        }
        else if (Kind == ModuleKind.Panel)
        {
            Add("pw", PanelWidth);
            Add("pl", PanelLength);
            Add("pd", PanelThickness);
            AddI("ar", AutoRacks ? 1 : 0);
            Add("dr", DoorTime);
            var list = new StringBuilder();
            foreach (var r in Racks)
            {
                if (list.Length > 0) list.Append('/');
                list.Append(r.ToString(CultureInfo.InvariantCulture));
            }
            sb.Append(";rl=").Append(list);
        }
        else
        {
            AddI("det", Detector);
            AddI("lk", LinkSet ? 1 : 0);
            Add("mn", MinDist);
            Add("mx", MaxDist);
            Add("sl", MinSpeed);
            Add("sh", MaxSpeed);
            Add("re", Reaction);
            Add("pa", Pause);
            AddI("pr", (int)Priority);
            AddI("ch", Charges);
            AddI("pt", PerTarget);
        }
        return sb.ToString();
    }

    public static ApsSettings FromToken(string token, ModuleKind kind)
    {
        var s = Default(kind);
        if (string.IsNullOrEmpty(token)) return s;
        var map = new Dictionary<string, string>();
        foreach (var part in token.Split(';'))
        {
            var eq = part.IndexOf('=');
            if (eq > 0) map[part.Substring(0, eq).Trim()] = part.Substring(eq + 1).Trim();
        }
        float F(string key, float def) =>
            map.TryGetValue(key, out var v) && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) && !float.IsNaN(f) ? f : def;
        int I(string key, int def) =>
            map.TryGetValue(key, out var v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : def;

        s.Azimuth = F("az", s.Azimuth);
        s.Elevation = F("el", s.Elevation);
        s.Full360 = I("full", s.Full360 ? 1 : 0) != 0;
        s.HArc = F("h", s.HArc);
        s.VArc = F("v", s.VArc);
        s.Types = (ThreatTypes)I("t", (int)s.Types);
        s.Range = F("r", s.Range);
        s.Detector = I("det", s.Detector);
        s.LinkSet = I("lk", 0) != 0;
        s.MinDist = F("mn", s.MinDist);
        s.MaxDist = F("mx", s.MaxDist);
        s.MinSpeed = F("sl", s.MinSpeed);
        s.MaxSpeed = F("sh", s.MaxSpeed);
        s.Reaction = F("re", s.Reaction);
        s.Pause = F("pa", s.Pause);
        s.Priority = (TargetPriority)I("pr", (int)s.Priority);
        s.Charges = I("ch", s.Charges);
        s.PerTarget = I("pt", s.PerTarget);
        s.Eo = (EoThreats)I("eo", (int)s.Eo);
        s.Warn = I("wn", s.Warn ? 1 : 0) != 0;
        s.ScreenDist = F("sd", s.ScreenDist);
        s.BuildTime = F("bt", s.BuildTime);
        s.Duration = F("du", s.Duration);
        s.Mode = (JammerMode)I("md", (int)s.Mode);
        s.JamChance = F("jc", s.JamChance);
        if (kind == ModuleKind.Era)
        {
            s.EraWidth = F("ew", s.EraWidth);
            s.EraHeight = F("eh", s.EraHeight);
            s.EraThickness = F("et", s.EraThickness);
            s.TileWidth = F("tw", s.TileWidth);
            s.TileHeight = F("th", s.TileHeight);
            s.CutHeat = F("rh", s.CutHeat);
            s.CutKinetic = F("rk", s.CutKinetic);
            s.CutTandem = F("rt", s.CutTandem);
            s.TriggerCaliber = F("tc", s.TriggerCaliber);
            s.HeadOn = F("ho", s.HeadOn);
            s.ArealMass = F("kg", s.ArealMass);
        }
        if (kind == ModuleKind.Panel)
        {
            s.PanelWidth = F("pw", s.PanelWidth);
            s.PanelLength = F("pl", s.PanelLength);
            s.PanelThickness = F("pd", s.PanelThickness);
            s.AutoRacks = I("ar", s.AutoRacks ? 1 : 0) != 0;
            s.DoorTime = F("dr", s.DoorTime);
            var racks = new List<int>();
            if (map.TryGetValue("rl", out var rl))
                foreach (var part in rl.Split('/'))
                    if (int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var vuid) && vuid != 0 && !racks.Contains(vuid))
                        racks.Add(vuid);
            s.Racks = racks.ToArray();
        }
        s.Sanitize();
        return s;
    }

    private static string KindCode(ModuleKind k) => k switch
    {
        ModuleKind.Detector => "d",
        ModuleKind.Launcher => "l",
        ModuleKind.Warner => "w",
        ModuleKind.Smoke => "s",
        ModuleKind.Jammer => "j",
        ModuleKind.Era => "e",
        ModuleKind.Panel => "p",
        ModuleKind.RadioJammer => "rf",
        _ => "?"
    };

    
    public bool SameSector(ApsSettings o)
    {
        var a = Clone();
        var b = o.Clone();
        a.Detector = b.Detector = 0;
        a.LinkSet = b.LinkSet = false;
        return a.ToToken() == b.ToToken();
    }

    public static ModuleKind? KindOfToken(string token)
    {
        if (string.IsNullOrEmpty(token)) return null;
        if (token.Contains(";k=d;")) return ModuleKind.Detector;
        if (token.Contains(";k=l;")) return ModuleKind.Launcher;
        if (token.Contains(";k=w;")) return ModuleKind.Warner;
        if (token.Contains(";k=s;")) return ModuleKind.Smoke;
        if (token.Contains(";k=j;")) return ModuleKind.Jammer;
        if (token.Contains(";k=e;")) return ModuleKind.Era;
        if (token.Contains(";k=p;")) return ModuleKind.Panel;
        if (token.Contains(";k=rf;")) return ModuleKind.RadioJammer;
        return null;
    }
}
