using System;
using HarmonyLib;
using Sprocket.VehicleDesigner;
using UnityEngine;

namespace CombatEffects;



[HarmonyPatch(typeof(VehicleEditor), nameof(VehicleEditor.EditorUpdate))]
internal static class EditorWatch
{
    private static float _seen = -10f;

    public static bool Active => Time.unscaledTime - _seen < 0.5f;

    private static void Postfix()
    {
        var was = Active;
        _seen = Time.unscaledTime;
        if (was) return;
        try
        {
            Hooks.StopAll();
            Effects.Clear();
            Plugin.Log.LogInfo("Vehicle editor opened: battle effects cleared");
        }
        catch (Exception e) { Plugin.Log.LogWarning("Clearing effects for the editor failed: " + e.Message); }
    }
}
