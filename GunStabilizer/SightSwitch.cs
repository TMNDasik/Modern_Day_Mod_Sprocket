using System;
using HarmonyLib;
using Sprocket;
using Sprocket.Vehicles;
using UnityEngine;

namespace GunStabilizer;






[HarmonyPatch(typeof(ScopeController), nameof(ScopeController.ActivateSight))]
internal static class SightSwitch
{
    private static int _errors;

    private static void Prefix(ScopeController __instance, out Vector3? __state)
    {
        __state = null;
        try
        {
            
            if (!__instance.scoped) return;
            
            SightHold.Allow();
            var cam = __instance.controller;
            if (cam == null || cam.WasCollected) return;
            var e = ScopeCameraController.CameraAngleToEuler(cam.targetAngles);
            var dir = Quaternion.Euler(e.x, e.y, 0f) * Vector3.forward;
            var from = cam.transform.position;
            __state = from + dir * ScopeCarry.AimDistanceFrom(from);
        }
        catch (Exception ex)
        {
            if (_errors++ < 5) Plugin.Log.LogWarning("Sight switch (before) failed: " + ex.Message);
        }
    }

    private static void Postfix(ScopeController __instance, Vector3? __state)
    {
        if (__state == null) return;
        try
        {
            var cam = __instance.controller;
            if (cam == null || cam.WasCollected) return;
            var point = __state.Value;
            
            cam.AimAt(point, Vector3.up);
            var dir = point - cam.transform.position;
            if (dir.sqrMagnitude > 1e-4f) cam.currentRotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
            if (Plugin.DebugLogging.Value) Plugin.Log.LogInfo($"Fire group switched in the sight — aim kept at {dir.magnitude:0} m");
        }
        catch (Exception ex)
        {
            if (_errors++ < 5) Plugin.Log.LogWarning("Sight switch (after) failed: " + ex.Message);
        }
    }
}
