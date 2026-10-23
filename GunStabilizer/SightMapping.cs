using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace GunStabilizer;



internal static class SightMapping
{
    private delegate bool MapFn(Vector2 cameraViewport, out Vector2 screenViewport);

    private static MapFn _map;
    private static float _nextTry;

    public static bool Map(Vector2 cameraViewport, out Vector2 screenViewport)
    {
        screenViewport = cameraViewport;
        if (_map == null)
        {
            if (Time.unscaledTime < _nextTry) return false;
            _nextTry = Time.unscaledTime + 10f;
            try
            {
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (a.GetName().Name is not ("ThermalSight" or "MDThermalSight")) continue;
                    var m = a.GetType("ThermalSight.Api")?.GetMethod("SightToScreen", BindingFlags.Public | BindingFlags.Static);
                    if (m != null) _map = (MapFn)Delegate.CreateDelegate(typeof(MapFn), m);
                    break;
                }
            }
            catch { }
            if (_map == null) return false;
        }
        try { return _map(cameraViewport, out screenViewport); }
        catch
        {
            screenViewport = cameraViewport;
            return false;
        }
    }
}


[HarmonyPatch(typeof(Sprocket.VehicleControl.VehicleInputSystemInputs), nameof(Sprocket.VehicleControl.VehicleInputSystemInputs.Fire))]
internal static class FireInterlockPatch
{
    private static void Postfix(ref bool __result)
    {
        if (!__result) return;
        try
        {
            if (!FireControl.HoldFire) return;
            __result = false;
            FireControl.NoteHeld();
        }
        catch { }
    }
}
