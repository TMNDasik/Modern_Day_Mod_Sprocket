using HarmonyLib;
using UnityEngine;

namespace GunStabilizer;





[HarmonyPatch(typeof(Sprocket.Gameplay.VehicleControl.VehicleController), nameof(Sprocket.Gameplay.VehicleControl.VehicleController.OnWeaponFire))]
internal static class OwnFireShake
{
    internal static bool Firing;
    internal static float LastShot = -100f;

    private static void Prefix()
    {
        Firing = true;
        LastShot = Time.time;
    }

    private static void Postfix() => Firing = false;
}

[HarmonyPatch(typeof(Sprocket.VehicleControl.VehicleControlPlayerState), nameof(Sprocket.VehicleControl.VehicleControlPlayerState.Shake))]
internal static class SightShakePatch
{
    private static bool Prefix(ref float value)
    {
        if (!OwnFireShake.Firing) return true;
        var scoped = false;
        try { scoped = ScopeCarry.Scope != null && !ScopeCarry.Scope.WasCollected && ScopeCarry.Scope.scoped; } catch { }
        if (!scoped) return true;
        var k = Mathf.Clamp01(Plugin.SightShake.Value);
        if (k <= 0.001f) return false;
        value *= k;
        return true;
    }
}
