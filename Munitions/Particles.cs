using System;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Munitions;


internal struct Particle
{
    public Vector3 Pos, Vel;
    public float Age, Life;
    public float Size0, Size1;      
    public float Rot, RotSpeed;     
    public float Drag;              
    public float Gravity;           
    public float Buoyancy;          
    public Color C0, C1;            
    public float FadeIn;            
    public float FadeOut;           
    public float Boost;             
    public float Stretch;           
    public float MaxLength;         
    public float MinAngular;        
    public float GroundY;           
    public byte Cell;               
    public float Wind;              
    public float Turb;              
    public float Seed;              
}


internal sealed unsafe class ParticleBatch
{
    private const int ArrayHeader = 0x20;

    private readonly bool _sorted;
    private readonly bool _lit;      
    private readonly int _grid;      
    private readonly string _name;
    private Particle[] _items = new Particle[256];
    private int _count;
    private Particle[] _once = new Particle[64]; 
    private int _onceCount;

    private GameObject _go;
    private Mesh _mesh;
    private MeshRenderer _renderer;
    private int _capacity, _lastUsed;
    private Il2CppStructArray<Vector3> _verts;
    private Il2CppStructArray<Vector2> _uv, _uv2;
    private Il2CppStructArray<Color32> _colors;
    private Il2CppStructArray<Vector3> _normals;
    private Il2CppStructArray<Vector4> _tangents;
    private int[] _order = new int[256];
    private float[] _keys = new float[256];

    public ParticleBatch(string name, bool sorted, int grid = 2, bool lit = false)
    {
        _name = name;
        _sorted = sorted;
        _grid = Math.Max(1, grid);
        _lit = lit;
    }

    public int Count => _count;

    public void Add(in Particle p)
    {
        if (_count >= Math.Max(64, Plugin.MaxParticles.Value)) return;
        if (_count == _items.Length) Array.Resize(ref _items, _items.Length * 2);
        _items[_count++] = p;
    }

    public void AddOnce(in Particle p)
    {
        if (_onceCount == _once.Length) Array.Resize(ref _once, _once.Length * 2);
        _once[_onceCount++] = p;
    }

    public void Clear()
    {
        _count = 0;
        _onceCount = 0;
    }

    public void Simulate(float dt)
    {
        if (dt <= 0f) return;
        var g = Physics.gravity;
        var wind = Particles.Wind;
        var time = Time.time;
        for (var i = 0; i < _count;)
        {
            ref var p = ref _items[i];
            p.Age += dt;
            if (p.Age >= p.Life)
            {
                _items[i] = _items[--_count];
                continue;
            }
            p.Vel += (g * p.Gravity + Vector3.up * p.Buoyancy) * dt;
            if (p.Turb > 0f) p.Vel += Particles.Turbulence(p.Pos, time, p.Seed) * (p.Turb * dt);
            if (p.Drag > 0f) p.Vel *= Mathf.Exp(-p.Drag * dt);
            p.Pos += p.Vel * dt;
            if (p.Wind != 0f) p.Pos += wind * (p.Wind * dt);
            p.Rot += p.RotSpeed * dt;
            if (!float.IsNaN(p.GroundY) && p.Pos.y < p.GroundY)
            {
                
                p.Pos.y = p.GroundY;
                p.Vel = new Vector3(p.Vel.x * 0.3f, -p.Vel.y * 0.2f, p.Vel.z * 0.3f);
                p.RotSpeed *= 0.3f;
                if (p.Vel.sqrMagnitude < 1f) { p.Vel = Vector3.zero; p.Gravity = 0f; p.RotSpeed = 0f; }
            }
            i++;
        }
    }

    public void Render(Camera cam, Material material)
    {
        var n = _count + _onceCount;
        if (n == 0 || cam == null || material == null)
        {
            if (_go != null && !_go.WasCollected && _go.activeSelf) _go.SetActive(false);
            _onceCount = 0;
            return;
        }
        if (!EnsureObjects(material)) { _onceCount = 0; return; }
        EnsureCapacity(n);
        if (!_go.activeSelf) _go.SetActive(true);

        var ct = cam.transform;
        var camPos = ct.position;
        var camRight = ct.right;
        var camUp = ct.up;
        var pixelAngle = cam.fieldOfView * Mathf.Deg2Rad / Mathf.Max(240, cam.pixelHeight);

        
        if (_order.Length < n)
        {
            _order = new int[_capacity];
            _keys = new float[_capacity];
        }
        for (var i = 0; i < n; i++)
        {
            _order[i] = i;
            if (_sorted) _keys[i] = -(Get(i).Pos - camPos).sqrMagnitude;
        }
        if (_sorted) Array.Sort(_keys, _order, 0, n);

        var pv = (Vector3*)((byte*)_verts.Pointer + ArrayHeader);
        var puv = (Vector2*)((byte*)_uv.Pointer + ArrayHeader);
        var puv2 = (Vector2*)((byte*)_uv2.Pointer + ArrayHeader);
        var pc = (Color32*)((byte*)_colors.Pointer + ArrayHeader);
        var pn = _lit ? (Vector3*)((byte*)_normals.Pointer + ArrayHeader) : null;
        var pt = _lit ? (Vector4*)((byte*)_tangents.Pointer + ArrayHeader) : null;
        var cellSize = 1f / _grid;
        var lo = 0.004f * 2f / _grid;
        var hi = cellSize - lo;

        for (var q = 0; q < n; q++)
        {
            ref var p = ref Get(_order[q]);
            var t = p.Life > 0f ? Mathf.Clamp01(p.Age / p.Life) : 0f;
            var grow = 1f - (1f - t) * (1f - t);
            var size = Mathf.Lerp(p.Size0, p.Size1, grow);
            var dist = (p.Pos - camPos).magnitude;
            if (p.MinAngular > 0f) size = Mathf.Max(size, dist * pixelAngle * p.MinAngular);
            var col = Color.Lerp(p.C0, p.C1, t);
            if (p.FadeOut > 0f)
            {
                col.a = p.C0.a;
                if (t > 1f - p.FadeOut) col.a *= Mathf.Clamp01((1f - t) / p.FadeOut);
            }
            if (p.FadeIn > 0f && t < p.FadeIn) col.a *= t / p.FadeIn;

            Vector3 a, b, c, d;
            Vector3 axisR = camRight, axisU = camUp;
            if (p.Stretch > 0f && p.Vel.sqrMagnitude > 1e-4f)
            {
                var speed = p.Vel.magnitude;
                var dir = p.Vel / speed;
                var len = Mathf.Clamp(speed * p.Stretch, size, p.MaxLength > 0f ? p.MaxLength : 1e4f);
                var side = Vector3.Cross(dir, camPos - p.Pos);
                side = side.sqrMagnitude > 1e-8f ? side.normalized * (size * 0.5f) : camUp * (size * 0.5f);
                var tail = p.Pos - dir * len;
                a = tail - side; b = p.Pos - side; c = p.Pos + side; d = tail + side;
            }
            else
            {
                var h = size * 0.5f;
                var cs = Mathf.Cos(p.Rot);
                var sn = Mathf.Sin(p.Rot);
                axisR = camRight * cs + camUp * sn;
                axisU = camUp * cs - camRight * sn;
                var r = axisR * h;
                var u = axisU * h;
                a = p.Pos - r - u; b = p.Pos + r - u; c = p.Pos + r + u; d = p.Pos - r + u;
            }

            var v0 = q * 4;
            pv[v0] = a; pv[v0 + 1] = b; pv[v0 + 2] = c; pv[v0 + 3] = d;
            var cell = p.Cell % (_grid * _grid);
            var cx = (cell % _grid) * cellSize;
            var cy = (cell / _grid) * cellSize;
            puv[v0] = new Vector2(cx + lo, cy + lo);
            puv[v0 + 1] = new Vector2(cx + hi, cy + lo);
            puv[v0 + 2] = new Vector2(cx + hi, cy + hi);
            puv[v0 + 3] = new Vector2(cx + lo, cy + hi);
            if (_lit)
            {
                
                var seedAge = new Vector2(p.Seed, t);
                puv2[v0] = puv2[v0 + 1] = puv2[v0 + 2] = puv2[v0 + 3] = seedAge;
                var tan = new Vector4(axisU.x, axisU.y, axisU.z, 1f);
                pn[v0] = pn[v0 + 1] = pn[v0 + 2] = pn[v0 + 3] = axisR;
                pt[v0] = pt[v0 + 1] = pt[v0 + 2] = pt[v0 + 3] = tan;
                var lc = (Color32)new Color(Mathf.Clamp01(col.r), Mathf.Clamp01(col.g), Mathf.Clamp01(col.b), Mathf.Clamp01(col.a));
                pc[v0] = pc[v0 + 1] = pc[v0 + 2] = pc[v0 + 3] = lc;
                continue;
            }
            var boost = new Vector2(p.Boost > 0f ? p.Boost : 1f, 0f);
            puv2[v0] = puv2[v0 + 1] = puv2[v0 + 2] = puv2[v0 + 3] = boost;
            if (_sorted && p.Stretch <= 0f)
            {
                
                var hInv = 1f / Mathf.Max(size * 0.5f, 1e-4f);
                pc[v0] = Shade(col, a - p.Pos, camUp, hInv);
                pc[v0 + 1] = Shade(col, b - p.Pos, camUp, hInv);
                pc[v0 + 2] = Shade(col, c - p.Pos, camUp, hInv);
                pc[v0 + 3] = Shade(col, d - p.Pos, camUp, hInv);
            }
            else
            {
                var c32 = (Color32)new Color(Mathf.Clamp01(col.r), Mathf.Clamp01(col.g), Mathf.Clamp01(col.b), Mathf.Clamp01(col.a));
                pc[v0] = pc[v0 + 1] = pc[v0 + 2] = pc[v0 + 3] = c32;
            }
        }
        
        for (var q = n; q < _lastUsed; q++)
        {
            var v0 = q * 4;
            pv[v0] = pv[v0 + 1] = pv[v0 + 2] = pv[v0 + 3] = camPos;
            pc[v0] = pc[v0 + 1] = pc[v0 + 2] = pc[v0 + 3] = new Color32(0, 0, 0, 0);
        }
        _lastUsed = n;
        _onceCount = 0;

        _mesh.vertices = _verts;
        _mesh.uv = _uv;
        _mesh.uv2 = _uv2;
        _mesh.colors32 = _colors;
        if (_lit)
        {
            _mesh.normals = _normals;
            _mesh.tangents = _tangents;
        }
        _mesh.bounds = new Bounds(camPos, new Vector3(40000f, 40000f, 40000f));
    }

    private ref Particle Get(int i) => ref i < _count ? ref _items[i] : ref _once[i - _count];

    private static Color32 Shade(Color col, Vector3 offset, Vector3 camUp, float hInv)
    {
        var up = Vector3.Dot(offset, camUp) * hInv * 0.7071f; 
        var f = 0.95f + 0.13f * up;
        return new Color(Mathf.Clamp01(col.r * f), Mathf.Clamp01(col.g * f), Mathf.Clamp01(col.b * f), Mathf.Clamp01(col.a));
    }

    private bool EnsureObjects(Material material)
    {
        if (_go != null && !_go.WasCollected && _mesh != null && !_mesh.WasCollected)
        {
            if (_renderer.sharedMaterial == null || _renderer.sharedMaterial.Pointer != material.Pointer) _renderer.sharedMaterial = material;
            return true;
        }
        try
        {
            _go = new GameObject("Munitions " + _name);
            Object.DontDestroyOnLoad(_go);
            _mesh = new Mesh { name = "Munitions " + _name };
            _mesh.MarkDynamic();
            _mesh.hideFlags = HideFlags.DontUnloadUnusedAsset;
            _go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer = _go.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = material;
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _capacity = 0;
            _lastUsed = 0;
            return true;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Particle renderer '{_name}' failed: {e.Message}");
            return false;
        }
    }

    private void EnsureCapacity(int n)
    {
        if (n <= _capacity) return;
        var cap = Math.Max(64, _capacity);
        while (cap < n) cap *= 2;
        _capacity = cap;
        _verts = new Il2CppStructArray<Vector3>(cap * 4);
        _uv = new Il2CppStructArray<Vector2>(cap * 4);
        _uv2 = new Il2CppStructArray<Vector2>(cap * 4);
        _colors = new Il2CppStructArray<Color32>(cap * 4);
        if (_lit)
        {
            _normals = new Il2CppStructArray<Vector3>(cap * 4);
            _tangents = new Il2CppStructArray<Vector4>(cap * 4);
        }
        var tris = new int[cap * 6];
        for (var q = 0; q < cap; q++)
        {
            int v = q * 4, t = q * 6;
            tris[t] = v; tris[t + 1] = v + 1; tris[t + 2] = v + 2;
            tris[t + 3] = v; tris[t + 4] = v + 2; tris[t + 5] = v + 3;
        }
        _mesh.Clear();
        if (cap * 4 > 65000) _mesh.indexFormat = IndexFormat.UInt32;
        _mesh.vertices = _verts;
        _mesh.triangles = tris;
        _lastUsed = cap;
        _order = new int[cap];
        _keys = new float[cap];
    }
}


internal static class Particles
{
    public static readonly ParticleBatch Glow = new("glow", false);
    public static readonly ParticleBatch Smoke = new("smoke", true);
    
    public static readonly ParticleBatch SmokeLit = new("smoke lit", true, 4, true);

    
    public static readonly Vector3 Wind = MakeWind();

    private static Vector3 MakeWind()
    {
        var rng = new System.Random();
        var ang = (float)rng.NextDouble() * Mathf.PI * 2f;
        var speed = 0.18f + 0.2f * (float)rng.NextDouble();
        return new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * speed;
    }

    
    public static Vector3 Turbulence(Vector3 p, float t, float seed)
    {
        var s = seed * 6.2831853f;
        return new Vector3(
            Mathf.Sin(p.z * 0.31f + t * 0.53f + s) + 0.5f * Mathf.Sin(p.y * 0.47f - t * 0.37f + s * 1.7f),
            0.35f * (Mathf.Sin(p.x * 0.29f + t * 0.41f + s * 2.3f) + Mathf.Cos(p.z * 0.37f - t * 0.29f + s)),
            Mathf.Cos(p.x * 0.33f - t * 0.47f + s * 0.7f) + 0.5f * Mathf.Cos(p.y * 0.43f + t * 0.31f + s * 2.9f));
    }

    private static float _nextLightCheck;
    private static Light _sun;

    public static void Tick(Camera cam)
    {
        var dt = Time.deltaTime;
        Effects.TickEmitters(dt);
        Glow.Simulate(dt);
        Smoke.Simulate(dt);
        SmokeLit.Simulate(dt);
        UpdateLighting();
        Glow.Render(cam, Fx.FxAddMat);
        Smoke.Render(cam, Fx.FxAlphaMat);
        SmokeLit.Render(cam, Fx.SmokeLitMat);
    }

    
    private static void UpdateLighting()
    {
        try
        {
            if (Time.unscaledTime >= _nextLightCheck)
            {
                _nextLightCheck = Time.unscaledTime + 5f;
                _sun = null;
                var best = 0f;
                foreach (var l in Resources.FindObjectsOfTypeAll<Light>())
                {
                    if (l == null || l.type != LightType.Directional || !l.isActiveAndEnabled) continue;
                    if (l.intensity > best) { best = l.intensity; _sun = l; }
                }
            }
            var darken = 0f;
            var toSun = new Vector3(0.3f, 0.8f, 0.25f).normalized;
            var sunColor = new Color(1f, 0.97f, 0.92f);
            var day = 1f;
            if (_sun != null && !_sun.WasCollected)
            {
                toSun = -_sun.transform.forward;
                var elevation = toSun.y;
                day = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.05f, 0.3f, elevation));
                darken = 1f - Mathf.Lerp(0.1f, 1f, day);
                var c = _sun.color;
                try { if (_sun.useColorTemperature) c *= Mathf.CorrelatedColorTemperatureToRGB(_sun.colorTemperature); } catch { }
                var max = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
                if (max > 1e-3f) sunColor = new Color(c.r / max, c.g / max, c.b / max);
                
                sunColor = Color.Lerp(new Color(1f, 0.72f, 0.5f), sunColor, Mathf.InverseLerp(0.05f, 0.45f, elevation));
            }
            Shader.SetGlobalFloat("_MunitionsDarken", darken);
            Shader.SetGlobalVector("_MunitionsSunDir", new Vector4(toSun.x, toSun.y, toSun.z, 1f));
            var strength = 0.6f * Mathf.Lerp(0.35f, 1f, day);
            Shader.SetGlobalVector("_MunitionsSunColor", new Vector4(sunColor.r, sunColor.g, sunColor.b, strength));
            Shader.SetGlobalVector("_MunitionsSky", new Vector4(0.52f, 0.55f, 0.6f, 1f));
            Shader.SetGlobalVector("_MunitionsGround", new Vector4(0.4f, 0.38f, 0.35f, 1f));
        }
        catch { }
    }
}
