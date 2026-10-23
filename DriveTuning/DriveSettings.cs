using System;
using System.Globalization;
using System.Text.RegularExpressions;
using Sprocket.Blueprints;

namespace DriveTuning;






internal struct DriveSettings
{
    public float Speed, Accel, AzSpeed, AzAccel;

    public bool Any => Speed > 0f || Accel > 0f || AzSpeed > 0f || AzAccel > 0f;

    private static readonly Regex Token = new(@"\s?\[drive:([0-9.,]*)\]", RegexOptions.Compiled);

    public static DriveSettings Read(Blueprint bp)
    {
        var s = new DriveSettings();
        string d = null;
        try { d = bp?.Description; }
        catch { }
        if (string.IsNullOrEmpty(d)) return s;
        var m = Token.Match(d);
        if (!m.Success) return s;
        var parts = m.Groups[1].Value.Split(',');
        float Get(int i) => i < parts.Length && float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0f ? v : 0f;
        s.Speed = Get(0);
        s.Accel = Get(1);
        s.AzSpeed = Get(2);
        s.AzAccel = Get(3);
        return s;
    }

    
    public static string With(string description, DriveSettings s, bool laying)
    {
        var d = description ?? "";
        d = Token.Replace(d, "");
        if (!s.Any) return d.Length == 0 ? null : d;
        var token = laying
            ? string.Format(CultureInfo.InvariantCulture, "[drive:{0:0.#},{1:0.#},{2:0.#},{3:0.#}]", s.Speed, s.Accel, s.AzSpeed, s.AzAccel)
            : string.Format(CultureInfo.InvariantCulture, "[drive:{0:0.#},{1:0.#}]", s.Speed, s.Accel);
        return d.Length == 0 ? token : d + " " + token;
    }
}
