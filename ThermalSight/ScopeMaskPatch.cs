using HarmonyLib;
using Sprocket.Vehicles.Weapons;

namespace ThermalSight;



[HarmonyPatch(typeof(Scope), nameof(Scope.IsActive))]
internal static class ScopeMaskPatch
{
    private static void Postfix(ref bool __result)
    {
        if (ThermalView.AnyActive) __result = false;
    }
}
