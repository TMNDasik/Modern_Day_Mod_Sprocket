using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using HarmonyLib;
using Sprocket.Blueprints;
using Sprocket.Vehicles.Engines;
using Sprocket.Vehicles.Transmissions;
using Sprocket.VehicleControl;
using UnityEngine;

namespace Powertrain;


[HarmonyPatch(typeof(VehicleControlPlayerState), nameof(VehicleControlPlayerState.InternalUpdate))]
internal static class PlayerTracker
{
    private static GameObject _vehicleGo;
    private static IntPtr _vehicle;
    private static int _lastFrame = -100;
    private static int _errors;

    private static IntPtr _scanned;
    private static float _scanTime = -100f;
    private static float _lastInControl = -100f;

    public static readonly HashSet<IntPtr> Engines = new();
    public static IntPtr Transmission;    
    public static bool Manual;
    public static GearSelector Current;   

    private static readonly Dictionary<IntPtr, GearSelector> Selectors = new();

    public static bool InControl => Time.frameCount - _lastFrame <= 2 && _vehicle != IntPtr.Zero;
    public static GameObject VehicleGo => InControl ? _vehicleGo : null;
    public static IntPtr Vehicle => InControl ? _vehicle : IntPtr.Zero;

    private static void Prefix(VehicleControlPlayerState __instance)
    {
        try
        {
            _lastFrame = Time.frameCount;
            var vc = __instance.vehicleController;
            var go = vc != null ? vc.ControlledVehicleGameObject : null;
            _vehicleGo = go;
            _vehicle = go != null ? go.Pointer : IntPtr.Zero;
        }
        catch (Exception e)
        {
            if (_errors++ < 5) Plugin.Log.LogError("Player tracking failed: " + e);
        }
    }

    
    
    public static unsafe void Refresh()
    {
        var now = Time.unscaledTime;
        if (!InControl)
        {
            Current = null;
            return;
        }
        var regained = now - _lastInControl > 0.5f;
        _lastInControl = now;
        if (!regained && _scanned == _vehicle && now - _scanTime < 1f) return;
        var newVehicle = _scanned != _vehicle;
        _scanned = _vehicle;
        _scanTime = now;
        Engines.Clear();
        Transmission = IntPtr.Zero;
        Manual = false;
        try
        {
            if (_vehicleGo == null || _vehicleGo.WasCollected) { Current = null; return; }
            foreach (var ce in _vehicleGo.GetComponentsInChildren<CombustionEngine>(true))
            {
                var beh = ce != null ? ce.behaviour : null;
                if (beh != null && beh.Pointer != IntPtr.Zero) Engines.Add(beh.Pointer);
            }
            var drive = 1;
            var reverse = 1;
            foreach (var tb in _vehicleGo.GetComponentsInChildren<TransmissionBlock>(true))
            {
                if (tb == null) continue;
                if (GearboxSettings.IsManual(tb.Blueprint)) Manual = true;
                var beh = tb.behaviour;
                if (beh == null || beh.Pointer == IntPtr.Zero || Transmission != IntPtr.Zero) continue;
                Transmission = beh.Pointer;
                var b = (byte*)Transmission;
                drive = *(int*)(b + Gearbox.TransDriveCount);
                reverse = *(int*)(b + Gearbox.TransReverseCount);
            }
            if (Engines.Count == 0 || Transmission == IntPtr.Zero)
            {
                Current = null;
                return;
            }
            if (!Selectors.TryGetValue(_vehicle, out var sel))
            {
                if (Selectors.Count > 64) Selectors.Clear();
                sel = new GearSelector();
                Selectors[_vehicle] = sel;
            }
            sel.Configure(Manual, drive, reverse);
            if (regained || newVehicle || sel.TransmissionPtr != Transmission)
            {
                sel.TransmissionPtr = Transmission;
                sel.SyncPending = true;
            }
            Current = sel;
        }
        catch (Exception e)
        {
            Current = null;
            if (_errors++ < 5) Plugin.Log.LogError("Vehicle scan failed: " + e);
        }
    }
}


internal static class GearboxSettings
{
    private static readonly Regex Token = new(@"\s?\[gearbox:manual\]", RegexOptions.Compiled);

    public static bool IsManual(Blueprint bp)
    {
        try
        {
            var d = bp?.Description;
            return !string.IsNullOrEmpty(d) && Token.IsMatch(d);
        }
        catch { return false; }
    }

    public static string With(string description, bool manual)
    {
        var d = Token.Replace(description ?? "", "");
        if (!manual) return d.Length == 0 ? null : d;
        return d.Length == 0 ? "[gearbox:manual]" : d + " [gearbox:manual]";
    }
}
