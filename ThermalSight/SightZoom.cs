using System;
using HarmonyLib;
using UnityEngine;

namespace ThermalSight;







internal static class SightZoom
{
    internal static bool Smooth => Plugin.SmoothZoom != null && Plugin.SmoothZoom.Value;
    internal static float Step => Smooth ? 0.1f : 1f;

    
    internal static float Next(float current, int dir, float max)
    {
        var step = Step;
        var k = current / step;
        var n = dir > 0 ? (Mathf.Floor(k + 1e-3f) + 1f) * step : (Mathf.Ceil(k - 1e-3f) - 1f) * step;
        return Mathf.Clamp(n, 1f, Mathf.Max(1f, max));
    }

    
    internal static float Snap(float current, float max)
    {
        var step = Step;
        var n = current >= max - 1e-3f ? max : Mathf.Round(current / step) * step;
        return Mathf.Clamp(n, 1f, Mathf.Max(1f, max));
    }

    internal static string Format(float zoom) => Smooth ? $"x{zoom:0.0}" : Mathf.Abs(zoom - Mathf.Round(zoom)) < 0.01f ? $"x{zoom:0}" : $"x{zoom:0.0}";

    private static IntPtr _controller;
    private static float _min, _max, _mag = 1f, _lastFocal = -1f;

    [HarmonyPatch(typeof(Sprocket.ScopeCameraController), nameof(Sprocket.ScopeCameraController.ProcessInputs))]
    private static class WheelPatch
    {
        private static void Prefix(Sprocket.ScopeCameraController __instance, Sprocket.CameraInputProccessingFlags flags)
        {
            try
            {
                
                if (ThermalController.ActiveView != null || DynamicSightCompat.ZoomActive) { _lastFocal = -1f; return; }
                var min = __instance.minZoom;
                var max = __instance.maxZoom;
                if (min <= 0f || max <= min * 1.001f) return;
                var maxMag = max / min;
                var target = __instance.targetFocalLength;
                if (__instance.Pointer != _controller || Math.Abs(min - _min) > 1e-4f || Math.Abs(max - _max) > 1e-4f)
                {
                    _controller = __instance.Pointer;
                    _min = min;
                    _max = max;
                    _mag = Snap(target / min, maxMag);
                }
                
                else if (_lastFocal > 0f && Math.Abs(target - _lastFocal) > 1e-3f && Math.Abs(target - min) < 1e-3f) _mag = 1f;

                var scroll = __instance.scrollValue;
                if (Math.Abs(scroll) > 1e-4f && ((int)flags & 2) != 0)
                {
                    
                    var dir = scroll * (__instance.zoomSensitivity < 0f ? -1f : 1f) > 0f ? 1 : -1;
                    _mag = Next(_mag, dir, maxMag);
                    __instance.scrollValue = 0f;
                }
                var focal = Mathf.Min(min * _mag, max);
                if (Math.Abs(target - focal) > 1e-4f) __instance.targetFocalLength = focal;
                
                if (!Smooth) __instance.appliedFocalLength = focal;
                _lastFocal = focal;
            }
            catch { }
        }
    }
}
