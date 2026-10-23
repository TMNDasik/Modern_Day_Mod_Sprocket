using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using Sprocket.Cannons.UI;
using Sprocket.Vehicles.UI;
using Sprocket.WeaponFramework;

namespace Munitions;






internal static class ReloadDisplay
{
    private static readonly Dictionary<IntPtr, Il2CppSystem.Collections.Generic.IReadOnlyList<ILoadable>> Loaders = new();
    private static int _errors;
    private static bool _shown;

    [HarmonyPatch(typeof(LoadProgressDisplay), nameof(LoadProgressDisplay.SetLoaders))]
    private static class SetLoadersPatch
    {
        private static void Postfix(LoadProgressDisplay __instance, Il2CppSystem.Collections.Generic.IReadOnlyList<ILoadable> loadables)
        {
            try
            {
                if (Loaders.Count > 16) Loaders.Clear();
                Loaders[__instance.Pointer] = loadables;
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(LoadProgressDisplay), nameof(LoadProgressDisplay.UpdateDisplay))]
    private static class MainPatch
    {
        private static void Postfix(LoadProgressDisplay __instance)
        {
            try
            {
                var cannon = PlayerTracker.InControl ? PlayerTracker.Cannon : null;
                if (!Autoloader.CycleProgress(cannon, out var remaining, out var fraction) &&
                    !Autoloader.CycleProgress(MainTask(__instance), out remaining, out fraction)) return;
                var text = __instance.mainTimerDisplay;
                if (text != null) text.text = remaining.ToString("0.0", CultureInfo.CurrentCulture);
                var bar = __instance.mainReloadBar;
                if (bar != null) bar.fillAmount = fraction;
                if (!_shown)
                {
                    _shown = true;
                    Plugin.Log.LogInfo($"Reload indicator: loader cycle shown in the sight ({remaining:0.0} s left)");
                }
            }
            catch (Exception e)
            {
                if (_errors++ < 3) Plugin.Log.LogWarning("Reload indicator (sight) failed: " + e.Message);
            }
        }
    }

    
    private static IntPtr MainTask(LoadProgressDisplay display)
    {
        try
        {
            if (!Loaders.TryGetValue(display.Pointer, out var list) || list == null) return IntPtr.Zero;
            var index = display.mainLoaderIndex;
            if (index < 0) return IntPtr.Zero;
            var task = list[index]?.LoadTask;
            return task != null ? task.Pointer : IntPtr.Zero;
        }
        catch { return IntPtr.Zero; }
    }

    [HarmonyPatch(typeof(LoadIndicator), nameof(LoadIndicator.UpdateLoadProgress))]
    private static class IndicatorPatch
    {
        private static void Postfix(LoadIndicator __instance)
        {
            try
            {
                var task = __instance.task;
                if (task == null || !Autoloader.CycleProgress(task.Pointer, out var remaining, out var fraction)) return;
                var text = __instance.loadTimerDisplay;
                if (text != null) text.text = remaining.ToString("0.0", CultureInfo.CurrentCulture);
                var bar = __instance.ProgressBarOverride ?? __instance.loadProgressBar;
                if (bar != null) bar.fillAmount = fraction;
            }
            catch (Exception e)
            {
                if (_errors++ < 3) Plugin.Log.LogWarning("Reload indicator (weapon) failed: " + e.Message);
            }
        }
    }
}
