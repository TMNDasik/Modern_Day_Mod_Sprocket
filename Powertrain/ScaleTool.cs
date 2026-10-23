using System;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Sprocket.PartImporting;
using Sprocket.Placements;
using Sprocket.VehicleDesigner;
using Sprocket.Vehicles;
using Sprocket.Vehicles.Engines;
using Sprocket.Vehicles.Transmissions;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Powertrain;




internal static class ScaleTool
{
    public const string EngineGuid = "ebcf5eda-e50b-4f18-a063-0138a9d630e0";
    private static readonly string[] Guids =
    {
        EngineGuid,
        "7a508b04-0d69-40d3-9f4c-92c02346b9de", 
        "6b417b04-0d69-40d3-9f4c-92c02346b9de"  
    };

    private static bool _defsDone;
    private static float _nextSearch;
    private static int _editorFrame = -100;

    public static bool DesignerActive => Time.frameCount - _editorFrame <= 3;

    private static bool IsOurs(string guid) => Array.IndexOf(Guids, guid) >= 0;

    
    public static void EnsureDefinitions()
    {
        if (_defsDone || Time.unscaledTime < _nextSearch) return;
        _nextSearch = Time.unscaledTime + 1f;
        try
        {
            var loader = Object.FindObjectOfType<Sprocket.Vehicles.PartImporting.PartAssetDatabaseLoader>();
            var db = loader != null ? loader.partDatabase : null;
            if (db?.partDefinitionLookup == null) return;
            var n = 0;
            foreach (var guid in Guids)
            {
                if (!db.partDefinitionLookup.ContainsKey(guid)) continue;
                var t = db.partDefinitionLookup[guid]?.transform;
                if (t == null) continue;
                t.options |= PartOptions.Scaling;
                t.minScale = Arr(PartScale.Min);
                t.maxScale = Arr(PartScale.Max);
                n++;
            }
            _defsDone = true;
            Plugin.Log.LogInfo($"Scale tool unlocked for engines and gearboxes ({n} part types)");
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("Unlocking the scale tool failed: " + e.Message);
            _nextSearch = Time.unscaledTime + 30f;
        }
    }

    private static Il2CppStructArray<float> Arr(float v)
    {
        var a = new Il2CppStructArray<float>(3);
        a[0] = v; a[1] = v; a[2] = v;
        return a;
    }

    
    [HarmonyPatch(typeof(VehicleObject), nameof(VehicleObject.Initiate))]
    private static class InitiatePatch
    {
        private static void Postfix(VehicleObject __instance)
        {
            try
            {
                if (!IsOurs(__instance.guid)) return;
                var vt = __instance.VehicleTransform;
                if (vt == null) return;
                vt.Options |= PlacementOptions.AllowScale;
                vt.MinScale = Vector3.one * PartScale.Min;
                vt.MaxScale = Vector3.one * PartScale.Max;
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(VehicleEditor), nameof(VehicleEditor.EditorUpdate))]
    private static class EditorHook
    {
        private static void Postfix() => _editorFrame = Time.frameCount;
    }

    
    [HarmonyPatch(typeof(VehicleTransform), nameof(VehicleTransform.OnTransformChanged))]
    private static class ChangedPatch
    {
        private static void Postfix(VehicleTransform __instance, TransformChangeType mode)
        {
            if ((mode & TransformChangeType.Scale) == 0 || !DesignerActive) return;
            try
            {
                var vo = __instance.VehicleObject;
                if (vo == null || !IsOurs(vo.guid)) return;
                foreach (var b in vo.behaviours)
                {
                    if (b == null) continue;
                    if (b.TryCast<CombustionEngine>() != null || b.TryCast<TransmissionBlock>() != null) b.RequestRebuild();
                }
            }
            catch (Exception e) { if (Plugin.DebugLogging.Value) Plugin.Log.LogWarning("Scale rebuild failed: " + e.Message); }
        }
    }

    
    public static Vector3 Of(VehicleComponent c)
    {
        try
        {
            var vt = c?.VehicleTransform;
            if (vt == null) return Vector3.one;
            var s = vt.scale;
            s = new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
            if (s.x < 0.01f || s.y < 0.01f || s.z < 0.01f || float.IsNaN(s.x)) return Vector3.one;
            return s;
        }
        catch { return Vector3.one; }
    }
}
