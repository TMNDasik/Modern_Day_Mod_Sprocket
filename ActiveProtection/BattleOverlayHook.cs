using System;
using HarmonyLib;
using Sprocket.Vehicles.UI;

namespace ActiveProtection;





internal static class BattleOverlayHook
{
    private static VehicleBattleOverlayController _ctrl;
    private static bool _logged, _loggedValue;

    public static bool Active
    {
        get
        {
            bool on;
            try { on = _ctrl != null && !_ctrl.WasCollected && _ctrl.overlayActive; }
            catch { on = false; }
            if (!_logged || on != _loggedValue)
            {
                _logged = true;
                _loggedValue = on;
                if (_ctrl != null) Plugin.Log.LogInfo(on ? "Damage view (O) open — showing APS / EO zones" : "Damage view (O) closed");
            }
            return on;
        }
    }

    [HarmonyPatch(typeof(VehicleBattleOverlayController), nameof(VehicleBattleOverlayController.ToggleOverlay))]
    private static class TogglePatch
    {
        private static void Postfix(VehicleBattleOverlayController __instance) => _ctrl = __instance;
    }

    [HarmonyPatch(typeof(VehicleBattleOverlayController), nameof(VehicleBattleOverlayController.SetOverlayActive))]
    private static class SetPatch
    {
        private static void Postfix(VehicleBattleOverlayController __instance) => _ctrl = __instance;
    }
}
