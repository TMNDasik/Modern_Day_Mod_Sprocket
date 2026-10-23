using System;
using HarmonyLib;
using Sprocket.UI;
using Sprocket.VehicleDesigner.UI;
using Sprocket.Vehicles.Overlays;

namespace ActiveProtection;



[HarmonyPatch(typeof(VehiclePartFilterProperty), nameof(VehiclePartFilterProperty.DrawFilterToggle))]
internal static class ZonesToggle
{
    private static Il2CppSystem.Action<bool> _onToggle;

    private static void Postfix(IGUILayout drawer, string name)
    {
        if (name != "Other") return;
        try
        {
            var d = drawer?.TryCast<IGUIElementDrawer>();
            if (d == null) return;
            _onToggle ??= (Action<bool>)(v => Plugin.ShowAllZones.Value = v);
            d.ToggleField("APS zones", Plugin.ShowAllZones.Value, _onToggle,
                "Show the zones and links of every APS and EO module at once. Off: only the selected module's zone and its links.");
        }
        catch (Exception e) { Plugin.Log.LogWarning("APS zones toggle failed: " + e.Message); }
    }
}
