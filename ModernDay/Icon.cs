using UnityEngine;

namespace ModernDay;


internal static class Icon
{
    private static Sprite _sprite;

    private static readonly Vector2[] Shield =
    {
        new(0.20f, 0.84f), new(0.50f, 0.94f), new(0.80f, 0.84f), new(0.80f, 0.50f), new(0.50f, 0.06f), new(0.20f, 0.50f)
    };

    public static Sprite Get()
    {
        if (_sprite != null && !_sprite.WasCollected) return _sprite;
        const int n = 128;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
        {
            name = "Modern Day icon",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        var px = new Color32[n * n];
        var centre = new Vector2(0.5f, 0.47f);
        for (var y = 0; y < n; y++)
        for (var x = 0; x < n; x++)
        {
            var p = new Vector2((x + 0.5f) / n, (y + 0.5f) / n);
            var a = Cover(Mathf.Abs(PolyDistance(p, Shield)) - 0.035f, n);
            var d = p - centre;
            var r = d.magnitude;
            a = Mathf.Max(a, Cover(r - 0.065f, n));
            var ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            var arc = Mathf.Clamp01((ang - 35f) / 6f) * Mathf.Clamp01((145f - ang) / 6f);
            a = Mathf.Max(a, Cover(Mathf.Abs(r - 0.16f) - 0.028f, n) * arc);
            a = Mathf.Max(a, Cover(Mathf.Abs(r - 0.255f) - 0.028f, n) * arc);
            px[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
        }
        tex.SetPixels32(px);
        tex.Apply(false, false);
        tex.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        _sprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
        _sprite.name = "Modern Day";
        _sprite.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        return _sprite;
    }

    
    private static float Cover(float distance, int size) => Mathf.Clamp01(0.5f - distance * size);

    private static float PolyDistance(Vector2 p, Vector2[] poly)
    {
        var best = float.MaxValue;
        for (var i = 0; i < poly.Length; i++)
        {
            var a = poly[i];
            var b = poly[(i + 1) % poly.Length];
            var ab = b - a;
            var t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            best = Mathf.Min(best, (p - (a + ab * t)).magnitude);
        }
        return best;
    }
}
