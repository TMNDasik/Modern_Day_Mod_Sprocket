using HarmonyLib;
using UnityEngine.VFX;

namespace Munitions;




internal static class VfxGuard
{
    [HarmonyPatch(typeof(VisualEffect), nameof(VisualEffect.SetFloat), typeof(int), typeof(float))]
    private static class FloatPatch
    {
        private static bool Prefix(VisualEffect __instance, int nameID)
        {
            try { return __instance == null || __instance.HasFloat(nameID); }
            catch { return true; }
        }
    }

    [HarmonyPatch(typeof(VisualEffect), nameof(VisualEffect.SetInt), typeof(int), typeof(int))]
    private static class IntPatch
    {
        private static bool Prefix(VisualEffect __instance, int nameID)
        {
            try { return __instance == null || __instance.HasInt(nameID); }
            catch { return true; }
        }
    }
}
