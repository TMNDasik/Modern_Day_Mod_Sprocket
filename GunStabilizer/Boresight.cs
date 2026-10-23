using System;
using HarmonyLib;
using Sprocket.DamageModelling;
using Sprocket.Vehicles;
using Sprocket.Vehicles.UI;
using Sprocket.Vehicles.Weapons.Cannons;
using UnityEngine;

namespace GunStabilizer;








internal static unsafe class Boresight
{
    private const int PoolOffset = 0x38, NextIdOffset = 0x44, ElemSize = 0x50, ElemBase = 0x20;
    private const int EId = 0x18, EVel = 0x34, EFlags = 0x48;

    private static IntPtr _weapon;                 
    private static Quaternion _correction = Quaternion.identity;
    private static int _frame = -100;
    private static int _errors;

    
    
    
    
    private static bool _renderHooked;
    private static ScopeOverlayVehicleControllerPlugin _overlay;
    private static float _nextOverlaySearch;

    public static void EnsureRenderHook()
    {
        if (_renderHooked) return;
        _renderHooked = true;
        try
        {
            UnityEngine.Events.UnityAction cb = (Action)Render;
            Application.add_onBeforeRender(cb);
        }
        catch (Exception e) { Plugin.Log.LogWarning("Boresight render hook failed: " + e.Message); }
    }

    private static void Render()
    {
        try
        {
            if (!Plugin.Boresight.Value || Time.frameCount - ScopeCarry.LastControlFrame > 2) { _frame = -100; return; }
            var sc = ScopeCarry.Scope;
            if (sc == null || sc.WasCollected) { _frame = -100; return; }
            var point = Update(sc);
            if (point == null) return;
            var overlay = Overlay();
            var cam = ScopeCarry.PlayerCamera;
            if (overlay == null || cam == null || cam.WasCollected) return;
            var scope = overlay.postProcess;
            if (scope == null || scope.WasCollected) return;
            var vp = cam.WorldToViewportPoint(point.Value);
            if (vp.z <= 0f) return;
            scope.centre.value = new Vector2(vp.x, vp.y);
        }
        catch (Exception e)
        {
            _frame = -100;
            if (_errors++ < 5) Plugin.Log.LogWarning("Boresight failed: " + e.Message);
        }
    }

    
    internal static bool ReticleCentre(out Vector2 viewport)
    {
        viewport = new Vector2(0.5f, 0.5f);
        try
        {
            var overlay = Overlay();
            var scope = overlay != null ? overlay.postProcess : null;
            if (scope == null || scope.WasCollected) return false;
            viewport = scope.centre.value;
            return viewport.x > 0f && viewport.x < 1f && viewport.y > 0f && viewport.y < 1f;
        }
        catch { return false; }
    }

    private static ScopeOverlayVehicleControllerPlugin Overlay()
    {
        if (_overlay != null && !_overlay.WasCollected && _overlay) return _overlay;
        _overlay = null;
        if (Time.unscaledTime < _nextOverlaySearch) return null;
        _nextOverlaySearch = Time.unscaledTime + 1f;
        try { _overlay = UnityEngine.Object.FindObjectOfType<ScopeOverlayVehicleControllerPlugin>(); }
        catch { _overlay = null; }
        return _overlay;
    }

    
    private static Vector3? Update(ScopeController sc)
    {
        if (!sc.scoped || sc.activeSight == null) { _frame = -100; return null; } 
        var vc = ScopeCarry.Controller;
        var wt = sc.WeaponTransform;
        if (vc == null || vc.WasCollected || wt == null) { _frame = -100; return null; }

        var ray = vc.aimRay;
        var dir = ray.direction;
        if (dir.sqrMagnitude < 1e-6f) { _frame = -100; return null; }
        dir.Normalize();
        var range = Mathf.Clamp(sc.gunConverganceDistance, 20f, 10000f);
        var point = ray.origin + dir * range;           

        var w = wt.position;
        
        
        var f = FireControl.Lower(wt.forward);
        var g = point - w;
        if (g.sqrMagnitude < 1f) { _frame = -100; return null; }
        g.Normalize();
        var error = Vector3.Angle(f, g);
        
        var bound = Mathf.Asin(Mathf.Clamp01((w - ray.origin).magnitude / range)) * Mathf.Rad2Deg + 0.03f;
        var step = Mathf.Min(error, bound);
        var axis = Vector3.Cross(f, g);
        _correction = step > 1e-4f && axis.sqrMagnitude > 1e-12f ? Quaternion.AngleAxis(step, axis.normalized) : Quaternion.identity;
        _weapon = wt.Pointer;
        _frame = Time.frameCount;
        return w + _correction * f * range;
    }

    

    private static IntPtr _firing;

    [HarmonyPatch(typeof(CannonBehaviour), nameof(CannonBehaviour.FireInternal))]
    private static class FirePatch
    {
        private static void Prefix(CannonBehaviour __instance)
        {
            _firing = IntPtr.Zero;
            try
            {
                if (_weapon == IntPtr.Zero || Time.frameCount - _frame > 3) return;
                var t = __instance.transform;
                if (t == null) return;
                
                if (t.Pointer == _weapon) { _firing = __instance.Pointer; return; }
                var w = new Transform(_weapon);
                if (w.IsChildOf(t) || t.IsChildOf(w)) _firing = __instance.Pointer;
            }
            catch { _firing = IntPtr.Zero; }
        }

        private static void Postfix() => _firing = IntPtr.Zero;
    }

    [HarmonyPatch(typeof(ProjectileRegister), nameof(ProjectileRegister.Launch))]
    private static class LaunchPatch
    {
        private static void Postfix(ProjectileRegister __instance)
        {
            if (_firing == IntPtr.Zero) return;
            try
            {
                var reg = __instance.Pointer;
                var next = *(int*)(reg + NextIdOffset);
                var id = next == 0 ? 0x7FFFFFFE : next - 1;
                var pool = *(IntPtr*)(reg + PoolOffset);
                if (pool == IntPtr.Zero) return;
                var len = *(int*)(pool + 0x18);
                for (var i = len - 1; i >= 0; i--)
                {
                    var e = pool + ElemBase + i * ElemSize;
                    if (*(int*)(e + EId) != id || (*(int*)(e + EFlags) & 1) == 0) continue;
                    var v = *(Vector3*)(e + EVel);
                    *(Vector3*)(e + EVel) = _correction * v;
                    break;
                }
            }
            catch (Exception ex)
            {
                if (_errors++ < 5) Plugin.Log.LogWarning("Boresight launch failed: " + ex.Message);
            }
        }
    }
}
