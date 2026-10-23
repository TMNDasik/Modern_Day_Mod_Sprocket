using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Sprocket.Vehicles;
using Sprocket.Vehicles.Serialization;
using UnityEngine;

namespace GunStabilizer;





internal static class FcsRegistry
{
    public const string Key = "mdfcs";

    internal sealed class Entry
    {
        public VehicleObject Vo;
        public IntPtr Ptr;
        public int Id;
        public FcsSettings S;
        public bool Alive
        {
            get
            {
                try { return Vo != null && !Vo.WasCollected && Vo.Pointer == Ptr && Vo; }
                catch { return false; }
            }
        }
    }

    private static readonly Dictionary<IntPtr, Entry> All = new();
    private static readonly List<Entry> Buffer = new();

    public static Entry Ensure(VehicleObject vo)
    {
        if (!FcsPart.IsOurs(vo)) return null;
        if (All.TryGetValue(vo.Pointer, out var e) && e.Alive) return e;
        e = new Entry { Vo = vo, Ptr = vo.Pointer, Id = vo.GetInstanceID(), S = new FcsSettings() };
        All[vo.Pointer] = e;
        return e;
    }

    public static void Edit(VehicleObject vo, Action<FcsSettings> change)
    {
        var e = Ensure(vo);
        if (e == null) return;
        var s = e.S.Clone();
        change(s);
        s.Sanitize();
        e.S = s;
    }

    
    public static List<Entry> Snapshot()
    {
        Buffer.Clear();
        List<IntPtr> dead = null;
        foreach (var kv in All)
        {
            if (kv.Value.Alive) Buffer.Add(kv.Value);
            else (dead ??= new List<IntPtr>()).Add(kv.Key);
        }
        if (dead != null) foreach (var k in dead) All.Remove(k);
        return Buffer;
    }

    

    private sealed class Cached
    {
        public Entry Entry;
        public float CheckedAt;
    }

    private static readonly Dictionary<int, Cached> ByVehicle = new();

    
    
    public static Entry ForVehicle(Vehicle vehicle)
    {
        if (vehicle == null) return null;
        var key = vehicle.gameObject.GetInstanceID();
        var now = Time.unscaledTime;
        if (ByVehicle.TryGetValue(key, out var c) && now - c.CheckedAt < 5f && (c.Entry == null || c.Entry.Alive)) return c.Entry;
        Entry found = null;
        try
        {
            var root = vehicle.transform;
            foreach (var e in Snapshot())
            {
                Transform t;
                try { t = e.Vo.transform; }
                catch { continue; }
                if (t == null || !t.IsChildOf(root)) continue;
                if (found == null || e.Id < found.Id) found = e;
            }
        }
        catch { }
        ByVehicle[key] = new Cached { Entry = found, CheckedAt = now };
        return found;
    }

    public static void Forget() => ByVehicle.Clear();

    

    
    [HarmonyPatch(typeof(VehicleObjectSerialization), nameof(VehicleObjectSerialization.ToBlueprint))]
    private static class SavePatch
    {
        private static void Postfix(VehicleObject obj, VehicleObjectBlueprint __result)
        {
            try
            {
                if (__result == null || !FcsPart.IsOurs(obj)) return;
                var e = Ensure(obj);
                var state = __result.State;
                if (e == null || state == null || state.FindElement(Key) >= 0) return;
                state.AddValue(Key, new Il2CppSystem.Object(IL2CPP.ManagedStringToIl2Cpp(e.S.ToToken())));
            }
            catch (Exception ex) { Plugin.Log.LogError("Fire control computer settings save failed: " + ex); }
        }
    }

    
    [HarmonyPatch(typeof(VehicleObjectSerialization), nameof(VehicleObjectSerialization.ApplyBaseObjectState))]
    private static class LoadPatch
    {
        private static void Postfix(VehicleObject obj, Il2CppSystem.Runtime.Serialization.SerializationInfo state)
        {
            try
            {
                if (!FcsPart.IsOurs(obj)) return;
                var e = Ensure(obj);
                if (e == null || state == null || state.FindElement(Key) < 0) return;
                e.S = FcsSettings.FromToken(state.GetString(Key));
                if (Plugin.DebugLogging.Value) Plugin.Log.LogInfo("Fire control computer loaded: " + e.S.ToToken());
            }
            catch (Exception ex) { Plugin.Log.LogError("Fire control computer settings load failed: " + ex); }
        }
    }
}
