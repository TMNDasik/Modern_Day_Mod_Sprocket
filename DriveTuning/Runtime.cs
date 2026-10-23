using System;
using System.Collections.Generic;
using HarmonyLib;
using Sprocket.Vehicles.Turrets;
using Sprocket.Vehicles.Weapons;

namespace DriveTuning;






internal static class Runtime
{
    private static readonly Dictionary<IntPtr, DriveSettings> Turrets = new();
    private static readonly Dictionary<IntPtr, DriveSettings> Laying = new();

    [HarmonyPatch(typeof(TraverseMotor), nameof(TraverseMotor.EnableBehaviour))]
    private static class TraverseEnablePatch
    {
        private static void Postfix(TraverseMotor __instance)
        {
            try
            {
                var tb = __instance.Behaviour?.TryCast<TurretBehaviour>();
                if (tb == null) return;
                var s = DriveSettings.Read(__instance.TraverseBlueprintSlot?.Blueprint);
                if (!s.Any) { Turrets.Remove(tb.Pointer); return; }
                Turrets[tb.Pointer] = s;
                Apply(tb, s);
                if (Plugin.DebugLogging.Value)
                    Plugin.Log.LogInfo($"Turret drive: {Describe(s.Speed, tb.maxTraverseRate)} °/s, {Describe(s.Accel, tb.maxAcceleration)} °/s²");
            }
            catch (Exception e) { Plugin.Log.LogError("Turret drive tuning failed: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(LayingDrive), nameof(LayingDrive.EnableBehaviour))]
    private static class LayingEnablePatch
    {
        private static void Postfix(LayingDrive __instance)
        {
            try
            {
                var ldb = __instance.Behaviour?.TryCast<LayingDriveBehaviour>();
                if (ldb == null) return;
                var s = DriveSettings.Read(__instance.BlueprintSlot?.Blueprint);
                if (!s.Any) { Laying.Remove(ldb.Pointer); return; }
                Laying[ldb.Pointer] = s;
                Apply(ldb, s);
                if (Plugin.DebugLogging.Value)
                    Plugin.Log.LogInfo($"Laying drive: elevation {ldb.elevationInfo.MaxSpeed:0.#} °/s, azimuth {ldb.azimuthInfo.MaxSpeed:0.#} °/s");
            }
            catch (Exception e) { Plugin.Log.LogError("Laying drive tuning failed: " + e.Message); }
        }
    }

    
    [HarmonyPatch(typeof(TurretBehaviour), nameof(TurretBehaviour.MoveToTarget))]
    private static class TurretMovePatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(TurretBehaviour __instance)
        {
            if (Turrets.Count == 0) return;
            try { if (Turrets.TryGetValue(__instance.Pointer, out var s)) Apply(__instance, s); }
            catch { }
        }
    }

    [HarmonyPatch(typeof(LayingDriveBehaviour), nameof(LayingDriveBehaviour.MoveToTarget))]
    private static class LayingMovePatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(LayingDriveBehaviour __instance)
        {
            if (Laying.Count == 0) return;
            try { if (Laying.TryGetValue(__instance.Pointer, out var s)) Apply(__instance, s); }
            catch { }
        }
    }

    private static void Apply(TurretBehaviour tb, DriveSettings s)
    {
        if (s.Speed > 0f && tb.maxTraverseRate != s.Speed) tb.maxTraverseRate = s.Speed;
        if (s.Accel > 0f && tb.maxAcceleration != s.Accel)
        {
            tb.maxAcceleration = s.Accel;
            
            if (tb.maxBrakeAcceleration < s.Accel) tb.maxBrakeAcceleration = s.Accel;
        }
    }

    private static void Apply(LayingDriveBehaviour ldb, DriveSettings s)
    {
        if (s.Speed > 0f || s.Accel > 0f) ldb.elevationInfo = Tune(ldb, ldb.elevationInfo, s.Speed, s.Accel, true);
        if (s.AzSpeed > 0f || s.AzAccel > 0f) ldb.azimuthInfo = Tune(ldb, ldb.azimuthInfo, s.AzSpeed, s.AzAccel, false);
    }

    
    private static unsafe RotationAxisInfo Tune(LayingDriveBehaviour ldb, RotationAxisInfo info, float speed, float accel, bool elevation)
    {
        var torque = info.MotorTorque;
        if (accel > 0f)
        {
            var inertia = ldb.momentOfInertia;
            if (inertia > 0f && !float.IsNaN(inertia))
            {
                
                var gravity = elevation ? Math.Abs(ldb.mass * ldb.centreOfMass * 9.81f) : 0f;
                torque = accel * inertia + gravity;
            }
        }
        var p = (float*)&info;
        if (accel > 0f) p[0] = torque;
        if (speed > 0f) p[1] = speed;
        return info;
    }

    private static string Describe(float setting, float actual) => setting > 0f ? $"{actual:0.#}" : $"{actual:0.#} (game)";
}
