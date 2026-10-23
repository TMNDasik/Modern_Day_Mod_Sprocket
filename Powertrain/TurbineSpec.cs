using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Il2CppInterop.Runtime;
using Sprocket.Vehicles.Engines;

namespace Powertrain;






internal sealed class TurbineSpec
{
    public const float MinPower = 100f, MaxPower = 3000f;
    public const float MinInertia = 0.5f, MaxInertia = 100f;
    public const float MinRatio = 2f, MaxRatio = 30f;
    public const int MinSafe = 1000, MaxSafe = 6000;
    public const int MinIdle = 300, MaxIdle = 1500;
    public const int MinLimit = 800, MaxLimit = 6000;
    public const int MinUpshift = 600, MaxUpshift = 5800;
    public const int MinOperating = 500, MaxOperating = 5500;
    public const int IdleGap = 50;

    public bool Enabled;
    public float Power = 1100f, Inertia = 10f, Ratio = 7.5f;
    public int Safe = 3600, Idle = 900, Limit = 3400, Upshift = 3300, Operating = 2000;
    public bool Override;

    public bool HasBackup;
    public byte BackupCylinders;
    public float BackupVolume;
    public ushort BackupUpshift, BackupDownshift;
    public short BackupRevLimit;

    public TurbineSpec Clone() => (TurbineSpec)MemberwiseClone();

    private static readonly Regex Token = new(@"\s?\[gte:([^\]]*)\]", RegexOptions.Compiled);

    public static TurbineSpec Parse(string description)
    {
        if (string.IsNullOrEmpty(description)) return null;
        var m = Token.Match(description);
        if (!m.Success) return null;
        var halves = m.Groups[1].Value.Split('|');
        var p = halves[0].Split(',');
        float F(int i, float def) => i < p.Length && float.TryParse(p[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : def;
        var s = new TurbineSpec();
        s.Enabled = F(0, 0) > 0.5f;
        s.Power = Math.Clamp(F(1, s.Power), MinPower, MaxPower);
        s.Inertia = Math.Clamp(F(2, s.Inertia), MinInertia, MaxInertia);
        s.Ratio = Math.Clamp(F(3, s.Ratio), MinRatio, MaxRatio);
        s.Safe = (int)Math.Clamp(F(4, s.Safe), MinSafe, MaxSafe);
        s.Idle = (int)Math.Clamp(F(5, s.Idle), MinIdle, MaxIdle);
        s.Limit = (int)Math.Clamp(F(6, s.Limit), MinLimit, MaxLimit);
        s.Upshift = (int)Math.Clamp(F(7, s.Upshift), MinUpshift, MaxUpshift);
        s.Operating = (int)Math.Clamp(F(8, s.Operating), MinOperating, MaxOperating);
        s.Override = F(9, 0) > 0.5f;
        s.Normalize(-1);
        if (halves.Length > 1)
        {
            var b = halves[1].Split(',');
            if (b.Length >= 5
                && byte.TryParse(b[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var cc)
                && float.TryParse(b[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var cv)
                && ushort.TryParse(b[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var up)
                && ushort.TryParse(b[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var dn)
                && short.TryParse(b[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var rl))
            {
                s.HasBackup = true;
                s.BackupCylinders = cc;
                s.BackupVolume = cv;
                s.BackupUpshift = up;
                s.BackupDownshift = dn;
                s.BackupRevLimit = rl;
            }
        }
        return s;
    }

    public string Format()
    {
        var t = string.Format(CultureInfo.InvariantCulture, "[gte:{0},{1:0.#},{2:0.##},{3:0.##},{4},{5},{6},{7},{8},{9}",
            Enabled ? 1 : 0, Power, Inertia, Ratio, Safe, Idle, Limit, Upshift, Operating, Override ? 1 : 0);
        if (HasBackup)
            t += string.Format(CultureInfo.InvariantCulture, "|{0},{1:0.####},{2},{3},{4}",
                BackupCylinders, BackupVolume, BackupUpshift, BackupDownshift, BackupRevLimit);
        return t + "]";
    }

    
    public static string With(string description, TurbineSpec s)
    {
        var d = Token.Replace(description ?? "", "");
        if (s == null) return d.Length == 0 ? null : d;
        var token = s.Format();
        return d.Length == 0 ? token : d + " " + token;
    }

    
    
    
    public void Normalize(int changed)
    {
        var v = new[] { Idle, Operating, Upshift, Limit, Safe };
        var min = new[] { MinIdle, MinOperating, MinUpshift, MinLimit, MinSafe };
        var max = new[] { MaxIdle, MaxOperating, MaxUpshift, MaxLimit, MaxSafe };
        var active = new[] { true, true, true, Override, true };
        for (var i = 0; i < 5; i++) v[i] = Math.Clamp(v[i], min[i], max[i]);
        int Gap(int lower) => lower == 0 ? IdleGap : 0;

        var start = changed < 0 ? 0 : changed;
        
        var prev = start;
        for (var k = start + 1; k < 5; k++)
        {
            if (!active[k]) continue;
            if (active[prev]) v[k] = Math.Max(v[k], v[prev] + Gap(prev));
            prev = k;
        }
        
        var next = start;
        for (var k = start - 1; k >= 0; k--)
        {
            if (!active[k]) continue;
            if (active[next]) v[k] = Math.Min(v[k], v[next] - Gap(k));
            next = k;
        }
        for (var i = 0; i < 5; i++) v[i] = Math.Clamp(v[i], min[i], max[i]);
        
        for (var k = 1; k < 5; k++)
        {
            if (!active[k]) continue;
            var lower = k - 1;
            while (lower > 0 && !active[lower]) lower--;
            v[k] = Math.Max(v[k], v[lower] + Gap(lower));
        }
        Idle = v[0];
        Operating = v[1];
        Upshift = v[2];
        Limit = active[3] ? v[3] : Limit;
        Safe = v[4];
        if (!Override) Limit = Math.Clamp(Limit, MinLimit, MaxLimit);
    }
}


internal static unsafe class SpecCache
{
    private const int DescriptionOffset = 0x18;
    private static readonly Dictionary<IntPtr, (IntPtr Desc, TurbineSpec Spec)> Cache = new();

    
    public static TurbineSpec GetAny(EngineBlueprint bp)
    {
        if (bp == null) return null;
        var p = bp.Pointer;
        if (p == IntPtr.Zero) return null;
        var d = *(IntPtr*)(p + DescriptionOffset);
        if (Cache.TryGetValue(p, out var e) && e.Desc == d) return e.Spec;
        TurbineSpec spec = null;
        try { spec = d == IntPtr.Zero ? null : TurbineSpec.Parse(IL2CPP.Il2CppStringToManaged(d)); }
        catch (Exception ex) { if (Plugin.DebugLogging.Value) Plugin.Log.LogWarning("Turbine token parse failed: " + ex.Message); }
        if (Cache.Count > 1024) Cache.Clear();
        Cache[p] = (d, spec);
        return spec;
    }

    
    public static TurbineSpec Get(EngineBlueprint bp)
    {
        var s = GetAny(bp);
        return s != null && s.Enabled ? s : null;
    }
}
