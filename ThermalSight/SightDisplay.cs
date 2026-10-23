using UnityEngine;
using UnityEngine.UI;

namespace ThermalSight;


internal readonly struct SightStatus
{
    public readonly bool Ready;     
    public readonly string Ammo;    
    public readonly int Range;      

    public SightStatus(bool ready, string ammo, int range)
    {
        Ready = ready;
        Ammo = ammo;
        Range = range;
    }
}



internal sealed class SightDisplay
{
    private sealed class Field
    {
        public GameObject Box;
        public RawImage[] Glyphs;
        public float PixelW, PixelH;
        public string Shown;
    }

    private const float GlyphHeight = 0.78f; 

    private readonly Field _ready, _ammo, _range, _mode;

    private SightDisplay(RectTransform frame)
    {
        
        _ready = MakeField(frame, "Ready", 510, 600, 681, 725, 1);
        _ammo = MakeField(frame, "Ammo", 900, 940, 681, 725, 1);
        _range = MakeField(frame, "Range", 1112, 1228, 681, 725, 4);
        _mode = MakeField(frame, "Mode", 1263, 1379, 680, 725, 1);
    }

    public static SightDisplay Create(RectTransform frame)
    {
        if (frame == null || GpuResources.Glyphs.Count == 0) return null;
        return new SightDisplay(frame);
    }

    public void Apply(in SightStatus s)
    {
        if (!Plugin.HudStatusLine.Value)
        {
            Set(_ready, null);
            Set(_ammo, null);
            Set(_range, null);
            Set(_mode, null);
            return;
        }
        Set(_ready, s.Ready ? "got" : null);
        Set(_ammo, s.Ammo);
        var r = Mathf.Clamp(s.Range, 0, 9999).ToString("D4");
        Set(_range, "d" + r[0], "d" + r[1], "d" + r[2], "d" + r[3]);
        Set(_mode, "osn");
    }

    private static Field MakeField(RectTransform frame, string name, float x0, float x1, float y0, float y1, int glyphs)
    {
        var go = new GameObject("Label" + name);
        go.transform.SetParent(frame, false);
        var box = go.AddComponent<Image>();
        box.color = Color.black;
        box.raycastTarget = false;
        var rt = box.rectTransform;
        rt.anchorMin = new Vector2(x0 / GpuResources.FramePixelsW, 1f - (y1 + 1f) / GpuResources.FramePixelsH);
        rt.anchorMax = new Vector2((x1 + 1f) / GpuResources.FramePixelsW, 1f - y0 / GpuResources.FramePixelsH);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var f = new Field { Box = go, Glyphs = new RawImage[glyphs], PixelW = x1 - x0 + 1f, PixelH = y1 - y0 + 1f };
        for (var i = 0; i < glyphs; i++)
        {
            var g = new GameObject("Glyph" + i);
            g.transform.SetParent(go.transform, false);
            var img = g.AddComponent<RawImage>();
            img.raycastTarget = false;
            img.color = Color.white;
            f.Glyphs[i] = img;
            g.SetActive(false);
        }
        go.SetActive(false);
        return f;
    }

    
    private static void Set(Field f, params string[] keys)
    {
        var id = keys == null || keys.Length == 0 || keys[0] == null ? null : string.Join(",", keys);
        if (id == f.Shown) return;
        f.Shown = id;
        if (id == null)
        {
            f.Box.SetActive(false);
            return;
        }

        
        var n = Mathf.Min(keys.Length, f.Glyphs.Length);
        var widths = new float[n];
        var total = 0f;
        for (var i = 0; i < n; i++)
        {
            if (!GpuResources.Glyphs.TryGetValue(keys[i], out var tex) || tex == null) continue;
            widths[i] = (float)tex.width / tex.height * GlyphHeight * f.PixelH / f.PixelW;
            total += widths[i];
        }
        var x = 0.5f - total * 0.5f;
        for (var i = 0; i < f.Glyphs.Length; i++)
        {
            var img = f.Glyphs[i];
            if (i >= n || widths[i] <= 0f)
            {
                img.gameObject.SetActive(false);
                continue;
            }
            img.texture = GpuResources.Glyphs[keys[i]];
            var rt = img.rectTransform;
            rt.anchorMin = new Vector2(x, (1f - GlyphHeight) * 0.5f);
            rt.anchorMax = new Vector2(x + widths[i], (1f + GlyphHeight) * 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            img.gameObject.SetActive(true);
            x += widths[i];
        }
        f.Box.SetActive(true);
    }
}
