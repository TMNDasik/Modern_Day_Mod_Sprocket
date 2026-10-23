using System;
using System.Collections.Generic;
using UnityEngine;

namespace ThermalSight;











internal sealed class ThermalDamage
{
    private struct Hit
    {
        public Vector2 Uv;
        public float Strength;
        public int Seed;
        public float Time;
    }

    private const float MaxDelay = 8f, MaxArrival = 12f;

    private readonly List<Hit> _display = new(), _head = new();
    public float DisplayHealth = 1f, HeadHealth = 1f;
    public bool Display => _display.Count > 0;
    public bool Head => _head.Count > 0;
    public bool Any => Display || Head;

    private float _epoch;   
    private Texture2D _cols, _rows, _map, _win, _sensor;
    private int _w, _h, _sw, _sh, _builtDisplay = -1, _builtHead = -1;
    private float _nextFreeze, _freezeUntil;
    private readonly System.Random _freezeRng = new();
    private static int _seedCounter;

    public void AddDisplay(Vector2 uv, float strength) => _display.Add(NewHit(uv, strength));

    public void AddHead(Vector2 uv, float strength)
    {
        _head.Add(NewHit(uv, strength));
        if (_nextFreeze <= 0f) _nextFreeze = Time.unscaledTime + 1.5f;
    }

    private static Hit NewHit(Vector2 uv, float strength) => new()
    {
        Uv = new Vector2(Mathf.Clamp01(uv.x), Mathf.Clamp01(uv.y)),
        Strength = Mathf.Clamp01(strength),
        Seed = unchecked(Environment.TickCount * 7919 ^ ++_seedCounter * (int)0x9E3779B1),
        Time = UnityEngine.Time.unscaledTime
    };

    
    public bool Frozen(float now)
    {
        if (!Head || HeadHealth >= 0.5f || HeadHealth <= 0f) return false;
        if (now >= _nextFreeze)
        {
            _freezeUntil = now + (0.3f + 1.7f * (float)_freezeRng.NextDouble()) * (1.2f - HeadHealth);
            _nextFreeze = _freezeUntil + 2f + 9f * (float)_freezeRng.NextDouble() * HeadHealth * 2f;
        }
        return now < _freezeUntil;
    }

    public void Release()
    {
        foreach (var t in new[] { _cols, _rows, _map, _win, _sensor })
            if (t != null) UnityEngine.Object.Destroy(t);
        _cols = _rows = _map = _win = _sensor = null;
        _builtDisplay = _builtHead = -1;
    }

    
    public void Prepare(int w, int h, int sw, int sh)
    {
        var resized = w != _w || h != _h;
        if (resized) { _w = w; _h = h; _builtDisplay = _builtHead = -1; }
        if (sw != _sw || sh != _sh) { _sw = sw; _sh = sh; _builtHead = -1; }
        if (Display && _builtDisplay != _display.Count) { BuildDisplay(); _builtDisplay = _display.Count; }
        if (Head && _builtHead != _head.Count) { BuildHead(); _builtHead = _head.Count; }
    }

    public void Apply(Material m, Rect uvRect, float now, int frame)
    {
        m.SetVector("_UvRect", new Vector4(uvRect.x, uvRect.y, uvRect.width, uvRect.height));
        m.SetVector("_Size", new Vector4(_w, _h, 1f / _w, 1f / _h));
        m.SetFloat("_Time2", now % 3600f);
        m.SetFloat("_Frame", frame % 10000);
        m.SetFloat("_Display", Display ? 1f : 0f);
        m.SetFloat("_Head", Head ? 1f : 0f);
        if (Display)
        {
            m.SetTexture("_Cols", _cols);
            m.SetTexture("_Rows", _rows);
            m.SetTexture("_Map", _map);
            m.SetFloat("_Age", Mathf.Min(now - _epoch, MaxArrival - 0.1f));
            m.SetFloat("_Broken", 1f - DisplayHealth);
            m.SetFloat("_Dead", DisplayHealth <= 0f ? 1f : 0f);
        }
        if (Head)
        {
            m.SetTexture("_Win", _win);
            m.SetTexture("_Sensor", _sensor);
            m.SetFloat("_SensorTex", _sensor.width);
            m.SetFloat("_HeadBlur", 1.5f + 5f * (1f - HeadHealth));
            m.SetFloat("_Haze", (1f - HeadHealth) * 0.3f);
            m.SetFloat("_NoSignal", HeadHealth <= 0f ? 1f : 0f);
        }
    }

    

    private void BuildDisplay()
    {
        int w = _w, h = _h, mw = (w + 1) / 2, mh = (h + 1) / 2;
        _epoch = _display[_display.Count - 1].Time;
        var cols = new Color32[w * 3];
        var rows = new Color32[h * 3];

        
        
        OpticsDamage.Painter.Begin(mw, mh);
        var destroyed = new System.Random(_display[0].Seed ^ 0x5bd1e995);
        var extra = new List<Vector2>();
        foreach (var hit in _display)
        {
            var rng = new System.Random(hit.Seed);
            var c = new Vector2(hit.Uv.x * mw, hit.Uv.y * mh);
            var longCracks = 2 + (int)(hit.Strength * 3f + (float)rng.NextDouble());
            for (var i = 0; i < longCracks; i++)
                Curve(rng, c, (float)(rng.NextDouble() * Math.PI * 2), mw, mh);
        }
        if (DisplayHealth <= 0f)
            for (var i = 0; i < 3; i++)
            {
                var c = new Vector2((float)destroyed.NextDouble() * mw, (float)destroyed.NextDouble() * mh);
                extra.Add(c);
                Curve(destroyed, c, (float)(destroyed.NextDouble() * Math.PI * 2), mw, mh);
            }
        var walls = (float[])OpticsDamage.Painter.White.Clone();
        foreach (var hit in _display)
        {
            var rng = new System.Random(hit.Seed ^ 0x7FEB352D);
            var c = new Vector2(hit.Uv.x * mw, hit.Uv.y * mh);
            var s = hit.Strength;
            OpticsDamage.Painter.Impact(rng, c, 0.35f + 0.55f * s, mh, 0.55f);
            
            OpticsDamage.Painter.Disc(c, mh * (0.008f + 0.014f * s), 1f, 0f, rng);
            OpticsDamage.Painter.Disc(c, mh * (0.02f + 0.03f * s), 0.45f, 0f, rng);
        }
        foreach (var c in extra) OpticsDamage.Painter.Impact(destroyed, c, 0.8f, mh, 0.5f);
        var white = OpticsDamage.Painter.White;
        var dark = OpticsDamage.Painter.Dark;

        var arrival = new float[mw * mh];
        Array.Fill(arrival, float.MaxValue);
        foreach (var hit in _display)
        {
            if (hit.Strength < 0.2f) continue;
            var rng = new System.Random(hit.Seed ^ 0x2545F491);
            Leak(arrival, walls, mw, mh, new Vector2(hit.Uv.x * mw, hit.Uv.y * mh), hit.Strength, rng, hit.Time - _epoch);
        }
        var rim = Rim(arrival, mw, mh);

        foreach (var hit in _display)
        {
            var rng = new System.Random(hit.Seed ^ 0x68E31DA4);
            Columns(cols, w, hit, rng, hit.Time - _epoch);
            Rows(rows, w, h, hit, rng, hit.Time - _epoch);
        }
        if (DisplayHealth <= 0f)
        {
            
            var rng = new System.Random(_display[0].Seed ^ 0x1B873593);
            for (var x = 0; x < w; x++)
                if (rng.NextDouble() < 0.07) SetLine(cols, w, x, Pick(rng), x % 3, 0f, 1f, 0f, rng);
        }

        var map = new Color32[mw * mh];
        for (var i = 0; i < map.Length; i++)
        {
            var t = arrival[i];
            var r = t == float.MaxValue ? (byte)255 : (byte)Mathf.Clamp(Mathf.RoundToInt(t / MaxArrival * 254f), 0, 254);
            map[i] = new Color32(r, (byte)(Mathf.Clamp01(white[i]) * 255f), (byte)(Mathf.Clamp01(dark[i]) * 255f), (byte)(rim[i] * 255f));
        }

        Upload(ref _cols, w, 3, cols, FilterMode.Point);
        Upload(ref _rows, h, 3, rows, FilterMode.Point);
        Upload(ref _map, mw, mh, map, FilterMode.Bilinear);
    }

    
    private static void Curve(System.Random rng, Vector2 p, float angle, int mw, int mh)
    {
        var turn = 0f;
        for (var i = 0; i < 6000; i++)
        {
            turn = Mathf.Clamp(turn + (float)(rng.NextDouble() - 0.5) * 0.01f, -0.018f, 0.018f);
            angle += turn;
            var next = p + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 3f;
            OpticsDamage.Painter.Line(p, next, 0.9f);
            p = next;
            if (p.x < -2 || p.y < -2 || p.x > mw + 2 || p.y > mh + 2) break;
            if (rng.NextDouble() < 0.004)
                OpticsDamage.Painter.Crack(rng, p, angle + (rng.NextDouble() < 0.5 ? -1f : 1f) * (0.4f + (float)rng.NextDouble() * 0.6f),
                    mh * (0.04f + 0.12f * (float)rng.NextDouble()), 0.7f, 1);
        }
    }

    
    
    private static void Leak(float[] arrival, float[] white, int mw, int mh, Vector2 c, float s, System.Random rng, float offset)
    {
        var limit = mh * (0.12f + 0.6f * s) * 5f;
        var spread = 1.2f + 2.8f * (float)rng.NextDouble();
        var p1 = (float)rng.NextDouble() * 6.283f;
        var p2 = (float)rng.NextDouble() * 6.283f;
        var seed = rng.Next();
        var cell = mh * 0.07f;
        var dist = new int[mw * mh];
        Array.Fill(dist, int.MaxValue);
        const int ring = 16;
        var buckets = new Queue<int>[ring];
        for (var i = 0; i < ring; i++) buckets[i] = new Queue<int>();
        int cx = Mathf.Clamp((int)c.x, 0, mw - 1), cy = Mathf.Clamp((int)c.y, 0, mh - 1);
        var start = cy * mw + cx;
        dist[start] = 0;
        buckets[0].Enqueue(start);
        var pending = 1;
        for (var d = 0; pending > 0; d++)
        {
            var q = buckets[d % ring];
            while (q.Count > 0)
            {
                var i = q.Dequeue();
                pending--;
                if (dist[i] != d) continue;
                int x = i % mw, y = i / mw;
                
                var a = Mathf.Atan2(y - cy, x - cx);
                var blot = 0.65f * Noise(x / cell, y / cell, seed) + 0.35f * Noise(x / cell * 3.1f, y / cell * 3.1f, seed + 77);
                var lim = limit * (0.5f + 1.0f * blot) * (1f + 0.15f * Mathf.Sin(a * 3f + p1) + 0.1f * Mathf.Sin(a * 5f + p2));
                if (d > lim) continue;
                var t = Mathf.Max(0f, offset + spread * d / limit);
                if (t < arrival[i]) arrival[i] = t;
                for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= mw || ny >= mh) continue;
                    var n = ny * mw + nx;
                    if (white[n] > 0.15f) continue;
                    var nd = d + (dx != 0 && dy != 0 ? 7 : 5) + (Mix(n, seed) & 7);
                    if (nd >= dist[n]) continue;
                    dist[n] = nd;
                    buckets[nd % ring].Enqueue(n);
                    pending++;
                }
            }
        }
    }

    
    private static float[] Rim(float[] arrival, int mw, int mh)
    {
        var rim = new float[mw * mh];
        for (var y = 1; y < mh - 1; y++)
        for (var x = 1; x < mw - 1; x++)
        {
            var i = y * mw + x;
            if (arrival[i] == float.MaxValue) continue;
            if (arrival[i - 1] != float.MaxValue && arrival[i + 1] != float.MaxValue &&
                arrival[i - mw] != float.MaxValue && arrival[i + mw] != float.MaxValue) continue;
            for (var dy = -3; dy <= 3; dy++)
            for (var dx = -3; dx <= 3; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= mw || ny >= mh) continue;
                var n = ny * mw + nx;
                if (arrival[n] != float.MaxValue) continue;
                var k = 1f - Mathf.Sqrt(dx * dx + dy * dy) / 3.5f;
                if (k > rim[n]) rim[n] = k;
            }
        }
        return rim;
    }

    
    private static float Noise(float x, float y, int seed)
    {
        int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
        float fx = x - ix, fy = y - iy;
        fx = fx * fx * (3f - 2f * fx);
        fy = fy * fy * (3f - 2f * fy);
        float Corner(int cx, int cy) => (Mix(cx * 92821 + cy, seed) & 1023) / 1023f;
        return Mathf.Lerp(Mathf.Lerp(Corner(ix, iy), Corner(ix + 1, iy), fx), Mathf.Lerp(Corner(ix, iy + 1), Corner(ix + 1, iy + 1), fx), fy);
    }

    private static int Mix(int n, int seed)
    {
        unchecked
        {
            var h = (uint)(n * 374761393 + seed * 668265263);
            h = (h ^ (h >> 13)) * 1274126177;
            return (int)(h ^ (h >> 16));
        }
    }

    
    private static int Pick(System.Random rng)
    {
        var r = rng.NextDouble();
        return r < 0.5 ? 1 : r < 0.65 ? 3 : r < 0.77 ? 2 : r < 0.88 ? 4 : 5;
    }

    
    private static void Columns(Color32[] cols, int w, Hit hit, System.Random rng, float offset)
    {
        var s = hit.Strength;
        var cx = (int)(hit.Uv.x * w);
        var cy = hit.Uv.y;
        var bw = w * (0.03f + 0.2f * s) * (0.6f + 0.8f * (float)rng.NextDouble());
        var left = bw * (0.2f + 1.6f * (float)rng.NextDouble());
        var right = bw * (0.2f + 1.6f * (float)rng.NextDouble());
        var pmax = 0.3f + 0.6f * s;
        int x0 = Math.Max(0, cx - (int)(left * 3.5f)), x1 = Math.Min(w - 1, cx + (int)(right * 3.5f));
        for (var x = x0; x <= x1; x++)
        {
            
            var dx = x - cx;
            var q = Mathf.Exp(-Mathf.Abs(dx) / Mathf.Max(1f, dx < 0 ? left : right));
            if (rng.NextDouble() > pmax * q + 0.015f) continue;
            var run = rng.NextDouble() < 0.35 ? 1 + rng.Next(4) : 1;   
            var type = Pick(rng);
            var colour = rng.NextDouble() < 0.55 ? x % 3 : 3 + rng.Next(5);
            float y0, y1;
            var e = rng.NextDouble();
            var j = (float)(rng.NextDouble() - 0.5) * 0.08f;
            if (e < 0.45) { y0 = 0f; y1 = 1f; }
            else if (e < 0.75) { y0 = Mathf.Clamp01(cy + j); y1 = 1f; }
            else { y0 = 0f; y1 = Mathf.Clamp01(cy + j); }
            var delay = rng.NextDouble() < 0.7 ? 0.15f * (float)rng.NextDouble() : 0.3f + 4.5f * (float)rng.NextDouble();
            for (var k = 0; k < run && x + k < w; k++)
                SetLine(cols, w, x + k, type, run > 1 ? (x + k) % 3 : colour, y0, y1, Mathf.Max(0f, offset + delay), rng);
            x += run - 1;
        }
    }

    
    private static void Rows(Color32[] rows, int w, int h, Hit hit, System.Random rng, float offset)
    {
        var s = hit.Strength;
        var thin = rng.Next(1 + (int)(s * 3f));
        for (var i = 0; i < thin; i++)
        {
            var y = (int)(Mathf.Clamp01(hit.Uv.y + (float)(rng.NextDouble() - 0.5) * 0.7f) * (h - 1));
            var height = 1 + rng.Next(2);
            var type = rng.NextDouble() < 0.4 ? 3 : rng.NextDouble() < 0.6 ? 1 : 4;
            var e = rng.NextDouble();
            float x0 = 0f, x1 = 1f;
            if (e < 0.25) x1 = hit.Uv.x;
            else if (e < 0.5) x0 = hit.Uv.x;
            var colour = rng.Next(8);
            var delay = rng.NextDouble() < 0.6 ? 0f : 0.5f + 3f * (float)rng.NextDouble();
            for (var k = 0; k < height && y + k < h; k++)
                SetLine(rows, h, y + k, type, colour, x0, x1, Mathf.Max(0f, offset + delay), rng);
        }
        if (rng.NextDouble() < 0.25 + 0.4 * s)
        {
            var y = (int)(Mathf.Clamp01(hit.Uv.y + (float)(rng.NextDouble() - 0.5) * 0.8f) * (h - 1));
            var height = 6 + rng.Next(25);
            var a = (float)rng.NextDouble() * 0.5f;
            var b = a + 0.3f + 0.5f * (float)rng.NextDouble();
            var delay = rng.NextDouble() < 0.5 ? 0f : 0.3f + 2f * (float)rng.NextDouble();
            for (var k = 0; k < height && y + k < h; k++)
                if (rng.NextDouble() < 0.7) SetLine(rows, h, y + k, 3, rng.Next(8), a, Mathf.Min(1f, b), Mathf.Max(0f, offset + delay), rng);
        }
    }

    private static void SetLine(Color32[] tex, int n, int i, int type, int colour, float from, float to, float delay, System.Random rng)
    {
        var group = rng.NextDouble() < 0.22 ? 1 + rng.Next(7) : 0;
        var dash = 2 + rng.Next(13);
        var bright = 140 + rng.Next(116);
        var frozen = Mathf.Clamp01(from + (to - from) * (float)rng.NextDouble());
        var a = Word(from);
        var b = Word(to);
        var f = Word(frozen);
        tex[i] = new Color32((byte)type, (byte)(colour + group * 8), (byte)dash, (byte)Mathf.Clamp(Mathf.RoundToInt(delay / MaxDelay * 255f), 0, 255));
        tex[n + i] = new Color32((byte)(a >> 8), (byte)a, (byte)(b >> 8), (byte)b);
        tex[2 * n + i] = new Color32((byte)(f >> 8), (byte)f, (byte)bright, 0);
    }

    private static int Word(float v) => Mathf.Clamp(Mathf.RoundToInt(v * 65535f), 0, 65535);

    

    private void BuildHead()
    {
        int mw = (_w + 1) / 2, mh = (_h + 1) / 2;
        OpticsDamage.Painter.Begin(mw, mh);
        var haze = new float[mw * mh];
        var sensorW = Math.Max(Math.Max(_sw, _sh), 1);
        var sensor = new Color32[sensorW * 2];
        foreach (var hit in _head)
        {
            var rng = new System.Random(hit.Seed);
            var s = hit.Strength;
            var c = new Vector2(hit.Uv.x * mw, hit.Uv.y * mh);
            OpticsDamage.Painter.Impact(rng, c, 0.3f + 0.6f * s, mh, 0.9f);

            
            var radius = mh * (0.15f + 0.35f * s);
            int x0 = Math.Max(0, (int)(c.x - radius * 2f)), x1 = Math.Min(mw - 1, (int)(c.x + radius * 2f));
            int y0 = Math.Max(0, (int)(c.y - radius * 2f)), y1 = Math.Min(mh - 1, (int)(c.y + radius * 2f));
            for (var y = y0; y <= y1; y++)
            for (var x = x0; x <= x1; x++)
            {
                var d = new Vector2(x - c.x, y - c.y).magnitude / radius;
                haze[y * mw + x] += 0.6f * s * Mathf.Exp(-d * d);
            }

            
            var columns = 1 + (int)(s * 5f);
            for (var i = 0; i < columns; i++)
            {
                var x = Mathf.Clamp((int)((hit.Uv.x + (rng.NextDouble() - 0.5) * 0.3) * _sw), 0, _sw - 1);
                sensor[x] = Fault(rng);
            }
            var lines = (int)(s * 3f + rng.NextDouble());
            for (var i = 0; i < lines; i++)
            {
                var y = Mathf.Clamp((int)((hit.Uv.y + (rng.NextDouble() - 0.5) * 0.4) * _sh), 0, _sh - 1);
                sensor[sensorW + y] = Fault(rng);
            }
        }
        var white = OpticsDamage.Painter.White;
        var dark = OpticsDamage.Painter.Dark;
        var win = new Color32[mw * mh];
        for (var i = 0; i < win.Length; i++)
            win[i] = new Color32((byte)(Mathf.Clamp01(white[i] * 0.8f) * 255f), (byte)(Mathf.Clamp01(haze[i]) * 255f),
                (byte)(Mathf.Clamp01(dark[i]) * 255f), 255);
        Upload(ref _win, mw, mh, win, FilterMode.Bilinear);
        Upload(ref _sensor, sensorW, 2, sensor, FilterMode.Point);
    }

    private static Color32 Fault(System.Random rng)
    {
        var r = rng.NextDouble();
        var type = r < 0.4 ? 1 : r < 0.65 ? 2 : 3;
        return new Color32((byte)type, (byte)(150 + rng.Next(106)), 0, 0);
    }

    private static void Upload(ref Texture2D tex, int w, int h, Color32[] px, FilterMode filter)
    {
        if (tex == null || tex.width != w || tex.height != h)
        {
            if (tex != null) UnityEngine.Object.Destroy(tex);
            
            tex = new Texture2D(w, h, TextureFormat.RGBA32, false, true) { wrapMode = TextureWrapMode.Clamp, filterMode = filter };
        }
        tex.SetPixels32(px);
        tex.Apply(false);
    }
}
