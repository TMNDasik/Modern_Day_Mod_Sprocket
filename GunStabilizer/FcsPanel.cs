using System;
using HarmonyLib;
using Properties;
using Sprocket.UI;
using Sprocket.VehicleDesigner.Modules;
using Sprocket.Vehicles;
using UnityEngine;
using UnityEngine.Events;

namespace GunStabilizer;


[HarmonyPatch(typeof(VehicleObjectEditor), nameof(VehicleObjectEditor.OnGUI))]
internal static class FcsPanel
{
    private static Il2CppSystem.Collections.Generic.List<string> _generations;

    private static void Postfix(VehicleObjectEditor __instance, IGUILayout layout)
    {
        try
        {
            var target = __instance.target;
            if (target == null || !FcsPart.IsOurs(target)) return;
            var e = FcsRegistry.Ensure(target);
            var drawer = layout.TryCast<IGUIElementDrawer>();
            if (e == null || drawer == null) return;
            Draw(__instance, drawer, target, e.S);
        }
        catch (Exception ex) { Plugin.Log.LogError("Fire control computer panel failed: " + ex); }
    }

    private static void Draw(VehicleObjectEditor editor, IGUIElementDrawer drawer, VehicleObject target, FcsSettings s)
    {
        if (_generations == null)
        {
            _generations = new Il2CppSystem.Collections.Generic.List<string>();
            _generations.Add("Analog (1970s)");
            _generations.Add("Digital (1980s+)");
        }
        drawer.Header("Fire Control Computer");
        UnityAction<int> onGen = (Action<int>)(i => Set(editor, target, x => x.Gen = i + 1));
        drawer.Dropdown("Generation", _generations.Cast<Il2CppSystem.Collections.Generic.IReadOnlyList<string>>(), s.Gen - 1, onGen,
            "How the rangefinder and the ballistic computer work");
        drawer.InfoField(s.Summary, 4);

        Toggle(editor, drawer, target, "Laser rangefinder", s.Laser, (x, v) => x.Laser = v,
            "The laser button (middle mouse button, Thermal Sight) measures the range to the target under the reticle. " +
            "Off = no laser: set the range by hand with the gun convergence keys.");
        if (s.Laser)
        {
            drawer.Float(Prop(target, "Max range", () => FcsRegistry.Ensure(target)?.S.MaxRange ?? 5000f, (x, v) => x.MaxRange = Mathf.Round(v / 100f) * 100f,
                FcsSettings.MaxRangeMin, FcsSettings.MaxRangeMax, 500f, "0' m'", "Beyond this the laser gets no return (NO RANGE)."));
            Toggle(editor, drawer, target, "Last return", s.LastReturn, (x, v) => x.LastReturn = v,
                "The beam spreads a little: off = the nearest return (first echo), on = the farthest one (last echo), " +
                "which ranges through a bush or a branch in front of the target — but past the edge of a target it ranges the ground behind.");
        }
        Toggle(editor, drawer, target, "Automatic correction", s.Auto, (x, v) => x.Auto = v,
            $"FCS mode ({Plugin.FcsKey}) lays the gun on the computed angle. Off = the computer only shows the correction in mils " +
            "under the reticle and the gunner lays it by hand.");
        Toggle(editor, drawer, target, "Lead and own motion", s.Lead, (x, v) => x.Lead = v,
            "Keep the reticle on a moving target for a second and lase: the computer takes how fast you were tracking it (this also " +
            "covers your own movement) and leads it sideways until the next measurement. A mark in the sight shows where the target " +
            "will be when the shell arrives. Stop tracking and the lead is dropped.");
        Toggle(editor, drawer, target, "Fire only when laid", s.Interlock, (x, v) => x.Interlock = v,
            "In FCS mode the gun holds fire until it is on the computed angle (READY).");
        Toggle(editor, drawer, target, "Can be damaged", s.Damageable, (x, v) => x.Damageable = v,
            "Hits and spall near the computer make it inaccurate, then knock it out (FCS FAIL: the range is still measured, " +
            "the gun is laid by hand).");
    }

    private static void Set(VehicleObjectEditor editor, VehicleObject target, Action<FcsSettings> change)
    {
        try
        {
            FcsRegistry.Edit(target, change);
            editor.RequestRedraw();
        }
        catch (Exception e) { Plugin.Log.LogError("Fire control computer setting failed: " + e); }
    }

    private static void Toggle(VehicleObjectEditor editor, IGUIElementDrawer drawer, VehicleObject target, string name, bool value,
        Action<FcsSettings, bool> set, string tooltip)
    {
        Il2CppSystem.Action<bool> onToggle = (Action<bool>)(v => Set(editor, target, x => set(x, v)));
        drawer.ToggleField(name, value, onToggle, tooltip);
    }

    private static FloatProperty Prop(VehicleObject target, string name, Func<float> get, Action<FcsSettings, float> set,
        float min, float max, float step, string format, string tooltip)
    {
        Il2CppSystem.Func<float> g = (Func<float>)(() =>
        {
            try { return get(); }
            catch { return min; }
        });
        Il2CppSystem.Action<float> s = (Action<float>)(v =>
        {
            try { FcsRegistry.Edit(target, x => set(x, v)); }
            catch (Exception e) { Plugin.Log.LogError($"Fire control computer setting '{name}' failed: " + e); }
        });
        return new FloatProperty(name, g, s, min, max, step, format, null, tooltip);
    }
}
