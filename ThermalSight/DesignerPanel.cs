using System;
using HarmonyLib;
using Properties;
using Sprocket.UI;
using Sprocket.VehicleDesigner.Modules;
using Sprocket.Vehicles;
using UnityEngine.Events;

namespace ThermalSight;


[HarmonyPatch(typeof(VehicleObjectEditor), nameof(VehicleObjectEditor.OnGUI))]
internal static class DesignerPanel
{
    private static Il2CppSystem.Collections.Generic.List<string> _options;

    private static void Postfix(VehicleObjectEditor __instance, IGUILayout layout)
    {
        try
        {
            var target = __instance.target;
            if (target == null || !ThermalParts.TryDecode(target.guid, out var config)) return;

            var drawer = layout.TryCast<IGUIElementDrawer>();
            if (drawer == null)
            {
                Plugin.Log.LogWarning("IGUILayout is not an IGUIElementDrawer, cannot draw thermal sight settings");
                return;
            }

            if (_options == null)
            {
                _options = new Il2CppSystem.Collections.Generic.List<string>();
                foreach (var g in GenerationProfile.All) _options.Add(g.Label);
            }

            var editor = __instance;
            UnityAction<int> onGen = (Action<int>)(index =>
            {
                try
                {
                    ThermalParts.Apply(target, Current(target).WithGen(index + 1));
                    editor.RequestRedraw();
                }
                catch (Exception e) { Plugin.Log.LogError("Generation change failed: " + e); }
            });

            drawer.Header("Thermal Imager");
            drawer.Dropdown("Generation", _options.Cast<Il2CppSystem.Collections.Generic.IReadOnlyList<string>>(),
                config.Gen - 1, onGen, "Sensor generation of the thermal imager");
            drawer.InfoField(GenerationProfile.Get(config.Gen).Summary, 4);

            
            
            var optical = new FloatProperty("Optical zoom (max)", config.Optical, 0f, ThermalParts.MaxZoom, 1f, "0", null,
                "Maximum optical magnification (0 = no optical zoom). Used first when zooming in.");
            var digital = new FloatProperty("Digital zoom (max)", config.Digital, 0f, ThermalParts.MaxZoom, 1f, "0", null,
                "Additional digital magnification on top of the optics (0 = no digital zoom). Softer image, more visible noise.");

            var syncing = false;
            Property<float>.ValueChange onOptical = (Action<Il2CppSystem.Object, float>)((_, v) =>
            {
                if (syncing) return;
                syncing = true;
                try { SetZoom(target, (int)Math.Round(v), null, digital); }
                finally { syncing = false; }
            });
            Property<float>.ValueChange onDigital = (Action<Il2CppSystem.Object, float>)((_, v) =>
            {
                if (syncing) return;
                syncing = true;
                try { SetZoom(target, null, (int)Math.Round(v), optical); }
                finally { syncing = false; }
            });
            optical.add_ValueChanged(onOptical);
            digital.add_ValueChanged(onDigital);

            drawer.Float(optical);
            drawer.Float(digital);
            drawer.InfoField(
                $"Optical + digital cannot exceed x{ThermalParts.MaxZoom}. The mouse wheel zooms with the optics first, " +
                "then continues with digital zoom.", 3);
        }
        catch (Exception e) { Plugin.Log.LogError("Designer panel failed: " + e); }
    }

    private static SightConfig Current(VehicleObject target) =>
        ThermalParts.TryDecode(target.guid, out var c) ? c : new SightConfig(1, ThermalParts.DefaultOptical, ThermalParts.DefaultDigital);

    private static void SetZoom(VehicleObject target, int? newOptical, int? newDigital, FloatProperty other)
    {
        try
        {
            var c = Current(target);
            var o = newOptical ?? c.Optical;
            var d = newDigital ?? c.Digital;
            o = Math.Clamp(o, 0, ThermalParts.MaxZoom);
            d = Math.Clamp(d, 0, ThermalParts.MaxZoom);
            
            if (o + d > ThermalParts.MaxZoom)
            {
                if (newOptical.HasValue) d = ThermalParts.MaxZoom - o;
                else o = ThermalParts.MaxZoom - d;
            }
            if (o == c.Optical && d == c.Digital) return;
            ThermalParts.Apply(target, new SightConfig(c.Gen, o, d));
            
            var otherValue = newOptical.HasValue ? d : o;
            if (other != null && Math.Abs(other.Value - otherValue) > 0.01f) other.Value = otherValue;
        }
        catch (Exception e) { Plugin.Log.LogError("Zoom limit change failed: " + e); }
    }
}
