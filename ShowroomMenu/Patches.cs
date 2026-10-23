using System;
using HarmonyLib;
using UnityEngine;

namespace ShowroomMenu;


[HarmonyPatch(typeof(Sprocket.MainMenu), nameof(Sprocket.MainMenu.SceneUpdate))]
internal static class SceneUpdatePatch
{
    private static void Postfix(Sprocket.MainMenu __instance) => Director.Tick(__instance);
}





internal static class MenuScreen
{
    public static bool Main = true;
}

[HarmonyPatch(typeof(Sprocket.MainMenu), nameof(Sprocket.MainMenu.DrawMainMenu))]
internal static class DrawMainMenuPatch
{
    private static void Postfix() => MenuScreen.Main = true;
}

[HarmonyPatch(typeof(Sprocket.MainMenu), nameof(Sprocket.MainMenu.SetActiveMenu))]
internal static class SetActiveMenuPatch
{
    private static void Prefix(Sprocket.MainMenuScreen menu)
    {
        if (menu != null) MenuScreen.Main = false;
    }
}


[HarmonyPatch(typeof(Sprocket.MainMenu), nameof(Sprocket.MainMenu.Release))]
internal static class ReleasePatch
{
    private static void Prefix() => Director.Shutdown("main menu released");
}


public class Runner : MonoBehaviour
{
    public Runner(IntPtr ptr) : base(ptr) { }

    private int _errors;

    private void Update()
    {
        try { Director.Background(); }
        catch (Exception e)
        {
            if (_errors++ < 5) Plugin.Log.LogError("Background cleanup failed: " + e);
        }
    }
}
