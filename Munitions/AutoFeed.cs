using System;
using System.Collections.Generic;
using HarmonyLib;
using Sprocket.DamageModelling;
using Sprocket.Vehicles.Cannons;
using Sprocket.Vehicles.Fires;
using Sprocket.Vehicles.Weapons;
using Sprocket.Vehicles.Weapons.Cannons;
using Sprocket.WeaponFramework;
using UnityEngine;

namespace Munitions;




internal static class AutoFeed
{
    internal sealed class Gun
    {
        public Cannon Cannon;
        public CannonBehaviour Weapon;
        public AutoSpec Spec;
        public float Caliber, Rpm, Interval;
        public float NextShot = -1f;
        public int VehicleKey;
        public int ChamberKind = -1;          
        public IntPtr ChamberRack;            
        public bool Empty;                    
    }

    private static readonly Dictionary<IntPtr, Gun> ByWeapon = new();
    private static int _errors;

    public static bool TryGet(IntPtr weapon, out Gun g) => ByWeapon.TryGetValue(weapon, out g);

    public static bool IsAuto(IntPtr weapon) => weapon != IntPtr.Zero && ByWeapon.ContainsKey(weapon);

    [HarmonyPatch(typeof(Cannon), nameof(Cannon.EnableBehaviour))]
    private static class EnablePatch
    {
        private static void Postfix(Cannon __instance)
        {
            try
            {
                var spec = AutoWeaponPart.SpecOf(__instance);
                if (spec == null) return;
                Forget(__instance.Pointer); 
                var weapon = __instance.Behaviour;
                var bp = __instance.Blueprint;
                if (weapon == null || bp == null) return;
                var rpm = AutoWeaponPart.Rpm(spec, bp);
                var g = new Gun
                {
                    Cannon = __instance,
                    Weapon = weapon,
                    Spec = spec,
                    Caliber = AutoWeaponPart.NominalCaliber(spec, bp),
                    Rpm = rpm,
                    Interval = 60f / Mathf.Max(1f, rpm),
                    VehicleKey = Racks.VehicleKeyOf(__instance.transform)
                };
                ByWeapon[weapon.Pointer] = g;
                Plugin.Log.LogInfo($"{spec.DisplayName} {g.Caliber:0.##} mm ready: {rpm:0} rounds/min, {LauncherPart.GroupLabel(weapon.FireGroup)}" +
                                   (LauncherPart.HasOperator(__instance) ? "" : " (no crew member operates it: it cannot be selected)"));
            }
            catch (Exception e) { Plugin.Log.LogError("Auto weapon setup failed: " + e); }
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

    
    public static bool HandleRequest(LoadTask task)
    {
        var target = task.target;
        if (target == null || !ByWeapon.TryGetValue(target.Pointer, out var g)) return false;
        try
        {
            var state = (int)task.state;
            var request = (int)task.currentRequest;
            if (state == (int)LoadState.Loaded && request == (int)LoadRequest.Consumed)
            {
                
                g.ChamberKind = -1;
                g.ChamberRack = IntPtr.Zero;
                task.SetState(LoadState.Unloaded);
            }
            else if (state == (int)LoadState.Loaded && request == (int)LoadRequest.Unused)
            {
                
                ReturnChambered(g);
                task.SetState(LoadState.Unloaded);
                g.NextShot = Mathf.Min(g.NextShot, Time.time);
            }
            task.currentRequest = LoadRequest.None;
        }
        catch (Exception e) { if (_errors++ < 10) Plugin.Log.LogError("Auto weapon request failed: " + e.Message); }
        return true;
    }

    
    
    
    [HarmonyPatch(typeof(WeaponBehaviour), nameof(WeaponBehaviour.Fire))]
    private static class FirePatch
    {
        private static void Postfix(WeaponBehaviour __instance)
        {
            if (!ByWeapon.TryGetValue(__instance.Pointer, out var g)) return;
            var now = Time.time;
            var earliest = now + g.Interval - Mathf.Min(Time.deltaTime, 0.25f * g.Interval);
            g.NextShot = Mathf.Max(g.NextShot + g.Interval, earliest);
        }
    }

    
    public static void Tick()
    {
        if (ByWeapon.Count == 0) return;
        List<IntPtr> dead = null;
        var now = Time.time;
        foreach (var kv in ByWeapon)
        {
            var g = kv.Value;
            if (!Alive(g, kv.Key))
            {
                (dead ??= new List<IntPtr>()).Add(kv.Key);
                continue;
            }
            if (now < g.NextShot) continue;
            try
            {
                var task = g.Weapon.loadTask?.TryCast<LoadTask>();
                if (task == null) continue;
                var state = (int)task.state;
                if (state == (int)LoadState.Loaded)
                {
                    
                    
                    
                    if ((int)task.currentRequest != (int)LoadRequest.Consumed) continue;
                    g.ChamberKind = -1;
                    g.ChamberRack = IntPtr.Zero;
                    task.currentRequest = LoadRequest.None;
                    task.SetState(LoadState.Unloaded);
                }
                Feed(g, task);
            }
            catch (Exception e) { if (_errors++ < 10) Plugin.Log.LogError("Auto weapon feed failed: " + e); }
        }
        if (dead != null) foreach (var p in dead) ByWeapon.Remove(p);
    }

    private static bool Alive(Gun g, IntPtr weapon)
    {
        try
        {
            if (g.Cannon == null || g.Cannon.WasCollected) return false;
            var beh = g.Cannon.Behaviour;
            return beh != null && beh.Pointer == weapon;
        }
        catch { return false; }
    }

    private static bool IsPlayer(Gun g) => PlayerTracker.InControl && g.VehicleKey != 0 && g.VehicleKey == PlayerTracker.VehicleKey;

    private static void Feed(Gun g, LoadTask task)
    {
        var sizeValue = task.Request.SizeID.Value;
        int typeId;
        var racks = Racks.Of(g.VehicleKey, sizeValue);
        racks.RemoveAll(Autoloader.IsCarouselRack);
        if (racks.Count > 0)
        {
            foreach (var r in racks) Racks.Reconcile(r);
            var kind = Choose(g, racks);
            if (kind < 0) { g.Empty = true; return; }
            RackState from = null;
            foreach (var r in racks)
                if (r.Count[kind] > 0 && (from == null || r.Count[kind] > from.Count[kind])) from = r;
            if (from == null || from.Types.TypeIds[kind] == 0) { g.Empty = true; return; }
            from.Count[kind]--;
            var total = from.Total;
            from.Beh.amountStored = (ushort)total;
            from.LastTotal = total;
            Racks.FireStorageChanged(from);
            typeId = from.Types.TypeIds[kind];
            g.ChamberKind = kind;
            g.ChamberRack = from.Ptr;
        }
        else
        {
            
            Racks.AnyRack from = null;
            foreach (var r in Racks.VanillaOf(g.VehicleKey, sizeValue))
                if (r.Beh.amountStored > 0 && (from == null || r.Beh.amountStored > from.Beh.amountStored)) from = r;
            if (from == null || !from.Beh.TakeShell()) { g.Empty = true; return; }
            typeId = from.Beh.StoredTypeID.Value;
            g.ChamberKind = -1;
            g.ChamberRack = from.Ptr;
        }
        g.Empty = false;
        task.currentRequest = LoadRequest.None;
        task.SetState(LoadState.Loaded);
        g.Weapon._LoadedProjectileTypeID_k__BackingField = new ProjectileTypeID(typeId);
        g.Weapon._NeedsReload_k__BackingField = false;
    }

    
    private static int Choose(Gun g, List<RackState> racks)
    {
        if (IsPlayer(g))
            foreach (var r in racks)
                if (r.Count[Controller.Selected] > 0) return Controller.Selected;
        foreach (var k in Kind.AutoOrder)
            foreach (var r in racks)
                if (r.Count[k] > 0) return k;
        return -1;
    }

    private static void ReturnChambered(Gun g)
    {
        if (g.ChamberRack == IntPtr.Zero) return;
        try
        {
            var managed = Racks.Get(g.ChamberRack);
            if (managed != null && g.ChamberKind >= 0)
            {
                managed.Count[g.ChamberKind]++;
                var total = managed.Total;
                managed.Beh.amountStored = (ushort)total;
                managed.LastTotal = total;
                Racks.FireStorageChanged(managed);
            }
            else if (Racks.Every.TryGetValue(g.ChamberRack, out var any) && any.Alive)
                any.Beh.amountStored = (ushort)(any.Beh.amountStored + 1);
        }
        catch { }
        g.ChamberKind = -1;
        g.ChamberRack = IntPtr.Zero;
    }

    
    public static int RoundsLeft(Gun g, int sizeValue)
    {
        var n = 0;
        try
        {
            foreach (var r in Racks.VanillaOf(g.VehicleKey, sizeValue)) n += r.Beh.amountStored;
        }
        catch { }
        return n;
    }

    

    
    
    private static readonly HashSet<IntPtr> FlashFixed = new();
    private const ushort SmallestCaliber = 5;

    [HarmonyPatch(typeof(MuzzleFlashEffect), nameof(MuzzleFlashEffect.Setup))]
    private static class MuzzleFlashPatch
    {
        private static unsafe void Prefix(MuzzleFlashEffect __instance)
        {
            try
            {
                var self = __instance.Pointer;
                
                if (Hidden.Remove(self))
                {
                    var v = __instance.visualEffect;
                    if (v != null && !v.WasCollected) v.enabled = true;
                }
                if (!FlashFixed.Add(self)) return;
                if (FlashFixed.Count > 4096) FlashFixed.Clear();
                var arr = *(IntPtr*)(self + 0x78);           
                if (arr == IntPtr.Zero) return;
                var len = *(int*)(arr + 0x18);
                if (len <= 0 || len > 64) return;
                var best = -1;
                ushort min = ushort.MaxValue;
                for (var i = 0; i < len; i++)
                {
                    var minCal = *(ushort*)(arr + 0x20 + i * 0x38 + 2);   
                    if (minCal < min) { min = minCal; best = i; }
                }
                if (best >= 0 && min > SmallestCaliber)
                {
                    *(ushort*)(arr + 0x20 + best * 0x38 + 2) = SmallestCaliber;
                    if (Plugin.DebugLogging.Value) Plugin.Log.LogInfo($"Muzzle flash: smallest set now starts at {SmallestCaliber} mm (was {min} mm)");
                }
            }
            catch (Exception e) { if (_errors++ < 10) Plugin.Log.LogWarning("Muzzle flash fix failed: " + e.Message); }
        }

        
        
        
        
        private static void Postfix(MuzzleFlashEffect __instance, CannonMuzzleFlashEffectInfo info, Transform parent, Vector3 position)
        {
            try
            {
                if (parent == null) return;
                
                if (CombatFx.FlashesActive) return;
                var type = Api.AutoWeaponType(parent);
                if (type == 0) return;
                var vfx = __instance.visualEffect;
                if (vfx == null) return;
                var now = Time.time;
                var key = parent.Pointer;
                var burst = LastFlash.TryGetValue(key, out var last) && now - last < 0.6f;
                LastFlash[key] = now;
                if (LastFlash.Count > 256) { LastFlash.Clear(); LastShown.Clear(); }
                if (burst && LastShown.TryGetValue(key, out var shown) && now - shown < BurstEffectInterval && now >= shown)
                {
                    vfx.enabled = false;
                    if (Hidden.Count > 2048) Hidden.Clear();
                    Hidden.Add(__instance.Pointer);
                    Effects.AutoMuzzleFlash(position, Api.AutoWeaponCaliber(parent));
                    return;
                }
                LastShown[key] = now;
                var factor = Mathf.Clamp(Plugin.AutoSmoke.Value, 0f, 1f) * (burst ? 0.35f : 1f);
                if (_volumeId == 0) _volumeId = Shader.PropertyToID("Propellant Volume");
                if (_detailId == 0) _detailId = Shader.PropertyToID("High Detail");
                if (vfx.HasFloat(_volumeId)) vfx.SetFloat(_volumeId, info.PropellantVolume * factor);
                if (vfx.HasBool(_detailId)) vfx.SetBool(_detailId, false);
            }
            catch (Exception e) { if (_errors++ < 10) Plugin.Log.LogWarning("Auto weapon smoke reduction failed: " + e.Message); }
        }
    }

    private static readonly Dictionary<IntPtr, float> LastFlash = new();
    private static readonly Dictionary<IntPtr, float> LastShown = new();
    private static readonly HashSet<IntPtr> Hidden = new();
    private const float BurstEffectInterval = 1f;  
    private static int _volumeId, _detailId;
}
