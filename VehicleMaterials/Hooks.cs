using System;
using HarmonyLib;
using Operations;
using Sprocket.MeshEditing;
using Sprocket.VehicleDesigner;
using Sprocket.Vehicles;
using Sprocket.Vehicles.PlateStructures;
using Sprocket.Vehicles.PlateStructures.Design;
using UnityEngine;

namespace VehicleMaterials;



internal static class Hooks
{
    private static VehicleEditor _editor;
    private static int _lastFrame = -100;
    public static MeshEditor MeshEditor;

    [HarmonyPatch(typeof(VehicleEditor), nameof(VehicleEditor.EditorUpdate))]
    private static class EditorHook
    {
        private static void Postfix(VehicleEditor __instance)
        {
            _editor = __instance;
            _lastFrame = Time.frameCount;
        }
    }

    [HarmonyPatch(typeof(PlateStructureEditor), "OnGUI")]
    private static class StructureEditorHook
    {
        private static void Postfix(PlateStructureEditor __instance)
        {
            try { MeshEditor = __instance.MeshEditor; }
            catch { }
        }
    }

    [HarmonyPatch(typeof(PlateStructureMesh), nameof(PlateStructureMesh.ExecuteRebuild))]
    private static class RebuildHook
    {
        private static void Postfix(PlateStructureMesh __instance)
        {
            try { Applier.MeshRebuilt(__instance.Pointer); }
            catch { }
        }
    }

    public static bool Active => _editor != null && Time.frameCount - _lastFrame <= 3;

    public static IOperatable Executer
    {
        get
        {
            if (!Active) return null;
            try { return _editor.OperationExecuter; }
            catch { return null; }
        }
    }

    public static bool IsSelected(VehicleObject vo)
    {
        if (vo == null || !Active) return false;
        try
        {
            var reader = _editor.SelectionReader;
            if (reader == null) return false;
            var count = reader.Count;
            var items = reader.Items;
            if (items == null) return false;
            for (var i = 0; i < count; i++)
            {
                var c = items[i];
                var o = c != null ? c.VehicleObject : null;
                if (o != null && o.Pointer == vo.Pointer) return true;
            }
        }
        catch (Exception e)
        {
            if (Plugin.DebugLogging.Value) Plugin.Log.LogWarning("Selection read failed: " + e.Message);
        }
        return false;
    }
}
