using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Sprocket.UI;
using Sprocket.VehicleDesigner;
using Sprocket.VehicleDesigner.UI;
using Sprocket.Vehicles;
using Sprocket.Vehicles.Overlays;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ModernDay;







internal static class Visibility
{
    public static bool Shown = true;

    private static InteriorOverlayConfig _config;
    private static int _editorFrame = -100;
    private static readonly Dictionary<IntPtr, Renderer> Hidden = new();

    private static bool DesignerActive => Time.frameCount - _editorFrame <= 3;

    [HarmonyPatch(typeof(VehicleEditor), nameof(VehicleEditor.EditorUpdate))]
    private static class EditorHook
    {
        private static void Postfix() => _editorFrame = Time.frameCount;
    }

    [HarmonyPatch(typeof(InteriorOverlayConfig), nameof(InteriorOverlayConfig.Draw))]
    private static class ConfigHook
    {
        private static void Prefix(InteriorOverlayConfig __instance) => _config = __instance;
    }

    
    [HarmonyPatch(typeof(VehiclePartFilterProperty), nameof(VehiclePartFilterProperty.DrawFilterToggle))]
    private static class TogglePatch
    {
        private static void Postfix(IGUILayout drawer, string name)
        {
            if (name != "Other") return;
            try
            {
                var d = drawer?.TryCast<IGUIElementDrawer>();
                if (d == null) return;
                Il2CppSystem.Action<bool> onToggle = (Action<bool>)Set;
                d.ToggleField("Modern Day", Shown, onToggle,
                    "Parts from the Modern Day section: APS and EO modules, ERA blocks, blow-off panels, thermal sights " +
                    "and ATGM launchers.");
                Il2CppSystem.Action<bool> onReveal = (Action<bool>)SetReveal;
                d.ToggleField("Hidden parts", RevealHidden, onReveal,
                    "Shows the parts marked Hidden (right panel) while you work in the designer, so you can select and edit them. " +
                    "In test drive and battle they stay hidden.");
            }
            catch (Exception e) { Plugin.Log.LogWarning("Modern Day visibility toggle failed: " + e.Message); }
        }
    }

    
    [HarmonyPatch]
    private static class ButtonsPatch
    {
        private static IEnumerable<MethodBase> TargetMethods() =>
            typeof(VehiclePartFilterProperty).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(m => m.Name.Contains("Draw_b__6_") && m.GetParameters().Length == 0 && m.ReturnType == typeof(void));

        private static void Prefix(VehiclePartFilterProperty __instance, out int __state)
        {
            __state = -1;
            try { __state = (int)__instance.Filter; } catch { }
        }

        private static void Postfix(VehiclePartFilterProperty __instance, int __state)
        {
            if (__state < 0) return;
            try
            {
                var now = (int)__instance.Filter;
                var all = (int)VehiclePartType.All;
                if (now == 0) Set(false);
                else if (now == all || now == (int)__instance.DefaultFilter) Set(true);
                else if (now == (~__state & all)) Set(!Shown);
            }
            catch { }
        }
    }

    
    
    
    
    
    
    private static readonly Dictionary<IntPtr, (IntPtr Renderer, bool Modern)> ModernRenderers = new();
    private static int _overlayErrors;

    [HarmonyPatch(typeof(VehicleRenderer), nameof(VehicleRenderer.ApplyOverlay))]
    private static class OverlayPatch
    {
        private static void Postfix(VehicleRenderer __instance, VehicleOverlay overlay)
        {
            try
            {
                if (overlay == null) return;
                
                _lastApplied = IsInterior(overlay);
                _nextMode = 0f;
                if (!IsModern(__instance)) return;
                __instance.overlayMaterial = null;
                __instance.overlayMaterialPropertyBlock = null;
                __instance.forceNoShadows = false;
                __instance.SetHideFlag(VehicleRendererHideFlags.Overlay, false);
                __instance.UpdateVisibility();
                __instance.UpdateShadowing();
                __instance.ApplyMaterial();
            }
            catch (Exception e)
            {
                if (_overlayErrors++ < 3) Plugin.Log.LogWarning("Modern Day overlay fix failed: " + e.Message);
            }
        }
    }

    
    
    
    private static bool _interiorMode;
    private static float _nextMode;
    private static readonly Dictionary<IntPtr, bool> InteriorOverlays = new();

    
    public static bool EffectiveShown => Shown || !_interiorMode || !DesignerActive;

    private static bool IsInterior(VehicleOverlay overlay)
    {
        if (InteriorOverlays.TryGetValue(overlay.Pointer, out var known)) return known;
        bool interior;
        try { interior = overlay.TryCast<InteriorOverlay>() != null; }
        catch { interior = false; }
        if (InteriorOverlays.Count > 64) InteriorOverlays.Clear();
        InteriorOverlays[overlay.Pointer] = interior;
        return interior;
    }

    
    
    private static bool _lastApplied;
    private static bool _modeLogged, _modeLoggedValue;
    private static VehicleRendererGroup _probe;

    private static void UpdateMode(float now)
    {
        if (now < _nextMode) return;
        _nextMode = now + 0.25f;
        var interior = false;
        if (DesignerActive)
        {
            interior = _lastApplied;
            var found = false;
            
            try
            {
                var lods = _probe != null && !_probe.WasCollected && _probe ? _probe.lods : null;
                var ov = lods != null && lods.Length > 0 ? lods[0]?.appliedOverlay : null;
                if (ov != null)
                {
                    interior = IsInterior(ov);
                    found = true;
                }
            }
            catch { }
            if (!found) RefreshVehicles(now);
            foreach (var v in found ? Array.Empty<Vehicle>() : _vehicles)
            {
                if (found) break;
                if (v == null || v.WasCollected || !v) continue;
                try { if (v.BehaviourEnabled) continue; } catch { continue; }
                try
                {
                    foreach (var g in v.GetComponentsInChildren<VehicleRendererGroup>(true))
                    {
                        var lods = g != null ? g.lods : null;
                        var ov = lods != null && lods.Length > 0 ? lods[0]?.appliedOverlay : null;
                        if (ov == null) continue;
                        interior = IsInterior(ov);
                        _probe = g;
                        found = true;
                        break;
                    }
                }
                catch { }
            }
        }
        _interiorMode = interior;
        if (!_modeLogged || _modeLoggedValue != interior)
        {
            _modeLogged = true;
            _modeLoggedValue = interior;
            Plugin.Log.LogInfo(interior ? "Modern Day: Visibility view — the Modern Day box applies" : "Modern Day: normal view — mod parts always shown");
        }
    }

    private static void RefreshVehicles(float now)
    {
        if (now < _nextVehicles) return;
        _nextVehicles = now + 1f;
        try { _vehicles = Object.FindObjectsOfType<Vehicle>().ToArray(); }
        catch { _vehicles = Array.Empty<Vehicle>(); }
    }

    private static bool IsModern(VehicleRenderer vr)
    {
        var r = vr.Renderer;
        if (r == null) return false;
        if (ModernRenderers.TryGetValue(vr.Pointer, out var known) && known.Renderer == r.Pointer) return known.Modern;
        var vo = r.GetComponentInParent<VehicleObject>();
        var modern = vo != null && Parts.IsModern(vo.guid);
        if (ModernRenderers.Count > 20000) ModernRenderers.Clear();
        ModernRenderers[vr.Pointer] = (r.Pointer, modern);
        return modern;
    }

    
    public static bool RevealHidden;

    private static void SetReveal(bool on)
    {
        if (RevealHidden == on) return;
        RevealHidden = on;
        Plugin.Log.LogInfo(on ? "Hidden parts shown in the designer" : "Hidden parts hidden in the designer");
    }

    private static void Set(bool shown)
    {
        if (Shown == shown) return;
        Shown = shown;
        _next = 0f;
        Plugin.Log.LogInfo(shown ? "Modern Day parts shown" : "Modern Day parts hidden");
    }

    

    private static float _next;
    private static Vehicle[] _vehicles = Array.Empty<Vehicle>();
    private static float _nextVehicles;

    public static void Tick()
    {
        var now = Time.unscaledTime;
        UpdateMode(now);
        
        var hide = !Shown && DesignerActive && _interiorMode;
        if (!hide)
        {
            if (Hidden.Count > 0) Restore();
            return;
        }
        if (now < _next) return;
        _next = now + 0.25f;
        try
        {
            RefreshVehicles(now);
            foreach (var v in _vehicles)
            {
                if (v == null || v.WasCollected || !v) continue;
                try { if (v.BehaviourEnabled) continue; } catch { continue; }
                foreach (var vo in v.GetComponentsInChildren<VehicleObject>(true))
                {
                    if (vo == null || !Parts.IsModern(vo.guid)) continue;
                    foreach (var r in vo.GetComponentsInChildren<Renderer>(true))
                    {
                        if (r == null || r.forceRenderingOff) continue;
                        
                        var owner = r.GetComponentInParent<VehicleObject>();
                        if (owner == null || owner.Pointer != vo.Pointer && !Parts.IsModern(owner.guid)) continue;
                        r.forceRenderingOff = true;
                        Hidden[r.Pointer] = r;
                    }
                }
            }
        }
        catch (Exception e) { Plugin.Log.LogWarning("Hiding Modern Day parts failed: " + e.Message); }
    }

    private static void Restore()
    {
        foreach (var r in Hidden.Values)
            try { if (r != null && !r.WasCollected && r) r.forceRenderingOff = false; } catch { }
        Hidden.Clear();
        
        
        if (_interiorMode)
            try { if (_config != null && !_config.WasCollected) _config.ApplyFilterChanges(); } catch { }
    }
}

public class Ticker : MonoBehaviour
{
    public Ticker(IntPtr ptr) : base(ptr) { }

    private void Update()
    {
        try { Visibility.Tick(); }
        catch { }
        try { HideInBattle.Tick(); }
        catch { }
    }
}
