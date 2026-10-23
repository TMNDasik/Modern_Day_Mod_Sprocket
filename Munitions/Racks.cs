using System;
using System.Collections.Generic;
using HarmonyLib;
using Sprocket.DamageModelling;
using Sprocket.Vehicles;
using Sprocket.Vehicles.AmmunitionStorage;
using Sprocket.Vehicles.Weapons;
using Sprocket.WeaponFramework;
using UnityEngine;

namespace Munitions;




internal sealed class RackState
{
    public IntPtr Ptr;
    public AmmoRackBehaviour Beh;
    public AmmoRack Rack;
    public int VehicleKey;
    public int SizeValue;
    public SizeTypes Types;
    public int VanillaType;
    public int LastTotal;
    public readonly int[] Count = new int[Kind.Count];
    public readonly int[] Design = new int[Kind.Count];

    public int Total => Kind.Sum(Count);

    public bool Alive
    {
        get
        {
            try { return Rack != null && !Rack.WasCollected && Rack.behaviour != null && Rack.behaviour.Pointer == Ptr; }
            catch { return false; }
        }
    }
}

internal static unsafe class Racks
{
    public static readonly Dictionary<IntPtr, RackState> All = new();
    private static readonly System.Random Rng = new();

    
    private static readonly Dictionary<IntPtr, (int Kind, int TypeId)> Held = new();

    public static RackState Get(IntPtr behaviour) => All.TryGetValue(behaviour, out var s) ? s : null;

    public static int VehicleKeyOf(Transform t)
    {
        if (t == null) return 0;
        var v = t.GetComponentInParent<Vehicle>();
        return v != null ? v.gameObject.GetInstanceID() : t.root.gameObject.GetInstanceID();
    }

    public static List<RackState> Of(int vehicleKey, int sizeValue)
    {
        var list = new List<RackState>();
        foreach (var s in All.Values)
            if (s.VehicleKey == vehicleKey && (sizeValue == 0 || s.SizeValue == sizeValue)) list.Add(s);
        return list;
    }

    public static bool HeldKind(IntPtr task, out int kind)
    {
        if (Held.TryGetValue(task, out var h)) { kind = h.Kind; return true; }
        kind = -1;
        return false;
    }

    
    public static void Reconcile(RackState s)
    {
        if (Guard.InRequest) return;
        int total;
        try { total = s.Beh.amountStored; }
        catch { return; }
        if (total == s.LastTotal) return;
        if (total < s.LastTotal) Remove(s, s.LastTotal - total);
        else Add(s, total - s.LastTotal);
        s.LastTotal = total;
    }

    private static void Remove(RackState s, int n)
    {
        for (; n > 0 && s.Total > 0; n--)
        {
            var r = Rng.Next(s.Total);
            for (var k = 0; k < Kind.Count; k++)
            {
                if (r < s.Count[k]) { s.Count[k]--; break; }
                r -= s.Count[k];
            }
        }
    }

    
    private static void Add(RackState s, int n)
    {
        for (; n > 0; n--)
        {
            var best = Kind.Apfsds;
            var bestDeficit = int.MinValue;
            for (var k = 0; k < Kind.Count; k++)
            {
                if (k == Kind.Atgm && !s.Types.AtgmAllowed) continue;
                var deficit = s.Design[k] - s.Count[k];
                if (deficit > bestDeficit) { bestDeficit = deficit; best = k; }
            }
            s.Count[best]++;
        }
    }

    
    public static string Describe(int[] counts)
    {
        var parts = new List<string>();
        foreach (var k in Kind.Order)
            if (k < counts.Length && counts[k] > 0) parts.Add($"{Kind.Names[k]} {counts[k]}");
        return parts.Count == 0 ? "empty" : string.Join(", ", parts);
    }

    public static void Cleanup()
    {
        var dead = new List<IntPtr>();
        foreach (var kv in All) if (!kv.Value.Alive) dead.Add(kv.Key);
        foreach (var p in dead) All.Remove(p);
        var deadAny = new List<IntPtr>();
        foreach (var kv in Every) if (!kv.Value.Alive) deadAny.Add(kv.Key);
        foreach (var p in deadAny) Every.Remove(p);
        if (Held.Count > 512) Held.Clear();
    }

    public static void FireStorageChanged(RackState s)
    {
        try
        {
            var handler = s.Beh.StorageValueChanged;
            handler?.Invoke(s.Beh.Cast<Il2CppSystem.Object>(), Il2CppSystem.EventArgs.Empty);
        }
        catch (Exception e)
        {
            if (Plugin.DebugLogging.Value) Plugin.Log.LogWarning("Rack display refresh failed: " + e.Message);
        }
    }

    

    [HarmonyPatch(typeof(AmmoRack), nameof(AmmoRack.EnableBehaviour))]
    private static class EnablePatch
    {
        private static void Postfix(AmmoRack __instance)
        {
            try { Setup(__instance); }
            catch (Exception e) { Plugin.Log.LogError("Ammo rack setup failed: " + e); }
        }
    }

    
    internal sealed class AnyRack
    {
        public AmmoRack Rack;
        public AmmoRackBehaviour Beh;
        public IntPtr Ptr;
        public int VehicleKey, SizeValue;

        public bool Alive
        {
            get
            {
                try { return Rack != null && !Rack.WasCollected && Rack.behaviour != null && Rack.behaviour.Pointer == Ptr; }
                catch { return false; }
            }
        }
    }

    public static readonly Dictionary<IntPtr, AnyRack> Every = new();

    
    public static List<AnyRack> VanillaOf(int vehicleKey, int sizeValue)
    {
        var list = new List<AnyRack>();
        foreach (var r in Every.Values)
            if (r.VehicleKey == vehicleKey && r.SizeValue == sizeValue && !All.ContainsKey(r.Ptr) && r.Alive) list.Add(r);
        return list;
    }

    private static void Setup(AmmoRack rack)
    {
        var beh = rack.behaviour;
        if (beh == null) return;
        var sizeValue = beh.ShellSizeID.Value;
        Every[beh.Pointer] = new AnyRack { Rack = rack, Beh = beh, Ptr = beh.Pointer, VehicleKey = VehicleKeyOf(rack.transform), SizeValue = sizeValue };
        var caliber = (sizeValue >> 16) & 0xFFFF;
        var bp = rack.Blueprint;
        var design = Design.Read(bp?.Description, beh.Capacity, caliber);
        if (design == null) { All.Remove(beh.Pointer); return; }

        var types = TypeRegistry.Ensure(sizeValue);
        if (types == null) { All.Remove(beh.Pointer); return; }
        for (var k = 0; k < Kind.Count; k++)
            if (design[k] > 0 && types.TypeIds[k] == 0)
            {
                
                design[Kind.Apfsds] += design[k];
                design[k] = 0;
            }
        if (types.TypeIds[Kind.Apfsds] == 0) { All.Remove(beh.Pointer); return; }

        var s = new RackState
        {
            Ptr = beh.Pointer,
            Beh = beh,
            Rack = rack,
            VehicleKey = VehicleKeyOf(rack.transform),
            SizeValue = sizeValue,
            Types = types,
            VanillaType = beh.StoredTypeID.Value
        };
        for (var k = 0; k < Kind.Count; k++) s.Design[k] = s.Count[k] = design[k];
        var total = s.Total;
        beh.Capacity = (ushort)total;
        beh.amountStored = (ushort)total;
        s.LastTotal = total;
        All[s.Ptr] = s;
        FireStorageChanged(s);
        
        if (Autoloader.Enabled && CarouselSpec.TryRead(bp?.Description, out var spec)) Autoloader.Setup(s, spec);
        if (Plugin.DebugLogging.Value)
            Plugin.Log.LogInfo($"Rack {caliber} mm: {Describe(s.Count)}");
    }

    

    
    private sealed class RequestContext
    {
        public IntPtr Task;
        public int Frame = Time.frameCount;
        public int Chosen = -1;
        public int ReturnKind = -1;
        public List<RackState> Racks;
        public List<RackState> Carousels;   
        public int[] SavedTypes;
        public int SavedRequestType;
    }

    private static RequestContext _ctx;
    private static (IntPtr Task, int Kind) _returning = (IntPtr.Zero, -1);

    internal static class Guard
    {
        public static bool InRequest => _ctx != null;
    }

    
    private static int Choose(int vehicleKey, List<RackState> racks)
    {
        var order = new List<int>(Kind.Count);
        if (vehicleKey != 0 && vehicleKey == PlayerTracker.VehicleKey && PlayerTracker.InControl)
            order.Add(Controller.Selected);
        foreach (var k in Kind.AutoOrder) if (!order.Contains(k)) order.Add(k);
        foreach (var k in order)
            foreach (var r in racks)
                if (r.Count[k] > 0) return k;
        return -1;
    }

    private static unsafe int VehicleOfTask(LoadTask task)
    {
        var target = task.target;
        if (target == null) return 0;
        var weapon = new WeaponBehaviour(target.Pointer);
        return VehicleKeyOf(weapon.transform);
    }

    [HarmonyPatch(typeof(LoadTask), nameof(LoadTask.UpdateRequest))]
    private static class UpdateRequestPatch
    {
        private static bool Prefix(LoadTask __instance)
        {
            
            try { if (Launchers.HandleRequest(__instance)) return false; }
            catch (Exception e) { Plugin.Log.LogError("Launcher request failed: " + e.Message); }
            
            try { if (AutoFeed.HandleRequest(__instance)) return false; }
            catch (Exception e) { Plugin.Log.LogError("Auto weapon request failed: " + e.Message); }
            
            try { if (Autoloader.HandleRequest(__instance)) return false; }
            catch (Exception e) { Plugin.Log.LogError("Autoloader request failed: " + e.Message); }

            
            if (_ctx != null && _ctx.Frame != Time.frameCount) Restore();
            if (_ctx != null) return true;
            try { Begin(__instance); }
            catch (Exception e)
            {
                Restore();
                Plugin.Log.LogError("Loader rack selection failed: " + e);
            }
            return true;
        }

        private static void Postfix(LoadTask __instance)
        {
            if (_ctx == null || _ctx.Task != __instance.Pointer) return;
            try { Restore(); }
            catch (Exception e) { Plugin.Log.LogError("Loader rack restore failed: " + e); }
        }
    }

    private static void Begin(LoadTask task)
    {
        var sizeValue = task.Request.SizeID.Value;
        var vehicleKey = VehicleOfTask(task);
        if (vehicleKey == 0) return;
        var racks = Of(vehicleKey, sizeValue);
        if (racks.Count == 0) return;
        foreach (var r in racks) Reconcile(r);
        List<RackState> carousels = null;
        for (var i = racks.Count - 1; i >= 0; i--)
            if (Autoloader.IsCarouselRack(racks[i]))
            {
                (carousels ??= new List<RackState>()).Add(racks[i]);
                racks.RemoveAt(i);
            }

        var ptr = task.Pointer;
        var state = (int)task.state;
        var request = (int)task.currentRequest;
        var ctx = new RequestContext { Task = ptr, Racks = racks, Carousels = carousels };
        if (carousels != null)
            foreach (var r in carousels) r.Beh.amountStored = 0;

        
        if (Held.TryGetValue(ptr, out var held))
        {
            if (state == (int)LoadState.Loaded && request == (int)LoadRequest.Consumed) Held.Remove(ptr);
            else ctx.ReturnKind = held.Kind;
        }

        _ctx = ctx; 
        if (racks.Count == 0) return;
        ctx.Chosen = Choose(vehicleKey, racks);
        if (ctx.Chosen < 0) return;

        var typeId = racks[0].Types.TypeIds[ctx.Chosen];
        var saved = new int[racks.Count];
        for (var i = 0; i < racks.Count; i++) saved[i] = racks[i].Beh.StoredTypeID.Value;
        
        ctx.SavedRequestType = *(int*)(ptr + RequestTypeOffset);
        ctx.SavedTypes = saved;
        for (var i = 0; i < racks.Count; i++)
        {
            var r = racks[i];
            r.Beh.amountStored = (ushort)r.Count[ctx.Chosen];
            r.Beh.StoredTypeID = new ProjectileTypeID(typeId);
        }
        *(int*)(ptr + RequestTypeOffset) = typeId;
    }

    private const int RequestTypeOffset = 0x68; 

    private static void Restore()
    {
        var ctx = _ctx;
        _ctx = null;
        if (ctx == null) return;
        if (ctx.Carousels != null)
            foreach (var r in ctx.Carousels)
                try { r.Beh.amountStored = (ushort)r.Total; r.LastTotal = r.Total; } catch { }
        for (var i = 0; i < ctx.Racks.Count; i++)
        {
            var r = ctx.Racks[i];
            try
            {
                if (ctx.SavedTypes != null) r.Beh.StoredTypeID = new ProjectileTypeID(ctx.SavedTypes[i]);
                var total = r.Total;
                r.Beh.amountStored = (ushort)total;
                r.LastTotal = total;
                FireStorageChanged(r);
            }
            catch { }
        }
        if (ctx.SavedTypes != null) *(int*)(ctx.Task + RequestTypeOffset) = ctx.SavedRequestType;
    }

    
    [HarmonyPatch(typeof(AmmoRackBehaviour), nameof(AmmoRackBehaviour.TakeShell))]
    private static class TakeShellPatch
    {
        private static void Postfix(AmmoRackBehaviour __instance, bool __result)
        {
            if (!__result) return;
            try
            {
                var s = Get(__instance.Pointer);
                if (s == null) return;
                var ctx = _ctx;
                if (ctx == null || ctx.Chosen < 0) return; 
                if (s.Count[ctx.Chosen] > 0) s.Count[ctx.Chosen]--;
                Held[ctx.Task] = (ctx.Chosen, s.Types.TypeIds[ctx.Chosen]);
            }
            catch (Exception e) { Plugin.Log.LogError("Take shell failed: " + e.Message); }
        }
    }

    
    [HarmonyPatch(typeof(AmmoRackBehaviour), nameof(AmmoRackBehaviour.LoadAmmo))]
    private static class LoadAmmoPatch
    {
        private static void Prefix(AmmoRackBehaviour __instance, out int __state)
        {
            __state = -1;
            try
            {
                var s = Get(__instance.Pointer);
                if (s == null) return;
                Reconcile(s);
                __state = __instance.amountStored;
            }
            catch { }
        }

        private static void Postfix(AmmoRackBehaviour __instance, int __state)
        {
            if (__state < 0) return;
            try
            {
                var s = Get(__instance.Pointer);
                if (s == null) return;
                var added = __instance.amountStored - __state;
                if (added <= 0) return;
                var ctx = _ctx;
                var kind = -1;
                if (ctx != null && ctx.ReturnKind >= 0) { kind = ctx.ReturnKind; ctx.ReturnKind = -1; }
                else if (_returning.Task != IntPtr.Zero && _returning.Kind >= 0) { kind = _returning.Kind; _returning.Kind = -1; }
                if (kind >= 0)
                {
                    s.Count[kind] += 1;
                    added -= 1;
                }
                if (added > 0) Add(s, added);
                if (ctx == null) s.LastTotal = __instance.amountStored;
            }
            catch (Exception e) { Plugin.Log.LogError("Load ammo failed: " + e.Message); }
        }
    }

    
    [HarmonyPatch(typeof(LoadTask), nameof(LoadTask.Cancel))]
    private static class CancelPatch
    {
        private static void Prefix(LoadTask __instance) => BeginReturn(__instance);
        private static void Postfix(LoadTask __instance) => EndReturn(__instance);
    }

    [HarmonyPatch(typeof(LoadTask), nameof(LoadTask.Unload))]
    private static class UnloadPatch
    {
        private static void Prefix(LoadTask __instance) => BeginReturn(__instance);
        private static void Postfix(LoadTask __instance) => EndReturn(__instance);
    }

    private static void BeginReturn(LoadTask task)
    {
        try
        {
            var ptr = task.Pointer;
            _returning = Held.TryGetValue(ptr, out var h) ? (ptr, h.Kind) : (IntPtr.Zero, -1);
        }
        catch { _returning = (IntPtr.Zero, -1); }
    }

    private static void EndReturn(LoadTask task)
    {
        try
        {
            if ((int)task.state != (int)LoadState.Loaded && (int)task.state != (int)LoadState.Loading) Held.Remove(task.Pointer);
        }
        catch { }
        _returning = (IntPtr.Zero, -1);
    }

    
    [HarmonyPatch(typeof(LoadTask), nameof(LoadTask.SetState))]
    private static class SetStatePatch
    {
        private static void Postfix(LoadTask __instance)
        {
            try
            {
                if ((int)__instance.state != (int)LoadState.Loaded) return;
                if (!Held.TryGetValue(__instance.Pointer, out var h)) return;
                var target = __instance.target;
                if (target == null) return;
                var weapon = new WeaponBehaviour(target.Pointer);
                weapon._LoadedProjectileTypeID_k__BackingField = new ProjectileTypeID(h.TypeId);
            }
            catch (Exception e) { Plugin.Log.LogError("Loaded type update failed: " + e.Message); }
        }
    }
}
