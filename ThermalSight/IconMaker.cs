using System;
using UnityEngine;

namespace ThermalSight;


internal static class IconMaker
{
    public static Sprite MakeThermalIcon(Sprite source)
    {
        if (source == null) return null;

        var tex = source.texture;
        var r = source.textureRect;
        int w = Mathf.RoundToInt(r.width), h = Mathf.RoundToInt(r.height);
        if (w <= 0 || h <= 0) return null;

        
        var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32);
        var prev = RenderTexture.active;
        Texture2D result;
        try
        {
            Graphics.Blit(tex, rt);
            RenderTexture.active = rt;
            result = new Texture2D(w, h, TextureFormat.RGBA32, false);
            result.ReadPixels(new Rect(r.x, r.y, w, h), 0, 0, false);
        }
        finally
        {
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
        }

        var px = result.GetPixels32();
        for (var i = 0; i < px.Length; i++)
        {
            var c = px[i];
            if (c.a == 0) continue;
            
            var l = (0.55f * c.r + 0.4f * c.g + 0.05f * c.b) / 255f;
            l = Mathf.Clamp01((l - 0.15f) * 1.6f);
            var v = (byte)(l * 235 + 20);
            px[i] = new Color32(v, v, v, c.a);
        }

        
        var dot = Math.Max(3, w / 9);
        for (var y = h - dot - 2; y < h - 2; y++)
        for (var x = w - dot - 2; x < w - 2; x++)
        {
            if (x < 0 || y < 0) continue;
            px[y * w + x] = new Color32(255, 255, 255, 255);
        }

        result.SetPixels32(px);
        result.Apply(false);
        result.filterMode = FilterMode.Bilinear;

        return Sprite.Create(result, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), source.pixelsPerUnit);
    }

    
    public static Sprite MakeNightVisionIcon(Sprite source)
    {
        if (source == null) return null;
        var tex = source.texture;
        var r = source.textureRect;
        int w = Mathf.RoundToInt(r.width), h = Mathf.RoundToInt(r.height);
        if (w <= 0 || h <= 0) return null;
        var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32);
        var prev = RenderTexture.active;
        Texture2D result;
        try
        {
            Graphics.Blit(tex, rt);
            RenderTexture.active = rt;
            result = new Texture2D(w, h, TextureFormat.RGBA32, false);
            result.ReadPixels(new Rect(r.x, r.y, w, h), 0, 0, false);
        }
        finally
        {
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
        }
        var px = result.GetPixels32();
        for (var i = 0; i < px.Length; i++)
        {
            var c = px[i];
            if (c.a == 0) continue;
            var l = Mathf.Clamp01(((0.3f * c.r + 0.6f * c.g + 0.1f * c.b) / 255f - 0.1f) * 1.4f);
            px[i] = new Color32((byte)(l * 120 + 10), (byte)(l * 235 + 20), (byte)(l * 110 + 10), c.a);
        }
        var dot = Math.Max(3, w / 9);
        for (var y = h - dot - 2; y < h - 2; y++)
        for (var x = w - dot - 2; x < w - 2; x++)
        {
            if (x < 0 || y < 0) continue;
            px[y * w + x] = new Color32(120, 255, 110, 255);
        }
        result.SetPixels32(px);
        result.Apply(false);
        result.filterMode = FilterMode.Bilinear;
        return Sprite.Create(result, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), source.pixelsPerUnit);
    }
}
