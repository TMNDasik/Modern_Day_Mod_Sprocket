using System;
using HarmonyLib;
using Sprocket.Vehicles;
using Sprocket.Vehicles.AttachedBehaviours;
using Sprocket.Vehicles.Cannons;
using Sprocket.WeaponFramework;
using UnityEngine;

namespace ThermalSight;





[HarmonyPatch(typeof(Cannon), nameof(Cannon.EnableBehaviour))]
internal static class PrimarySight
{
    private static void Postfix(Cannon __instance)
    {
        try
        {
            var beh = __instance.behaviour;
            if (beh == null) return;
            var sight = beh.Sight;
            var current = sight?.TryCast<GunnerSight>();
            if (current == null || !ThermalController.IsSpecialSight(current)) return;

            var regular = FindRegular(__instance, current);
            if (regular == null) return; 
            var regularI = regular.TryCast<IGunnerSight>();
            if (regularI == null) return;
            beh.Sight = regularI;
            Plugin.Log.LogInfo($"Gun '{__instance.gameObject.name}': main sight is the regular sight, thermal sight on {Plugin.ToggleKeyName}");
        }
        catch (Exception e) { Plugin.Log.LogWarning("Main sight selection failed: " + e.Message); }
    }

    
    private static GunnerSight FindRegular(Cannon cannon, GunnerSight thermal)
    {
        var vehicle = cannon.GetComponentInParent<Vehicle>();
        var root = vehicle != null ? vehicle.transform : cannon.transform.root;
        GunnerSight best = null;
        var bestScore = float.MaxValue;
        var from = thermal.transform.position;
        var weapon = cannon.transform;
        foreach (var s in root.GetComponentsInChildren<GunnerSight>(true))
        {
            if (s == null || s.Pointer == thermal.Pointer) continue;
            if (ThermalController.IsSpecialSight(s)) continue;
            var score = Vector3.Distance(s.transform.position, from);
            var linked = s.LinkedWeapon;
            if (linked != null && linked.Pointer == weapon.Pointer) score -= 1000f;
            if (score < bestScore) { bestScore = score; best = s; }
        }
        return best;
    }
}
