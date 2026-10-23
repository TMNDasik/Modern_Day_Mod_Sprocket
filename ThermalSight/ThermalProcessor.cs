using System;
using System.Threading.Tasks;

namespace ThermalSight;




internal sealed class ThermalProcessor
{
    public int Width { get; private set; }
    public int Height { get; private set; }

    private float[] _sig, _tmp, _prev, _base;
    private float[] _fixedPattern, _columnPattern, _rowFixed, _rowDrift;
    private int[] _deadIndex;
    private byte[] _deadValue;
    private bool _hasPrev;
    private int _frame;
    private float _time;
    private int _seed;
    private int _profileGen = -1;

    private float _agcLo = 0.2f, _agcHi = 0.8f;
    private bool _agcInit;
    private readonly int[] _hist = new int[512];

    public void Setup(int width, int height, int seed, GenerationProfile p)
    {
        if (width == Width && height == Height && seed == _seed && p.Gen == _profileGen) return;
        Width = width; Height = height; _seed = seed; _profileGen = p.Gen;
        var n = width * height;
        _sig = new float[n]; _tmp = new float[n]; _prev = new float[n]; _base = new float[n];
        _hasPrev = false; _agcInit = false;

        var rng = new Random(seed);
        
        _fixedPattern = new float[n];
        var coarse = new float[n];
        for (var i = 0; i < n; i++) coarse[i] = (float)(rng.NextDouble() * 2 - 1);
        BoxBlur(coarse, _tmp, width, height, Math.Max(2, height / 40));
        BoxBlur(coarse, _tmp, width, height, Math.Max(2, height / 40));
        var norm = 0f;
        for (var i = 0; i < n; i++) norm = Math.Max(norm, Math.Abs(coarse[i]));
        for (var i = 0; i < n; i++)
            _fixedPattern[i] = coarse[i] / Math.Max(norm, 1e-4f) * 0.8f + (float)(rng.NextDouble() * 2 - 1) * 0.35f;

        _columnPattern = new float[width];
        for (var x = 0; x < width; x++) _columnPattern[x] = (float)(rng.NextDouble() * 2 - 1);

        _rowFixed = new float[height];
        _rowDrift = new float[height];
        for (var y = 0; y < height; y++)
        {
            _rowFixed[y] = (float)(rng.NextDouble() * 2 - 1);
            _rowDrift[y] = (float)(rng.NextDouble() * 2 - 1);
        }

        _deadIndex = new int[p.DeadPixels];
        _deadValue = new byte[p.DeadPixels];
        for (var i = 0; i < p.DeadPixels; i++)
        {
            
            int x, y;
            do { x = rng.Next(width); y = rng.Next(height); }
            while (Math.Abs(x - width / 2) < width / 12 && Math.Abs(y - height / 2) < height / 12);
            _deadIndex[i] = y * width + x;
            _deadValue[i] = (byte)(rng.NextDouble() < 0.6 ? 255 : 0);
        }
    }

    
    
    public void Process(byte[] rgba, byte[] outRgba, GenerationProfile p, float dt, float slew,
                        float pitchDeg, float vfovDeg, float heatGain)
    {
        int w = Width, h = Height, n = w * h;
        _frame++;
        _time += dt;
        var sig = _sig;

        
        if (_skyRow == null || _skyRow.Length != h) _skyRow = new float[h];
        for (var y = 0; y < h; y++)
        {
            var elev = pitchDeg + ((y + 0.5f) / h - 0.5f) * vfovDeg; 
            _skyRow[y] = 0.06f + 0.22f * MathF.Exp(-MathF.Max(0f, elev) / 6f);
        }
        var skyRow = _skyRow;

        
        
        
        
        
        
        if (_cov == null || _cov.Length != n)
        {
            _cov = new float[n]; _vehRaw = new float[n];
            _bgW = new float[n]; _wB = new float[n]; _vW = new float[n]; _wV = new float[n];
        }
        float[] cov = _cov, vehRaw = _vehRaw, bgW = _bgW, wB = _wB, vW = _vW, wV = _wV;

        Parallel.For(0, h, y =>
        {
            var row = y * w;
            var sky = skyRow[y];
            for (var x = 0; x < w; x++)
            {
                var i = row + x;
                var j = i * 4;
                float r = rgba[j] * (1f / 255f), g = rgba[j + 1] * (1f / 255f), b = rgba[j + 2] * (1f / 255f);
                var mx = r > g ? r : g;
                float bg, c = 0f, veh = 0f;
                if (mx < 0.02f && b < 0.02f) bg = sky;
                else
                {
                    
                    
                    var lum = 0.74f * r + 0.26f * g;
                    if (lum > 0.55f) lum = 0.55f + (lum - 0.55f) * 0.3f;
                    bg = 0.36f + 0.26f * lum;
                    
                    c = Smooth(0.03f, 0.12f, b - mx);
                    
                    veh = SrgbToLinear[rgba[j + 2]] * 1.5f * heatGain; 
                }
                sig[i] = bg; cov[i] = c; vehRaw[i] = veh;
                var wb = c < 0.05f ? 1f : 0f;
                bgW[i] = bg * wb; wB[i] = wb;
                var wv = c > 0.9f ? 1f : 0f;
                vW[i] = veh * wv; wV[i] = wv;
            }
        });

        
        
        BoxBlur(bgW, _tmp, w, h, 2);
        BoxBlur(wB, _tmp, w, h, 2);
        BoxBlur(vW, _tmp, w, h, 1);
        BoxBlur(wV, _tmp, w, h, 1);
        Parallel.For(0, h, y =>
        {
            var row = y * w;
            for (var x = 0; x < w; x++)
            {
                var i = row + x;
                var c = cov[i];
                var bgSmooth = wB[i] > 1e-3f ? bgW[i] / wB[i] : sig[i];
                var bg = c < 0.05f ? sig[i] + (bgSmooth - sig[i]) * 0.5f : bgSmooth;
                if (c <= 0f) { sig[i] = bg; continue; }
                var veh = wV[i] > 1e-3f ? vW[i] / wV[i] : vehRaw[i];
                sig[i] = bg + (veh - bg) * c;
            }
        });

        
        if (p.OpticalBlur > 0.05f) GaussianBlur(sig, _tmp, w, h, p.OpticalBlur, p.OpticalBlur);
        if (p.HorizontalSmear > 0.05f) GaussianBlur(sig, _tmp, w, h, p.HorizontalSmear, 0f);

        
        if (p.Halo > 0.01f)
        {
            var hot = _base;
            Parallel.For(0, n / 4096 + 1, c =>
            {
                var end = Math.Min(n, (c + 1) * 4096);
                for (var i = c * 4096; i < end; i++) hot[i] = MathF.Max(0f, sig[i] - 0.72f);
            });
            var r = Math.Max(2, h / 60);
            BoxBlur(hot, _tmp, w, h, r);
            BoxBlur(hot, _tmp, w, h, r);
            var k = p.Halo * 1.6f;
            Parallel.For(0, n / 4096 + 1, c =>
            {
                var end = Math.Min(n, (c + 1) * 4096);
                for (var i = c * 4096; i < end; i++) sig[i] += hot[i] * k;
            });
        }

        
        var frame = _frame;
        var bandPhase = _time * p.BandingDrift;
        Parallel.For(0, h, y =>
        {
            var state = (uint)(frame * 7919 + y * 104729 + _seed) | 1u;
            var row = y * w;
            var band = 0f;
            if (p.ScanBanding > 0)
            {
                
                var drift = MathF.Sin(bandPhase + _rowDrift[y] * 6.283f);
                band = p.ScanBanding * (0.65f * _rowFixed[y] + 0.35f * drift);
            }
            for (var x = 0; x < w; x++)
            {
                var i = row + x;
                var noise = Gauss(ref state) * p.TemporalNoise;
                var fpn = _fixedPattern[i] * p.FixedPattern + _columnPattern[x] * p.ColumnPattern;
                sig[i] += noise + fpn + band;
            }
        });

        
        if (p.TemporalFilter > 0.01f)
        {
            if (!_hasPrev) Array.Copy(sig, _prev, n);
            var prev = _prev;
            var a0 = p.TemporalFilter;
            Parallel.For(0, h, y =>
            {
                var row = y * w;
                for (var x = 0; x < w; x++)
                {
                    var i = row + x;
                    var d = MathF.Abs(sig[i] - prev[i]);
                    var motion = Smooth(0.015f, 0.08f, d);
                    var a = a0 * (1f - 0.9f * motion);
                    var v = sig[i] + (prev[i] - sig[i]) * a;
                    prev[i] = v;
                    sig[i] = v;
                }
            });
            _hasPrev = true;
        }

        
        if (p.Denoise > 0.01f)
        {
            Array.Copy(sig, _base, n);
            BoxBlur(_base, _tmp, w, h, 1);
            var k = p.Denoise;
            var b = _base;
            Parallel.For(0, n / 4096 + 1, c =>
            {
                var end = Math.Min(n, (c + 1) * 4096);
                for (var i = c * 4096; i < end; i++) sig[i] += (b[i] - sig[i]) * k;
            });
        }

        
        
        ComputeRange(sig, w, h, out var lo, out var hi);
        var t = 1f - MathF.Exp(-dt * p.AgcSpeed);
        if (!_agcInit) { _agcLo = lo; _agcHi = hi; _agcInit = true; }
        else { _agcLo += (lo - _agcLo) * t; _agcHi += (hi - _agcHi) * t; }
        
        const float minSpan = 0.24f;
        var span = _agcHi - _agcLo;
        var range = MathF.Max(span, minSpan);
        var inv = 1f / range;
        var agcLo = span < minSpan ? (_agcLo + _agcHi) * 0.5f - minSpan * 0.5f : _agcLo;
        Parallel.For(0, n / 4096 + 1, c =>
        {
            var end = Math.Min(n, (c + 1) * 4096);
            for (var i = c * 4096; i < end; i++) sig[i] = (sig[i] - agcLo) * inv;
        });

        
        if (p.Equalization > 0.01f)
        {
            UpdateEqualizationLut(sig, w, h, t);
            var lut = _lut;
            var eq = p.Equalization;
            Parallel.For(0, n / 4096 + 1, c =>
            {
                var end = Math.Min(n, (c + 1) * 4096);
                for (var i = c * 4096; i < end; i++)
                {
                    var v = sig[i];
                    var vc = v < 0 ? 0 : v > 1 ? 1 : v;
                    var f = vc * (LutSize - 1);
                    var bi = (int)f;
                    var e = bi >= LutSize - 1 ? lut[LutSize - 1] : lut[bi] + (lut[bi + 1] - lut[bi]) * (f - bi);
                    sig[i] = v + (e - vc) * eq;
                }
            });
        }

        
        if (p.LocalContrast > 0.01f)
        {
            Array.Copy(sig, _base, n);
            var r = Math.Max(3, h / 24);
            BoxBlur(_base, _tmp, w, h, r);
            BoxBlur(_base, _tmp, w, h, r);
            var k = p.LocalContrast * 1.4f;
            var b = _base;
            Parallel.For(0, n / 4096 + 1, c =>
            {
                var end = Math.Min(n, (c + 1) * 4096);
                for (var i = c * 4096; i < end; i++)
                {
                    var detail = sig[i] - b[i];
                    
                    sig[i] = b[i] * (1f - 0.25f * p.LocalContrast) + 0.125f * p.LocalContrast + detail * (1f + k);
                }
            });
        }

        
        var misreg = p.Misregistration * Smooth(25f, 80f, slew);
        if (misreg > 0.3f)
        {
            Array.Copy(sig, _base, n);
            var shift = Math.Max(1, (int)MathF.Round(misreg));
            var b = _base;
            Parallel.For(0, h, y =>
            {
                var row = y * w;
                for (var x = shift; x < w; x++)
                {
                    var i = row + x;
                    
                    var other = b[i - shift];
                    sig[i] = sig[i] * 0.78f + other * 0.22f;
                }
            });
        }

        
        
        var median = SampledMedian(sig, w, h);
        var targetGamma = Math.Clamp(MathF.Log(0.42f) / MathF.Log(Math.Clamp(median, 0.05f, 0.95f)), 0.6f, 2.4f);
        if (_levelGamma <= 0) _levelGamma = targetGamma;
        else _levelGamma += (targetGamma - _levelGamma) * t;

        
        var vig = p.Vignette;
        var gamma = p.Gamma * _levelGamma;
        float cx = w * 0.5f, cy = h * 0.5f;
        var invR2 = 1f / (cx * cx + cy * cy);
        Parallel.For(0, h, y =>
        {
            var row = y * w;
            var dy2 = (y - cy) * (y - cy);
            for (var x = 0; x < w; x++)
            {
                var i = row + x;
                var v = sig[i];
                if (vig > 0) v -= vig * ((x - cx) * (x - cx) + dy2) * invR2;
                v = v < 0 ? 0 : v > 1 ? 1 : v;
                if (gamma != 1f) v = MathF.Pow(v, gamma);
                var bv = (byte)(v * 255f + 0.5f);
                var j = i * 4;
                outRgba[j] = bv; outRgba[j + 1] = bv; outRgba[j + 2] = bv; outRgba[j + 3] = 255;
            }
        });

        for (var i = 0; i < _deadIndex.Length; i++)
        {
            var j = _deadIndex[i] * 4;
            var v = _deadValue[i];
            outRgba[j] = v; outRgba[j + 1] = v; outRgba[j + 2] = v;
        }
    }

    

    private float[] _cov, _vehRaw, _bgW, _wB, _vW, _wV;

    private static readonly float[] SrgbToLinear = BuildSrgbLut();

    private static float[] BuildSrgbLut()
    {
        var lut = new float[256];
        for (var i = 0; i < 256; i++)
        {
            var c = i / 255f;
            lut[i] = c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
        }
        return lut;
    }
    private float _levelGamma;
    private readonly int[] _medHist = new int[256];

    private float SampledMedian(float[] sig, int w, int h)
    {
        Array.Clear(_medHist, 0, _medHist.Length);
        var count = 0;
        for (var y = h / 10; y < h - h / 10; y += 3)
        {
            var row = y * w;
            for (var x = w / 10; x < w - w / 10; x += 3)
            {
                var b = (int)(sig[row + x] * 255f);
                b = b < 0 ? 0 : b > 255 ? 255 : b;
                _medHist[b]++;
                count++;
            }
        }
        var half = count / 2;
        var acc = 0;
        for (var i = 0; i < 256; i++)
        {
            acc += _medHist[i];
            if (acc >= half) return (i + 0.5f) / 255f;
        }
        return 0.5f;
    }

    private const int LutSize = 256;
    private float[] _lut;
    private float[] _skyRow;
    private readonly int[] _eqHist = new int[LutSize];

    private void UpdateEqualizationLut(float[] sig, int w, int h, float t)
    {
        Array.Clear(_eqHist, 0, LutSize);
        var count = 0;
        for (var y = h / 20; y < h - h / 20; y += 2)
        {
            var row = y * w;
            for (var x = w / 20; x < w - w / 20; x += 2)
            {
                var v = sig[row + x];
                var b = (int)(v * (LutSize - 1));
                b = b < 0 ? 0 : b >= LutSize ? LutSize - 1 : b;
                _eqHist[b]++;
                count++;
            }
        }
        if (count == 0) return;

        
        var plateau = Math.Max(1, (int)(count / (float)LutSize * 2f));
        var clipped = 0L;
        for (var i = 0; i < LutSize; i++) clipped += Math.Min(_eqHist[i], plateau);

        var target = new float[LutSize];
        long acc = 0;
        for (var i = 0; i < LutSize; i++)
        {
            acc += Math.Min(_eqHist[i], plateau);
            target[i] = acc / (float)clipped;
        }

        if (_lut == null) { _lut = target; return; }
        for (var i = 0; i < LutSize; i++) _lut[i] += (target[i] - _lut[i]) * t;
    }

    private void ComputeRange(float[] sig, int w, int h, out float lo, out float hi)
    {
        Array.Clear(_hist, 0, _hist.Length);
        const float min = -0.5f, max = 1.5f;
        var scale = (_hist.Length - 1) / (max - min);
        var count = 0;
        
        for (var y = h / 20; y < h - h / 20; y += 2)
        {
            var row = y * w;
            for (var x = w / 20; x < w - w / 20; x += 2)
            {
                var b = (int)((sig[row + x] - min) * scale);
                b = b < 0 ? 0 : b >= _hist.Length ? _hist.Length - 1 : b;
                _hist[b]++;
                count++;
            }
        }
        int loTarget = (int)(count * 0.005f), hiTarget = (int)(count * 0.997f);
        int acc = 0, loBin = 0, hiBin = _hist.Length - 1;
        var loFound = false;
        for (var i = 0; i < _hist.Length; i++)
        {
            acc += _hist[i];
            if (!loFound && acc >= loTarget) { loBin = i; loFound = true; }
            if (acc >= hiTarget) { hiBin = i; break; }
        }
        lo = min + loBin / scale;
        hi = min + (hiBin + 1) / scale;
    }

    private static float Smooth(float e0, float e1, float x)
    {
        var t = Math.Clamp((x - e0) / (e1 - e0), 0f, 1f);
        return t * t * (3 - 2 * t);
    }

    private static float Gauss(ref uint s)
    {
        
        float Next(ref uint st)
        {
            st ^= st << 13; st ^= st >> 17; st ^= st << 5;
            return (st & 0xFFFFFF) * (1f / 16777216f);
        }
        return (Next(ref s) + Next(ref s) + Next(ref s) - 1.5f) * 2f;
    }

    private static void GaussianBlur(float[] data, float[] tmp, int w, int h, float sigmaX, float sigmaY)
    {
        if (sigmaX > 0.05f)
        {
            var k = Kernel(sigmaX);
            var r = k.Length / 2;
            Parallel.For(0, h, y =>
            {
                var row = y * w;
                for (var x = 0; x < w; x++)
                {
                    float s = 0;
                    for (var i = -r; i <= r; i++)
                    {
                        var xx = x + i; xx = xx < 0 ? 0 : xx >= w ? w - 1 : xx;
                        s += data[row + xx] * k[i + r];
                    }
                    tmp[row + x] = s;
                }
            });
            Array.Copy(tmp, data, w * h);
        }
        if (sigmaY > 0.05f)
        {
            var k = Kernel(sigmaY);
            var r = k.Length / 2;
            Parallel.For(0, h, y =>
            {
                var row = y * w;
                for (var x = 0; x < w; x++)
                {
                    float s = 0;
                    for (var i = -r; i <= r; i++)
                    {
                        var yy = y + i; yy = yy < 0 ? 0 : yy >= h ? h - 1 : yy;
                        s += data[yy * w + x] * k[i + r];
                    }
                    tmp[row + x] = s;
                }
            });
            Array.Copy(tmp, data, w * h);
        }
    }

    private static float[] Kernel(float sigma)
    {
        var r = Math.Max(1, (int)MathF.Ceiling(sigma * 2.5f));
        var k = new float[r * 2 + 1];
        float sum = 0;
        for (var i = -r; i <= r; i++) { k[i + r] = MathF.Exp(-(i * i) / (2 * sigma * sigma)); sum += k[i + r]; }
        for (var i = 0; i < k.Length; i++) k[i] /= sum;
        return k;
    }

    
    private static void BoxBlur(float[] data, float[] tmp, int w, int h, int r)
    {
        var norm = 1f / (2 * r + 1);
        Parallel.For(0, h, y =>
        {
            var row = y * w;
            float acc = 0;
            for (var i = -r; i <= r; i++) acc += data[row + Math.Clamp(i, 0, w - 1)];
            for (var x = 0; x < w; x++)
            {
                tmp[row + x] = acc * norm;
                acc += data[row + Math.Min(x + r + 1, w - 1)] - data[row + Math.Max(x - r, 0)];
            }
        });
        Parallel.For(0, w, x =>
        {
            float acc = 0;
            for (var i = -r; i <= r; i++) acc += tmp[Math.Clamp(i, 0, h - 1) * w + x];
            for (var y = 0; y < h; y++)
            {
                data[y * w + x] = acc * norm;
                acc += tmp[Math.Min(y + r + 1, h - 1) * w + x] - tmp[Math.Max(y - r, 0) * w + x];
            }
        });
    }
}
