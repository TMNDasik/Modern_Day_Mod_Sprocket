using System;
using System.Collections.Generic;
using HarmonyLib;
using Sprocket.DamageModelling;
using Sprocket.Vehicles.Fires;
using Sprocket.Vehicles.Weapons.Cannons;
using UnityEngine;

namespace Munitions;







internal static unsafe class Guidance
{
    private const int PoolOffset = 0x38, NextIdOffset = 0x44;
    private const int ElemSize = 0x50, ElemBase = 0x20;
    private const int EDef = 0x08, EId = 0x18, EPos = 0x28, EVel = 0x34, ELayer = 0x40, EFlags = 0x48;
    private const int FlagActive = 1, FlagRelease = 2;

    private const float GatherTime = 0.3f;      
    private const float BoostTime = 1.0f;       
    private const float ArmDistance = 30f;      
    private const float BeamWidth = 12f;        

    internal static int FiringKind = -1;
    private static IntPtr _firingWeapon;
    private static SizeTypes _firingTypes;
    private static ProjectileDefinition _swapped;
    private static float _savedDiameter, _savedLength;

    private static readonly List<Missile> Missiles = new();
    private static int _errors;

    
    private static float LaunchSpeed(int kind) => Mathf.Max(20f, Kind.IsLauncher(kind) ? Plugin.LauncherLaunchSpeed.Value : Plugin.AtgmLaunchSpeed.Value);

    private sealed class Missile
    {
        public int Kind;
        public float Launch, Cruise, Accel, MaxFlight;
        public float JetSpeed;                          
        public HashSet<IntPtr> OwnModels = new();       
        public IntPtr OwnBody;
        public int Id, Index = -1;
        public ProjectileRegister Register;
        public IntPtr RegPtr;
        public IntPtr WeaponPtr;
        public Transform Weapon;
        public int VehicleKey;
        public SizeTypes Types;
        public float LaunchTime;
        public Vector3 LaunchPos;
        public Vector3 BeamOrigin, BeamDir;
        public bool BeamInit, Lost, Ended, Boosted;
        public MissileFx Fx;
        public int Guide;                 
        public bool Jammed;               
        public Quaternion JamOffset = Quaternion.identity;
        public Wire Wire;                 
        
        public bool Rocket, BurnedOut;
        public float Burn;
        public Vector3 Dir, Inherit;      
        public Vector3 LastPos, LastVel;
    }

    
    
    private static Func<int, Vector3, Vector3, Vector3, int, int> _filter;
    public static void SetFilter(Func<int, Vector3, Vector3, Vector3, int, int> filter) => _filter = filter;

    

    [HarmonyPatch(typeof(CannonBehaviour), nameof(CannonBehaviour.FireInternal))]
    private static class FirePatch
    {
        
        private static void Prefix(CannonBehaviour __instance, ref ProjectileTypeID __0)
        {
            try
            {
                FiringKind = -1;
                _firingWeapon = __instance.Pointer;
                var typeId = __instance._LoadedProjectileTypeID_k__BackingField.Value;
                if (!TypeRegistry.TryGetKind(typeId, out var types, out var kind)) return;
                
                if (kind == Kind.Atgm && __0.Value == typeId)
                {
                    var variant = Kind.WithWarhead(Kind.Atgm, AtgmSettings.Warhead(ApfsdsSettings.BlueprintOf(__instance.transform)));
                    if (variant != kind && types.TypeIds[variant] != 0)
                    {
                        __0 = new ProjectileTypeID(types.TypeIds[variant]);
                        kind = variant;
                    }
                }
                FiringKind = kind;
                _firingTypes = types;
                if (kind != Kind.Apfsds) return;
                var def = types.Def(Kind.Apfsds);
                if (def == null) return;
                _savedDiameter = def.Diameter;
                _savedLength = def.Length;
                def.Diameter = types.Caliber / 1000f;
                def.Length = types.Spec.LaunchLength;
                _swapped = def;
            }
            catch (Exception e) { if (_errors++ < 10) Plugin.Log.LogError("Fire prefix failed: " + e); }
        }

        private static void Postfix()
        {
            try
            {
                if (_swapped != null)
                {
                    _swapped.Diameter = _savedDiameter;
                    _swapped.Length = _savedLength;
                }
            }
            catch (Exception e) { if (_errors++ < 10) Plugin.Log.LogError("Fire postfix failed: " + e); }
            _swapped = null;
            _firingWeapon = IntPtr.Zero;
            _firingTypes = null;
            FiringKind = -1;
        }
    }

    
    [HarmonyPatch(typeof(CannonMuzzleFlashEffectsAssemblyStage), nameof(CannonMuzzleFlashEffectsAssemblyStage.PlayEffect))]
    private static class MuzzleFlashPatch
    {
        private static bool Prefix() => !Kind.IsMissile(FiringKind) && !Kind.IsRocket(FiringKind);
    }

    [HarmonyPatch(typeof(ProjectileRegister), nameof(ProjectileRegister.Launch))]
    private static class LaunchPatch
    {
        private static void Postfix(ProjectileRegister __instance)
        {
            if (_firingWeapon == IntPtr.Zero) return;
            try
            {
                
                if (FiringKind < 0 || _firingTypes == null) VanillaBore(__instance);
                else OnLaunched(__instance);
            }
            catch (Exception e) { if (_errors++ < 10) Plugin.Log.LogError("Launch postfix failed: " + e); }
        }
    }

    

    private static void VanillaBore(ProjectileRegister register)
    {
        var reg = register.Pointer;
        var next = *(int*)(reg + NextIdOffset);
        var id = next == 0 ? 0x7FFFFFFE : next - 1;
        var index = FindIndex(reg, id, -1);
        if (index < 0) return;
        ApplyBore(register, id, index, Elem(reg, index), -1);
    }

    
    private static void ApplyBore(ProjectileRegister register, int id, int index, IntPtr e, int kind)
    {
        var weapon = new CannonBehaviour(_firingWeapon);
        var type = Bore.OfWeapon(_firingWeapon, weapon.transform);
        if (type == Bore.Unset) return;
        Bore.Effect(type, kind, AutoFeed.IsAuto(_firingWeapon), out var penetration, out var spread);
        if (spread > 0f) *(Vector3*)(e + EVel) = Bore.Deviate(*(Vector3*)(e + EVel), spread);
        if (penetration >= 0.999f) return;
        var factor = Mathf.Pow(penetration, 1f / 1.43f);
        
        
        if (kind < 0 && AutoFeed.IsAuto(_firingWeapon))
        {
            *(Vector3*)(e + EVel) = *(Vector3*)(e + EVel) * factor;
            return;
        }
        foreach (var d in Darts)
        {
            if (d.Id != id || d.RegPtr != register.Pointer) continue;
            if (d.JetSpeed > 0f) d.JetSpeed *= factor;
            else d.Factor *= factor;
            return;
        }
        Darts.Add(new Dart
        {
            Id = id, Index = index, Register = register, RegPtr = register.Pointer, Def = *(IntPtr*)(e + EDef),
            Factor = factor, LaunchTime = Time.time, LaunchPos = *(Vector3*)(e + EPos)
        });
    }

    private static void OnLaunched(ProjectileRegister register)
    {
        var reg = register.Pointer;
        var next = *(int*)(reg + NextIdOffset);
        var id = next == 0 ? 0x7FFFFFFE : next - 1;
        var index = FindIndex(reg, id, -1);
        if (index < 0) return;
        var e = Elem(reg, index);
        var vel = *(Vector3*)(e + EVel);

        if (FiringKind == Kind.Apfsds) vel = ApplyApfsds(register, id, index, e, vel);
        else if (FiringKind == Kind.Heat) vel = TrackHeat(register, id, index, e, vel);
        if (Kind.IsRocket(FiringKind))
        {
            LaunchRocket(register, id, index, e);
            return;
        }
        if (!Kind.IsMissile(FiringKind))
            try { ApplyBore(register, id, index, e, FiringKind); }
            catch (Exception ex) { if (_errors++ < 10) Plugin.Log.LogError("Bore effect failed: " + ex); }

        if (!Kind.IsMissile(FiringKind))
        {
            Effects.OnLaunch(register, id, index, FiringKind, _firingTypes, *(Vector3*)(e + EPos), vel, 0f, AutoFeed.IsAuto(_firingWeapon));
            if (Plugin.DebugLogging.Value)
            {
                var s = _firingTypes.Spec;
                var speed = vel.magnitude;
                var cal = _firingTypes.Caliber;
                var pen = FiringKind switch
                {
                    Kind.Apfsds => ShellSpec.Penetration(s.RodDiameter, s.RodMass, speed, s.RodConstant),
                    Kind.Heat => s.HeatShellPenetration,
                    Kind.Ap => ShellSpec.Penetration(cal, s.ApMass, speed, s.ApConstant),
                    Kind.Aphe => ShellSpec.Penetration(cal, s.ApheMass, speed, s.ApheConstant),
                    Kind.Hesh => ShellSpec.Penetration(cal, s.HeshMass, speed, s.HeshConstant),
                    _ => ShellSpec.Penetration(cal, s.HeMass, speed, s.HeConstant)
                };
                Plugin.Log.LogInfo($"{_firingTypes.Caliber} mm {Kind.Names[FiringKind]}: {speed:0} m/s, ~{pen:0} mm penetration at the muzzle");
            }
            return;
        }

        var weapon = new CannonBehaviour(_firingWeapon);
        var wt = weapon.transform;
        var pos = *(Vector3*)(e + EPos);
        var fwd = wt != null ? wt.forward : (vel.sqrMagnitude > 1e-4f ? vel.normalized : Vector3.forward);
        var kind = FiringKind;
        AtgmSettings.ForWeapon(wt, kind, out var cruise, out var gs, out var penSetting);
        var launch = Mathf.Min(LaunchSpeed(kind), cruise);
        var spec = _firingTypes.Spec;
        var penetration = AtgmSettings.EffectivePenetration(kind, _firingTypes.Caliber, penSetting);
        vel += fwd * launch;
        *(Vector3*)(e + EVel) = vel;

        var m = new Missile
        {
            Kind = kind,
            Launch = launch,
            Cruise = cruise,
            Accel = gs * AtgmSettings.Gravity,
            MaxFlight = AtgmSettings.MaxFlight(kind, cruise),
            JetSpeed = JetSpeedFor(spec, kind, penetration),
            Id = id,
            Index = index,
            Register = register,
            RegPtr = reg,
            WeaponPtr = _firingWeapon,
            Weapon = wt,
            VehicleKey = wt != null ? Racks.VehicleKeyOf(wt) : 0,
            Types = _firingTypes,
            LaunchTime = Time.time,
            LaunchPos = pos,
            LastPos = pos,
            LastVel = vel,
            Guide = AtgmSettings.Guide(ApfsdsSettings.BlueprintOf(wt))
        };
        FindOwnVehicle(m, wt);
        m.Fx = MissileFx.Create(pos);
        var wired = Kind.IsLauncher(kind) && AtgmSettings.Wire(ApfsdsSettings.BlueprintOf(wt));
        if (wired) m.Wire = Wires.Start(wt, pos, Plugin.AtgmWireLength.Value);
        Missiles.Add(m);
        
        if (Kind.Base(kind) == Kind.Atgm) Fx.PlayLaunch(pos, wt);
        else if (SoundBridge.Active) Fx.PlayTubeFire(wt, pos, wired);
        m.Fx?.StartFlightSound(Kind.IsLauncher(kind) ? Plugin.LauncherSoundVolume.Value : Plugin.AtgmSoundVolume.Value);
        Effects.OnLaunch(register, id, index, kind, _firingTypes, pos, vel, m.MaxFlight);
        var warhead = Kind.WarheadOf(kind) == Kind.WarheadHeat ? $"HEAT {penetration:0} mm"
            : $"{(Kind.IsThermobaric(kind) ? "thermobaric" : "HE")} warhead, blast radius {Mathf.Sqrt(spec.MissileBlast(kind) / 10f):0.#} m";
        Plugin.Log.LogInfo($"{Kind.Names[kind]} launched ({_firingTypes.Caliber} mm, {warhead}, {cruise:0} m/s, {gs:0.#} g" +
                           $"{(m.OwnModels.Count > 0 ? "" : ", own vehicle not found")})");
    }

    

    
    
    
    private static void LaunchRocket(ProjectileRegister register, int id, int index, IntPtr e)
    {
        var kind = FiringKind;
        var weapon = new CannonBehaviour(_firingWeapon);
        var wt = weapon.transform;
        var pos = *(Vector3*)(e + EPos);
        var inherit = *(Vector3*)(e + EVel); 
        var axis = wt != null ? wt.forward : (inherit.sqrMagnitude > 1e-4f ? inherit.normalized : Vector3.forward);
        RocketSettings.ForWeapon(wt, out _, out var speed);
        var dir = Disperse(axis, Mathf.Clamp(Plugin.RocketDispersion.Value, 0f, 5f));
        var launch = Mathf.Max(20f, speed * 0.15f);
        var vel = inherit + dir * launch;
        *(Vector3*)(e + EVel) = vel;
        var burn = Mathf.Clamp(Plugin.RocketBurnTime.Value, 0.2f, 4f);

        var m = new Missile
        {
            Kind = kind,
            Rocket = true,
            Launch = launch,
            Cruise = speed,
            Burn = burn,
            Dir = dir,
            Inherit = inherit,
            MaxFlight = RocketPart.Lifetime,
            JetSpeed = kind == Kind.RocketHeat ? ShellSpec.JetSpeed : 0f,
            Id = id,
            Index = index,
            Register = register,
            RegPtr = register.Pointer,
            WeaponPtr = _firingWeapon,
            Weapon = wt,
            VehicleKey = wt != null ? Racks.VehicleKeyOf(wt) : 0,
            Types = _firingTypes,
            LaunchTime = Time.time,
            LaunchPos = pos,
            LastPos = pos,
            LastVel = vel
        };
        FindOwnVehicle(m, wt);
        m.Fx = MissileFx.Create(pos);
        Missiles.Add(m);
        Fx.PlayRocketLaunch(wt, pos);
        m.Fx?.StartFlightSound(Plugin.RocketSoundVolume.Value);
        Effects.OnLaunch(register, id, index, kind, _firingTypes, pos, vel, RocketPart.Lifetime);
        Plugin.Log.LogInfo($"{Kind.Names[kind]} launched ({_firingTypes.Caliber} mm, {speed:0} m/s after a {burn:0.0} s burn, " +
                           $"{Vector3.Angle(axis, dir):0.00}° off the tube axis{(m.OwnModels.Count > 0 ? "" : ", own vehicle not found")})");
    }

    
    private static Vector3 Disperse(Vector3 axis, float degrees)
    {
        if (degrees <= 0f) return axis;
        var u = UnityEngine.Random.value;
        var ang = Mathf.Min(degrees * 0.8f * Mathf.Sqrt(-2f * Mathf.Log(Mathf.Max(1e-6f, 1f - u))), degrees * 4f);
        var perp = Vector3.Cross(axis, Vector3.up);
        if (perp.sqrMagnitude < 1e-4f) perp = Vector3.Cross(axis, Vector3.right);
        perp.Normalize();
        var tilted = Quaternion.AngleAxis(ang, perp) * axis;
        return (Quaternion.AngleAxis(UnityEngine.Random.value * 360f, axis) * tilted).normalized;
    }

    private static void SteerRocket(Missile m)
    {
        var e = Locate(m);
        if (e == IntPtr.Zero) { End(m); return; }
        var pos = *(Vector3*)(e + EPos);
        var vel = *(Vector3*)(e + EVel);
        m.LastPos = pos;
        m.LastVel = vel;
        
        if (m.JetSpeed > 0f && !m.Boosted && (pos - m.LaunchPos).sqrMagnitude > 4f)
        {
            var sp = vel.magnitude;
            var d = sp > 1f ? vel / sp : m.Dir;
            if (HitAhead(m, pos, d, sp * Time.fixedDeltaTime * 1.5f + 1f, *(int*)(e + ELayer)))
            {
                *(Vector3*)(e + EVel) = d * m.JetSpeed;
                m.Boosted = true;
                if (!m.BurnedOut)
                {
                    m.BurnedOut = true;
                    m.Fx?.StopEmitting();
                }
                if (Plugin.DebugLogging.Value) Plugin.Log.LogInfo($"{Kind.Names[m.Kind]}: warhead triggered {(pos - m.LaunchPos).magnitude:0} m from the launcher");
                return;
            }
        }
        if (m.BurnedOut) return;

        var t = Time.time - m.LaunchTime;
        if (t >= m.Burn)
        {
            m.BurnedOut = true;
            m.Fx?.StopEmitting();
            if (Plugin.DebugLogging.Value)
                Plugin.Log.LogInfo($"{Kind.Names[m.Kind]}: motor burnt out {(pos - m.LaunchPos).magnitude:0} m from the launcher at {vel.magnitude:0} m/s");
            return;
        }
        var dt = Time.fixedDeltaTime;
        var speed = Mathf.Lerp(m.Launch, m.Cruise, t / m.Burn);
        var newVel = m.Inherit + m.Dir * speed - Physics.gravity * dt; 
        *(Vector3*)(e + EVel) = newVel;
        PassOwnVehicle(m, e, pos, newVel, dt);
    }

    
    
    
    private static void ThermobaricEnded(Missile m)
    {
        if (!Kind.IsThermobaric(m.Kind) || m.Types == null) return;
        if (m.Register == null || m.Register.WasCollected) return; 
        try
        {
            var pos = m.LastPos;
            var found = false;
            var history = m.Register.projectileHistory;
            if (history != null && history.TryGetValue(m.Id, out var pe) && pe != null)
            {
                var dm = pe.TryCast<HitDamageModelProjectileEvent>();
                if (dm != null) { pos = dm.Position; found = true; }
                else
                {
                    var gr = pe.TryCast<HitGroundProjectileEvent>();
                    if (gr != null) { pos = gr.Position; found = true; }
                }
            }
            if (!found)
            {
                if (Time.time - m.LaunchTime > m.MaxFlight - 0.5f) return;
                pos = m.LastPos + m.LastVel * Time.fixedDeltaTime * 0.5f;
            }
            Thermobaric.Detonate(pos, m.Types, m.Kind, found ? 1f : 0.5f);
        }
        catch (Exception e) { if (_errors++ < 10) Plugin.Log.LogError("Thermobaric detonation failed: " + e); }
    }

    

    private sealed class Dart
    {
        public int Id, Index;
        public ProjectileRegister Register;
        public IntPtr RegPtr, Def;
        public float Factor, LaunchTime;
        public float JetSpeed;          
        public Vector3 LaunchPos;
    }

    
    
    private static Vector3 TrackHeat(ProjectileRegister register, int id, int index, IntPtr e, Vector3 vel)
    {
        var jet = ShellSpec.JetSpeed;
        try
        {
            var bp = ApfsdsSettings.BlueprintOf(new CannonBehaviour(_firingWeapon).transform);
            var natural = vel.magnitude;
            if (bp != null && natural >= 1f)
            {
                HeatSettings.RecordMeasured(bp, natural);
                HeatSettings.Read(bp, out var penSet, out var speedSet);
                if (speedSet > 0f)
                {
                    vel = vel / natural * Mathf.Clamp(speedSet, HeatSettings.MinSpeed, HeatSettings.MaxSpeed);
                    *(Vector3*)(e + EVel) = vel;
                }
                if (penSet > 0f) jet = HeatSettings.JetSpeed(_firingTypes.Caliber, HeatSettings.EffectivePenetration(_firingTypes.Caliber, penSet));
                if (Plugin.DebugLogging.Value && (penSet > 0f || speedSet > 0f))
                    Plugin.Log.LogInfo($"{_firingTypes.Caliber} mm HEAT: {vel.magnitude:0} m/s, " +
                                       $"{HeatSettings.EffectivePenetration(_firingTypes.Caliber, penSet):0} mm (jet {jet:0} m/s)");
            }
        }
        catch (Exception ex) { if (_errors++ < 10) Plugin.Log.LogError("HEAT settings failed: " + ex); }
        Darts.Add(new Dart
        {
            Id = id, Index = index, Register = register, RegPtr = register.Pointer, Def = *(IntPtr*)(e + EDef),
            Factor = 1f, JetSpeed = jet, LaunchTime = Time.time, LaunchPos = *(Vector3*)(e + EPos)
        });
        return vel;
    }

    private static readonly List<Dart> Darts = new();

    
    
    private static Vector3 ApplyApfsds(ProjectileRegister register, int id, int index, IntPtr e, Vector3 vel)
    {
        try
        {
            var weapon = new CannonBehaviour(_firingWeapon);
            var bp = ApfsdsSettings.BlueprintOf(weapon.transform);
            var natural = vel.magnitude;
            if (bp == null || natural < 1f) return vel;
            ApfsdsSettings.RecordMeasured(bp, natural);
            ApfsdsSettings.Read(bp, out var dartSet, out var speedSet, out var penSet);
            var speed = natural;
            if (speedSet > 0f)
            {
                speed = Mathf.Clamp(speedSet, ApfsdsSettings.MinSpeed, ApfsdsSettings.MaxSpeed);
                vel = vel / natural * speed;
                *(Vector3*)(e + EVel) = vel;
            }
            if (dartSet <= 0f && penSet <= 0f) return vel;

            var cal = _firingTypes.Caliber;
            var s = _firingTypes.Spec;
            var dart = ApfsdsSettings.EffectiveDart(cal, dartSet);
            var wanted = ApfsdsSettings.EffectivePenetration(cal, dart, speed, penSet);
            var game = ShellSpec.Penetration(s.RodDiameter, s.RodMass, speed, s.RodConstant);
            if (game < 1f) return vel;
            var factor = Mathf.Clamp(Mathf.Pow(wanted / game, 1f / 1.43f), 0.05f, 20f);
            if (Mathf.Abs(factor - 1f) > 0.005f)
                Darts.Add(new Dart
                {
                    Id = id, Index = index, Register = register, RegPtr = register.Pointer, Def = *(IntPtr*)(e + EDef),
                    Factor = factor, LaunchTime = Time.time, LaunchPos = *(Vector3*)(e + EPos)
                });
            if (Plugin.DebugLogging.Value)
                Plugin.Log.LogInfo($"{cal} mm APFSDS: {dart:0} mm dart, {speed:0} m/s, {wanted:0} mm (game {game:0} mm, impact factor {factor:0.00})");
        }
        catch (Exception ex) { if (_errors++ < 10) Plugin.Log.LogError("APFSDS settings failed: " + ex); }
        return vel;
    }

    
    private static void StepDarts(IntPtr reg)
    {
        var dt = Time.fixedDeltaTime;
        var now = Time.time;
        for (var i = Darts.Count - 1; i >= 0; i--)
        {
            var d = Darts[i];
            if (d.RegPtr != reg) continue;
            if (d.Register == null || d.Register.WasCollected || now - d.LaunchTime > 30f) { Darts.RemoveAt(i); continue; }
            var index = FindIndex(reg, d.Id, d.Index);
            if (index < 0) { Darts.RemoveAt(i); continue; }
            var e = Elem(reg, index);
            if (*(IntPtr*)(e + EDef) != d.Def) { Darts.RemoveAt(i); continue; }
            d.Index = index;
            var pos = *(Vector3*)(e + EPos);
            var vel = *(Vector3*)(e + EVel);
            var speed = vel.magnitude;
            if (speed < 1f) { Darts.RemoveAt(i); continue; }
            
            if (now - d.LaunchTime < 0.04f || (pos - d.LaunchPos).sqrMagnitude < 25f) continue;
            if (!DartHitAhead(pos, vel / speed, speed * dt * 1.5f + 1f, *(int*)(e + ELayer))) continue;
            *(Vector3*)(e + EVel) = d.JetSpeed > 0f ? vel / speed * d.JetSpeed : vel * d.Factor;
            Darts.RemoveAt(i);
        }
    }

    private static bool DartHitAhead(Vector3 pos, Vector3 dir, float distance, int layerMask)
    {
        if (!_damageRaycastFailed)
        {
            try { return DamageModelRaycast(pos, dir, distance, out var hit, out _) && hit <= distance; }
            catch (Exception e)
            {
                _damageRaycastFailed = true;
                Plugin.Log.LogWarning("Damage model raycast unavailable, using physics: " + e.Message);
            }
        }
        try
        {
            return Physics.Raycast(pos, dir, out var rh, distance, layerMask) && rh.rigidbody != null &&
                   rh.collider.gameObject.name != "Munitions casing";
        }
        catch { return false; }
    }

    
    
    
    internal static bool ScaleImpact(int id, float speedFactor)
    {
        speedFactor = Mathf.Clamp(speedFactor, 0.01f, 1f);
        foreach (var m in Missiles)
        {
            if (m.Id != id || m.Ended) continue;
            if (m.Boosted || (m.Rocket && m.JetSpeed <= 0f)) return false; 

            m.JetSpeed = Mathf.Max(50f, m.JetSpeed * speedFactor);
            return true;
        }
        foreach (var d in Darts)
        {
            if (d.Id != id || d.JetSpeed <= 0f) continue;
            d.JetSpeed = Mathf.Max(50f, d.JetSpeed * speedFactor);
            return true;
        }
        return false;
    }

    
    
    private static float JetSpeedFor(ShellSpec s, int kind, float penetration)
    {
        
        if (Kind.WarheadOf(kind) != Kind.WarheadHeat) return ShellSpec.JetSpeed;
        var constant = Kind.IsLauncher(kind) ? s.LauncherConstant : s.AtgmConstant;
        var basePen = ShellSpec.Penetration(s.JetDiameter, s.JetMass, ShellSpec.JetSpeed, constant);
        if (basePen <= 1f || penetration <= 0f) return ShellSpec.JetSpeed;
        return Mathf.Clamp(ShellSpec.JetSpeed * Mathf.Pow(penetration / basePen, 1f / 1.43f), 150f, 20000f);
    }

    
    private static void FindOwnVehicle(Missile m, Transform weapon)
    {
        try
        {
            var vehicle = weapon != null ? weapon.GetComponentInParent<Sprocket.Vehicles.Vehicle>() : null;
            if (vehicle == null) return;
            var body = vehicle.body;
            m.OwnBody = body != null ? body.Pointer : IntPtr.Zero;
            foreach (var cs in vehicle.GetComponentsInChildren<CompoundStructure>(true))
                if (cs != null) m.OwnModels.Add(cs.Pointer);
            
            if (m.OwnModels.Count == 0 && m.OwnBody != IntPtr.Zero)
            {
                var models = DamageModel.models;
                if (models != null)
                    for (var i = 0; i < models.Count; i++)
                    {
                        var cs = models[i]?.TryCast<CompoundStructure>();
                        var rb = cs != null ? cs.RigidBody : null;
                        if (rb != null && rb.Pointer == m.OwnBody) m.OwnModels.Add(cs.Pointer);
                    }
            }
        }
        catch (Exception e) { if (_errors++ < 10) Plugin.Log.LogWarning("Own vehicle lookup failed: " + e.Message); }
    }

    

    [HarmonyPatch(typeof(ProjectileRegister), nameof(ProjectileRegister.FixedUpdate))]
    private static class FixedUpdatePatch
    {
        private static void Prefix(ProjectileRegister __instance)
        {
            if (Darts.Count > 0)
            {
                try { StepDarts(__instance.Pointer); }
                catch (Exception e) { if (_errors++ < 10) Plugin.Log.LogError("APFSDS impact failed: " + e); }
            }
            if (Missiles.Count == 0) return;
            var reg = __instance.Pointer;
            foreach (var m in Missiles)
            {
                if (m.Ended || m.RegPtr != reg) continue;
                try { Steer(m); }
                catch (Exception e)
                {
                    m.Ended = true;
                    if (_errors++ < 10) Plugin.Log.LogError("ATGM guidance failed: " + e);
                }
            }
        }
    }

    private static void Steer(Missile m)
    {
        if (m.Rocket)
        {
            SteerRocket(m);
            return;
        }
        var e = Locate(m);
        if (e == IntPtr.Zero) { End(m); return; }

        var dt = Time.fixedDeltaTime;
        var t = Time.time - m.LaunchTime;
        var pos = *(Vector3*)(e + EPos);
        var vel = *(Vector3*)(e + EVel);
        m.LastPos = pos;
        m.LastVel = vel;
        var g = Physics.gravity;

        
        if (m.Wire != null)
        {
            Wires.Feed(m.Wire, pos, vel);
            if (m.Wire.Snapped && !m.Lost)
            {
                m.Lost = true;
                Plugin.Log.LogInfo($"ATGM guidance wire ran out ({m.Wire.Laid:0} m) — the missile flies on unguided");
            }
        }

        var maxFlight = m.MaxFlight;
        if (t > maxFlight)
        {
            *(int*)(e + EFlags) |= FlagRelease; 
            End(m);
            Effects.Airburst(pos, m.Types.Caliber);
            if (Plugin.DebugLogging.Value) Plugin.Log.LogInfo("ATGM self-destructed");
            return;
        }

        var cruise = m.Cruise;
        var launch = m.Launch;
        var targetSpeed = t < BoostTime ? Mathf.Lerp(launch, cruise, t / BoostTime)
            : t < maxFlight * 0.85f ? cruise : cruise * 0.9f;
        var speed = vel.magnitude;
        var dir = speed > 1f ? vel / speed : Vector3.forward;
        Vector3 newVel;

        var verdict = 0;
        if (!m.Lost && TryBeam(m, out var o, out var d) && (verdict = Verdict(m, o, d, pos)) != 1)
        {
            
            if (verdict == 2 && !m.Jammed)
            {
                m.Jammed = true;
                m.JamOffset = Quaternion.Euler(UnityEngine.Random.Range(1.5f, 6f) * (UnityEngine.Random.value < 0.75f ? 1f : -0.5f),
                    UnityEngine.Random.Range(-3f, 3f), 0f);
                Plugin.Log.LogInfo("ATGM jammed: the launcher's IR tracker lost the missile");
            }
            if (m.Jammed) d = m.JamOffset * d;
            
            var lag = Mathf.Max(0.01f, Plugin.AtgmLag.Value);
            var a = 1f - Mathf.Exp(-dt / lag);
            if (!m.BeamInit) { m.BeamOrigin = o; m.BeamDir = d; m.BeamInit = true; }
            else
            {
                m.BeamOrigin = Vector3.Lerp(m.BeamOrigin, o, a);
                m.BeamDir = Vector3.Slerp(m.BeamDir, d, a).normalized;
            }

            var r = pos - m.BeamOrigin;
            var along = Vector3.Dot(r, m.BeamDir);
            if (along < -10f || (along > 60f && Vector3.Angle(r, m.BeamDir) > BeamWidth))
            {
                m.Lost = true;
                if (Plugin.DebugLogging.Value) Plugin.Log.LogInfo($"ATGM lost the guidance beam at {along:0} m");
                newVel = dir * targetSpeed;
            }
            else
            {
                var desired = dir;
                if (t >= GatherTime)
                {
                    var look = Mathf.Clamp(Mathf.Max(speed, 50f) * 0.6f, 50f, 400f);
                    var aim = m.BeamOrigin + m.BeamDir * (Mathf.Max(along, 0f) + look);
                    desired = (aim - pos).normalized;
                }
                var maxTurn = Mathf.Max(1f, m.Accel) / Mathf.Max(speed, 50f) * dt;
                var nd = Vector3.RotateTowards(dir, desired, maxTurn, 0f);
                newVel = nd * targetSpeed - g * dt; 
            }
        }
        else
        {
            if (!m.Lost)
            {
                m.Lost = true;
                if (verdict == 1) Plugin.Log.LogInfo("ATGM guidance lost in an aerosol screen");
                else if (Plugin.DebugLogging.Value) Plugin.Log.LogInfo("ATGM lost its launcher");
            }
            newVel = dir * targetSpeed;
        }

        
        if (!m.Boosted && (pos - m.LaunchPos).sqrMagnitude > ArmDistance * ArmDistance)
        {
            var nspeed = newVel.magnitude;
            var ndir = nspeed > 1f ? newVel / nspeed : dir;
            var reach = nspeed * dt * 1.5f + 1f;
            if (HitAhead(m, pos, ndir, reach, *(int*)(e + ELayer)))
            {
                newVel = ndir * m.JetSpeed - g * dt;
                m.Boosted = true;
                if (Plugin.DebugLogging.Value) Plugin.Log.LogInfo($"ATGM warhead triggered {(pos - m.LaunchPos).magnitude:0} m from launch");
            }
        }

        *(Vector3*)(e + EVel) = newVel;
        PassOwnVehicle(m, e, pos, newVel, dt);
    }

    
    
    private static void PassOwnVehicle(Missile m, IntPtr e, Vector3 pos, Vector3 vel, float dt)
    {
        if (m.OwnModels.Count == 0 || _damageRaycastFailed) return;
        var speed = vel.magnitude;
        if (speed < 1f) return;
        var dir = vel / speed;
        var step = speed * dt * 1.2f + 0.5f;
        var start = pos;
        var moved = false;
        try
        {
            for (var i = 0; i < 16; i++)
            {
                if (!DamageModelRaycast(start, dir, step, out var hitDistance, out var model) || hitDistance > step) break;
                if (!m.OwnModels.Contains(model)) break;
                start += dir * (hitDistance + 0.05f);
                moved = true;
            }
        }
        catch { return; }
        if (!moved) return;
        *(Vector3*)(e + EPos) = start;
    }

    private static int Verdict(Missile m, Vector3 origin, Vector3 dir, Vector3 pos)
    {
        var f = _filter;
        if (f == null) return 0;
        try { return f(m.Id, origin, dir, pos, m.Guide); }
        catch (Exception e)
        {
            if (_errors++ < 10) Plugin.Log.LogWarning("Guidance filter failed: " + e.Message);
            return 0;
        }
    }

    private static bool TryBeam(Missile m, out Vector3 origin, out Vector3 dir)
    {
        origin = default;
        dir = default;
        var wt = m.Weapon;
        if (wt == null || wt.WasCollected) return false;

        
        
        if (PlayerTracker.InControl && PlayerTracker.Scoped && m.VehicleKey != 0 && m.VehicleKey == PlayerTracker.VehicleKey)
        {
            var cam = PlayerTracker.Camera;
            if (cam != null && !cam.WasCollected)
            {
                var ct = cam.transform;
                origin = ct.position;
                dir = ct.forward;
                return true;
            }
        }
        origin = wt.position;
        dir = wt.forward;
        return true;
    }

    private static bool _damageRaycastFailed;
    private static IntPtr _raycastMethod;

    
    
    
    internal static bool DamageModelRaycast(Vector3 pos, Vector3 dir, float distance, out float hitDistance, out IntPtr model)
    {
        hitDistance = 0f;
        model = IntPtr.Zero;
        if (_raycastMethod == IntPtr.Zero)
        {
            foreach (var f in typeof(DamageModel).GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static))
                if (f.Name.StartsWith("NativeMethodInfoPtr_Raycast", StringComparison.Ordinal))
                {
                    _raycastMethod = (IntPtr)f.GetValue(null);
                    break;
                }
            if (_raycastMethod == IntPtr.Zero) throw new MissingMethodException("DamageModel.Raycast method info not found");
        }
        var hit = stackalloc byte[64]; 
        for (var i = 0; i < 64; i++) hit[i] = 0;
        var args = stackalloc void*[4];
        args[0] = &pos;
        args[1] = &dir;
        args[2] = &distance;
        args[3] = hit;
        var exc = IntPtr.Zero;
        var ret = Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(_raycastMethod, IntPtr.Zero, args, ref exc);
        if (exc != IntPtr.Zero || ret == IntPtr.Zero) return false;
        var ok = *(byte*)Il2CppInterop.Runtime.IL2CPP.il2cpp_object_unbox(ret) != 0;
        hitDistance = *(float*)hit;
        model = *(IntPtr*)(hit + 0x10); 
        return ok;
    }

    
    private static bool HitAhead(Missile m, Vector3 pos, Vector3 dir, float distance, int layerMask)
    {
        if (!_damageRaycastFailed)
        {
            try
            {
                var start = pos;
                var left = distance;
                for (var i = 0; i < 16 && left > 0f; i++)
                {
                    if (!DamageModelRaycast(start, dir, left, out var hitDistance, out var model) || hitDistance > left) break;
                    if (!m.OwnModels.Contains(model)) return true;
                    start += dir * (hitDistance + 0.05f);
                    left -= hitDistance + 0.05f;
                }
            }
            catch (Exception e)
            {
                _damageRaycastFailed = true;
                Plugin.Log.LogWarning("Damage model raycast unavailable, using physics: " + e.Message);
            }
        }
        try
        {
            if (Physics.Raycast(pos, dir, out var rh, distance, layerMask) && rh.rigidbody != null &&
                (m.OwnBody == IntPtr.Zero || rh.rigidbody.Pointer != m.OwnBody) &&
                rh.collider.gameObject.name != "Munitions casing") return true; 
        }
        catch { }
        return false;
    }

    private static void End(Missile m)
    {
        if (m.Ended) return;
        m.Ended = true;
        m.Fx?.StopEmitting();
        Wires.Detach(m.Wire);
        if (Kind.IsThermobaric(m.Kind)) ThermobaricEnded(m);
    }

    

    public static void UpdateVisuals()
    {
        if (Missiles.Count == 0) return;
        var cam = Controller.ViewCamera();
        var extrapolate = Mathf.Max(0f, Time.time - Time.fixedTime);
        for (var i = Missiles.Count - 1; i >= 0; i--)
        {
            var m = Missiles[i];
            var fx = m.Fx;
            if (!m.Ended)
            {
                var e = Locate(m);
                if (e == IntPtr.Zero) End(m);
                else if (fx != null)
                {
                    var pos = *(Vector3*)(e + EPos);
                    var vel = *(Vector3*)(e + EVel);
                    fx.SetHead(pos + vel * extrapolate, vel);
                }
            }
            if (fx == null || !fx.Update(cam))
            {
                fx?.Destroy();
                m.Fx = null;
                
                if (m.Rocket && !m.Ended) continue;
                Missiles.RemoveAt(i);
            }
        }
    }

    

    private static IntPtr Pool(IntPtr reg) => *(IntPtr*)(reg + PoolOffset);

    private static IntPtr Elem(IntPtr reg, int index) => Pool(reg) + ElemBase + index * ElemSize;

    private static int FindIndex(IntPtr reg, int id, int hint)
    {
        var pool = Pool(reg);
        if (pool == IntPtr.Zero) return -1;
        var len = *(int*)(pool + 0x18);
        if (hint >= 0 && hint < len)
        {
            var e = pool + ElemBase + hint * ElemSize;
            if (*(int*)(e + EId) == id && (*(int*)(e + EFlags) & FlagActive) != 0) return hint;
        }
        for (var i = 0; i < len; i++)
        {
            var e = pool + ElemBase + i * ElemSize;
            if (*(int*)(e + EId) == id && (*(int*)(e + EFlags) & FlagActive) != 0) return i;
        }
        return -1;
    }

    private static IntPtr Locate(Missile m)
    {
        if (m.Register == null || m.Register.WasCollected) return IntPtr.Zero;
        var index = FindIndex(m.RegPtr, m.Id, m.Index);
        if (index < 0) return IntPtr.Zero;
        m.Index = index;
        var e = Elem(m.RegPtr, index);
        
        var def = *(IntPtr*)(e + EDef);
        return def == m.Types.Defs[m.Kind] ? e : IntPtr.Zero;
    }
}
