using System;
using System.Collections.Generic;
using HarmonyLib;
using Sprocket;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Powertrain;





internal static class InputBridge
{
    private static readonly HashSet<IntPtr> Remapped = new();
    private static float _nextScan;

    public static bool MouseShifting;

    
    public static void Update()
    {
        if (!Plugin.RemapCommandKey.Value || Time.unscaledTime < _nextScan) return;
        _nextScan = Time.unscaledTime + 2f;
        try
        {
            foreach (var asset in Resources.FindObjectsOfTypeAll<InputActionAsset>())
            {
                if (asset == null || Remapped.Contains(asset.Pointer)) continue;
                var action = asset.FindAction("CommandVehicle", false);
                if (action == null) continue;
                Remapped.Add(asset.Pointer);
                InputActionRebindingExtensions.ApplyBindingOverride(action, "<Keyboard>/k", null, "<Keyboard>/q");
                Plugin.Log.LogInfo("Command vehicle moved from Q to K");
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("Command key remap failed: " + e.Message);
            _nextScan = Time.unscaledTime + 30f;
        }
    }

    
    [HarmonyPatch(typeof(OrbitalMovementController), nameof(OrbitalMovementController.ProcessInputs))]
    private static class OrbitalCamera
    {
        private static void Prefix(OrbitalMovementController __instance)
        {
            if (!MouseShifting) return;
            try { __instance.movementInput = Vector2.zero; }
            catch { }
        }
    }

    [HarmonyPatch(typeof(ScopeCameraController), nameof(ScopeCameraController.ProcessInputs))]
    private static class ScopeCamera
    {
        private static void Prefix(ScopeCameraController __instance)
        {
            if (!MouseShifting) return;
            try { __instance.movementInput = Vector2.zero; }
            catch { }
        }
    }
}
