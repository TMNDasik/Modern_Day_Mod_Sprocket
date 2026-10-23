using System;
using HarmonyLib;
using Sprocket.AudioSystems;
using Sprocket.VehicleControl;
using Sprocket.Vehicles;
using UnityEngine;

namespace SoundOverhaul;



[HarmonyPatch(typeof(VehicleControlPlayerState), nameof(VehicleControlPlayerState.InternalUpdate))]
internal static class View
{
    private static IntPtr _vehicle;
    private static bool _scoped;
    private static Camera _camera;
    private static int _lastFrame = -100;
    private static int _errors;

    private static void Prefix(VehicleControlPlayerState __instance)
    {
        try
        {
            _lastFrame = Time.frameCount;
            var vc = __instance.vehicleController;
            if (vc == null) { _vehicle = IntPtr.Zero; _scoped = false; return; }
            _camera = vc.ActiveCamera;
            var go = vc.ControlledVehicleGameObject;
            var v = go != null ? go.GetComponentInParent<Vehicle>() : null;
            _vehicle = v != null ? v.Pointer : IntPtr.Zero;
            var active = __instance.activeMovementController;
            var scope = __instance.scopeMovementController;
            _scoped = active != null && scope != null && active.Pointer == scope.Pointer;
        }
        catch (Exception e)
        {
            if (_errors++ < 5) Plugin.Log.LogError("Sound view tracking failed: " + e);
        }
    }

    public static bool InControl => Time.frameCount - _lastFrame <= 2 && _vehicle != IntPtr.Zero;

    
    public static bool Inside(Component part)
    {
        if (!InControl || !_scoped || part == null) return false;
        try
        {
            var v = part.GetComponentInParent<Vehicle>();
            return v != null && v.Pointer == _vehicle;
        }
        catch { return false; }
    }

    public static bool OwnVehicle(Component part)
    {
        if (!InControl || part == null) return false;
        try
        {
            var v = part.GetComponentInParent<Vehicle>();
            return v != null && v.Pointer == _vehicle;
        }
        catch { return false; }
    }

    private static AudioListener _found;
    private static float _nextFind;
    private static Vector3 _last;
    private static bool _hasLast;

    
    
    
    public static Vector3 Listener
    {
        get
        {
            try
            {
                var l = GlobalAudioSettings.ActiveAudioListener;
                if (l != null && !l.WasCollected && l) return Remember(l.transform.position);
            }
            catch { }
            try
            {
                if (_found == null || _found.WasCollected || !_found || !_found.isActiveAndEnabled)
                {
                    _found = null;
                    if (Time.unscaledTime >= _nextFind)
                    {
                        _nextFind = Time.unscaledTime + 1f;
                        var any = UnityEngine.Object.FindObjectOfType<AudioListener>();
                        if (any != null && any.isActiveAndEnabled) _found = any;
                    }
                }
                if (_found != null) return Remember(_found.transform.position);
            }
            catch { _found = null; }
            try
            {
                if (_camera != null && !_camera.WasCollected && _camera) return Remember(_camera.transform.position);
                var main = Camera.main;
                if (main != null) return Remember(main.transform.position);
            }
            catch { }
            return _hasLast ? _last : Vector3.zero;
        }
    }

    public static bool HasListener => _hasLast;

    private static Vector3 Remember(Vector3 p)
    {
        _last = p;
        _hasLast = true;
        return p;
    }
}
