using System;
using System.Globalization;
using UnityEngine;

namespace GunStabilizer;



internal sealed class FcsSettings
{
    public const int Analog = 1, Digital = 2;
    public const float MaxRangeMin = 1000f, MaxRangeMax = 10000f;

    public int Gen = Digital;
    public bool Laser = true;          
    public float MaxRange = 5000f;     
    public bool LastReturn;            
    public bool Auto = true;           
    public bool Lead = true;           
    public bool Interlock;             
    public bool Damageable = true;     

    
    public static readonly FcsSettings Legacy = new() { Gen = Digital, Laser = true, MaxRange = 0f, Lead = false, Interlock = false, Damageable = false };

    public bool IsAnalog => Gen == Analog;

    
    public float RangeError => IsAnalog ? 5f : 2.5f;
    public float LaserCooldown => IsAnalog ? 3f : 1f;
    public float MinRange => IsAnalog ? 200f : 50f;
    public float ComputeTime => IsAnalog ? 1f : 0.35f;

    public string Label => IsAnalog ? "Analog (1970s)" : "Digital (1980s+)";

    public string Summary => IsAnalog
        ? "Ruby laser and an analog ballistic computer. Range 200 m and up, about ±10 m; the laser recharges for 3 s; " +
          "the solution takes 1 s. Lead: keep the sight on a moving target for a moment, lase — the gun leads it sideways " +
          "until the next measurement or until you stop tracking."
        : "Nd:YAG laser and a digital ballistic computer. Range 50 m and up, about ±5 m; one measurement per second. " +
          "Lead: keep the sight on a moving target for a second, lase — the gun leads it sideways until the next " +
          "measurement or until you stop tracking.";

    public FcsSettings Clone() => (FcsSettings)MemberwiseClone();

    public void Sanitize()
    {
        if (Gen != Analog && Gen != Digital) Gen = Digital;
        MaxRange = float.IsNaN(MaxRange) || float.IsInfinity(MaxRange) ? 5000f : Mathf.Clamp(MaxRange, MaxRangeMin, MaxRangeMax);
    }

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public string ToToken() => string.Format(Inv, "fcs1;gen={0};laser={1};max={2:0};last={3};auto={4};lead={5};lock={6};dmg={7}",
        Gen, B(Laser), MaxRange, B(LastReturn), B(Auto), B(Lead), B(Interlock), B(Damageable));

    private static int B(bool v) => v ? 1 : 0;

    public static FcsSettings FromToken(string token)
    {
        var s = new FcsSettings();
        if (string.IsNullOrEmpty(token)) return s;
        foreach (var part in token.Split(';'))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0) continue;
            var key = part.Substring(0, eq).Trim();
            if (!float.TryParse(part.Substring(eq + 1).Trim(), NumberStyles.Float, Inv, out var f)) continue;
            switch (key)
            {
                case "gen": s.Gen = (int)Math.Round(f); break;
                case "laser": s.Laser = f > 0.5f; break;
                case "max": s.MaxRange = f; break;
                case "last": s.LastReturn = f > 0.5f; break;
                case "auto": s.Auto = f > 0.5f; break;
                case "lead": s.Lead = f > 0.5f; break;
                case "lock": s.Interlock = f > 0.5f; break;
                case "dmg": s.Damageable = f > 0.5f; break;
            }
        }
        s.Sanitize();
        return s;
    }
}
