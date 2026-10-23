using System;
using HarmonyLib;
using Sprocket.VehicleControl;
using UnityEngine;

namespace GunStabilizer;






[HarmonyPatch(typeof(VehicleControlPlayerState), nameof(VehicleControlPlayerState.MatchThirdPersonViewToActiveControllerView))]
internal static class ThirdPersonLevel
{
    private static int _errors;

    private static void Postfix(VehicleControlPlayerState __instance)
    {
        try
        {
            var orbital = __instance.cameraMovementController;
            if (orbital == null) return;
            var q = orbital.currentRotation;
            var forward = q * Vector3.forward;
            if (forward.sqrMagnitude < 1e-6f || Mathf.Abs(Vector3.Dot(forward.normalized, Vector3.up)) > 0.999f) return;
            orbital.currentRotation = Quaternion.LookRotation(forward, Vector3.up);
        }
        catch (Exception e)
        {
            if (_errors++ < 5) Plugin.Log.LogWarning("Levelling the third-person camera failed: " + e.Message);
        }
    }
}
