using System;
using System.Collections.Generic;
using HarmonyLib;
using Sprocket.DamageModelling;
using UnityEngine;

namespace Munitions;


internal static unsafe class Effects
{
    private const int ElemSize = 0x50, ElemBase = 0x20, PoolOffset = 0x38;
    private const int EDef = 0x08, EEvent = 0x10, EId = 0x18, EPos = 0x28, EVel = 0x34, EFlags = 0x48;
    private const float TracerBurn = 3.5f;    

    private static readonly System.Random Rng = new();
    private static readonly List<Shell> Shells = new();

    private sealed class Shell
    {
        public int Id, Index = -1, Kind;
        public ProjectileRegister Register;
        public IntPtr RegPtr, Def;
        public SizeTypes Types;
        public float LaunchTime;
        public IntPtr LastEvent;
        public int Events;
        public Vector3 Pos, Vel;
        public bool Done;
        public float MaxFlight;
        public bool Auto;                     
        public bool Fused;                    
    }

    private static bool On => Plugin.EffectsEnabled.Value;
    private static float Density => Mathf.Clamp(Plugin.EffectDensity.Value, 0.1f, 4f);
    private static int N(float n) => Math.Max(1, (int)Mathf.Round(n * Density));

    
    private static int NP(float n)
    {
        var v = Mathf.Max(0f, n * Density);
        var i = (int)v;
        if (Rng.NextDouble() < v - i) i++;
        return i;
    }

    
    
    private static float Small(int caliber, bool auto) =>
        Mathf.Clamp(Mathf.Pow(Mathf.Max(caliber, 1) / 60f, 1.5f), 0.05f, 1f) * (auto ? 0.5f : 1f);

    

    public static void OnLaunch(ProjectileRegister register, int id, int index, int kind, SizeTypes types, Vector3 pos, Vector3 vel, float maxFlight = 0f, bool automatic = false)
    {
        
        
        Shells.Add(new Shell
        {
            Id = id, Index = index, Kind = kind, Register = register, RegPtr = register.Pointer,
            Def = types.Defs[kind], Types = types, LaunchTime = Time.time, Pos = pos, Vel = vel, MaxFlight = maxFlight, Auto = automatic
        });
        var dir = vel.sqrMagnitude > 1f ? vel.normalized : Vector3.forward;
        try
        {
            var tube = Kind.IsLauncher(kind) || Kind.IsRocket(kind);
            if (tube && CombatFx.Launch(pos, dir, TubeLength(types.Caliber), types.Caliber, Kind.External(kind))) return;
            if (!On) return;
            if (kind == Kind.Apfsds) SabotDiscard(pos, dir, vel.magnitude, types.Caliber, automatic);
            else if (Kind.Base(kind) == Kind.Atgm) AtgmLaunch(pos, dir, types.Caliber);
            else if (tube) TubeLaunch(pos, dir, types.Caliber);
        }
        catch (Exception e) { Plugin.Log.LogWarning("Launch effect failed: " + e.Message); }
    }

    
    private static float TubeLength(int caliber) =>
        Launchers.FiringTubeLength > 0f ? Launchers.FiringTubeLength : LauncherPart.TubeCalibers * caliber / 1000f;

    [HarmonyPatch(typeof(ProjectileRegister), nameof(ProjectileRegister.FixedUpdate))]
    private static class HitWatch
    {
        private static void Postfix(ProjectileRegister __instance)
        {
            if (Shells.Count == 0) return;
            try { Watch(__instance); }
            catch (Exception e) { Plugin.Log.LogError("Hit effects failed: " + e); Shells.Clear(); }
        }
    }

    private static void Watch(ProjectileRegister register)
    {
        var reg = register.Pointer;
        for (var i = Shells.Count - 1; i >= 0; i--)
        {
            var s = Shells[i];
            if (s.RegPtr != reg) continue;
            var e = Locate(s);
            if (e != IntPtr.Zero)
            {
                var prev = s.Pos;
                s.Pos = *(Vector3*)(e + EPos);
                s.Vel = *(Vector3*)(e + EVel);
                var ev = *(IntPtr*)(e + EEvent);
                if (ev != IntPtr.Zero && ev != s.LastEvent)
                {
                    s.LastEvent = ev;
                    OnEvent(s, new ProjectileEvent(ev), prev, false);
                }
                continue;
            }

            
            ProjectileEvent last = null;
            try
            {
                var history = register.projectileHistory;
                if (history != null && history.TryGetValue(s.Id, out var pe)) last = pe;
            }
            catch { }
            if (last != null && last.Pointer != s.LastEvent) OnEvent(s, last, s.Pos, true);
            else if (s.Events == 0 && !Kind.IsKinetic(s.Kind) &&
                     Time.time - s.LaunchTime < ((Kind.IsMissile(s.Kind) || Kind.IsRocket(s.Kind)) && s.MaxFlight > 0f ? s.MaxFlight - 0.1f : 9f))
            {
                
                Detonation(s, s.Pos + s.Vel * Time.fixedDeltaTime * 0.5f, -SafeDir(s.Vel), false, SafeDir(s.Vel), false);
            }
            Shells.RemoveAt(i);
        }
    }

    
    private static void OnEvent(Shell s, ProjectileEvent ev, Vector3 prev, bool ended)
    {
        if (ev == null) return;
        Vector3 pos, normal, incidence;
        bool ground;
        var type = 0;
        var dm = ev.TryCast<HitDamageModelProjectileEvent>();
        if (dm != null)
        {
            pos = dm.Position;
            normal = dm.Normal;
            incidence = dm.Incidence;
            type = (int)dm.Type;
            ground = false;
        }
        else
        {
            var gr = ev.TryCast<HitGroundProjectileEvent>();
            if (gr == null) return; 
            pos = gr.Position;
            normal = gr.Normal;
            incidence = gr.Incidence;
            ground = true;
        }
        if (normal.sqrMagnitude < 1e-4f) normal = -SafeDir(s.Vel);
        normal.Normalize();
        incidence = incidence.sqrMagnitude > 1e-4f ? incidence.normalized : SafeDir(s.Vel);
        s.Events++;

        
        if (s.Kind == Kind.Aphe && !s.Fused && !ground)
        {
            s.Fused = true;
            try { ApheFuse.OnHit(s.Types, prev, pos, incidence, type, ended); }
            catch (Exception e) { Plugin.Log.LogWarning("APHE fuse failed: " + e.Message); }
        }

        try
        {
            switch (s.Kind)
            {
                case Kind.Apfsds:
                case Kind.Ap:
                case Kind.Aphe:
                    
                    if (s.Events > 4 || !On || CombatFx.HitsActive) return;
                    if (ground) KineticGround(pos, normal, incidence, s.Types.Caliber, s.Auto);
                    else KineticArmour(pos, normal, incidence, type, s.Types.Caliber, s.Auto);
                    break;
                default:
                    if (s.Events > 1) return;
                    Detonation(s, pos, normal, ground, incidence, true);
                    break;
            }
        }
        catch (Exception e) { Plugin.Log.LogWarning("Impact effect failed: " + e.Message); }
    }

    
    private static void Detonation(Shell s, Vector3 pos, Vector3 normal, bool ground, Vector3 incidence, bool gameShows)
    {
        var external = Kind.Burst(s.Kind);
        RecordDetonation(pos, external, s.Types.Caliber, s.Auto);
        
        var replaced = gameShows ? CombatFx.HitsActive : CombatFx.Detonation(pos, normal, s.Types.Caliber, external, ground, s.Auto);
        if (!On) return;
        
        if (Kind.IsThermobaric(s.Kind)) ThermobaricBurst(pos, normal, ground, s.Types.Spec.FireballRadius(s.Kind));
        else if (replaced) return;
        else if (s.Kind is Kind.He or Kind.Hesh) HeBurst(pos, normal, ground, s.Types.Spec.Filler(s.Kind), s.Auto);
        else if (s.Kind is Kind.RocketHe or Kind.AtgmHe or Kind.LauncherHe) HeBurst(pos, normal, ground, s.Types.Spec.Filler(s.Kind), false);
        else HeatBurst(pos, normal, ground, incidence, s.Types.Caliber, s.Auto);
    }

    
    
    
    
    public static void ExternalDetonation(ProjectileRegister register, Vector3 pos, Vector3 normal, Vector3 velocity,
        int externalKind, int caliber, bool ground)
    {
        RecordDetonation(pos, externalKind, caliber, false);
        if (GameExplosion(register, pos, normal, velocity, caliber))
        {
            if (On && !CombatFx.HitsActive) HeatBurst(pos, normal, ground, -normal, caliber, false);
            return;
        }
        var replaced = CombatFx.Detonation(pos, normal, caliber, externalKind, ground, false);
        if (!On || replaced) return;
        HeatBurst(pos, normal, ground, -normal, caliber, false);
    }

    
    
    
    public static void BlastDetonation(Vector3 pos, Vector3 normal, int externalKind, int caliber, bool ground)
    {
        RecordDetonation(pos, externalKind, caliber, false);
        if (!On) return;
        if (externalKind == 6) ThermobaricBurst(pos, normal, ground, ShellSpec.For(caliber).RocketTbRadius);
        else if (!CombatFx.HitsActive) HeatBurst(pos, normal, ground, -normal, caliber, false);
    }

    private static int _gameExplosionErrors;

    
    private static bool GameExplosion(ProjectileRegister register, Vector3 pos, Vector3 normal, Vector3 velocity, int caliber)
    {
        try
        {
            var config = register?.Context?.ShellImpactMarkEffectFactory?.TryCast<Sprocket.Vehicles.Weapons.ProjectileEffectConfig>();
            if (config == null) return false;
            var info = new ProjectileEffectInfo
            {
                Type = ProjectileEffectType.Explosion,
                Position = pos,
                HitNormal = normal,
                HitVelocity = velocity,
                Parent = null,
                Calibre = (ushort)Mathf.Clamp(caliber, 1, 1000)
            };
            config.PlayEffect(info);
            return true;
        }
        catch (Exception e)
        {
            if (_gameExplosionErrors++ < 3) Plugin.Log.LogWarning("Game explosion effect failed: " + e.Message);
            return false;
        }
    }

    
    
    
    

    private sealed class Det
    {
        public Vector3 Pos;
        public int Kind, Caliber;
        public float At;
        public bool Claimed, Auto;
    }

    private static readonly List<Det> Detonations = new();

    private static void RecordDetonation(Vector3 pos, int externalKind, int caliber, bool automatic)
    {
        var now = Time.time;
        Detonations.RemoveAll(d => now - d.At > 2f || now < d.At);
        if (Detonations.Count > 64) Detonations.RemoveAt(0);
        Detonations.Add(new Det { Pos = pos, Kind = externalKind, Caliber = caliber, At = now, Auto = automatic });
    }

    
    
    public static int PeekDetonation(Vector3 pos, float radius)
    {
        var now = Time.time;
        Det best = null;
        var bestDist = radius * radius;
        foreach (var d in Detonations)
        {
            if (now - d.At > 0.5f || now < d.At) continue;
            var dist = (d.Pos - pos).sqrMagnitude;
            if (dist <= bestDist) { bestDist = dist; best = d; }
        }
        if (best == null) return -1;
        return (best.Auto ? 1 << 24 : 0) | ((best.Kind & 0xFF) << 16) | (best.Caliber & 0xFFFF);
    }

    
    public static int ClaimDetonation(Vector3 pos, float radius)
    {
        var now = Time.time;
        Det best = null;
        var bestDist = radius * radius;
        foreach (var d in Detonations)
        {
            if (d.Claimed || now - d.At > 0.5f) continue;
            var dist = (d.Pos - pos).sqrMagnitude;
            if (dist <= bestDist) { bestDist = dist; best = d; }
        }
        if (best == null) return -1;
        best.Claimed = true;
        return (best.Kind << 16) | (best.Caliber & 0xFFFF);
    }

    
    public static Vector4 TakeUnclaimed(float minAge)
    {
        var now = Time.time;
        foreach (var d in Detonations)
        {
            if (d.Claimed || now - d.At < minAge) continue;
            d.Claimed = true;
            return new Vector4(d.Pos.x, d.Pos.y, d.Pos.z, (d.Kind << 16) | (d.Caliber & 0xFFFF));
        }
        return new Vector4(0f, 0f, 0f, -1f);
    }

    
    public static void DrawTracers()
    {
        if (Shells.Count == 0) return;
        
        for (var i = Shells.Count - 1; i >= 0; i--)
        {
            var s = Shells[i];
            if (s.Register == null || s.Register.WasCollected || Time.time - s.LaunchTime > 40f || Time.time < s.LaunchTime)
                Shells.RemoveAt(i);
        }
        if (!On || !Plugin.Tracers.Value) return;
        var extrapolate = Mathf.Max(0f, Time.time - Time.fixedTime);
        foreach (var s in Shells)
        {
            if (Kind.IsMissile(s.Kind) || Kind.IsRocket(s.Kind) || s.Events > 0) continue;
            var age = Time.time - s.LaunchTime;
            if (age > TracerBurn || age < 0.02f) continue;
            var e = Locate(s);
            if (e == IntPtr.Zero) continue;
            var pos = *(Vector3*)(e + EPos);
            var vel = *(Vector3*)(e + EVel);
            pos += vel * extrapolate;
            var color = s.Kind switch
            {
                Kind.Apfsds or Kind.Ap or Kind.Aphe => new Color(1f, 0.2f, 0.08f, 1f),
                Kind.Heat => new Color(1f, 0.82f, 0.25f, 1f),
                _ => new Color(1f, 0.5f, 0.16f, 1f)
            };
            var fade = Mathf.Clamp01((TracerBurn - age) / 0.5f);
            color.a = fade;
            Particles.Glow.AddOnce(new Particle
            {
                Pos = pos, Vel = vel, Life = 1f, Size0 = 0.09f, Size1 = 0.09f, C0 = color, C1 = color,
                Boost = 3f, Stretch = 0.014f, MaxLength = 28f, MinAngular = 1.6f, Cell = 1, GroundY = float.NaN
            });
            Particles.Glow.AddOnce(new Particle
            {
                Pos = pos, Life = 1f, Size0 = 0.3f, Size1 = 0.3f, C0 = color, C1 = color,
                Boost = 2.5f, MinAngular = 4f, Cell = 0, GroundY = float.NaN
            });
        }
    }

    

    
    
    private static void SabotDiscard(Vector3 muzzle, Vector3 dir, float speed, int caliber, bool automatic = false)
    {
        if (caliber < 20) return; 
        var k = caliber / 120f;
        var perp = Perp(dir);
        var petals = caliber >= 100 ? 3 : 2;
        var start = (float)Rng.NextDouble() * 360f;
        for (var i = 0; i < petals; i++)
        {
            var radial = Quaternion.AngleAxis(start + i * 360f / petals + Range(-15f, 15f), dir) * perp;
            Smoke(new Particle
            {
                Pos = muzzle + dir * Range(1f, 3f), Vel = dir * speed * Range(0.45f, 0.6f) + radial * Range(60f, 110f),
                Life = 1.6f, Size0 = 0.45f * k, Size1 = 0.45f * k, Rot = Range(0f, 6.3f), RotSpeed = Range(-25f, 25f),
                Drag = 2.2f, Gravity = 1f, C0 = new Color(0.33f, 0.33f, 0.31f, 1f), C1 = new Color(0.33f, 0.33f, 0.31f, 0.8f),
                MinAngular = 1.5f, Cell = 2
            });
        }
        var puffs = automatic ? (int)Mathf.Round(N(7) * Mathf.Clamp01(Plugin.AutoSmoke.Value)) : N(7);
        for (var i = 0; i < puffs; i++)
        {
            Smoke(new Particle
            {
                Pos = muzzle + dir * Range(2f, 8f) + Random3() * 0.2f,
                Vel = dir * Range(15f, 60f) + Random3() * Range(4f, 12f),
                Life = Range(0.5f, 1.1f), Size0 = 0.35f * k, Size1 = Range(1.4f, 2.4f) * k, Rot = Range(0f, 6.3f), RotSpeed = Range(-1f, 1f),
                Drag = 4f, Buoyancy = 0.3f, C0 = new Color(0.86f, 0.85f, 0.83f, 0.42f), C1 = new Color(0.9f, 0.9f, 0.88f, 0f),
                FadeIn = 0.05f, Cell = (byte)Rng.Next(2)
            });
        }
    }

    
    public static void AutoMuzzleFlash(Vector3 muzzle, float caliber)
    {
        if (!On) return;
        try
        {
            var k = Mathf.Clamp(caliber / 30f, 0.3f, 2.5f);
            Glow(new Particle { Pos = muzzle, Life = Range(0.035f, 0.055f), Size0 = 0.9f * k, Size1 = 1.3f * k, Rot = Range(0f, 6.3f), C0 = new Color(1f, 0.82f, 0.55f, 1f), C1 = new Color(1f, 0.5f, 0.2f, 0f), Boost = 3f, Cell = 3 });
            Glow(new Particle { Pos = muzzle, Life = Range(0.05f, 0.07f), Size0 = 0.6f * k, Size1 = 1f * k, C0 = new Color(1f, 0.7f, 0.4f, 0.9f), C1 = new Color(1f, 0.4f, 0.1f, 0f), Boost = 2f, Cell = 0 });
        }
        catch (Exception e) { Plugin.Log.LogWarning("Muzzle flash failed: " + e.Message); }
    }

    
    private static void AtgmLaunch(Vector3 muzzle, Vector3 dir, int caliber)
    {
        var k = caliber / 125f;
        Glow(new Particle { Pos = muzzle + dir * 1.2f, Life = 0.09f, Size0 = 1.5f * k, Size1 = 2.6f * k, C0 = new Color(1f, 0.7f, 0.4f, 1f), C1 = new Color(1f, 0.4f, 0.1f, 0f), Boost = 3f, Cell = 0 });
        for (var i = 0; i < N(11); i++)
        {
            Smoke(new Particle
            {
                Pos = muzzle + dir * Range(0f, 4f) + Random3() * 0.3f,
                Vel = dir * Range(3f, 18f) + Random3() * Range(1f, 3f),
                Life = Range(2f, 3.6f), Size0 = 0.8f * k, Size1 = Range(3f, 5f) * k, Rot = Range(0f, 6.3f), RotSpeed = Range(-0.5f, 0.5f),
                Drag = 1.8f, Buoyancy = 0.4f, C0 = new Color(0.78f, 0.77f, 0.75f, 0.55f), C1 = new Color(0.83f, 0.83f, 0.81f, 0f),
                FadeIn = 0.04f, Cell = (byte)Rng.Next(2)
            });
        }
    }

    
    private static void TubeLaunch(Vector3 muzzle, Vector3 dir, int caliber)
    {
        var k = caliber / 152f;
        var rear = muzzle - dir * TubeLength(caliber);
        Glow(new Particle { Pos = muzzle + dir * 0.4f, Life = 0.07f, Size0 = 0.8f * k, Size1 = 1.6f * k, C0 = new Color(1f, 0.75f, 0.45f, 1f), C1 = new Color(1f, 0.4f, 0.1f, 0f), Boost = 2.5f, Cell = 0 });
        Glow(new Particle { Pos = rear - dir * 0.6f * k, Life = 0.1f, Size0 = 1.4f * k, Size1 = 2.8f * k, C0 = new Color(1f, 0.7f, 0.4f, 1f), C1 = new Color(1f, 0.35f, 0.08f, 0f), Boost = 3f, Cell = 0 });
        for (var i = 0; i < N(4); i++)
            Glow(new Particle
            {
                Pos = rear - dir * Range(0.3f, 1.2f) * k, Vel = -dir * Range(15f, 35f) + Random3() * 4f, Life = Range(0.12f, 0.25f),
                Size0 = 0.6f * k, Size1 = 1.6f * k, Rot = Range(0f, 6.3f), Drag = 6f, C0 = new Color(1f, 0.75f, 0.45f, 1f), C1 = new Color(0.9f, 0.25f, 0.05f, 0f),
                Boost = 2f, Cell = 2
            });
        
        for (var i = 0; i < N(12); i++)
            Smoke(new Particle
            {
                Pos = rear - dir * Range(0.5f, 3f) * k + Random3() * 0.4f,
                Vel = -dir * Range(8f, 30f) + Random3() * Range(2f, 5f),
                Life = Range(2.2f, 4f), Size0 = 0.9f * k, Size1 = Range(3.5f, 5.5f) * k, Rot = Range(0f, 6.3f), RotSpeed = Range(-0.3f, 0.3f),
                Drag = 2f, Buoyancy = 0.4f, C0 = new Color(0.72f, 0.7f, 0.66f, 0.6f), C1 = new Color(0.78f, 0.76f, 0.72f, 0f),
                FadeIn = 0.03f, Cell = (byte)Rng.Next(2)
            });
        
        for (var i = 0; i < N(4); i++)
            Smoke(new Particle
            {
                Pos = muzzle + dir * Range(0.2f, 1.5f), Vel = dir * Range(2f, 8f) + Random3() * 1.5f,
                Life = Range(1.2f, 2f), Size0 = 0.5f * k, Size1 = Range(1.8f, 2.6f) * k, Rot = Range(0f, 6.3f),
                Drag = 2f, Buoyancy = 0.3f, C0 = new Color(0.78f, 0.77f, 0.75f, 0.45f), C1 = new Color(0.82f, 0.82f, 0.8f, 0f),
                FadeIn = 0.04f, Cell = (byte)Rng.Next(2)
            });
    }

    
    
    private static void KineticArmour(Vector3 pos, Vector3 normal, Vector3 incidence, int hitType, int caliber, bool auto)
    {
        var k = Mathf.Clamp(caliber / 120f, 0.4f, 1.6f);
        var kf = Mathf.Clamp(caliber / 120f, 0.1f, 1.6f);          
        var share = Small(caliber, auto);
        var energy = Mathf.Lerp(0.45f, 1f, Mathf.Clamp01(caliber / 60f)); 
        const int penetrated = 2, deflected = 4;
        Glow(new Particle { Pos = pos + normal * 0.2f * kf, Life = 0.07f, Size0 = 1.2f * kf, Size1 = 2.8f * kf, C0 = new Color(1f, 0.95f, 0.85f, 1f), C1 = new Color(1f, 0.7f, 0.4f, 0f), Boost = 4f, Cell = 3 });
        Glow(new Particle { Pos = pos + normal * 0.2f * kf, Life = 0.12f, Size0 = 1.5f * kf, Size1 = 3.5f * kf, C0 = new Color(1f, 0.8f, 0.5f, 0.8f), C1 = new Color(1f, 0.4f, 0.1f, 0f), Boost = 2f, Cell = 0 });

        Vector3 axis;
        float cone;
        if (hitType == deflected) { axis = Vector3.Reflect(incidence, normal); cone = 25f; }
        else if (hitType == penetrated) { axis = (-incidence + normal).normalized; cone = 65f; }
        else { axis = normal; cone = 75f; }
        var sparks = NP((hitType == deflected ? 45 : 60) * share);
        for (var i = 0; i < sparks; i++)
        {
            var hot = (float)Rng.NextDouble();
            Glow(new Particle
            {
                Pos = pos + normal * 0.05f, Vel = Cone(axis, cone) * Range(60f, hitType == deflected ? 380f : 240f) * energy,
                Life = Range(0.25f, 0.95f) * energy, Size0 = 0.05f * k, Size1 = 0.04f * k, Drag = 1.4f, Gravity = 1f,
                C0 = new Color(1f, 0.9f + 0.1f * hot, 0.65f + 0.3f * hot, 1f), C1 = new Color(1f, 0.45f, 0.12f, 0f),
                Boost = 2.5f, Stretch = 0.022f, MaxLength = 3f, MinAngular = 1.2f, Cell = 1
            });
        }
        for (var i = 0; i < NP(5 * share); i++)
        {
            Smoke(new Particle
            {
                Pos = pos + normal * 0.3f * kf + Random3() * 0.3f * kf, Vel = normal * Range(1f, 4f) + Random3() * 1.5f,
                Life = Range(1.2f, 2.2f), Size0 = 0.5f * kf, Size1 = Range(2f, 3.2f) * kf, Rot = Range(0f, 6.3f), RotSpeed = Range(-0.6f, 0.6f),
                Drag = 1.5f, Buoyancy = 0.6f, C0 = new Color(0.36f, 0.34f, 0.31f, 0.6f), C1 = new Color(0.42f, 0.41f, 0.39f, 0f),
                FadeIn = 0.05f, Cell = (byte)Rng.Next(2)
            });
        }
    }

    
    private static void KineticGround(Vector3 pos, Vector3 normal, Vector3 incidence, int caliber, bool auto)
    {
        var k = Mathf.Clamp(caliber / 120f, 0.12f, 1.6f);
        var share = Small(caliber, auto);
        var up = (normal + Vector3.Reflect(incidence, normal) * 0.6f).normalized;
        Dirt(pos, normal, up, 0.8f * k, NP(6 * share), NP(10 * share), pos.y - 0.2f);
    }

    
    
    private static void HeBurst(Vector3 pos, Vector3 normal, bool ground, float fillerKg, bool auto)
    {
        var kRaw = Mathf.Pow(Mathf.Max(fillerKg, 0.005f), 1f / 3f);
        var k = Mathf.Clamp(kRaw, 0.2f, 2.5f);
        var share = Mathf.Clamp(kRaw * kRaw / 0.16f, 0.05f, 1f) * (auto ? 0.5f : 1f);  
        var center = pos + normal * 0.4f * k;
        Glow(new Particle { Pos = center, Life = 0.09f, Size0 = 3f * k, Size1 = 7f * k, C0 = new Color(1f, 0.88f, 0.7f, 1f), C1 = new Color(1f, 0.6f, 0.3f, 0f), Boost = 4f, Cell = 0 });
        Glow(new Particle { Pos = center, Life = 0.06f, Size0 = 5f * k, Size1 = 10f * k, C0 = new Color(1f, 0.9f, 0.75f, 1f), C1 = new Color(1f, 0.7f, 0.4f, 0f), Boost = 2f, Cell = 3 });
        for (var i = 0; i < Math.Max(1, NP(7 * share)); i++)
        {
            Glow(new Particle
            {
                Pos = center + Random3() * 0.8f * k, Vel = (Random3() + normal * 0.9f) * Range(6f, 15f) * k,
                Life = Range(0.25f, 0.5f), Size0 = 1.1f * k, Size1 = Range(2.8f, 4f) * k, Rot = Range(0f, 6.3f), RotSpeed = Range(-2f, 2f),
                Drag = 5f, Buoyancy = 2f, C0 = new Color(1f, 0.78f, 0.5f, 1f), C1 = new Color(0.9f, 0.22f, 0.04f, 0f),
                Boost = 2.2f, Cell = 2
            });
        }
        var groundY = ground ? pos.y - 0.15f : float.NaN;
        for (var i = 0; i < NP(38 * share); i++)
        {
            var dir = ground ? Hemisphere(normal) : Random3().normalized;
            Glow(new Particle
            {
                Pos = center, Vel = dir * Range(120f, 320f), Life = Range(0.12f, 0.35f), Size0 = 0.07f * k, Size1 = 0.05f * k,
                Drag = 1.2f, Gravity = 1f, C0 = new Color(1f, 0.82f, 0.48f, 1f), C1 = new Color(1f, 0.45f, 0.12f, 0f),
                Boost = 2f, Stretch = 0.018f, MaxLength = 5f, MinAngular = 1.2f, Cell = 1, GroundY = groundY
            });
        }
        for (var i = 0; i < Math.Max(1, NP(13 * share)); i++)
        {
            var t = (float)Rng.NextDouble();
            Smoke(new Particle
            {
                Pos = center + Random3() * 1.2f * k,
                Vel = Random3() * Range(1f, 4f) * k + normal * Range(2f, 6f) * k + Vector3.up * Range(0.5f, 2.5f),
                Life = Range(5f, 8f), Size0 = 1.4f * k, Size1 = Range(5f, 8.5f) * k, Rot = Range(0f, 6.3f), RotSpeed = Range(-0.35f, 0.35f),
                Drag = 1f, Buoyancy = 1.1f,
                C0 = new Color(0.13f + 0.06f * t, 0.125f + 0.055f * t, 0.12f + 0.05f * t, 0.92f), C1 = new Color(0.33f, 0.32f, 0.31f, 0f),
                FadeIn = 0.015f, Cell = (byte)Rng.Next(2)
            });
        }
        if (ground) Dirt(pos, normal, normal, k, NP(11 * share), NP(18 * share), groundY);
    }

    
    
    private static void HeatBurst(Vector3 pos, Vector3 normal, bool ground, Vector3 incidence, int caliber, bool auto)
    {
        var k = Mathf.Clamp(caliber / 125f, caliber >= 62 ? 0.5f : 0.18f, 1.6f);
        var share = Small(caliber, auto);
        var center = pos + normal * 0.5f * k;
        Glow(new Particle { Pos = center, Life = 0.1f, Size0 = 3f * k, Size1 = 6.5f * k, C0 = new Color(1f, 0.93f, 0.8f, 1f), C1 = new Color(1f, 0.6f, 0.3f, 0f), Boost = 5f, Cell = 0 });
        Glow(new Particle { Pos = center, Life = 0.07f, Size0 = 6f * k, Size1 = 11f * k, C0 = new Color(1f, 0.95f, 0.85f, 1f), C1 = new Color(1f, 0.7f, 0.4f, 0f), Boost = 2.5f, Cell = 3 });
        for (var i = 0; i < Math.Max(1, NP(6 * share)); i++)
        {
            Glow(new Particle
            {
                Pos = center + Random3() * 0.5f * k, Vel = (Random3() + normal) * Range(5f, 12f) * k,
                Life = Range(0.25f, 0.45f), Size0 = 1f * k, Size1 = Range(2.5f, 3.4f) * k, Rot = Range(0f, 6.3f), RotSpeed = Range(-2f, 2f),
                Drag = 5f, Buoyancy = 2f, C0 = new Color(1f, 0.8f, 0.55f, 1f), C1 = new Color(0.9f, 0.25f, 0.05f, 0f), Boost = 2.4f, Cell = 2
            });
        }
        for (var i = 0; i < NP(45 * share); i++)
        {
            var hot = (float)Rng.NextDouble();
            Glow(new Particle
            {
                Pos = center, Vel = Hemisphere(normal) * Range(80f, 280f), Life = Range(0.2f, 0.6f), Size0 = 0.06f * Mathf.Max(k, 0.4f), Size1 = 0.04f * Mathf.Max(k, 0.4f),
                Drag = 1.3f, Gravity = 1f, C0 = new Color(1f, 0.88f + 0.12f * hot, 0.6f + 0.3f * hot, 1f), C1 = new Color(1f, 0.45f, 0.12f, 0f),
                Boost = 2.5f, Stretch = 0.02f, MaxLength = 4f, MinAngular = 1.2f, Cell = 1, GroundY = ground ? pos.y - 0.1f : float.NaN
            });
        }
        for (var i = 0; i < Math.Max(1, NP(9 * share)); i++)
        {
            Smoke(new Particle
            {
                Pos = center + Random3() * 0.8f * k, Vel = Random3() * Range(1f, 3f) + normal * Range(1.5f, 4f) + Vector3.up * Range(0.5f, 2f),
                Life = Range(4f, 6.5f), Size0 = 1.1f * k, Size1 = Range(4f, 6f) * k, Rot = Range(0f, 6.3f), RotSpeed = Range(-0.4f, 0.4f),
                Drag = 1.1f, Buoyancy = 1f, C0 = new Color(0.1f, 0.095f, 0.09f, 0.92f), C1 = new Color(0.3f, 0.29f, 0.28f, 0f),
                FadeIn = 0.015f, Cell = (byte)Rng.Next(2)
            });
        }
        if (ground) Dirt(pos, normal, normal, 0.8f * k, NP(7 * share), NP(12 * share), pos.y - 0.15f);
    }

    
    
    
    private static void ThermobaricBurst(Vector3 pos, Vector3 normal, bool ground, float radius)
    {
        var r = Mathf.Max(2f, radius);
        var k = Mathf.Clamp(r / 10f, 0.3f, 1.6f);
        var center = pos + normal * Mathf.Min(1.5f, 0.15f * r);
        
        Glow(new Particle { Pos = center, Life = 0.06f, Size0 = 1.5f * k, Size1 = 4f * k, C0 = new Color(1f, 0.95f, 0.85f, 1f), C1 = new Color(1f, 0.7f, 0.4f, 0f), Boost = 4f, Cell = 3 });
        for (var i = 0; i < N(12); i++)
        {
            var d = ground ? Hemisphere(normal) : Random3().normalized;
            Smoke(new Particle
            {
                Pos = center + d * Range(0.2f, 0.6f) * r * 0.3f, Vel = d * Range(25f, 55f) * k,
                Life = Range(0.25f, 0.4f), Size0 = 0.6f * r * 0.3f, Size1 = Range(0.7f, 1f) * r * 0.6f, Rot = Range(0f, 6.3f),
                Drag = 7f, C0 = new Color(0.72f, 0.7f, 0.66f, 0.55f), C1 = new Color(0.8f, 0.78f, 0.74f, 0f), FadeIn = 0.05f, Cell = (byte)Rng.Next(2)
            });
        }
        var groundY = ground ? pos.y - 0.15f : float.NaN;
        Emitters.Add(new Emitter
        {
            Thermo = true, Center = center, Pos = pos, Radius = r, Intensity = k, Up = ground ? normal : Vector3.up,
            Landed = ground, GroundY = groundY, Life = 6f, Seed = Range(0f, 100f)
        });
    }

    
    private static void TickThermo(Emitter em, float dt)
    {
        const float ignite = 0.12f;
        if (em.Age < ignite) return;
        var r = em.Radius;
        var k = em.Intensity;
        var up = em.Up;
        var ground = em.Landed;
        if (!em.Vent && em.Burn <= 0f)
        {
            em.Burn = 1f; 
            Glow(new Particle { Pos = em.Center, Life = 0.12f, Size0 = r * 1.2f, Size1 = r * 2.6f, C0 = new Color(1f, 0.96f, 0.86f, 1f), C1 = new Color(1f, 0.7f, 0.35f, 0f), Boost = 6f, Cell = 3 });
            Glow(new Particle { Pos = em.Center, Life = 0.35f, Size0 = r * 1.1f, Size1 = r * 1.8f, C0 = new Color(1f, 0.85f, 0.55f, 1f), C1 = new Color(1f, 0.45f, 0.1f, 0f), Boost = 4f, Cell = 0 });
            
            for (var i = 0; i < N(26 * Mathf.Max(0.6f, k)); i++)
            {
                var d = ground ? Hemisphere(up) : Random3().normalized;
                Glow(new Particle
                {
                    Pos = em.Center + d * Range(0.1f, 0.55f) * r, Vel = d * Range(4f, 10f) * k + up * Range(2f, 6f) * k,
                    Life = Range(0.7f, 1.4f), Size0 = r * Range(0.3f, 0.4f), Size1 = r * Range(0.6f, 0.9f), Rot = Range(0f, 6.3f), RotSpeed = Range(-1.5f, 1.5f),
                    Drag = 2.5f, Buoyancy = 8f * k, C0 = new Color(1f, 0.86f, 0.52f, 1f), C1 = new Color(0.8f, 0.18f, 0.03f, 0f), Boost = 3f, Cell = 2
                });
            }
            
            for (var i = 0; i < N(60); i++)
            {
                var hot = (float)Rng.NextDouble();
                var d = ground ? Hemisphere(up) : Random3().normalized;
                Glow(new Particle
                {
                    Pos = em.Center + d * r * 0.3f, Vel = d * Range(40f, 120f) * Mathf.Sqrt(k), Life = Range(0.6f, 1.8f),
                    Size0 = 0.12f * Mathf.Max(k, 0.5f), Size1 = 0.06f, Drag = 1.6f, Gravity = 1f,
                    C0 = new Color(1f, 0.8f + 0.2f * hot, 0.45f + 0.3f * hot, 1f), C1 = new Color(1f, 0.35f, 0.08f, 0f),
                    Boost = 2.4f, Stretch = 0.02f, MaxLength = 4f, MinAngular = 1.2f, Cell = 1, GroundY = ground ? em.GroundY : float.NaN
                });
            }
            
            if (ground)
            {
                var a = Perp(up);
                var b = Vector3.Cross(up, a);
                for (var i = 0; i < N(26); i++)
                {
                    var ang = (float)Rng.NextDouble() * Mathf.PI * 2f;
                    var radial = a * Mathf.Cos(ang) + b * Mathf.Sin(ang);
                    Smoke(new Particle
                    {
                        Pos = em.Pos + radial * Range(0.3f, 0.7f) * r + up * 0.4f, Vel = radial * Range(25f, 45f) * k + up * Range(0.5f, 2f),
                        Life = Range(2.5f, 4f), Size0 = 2f * k, Size1 = Range(6f, 10f) * k, Rot = Range(0f, 6.3f), RotSpeed = Range(-0.3f, 0.3f),
                        Drag = 2.2f, Buoyancy = 0.15f, C0 = new Color(0.45f, 0.39f, 0.32f, 0.7f), C1 = new Color(0.55f, 0.5f, 0.44f, 0f),
                        FadeIn = 0.02f, Cell = 3
                    });
                }
                Dirt(em.Pos, up, up, 1.5f * k, N(10), N(20), em.GroundY);
            }
        }

        var t = em.Age - ignite;
        
        if (t < 1.1f)
        {
            em.AccCore += 45f * (1f - t / 1.1f) * Density * dt;
            while (em.AccCore >= 1f)
            {
                em.AccCore -= 1f;
                Glow(new Particle
                {
                    Pos = em.Center + Random3() * r * 0.35f + up * t * r * 0.6f, Vel = up * Range(6f, 14f) * k + Random3() * 2f,
                    Life = Range(0.5f, 0.9f), Size0 = r * 0.28f, Size1 = r * Range(0.45f, 0.6f), Rot = Range(0f, 6.3f), RotSpeed = Range(-2f, 2f),
                    Drag = 1.8f, Buoyancy = 10f * k, C0 = new Color(1f, 0.78f, 0.42f, 0.95f), C1 = new Color(0.75f, 0.15f, 0.03f, 0f), Boost = 2.6f, Cell = 2
                });
            }
        }
        
        if (t < 4f)
        {
            em.AccSmoke += 10f * Mathf.Lerp(1f, 0.3f, t / 4f) * Density * dt;
            while (em.AccSmoke >= 1f)
            {
                em.AccSmoke -= 1f;
                DarkSmoke(new Particle
                {
                    Pos = em.Center + Random3() * r * 0.4f + up * Mathf.Min(t, 1.5f) * r * 0.7f,
                    Vel = up * Range(4f, 8f) * k + Random3() * 1.5f,
                    Life = Range(10f, 16f), Size0 = r * 0.5f, Size1 = r * Range(1.2f, 1.8f),
                    Rot = Range(0f, 6.3f), RotSpeed = Range(-0.2f, 0.2f), Drag = 0.8f, Buoyancy = 1.2f, Turb = 0.6f, Wind = 2f
                });
            }
        }
    }

    
    public static void Airburst(Vector3 pos, int caliber)
    {
        RecordDetonation(pos, Kind.External(Kind.Atgm), caliber, false);
        
        if (CombatFx.Detonation(pos, Vector3.up, caliber, Kind.External(Kind.Atgm), false, false)) return;
        if (!On) return;
        try
        {
            var k = Mathf.Clamp(caliber / 125f, 0.5f, 1.6f);
            Glow(new Particle { Pos = pos, Life = 0.09f, Size0 = 2f * k, Size1 = 4.5f * k, C0 = new Color(1f, 0.9f, 0.7f, 1f), C1 = new Color(1f, 0.5f, 0.2f, 0f), Boost = 4f, Cell = 0 });
            for (var i = 0; i < N(4); i++)
                Glow(new Particle
                {
                    Pos = pos + Random3() * 0.4f, Vel = Random3() * Range(4f, 9f), Life = Range(0.2f, 0.35f), Size0 = 0.8f * k, Size1 = 2f * k,
                    Rot = Range(0f, 6.3f), Drag = 5f, C0 = new Color(1f, 0.75f, 0.45f, 1f), C1 = new Color(0.9f, 0.2f, 0.04f, 0f), Boost = 2f, Cell = 2
                });
            for (var i = 0; i < N(30); i++)
                Glow(new Particle
                {
                    Pos = pos, Vel = Random3().normalized * Range(80f, 220f), Life = Range(0.15f, 0.4f), Size0 = 0.05f, Size1 = 0.04f,
                    Drag = 1.2f, Gravity = 1f, C0 = new Color(1f, 0.85f, 0.55f, 1f), C1 = new Color(1f, 0.4f, 0.1f, 0f),
                    Boost = 2f, Stretch = 0.018f, MaxLength = 4f, MinAngular = 1.2f, Cell = 1, GroundY = float.NaN
                });
            for (var i = 0; i < N(6); i++)
                Smoke(new Particle
                {
                    Pos = pos + Random3() * 0.6f, Vel = Random3() * Range(1f, 3f), Life = Range(3.5f, 5.5f), Size0 = 0.9f * k, Size1 = Range(3f, 4.5f) * k,
                    Rot = Range(0f, 6.3f), RotSpeed = Range(-0.4f, 0.4f), Drag = 1.2f, Buoyancy = 0.5f,
                    C0 = new Color(0.16f, 0.15f, 0.14f, 0.85f), C1 = new Color(0.35f, 0.34f, 0.33f, 0f), FadeIn = 0.02f, Cell = (byte)Rng.Next(2)
                });
        }
        catch (Exception e) { Plugin.Log.LogWarning("Airburst effect failed: " + e.Message); }
    }

    
    
    public static void ApsBurst(Vector3 launcher, Vector3 point, Vector3 threatDir, bool kinetic, bool success)
    {
        if (!On) return;
        try
        {
            var dir = SafeDir(threatDir);
            
            var outDir = SafeDir(point - launcher);
            Glow(new Particle { Pos = launcher + outDir * 0.2f, Life = 0.06f, Size0 = 0.5f, Size1 = 1.3f, C0 = new Color(1f, 0.9f, 0.7f, 1f), C1 = new Color(1f, 0.6f, 0.3f, 0f), Boost = 3f, Cell = 3 });
            for (var i = 0; i < N(5); i++)
                Smoke(new Particle
                {
                    Pos = launcher + outDir * Range(0.1f, 0.6f), Vel = outDir * Range(2f, 6f) + Random3() * 1.2f + Vector3.up * 0.5f,
                    Life = Range(1.5f, 2.5f), Size0 = 0.4f, Size1 = Range(1.6f, 2.4f), Rot = Range(0f, 6.3f), RotSpeed = Range(-0.4f, 0.4f),
                    Drag = 2f, Buoyancy = 0.5f, C0 = new Color(0.86f, 0.86f, 0.84f, 0.7f), C1 = new Color(0.9f, 0.9f, 0.88f, 0f),
                    FadeIn = 0.03f, Cell = (byte)Rng.Next(2)
                });

            
            Glow(new Particle { Pos = point, Life = 0.12f, Size0 = 3f, Size1 = 7.5f, C0 = new Color(1f, 0.92f, 0.72f, 1f), C1 = new Color(1f, 0.6f, 0.25f, 0f), Boost = 4.5f, Cell = 0 });
            Glow(new Particle { Pos = point, Life = 0.07f, Size0 = 5f, Size1 = 11f, C0 = new Color(1f, 0.95f, 0.85f, 1f), C1 = new Color(1f, 0.7f, 0.4f, 0f), Boost = 2.5f, Cell = 3 });
            for (var i = 0; i < N(8); i++)
                Glow(new Particle
                {
                    Pos = point + Random3() * 0.9f, Vel = Random3() * Range(5f, 12f), Life = Range(0.25f, 0.5f),
                    Size0 = 1.2f, Size1 = Range(3f, 4.2f), Rot = Range(0f, 6.3f), RotSpeed = Range(-2f, 2f), Drag = 5f, Buoyancy = 2f,
                    C0 = new Color(1f, 0.78f, 0.45f, 1f), C1 = new Color(0.9f, 0.25f, 0.05f, 0f), Boost = 2.4f, Cell = 2
                });
            
            for (var i = 0; i < N(70); i++)
            {
                var d = i % 2 == 0 ? Cone(-dir, 50f) : Random3().normalized;
                Glow(new Particle
                {
                    Pos = point, Vel = d * Range(150f, 380f), Life = Range(0.12f, 0.4f), Size0 = 0.05f, Size1 = 0.04f,
                    Drag = 1.2f, Gravity = 1f, C0 = new Color(1f, 0.86f, 0.55f, 1f), C1 = new Color(1f, 0.42f, 0.1f, 0f),
                    Boost = 2.2f, Stretch = 0.018f, MaxLength = 4f, MinAngular = 1.2f, Cell = 1, GroundY = float.NaN
                });
            }
            
            if (kinetic && success)
                for (var i = 0; i < N(30); i++)
                    Glow(new Particle
                    {
                        Pos = point, Vel = Cone(dir, 14f) * Range(200f, 600f), Life = Range(0.1f, 0.3f), Size0 = 0.06f, Size1 = 0.05f,
                        Drag = 0.8f, Gravity = 1f, C0 = new Color(1f, 0.95f, 0.8f, 1f), C1 = new Color(1f, 0.5f, 0.15f, 0f),
                        Boost = 3f, Stretch = 0.02f, MaxLength = 6f, MinAngular = 1.2f, Cell = 1, GroundY = float.NaN
                    });
            
            for (var i = 0; i < N(10); i++)
                Smoke(new Particle
                {
                    Pos = point, Vel = Random3().normalized * Range(8f, 25f) + Vector3.up * Range(2f, 8f),
                    Life = Range(1.2f, 2.4f), Size0 = Range(0.08f, 0.2f), Size1 = Range(0.08f, 0.18f), Rot = Range(0f, 6.3f), RotSpeed = Range(-12f, 12f),
                    Drag = 0.3f, Gravity = 1f, C0 = new Color(0.16f, 0.15f, 0.13f, 1f), C1 = new Color(0.16f, 0.15f, 0.13f, 0f),
                    MinAngular = 1f, Cell = 2, GroundY = float.NaN
                });
            
            for (var i = 0; i < N(14); i++)
            {
                var t = (float)Rng.NextDouble();
                Smoke(new Particle
                {
                    Pos = point + Random3() * 1.3f, Vel = Random3() * Range(1.5f, 5f) + Vector3.up * Range(0.8f, 3f),
                    Life = Range(5f, 8f), Size0 = 1.6f, Size1 = Range(6f, 9f), Rot = Range(0f, 6.3f), RotSpeed = Range(-0.3f, 0.3f),
                    Drag = 1.2f, Buoyancy = 1.1f,
                    C0 = new Color(0.5f + 0.15f * t, 0.5f + 0.14f * t, 0.48f + 0.13f * t, 0.9f), C1 = new Color(0.72f, 0.72f, 0.7f, 0f),
                    FadeIn = 0.02f, Cell = (byte)Rng.Next(2)
                });
            }
            
            foreach (var hit in Physics.RaycastAll(point, Vector3.down, 7f))
            {
                if (hit.rigidbody != null) continue;
                var k = Mathf.Lerp(1f, 0.4f, hit.distance / 7f);
                Dirt(hit.point, hit.normal, hit.normal, k, N(9), N(6), hit.point.y - 0.15f);
                break;
            }
        }
        catch (Exception e) { Plugin.Log.LogWarning("APS burst effect failed: " + e.Message); }
    }

    
    
    public static void EraBurst(Vector3 point, Vector3 normal, float size)
    {
        if (!On) return;
        try
        {
            var n = SafeDir(normal);
            var k = Mathf.Clamp(size / 0.25f, 0.6f, 2.5f);
            var center = point + n * 0.15f * k;
            Glow(new Particle { Pos = center, Life = 0.06f, Size0 = 1.4f * k, Size1 = 3.2f * k, C0 = new Color(1f, 0.97f, 0.9f, 1f), C1 = new Color(1f, 0.75f, 0.45f, 0f), Boost = 5f, Cell = 3 });
            Glow(new Particle { Pos = center + n * 0.3f * k, Life = 0.1f, Size0 = 1f * k, Size1 = 2.4f * k, C0 = new Color(1f, 0.85f, 0.6f, 1f), C1 = new Color(1f, 0.45f, 0.15f, 0f), Boost = 3.5f, Cell = 0 });
            for (var i = 0; i < N(5); i++)
                Glow(new Particle
                {
                    Pos = center + Random3() * 0.2f * k, Vel = (Cone(n, 55f) * Range(4f, 10f) + Random3() * 2f) * k,
                    Life = Range(0.15f, 0.3f), Size0 = 0.5f * k, Size1 = Range(1.3f, 2f) * k, Rot = Range(0f, 6.3f), RotSpeed = Range(-2f, 2f),
                    Drag = 6f, Buoyancy = 1.5f, C0 = new Color(1f, 0.8f, 0.5f, 1f), C1 = new Color(0.9f, 0.25f, 0.05f, 0f), Boost = 2.2f, Cell = 2
                });
            
            for (var i = 0; i < N(30); i++)
                Glow(new Particle
                {
                    Pos = center, Vel = Cone(n, 75f) * Range(60f, 200f), Life = Range(0.12f, 0.35f), Size0 = 0.04f, Size1 = 0.03f,
                    Drag = 1.4f, Gravity = 1f, C0 = new Color(1f, 0.9f, 0.65f, 1f), C1 = new Color(1f, 0.45f, 0.12f, 0f),
                    Boost = 2.2f, Stretch = 0.018f, MaxLength = 3f, MinAngular = 1.2f, Cell = 1, GroundY = float.NaN
                });
            
            for (var i = 0; i < N(6); i++)
                Smoke(new Particle
                {
                    Pos = center, Vel = Cone(n, 40f) * Range(10f, 30f) + Vector3.up * Range(1f, 5f),
                    Life = Range(1f, 2f), Size0 = Range(0.06f, 0.14f) * k, Size1 = Range(0.06f, 0.12f) * k, Rot = Range(0f, 6.3f), RotSpeed = Range(-14f, 14f),
                    Drag = 0.4f, Gravity = 1f, C0 = new Color(0.18f, 0.17f, 0.15f, 1f), C1 = new Color(0.18f, 0.17f, 0.15f, 0f),
                    MinAngular = 1f, Cell = 2, GroundY = float.NaN
                });
            
            for (var i = 0; i < N(7); i++)
            {
                var t = (float)Rng.NextDouble();
                Smoke(new Particle
                {
                    Pos = center + Random3() * 0.3f * k, Vel = n * Range(1.5f, 4f) * k + Random3() * Range(0.5f, 1.5f) + Vector3.up * Range(0.4f, 1.2f),
                    Life = Range(2.5f, 4f), Size0 = 0.6f * k, Size1 = Range(2.2f, 3.4f) * k, Rot = Range(0f, 6.3f), RotSpeed = Range(-0.4f, 0.4f),
                    Drag = 1.6f, Buoyancy = 0.8f,
                    C0 = new Color(0.32f + 0.12f * t, 0.31f + 0.12f * t, 0.3f + 0.11f * t, 0.85f), C1 = new Color(0.55f, 0.55f, 0.53f, 0f),
                    FadeIn = 0.015f, Cell = (byte)Rng.Next(2)
                });
            }
        }
        catch (Exception e) { Plugin.Log.LogWarning("ERA burst effect failed: " + e.Message); }
    }

    
    public static void SmokeLaunch(Vector3 muzzle, Vector3 velocity, float flightTime)
    {
        
        var replaced = CombatFx.SmokeLaunch(muzzle, velocity);
        if (!On) return;
        try
        {
            var dir = SafeDir(velocity);
            if (!replaced) Glow(new Particle { Pos = muzzle + dir * 0.15f, Life = 0.05f, Size0 = 0.3f, Size1 = 0.8f, C0 = new Color(1f, 0.85f, 0.6f, 1f), C1 = new Color(1f, 0.5f, 0.2f, 0f), Boost = 2.5f, Cell = 3 });
            for (var i = 0; i < (replaced ? 0 : N(4)); i++)
                Smoke(new Particle
                {
                    Pos = muzzle + dir * Range(0.1f, 0.5f), Vel = dir * Range(1.5f, 4f) + Random3() * 0.8f,
                    Life = Range(1.2f, 2f), Size0 = 0.3f, Size1 = Range(1f, 1.6f), Rot = Range(0f, 6.3f), RotSpeed = Range(-0.4f, 0.4f),
                    Drag = 2f, Buoyancy = 0.4f, C0 = new Color(0.85f, 0.85f, 0.83f, 0.6f), C1 = new Color(0.9f, 0.9f, 0.88f, 0f),
                    FadeIn = 0.03f, Cell = (byte)Rng.Next(2)
                });
            
            Glow(new Particle
            {
                Pos = muzzle, Vel = velocity, Life = Mathf.Max(0.1f, flightTime), Size0 = 0.08f, Size1 = 0.08f, Gravity = 1f,
                C0 = new Color(1f, 0.6f, 0.25f, 0.8f), C1 = new Color(1f, 0.45f, 0.15f, 0.6f), Boost = 1.5f, Cell = 0,
                MinAngular = 1.5f, GroundY = float.NaN
            });
        }
        catch (Exception e) { Plugin.Log.LogWarning("Smoke launch effect failed: " + e.Message); }
    }

    
    
    
    
    

    private sealed class Emitter
    {
        public bool Pellet;                 
        public Vector3 Pos, Vel;
        public float Age, Life, Acc, GroundY;
        public bool Landed;
        public Vector3 Center;
        public float Radius, HalfHeight, Build, Duration;
        
        public bool Vent;
        public Transform Anchor;
        public Vector3 LocalPos, LocalUp, Up;
        public float Size, Intensity, Burn, Seed, FlareUntil, AccCore, AccEmber, AccSmoke;
        
        
        public bool Thermo;
    }

    private static readonly List<Emitter> Emitters = new();

    public static void ClearEmitters()
    {
        Emitters.Clear();
        Particles.SmokeLit.Clear();
    }

    public static void TickEmitters(float dt)
    {
        if (Emitters.Count == 0 || dt <= 0f) return;
        dt = Mathf.Min(dt, 0.1f);
        var g = Physics.gravity;
        for (var i = Emitters.Count - 1; i >= 0; i--)
        {
            var em = Emitters[i];
            em.Age += dt;
            if (em.Age >= em.Life) { Emitters.RemoveAt(i); continue; }
            try
            {
                if (em.Vent) TickVent(em, dt);
                else if (em.Thermo) TickThermo(em, dt);
                else if (em.Pellet) TickPellet(em, dt, g);
                else TickVolume(em, dt);
            }
            catch { Emitters.RemoveAt(i); }
        }
    }

    
    private static void TickPellet(Emitter em, float dt, Vector3 g)
    {
        if (!em.Landed)
        {
            em.Vel += g * dt;
            em.Vel *= Mathf.Exp(-0.4f * dt);
            em.Pos += em.Vel * dt;
            if (em.Pos.y <= em.GroundY + 0.15f)
            {
                em.Pos.y = em.GroundY + 0.15f;
                em.Vel = Vector3.zero;
                em.Landed = true;
            }
        }
        var burn = Mathf.Clamp01(1f - em.Age / em.Life);
        var rate = em.Landed ? 1.3f * Mathf.Lerp(0.25f, 1f, burn) : 15f;
        em.Acc += rate * Density * dt;
        while (em.Acc >= 1f)
        {
            em.Acc -= 1f;
            if (em.Landed)
                SmokeLit(new Particle
                {
                    Pos = em.Pos + new Vector3(Range(-0.3f, 0.3f), Range(0f, 0.4f), Range(-0.3f, 0.3f)),
                    Vel = Vector3.up * Range(0.7f, 1.6f) + Random3() * 0.5f,
                    Life = Range(7f, 10f), Size0 = Range(1f, 1.6f), Size1 = Range(4.5f, 7.5f),
                    Rot = Range(0f, 6.3f), RotSpeed = Range(-0.18f, 0.18f), Drag = 0.8f, Buoyancy = 0.12f, Turb = 0.6f, Wind = 1f,
                    C0 = WhiteSmoke(0.5f), C1 = WhiteSmoke(0.5f), FadeIn = 0.06f, FadeOut = 0.4f
                });
            else
                SmokeLit(new Particle
                {
                    Pos = em.Pos + Random3() * 0.15f, Vel = em.Vel * 0.05f + Random3() * 0.6f,
                    Life = Range(5f, 8f), Size0 = Range(0.35f, 0.55f), Size1 = Range(1.8f, 3.2f),
                    Rot = Range(0f, 6.3f), RotSpeed = Range(-0.3f, 0.3f), Drag = 1.2f, Buoyancy = 0.1f, Turb = 0.5f, Wind = 1f,
                    C0 = WhiteSmoke(0.6f), C1 = WhiteSmoke(0.6f), FadeIn = 0.04f, FadeOut = 0.35f
                });
        }
    }

    
    private static void TickVolume(Emitter em, float dt)
    {
        var building = em.Age < em.Build * 1.2f;
        var rate = building ? 32f / Mathf.Max(0.4f, em.Build * 1.2f) : 1.6f;
        em.Acc += rate * Density * dt;
        while (em.Acc >= 1f)
        {
            em.Acc -= 1f;
            var off = Random3();
            off.y = off.y * 0.7f - 0.15f;       
            var pos = em.Center + Vector3.Scale(off, new Vector3(em.Radius * 0.8f, em.HalfHeight, em.Radius * 0.8f));
            pos.y = Mathf.Max(pos.y, em.GroundY + 0.8f);
            var outward = new Vector3(off.x, off.y * 0.4f, off.z);
            SmokeLit(new Particle
            {
                Pos = pos, Vel = outward * Range(0.6f, 2.2f) + Vector3.up * Range(0f, 0.3f),
                Life = Range(8f, 12f), Size0 = Range(2f, 3.4f), Size1 = Range(5.5f, 8.5f),
                Rot = Range(0f, 6.3f), RotSpeed = Range(-0.12f, 0.12f), Drag = 0.9f, Buoyancy = 0.06f, Turb = 0.5f, Wind = 1f,
                C0 = WhiteSmoke(0.45f), C1 = WhiteSmoke(0.45f), FadeIn = 0.08f, FadeOut = 0.45f
            });
        }
    }

    
    
    
    
    public static void VentFire(Transform anchor, Vector3 localPos, Vector3 localUp, float size, float intensity, float duration)
    {
        
        if (CombatFx.VentFire(anchor, localPos, localUp, size, intensity, duration)) return;
        if (!On) return;
        try
        {
            var w = Mathf.Clamp(size, 0.3f, 4f);
            var k = Mathf.Clamp(intensity, 0.2f, 2f);
            duration = Mathf.Clamp(duration, 2f, 60f);
            var pos = anchor != null ? anchor.TransformPoint(localPos) : Vector3.zero;
            var up = anchor != null ? SafeDir(anchor.TransformDirection(localUp)) : Vector3.up;
            var side = Perp(up);
            var side2 = Vector3.Cross(up, side);

            
            Glow(new Particle { Pos = pos + up * 1.5f * k, Life = 0.14f, Size0 = 3f * k, Size1 = 9f * k, C0 = new Color(1f, 0.93f, 0.78f, 1f), C1 = new Color(1f, 0.6f, 0.25f, 0f), Boost = 5f, Cell = 0 });
            Glow(new Particle { Pos = pos + up * 0.5f, Life = 0.08f, Size0 = 4f * k, Size1 = 10f * k, C0 = new Color(1f, 0.96f, 0.88f, 1f), C1 = new Color(1f, 0.7f, 0.4f, 0f), Boost = 3f, Cell = 3 });
            for (var i = 0; i < N(14); i++)
            {
                var off = (side * Range(-0.5f, 0.5f) + side2 * Range(-0.5f, 0.5f)) * w;
                Glow(new Particle
                {
                    Pos = pos + off + up * Range(0f, 2f) * k, Vel = Cone(up, 14f) * Range(18f, 40f) * k + off * 2f,
                    Life = Range(0.5f, 0.95f), Size0 = Mathf.Max(0.8f, w) * k, Size1 = Range(3.5f, 6f) * k, Rot = Range(0f, 6.3f), RotSpeed = Range(-2f, 2f),
                    Drag = 2.4f, Buoyancy = 6f, C0 = new Color(1f, 0.8f, 0.48f, 1f), C1 = new Color(0.9f, 0.28f, 0.05f, 0f), Boost = 3f, Cell = 2
                });
            }
            
            for (var i = 0; i < N(70); i++)
            {
                var hot = (float)Rng.NextDouble();
                Glow(new Particle
                {
                    Pos = pos + up * 0.3f, Vel = Cone(up, 45f) * Range(25f, 110f), Life = Range(0.4f, 1.4f), Size0 = 0.06f, Size1 = 0.04f,
                    Drag = 1.1f, Gravity = 1f, C0 = new Color(1f, 0.85f + 0.15f * hot, 0.55f + 0.35f * hot, 1f), C1 = new Color(1f, 0.4f, 0.1f, 0f),
                    Boost = 2.4f, Stretch = 0.02f, MaxLength = 3f, MinAngular = 1.2f, Cell = 1, GroundY = float.NaN
                });
            }
            
            for (var i = 0; i < N(12); i++)
                Smoke(new Particle
                {
                    Pos = pos + up * 0.2f, Vel = Cone(up, 35f) * Range(10f, 32f) + Random3() * 3f,
                    Life = Range(1.5f, 3f), Size0 = Range(0.08f, 0.22f), Size1 = Range(0.08f, 0.2f), Rot = Range(0f, 6.3f), RotSpeed = Range(-14f, 14f),
                    Drag = 0.3f, Gravity = 1f, C0 = new Color(0.14f, 0.13f, 0.12f, 1f), C1 = new Color(0.14f, 0.13f, 0.12f, 0f),
                    MinAngular = 1f, Cell = 2, GroundY = float.NaN
                });
            
            for (var i = 0; i < N(8); i++)
                DarkSmoke(new Particle
                {
                    Pos = pos + up * Range(1f, 4f) * k + Random3() * w, Vel = up * Range(3f, 7f) + Random3() * 2.5f,
                    Life = Range(7f, 10f), Size0 = Mathf.Max(1f, w) * 1.5f, Size1 = Range(6f, 9f) * k,
                    Rot = Range(0f, 6.3f), RotSpeed = Range(-0.25f, 0.25f), Drag = 1f, Buoyancy = 0.6f, Turb = 0.6f, Wind = 2f
                });

            Emitters.Add(new Emitter
            {
                Vent = true, Anchor = anchor, LocalPos = localPos, LocalUp = localUp, Pos = pos, Up = up,
                Size = w, Intensity = k, Burn = duration, Life = duration + 12f, Seed = Range(0f, 100f)
            });
        }
        catch (Exception e) { Plugin.Log.LogWarning("Vent fire effect failed: " + e.Message); }
    }

    private static void TickVent(Emitter em, float dt)
    {
        
        if (em.Anchor != null)
            try
            {
                if (em.Anchor.WasCollected) em.Anchor = null;
                else
                {
                    em.Pos = em.Anchor.TransformPoint(em.LocalPos);
                    em.Up = SafeDir(em.Anchor.TransformDirection(em.LocalUp));
                }
            }
            catch { em.Anchor = null; }

        var f = em.Age / Mathf.Max(0.1f, em.Burn);
        var s = f < 0.35f ? 1f : f < 1f ? Mathf.Lerp(0.7f, 0.2f, (f - 0.35f) / 0.65f) : 0f;
        if (s > 0f)
        {
            
            s *= 0.82f + 0.22f * Mathf.Sin(em.Age * 23f + em.Seed) * Mathf.Sin(em.Age * 7.3f + em.Seed * 0.7f);
            if (em.FlareUntil > em.Age) s = Mathf.Max(s, 0.95f);
            else if (f > 0.35f && Rng.NextDouble() < dt * 0.45f) em.FlareUntil = em.Age + Range(0.3f, 0.8f);
        }
        var w = em.Size;
        var k = em.Intensity;
        var up = em.Up;
        var side = Perp(up);
        var side2 = Vector3.Cross(up, side);
        Vector3 Off(float spread) => (side * Range(-0.5f, 0.5f) + side2 * Range(-0.5f, 0.5f)) * w * spread;

        if (s > 0f)
        {
            
            em.Acc += 70f * s * Density * dt;
            while (em.Acc >= 1f)
            {
                em.Acc -= 1f;
                var lean = side * Range(-0.08f, 0.08f) + side2 * Range(-0.08f, 0.08f);
                Glow(new Particle
                {
                    Pos = em.Pos + Off(0.7f) + up * Range(0f, 0.3f),
                    Vel = (up + lean) * Range(12f, 26f) * (0.55f + 0.45f * s) * (0.75f + 0.25f * k) + Particles.Wind * 2f,
                    Life = Range(0.4f, 0.75f) * (0.7f + 0.3f * s), Size0 = w * Range(0.35f, 0.55f),
                    Size1 = Range(1.6f, 2.6f) * Mathf.Max(w, 0.8f) * (0.6f + 0.4f * k), Rot = Range(0f, 6.3f), RotSpeed = Range(-3f, 3f),
                    Drag = 1.6f, Buoyancy = 6f, C0 = new Color(1f, 0.78f, 0.45f, 0.95f), C1 = new Color(0.85f, 0.22f, 0.04f, 0f), Boost = 2.6f, Cell = 2
                });
            }
            
            em.AccCore += 25f * s * Density * dt;
            while (em.AccCore >= 1f)
            {
                em.AccCore -= 1f;
                Glow(new Particle
                {
                    Pos = em.Pos + Off(0.45f), Vel = up * Range(15f, 30f) * (0.6f + 0.4f * s), Life = Range(0.12f, 0.25f),
                    Size0 = w * 0.5f, Size1 = w * 1.1f, Rot = Range(0f, 6.3f), Drag = 1f,
                    C0 = new Color(1f, 0.95f, 0.8f, 1f), C1 = new Color(1f, 0.7f, 0.35f, 0f), Boost = 3.5f, Cell = 0
                });
            }
            
            em.AccEmber += 18f * s * Density * dt;
            while (em.AccEmber >= 1f)
            {
                em.AccEmber -= 1f;
                Glow(new Particle
                {
                    Pos = em.Pos + Off(0.6f), Vel = Cone(up, 25f) * Range(8f, 30f), Life = Range(0.8f, 2.2f), Size0 = 0.05f, Size1 = 0.03f,
                    Drag = 0.8f, Gravity = 0.4f, Buoyancy = 1f, C0 = new Color(1f, 0.8f, 0.45f, 1f), C1 = new Color(1f, 0.35f, 0.08f, 0f),
                    Boost = 2.2f, Stretch = 0.015f, MaxLength = 1.5f, MinAngular = 1f, Cell = 1, GroundY = float.NaN
                });
            }
        }

        
        var smokeRate = s > 0f ? 7f + 9f * s : 6f * Mathf.Clamp01(1f - (em.Age - em.Burn) / 12f);
        em.AccSmoke += smokeRate * Density * dt;
        while (em.AccSmoke >= 1f)
        {
            em.AccSmoke -= 1f;
            var top = s > 0f ? up * Range(3f, 7f) * (0.5f + 0.5f * s) : up * Range(0.2f, 1f);
            DarkSmoke(new Particle
            {
                Pos = em.Pos + Off(0.8f) + top, Vel = up * Range(2.5f, 5f) * (0.6f + 0.6f * s) + Random3() * 0.8f,
                Life = Range(9f, 14f), Size0 = Mathf.Max(1f, w) * Range(1.2f, 1.8f), Size1 = Range(6f, 10f) * (0.7f + 0.3f * k),
                Rot = Range(0f, 6.3f), RotSpeed = Range(-0.2f, 0.2f), Drag = 0.5f, Buoyancy = 0.5f, Turb = 0.7f, Wind = 2.5f
            });
        }
    }

    
    private static void DarkSmoke(Particle p)
    {
        var t = (float)Rng.NextDouble();
        var c = new Color(0.07f + 0.04f * t, 0.065f + 0.04f * t, 0.06f + 0.035f * t, 0.9f);
        if (Fx.SmokeLitMat != null)
        {
            p.C0 = c;
            p.C1 = c;
            p.FadeIn = 0.04f;
            p.FadeOut = 0.5f;
            SmokeLit(p);
            return;
        }
        p.C0 = c;
        p.C1 = new Color(0.25f, 0.24f, 0.23f, 0f);
        p.FadeIn = 0.03f;
        p.Cell = (byte)Rng.Next(2);
        Smoke(p);
    }

    private static Color WhiteSmoke(float alpha)
    {
        var t = (float)Rng.NextDouble();
        return new Color(0.94f + 0.05f * t, 0.94f + 0.05f * t, 0.92f + 0.05f * t, alpha);
    }

    private static void SmokeLit(Particle p)
    {
        p.GroundY = float.NaN;
        p.Cell = (byte)Rng.Next(16);
        if (p.Seed == 0f) p.Seed = (float)Rng.NextDouble();
        Particles.SmokeLit.Add(p);
    }

    
    private static float GroundBelow(Vector3 p, float fallback)
    {
        try
        {
            var best = float.MaxValue;
            var y = fallback;
            foreach (var h in Physics.RaycastAll(p + Vector3.up * 2f, Vector3.down, 60f))
            {
                if (h.rigidbody != null || h.distance >= best) continue;
                best = h.distance;
                y = h.point.y;
            }
            return y;
        }
        catch { return fallback; }
    }

    
    
    public static void SmokeCloud(Vector3 center, float radius, float buildTime, float duration)
    {
        
        if (CombatFx.SmokeCloud(center, radius, buildTime, duration)) return;
        if (!On) return;
        if (Fx.SmokeLitMat == null) { SmokeCloudFlat(center, radius, buildTime, duration); return; }
        try
        {
            radius = Mathf.Clamp(radius, 2f, 30f);
            buildTime = Mathf.Clamp(buildTime, 0.2f, 10f);
            duration = Mathf.Clamp(duration, 2f, 120f);
            var halfHeight = radius * 0.55f;
            var ground = GroundBelow(center, center.y - halfHeight * 0.7f);
            var k = radius / 10f;

            
            Glow(new Particle { Pos = center, Life = 0.1f, Size0 = 1.5f, Size1 = 4.5f, C0 = new Color(1f, 0.88f, 0.65f, 1f), C1 = new Color(1f, 0.55f, 0.25f, 0f), Boost = 3f, Cell = 3 });
            for (var i = 0; i < N(10); i++)
                SmokeLit(new Particle
                {
                    Pos = center + Random3() * 0.4f, Vel = Random3().normalized * Range(5f, 12f) * k,
                    Life = Range(6f, 9f), Size0 = Range(0.8f, 1.3f), Size1 = Range(4f, 6f) * k,
                    Rot = Range(0f, 6.3f), RotSpeed = Range(-0.25f, 0.25f), Drag = 2.2f, Buoyancy = 0.05f, Turb = 0.3f, Wind = 1f,
                    C0 = WhiteSmoke(0.75f), C1 = WhiteSmoke(0.75f), FadeIn = 0.02f, FadeOut = 0.4f
                });

            
            var burnTime = buildTime + duration * 0.6f;
            var pellets = N(12);
            for (var i = 0; i < pellets; i++)
            {
                var ang = (i + Range(-0.35f, 0.35f)) / pellets * Mathf.PI * 2f;
                var horiz = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * Range(6f, 14f) * k;
                var vel = horiz + Vector3.up * Range(-1f, 5f);
                Emitters.Add(new Emitter { Pellet = true, Pos = center, Vel = vel, Life = burnTime * Range(0.85f, 1.1f), GroundY = ground });
                Glow(new Particle
                {
                    Pos = center, Vel = vel, Life = Range(2f, 3.5f), Size0 = 0.28f, Size1 = 0.18f, Gravity = 1f, Drag = 0.4f,
                    C0 = new Color(1f, 0.9f, 0.7f, 1f), C1 = new Color(1f, 0.55f, 0.25f, 0f), Boost = 2.2f, Cell = 0,
                    MinAngular = 1.5f, GroundY = ground + 0.15f
                });
            }

            
            Emitters.Add(new Emitter
            {
                Center = center, Radius = radius, HalfHeight = halfHeight, Build = buildTime, Duration = duration,
                Life = buildTime + duration * 0.55f, GroundY = ground
            });
        }
        catch (Exception e) { Plugin.Log.LogWarning("Smoke screen effect failed: " + e.Message); }
    }

    
    
    
    public static void ExhaustSmoke(Vector3 pos, Vector3 dir, Vector3 vehicleVel, float dt, float intensity)
    {
        
        if (CombatFx.EngineSmoke(pos, dir, intensity)) return;
        if (!On) return;
        try
        {
            var expected = 14f * Mathf.Clamp(intensity, 0f, 3f) * Density * Mathf.Clamp(dt, 0f, 0.1f);
            var n = (int)expected;
            if (Rng.NextDouble() < expected - n) n++;
            dir = SafeDir(dir);
            for (var i = 0; i < n; i++)
            {
                var p = new Particle
                {
                    Pos = pos + dir * Range(0.05f, 0.25f) + Random3() * 0.08f,
                    Vel = dir * Range(2.5f, 5f) + vehicleVel * 0.85f + Random3() * 0.4f,
                    Life = Range(30f, 45f), Size0 = Range(0.35f, 0.6f), Size1 = Range(8f, 13f),
                    Rot = Range(0f, 6.3f), RotSpeed = Range(-0.08f, 0.08f), Drag = 1.6f, Buoyancy = 0.015f, Turb = 0.55f, Wind = 1f,
                    FadeIn = 0.01f, FadeOut = 0.35f
                };
                if (Fx.SmokeLitMat != null)
                {
                    p.C0 = WhiteSmoke(0.6f);
                    p.C1 = WhiteSmoke(0.6f);
                    SmokeLit(p);
                }
                else
                {
                    p.C0 = new Color(0.86f, 0.86f, 0.85f, 0.7f);
                    p.C1 = new Color(0.9f, 0.9f, 0.88f, 0f);
                    p.Cell = (byte)Rng.Next(2);
                    Smoke(p);
                }
            }
        }
        catch (Exception e) { Plugin.Log.LogWarning("Engine smoke effect failed: " + e.Message); }
    }

    
    private static void SmokeCloudFlat(Vector3 center, float radius, float buildTime, float duration)
    {
        try
        {
            radius = Mathf.Clamp(radius, 2f, 30f);
            buildTime = Mathf.Clamp(buildTime, 0.2f, 10f);
            duration = Mathf.Clamp(duration, 2f, 120f);
            Glow(new Particle { Pos = center, Life = 0.08f, Size0 = 1f, Size1 = 3f, C0 = new Color(1f, 0.85f, 0.6f, 1f), C1 = new Color(1f, 0.5f, 0.2f, 0f), Boost = 2.5f, Cell = 3 });
            var count = N(18 * Mathf.Clamp(radius / 8f, 0.5f, 2f));
            for (var i = 0; i < count; i++)
            {
                
                var off = Random3();
                off.y *= 0.5f;
                var t = (float)Rng.NextDouble();
                var life = duration * Range(0.9f, 1.15f) + buildTime;
                Smoke(new Particle
                {
                    Pos = center + off * radius * 0.25f,
                    Vel = off * (radius * 0.75f / Mathf.Max(0.5f, buildTime)) + Vector3.up * Range(0.1f, 0.5f),
                    Life = life, Size0 = radius * 0.35f, Size1 = radius * Range(0.9f, 1.25f),
                    Rot = Range(0f, 6.3f), RotSpeed = Range(-0.1f, 0.1f),
                    Drag = 1.6f / Mathf.Max(0.3f, buildTime), Buoyancy = 0.08f,
                    C0 = new Color(0.82f + 0.08f * t, 0.82f + 0.08f * t, 0.8f + 0.08f * t, 0.95f), C1 = new Color(0.86f, 0.86f, 0.84f, 0f),
                    FadeIn = Mathf.Clamp(buildTime / life * 0.5f, 0.01f, 0.3f), FadeOut = Mathf.Clamp(4f / life, 0.05f, 0.5f),
                    Cell = (byte)Rng.Next(2)
                });
            }
        }
        catch (Exception e) { Plugin.Log.LogWarning("Smoke screen effect failed: " + e.Message); }
    }

    
    private static void Dirt(Vector3 pos, Vector3 normal, Vector3 up, float k, int dust, int chunks, float groundY)
    {
        var tangentA = Perp(normal);
        var tangentB = Vector3.Cross(normal, tangentA);
        for (var i = 0; i < dust; i++)
        {
            var ang = (float)Rng.NextDouble() * Mathf.PI * 2f;
            var radial = tangentA * Mathf.Cos(ang) + tangentB * Mathf.Sin(ang);
            Smoke(new Particle
            {
                Pos = pos + radial * Range(0.3f, 1.2f) * k + normal * 0.3f, Vel = radial * Range(8f, 22f) * k + normal * Range(0.5f, 2f),
                Life = Range(3f, 5f), Size0 = 1.2f * k, Size1 = Range(5f, 8f) * k, Rot = Range(0f, 6.3f), RotSpeed = Range(-0.3f, 0.3f),
                Drag = 2.2f, Buoyancy = 0.15f, C0 = new Color(0.43f, 0.37f, 0.3f, 0.75f), C1 = new Color(0.52f, 0.48f, 0.42f, 0f),
                FadeIn = 0.02f, Cell = 3
            });
        }
        for (var i = 0; i < Math.Max(k < 0.3f ? (dust > 0 ? 1 : 0) : 2, dust / 2); i++)
        {
            Smoke(new Particle
            {
                Pos = pos + normal * 0.5f, Vel = Cone(up, 20f) * Range(12f, 30f) * k,
                Life = Range(1.5f, 2.6f), Size0 = 0.8f * k, Size1 = Range(3f, 4.5f) * k, Rot = Range(0f, 6.3f), RotSpeed = Range(-0.5f, 0.5f),
                Drag = 2.5f, Gravity = 0.35f, C0 = new Color(0.32f, 0.27f, 0.22f, 0.85f), C1 = new Color(0.45f, 0.4f, 0.34f, 0f),
                FadeIn = 0.02f, Cell = 3
            });
        }
        for (var i = 0; i < chunks; i++)
        {
            Smoke(new Particle
            {
                Pos = pos + normal * 0.2f, Vel = Cone(up, 45f) * Range(12f, 38f) * k,
                Life = Range(1.4f, 2.8f), Size0 = Range(0.12f, 0.35f) * k, Size1 = Range(0.12f, 0.3f) * k, Rot = Range(0f, 6.3f), RotSpeed = Range(-12f, 12f),
                Drag = 0.25f, Gravity = 1f, C0 = new Color(0.27f, 0.23f, 0.19f, 1f), C1 = new Color(0.27f, 0.23f, 0.19f, 0f),
                MinAngular = 1f, Cell = 2, GroundY = groundY
            });
        }
    }

    

    private static void Glow(Particle p)
    {
        if (p.GroundY == 0f) p.GroundY = float.NaN;
        Particles.Glow.Add(p);
    }

    private static void Smoke(Particle p)
    {
        if (p.GroundY == 0f) p.GroundY = float.NaN;
        if (p.Cell != 2)
        {
            
            p.Rot = Mathf.Repeat(p.Rot, 1.2f) - 0.6f;
            p.RotSpeed = Mathf.Clamp(p.RotSpeed, -0.25f, 0.25f);
        }
        Particles.Smoke.Add(p);
    }

    private static float Range(float a, float b) => a + (float)Rng.NextDouble() * (b - a);

    private static Vector3 Random3()
    {
        Vector3 v;
        do v = new Vector3(Range(-1f, 1f), Range(-1f, 1f), Range(-1f, 1f));
        while (v.sqrMagnitude > 1f || v.sqrMagnitude < 1e-4f);
        return v;
    }

    private static Vector3 Hemisphere(Vector3 normal)
    {
        var v = Random3().normalized;
        return Vector3.Dot(v, normal) < 0f ? -v : v;
    }

    private static Vector3 Cone(Vector3 axis, float angleDeg)
    {
        axis = axis.sqrMagnitude > 1e-6f ? axis.normalized : Vector3.up;
        var ang = Mathf.Acos(Mathf.Lerp(1f, Mathf.Cos(angleDeg * Mathf.Deg2Rad), (float)Rng.NextDouble()));
        var around = (float)Rng.NextDouble() * 360f;
        var tilt = Quaternion.AngleAxis(ang * Mathf.Rad2Deg, Perp(axis));
        return Quaternion.AngleAxis(around, axis) * (tilt * axis);
    }

    private static Vector3 Perp(Vector3 v)
    {
        var p = Vector3.Cross(v, Vector3.up);
        if (p.sqrMagnitude < 1e-4f) p = Vector3.Cross(v, Vector3.right);
        return p.normalized;
    }

    private static Vector3 SafeDir(Vector3 v) => v.sqrMagnitude > 1e-4f ? v.normalized : Vector3.forward;

    private static IntPtr Locate(Shell s)
    {
        if (s.Register == null || s.Register.WasCollected) return IntPtr.Zero;
        var pool = *(IntPtr*)(s.RegPtr + PoolOffset);
        if (pool == IntPtr.Zero) return IntPtr.Zero;
        var len = *(int*)(pool + 0x18);
        if (s.Index >= 0 && s.Index < len)
        {
            var e = pool + ElemBase + s.Index * ElemSize;
            if (Matches(e, s)) return e;
        }
        for (var i = 0; i < len; i++)
        {
            var e = pool + ElemBase + i * ElemSize;
            if (Matches(e, s)) { s.Index = i; return e; }
        }
        return IntPtr.Zero;
    }

    private static bool Matches(IntPtr e, Shell s) =>
        *(int*)(e + EId) == s.Id && (*(int*)(e + EFlags) & 1) != 0 && *(IntPtr*)(e + EDef) == s.Def;
}
