using System;
using System.Collections.Generic;
using HarmonyLib;
using Sprocket.DamageModelling;
using Sprocket.Vehicles.Cannons;
using Sprocket.Vehicles.Cannons.FX;
using Sprocket.Vehicles.Weapons;
using Sprocket.Vehicles.Weapons.Cannons;
using Sprocket.WeaponFramework;
using UnityEngine;

namespace Munitions;






internal static class Launchers
{
    private sealed class State
    {
        public Cannon Cannon;
        public CannonBehaviour Weapon;
        public int Caliber;
        public SizeTypes Types;
        public int Kind = Munitions.Kind.Launcher;   
        public bool Rocket;
        public int VehicleKey;
        public int FireGroup;
        public float TubeLength;
        public bool Fired;
        public float LaunchAt = -1f;
        public bool Ready => !Fired && LaunchAt < 0f;
        public string Label => Rocket
            ? $"Rocket launcher {Caliber} mm ({(Kind == Munitions.Kind.RocketTb ? "thermobaric" : Kind == Munitions.Kind.RocketHeat ? "HEAT" : "HE")})"
            : $"ATGM launcher {Caliber} mm" + (Kind == Munitions.Kind.LauncherHe ? " (HE)" : Kind == Munitions.Kind.LauncherTb ? " (thermobaric)" : "");
    }

    
    private sealed class Trigger
    {
        public float LastCall = -10f;
        public bool Latched;
        public float NextRocket;   
    }

    public readonly struct GroupInfo
    {
        public readonly int Group, Caliber, Ready, Total;
        public readonly bool Launching, Crewed;
        public readonly bool Rocket, Thermobaric;   

        public GroupInfo(int group, int caliber, int ready, int total, bool launching, bool crewed, bool rocket = false, bool thermobaric = false)
        {
            Group = group;
            Caliber = caliber;
            Ready = ready;
            Total = total;
            Launching = launching;
            Crewed = crewed;
            Rocket = rocket;
            Thermobaric = thermobaric;
        }

        public string Key => LauncherPart.GroupLabel(Group);
        public string StateText => Launching ? $"LAUNCH {Ready}/{Total}" : Ready > 0 ? $"READY {Ready}/{Total}" : "EMPTY";
        public string StateColor => Launching ? "#FFD24A" : Ready > 0 ? "#9CE69C" : "#8C8C8C";
    }

    private static readonly Dictionary<IntPtr, State> ByWeapon = new();
    private static readonly Dictionary<int, Trigger> Triggers = new();
    private static IntPtr _releasing;
    private const float ReleaseGap = 0.2f; 

    
    public static float FiringTubeLength;

    public static bool IsLauncher(IntPtr weapon) => weapon != IntPtr.Zero && ByWeapon.ContainsKey(weapon);

    
    public static bool IsRocket(IntPtr weapon) => weapon != IntPtr.Zero && ByWeapon.TryGetValue(weapon, out var s) && s.Rocket;

    
    public static List<GroupInfo> Groups(int vehicleKey)
    {
        var map = new SortedDictionary<int, (int Cal, int Ready, int Total, bool Launching, bool Crewed, int Rockets, int Tb)>();
        if (vehicleKey == 0) return new List<GroupInfo>();
        foreach (var s in ByWeapon.Values)
        {
            if (s.VehicleKey != vehicleKey) continue;
            map.TryGetValue(s.FireGroup, out var g);
            if (g.Total == 0) g.Cal = s.Caliber;
            g.Total++;
            if (s.Ready) g.Ready++;
            if (s.LaunchAt >= 0f) g.Launching = true;
            if (LauncherPart.HasOperator(s.Cannon)) g.Crewed = true;
            if (s.Rocket) g.Rockets++;
            if (Kind.IsThermobaric(s.Kind)) g.Tb++;
            map[s.FireGroup] = g;
        }
        var list = new List<GroupInfo>();
        foreach (var kv in map)
        {
            var g = kv.Value;
            list.Add(new GroupInfo(kv.Key, g.Cal, g.Ready, g.Total, g.Launching, g.Crewed, g.Rockets > 0 && g.Rockets * 2 >= g.Total, g.Tb * 2 > g.Rockets));
        }
        
        list.Sort((a, b) => (a.Group & -a.Group).CompareTo(b.Group & -b.Group));
        return list;
    }

    public static bool GroupOf(IntPtr weapon, out int group)
    {
        group = 0;
        if (!ByWeapon.TryGetValue(weapon, out var s)) return false;
        group = s.FireGroup;
        return true;
    }

    [HarmonyPatch(typeof(Cannon), nameof(Cannon.EnableBehaviour))]
    private static class EnablePatch
    {
        private static void Postfix(Cannon __instance)
        {
            try
            {
                if (!LauncherPart.IsLauncher(__instance)) return;
                Forget(__instance.Pointer); 
                var weapon = __instance.Behaviour;
                var bp = __instance.Blueprint;
                if (weapon == null || bp == null) return;
                var rocket = RocketPart.IsRocketLauncher(__instance);
                State s;
                if (rocket)
                {
                    var caliber = Math.Clamp((int)bp.Caliber, RocketPart.MinCaliber, RocketPart.MaxCaliber);
                    RocketSettings.Read(bp, out var warhead, out _);
                    s = new State
                    {
                        Cannon = __instance,
                        Weapon = weapon,
                        Caliber = caliber,
                        Rocket = true,
                        Kind = RocketSettings.KindOf(warhead),
                        Types = TypeRegistry.EnsureRocket(caliber),
                        VehicleKey = Racks.VehicleKeyOf(__instance.transform),
                        TubeLength = RocketPart.TubeLengthMm(caliber, RocketPart.TubeCaliberOf(bp)) / 1000f
                    };
                }
                else
                {
                    var caliber = Math.Clamp((int)bp.Caliber, LauncherPart.MinCaliber, LauncherPart.MaxCaliber);
                    s = new State
                    {
                        Cannon = __instance,
                        Weapon = weapon,
                        Caliber = caliber,
                        Kind = Kind.WithWarhead(Kind.Launcher, AtgmSettings.Warhead(bp)),
                        Types = TypeRegistry.EnsureLauncher(caliber),
                        VehicleKey = Racks.VehicleKeyOf(__instance.transform),
                        TubeLength = LauncherPart.TubeLengthMm(caliber, LauncherPart.TubeCaliberOf(bp)) / 1000f
                    };
                }
                
                var groups = LauncherPart.ResolveFireGroups(__instance);
                if (groups.TryGetValue(__instance.Pointer, out var group) && group != 0 && group != weapon.FireGroup)
                {
                    Plugin.Log.LogInfo($"{s.Label}: fire group {LauncherPart.GroupLabel(weapon.FireGroup)} is taken, using {LauncherPart.GroupLabel(group)}");
                    weapon.FireGroup = group;
                }
                s.FireGroup = weapon.FireGroup;
                ByWeapon[weapon.Pointer] = s;
                KeepLoaded(s);
                Plugin.Log.LogInfo($"{s.Label} ready on {LauncherPart.GroupLabel(s.FireGroup)}" +
                                   (s.Types == null ? " (WARNING: its projectile type could not be registered)" : "") +
                                   (LauncherPart.HasOperator(__instance) ? "" : " (no crew member operates it: it cannot be selected)"));
            }
            catch (Exception e) { Plugin.Log.LogError("Launcher setup failed: " + e); }
        }
    }

    [HarmonyPatch(typeof(Cannon), nameof(Cannon.DisableBehaviour))]
    private static class DisablePatch
    {
        private static void Postfix(Cannon __instance)
        {
            try { Forget(__instance.Pointer); }
            catch { }
        }
    }

    private static void Forget(IntPtr cannon)
    {
        List<IntPtr> old = null;
        foreach (var kv in ByWeapon)
            if (kv.Value.Cannon == null || kv.Value.Cannon.WasCollected || kv.Value.Cannon.Pointer == cannon)
                (old ??= new List<IntPtr>()).Add(kv.Key);
        if (old != null) foreach (var p in old) ByWeapon.Remove(p);
    }

    
    private static void KeepLoaded(State s)
    {
        if (s.Fired || s.LaunchAt >= 0f || s.Weapon == null) return;
        var task = s.Weapon.loadTask?.TryCast<LoadTask>();
        if (task == null) return;
        s.Types ??= s.Rocket ? TypeRegistry.EnsureRocket(s.Caliber) : TypeRegistry.EnsureLauncher(s.Caliber);
        if (s.Types == null || s.Types.TypeIds[s.Kind] == 0) return;
        var type = s.Types.TypeIds[s.Kind];
        if ((int)task.state == (int)LoadState.Loaded && s.Weapon._LoadedProjectileTypeID_k__BackingField.Value == type) return;
        task.currentRequest = LoadRequest.None;
        task.SetState(LoadState.Loaded);
        s.Weapon._LoadedProjectileTypeID_k__BackingField = new ProjectileTypeID(type);
        s.Weapon._NeedsReload_k__BackingField = false;
    }

    
    public static bool HandleRequest(LoadTask task)
    {
        var target = task.target;
        if (target == null || !ByWeapon.TryGetValue(target.Pointer, out var s)) return false;
        try
        {
            var state = (int)task.state;
            var request = (int)task.currentRequest;
            if (state == (int)LoadState.Loaded && request == (int)LoadRequest.Consumed)
            {
                
                s.Fired = true;
                task.SetState(LoadState.Unloaded);
            }
            else KeepLoaded(s);
            task.currentRequest = LoadRequest.None;
        }
        catch (Exception e) { Plugin.Log.LogError(s.Label + " load failed: " + e.Message); }
        return true;
    }

    private static Trigger TriggerOf(int vehicleKey)
    {
        if (!Triggers.TryGetValue(vehicleKey, out var t)) Triggers[vehicleKey] = t = new Trigger();
        return t;
    }

    
    [HarmonyPatch(typeof(WeaponBehaviour), nameof(WeaponBehaviour.Fire))]
    private static class FirePatch
    {
        private static bool Prefix(WeaponBehaviour __instance)
        {
            try
            {
                var ptr = __instance.Pointer;
                if (!ByWeapon.TryGetValue(ptr, out var s)) return true;
                if (ptr == _releasing) return true;

                
                var t = TriggerOf(s.VehicleKey);
                var now = Time.time;
                if (now - t.LastCall > (IsPlayer(s) ? 1f : ReleaseGap)) t.Latched = false;
                t.LastCall = now;

                if (!s.Ready) return false;
                if (__instance.LoadState != LoadState.Loaded) return false;

                
                if (s.Rocket)
                {
                    if (now < t.NextRocket) return false;
                    t.NextRocket = now + Mathf.Clamp(Plugin.RocketRipple.Value, 0.05f, 3f);
                    s.LaunchAt = now;
                    return false;
                }

                
                if (t.Latched) return false;
                t.Latched = true;

                s.LaunchAt = now + Mathf.Clamp(Plugin.LauncherDelay.Value, 0f, 5f);
                Fx.PlayKornet(s.Cannon != null ? s.Cannon.transform : null, __instance.ShellSpawnPosition,
                    s.Cannon != null && AtgmSettings.Wire(s.Cannon.Blueprint));
                Plugin.Log.LogInfo($"ATGM launcher {s.Caliber} mm: launch sequence");
                return false;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Launcher trigger failed: " + e.Message);
                return true;
            }
        }
    }

    private static bool IsPlayer(State s) => PlayerTracker.InControl && s.VehicleKey == PlayerTracker.VehicleKey;

    
    [HarmonyPatch(typeof(CannonEffect), nameof(CannonEffect.OnFire))]
    private static class ShellEjectPatch
    {
        private static bool Prefix(CannonEffect __instance)
        {
            try
            {
                var b = __instance.behaviour;
                return b == null || !ByWeapon.ContainsKey(b.Pointer);
            }
            catch { return true; }
        }
    }

    
    public static void Tick()
    {
        
        if (PlayerTracker.InControl && !PlayerTracker.Firing && Triggers.TryGetValue(PlayerTracker.VehicleKey, out var pt)) pt.Latched = false;

        if (ByWeapon.Count == 0) return;
        List<IntPtr> dead = null;
        foreach (var kv in new List<KeyValuePair<IntPtr, State>>(ByWeapon))
        {
            var s = kv.Value;
            if (!Alive(s, kv.Key))
            {
                (dead ??= new List<IntPtr>()).Add(kv.Key);
                continue;
            }
            if (s.LaunchAt < 0f)
            {
                try { KeepLoaded(s); }
                catch (Exception e) { if (Plugin.DebugLogging.Value) Plugin.Log.LogWarning("ATGM launcher preload failed: " + e.Message); }
                continue;
            }
            if (Time.time < s.LaunchAt) continue;
            s.LaunchAt = -1f;
            
            if (!s.Weapon._Functioning_k__BackingField || s.Weapon.LoadState != LoadState.Loaded)
            {
                Plugin.Log.LogInfo($"{s.Label}: launch aborted");
                continue;
            }
            _releasing = kv.Key;
            FiringTubeLength = s.TubeLength;
            try
            {
                s.Weapon.Fire();
                s.Fired = true;
            }
            catch (Exception e) { Plugin.Log.LogError($"{s.Label}: launch failed: " + e.Message); }
            finally
            {
                _releasing = IntPtr.Zero;
                FiringTubeLength = 0f;
            }
            
            try
            {
                var task = s.Weapon.loadTask?.TryCast<LoadTask>();
                if (s.Fired && task != null && (int)task.state == (int)LoadState.Loaded) task.SetState(LoadState.Unloaded);
            }
            catch { }
        }
        if (dead != null) foreach (var p in dead) ByWeapon.Remove(p);
    }

    
    private static bool Alive(State s, IntPtr weapon)
    {
        try
        {
            if (s.Cannon == null || s.Cannon.WasCollected) return false;
            var beh = s.Cannon.Behaviour;
            return beh != null && beh.Pointer == weapon;
        }
        catch { return false; }
    }
}
