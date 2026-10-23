using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace ActiveProtection;




internal sealed class EraElement
{
    public bool Explosive;
    public float X, Y, Depth, W = 90f, H = 90f, T;

    public EraElement Clone() => (EraElement)MemberwiseClone();
}




internal sealed class EraFace
{
    public Vector3 Center, Out, Up;
    public float W, H, D;
    public bool Mirror;

    public Vector3 Side => Vector3.Cross(Up, Out).normalized;
    public float Area => W * H;
}











internal sealed class EraConfig
{
    public const float HeadOn = 0.35f;
    public const float FullEffectAngle = ApsSettings.FullEffectAngle;
    public const float TriggerMin = ApsSettings.TriggerMin, TriggerMax = 100f, TriggerDefault = 20f;
    public const float ThicknessMin = 1f, ThicknessMax = 100f, SizeMinPct = 5f, SizeMaxPct = 150f;

    public bool On = true;
    public float Trigger = TriggerDefault;
    public readonly List<EraElement> Elements = new();
    public List<EraFace> Panels = new();
    
    
    public Vector3 PartMin, PartMax, MirMin, MirMax;

    public bool HasPartBox => PartMax.x > PartMin.x && PartMax.y > PartMin.y && PartMax.z > PartMin.z;
    public bool HasMirrorBox => MirMax.x > MirMin.x && MirMax.y > MirMin.y && MirMax.z > MirMin.z;

    private static bool InBox(Vector3 local, Vector3 min, Vector3 max, float margin)
    {
        var p = local * 1000f;
        var m = margin * 1000f;
        return p.x >= min.x - m && p.y >= min.y - m && p.z >= min.z - m && p.x <= max.x + m && p.y <= max.y + m && p.z <= max.z + m;
    }

    public bool InsidePart(Vector3 local, float margin) =>
        InBox(local, PartMin, PartMax, margin) || (HasMirrorBox && InBox(local, MirMin, MirMax, margin));

    
    public bool InHalf(Vector3 local, bool mirrorHalf, float margin)
    {
        if (!HasMirrorBox) return InBox(local, PartMin, PartMax, margin);
        var (min, max, oMin, oMax) = mirrorHalf ? (MirMin, MirMax, PartMin, PartMax) : (PartMin, PartMax, MirMin, MirMax);
        if (!InBox(local, min, max, margin)) return false;
        if (!InBox(local, oMin, oMax, margin)) return true;
        var p = local * 1000f;
        return (p - (min + max) * 0.5f).sqrMagnitude <= (p - (oMin + oMax) * 0.5f).sqrMagnitude;
    }

    public bool HasGeometry => Panels.Count > 0;

    public int Count(bool explosive)
    {
        var n = 0;
        foreach (var e in Elements) if (e.Explosive == explosive) n++;
        return n;
    }

    
    public int NumberOf(int index)
    {
        var n = 0;
        for (var i = 0; i <= index && i < Elements.Count; i++)
            if (Elements[i].Explosive == Elements[index].Explosive) n++;
        return n;
    }

    public float MinDepth
    {
        get
        {
            var d = float.MaxValue;
            foreach (var p in Panels) d = Mathf.Min(d, p.D);
            return d == float.MaxValue ? 20f : d;
        }
    }

    public float MaxDepth
    {
        get
        {
            var d = 20f;
            foreach (var p in Panels) d = Mathf.Max(d, p.D);
            return d + 50f;
        }
    }

    public EraConfig Clone()
    {
        
        var copy = new EraConfig
        {
            On = On, Trigger = Trigger, Panels = Panels, PartMin = PartMin, PartMax = PartMax, MirMin = MirMin, MirMax = MirMax
        };
        foreach (var e in Elements) copy.Elements.Add(e.Clone());
        return copy;
    }

    
    public EraConfig Mirrored()
    {
        var c = Clone();
        foreach (var e in c.Elements) e.X = -e.X;
        return c;
    }

    

    
    public void AddDefaults()
    {
        const float total = 12f;
        var start = Mathf.Round(Mathf.Max(0f, (MinDepth - total) * 0.5f));
        Elements.Add(new EraElement { Explosive = false, Depth = start, T = 3f });
        Elements.Add(new EraElement { Explosive = true, Depth = start + 3f, T = 6f });
        Elements.Add(new EraElement { Explosive = false, Depth = start + 9f, T = 3f });
    }

    
    public EraElement NewElement(bool explosive)
    {
        var deepest = 0f;
        foreach (var e in Elements) deepest = Mathf.Max(deepest, e.Depth + e.T);
        return new EraElement { Explosive = explosive, Depth = deepest, T = explosive ? 6f : 3f };
    }

    public void Sanitize()
    {
        Trigger = Mathf.Clamp(Mathf.Round(Trigger), TriggerMin, TriggerMax);
        foreach (var e in Elements)
        {
            e.T = Mathf.Clamp(e.T, ThicknessMin, ThicknessMax);
            e.W = Mathf.Clamp(e.W, SizeMinPct, SizeMaxPct);
            e.H = Mathf.Clamp(e.H, SizeMinPct, SizeMaxPct);
            e.X = Mathf.Clamp(e.X, -100f, 100f);
            e.Y = Mathf.Clamp(e.Y, -100f, 100f);
            e.Depth = Mathf.Max(0f, e.Depth);
        }
        _boundsValid = false;
        _summary = null;
    }

    

    
    public static void Box(EraFace p, EraElement e, out Vector3 center, out Vector3 half)
    {
        var x = (p.Mirror ? -e.X : e.X) * 0.01f * p.W * 0.5f;
        var y = e.Y * 0.01f * p.H * 0.5f;
        var z = -(e.Depth + e.T * 0.5f);
        center = (p.Center + p.Side * x + p.Up * y + p.Out * z) / 1000f;
        half = new Vector3(e.W * 0.01f * p.W, e.H * 0.01f * p.H, e.T) * 0.0005f;
    }

    
    public bool InsideAnyPanel(Vector3 local, float margin)
    {
        foreach (var p in Panels)
        {
            var d = local - p.Center / 1000f;
            var s = Vector3.Dot(d, p.Side);
            var u = Vector3.Dot(d, p.Up);
            var o = Vector3.Dot(d, p.Out);
            if (Mathf.Abs(s) <= p.W * 0.0005f + margin && Mathf.Abs(u) <= p.H * 0.0005f + margin &&
                o <= margin && o >= -p.D / 1000f - margin) return true;
        }
        return false;
    }

    
    private Bounds _bounds;
    private bool _boundsValid;

    public Bounds LocalBounds
    {
        get
        {
            if (_boundsValid) return _bounds;
            var any = false;
            var min = Vector3.positiveInfinity;
            var max = Vector3.negativeInfinity;
            void Add(Vector3 c, Vector3 side, Vector3 up, Vector3 o, Vector3 h)
            {
                for (var i = 0; i < 8; i++)
                {
                    var q = c + side * ((i & 1) != 0 ? h.x : -h.x) + up * ((i & 2) != 0 ? h.y : -h.y) + o * ((i & 4) != 0 ? h.z : -h.z);
                    min = Vector3.Min(min, q);
                    max = Vector3.Max(max, q);
                    any = true;
                }
            }
            foreach (var p in Panels)
            {
                var side = p.Side;
                Add((p.Center - p.Out * (p.D * 0.5f)) / 1000f, side, p.Up, p.Out, new Vector3(p.W, p.H, p.D) * 0.0005f);
                foreach (var e in Elements)
                {
                    Box(p, e, out var c, out var h);
                    Add(c, side, p.Up, p.Out, h);
                }
            }
            _bounds = any ? new Bounds((min + max) * 0.5f, max - min) : new Bounds(Vector3.zero, Vector3.zero);
            _boundsValid = true;
            return _bounds;
        }
    }

    

    public struct Hit
    {
        public float Heat, Kinetic, Tandem;
        public int Fired;
        public int Panel, Element;           
    }

    private static readonly List<(int Panel, int Element, float T)> Crossed = new();

    
    
    public bool Evaluate(Vector3 origin, Vector3 dir, out Hit hit, bool skipMain = false, bool skipMirror = false)
    {
        hit = default;
        hit.Panel = hit.Element = -1;
        Crossed.Clear();
        var start = origin - dir * 0.6f;
        var travel = dir * (1.2f + 2f * LocalBounds.size.magnitude);
        for (var pi = 0; pi < Panels.Count; pi++)
        {
            var p = Panels[pi];
            if (p.Mirror ? skipMirror : skipMain) continue;   
            var side = p.Side;
            for (var ei = 0; ei < Elements.Count; ei++)
            {
                Box(p, Elements[ei], out var c, out var h);
                var a = start - c;
                var aa = new Vector3(Vector3.Dot(a, side), Vector3.Dot(a, p.Up), Vector3.Dot(a, p.Out));
                var dd = new Vector3(Vector3.Dot(travel, side), Vector3.Dot(travel, p.Up), Vector3.Dot(travel, p.Out));
                if (Segment(aa, dd, h, out var t)) Crossed.Add((pi, ei, t));
            }
        }
        if (Crossed.Count == 0) return false;
        Crossed.Sort((x, y) => x.T.CompareTo(y.T));
        float explosive = 0f, first = 0f, plates = 0f;
        var layers = 0;
        
        var window = Crossed[0].T + 0.3f / Mathf.Max(0.01f, travel.magnitude);
        foreach (var (pi, ei, tt) in Crossed)
        {
            if (tt > window) break;
            var e = Elements[ei];
            if (!e.Explosive)
            {
                plates += e.T;
                continue;
            }
            if (layers == 0)
            {
                first = e.T;
                hit.Panel = pi;
                hit.Element = ei;
            }
            layers++;
            explosive += e.T;
        }
        if (layers == 0) return false;
        hit.Fired = layers;
        hit.Heat = HeatCut(explosive, plates);
        hit.Kinetic = KineticCut(explosive, plates);
        hit.Tandem = layers >= 2 ? 0.8f * HeatCut(explosive - first, plates) : 0.1f * HeatCut(explosive, plates);
        return true;
    }

    
    
    public static float HeatCut(float explosiveMm, float platesMm) =>
        explosiveMm <= 0f ? 0f : Mathf.Clamp(0.55f * (1f - Mathf.Exp(-explosiveMm / 4f)) + 0.02f * platesMm, 0f, 0.85f);

    
    public static float KineticCut(float explosiveMm, float platesMm) =>
        explosiveMm <= 0f ? 0f : Mathf.Clamp(0.02f * (platesMm - 5f), 0f, 0.5f) * Mathf.Clamp01(explosiveMm / 3f);

    public static float AngleEffect(float angleDeg) => Mathf.Lerp(HeadOn, 1f, Mathf.Clamp01(angleDeg / FullEffectAngle));

    
    
    private (float Coverage, float Heat, float Kinetic, float Tandem)? _summary;

    public (float Coverage, float Heat, float Kinetic, float Tandem) Summary()
    {
        _summary ??= ComputeSummary();
        return _summary.Value;
    }

    private (float Coverage, float Heat, float Kinetic, float Tandem) ComputeSummary()
    {
        if (!HasGeometry || Elements.Count == 0) return (0f, 0f, 0f, 0f);
        const int n = 5;
        float covered = 0f, total = 0f, heat = 0f, kinetic = 0f, tandem = 0f;
        foreach (var p in Panels)
        {
            var w = p.Area / (n * n);
            var side = p.Side;
            for (var i = 0; i < n; i++)
            for (var j = 0; j < n; j++)
            {
                total += w;
                var o = (p.Center + side * ((-0.5f + (i + 0.5f) / n) * p.W) + p.Up * ((-0.5f + (j + 0.5f) / n) * p.H) + p.Out * 30f) / 1000f;
                if (!Evaluate(o, -p.Out, out var h)) continue;
                covered += w;
                heat += h.Heat * w;
                kinetic += h.Kinetic * w;
                tandem += h.Tandem * w;
            }
        }
        if (covered <= 0f || total <= 0f) return (0f, 0f, 0f, 0f);
        return (covered / total, heat / covered, kinetic / covered, tandem / covered);
    }

    
    public static bool Segment(Vector3 a, Vector3 d, Vector3 half, out float tEnter)
    {
        float tMin = 0f, tMax = 1f;
        tEnter = 0f;
        for (var axis = 0; axis < 3; axis++)
        {
            var o = a[axis];
            var v = d[axis];
            var h = half[axis];
            if (Mathf.Abs(v) < 1e-9f)
            {
                if (o < -h || o > h) return false;
                continue;
            }
            var t1 = (-h - o) / v;
            var t2 = (h - o) / v;
            if (t1 > t2) (t1, t2) = (t2, t1);
            tMin = Mathf.Max(tMin, t1);
            tMax = Mathf.Min(tMax, t2);
            if (tMin > tMax) return false;
        }
        tEnter = tMin;
        return true;
    }

    
    
    

    private static string F(float v) => v.ToString("0.#", CultureInfo.InvariantCulture);
    private static string F3(float v) => v.ToString("0.####", CultureInfo.InvariantCulture);

    public string ToToken()
    {
        var sb = new StringBuilder("era2");
        sb.Append(";on=").Append(On ? 1 : 0);
        sb.Append(";tc=").Append(F(Trigger));
        if (HasPartBox)
            sb.Append(";bx=").Append(F(PartMin.x)).Append(',').Append(F(PartMin.y)).Append(',').Append(F(PartMin.z)).Append(',')
                .Append(F(PartMax.x)).Append(',').Append(F(PartMax.y)).Append(',').Append(F(PartMax.z));
        if (HasMirrorBox)
            sb.Append(";bm=").Append(F(MirMin.x)).Append(',').Append(F(MirMin.y)).Append(',').Append(F(MirMin.z)).Append(',')
                .Append(F(MirMax.x)).Append(',').Append(F(MirMax.y)).Append(',').Append(F(MirMax.z));
        sb.Append(";pn=");
        for (var i = 0; i < Panels.Count; i++)
        {
            var p = Panels[i];
            if (i > 0) sb.Append('|');
            sb.Append(F(p.Center.x)).Append(',').Append(F(p.Center.y)).Append(',').Append(F(p.Center.z)).Append(',')
                .Append(F3(p.Out.x)).Append(',').Append(F3(p.Out.y)).Append(',').Append(F3(p.Out.z)).Append(',')
                .Append(F3(p.Up.x)).Append(',').Append(F3(p.Up.y)).Append(',').Append(F3(p.Up.z)).Append(',')
                .Append(F(p.W)).Append(',').Append(F(p.H)).Append(',').Append(F(p.D)).Append(',').Append(p.Mirror ? 1 : 0);
        }
        sb.Append(";el=");
        for (var i = 0; i < Elements.Count; i++)
        {
            var e = Elements[i];
            if (i > 0) sb.Append('/');
            sb.Append(e.Explosive ? 'e' : 'p').Append(',').Append(F(e.X)).Append(',').Append(F(e.Y)).Append(',').Append(F(e.Depth))
                .Append(',').Append(F(e.W)).Append(',').Append(F(e.H)).Append(',').Append(F(e.T));
        }
        return sb.ToString();
    }

    public static EraConfig FromToken(string token)
    {
        if (string.IsNullOrEmpty(token)) return null;
        if (token.StartsWith("era1", StringComparison.Ordinal)) return FromV1(token);
        if (!token.StartsWith("era2", StringComparison.Ordinal)) return null;
        var c = new EraConfig();
        foreach (var (key, value) in Pairs(token))
        {
            try
            {
                switch (key)
                {
                    case "on": c.On = value != "0"; break;
                    case "tc": c.Trigger = P(value); break;
                    case "bx":
                    case "bm":
                    {
                        var f = value.Split(',');
                        if (f.Length < 6) break;
                        var min = new Vector3(P(f[0]), P(f[1]), P(f[2]));
                        var max = new Vector3(P(f[3]), P(f[4]), P(f[5]));
                        if (key == "bx") (c.PartMin, c.PartMax) = (min, max);
                        else (c.MirMin, c.MirMax) = (min, max);
                        break;
                    }
                    case "pn":
                        foreach (var item in value.Split('|'))
                        {
                            var f = item.Split(',');
                            if (f.Length < 13) continue;
                            c.Panels.Add(new EraFace
                            {
                                Center = new Vector3(P(f[0]), P(f[1]), P(f[2])),
                                Out = new Vector3(P(f[3]), P(f[4]), P(f[5])).normalized,
                                Up = new Vector3(P(f[6]), P(f[7]), P(f[8])).normalized,
                                W = P(f[9]), H = P(f[10]), D = P(f[11]), Mirror = f[12] == "1"
                            });
                        }
                        break;
                    case "el":
                        foreach (var item in value.Split('/'))
                        {
                            var f = item.Split(',');
                            if (f.Length < 7) continue;
                            c.Elements.Add(new EraElement
                            {
                                Explosive = f[0] == "e", X = P(f[1]), Y = P(f[2]), Depth = P(f[3]), W = P(f[4]), H = P(f[5]), T = P(f[6])
                            });
                        }
                        break;
                }
            }
            catch { }
        }
        c.Sanitize();
        return c;
    }

    
    
    private static EraConfig FromV1(string token)
    {
        var c = new EraConfig();
        Vector3 bc = Vector3.zero, bs = Vector3.zero;
        int ax = 2, up = 1, sg = 1;
        var raw = new List<(bool E, float X, float Y, float D, float W, float H, float T)>();
        foreach (var (key, value) in Pairs(token))
        {
            try
            {
                switch (key)
                {
                    case "on": c.On = value != "0"; break;
                    case "tc": c.Trigger = P(value); break;
                    case "bc": bc = Vec(value); break;
                    case "bs": bs = Vec(value); break;
                    case "ax": ax = int.Parse(value, CultureInfo.InvariantCulture); break;
                    case "up": up = int.Parse(value, CultureInfo.InvariantCulture); break;
                    case "sg": sg = value.StartsWith("-") ? -1 : 1; break;
                    case "el":
                        foreach (var item in value.Split('/'))
                        {
                            var f = item.Split(',');
                            if (f.Length >= 7) raw.Add((f[0] == "e", P(f[1]), P(f[2]), P(f[3]), P(f[4]), P(f[5]), P(f[6])));
                        }
                        break;
                }
            }
            catch { }
        }
        if (ax is < 0 or > 2) ax = 2;
        if (up is < 0 or > 2 || up == ax) up = ax == 1 ? 2 : 1;
        var sideAxis = 3 - ax - up;
        var o = Vector3.zero;
        o[ax] = sg;
        var u = Vector3.zero;
        u[up] = 1f;
        var s = Vector3.zero;
        s[sideAxis] = 1f;
        var w = Mathf.Max(1f, bs[sideAxis]);
        var h = Mathf.Max(1f, bs[up]);
        var d = Mathf.Max(1f, bs[ax]);
        var panel = new EraFace { Center = bc + o * (d * 0.5f), Out = o, Up = u, W = w, H = h, D = d };
        var flip = Vector3.Dot(panel.Side, s) < 0f ? -1f : 1f;
        if (bs.sqrMagnitude > 0.01f) c.Panels.Add(panel);
        foreach (var e in raw)
            c.Elements.Add(new EraElement
            {
                Explosive = e.E, X = flip * e.X / (w * 0.5f) * 100f, Y = e.Y / (h * 0.5f) * 100f, Depth = e.D,
                W = e.W / w * 100f, H = e.H / h * 100f, T = e.T
            });
        c.Sanitize();
        return c;
    }

    private static IEnumerable<(string Key, string Value)> Pairs(string token)
    {
        foreach (var part in token.Split(';'))
        {
            var eq = part.IndexOf('=');
            if (eq > 0) yield return (part.Substring(0, eq), part.Substring(eq + 1));
        }
    }

    private static float P(string s) => float.Parse(s, CultureInfo.InvariantCulture);

    private static Vector3 Vec(string s)
    {
        var f = s.Split(',');
        return f.Length < 3 ? Vector3.zero : new Vector3(P(f[0]), P(f[1]), P(f[2]));
    }
}
