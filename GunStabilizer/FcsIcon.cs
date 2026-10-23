using System;
using UnityEngine;

namespace GunStabilizer;




internal static class FcsIcon
{
    private const int Size = 256, S = Size * 2;

    public static Sprite Make()
    {
        var px = new Color[S * S];
        Shadow(px);
        
        Vector2 a = new(70, 170), b = new(380, 170), c = new(380, 420), d = new(70, 420);
        var lift = new Vector2(90, -80);
        Poly(px, new[] { a, a + lift, b + lift, b }, new Color(0.47f, 0.5f, 0.45f));
        Poly(px, new[] { b, b + lift, c + lift, c }, new Color(0.24f, 0.27f, 0.24f));
        Gradient(px, a, c, new Color(0.36f, 0.39f, 0.34f), new Color(0.28f, 0.31f, 0.27f));
        
        Edge(px, a, b, 3f, new Color(0.62f, 0.66f, 0.6f));
        Edge(px, a, a + lift, 3f, new Color(0.62f, 0.66f, 0.6f));
        Edge(px, b, b + lift, 3f, new Color(0.55f, 0.58f, 0.53f));
        Edge(px, b, c, 3f, new Color(0.2f, 0.22f, 0.2f));
        Edge(px, a + lift, b + lift, 2f, new Color(0.58f, 0.62f, 0.56f));
        Edge(px, b + lift, c + lift, 2f, new Color(0.18f, 0.2f, 0.18f));
        
        foreach (var p in new[] { new Vector2(86, 186), new Vector2(364, 186), new Vector2(86, 404), new Vector2(364, 404) })
        {
            Disc(px, p, 7f, new Color(0.18f, 0.19f, 0.17f));
            Disc(px, p + new Vector2(-1.5f, -1.5f), 3.5f, new Color(0.55f, 0.57f, 0.52f));
        }
        
        Rect(px, 102, 202, 302, 292, new Color(0.08f, 0.08f, 0.07f));
        Rect(px, 108, 208, 296, 286, new Color(0.05f, 0.035f, 0.02f));
        var digits = new[] { 0, 2, 7, 5 };
        for (var i = 0; i < digits.Length; i++) Digit(px, 116 + i * 45, 216, digits[i]);
        
        for (var row = 0; row < 2; row++)
        for (var col = 0; col < 4; col++)
        {
            float x = 112 + col * 44, y = 318 + row * 40;
            Rect(px, x, y, x + 30, y + 28, new Color(0.13f, 0.14f, 0.13f));
            Rect(px, x + 3, y + 3, x + 27, y + 22, row == 0 && col == 3 ? new Color(0.62f, 0.2f, 0.16f) : new Color(0.26f, 0.28f, 0.26f));
        }
        
        Disc(px, new Vector2(338, 226), 13f, new Color(0.09f, 0.1f, 0.09f));
        Glow(px, new Vector2(338, 226), 26f, new Color(0.35f, 1f, 0.4f), 0.35f);
        Disc(px, new Vector2(338, 226), 8f, new Color(0.45f, 1f, 0.5f));
        Disc(px, new Vector2(335, 223), 3f, new Color(0.9f, 1f, 0.9f));
        
        var plug = new Vector2(428, 268);
        Disc(px, plug, 26f, new Color(0.13f, 0.14f, 0.13f));
        Disc(px, plug, 19f, new Color(0.4f, 0.42f, 0.38f));
        Disc(px, plug, 9f, new Color(0.1f, 0.1f, 0.1f));

        
        var outPx = new Color32[Size * Size];
        for (var y = 0; y < Size; y++)
        for (var x = 0; x < Size; x++)
        {
            float r = 0, g = 0, bl = 0, al = 0;
            for (var dy = 0; dy < 2; dy++)
            for (var dx = 0; dx < 2; dx++)
            {
                var p = px[(y * 2 + dy) * S + x * 2 + dx];
                r += p.r * p.a; g += p.g * p.a; bl += p.b * p.a; al += p.a;
            }
            var inv = al > 1e-4f ? 1f / al : 0f;
            
            outPx[(Size - 1 - y) * Size + x] = new Color32((byte)(Mathf.Clamp01(r * inv) * 255f), (byte)(Mathf.Clamp01(g * inv) * 255f),
                (byte)(Mathf.Clamp01(bl * inv) * 255f), (byte)(Mathf.Clamp01(al * 0.25f) * 255f));
        }
        var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        tex.SetPixels32(outPx);
        tex.Apply(false);
        tex.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        var sprite = Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f);
        sprite.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        return sprite;
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
            if (Inside(p, new Vector2(x + 0.5f, y + 0.5f))) Blend(px, x, y, c, 1f);
    }

    private static bool Inside(Vector2[] p, Vector2 q)
    {
        var inside = false;
        for (int i = 0, j = p.Length - 1; i < p.Length; j = i++)
            if ((p[i].y > q.y) != (p[j].y > q.y) && q.x < (p[j].x - p[i].x) * (q.y - p[i].y) / (p[j].y - p[i].y) + p[i].x) inside = !inside;
        return inside;
    }

    private static void Gradient(Color[] px, Vector2 from, Vector2 to, Color top, Color bottom)
    {
        for (var y = (int)from.y; y < (int)to.y; y++)
        {
            var c = Color.Lerp(top, bottom, (y - from.y) / Math.Max(1f, to.y - from.y));
            for (var x = (int)from.x; x < (int)to.x; x++) Blend(px, x, y, c, 1f);
        }
    }

    private static void Rect(Color[] px, float x0, float y0, float x1, float y1, Color c)
    {
        for (var y = (int)y0; y < (int)y1; y++)
        for (var x = (int)x0; x < (int)x1; x++) Blend(px, x, y, c, 1f);
    }

    private static void Disc(Color[] px, Vector2 c, float r, Color col)
    {
        for (var y = (int)(c.y - r - 1); y <= (int)(c.y + r + 1); y++)
        for (var x = (int)(c.x - r - 1); x <= (int)(c.x + r + 1); x++)
        {
            var d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c);
            Blend(px, x, y, col, Mathf.Clamp01(r - d + 0.5f));
        }
    }

    private static void Glow(Color[] px, Vector2 c, float r, Color col, float strength)
    {
        for (var y = (int)(c.y - r); y <= (int)(c.y + r); y++)
        for (var x = (int)(c.x - r); x <= (int)(c.x + r); x++)
        {
            var d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c) / r;
            if (d < 1f) Blend(px, x, y, col, strength * (1f - d) * (1f - d));
        }
    }

    private static void Edge(Color[] px, Vector2 a, Vector2 b, float w, Color c)
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
            var d = Vector2.Distance(p, a + ab * t);
            Blend(px, x, y, c, Mathf.Clamp01(w * 0.5f - d + 0.5f));
        }
    }

    private static void Shadow(Color[] px)
    {
        var c = new Vector2(275, 440);
        for (var y = 380; y < 500; y++)
        for (var x = 40; x < 500; x++)
        {
            var dx = (x - c.x) / 230f;
            var dy = (y - c.y) / 38f;
            var d = dx * dx + dy * dy;
            if (d < 1f) Blend(px, x, y, Color.black, 0.45f * (1f - d));
        }
    }

    
    private static readonly string[] Segments = { "abcdef", "bc", "abged", "abgcd", "fgbc", "afgcd", "afgedc", "abc", "abcdefg", "abcdfg" };

    private static void Digit(Color[] px, float x, float y, int n)
    {
        const float w = 34f, h = 62f, t = 7f;
        var on = new Color(1f, 0.69f, 0.19f);
        var off = new Color(0.16f, 0.09f, 0.03f);
        foreach (var s in "abcdefg")
        {
            var lit = Segments[n].IndexOf(s) >= 0;
            var col = lit ? on : off;
            switch (s)
            {
                case 'a': Rect(px, x + t, y, x + w - t, y + t, col); break;
                case 'g': Rect(px, x + t, y + h / 2f - t / 2f, x + w - t, y + h / 2f + t / 2f, col); break;
                case 'd': Rect(px, x + t, y + h - t, x + w - t, y + h, col); break;
                case 'f': Rect(px, x, y + t, x + t, y + h / 2f - t / 2f, col); break;
                case 'b': Rect(px, x + w - t, y + t, x + w, y + h / 2f - t / 2f, col); break;
                case 'e': Rect(px, x, y + h / 2f + t / 2f, x + t, y + h - t, col); break;
                case 'c': Rect(px, x + w - t, y + h / 2f + t / 2f, x + w, y + h - t, col); break;
            }
            if (lit) Glow(px, new Vector2(x + w / 2f, y + h / 2f), 30f, on, 0.04f);
        }
    }
}
