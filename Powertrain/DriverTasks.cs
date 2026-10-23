using System;
using System.Collections.Generic;
using HarmonyLib;
using Sprocket.Powertrains.DriverInput;
using Sprocket.Vehicles.CrewSystems;
using UnityEngine;

namespace Powertrain;











internal static unsafe class DriverTasks
{
    private const int OperateCount = 0x14;
    private const float GraceSeconds = 1.5f;

    private sealed class Entry
    {
        public CrewTask Task;
        public float LastOperated;
    }

    private static readonly Dictionary<IntPtr, Entry> ByPowertrain = new();
    private static bool _captured;

    [HarmonyPatch(typeof(CrewTaskFactory), nameof(CrewTaskFactory.Sprocket_Vehicles_Powertrains_IDriveRoleFactory_Create))]
    private static class CreatePatch
    {
        private static void Postfix(IDriverInputReciever __0, CrewTask __result)
        {
            try
            {
                if (__0 == null || __result == null) return;
                if (ByPowertrain.Count > 256) Cleanup();
                ByPowertrain[__0.Pointer] = new Entry { Task = __result, LastOperated = Time.time };
                if (!_captured)
                {
                    _captured = true;
                    Plugin.Log.LogInfo("Gearbox: driver crew tasks are tracked (no driver = the vehicle stops, no shifting)");
                }
            }
            catch { }
        }
    }

    
    public static bool Unmanned(IntPtr powertrain)
    {
        if (!ByPowertrain.TryGetValue(powertrain, out var e)) return false;
        var task = e.Task;
        if (task == null || task.WasCollected) return false;
        var now = Time.time;
        if (((byte*)task.Pointer)[OperateCount] != 0)
        {
            e.LastOperated = now;
            return false;
        }
        return now - e.LastOperated > GraceSeconds;
    }

    
    
    private static readonly Dictionary<IntPtr, float> Seen = new();

    public static bool TaskMissing(IntPtr powertrain)
    {
        if (!_captured || ByPowertrain.ContainsKey(powertrain)) return false;
        var now = Time.time;
        if (!Seen.TryGetValue(powertrain, out var first))
        {
            if (Seen.Count > 256) Seen.Clear();
            Seen[powertrain] = now;
            return false;
        }
        return now - first > 2f;
    }

    
    public static string Describe(IntPtr powertrain)
    {
        if (!ByPowertrain.TryGetValue(powertrain, out var e) || e.Task == null || e.Task.WasCollected)
            return _captured ? "no driver task (no seat drives this vehicle)" : "driver tasks not tracked yet";
        return $"driver task operated by {((byte*)e.Task.Pointer)[OperateCount]} crew member(s), last {Time.time - e.LastOperated:0.0} s ago";
    }

    private static void Cleanup()
    {
        var dead = new List<IntPtr>();
        foreach (var p in ByPowertrain)
            if (p.Value.Task == null || p.Value.Task.WasCollected) dead.Add(p.Key);
        foreach (var k in dead) ByPowertrain.Remove(k);
        if (ByPowertrain.Count > 256) ByPowertrain.Clear();
    }
}
