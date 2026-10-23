using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Sprocket.DamageModelling;
using UnityEngine;
using Random = System.Random;

namespace ThermalSight;




internal sealed class ThermalEffects
{
    private struct Puff
    {
        public Vector3 Pos, Vel;
        public float Radius, Growth;
        public float Temp, TempEnd, CoolTau;     
        public float Opacity, FadeTau;          
        public float Age, Life, Buoyancy, Drag, Turbulence, Seed;
    }

    private sealed class HotSpot
    {
        public Transform Parent;
        public Vector3 LocalPos, LocalNormal, Forward;
        public float Temp, TempEnd, CoolTau, Radius, Age, Life, Opacity = 1f, Stretch = 1f;
        public bool HasForward;
    }

    private sealed class Emitter
    {
        public Component Source;   
        public Transform Anchor;
        public int Kind;           
        public float Accumulator, SmokeAccumulator;
    }

    public static ThermalEffects Instance;

    private readonly List<Puff> _puffs = new();
    private readonly List<HotSpot> _spots = new();
    private readonly List<Emitter> _emitters = new();
    private readonly Random _rng = new();
    private Material _gasMaterial;
    private static readonly Vector3 Wind = new(0.6f, 0f, 0.25f);

    private const int MaxPuffs = 900;

    public ThermalEffects() { Instance = this; }

    
    public float LastMuzzleTime = -10f, LastImpactTime = -10f;
    public Vector3 LastMuzzlePos, LastImpactPos;

    private float R(float a, float b) => a + (float)_rng.NextDouble() * (b - a);
    private Vector3 RandomDir() => new Vector3(R(-1, 1), R(-1, 1), R(-1, 1)).normalized;

    private void Add(Puff p)
    {
        if (_puffs.Count >= MaxPuffs) _puffs.RemoveAt(0);
        p.Seed = R(0, 100);
        _puffs.Add(p);
    }

    

    
    public void OnCannonFired(Vector3 muzzle, Vector3 dir, float scale)
    {
        LastMuzzleTime = Time.unscaledTime; LastMuzzlePos = muzzle;
        scale = Math.Clamp(scale, 0.5f, 2.5f);
        
        Add(new Puff { Pos = muzzle + dir * 0.5f * scale, Radius = 0.45f * scale, Growth = 3.5f * scale, Temp = 3.0f, TempEnd = 1.2f, CoolTau = 0.04f,
                       Opacity = 0.95f, FadeTau = 0.05f, Life = 0.12f, Turbulence = 0.4f });
        Add(new Puff { Pos = muzzle + dir * 1.4f * scale, Radius = 0.35f * scale, Growth = 3f * scale, Temp = 2.6f, TempEnd = 1.0f, CoolTau = 0.05f,
                       Opacity = 0.8f, FadeTau = 0.06f, Life = 0.1f, Turbulence = 0.5f });
        
        var count = (int)(9 * scale);
        for (var i = 0; i < count; i++)
        {
            var side = RandomDir() * R(0.3f, 1.2f);
            Add(new Puff
            {
                Pos = muzzle + dir * R(0.4f, 1.8f) * scale + side * 0.2f,
                Vel = dir * R(6f, 24f) * scale + side * R(1f, 3f),
                Radius = R(0.25f, 0.45f) * scale, Growth = R(0.8f, 1.6f) * scale,
                Temp = R(1.5f, 1.9f), TempEnd = 0.5f, CoolTau = R(0.3f, 0.6f),
                Opacity = R(0.3f, 0.5f), FadeTau = R(0.7f, 1.2f), Life = R(1.5f, 2.5f),
                Buoyancy = 1.6f, Drag = 3.5f, Turbulence = 0.8f
            });
        }
        
        for (var i = 0; i < 5; i++)
            Add(new Puff
            {
                Pos = muzzle + dir * R(1f, 4f) * scale + Vector3.down * R(0.5f, 1.5f),
                Vel = dir * R(2f, 6f) + RandomDir() * 1.5f,
                Radius = R(0.8f, 1.4f) * scale, Growth = R(0.8f, 1.5f),
                Temp = 0.47f, TempEnd = 0.46f, CoolTau = 5f,
                Opacity = R(0.06f, 0.12f), FadeTau = R(2f, 3.5f), Life = R(3f, 5f),
                Buoyancy = 0.2f, Drag = 2f, Turbulence = 0.9f
            });
    }

    
    public void OnImpact(ProjectileEffectType type, Vector3 pos, Vector3 normal, Vector3 velocity, Transform parent, float calibre)
    {
        LastImpactTime = Time.unscaledTime; LastImpactPos = pos;
        var s = Math.Clamp(calibre / 100f, 0.3f, 2.5f);
        if (normal.sqrMagnitude < 0.01f) normal = -velocity.normalized;
        normal = normal.normalized;

        if ((type & ProjectileEffectType.Explosion) != 0)
        {
            Flash(pos + normal * 0.3f, 1.6f * s, 3.2f, 0.22f);
            HotGas(pos, normal, (int)(18 * s), s, 2.2f, 1.8f);
            Dust(pos, normal, (int)(10 * s), s);
            Spot(parent, pos, normal, 1.9f, 20f, 0.25f * s);
        }
        else if ((type & ProjectileEffectType.Penetration) != 0)
        {
            Flash(pos + normal * 0.15f, 0.7f * s, 2.8f, 0.12f);
            HotGas(pos, normal, (int)(6 * s), s * 0.6f, 1.8f, 1.2f);
            
            Spot(parent, pos, normal, 2.2f, 35f, 0.1f * s);
        }
        else if ((type & (ProjectileEffectType.NonPenetration | ProjectileEffectType.Ricochet)) != 0)
        {
            Flash(pos + normal * 0.12f, 0.45f * s, 2.3f, 0.09f);
            HotGas(pos, normal, (int)(3 * s) + 1, s * 0.4f, 1.4f, 0.8f);
            Spot(parent, pos, normal, 1.5f, 10f, 0.08f * s);
        }
        else if ((type & ProjectileEffectType.EnvironmentImpact) != 0)
        {
            Flash(pos + normal * 0.2f, 0.5f * s, 2.0f, 0.08f);
            HotGas(pos, normal, (int)(3 * s) + 1, s * 0.5f, 1.3f, 0.8f);
            Dust(pos, normal, (int)(6 * s) + 2, s);
            Spot(null, pos, normal, 1.1f, 8f, 0.2f * s);
        }
    }

    
    public void OnBigExplosion(Vector3 pos)
    {
        Flash(pos + Vector3.up, 4f, 3.4f, 0.35f);
        HotGas(pos, Vector3.up, 40, 2.5f, 2.6f, 3f);
        Dust(pos, Vector3.up, 12, 2f);
    }

    
    public void AddObscurant(Vector3 center, float radius, float buildTime, float duration)
    {
        radius = Math.Clamp(radius, 2f, 30f);
        buildTime = Math.Clamp(buildTime, 0.2f, 10f);
        duration = Math.Clamp(duration, 2f, 120f);
        var count = Math.Clamp((int)(radius * 1.2f), 6, 24);
        for (var i = 0; i < count; i++)
        {
            var off = RandomDir();
            off.y *= 0.5f;
            var r0 = radius * R(0.15f, 0.25f);
            var r1 = radius * R(0.45f, 0.6f);
            Add(new Puff
            {
                Pos = center + off * radius * R(0.1f, 0.55f),
                Vel = off * R(0.2f, 0.6f),
                Radius = r0, Growth = (r1 - r0) / buildTime * 1.4f,
                Temp = R(0.62f, 0.75f), TempEnd = 0.5f, CoolTau = R(5f, 9f),
                Opacity = R(0.9f, 1f), FadeTau = duration * R(0.8f, 1.1f), Life = duration + R(2f, 4f),
                Buoyancy = 0.05f, Drag = 0.6f, Turbulence = 0.9f
            });
        }
    }

    public void AddFireEmitter(Component source, Transform anchor, int kind)
    {
        foreach (var e in _emitters) if (e.Source != null && e.Source.Pointer == source.Pointer) return;
        _emitters.Add(new Emitter { Source = source, Anchor = anchor, Kind = kind });
    }

    private void Flash(Vector3 pos, float radius, float temp, float life) =>
        Add(new Puff { Pos = pos, Radius = radius, Growth = radius * 6f, Temp = temp, TempEnd = 1.0f, CoolTau = life * 0.5f,
                       Opacity = 1f, FadeTau = life * 0.6f, Life = life, Turbulence = 0.35f });

    private void HotGas(Vector3 pos, Vector3 normal, int count, float s, float temp, float speed)
    {
        for (var i = 0; i < count; i++)
        {
            var d = (normal + RandomDir() * 0.8f).normalized;
            Add(new Puff
            {
                Pos = pos + d * R(0.05f, 0.4f) * s,
                Vel = d * R(2f, 8f) * speed,
                Radius = R(0.25f, 0.5f) * s, Growth = R(0.8f, 1.8f) * s,
                Temp = temp * R(0.8f, 1.1f), TempEnd = 0.48f, CoolTau = R(0.5f, 1.2f),
                Opacity = R(0.35f, 0.6f), FadeTau = R(1.2f, 2.5f), Life = R(2f, 4f),
                Buoyancy = 1.8f, Drag = 2.5f, Turbulence = 0.85f
            });
        }
    }

    private void Dust(Vector3 pos, Vector3 normal, int count, float s)
    {
        for (var i = 0; i < count; i++)
        {
            var d = (normal * 1.2f + RandomDir()).normalized;
            Add(new Puff
            {
                Pos = pos + d * R(0.2f, 1f) * s,
                Vel = d * R(1.5f, 6f) * s,
                Radius = R(0.6f, 1.3f) * s, Growth = R(0.6f, 1.4f) * s,
                Temp = 0.49f, TempEnd = 0.46f, CoolTau = 4f,
                Opacity = R(0.12f, 0.25f), FadeTau = R(3f, 6f), Life = R(4f, 8f),
                Buoyancy = 0.15f, Drag = 1.5f, Turbulence = 0.95f
            });
        }
    }

    private void Spot(Transform parent, Vector3 pos, Vector3 normal, float temp, float cool, float radius,
                      float opacity = 1f, float stretch = 1f, Vector3? forward = null, float tempEnd = 0.6f)
    {
        var s = new HotSpot { Parent = parent, Temp = temp, TempEnd = tempEnd, CoolTau = cool, Radius = Math.Max(0.05f, radius), Life = cool * 4f,
                              Opacity = opacity, Stretch = stretch, Forward = forward ?? Vector3.forward, HasForward = forward.HasValue };
        if (parent != null)
        {
            s.LocalPos = parent.InverseTransformPoint(pos + normal * 0.02f);
            s.LocalNormal = parent.InverseTransformDirection(normal);
        }
        else
        {
            s.LocalPos = pos + normal * 0.02f;
            s.LocalNormal = normal;
        }
        _spots.Add(s);
        if (_spots.Count > 400) _spots.RemoveAt(0);
    }

    

    public void Update(float dt)
    {
        if (dt <= 0) return;
        dt = Math.Min(dt, 0.1f);

        
        for (var i = _emitters.Count - 1; i >= 0; i--)
        {
            var e = _emitters[i];
            if (e.Source == null || e.Anchor == null || !e.Source.gameObject.activeInHierarchy) { _emitters.RemoveAt(i); continue; }
            var pos = e.Anchor.position;
            if (e.Kind == 0)
            {
                
                e.Accumulator += dt * 22f;
                while (e.Accumulator >= 1f)
                {
                    e.Accumulator -= 1f;
                    Add(new Puff
                    {
                        Pos = pos + new Vector3(R(-0.3f, 0.3f), R(0f, 0.3f), R(-0.3f, 0.3f)),
                        Vel = Vector3.up * R(2f, 4.5f) + RandomDir() * 0.6f,
                        Radius = R(0.2f, 0.4f), Growth = R(0.4f, 0.9f),
                        Temp = R(2.2f, 2.9f), TempEnd = 1.2f, CoolTau = R(0.25f, 0.45f),
                        Opacity = R(0.6f, 0.9f), FadeTau = R(0.35f, 0.6f), Life = R(0.5f, 0.9f),
                        Buoyancy = 2.5f, Drag = 1.2f, Turbulence = 0.9f
                    });
                }
                
                e.SmokeAccumulator += dt * 5f;
                while (e.SmokeAccumulator >= 1f)
                {
                    e.SmokeAccumulator -= 1f;
                    Add(new Puff
                    {
                        Pos = pos + Vector3.up * R(0.5f, 1.2f),
                        Vel = Vector3.up * R(1.5f, 3f) + Wind * 0.5f,
                        Radius = R(0.5f, 0.8f), Growth = R(0.5f, 0.9f),
                        Temp = R(1.1f, 1.4f), TempEnd = 0.5f, CoolTau = R(2f, 3.5f),
                        Opacity = R(0.35f, 0.5f), FadeTau = R(4f, 7f), Life = R(8f, 12f),
                        Buoyancy = 0.8f, Drag = 0.8f, Turbulence = 0.95f
                    });
                }
                HeatModel.Instance?.MarkBurning(e.Anchor.position, dt);
            }
            else
            {
                
                e.Accumulator += dt * 7f;
                while (e.Accumulator >= 1f)
                {
                    e.Accumulator -= 1f;
                    var fwd = e.Anchor.forward;
                    Add(new Puff
                    {
                        Pos = pos + fwd * 0.1f,
                        Vel = fwd * R(1.5f, 3f) + Vector3.up * 0.6f,
                        Radius = R(0.12f, 0.2f), Growth = R(0.5f, 0.9f),
                        Temp = R(1.0f, 1.25f), TempEnd = 0.5f, CoolTau = R(0.6f, 1.2f),
                        Opacity = R(0.25f, 0.4f), FadeTau = R(1.2f, 2f), Life = R(2f, 3f),
                        Buoyancy = 1.2f, Drag = 1.5f, Turbulence = 0.9f
                    });
                }
            }
        }

        
        for (var i = _puffs.Count - 1; i >= 0; i--)
        {
            var p = _puffs[i];
            p.Age += dt;
            if (p.Age >= p.Life) { _puffs.RemoveAt(i); continue; }
            var heat = Math.Max(0f, p.Temp - 0.5f);
            p.Vel += (Vector3.up * p.Buoyancy * Math.Min(1f, heat) + Wind * 0.3f) * dt;
            p.Vel *= MathF.Exp(-p.Drag * dt);
            p.Pos += p.Vel * dt;
            p.Radius += p.Growth * dt;
            p.Growth *= MathF.Exp(-0.8f * dt);
            p.Temp += (p.TempEnd - p.Temp) * (1f - MathF.Exp(-dt / Math.Max(0.01f, p.CoolTau)));
            p.Opacity *= MathF.Exp(-dt / Math.Max(0.01f, p.FadeTau));
            _puffs[i] = p;
        }

        
        for (var i = _spots.Count - 1; i >= 0; i--)
        {
            var s = _spots[i];
            s.Age += dt;
            if (s.Age >= s.Life) { _spots.RemoveAt(i); continue; }
            s.Temp += (s.TempEnd - s.Temp) * (1f - MathF.Exp(-dt / s.CoolTau));
        }
    }

    
    
    
    

    private const int ArrayHeader = 0x20;
    private GameObject _batchGo;
    private Mesh _mesh;
    private MeshRenderer _batchRenderer;
    private Il2CppStructArray<Vector3> _verts;
    private Il2CppStructArray<Vector2> _uv0, _uv1, _uv2, _uv3;
    private int _capacity, _lastUsed;

    public unsafe void Draw(Camera cam)
    {
        if (cam == null || !GpuResources.Available || GpuResources.GasShader == null) return;
        if (!EnsureBatch()) return;

        var ct = cam.transform;
        var camPos = ct.position;
        var camUp = ct.up;
        var camFwd = ct.forward;
        var n = _puffs.Count + _spots.Count + _frameStamps.Count;
        EnsureCapacity(Math.Max(n, 1));
        if (!_batchRenderer.enabled) _batchRenderer.enabled = true;

        var pv = (Vector3*)((byte*)_verts.Pointer + ArrayHeader);
        var p1 = (Vector2*)((byte*)_uv1.Pointer + ArrayHeader);
        var p2 = (Vector2*)((byte*)_uv2.Pointer + ArrayHeader);
        var p3 = (Vector2*)((byte*)_uv3.Pointer + ArrayHeader);
        var q = 0;

        foreach (var p in _puffs)
        {
            var toPuff = p.Pos - camPos;
            
            if (p.Opacity < 0.003f || Vector3.Dot(toPuff, camFwd) < -p.Radius || toPuff.sqrMagnitude < 0.01f) continue;
            
            var f = toPuff.normalized;
            var x = Vector3.Cross(camUp, f);
            x = x.sqrMagnitude > 1e-8f ? x.normalized : ct.right;
            var y = Vector3.Cross(f, x);
            Quad(pv, q, p.Pos, x * p.Radius, y * p.Radius);
            Params(p1, p2, p3, q, p.Temp, p.Opacity, p.Seed + p.Age * 0.7f, p.Turbulence, 1.3f, Math.Max(0.3f, p.Radius * 0.7f));
            q++;
        }

        foreach (var s in _spots)
        {
            Vector3 pos, normal;
            if (s.Parent != null)
            {
                if (s.Parent.WasCollected) continue;
                pos = s.Parent.TransformPoint(s.LocalPos);
                normal = s.Parent.TransformDirection(s.LocalNormal);
            }
            else { pos = s.LocalPos; normal = s.LocalNormal; }
            if (Vector3.Dot(pos - camPos, camFwd) < -s.Radius * 2f) continue;
            var upHint = s.HasForward ? s.Forward : (Mathf.Abs(normal.y) > 0.9f ? Vector3.forward : Vector3.up);
            Basis(-normal, upHint, out var x, out var y);
            Quad(pv, q, pos, x * s.Radius, y * (s.Radius * s.Stretch));
            Params(p1, p2, p3, q, s.Temp, s.Opacity, s.LocalPos.x * 13f, 0.3f, 2.2f, 0.01f);
            q++;
        }

        foreach (var st in _frameStamps)
        {
            Basis(-st.Normal, st.Forward, out var x, out var y);
            Quad(pv, q, st.Pos, x * (st.Width * 0.5f), y * (st.Length * 0.5f));
            Params(p1, p2, p3, q, st.Temp, st.Opacity, 3.7f, 0.5f, 1.2f, 0.01f);
            q++;
        }
        _frameStamps.Clear();

        
        for (var i = q; i < _lastUsed; i++)
        {
            var v0 = i * 4;
            pv[v0] = pv[v0 + 1] = pv[v0 + 2] = pv[v0 + 3] = camPos;
            p1[v0] = p1[v0 + 1] = p1[v0 + 2] = p1[v0 + 3] = Vector2.zero;
        }
        _lastUsed = q;

        _mesh.vertices = _verts;
        _mesh.uv2 = _uv1;
        _mesh.uv3 = _uv2;
        _mesh.uv4 = _uv3;
        _mesh.bounds = new Bounds(camPos, new Vector3(40000f, 40000f, 40000f));
    }

    
    private static void Basis(Vector3 forward, Vector3 up, out Vector3 x, out Vector3 y)
    {
        var f = forward.sqrMagnitude > 1e-8f ? forward.normalized : Vector3.forward;
        x = Vector3.Cross(up, f);
        if (x.sqrMagnitude < 1e-8f) x = Vector3.Cross(Mathf.Abs(f.y) < 0.9f ? Vector3.up : Vector3.forward, f);
        x.Normalize();
        y = Vector3.Cross(f, x);
    }

    private static unsafe void Quad(Vector3* pv, int q, Vector3 c, Vector3 hx, Vector3 hy)
    {
        var v0 = q * 4;
        pv[v0] = c - hx - hy;
        pv[v0 + 1] = c + hx - hy;
        pv[v0 + 2] = c + hx + hy;
        pv[v0 + 3] = c - hx + hy;
    }

    private static unsafe void Params(Vector2* p1, Vector2* p2, Vector2* p3, int q, float temp, float opacity, float seed,
                                      float turbulence, float softness, float softDepth)
    {
        var v0 = q * 4;
        var a = new Vector2(temp, opacity);
        var b = new Vector2(seed, turbulence);
        var c = new Vector2(softness, softDepth);
        p1[v0] = p1[v0 + 1] = p1[v0 + 2] = p1[v0 + 3] = a;
        p2[v0] = p2[v0 + 1] = p2[v0 + 2] = p2[v0 + 3] = b;
        p3[v0] = p3[v0 + 1] = p3[v0 + 2] = p3[v0 + 3] = c;
    }

    public void HideAll()
    {
        try { if (_batchRenderer != null && !_batchRenderer.WasCollected && _batchRenderer.enabled) _batchRenderer.enabled = false; }
        catch { }
        _frameStamps.Clear();
    }

    private bool EnsureBatch()
    {
        if (_batchGo != null && !_batchGo.WasCollected && _mesh != null && !_mesh.WasCollected) return true;
        try
        {
            _gasMaterial ??= new Material(GpuResources.GasShader) { name = "ThermalGas", hideFlags = HideFlags.DontSave };
            _batchGo = new GameObject("ThermalGasBatch") { layer = GpuThermalView.ProxyLayer };
            UnityEngine.Object.DontDestroyOnLoad(_batchGo);
            _mesh = new Mesh { name = "ThermalGas" };
            _mesh.MarkDynamic();
            _mesh.hideFlags = HideFlags.DontUnloadUnusedAsset;
            _batchGo.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _batchRenderer = _batchGo.AddComponent<MeshRenderer>();
            _batchRenderer.sharedMaterial = _gasMaterial;
            _batchRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _batchRenderer.receiveShadows = false;
            _capacity = 0;
            _lastUsed = 0;
            return true;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("Thermal gas renderer failed: " + e.Message);
            return false;
        }
    }

    private unsafe void EnsureCapacity(int n)
    {
        if (n <= _capacity) return;
        var cap = Math.Max(256, _capacity);
        while (cap < n) cap *= 2;
        _capacity = cap;
        _verts = new Il2CppStructArray<Vector3>(cap * 4);
        _uv0 = new Il2CppStructArray<Vector2>(cap * 4);
        _uv1 = new Il2CppStructArray<Vector2>(cap * 4);
        _uv2 = new Il2CppStructArray<Vector2>(cap * 4);
        _uv3 = new Il2CppStructArray<Vector2>(cap * 4);
        var uv = (Vector2*)((byte*)_uv0.Pointer + ArrayHeader);
        var tris = new int[cap * 6];
        for (var i = 0; i < cap; i++)
        {
            int v = i * 4, t = i * 6;
            uv[v] = new Vector2(0, 0); uv[v + 1] = new Vector2(1, 0); uv[v + 2] = new Vector2(1, 1); uv[v + 3] = new Vector2(0, 1);
            tris[t] = v; tris[t + 1] = v + 1; tris[t + 2] = v + 2;
            tris[t + 3] = v; tris[t + 4] = v + 2; tris[t + 5] = v + 3;
        }
        _mesh.Clear();
        if (cap * 4 > 65000) _mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        _mesh.vertices = _verts;
        _mesh.uv = _uv0;
        _mesh.triangles = tris;
        _lastUsed = cap;
    }

    
    
    

    private struct Stamp { public Vector3 Pos, Normal, Forward; public float Width, Length, Temp, Opacity; }
    private readonly List<Stamp> _frameStamps = new();

    private sealed class VehicleFx
    {
        public Vector3 LastTrail;
        public float PlumeAcc;
        public float NextGround;
        public bool HasGround;
        public Vector3 GroundLocal, GroundNormalLocal;   
    }

    private readonly Dictionary<IntPtr, VehicleFx> _vehicleState = new();

    public void UpdateVehicleEffects(IEnumerable<HeatModel.VehicleInfo> vehicles, Vector3 camPos, float dt)
    {
        var now = Time.unscaledTime;
        foreach (var v in vehicles)
        {
            if (v.Transform == null) continue;
            var vpos = v.Transform.position;
            if ((vpos - camPos).sqrMagnitude > 2500f * 2500f) continue;
            if (!_vehicleState.TryGetValue(v.Key, out var st)) _vehicleState[v.Key] = st = new VehicleFx { LastTrail = vpos };

            var up = v.Transform.up;
            var fwd = v.Transform.forward;
            var right = v.Transform.right;

            
            
            if (now >= st.NextGround)
            {
                st.NextGround = now + 0.1f;
                st.HasGround = false;
                Vector3 gp, gn;
                if (Physics.Raycast(vpos + up * 1.5f, -up, out var hit, 6f, ~0, QueryTriggerInteraction.Ignore) &&
                    !hit.collider.transform.IsChildOf(v.Transform))
                {
                    gp = hit.point; gn = hit.normal; st.HasGround = true;
                }
                else if (TryGroundBelow(vpos, v.Transform, out gp, out gn)) st.HasGround = true;
                if (st.HasGround)
                {
                    st.GroundLocal = v.Transform.InverseTransformPoint(gp);
                    st.GroundNormalLocal = v.Transform.InverseTransformDirection(gn);
                }
            }
            if (st.HasGround)
            {
                var gn = v.Transform.TransformDirection(st.GroundNormalLocal);
                _frameStamps.Add(new Stamp { Pos = v.Transform.TransformPoint(st.GroundLocal) + gn * 0.03f, Normal = gn, Forward = fwd,
                                             Width = v.Width * 1.1f, Length = v.Length * 0.9f, Temp = 0.6f + 0.1f * v.Engine, Opacity = 0.35f });
            }

            
            if (v.Engine > 0.3f && v.EnginePos.HasValue)
            {
                st.PlumeAcc += dt * 3.5f;
                while (st.PlumeAcc >= 1f)
                {
                    st.PlumeAcc -= 1f;
                    var deck = v.EnginePos.Value + up * v.DeckHeight;
                    Add(new Puff
                    {
                        Pos = deck + right * R(-0.5f, 0.5f) * v.Width * 0.5f + fwd * R(-0.4f, 0.4f),
                        Vel = up * R(0.6f, 1.3f) + Wind * 0.4f,
                        Radius = R(0.35f, 0.6f), Growth = R(0.3f, 0.6f),
                        Temp = R(0.95f, 1.15f), TempEnd = 0.5f, CoolTau = R(0.8f, 1.4f),
                        Opacity = R(0.12f, 0.2f), FadeTau = R(1.5f, 2.5f), Life = R(2.5f, 3.5f),
                        Buoyancy = 0.9f, Drag = 1f, Turbulence = 1f
                    });
                }
            }

            
            var moved = vpos - st.LastTrail;
            if (moved.magnitude > 0.6f)
            {
                st.LastTrail = vpos;
                for (var side = -1; side <= 1; side += 2)
                {
                    var p = vpos + right * side * v.Width * 0.38f;
                    if (TryGroundBelow(p, v.Transform, out var gp, out var gn))
                        Spot(null, gp + gn * 0.02f, gn, 0.62f + 0.08f * v.Running, 45f, v.Width * 0.12f, 0.6f, 2.2f, fwd, 0.48f);
                }
            }
        }
    }

    private static bool TryGroundBelow(Vector3 from, Transform vehicle, out Vector3 point, out Vector3 normal)
    {
        point = default; normal = Vector3.up;
        try
        {
            var hits = Physics.RaycastAll(from + Vector3.up * 2f, Vector3.down, 8f, ~0, QueryTriggerInteraction.Ignore);
            var best = float.MaxValue;
            foreach (var h in hits)
            {
                if (h.collider == null || h.collider.transform.IsChildOf(vehicle)) continue;
                if (h.distance < best) { best = h.distance; point = h.point; normal = h.normal; }
            }
            return best < float.MaxValue;
        }
        catch { return false; }
    }
}
