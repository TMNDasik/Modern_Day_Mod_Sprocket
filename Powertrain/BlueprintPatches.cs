using System;
using HarmonyLib;
using Sprocket.Vehicles.Engines;

namespace Powertrain;



internal static class BlueprintPatches
{
    
    public static void ApplyFields(EngineBlueprint bp, TurbineSpec s)
    {
        bp.CylinderCount = TurbineModel.EquivalentCylinders;
        bp.CylinderVolume = TurbineModel.EquivalentCylinderVolume(s);
        bp.Upshift = (ushort)s.Upshift;
        bp.Downshift = (ushort)s.Operating;
        bp.CustomRevLimit = s.Override ? (short)s.Limit : (short)-1;
    }

    
    [HarmonyPatch(typeof(EngineBlueprint), nameof(EngineBlueprint.Load))]
    private static class LoadPatch
    {
        private static void Postfix(EngineBlueprint __instance)
        {
            try
            {
                var s = SpecCache.Get(__instance);
                if (s != null) ApplyFields(__instance, s);
            }
            catch (Exception e) { Plugin.Log.LogWarning("Gas turbine blueprint update failed: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(EngineBlueprint), nameof(EngineBlueprint.Mass), MethodType.Getter)]
    private static class MassPatch
    {
        private static void Postfix(EngineBlueprint __instance, ref float __result)
        {
            var s = SpecCache.Get(__instance);
            if (s != null) __result = TurbineModel.Mass(s);
        }
    }

    [HarmonyPatch(typeof(EngineBlueprint), nameof(EngineBlueprint.Volume), MethodType.Getter)]
    private static class VolumePatch
    {
        private static void Postfix(EngineBlueprint __instance, ref float __result)
        {
            var s = SpecCache.Get(__instance);
            if (s != null) __result = TurbineModel.Volume(s);
        }
    }

    [HarmonyPatch(typeof(EngineBlueprint), nameof(EngineBlueprint.MaxRPM), MethodType.Getter)]
    private static class MaxRpmPatch
    {
        private static void Postfix(EngineBlueprint __instance, ref int __result)
        {
            var s = SpecCache.Get(__instance);
            if (s != null) __result = s.Safe;
        }
    }

    [HarmonyPatch(typeof(EngineBlueprint), nameof(EngineBlueprint.IdleRPM), MethodType.Getter)]
    private static class IdleRpmPatch
    {
        private static void Postfix(EngineBlueprint __instance, ref int __result)
        {
            var s = SpecCache.Get(__instance);
            if (s != null) __result = s.Idle;
        }
    }

    [HarmonyPatch(typeof(EngineBlueprint), nameof(EngineBlueprint.MaxTorque), MethodType.Getter)]
    private static class MaxTorquePatch
    {
        private static void Postfix(EngineBlueprint __instance, ref float __result)
        {
            var s = SpecCache.Get(__instance);
            if (s != null) __result = TurbineModel.StallTorque(s);
        }
    }

    [HarmonyPatch(typeof(EngineBlueprint), nameof(EngineBlueprint.Inertia), MethodType.Getter)]
    private static class InertiaPatch
    {
        private static void Postfix(EngineBlueprint __instance, ref float __result)
        {
            var s = SpecCache.Get(__instance);
            if (s != null) __result = s.Inertia;
        }
    }

    [HarmonyPatch(typeof(EngineBlueprint), nameof(EngineBlueprint.FrictionCoefficient), MethodType.Getter)]
    private static class FrictionPatch
    {
        private static void Postfix(EngineBlueprint __instance, ref float __result)
        {
            var s = SpecCache.Get(__instance);
            if (s != null) __result = TurbineModel.DragPerRad(s);
        }
    }

    [HarmonyPatch(typeof(EngineBlueprint), nameof(EngineBlueprint.FuelPerRevolution), MethodType.Getter)]
    private static class FuelPatch
    {
        private static void Postfix(EngineBlueprint __instance, ref float __result)
        {
            var s = SpecCache.Get(__instance);
            if (s != null) __result = TurbineModel.FuelFullPerSecond(s) / System.Math.Max(1f, s.Safe / 60f);
        }
    }

    [HarmonyPatch(typeof(EngineBlueprint), nameof(EngineBlueprint.AveragePower), MethodType.Getter)]
    private static class AveragePowerPatch
    {
        private static void Postfix(EngineBlueprint __instance, ref float __result)
        {
            var s = SpecCache.Get(__instance);
            if (s != null) __result = TurbineModel.PowerAt(s, (s.Upshift + s.Operating) * 0.5f);
        }
    }

    
    [HarmonyPatch(typeof(EngineBlueprint), nameof(EngineBlueprint.MaterialCost), MethodType.Getter)]
    private static class MaterialCostPatch
    {
        private static void Postfix(EngineBlueprint __instance, ref float __result)
        {
            if (SpecCache.Get(__instance) != null) __result *= 2.5f;
        }
    }

    [HarmonyPatch(typeof(EngineBlueprint), nameof(EngineBlueprint.AssemblyCost), MethodType.Getter)]
    private static class AssemblyCostPatch
    {
        private static void Postfix(EngineBlueprint __instance, ref float __result)
        {
            if (SpecCache.Get(__instance) != null) __result *= 2.5f;
        }
    }

    
    
    [HarmonyPatch]
    private static class SyncPatch
    {
        private static bool Prepare() => EngineCompat.Method(EngineCompat.PropertiesType, "SyncDependentValues") != null;
        private static System.Reflection.MethodBase TargetMethod() => EngineCompat.Method(EngineCompat.PropertiesType, "SyncDependentValues");

        private static bool Prefix(EngineBlueprint target) => SpecCache.Get(target) == null;
    }
}
