using HarmonyLib;
using Sprocket.Vehicles;
using Sprocket.Vehicles.AmmunitionStorage;
using Sprocket.Vehicles.Weapons;
using Sprocket.WeaponFramework;
using Sprocket.Vehicles.Cannons;
using UnityEngine;
namespace Munitions;

internal static class NpcMissileReserve
{
    internal static bool HasOtherAmmo(Transform transform)
    {
        var key=Racks.VehicleKeyOf(transform);
        if(PlayerTracker.InControl && PlayerTracker.VehicleKey==key)return false;
        var vehicle=transform.GetComponentInParent<Vehicle>();if(vehicle==null)return false;
        foreach(var rack in vehicle.GetComponentsInChildren<AmmoRack>(true))
        {
            var b=rack.behaviour;if(b==null || b.amountStored==0)continue;
            var state=Racks.Get(b.Pointer);
            if(state!=null){Racks.Reconcile(state);if(state.Total-state.Count[Kind.Atgm]>0)return true;}
            else if(!TypeRegistry.TryGetKind(b.StoredTypeID.Value,out _,out var kind) || !Kind.IsMissile(kind))return true;
        }
        foreach(var cannon in vehicle.GetComponentsInChildren<Cannon>(true))
        {
            var weapon=cannon.Behaviour;if(weapon==null)continue;
            if(!weapon._Functioning_k__BackingField || weapon.LoadState!=LoadState.Loaded)continue;
            var id=weapon._LoadedProjectileTypeID_k__BackingField.Value;
            if(id!=0 && (!TypeRegistry.TryGetKind(id,out _,out var kind) || !Kind.IsMissile(kind)))return true;
        }
        return false;
    }
    [HarmonyPatch(typeof(WeaponBehaviour),nameof(WeaponBehaviour.Fire))]
    private static class FirePatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(WeaponBehaviour __instance)
        {
            var id=__instance._LoadedProjectileTypeID_k__BackingField.Value;
            return !TypeRegistry.TryGetKind(id,out _,out var kind) || !Kind.IsMissile(kind) || !HasOtherAmmo(__instance.transform);
        }
    }
}
