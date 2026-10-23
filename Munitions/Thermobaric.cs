using System;
using System.Collections.Generic;
using HarmonyLib;
using Sprocket.Vehicles;
using Sprocket.Vehicles.Fires;
using UnityEngine;

namespace Munitions;




internal static class Thermobaric
{
    
    private static readonly Dictionary<IntPtr, FireControlVehicleModule> Modules = new();
    private static readonly Dictionary<IntPtr, IntPtr> VehicleOfModule = new();

    [HarmonyPatch(typeof(FireControlVehicleModule), nameof(FireControlVehicleModule.Update))]
    private static class ModulePatch
    {
        private static void Postfix(FireControlVehicleModule __instance)
        {
            try
            {
                var ptr = __instance.Pointer;
                if (Modules.TryGetValue(ptr, out var known) && known != null && !known.WasCollected) return;
                if (Modules.Count > 256) Cleanup();
                Modules[ptr] = __instance;
                VehicleOfModule.Remove(ptr);
            }
            catch { }
        }
    }

    private static void Cleanup()
    {
        var dead = new List<IntPtr>();
        foreach (var kv in Modules)
            if (kv.Value == null || kv.Value.WasCollected) dead.Add(kv.Key);
        foreach (var p in dead)
        {
            Modules.Remove(p);
            VehicleOfModule.Remove(p);
        }
    }

    
    public static void Detonate(Vector3 pos, SizeTypes types, int kind, float scale)
    {
        var radius = types.Spec.FireballRadius(kind) * Mathf.Clamp(scale, 0.1f, 1f);
        Fx.PlayThermobaric(pos, types.Caliber);
        var chance = Mathf.Clamp01(Plugin.ThermobaricFireChance.Value) * Mathf.Clamp01(scale);
        var near = VehiclesNear(pos, radius);
        Plugin.Log.LogInfo($"Thermobaric blast ({types.Caliber} mm): fireball {radius:0.#} m, {near.Count} vehicle(s) inside, " +
                           $"fire chance at the centre {chance * 100f:0}%, {Modules.Count} vehicle fire system(s) known");
        if (chance <= 0f || near.Count == 0) return;

        Cleanup();
        foreach (var kv in Modules)
        {
            var module = kv.Value;
            try
            {
                var vehicle = VehicleOf(kv.Key, module);
                if (vehicle == IntPtr.Zero || !near.TryGetValue(vehicle, out var hit)) continue;
                var name = hit.Vehicle != null ? hit.Vehicle.gameObject.name : "vehicle";
                if (module.OnFire)
                {
                    Plugin.Log.LogInfo($"Thermobaric blast: {name} is already burning");
                    continue;
                }
                var p = chance * Mathf.Clamp01(1f - hit.Distance / Mathf.Max(0.1f, radius));
                var roll = UnityEngine.Random.value;
                if (roll >= p)
                {
                    Plugin.Log.LogInfo($"Thermobaric blast: {name} {hit.Distance:0.0} m from the centre did not catch fire (chance {p * 100f:0}%)");
                    continue;
                }
                module.StartFire(FireDuration);
                Plugin.Log.LogInfo($"Thermobaric blast: {name} {hit.Distance:0.0} m from the centre caught fire (chance {p * 100f:0}%)");
            }
            catch (Exception e) { Plugin.Log.LogWarning("Thermobaric fire failed: " + e.Message); }
        }
    }

    
    private static float FireDuration
    {
        get
        {
            try { return Mathf.Max(5f, Sprocket.Engines.EngineRules.EngineFireDuration); }
            catch { return 30f; }
        }
    }

    
    private static Dictionary<IntPtr, (Vehicle Vehicle, float Distance)> VehiclesNear(Vector3 pos, float radius)
    {
        var near = new Dictionary<IntPtr, (Vehicle, float)>();
        try
        {
            foreach (var col in Physics.OverlapSphere(pos, radius))
            {
                if (col == null) continue;
                var rb = col.attachedRigidbody;
                if (rb == null) continue;
                var v = rb.GetComponentInParent<Vehicle>();
                if (v == null) continue;
                var b = col.bounds;
                var c = b.center;
                var ext = b.extents;
                var closest = new Vector3(Mathf.Clamp(pos.x, c.x - ext.x, c.x + ext.x), Mathf.Clamp(pos.y, c.y - ext.y, c.y + ext.y),
                    Mathf.Clamp(pos.z, c.z - ext.z, c.z + ext.z));
                var d = Vector3.Distance(closest, pos);
                if (!near.TryGetValue(v.Pointer, out var cur) || d < cur.Item2) near[v.Pointer] = (v, d);
            }
        }
        catch (Exception e) { Plugin.Log.LogWarning("Thermobaric overlap failed: " + e.Message); }
        return near;
    }

    
    private static IntPtr VehicleOf(IntPtr key, FireControlVehicleModule module)
    {
        if (VehicleOfModule.TryGetValue(key, out var cached)) return cached;
        var result = IntPtr.Zero;
        try
        {
            var r = module.receiver;
            GameObject go = null;
            if (r != null)
            {
                var stage = r.TryCast<FireControlAssemblyStage>();
                go = stage != null ? stage.GameObject : r.GameObject;
            }
            var v = go != null ? go.GetComponentInParent<Vehicle>() : null;
            if (v == null && go != null) v = go.GetComponentInChildren<Vehicle>();
            if (v != null) result = v.Pointer;
        }
        catch { }
        if (result != IntPtr.Zero) VehicleOfModule[key] = result;
        return result;
    }
}
