using System;
using System.Collections.Generic;
using UnityEngine;

namespace ModMenu;



internal static class ModIcons
{
    private const int Size = 128, S = Size * 2;
    private static readonly Dictionary<string, Sprite> Cache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Color Slate = new(0.13f, 0.15f, 0.17f), SlateTop = new(0.2f, 0.22f, 0.25f);
    private static readonly Color Gold = new(0.93f, 0.66f, 0.3f), Light = new(0.95f, 0.93f, 0.88f), Dim = new(0.55f, 0.57f, 0.6f);

    public static Sprite For(string guid)
    {
        if (Cache.TryGetValue(guid ?? "", out var s) && s != null && !s.WasCollected) return s;
        try { s = Make(guid ?? ""); }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("Mod icon failed: " + e.Message);
            s = null;
        }
        Cache[guid ?? ""] = s;
        return s;
    }

    private static Sprite Make(string guid)
    {
        var px = new Color[S * S];
        Badge(px);
        switch (guid.ToLowerInvariant())
        {
            case "sprocket.activeprotection": Shield(px); break;
            case "sprocket.combateffects": Burst(px); break;
            case "sprocket.crewvoices": Headset(px); break;
            case "sprocket.drivetuning": Gauge(px); break;
            case "sprocket.fpvdrones":
            case "sprocket.fpvpilot": Drone(px); break;
            case "sprocket.gunstabilizer": Crosshair(px); break;
            case "sprocket.modendaymenu": MenuLines(px); break;
            case "sprocket.modernday": Star(px); break;
            case "sprocket.munitions": Shell(px); break;
            case "sprocket.powertrain": Gear(px); break;
            case "sprocket.showroommenu": Turntable(px); break;
            case "sprocket.soundoverhaul": Speaker(px); break;
            case "sprocket.thermalsight.modernday": Thermal(px); break;
            case "sprocket.vehiclematerials": Palette(px); break;
            case "sprocket.wheels": Wheel(px); break;
            case "sprocket.mddynamicweather": Weather(px); break;
            case "sprocket.modsettings": Sliders(px); break;
            case "sprocket.tankedit": Clapper(px); break;
            default: Cube(px); break;
        }
        var outPx = new Color32[Size * Size];
        for (var y = 0; y < Size; y++)
        for (var x = 0; x < Size; x++)
        {
            float r = 0, g = 0, b = 0, a = 0;
            for (var dy = 0; dy < 2; dy++)
            for (var dx = 0; dx < 2; dx++)
            {
                var p = px[(y * 2 + dy) * S + x * 2 + dx];
                r += p.r * p.a; g += p.g * p.a; b += p.b * p.a; a += p.a;
            }
            var inv = a > 1e-4f ? 1f / a : 0f;
            outPx[(Size - 1 - y) * Size + x] = new Color32((byte)(Mathf.Clamp01(r * inv) * 255f), (byte)(Mathf.Clamp01(g * inv) * 255f),
                (byte)(Mathf.Clamp01(b * inv) * 255f), (byte)(Mathf.Clamp01(a * 0.25f) * 255f));
        }
        var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        tex.SetPixels32(outPx);
        tex.Apply(false);
        tex.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        var sprite = Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f);
        sprite.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        return sprite;
    }

    

    private static void Badge(Color[] px)
    {
        RoundRect(px, 6, 6, S - 6, S - 6, 34, Gold);
        RoundRect(px, 14, 14, S - 14, S - 14, 27, Slate);
        
        for (var y = 14; y < S / 2; y++)
        {
            var k = 1f - (y - 14f) / (S / 2f - 14f);
            for (var x = 14; x < S - 14; x++)
                if (px[y * S + x].a > 0.5f) Blend(px, x, y, SlateTop, 0.5f * k);
        }
    }

    

    private static void Shield(Color[] px)
    {
        var p = new List<Vector2> { new(128, 46), new(196, 72), new(190, 140) };
        for (var i = 0; i <= 12; i++) p.Add(new Vector2(190 - 62 * Mathf.Sin(i / 12f * Mathf.PI * 0.5f), 140 + 70 * Mathf.Sin(i / 12f * Mathf.PI * 0.5f)));
        var right = p.ToArray();
        var all = new List<Vector2>(right);
        for (var i = right.Length - 1; i >= 0; i--) all.Add(new Vector2(256 - right[i].x, right[i].y));
        Poly(px, all.ToArray(), Light);
        
        Arc(px, new Vector2(128, 170), 30, 6, -150, -30, Slate);
        Arc(px, new Vector2(128, 170), 52, 6, -145, -35, Slate);
        Disc(px, new Vector2(128, 170), 9, Gold);
    }

    private static void Burst(Color[] px)
    {
        var pts = new Vector2[24];
        for (var i = 0; i < pts.Length; i++)
        {
            var a = i / (float)pts.Length * Mathf.PI * 2f;
            var r = i % 2 == 0 ? 84f : 40f + (i % 4 == 1 ? 8f : 0f);
            pts[i] = new Vector2(128 + Mathf.Cos(a) * r, 132 + Mathf.Sin(a) * r);
        }
        Poly(px, pts, Gold);
        for (var i = 0; i < pts.Length; i++) pts[i] = Vector2.Lerp(new Vector2(128, 132), pts[i], 0.55f);
        Poly(px, pts, Light);
    }

    
    private static void Headset(Color[] px)
    {
        Arc(px, new Vector2(128, 140), 70, 14, -180, 0, Light);
        RoundRect(px, 44, 120, 82, 184, 14, Light);
        RoundRect(px, 174, 120, 212, 184, 14, Light);
        RoundRect(px, 52, 130, 74, 174, 8, Gold);
        RoundRect(px, 182, 130, 204, 174, 8, Gold);
        Arc(px, new Vector2(150, 176), 40, 7, 90, 150, Light);
        Disc(px, new Vector2(118, 212), 12, Gold);
    }

    private static void Gauge(Color[] px)
    {
        Arc(px, new Vector2(128, 150), 76, 14, -200, 20, Light);
        for (var i = 0; i <= 8; i++)
        {
            var a = (-200f + i * 27.5f) * Mathf.Deg2Rad;
            var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            Line(px, new Vector2(128, 150) + d * 50, new Vector2(128, 150) + d * 62, 5, i >= 7 ? Gold : Dim);
        }
        var n = -50f * Mathf.Deg2Rad;
        Line(px, new Vector2(128, 150), new Vector2(128, 150) + new Vector2(Mathf.Cos(n), Mathf.Sin(n)) * 64, 8, Gold);
        Disc(px, new Vector2(128, 150), 12, Light);
    }

    private static void Drone(Color[] px)
    {
        var c = new Vector2(128, 128);
        foreach (var d in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(1, 1) })
        {
            var m = c + d * 52;
            Line(px, c, m, 12, Light);
            Ring(px, m, 30, 6, Gold);
            Disc(px, m, 8, Light);
        }
        RoundRect(px, 104, 108, 152, 148, 12, Light);
        Disc(px, new Vector2(128, 124), 7, Slate);
    }

    private static void Crosshair(Color[] px)
    {
        var c = new Vector2(128, 128);
        Ring(px, c, 64, 9, Light);
        Line(px, new Vector2(128, 38), new Vector2(128, 92), 8, Light);
        Line(px, new Vector2(128, 164), new Vector2(128, 218), 8, Light);
        Line(px, new Vector2(38, 128), new Vector2(92, 128), 8, Light);
        Line(px, new Vector2(164, 128), new Vector2(218, 128), 8, Light);
        Disc(px, c, 10, Gold);
    }

    private static void MenuLines(Color[] px)
    {
        RoundRect(px, 56, 60, 200, 196, 14, Light);
        RoundRect(px, 66, 86, 190, 186, 8, Slate);
        Rect(px, 56, 60, 200, 84, Light);
        for (var i = 0; i < 3; i++) RoundRect(px, 82, 102 + i * 26, 174, 114 + i * 26, 6, i == 0 ? Gold : Dim);
    }

    private static void Star(Color[] px)
    {
        var pts = new Vector2[10];
        for (var i = 0; i < 10; i++)
        {
            var a = (-90f + i * 36f) * Mathf.Deg2Rad;
            var r = i % 2 == 0 ? 84f : 34f;
            pts[i] = new Vector2(128 + Mathf.Cos(a) * r, 136 + Mathf.Sin(a) * r);
        }
        Poly(px, pts, Gold);
        for (var i = 0; i < 10; i++) pts[i] = Vector2.Lerp(new Vector2(128, 136), pts[i], 0.6f);
        Poly(px, pts, Light);
    }

    private static void Shell(Color[] px)
    {
        
        var axis = new Vector2(1, -1).normalized;
        var side = new Vector2(axis.y, -axis.x);
        var b = new Vector2(78, 182);
        Vector2 P(float along, float across) => b + axis * along + side * across;
        Poly(px, new[] { P(0, -24), P(70, -24), P(70, 24), P(0, 24) }, Gold);
        Poly(px, new[] { P(70, -26), P(82, -26), P(82, 26), P(70, 26) }, Dim);
        Poly(px, new[] { P(82, -24), P(110, -24), P(140, -10), P(150, 0), P(140, 10), P(110, 24), P(82, 24) }, Light);
    }

    private static void Gear(Color[] px)
    {
        var c = new Vector2(128, 128);
        var pts = new List<Vector2>();
        const int teeth = 10;
        for (var i = 0; i < teeth * 4; i++)
        {
            var a = i / (teeth * 4f) * Mathf.PI * 2f;
            var r = i % 4 < 2 ? 82f : 64f;
            pts.Add(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
        }
        Poly(px, pts.ToArray(), Light);
        Disc(px, c, 30, Slate);
        Ring(px, c, 30, 6, Gold);
    }

    private static void Turntable(Color[] px)
    {
        Ellipse(px, new Vector2(128, 180), 92, 26, Gold);
        Ellipse(px, new Vector2(128, 172), 86, 22, Dim);
        
        Poly(px, new[] { new Vector2(58, 160), new Vector2(66, 138), new Vector2(190, 138), new Vector2(200, 160) }, Light);
        Poly(px, new[] { new Vector2(98, 138), new Vector2(108, 116), new Vector2(150, 116), new Vector2(160, 138) }, Light);
        Line(px, new Vector2(150, 124), new Vector2(214, 116), 8, Light);
    }

    private static void Speaker(Color[] px)
    {
        Poly(px, new[] { new Vector2(54, 104), new Vector2(84, 104), new Vector2(124, 70), new Vector2(124, 186), new Vector2(84, 152), new Vector2(54, 152) }, Light);
        Arc(px, new Vector2(124, 128), 34, 8, -45, 45, Gold);
        Arc(px, new Vector2(124, 128), 58, 8, -45, 45, Gold);
        Arc(px, new Vector2(124, 128), 82, 8, -45, 45, Dim);
    }

    private static void Thermal(Color[] px)
    {
        var c = new Vector2(128, 128);
        Ring(px, c, 76, 10, Light);
        for (var y = 0; y < S; y++)
        for (var x = 0; x < S; x++)
        {
            var d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c);
            if (d > 64f) continue;
            var heat = 1f - d / 64f;
            var col = heat > 0.66f ? Color.Lerp(Gold, Color.white, (heat - 0.66f) * 3f) : Color.Lerp(new Color(0.45f, 0.12f, 0.35f), Gold, heat * 1.5f);
            Blend(px, x, y, col, Mathf.Clamp01(64f - d + 0.5f));
        }
        Line(px, new Vector2(128, 78), new Vector2(128, 178), 4, Slate);
        Line(px, new Vector2(78, 128), new Vector2(178, 128), 4, Slate);
    }

    private static void Palette(Color[] px)
    {
        Ellipse(px, new Vector2(128, 132), 84, 70, Light);
        Disc(px, new Vector2(168, 162), 18, Slate);
        Disc(px, new Vector2(92, 104), 15, new Color(0.85f, 0.3f, 0.25f));
        Disc(px, new Vector2(132, 88), 15, Gold);
        Disc(px, new Vector2(172, 110), 15, new Color(0.35f, 0.65f, 0.4f));
        Disc(px, new Vector2(90, 150), 15, new Color(0.3f, 0.45f, 0.75f));
    }

    private static void Wheel(Color[] px)
    {
        var c = new Vector2(128, 128);
        Ring(px, c, 76, 18, Light);
        Ring(px, c, 56, 4, Dim);
        for (var i = 0; i < 6; i++)
        {
            var a = i / 6f * Mathf.PI * 2f;
            Line(px, c, c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 56, 8, Gold);
        }
        Disc(px, c, 16, Light);
    }

    private static void Sliders(Color[] px)
    {
        for (var i = 0; i < 3; i++)
        {
            var y = 82 + i * 46;
            Line(px, new Vector2(60, y), new Vector2(196, y), 8, Dim);
            var k = new[] { 150f, 92f, 172f }[i];
            Line(px, new Vector2(60, y), new Vector2(k, y), 8, Gold);
            Disc(px, new Vector2(k, y), 15, Light);
        }
    }

    private static void Clapper(Color[] px)
    {
        RoundRect(px, 58, 112, 198, 196, 8, Light);
        Poly(px, new[] { new Vector2(58, 104), new Vector2(190, 70), new Vector2(196, 92), new Vector2(62, 126) }, Gold);
    }

    
    private static void Weather(Color[] px)
    {
        var sun = new Vector2(104, 104);
        for (var i = 0; i < 8; i++)
        {
            var a = i * Mathf.PI / 4f;
            var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            Line(px, sun + d * 46f, sun + d * 64f, 10f, Gold);
        }
        Disc(px, sun, 36f, Gold);
        var c = new Vector2(142, 150);
        Disc(px, c + new Vector2(-34f, 8f), 30f, Light);
        Disc(px, c + new Vector2(2f, -14f), 40f, Light);
        Disc(px, c + new Vector2(38f, 6f), 30f, Light);
        RoundRect(px, c.x - 62f, c.y + 2f, c.x + 66f, c.y + 38f, 18f, Light);
    }

    private static void Cube(Color[] px)
    {
        Poly(px, new[] { new Vector2(128, 54), new Vector2(194, 90), new Vector2(128, 126), new Vector2(62, 90) }, Light);
        Poly(px, new[] { new Vector2(62, 90), new Vector2(128, 126), new Vector2(128, 202), new Vector2(62, 166) }, Gold);
        Poly(px, new[] { new Vector2(128, 126), new Vector2(194, 90), new Vector2(194, 166), new Vector2(128, 202) }, Dim);
    }

    

    private static void Blend(Color[] px, int x, int y, Color c, float a)
    {
        if (x < 0 || y < 0 || x >= S || y >= S || a <= 0f) return;
        var i = y * S + x;
        var d = px[i];
        var oa = a + d.a * (1f - a);
        if (oa <= 1e-5f) return;
        px[i] = new Color((c.r * a + d.r * d.a * (1f - a)) / oa, (c.g * a + d.g * d.a * (1f - a)) / oa, (c.b * a + d.b * d.a * (1f - a)) / oa, oa);
    }

    private static void Poly(Color[] px, Vector2[] p, Color c)
    {
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        foreach (var v in p) { x0 = Math.Min(x0, v.x); y0 = Math.Min(y0, v.y); x1 = Math.Max(x1, v.x); y1 = Math.Max(y1, v.y); }
        for (var y = (int)y0; y <= (int)y1; y++)
        for (var x = (int)x0; x <= (int)x1; x++)
        {
            var q = new Vector2(x + 0.5f, y + 0.5f);
            var inside = false;
            for (int i = 0, j = p.Length - 1; i < p.Length; j = i++)
                if ((p[i].y > q.y) != (p[j].y > q.y) && q.x < (p[j].x - p[i].x) * (q.y - p[i].y) / (p[j].y - p[i].y) + p[i].x) inside = !inside;
            if (inside) Blend(px, x, y, c, 1f);
        }
    }

    private static void Rect(Color[] px, float x0, float y0, float x1, float y1, Color c)
    {
        for (var y = (int)y0; y < (int)y1; y++)
        for (var x = (int)x0; x < (int)x1; x++) Blend(px, x, y, c, 1f);
    }

    private static void RoundRect(Color[] px, float x0, float y0, float x1, float y1, float r, Color c)
    {
        for (var y = (int)y0; y < (int)y1; y++)
        for (var x = (int)x0; x < (int)x1; x++)
        {
            var cx = Mathf.Clamp(x + 0.5f, x0 + r, x1 - r);
            var cy = Mathf.Clamp(y + 0.5f, y0 + r, y1 - r);
            var d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
            Blend(px, x, y, c, Mathf.Clamp01(r - d + 0.5f));
        }
    }

    private static void Disc(Color[] px, Vector2 c, float r, Color col)
    {
        for (var y = (int)(c.y - r - 1); y <= (int)(c.y + r + 1); y++)
        for (var x = (int)(c.x - r - 1); x <= (int)(c.x + r + 1); x++)
            Blend(px, x, y, col, Mathf.Clamp01(r - Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c) + 0.5f));
    }

    private static void Ellipse(Color[] px, Vector2 c, float rx, float ry, Color col)
    {
        for (var y = (int)(c.y - ry - 1); y <= (int)(c.y + ry + 1); y++)
        for (var x = (int)(c.x - rx - 1); x <= (int)(c.x + rx + 1); x++)
        {
            float dx = (x + 0.5f - c.x) / rx, dy = (y + 0.5f - c.y) / ry;
            var d = Mathf.Sqrt(dx * dx + dy * dy);
            Blend(px, x, y, col, Mathf.Clamp01((1f - d) * Mathf.Min(rx, ry) + 0.5f));
        }
    }

    private static void Ring(Color[] px, Vector2 c, float r, float w, Color col)
    {
        for (var y = (int)(c.y - r - w); y <= (int)(c.y + r + w); y++)
        for (var x = (int)(c.x - r - w); x <= (int)(c.x + r + w); x++)
        {
            var d = Mathf.Abs(Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c) - r);
            Blend(px, x, y, col, Mathf.Clamp01(w * 0.5f - d + 0.5f));
        }
    }

    
    private static void Arc(Color[] px, Vector2 c, float r, float w, float a0, float a1, Color col)
    {
        for (var y = (int)(c.y - r - w); y <= (int)(c.y + r + w); y++)
        for (var x = (int)(c.x - r - w); x <= (int)(c.x + r + w); x++)
        {
            var v = new Vector2(x + 0.5f, y + 0.5f) - c;
            var ang = Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;
            while (ang < a0) ang += 360f;
            while (ang > a0 + 360f) ang -= 360f;
            if (ang > a1) continue;
            var d = Mathf.Abs(v.magnitude - r);
            Blend(px, x, y, col, Mathf.Clamp01(w * 0.5f - d + 0.5f));
        }
    }

    private static void Line(Color[] px, Vector2 a, Vector2 b, float w, Color c)
    {
        var ab = b - a;
        var len2 = Math.Max(ab.sqrMagnitude, 1e-4f);
        int x0 = (int)(Math.Min(a.x, b.x) - w - 1), x1 = (int)(Math.Max(a.x, b.x) + w + 1);
        int y0 = (int)(Math.Min(a.y, b.y) - w - 1), y1 = (int)(Math.Max(a.y, b.y) + w + 1);
        for (var y = y0; y <= y1; y++)
        for (var x = x0; x <= x1; x++)
        {
            var p = new Vector2(x + 0.5f, y + 0.5f);
            var t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
            Blend(px, x, y, c, Mathf.Clamp01(w * 0.5f - Vector2.Distance(p, a + ab * t) + 0.5f));
        }
    }
}
