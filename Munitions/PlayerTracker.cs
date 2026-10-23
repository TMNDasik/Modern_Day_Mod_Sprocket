using System;
using HarmonyLib;
using Sprocket.Vehicles.Cannons;
using Sprocket.VehicleControl;
using UnityEngine;

namespace Munitions;


[HarmonyPatch(typeof(VehicleControlPlayerState), nameof(VehicleControlPlayerState.InternalUpdate))]
internal static class PlayerTracker
{
    public static int VehicleKey;
    public static Cannon Cannon;
    public static Camera Camera;
    public static GameObject VehicleGo;   
    public static bool Scoped;
    public static bool Firing; 
    public static int LastFrame = -100;
    private static int _errors;

    public static bool InControl => Time.frameCount - LastFrame <= 2 && VehicleKey != 0;

    private static void Prefix(VehicleControlPlayerState __instance)
    {
        try
        {
            LastFrame = Time.frameCount;
            var vc = __instance.vehicleController;
            if (vc == null)
            {
                VehicleKey = 0;
                Cannon = null;
                VehicleGo = null;
                Scoped = false;
                Firing = false;
                return;
            }
            Camera = vc.ActiveCamera;
            Firing = vc.firing;

            Cannon cannon = null;
            var gunner = vc.activeGunner;
            var weapon = gunner != null ? gunner.WeaponGameObject : null;
            if (weapon != null)
            {
                cannon = weapon.GetComponent<Cannon>();
                if (cannon == null) cannon = weapon.GetComponentInParent<Cannon>();
            }
            Cannon = cannon;

            var vehicleGo = vc.ControlledVehicleGameObject;
            VehicleGo = vehicleGo;
            VehicleKey = cannon != null ? Racks.VehicleKeyOf(cannon.transform)
                : vehicleGo != null ? Racks.VehicleKeyOf(vehicleGo.transform) : 0;

            var active = __instance.activeMovementController;
            var scope = __instance.scopeMovementController;
            Scoped = active != null && scope != null && active.Pointer == scope.Pointer;
        }
        catch (Exception e)
        {
            if (_errors++ < 5) Plugin.Log.LogError("Player tracking failed: " + e);
        }
    }
}
