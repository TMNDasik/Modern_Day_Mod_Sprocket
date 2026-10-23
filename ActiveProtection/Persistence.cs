using System;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Sprocket;
using Sprocket.Vehicles;
using Sprocket.Vehicles.Serialization;

namespace ActiveProtection;




internal static class Persistence
{
    public const string Key = "aps";

    
    [HarmonyPatch(typeof(VehicleObjectSerialization), nameof(VehicleObjectSerialization.ToBlueprint))]
    private static class SavePatch
    {
        private static void Postfix(VehicleObject obj, VehicleObjectBlueprint __result)
        {
            try
            {
                if (__result == null || !ApsParts.IsOurs(obj)) return;
                var m = Modules.Ensure(obj);
                var state = __result.State;
                if (m == null || state == null) return;
                if (state.FindElement(Key) >= 0) return;
                state.AddValue(Key, new Il2CppSystem.Object(IL2CPP.ManagedStringToIl2Cpp(m.S.ToToken())));
            }
            catch (Exception e) { Plugin.Log.LogError("APS settings save failed: " + e); }
        }
    }

    
    [HarmonyPatch(typeof(VehicleObjectSerialization), nameof(VehicleObjectSerialization.ApplyBaseObjectState))]
    private static class LoadPatch
    {
        private static void Postfix(VehicleObject obj, Il2CppSystem.Runtime.Serialization.SerializationInfo state)
        {
            try
            {
                if (state == null || !ApsParts.IsOurs(obj)) return;
                if (state.FindElement(Key) < 0)
                {
                    Modules.Ensure(obj);
                    return;
                }
                var token = state.GetString(Key);
                Modules.Load(obj, token);
                if (Plugin.DebugLogging.Value) Plugin.Log.LogInfo("APS module loaded: " + token);
            }
            catch (Exception e) { Plugin.Log.LogError("APS settings load failed: " + e); }
        }
    }
}
