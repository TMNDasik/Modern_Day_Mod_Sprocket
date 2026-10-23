using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ThermalSight;






internal static class DynamicSightCompat
{
    private const string AssemblyName = "SprocketDynamicSight";
    private const string ClearOverlayName = "DynamicSightClearOverlay";
    private const float RangeHold = 120f;   

    private static bool _found, _failed;
    private static float _nextSearch;
    private static FieldInfo _zoomActive;
    private static IntPtr _clearTex, _otherTex;
    private static int _errors;

    
    public static bool ZoomActive
    {
        get
        {
            if (_zoomActive == null) return false;
            try { return _zoomActive.GetValue(null) is true; }
            catch { return false; }
        }
    }

    
    public static void Tick()
    {
        if (_found || _failed || Time.unscaledTime < _nextSearch) return;
        _nextSearch = Time.unscaledTime + 3f;
        Assembly asm = null;
        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            if (a.GetName().Name == AssemblyName) { asm = a; break; }
        if (asm == null) return;
        _found = true;
        try
        {
            var zoom = asm.GetType("SprocketDynamicSight.Game.Zoom");
            _zoomActive = zoom != null ? AccessTools.Field(zoom, "Active") : null;
            var reader = asm.GetType("SprocketDynamicSight.Game.GameReader");
            var laser = reader != null ? AccessTools.Method(reader, "TryGetLaserRange") : null;
            if (laser != null)
                new Harmony(Plugin.Guid + ".dynamicsight").Patch(laser,
                    postfix: new HarmonyMethod(typeof(DynamicSightCompat).GetMethod(nameof(LaserRangePostfix), BindingFlags.Static | BindingFlags.NonPublic)));
            Plugin.Log.LogInfo($"Dynamic Gun Sight {asm.GetName().Version} found: its reticle and zoom take over while it draws, " +
                               $"our rangefinder feeds its range readout (zoom {(_zoomActive != null ? "ok" : "not found")}, " +
                               $"range {(laser != null ? "ok" : "not found")})");
        }
        catch (Exception e)
        {
            _failed = true;
            Plugin.Log.LogWarning("Dynamic Gun Sight compatibility unavailable: " + e.Message);
        }
    }

    
    public static bool HidesReticle(Texture overlay)
    {
        if (overlay == null) return false;
        try
        {
            var ptr = overlay.Pointer;
            if (ptr == _clearTex) return true;
            if (ptr == _otherTex) return false;
            if (overlay.name == ClearOverlayName) { _clearTex = ptr; return true; }
            _otherTex = ptr;
        }
        catch { }
        return false;
    }

    
    private static void LaserRangePostfix(ref float __0, ref bool __result)
    {
        try
        {
            if (__result) return;
            if (Api.LastRangeTime < 0f || Api.LastRange <= 0f || Time.time - Api.LastRangeTime > RangeHold || Time.time < Api.LastRangeTime) return;
            __0 = Api.LastRange;
            __result = true;
        }
        catch (Exception e) { if (_errors++ < 3) Plugin.Log.LogWarning("Dynamic Gun Sight range feed failed: " + e.Message); }
    }

    
    public static float Magnification(Camera cam)
    {
        try
        {
            if (cam == null) return 1f;
            var baseFov = Mathf.Clamp(Plugin.BaseFieldOfView.Value, 5f, 120f) * Mathf.Deg2Rad;
            var fov = Mathf.Clamp(cam.fieldOfView, 0.1f, 170f) * Mathf.Deg2Rad;
            return Mathf.Max(1f, Mathf.Tan(baseFov * 0.5f) / Mathf.Tan(fov * 0.5f));
        }
        catch { return 1f; }
    }
}
