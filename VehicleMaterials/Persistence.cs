using System;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Sprocket.Vehicles;
using Sprocket.Vehicles.Serialization;

namespace VehicleMaterials;




internal static class Persistence
{
    public const string Key = "mat";

    [HarmonyPatch(typeof(VehicleObjectSerialization), nameof(VehicleObjectSerialization.ToBlueprint))]
    private static class SavePatch
    {
        private static void Postfix(VehicleObject obj, VehicleObjectBlueprint __result)
        {
            try
            {
                var m = Store.Get(obj);
                if (m == null || __result == null) return;
                var faces = m.Faces;
                if (!m.FacesPending)
                {
                    var em = FaceTags.Mesh(obj);
                    if (em != null) faces = FaceTags.Read(em);
                }
                var token = PartMaterials.ToToken(m.Whole, faces);
                if (m.Whole == MaterialKind.Default && (faces == null || !Array.Exists(faces, t => t != 0))) return;
                var state = __result.State;
                if (state == null || state.FindElement(Key) >= 0) return;
                state.AddValue(Key, new Il2CppSystem.Object(IL2CPP.ManagedStringToIl2Cpp(token)));
            }
            catch (Exception e) { Plugin.Log.LogError("Material save failed: " + e); }
        }
    }

    [HarmonyPatch(typeof(VehicleObjectSerialization), nameof(VehicleObjectSerialization.ApplyBaseObjectState))]
    private static class LoadPatch
    {
        private static void Postfix(VehicleObject obj, Il2CppSystem.Runtime.Serialization.SerializationInfo state)
        {
            try
            {
                if (obj == null || state == null || state.FindElement(Key) < 0) return;
                var m = PartMaterials.FromToken(state.GetString(Key));
                if (!m.IsDefault) Store.Set(obj, m);
                Applier.Changed();
            }
            catch (Exception e) { Plugin.Log.LogError("Material load failed: " + e); }
        }
    }
}
