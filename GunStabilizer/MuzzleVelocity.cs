using System;
using System.Collections.Generic;
using HarmonyLib;
using Sprocket.DamageModelling;
using Sprocket.ProjectilePhysicsSystems;
using Sprocket.Vehicles.Cannons;
using Sprocket.Vehicles.Weapons.Cannons;
using UnityEngine;

namespace GunStabilizer;








internal static unsafe class MuzzleVelocity
{
    private const int PoolOffset = 0x38, NextIdOffset = 0x44, ElemSize = 0x50, ElemBase = 0x20;
    private const int EDef = 0x08, EId = 0x18, EVel = 0x34, EFlags = 0x48;

    public enum Source { Measured, Computed, Game, Default }

    private static readonly Dictionary<(IntPtr Weapon, IntPtr Def), float> Measured = new();
    private static IntPtr _firing;
    private static int _firingType;
    private static int _errors, _computeErrors;

    
    
    
    
    private static Dictionary<string, float> _saved;
    private static string SavePath => System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "sprocket.gunstabilizer.muzzle.txt");

    private static string Signature(CannonBehaviour weapon, ProjectileDefinition def)
    {
        try
        {
            var l = weapon.projectileLaunchInfo;
            return $"{l.BarrelLength * 1000f:0}|{l.MaxPressure}|{def.Diameter * 1000f:0}|{def.Name}";
        }
        catch { return null; }
    }

    private static Dictionary<string, float> Saved()
    {
        if (_saved != null) return _saved;
        _saved = new Dictionary<string, float>();
        try
        {
            if (System.IO.File.Exists(SavePath))
                foreach (var line in System.IO.File.ReadAllLines(SavePath))
                {
                    var tab = line.LastIndexOf('\t');
                    if (tab <= 0 || line.StartsWith("#")) continue;
                    if (float.TryParse(line.Substring(tab + 1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v))
                        _saved[line.Substring(0, tab)] = v;
                }
        }
        catch (Exception e) { Plugin.Log.LogWarning("Fire control: saved muzzle velocities not read: " + e.Message); }
        return _saved;
    }

    private static void Save()
    {
        try
        {
            var lines = new List<string> { "# Gun Stabilizer fire control: measured muzzle velocities (barrel mm|pressure|shell mm|shell name<TAB>m/s)" };
            foreach (var kv in _saved) lines.Add(kv.Key + "\t" + kv.Value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
            System.IO.File.WriteAllLines(SavePath, lines);
        }
        catch (Exception e) { Plugin.Log.LogWarning("Fire control: muzzle velocities not saved: " + e.Message); }
    }

    public static float For(CannonBehaviour weapon, Cannon cannon, ProjectileDefinition def, int typeId, out Source source)
    {
        source = Source.Measured;
        if (weapon != null && def != null && Measured.TryGetValue((weapon.Pointer, def.Pointer), out var v)) return v;
        var sig = weapon != null && def != null ? Signature(weapon, def) : null;
        if (sig != null && Saved().TryGetValue(sig, out v)) return v;
        var computed = Compute(weapon, def);
        if (computed > 20f)
        {
            source = Source.Computed;
            return MunitionsBridge.LaunchSpeed(weapon.Pointer, typeId, computed);
        }
        source = Source.Game;
        try
        {
            var game = cannon != null ? cannon.muzzleVelocity : 0f;
            if (game > 20f && !float.IsNaN(game)) return game;
        }
        catch { }
        source = Source.Default;
        return 850f;
    }

    
    private static float Compute(CannonBehaviour weapon, ProjectileDefinition def)
    {
        if (weapon == null || def == null) return 0f;
        try
        {
            var launcher = weapon.projectileLaunchInfo;
            var propellant = -1f;
            var functions = def.LaunchFunctions;
            if (functions != null)
                foreach (var f in functions)
                {
                    var p = f != null ? f.TryCast<DetonationPropellantProjectileFunction>() : null;
                    if (p != null) { propellant = p.PropellantLength; break; }
                }
            if (propellant <= 0f || launcher.BarrelLength <= 0f) return 0f;
            var info = ShellProperties.CalculateFireInfo(launcher.BarrelLength * 1000f, def.Diameter * 1000f, def.Length * 1000f, propellant, launcher.MaxPressure);
            var speed = info.MuzzleVelocity;
            return float.IsNaN(speed) || speed < 20f || speed > 5000f ? 0f : speed;
        }
        catch (Exception e)
        {
            if (_computeErrors++ < 3) Plugin.Log.LogWarning("Fire control: launch speed formula failed: " + e.Message);
            return 0f;
        }
    }

    public static void Forget() => Measured.Clear();

    [HarmonyPatch(typeof(CannonBehaviour), nameof(CannonBehaviour.FireInternal))]
    private static class FirePatch
    {
        private static void Prefix(CannonBehaviour __instance)
        {
            _firing = IntPtr.Zero;
            try
            {
                var key = ScopeCarry.PlayerVehicleKey;
                if (key != 0 && Registry.VehicleKeyOf(__instance.transform) == key)
                {
                    _firing = __instance.Pointer;
                    _firingType = __instance.LoadedProjectileTypeID.Value;
                }
            }
            catch { }
        }

        private static void Postfix() => _firing = IntPtr.Zero;
    }

    
    [HarmonyPatch(typeof(ProjectileRegister), nameof(ProjectileRegister.Launch))]
    [HarmonyPriority(Priority.Last)]
    private static class LaunchPatch
    {
        private static void Postfix(ProjectileRegister __instance)
        {
            if (_firing == IntPtr.Zero) return;
            try
            {
                var reg = __instance.Pointer;
                var next = *(int*)(reg + NextIdOffset);
                var id = next == 0 ? 0x7FFFFFFE : next - 1;
                var pool = *(IntPtr*)(reg + PoolOffset);
                if (pool == IntPtr.Zero) return;
                var len = *(int*)(pool + 0x18);
                for (var i = len - 1; i >= 0; i--)
                {
                    var e = pool + ElemBase + i * ElemSize;
                    if (*(int*)(e + EId) != id || (*(int*)(e + EFlags) & 1) == 0) continue;
                    var def = *(IntPtr*)(e + EDef);
                    var vel = *(Vector3*)(e + EVel);
                    
                    try { vel -= new CannonBehaviour(_firing).InheritedVelocity; } catch { }
                    var speed = vel.magnitude;
                    if (def == IntPtr.Zero || speed < 20f || float.IsNaN(speed)) return;
                    var key = (_firing, def);
                    var had = Measured.TryGetValue(key, out var old);
                    Measured[key] = speed;
                    var weapon = new CannonBehaviour(_firing);
                    var launched = new ProjectileDefinition(def);
                    ProjectileDefinition regDef = null;
                    try { regDef = BallisticComputer.Definition(_firingType); } catch { }
                    
                    var changed = false;
                    foreach (var d in new[] { launched, regDef })
                    {
                        var sig = d != null ? Signature(weapon, d) : null;
                        if (sig == null || (Saved().TryGetValue(sig, out var stored) && Mathf.Abs(stored - speed) <= 5f)) continue;
                        _saved[sig] = speed;
                        changed = true;
                    }
                    if (changed) Save();
                    if (!had || Mathf.Abs(old - speed) > 5f)
                    {
                        
                        
                        var formula = 0f;
                        var registered = 0f;
                        var same = true;
                        try { formula = Compute(weapon, launched); } catch { }
                        try
                        {
                            same = regDef == null || regDef.Pointer == def;
                            if (!same) registered = Compute(weapon, regDef);
                        }
                        catch { }
                        Plugin.Log.LogInfo($"Fire control: muzzle velocity measured {speed:0} m/s" +
                                           (formula > 0f ? $" (game launch formula {formula:0} m/s" : " (") +
                                           (same ? ")" : $"; the registered type {_firingType} has a different definition: {registered:0} m/s)"));
                    }
                    return;
                }
            }
            catch (Exception ex) { if (_errors++ < 5) Plugin.Log.LogWarning("Muzzle velocity measurement failed: " + ex.Message); }
        }
    }
}
