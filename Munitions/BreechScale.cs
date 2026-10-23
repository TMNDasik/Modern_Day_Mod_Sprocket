using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Sprocket.PartImporting;
using Sprocket.Placements;
using Sprocket.UI;
using Sprocket.VehicleDesigner;
using Sprocket.Vehicles;
using Sprocket.Vehicles.Cannons;
using Sprocket.Vehicles.Cannons.Editor;
using UnityEngine;

namespace Munitions;







internal static class BreechScale
{
    public const float Min = 0.3f, Max = 2.5f;
    private static readonly Regex Token = new(@"\[breech:(\d+)\]", RegexOptions.Compiled);

    private static float Legacy(CannonBlueprint bp)
    {
        try
        {
            var m = Token.Match(bp?.Description ?? "");
            if (!m.Success) return 1f;
            return Mathf.Clamp(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) / 100f, Min, Max);
        }
        catch { return 1f; }
    }

    
    private static bool IsGun(string guid)
    {
        if (guid == LauncherPart.CannonGuid) return true;
        foreach (var s in AutoWeaponPart.All) if (s.Guid == guid) return true;
        return false;
    }

    private static Vector3 ToolScale(VehicleComponent c)
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

    
    public static Vector3 Of(Cannon cannon)
    {
        var s = ToolScale(cannon) * Legacy(cannon?.Blueprint);
        return new Vector3(Mathf.Clamp(s.x, Min, Max), Mathf.Clamp(s.y, Min, Max), Mathf.Clamp(s.z, Min, Max));
    }

    

    private static Il2CppStructArray<float> Arr(float v)
    {
        var a = new Il2CppStructArray<float>(3);
        a[0] = v; a[1] = v; a[2] = v;
        return a;
    }

    
    public static void UnlockDefinitions(PartDefinitionDatabase db)
    {
        var n = 0;
        try
        {
            foreach (var guid in new[] { LauncherPart.CannonGuid }.Concat(AutoWeaponPart.All.Select(s => s.Guid)))
            {
                if (!db.partDefinitionLookup.ContainsKey(guid)) continue;
                var t = db.partDefinitionLookup[guid]?.transform;
                if (t == null) continue;
                t.options |= PartOptions.Scaling;
                t.minScale = Arr(Min);
                t.maxScale = Arr(Max);
                n++;
            }
            Plugin.Log.LogInfo($"Scale tool unlocked for gun breeches ({n} part types)");
        }
        catch (Exception e) { Plugin.Log.LogWarning("Unlocking the scale tool for guns failed: " + e.Message); }
    }

    
    [HarmonyPatch(typeof(VehicleObject), nameof(VehicleObject.Initiate))]
    private static class InitiatePatch
    {
        private static void Postfix(VehicleObject __instance)
        {
            try
            {
                if (!IsGun(__instance.guid)) return;
                var vt = __instance.VehicleTransform;
                if (vt == null) return;
                vt.Options |= PlacementOptions.AllowScale;
                vt.MinScale = Vector3.one * Min;
                vt.MaxScale = Vector3.one * Max;
            }
            catch { }
        }
    }

    private static int _editorFrame = -100;

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
            if ((mode & TransformChangeType.Scale) == 0 || Time.frameCount - _editorFrame > 3) return;
            try
            {
                var vo = __instance.VehicleObject;
                if (vo == null || !IsGun(vo.guid)) return;
                foreach (var b in vo.behaviours)
                {
                    var cannon = b != null ? b.TryCast<Cannon>() : null;
                    if (cannon == null) continue;
                    var bp = cannon.Blueprint;
                    if (bp != null) bp.physicalPropertiesDirty = true;
                    cannon.RequestRebuild();
                }
            }
            catch (Exception e) { if (Plugin.DebugLogging.Value) Plugin.Log.LogWarning("Breech rebuild failed: " + e.Message); }
        }
    }

    
    

    [ThreadStatic] private static Vector3 _tool;
    [ThreadStatic] private static int _depth;

    private static void Enter(VehicleComponent c)
    {
        _tool = _depth == 0 ? ToolScale(c) : _tool;
        _depth++;
    }

    private static void Leave()
    {
        if (_depth > 0) _depth--;
    }

    [HarmonyPatch(typeof(Cannon), nameof(Cannon.Build))]
    private static class BuildScope
    {
        private static void Prefix(Cannon __instance) => Enter(__instance);
        private static Exception Finalizer(Exception __exception) { Leave(); return __exception; }
    }

    [HarmonyPatch(typeof(CannonBreech), nameof(CannonBreech.OnCannonBuild))]
    private static class BreechScope
    {
        private static void Prefix(CannonBreech __instance) => Enter(__instance);
        private static Exception Finalizer(Exception __exception) { Leave(); return __exception; }
    }

    [HarmonyPatch(typeof(CannonBlueprint), nameof(CannonBlueprint.RecalculatePhysicalValues))]
    private static class SizePatch
    {
        private static void Postfix(CannonBlueprint __instance)
        {
            try
            {
                if (LauncherPart.IsLauncherBlueprint(__instance)) return;
                var s = (_depth > 0 ? _tool : Vector3.one) * Legacy(__instance);
                s = new Vector3(Mathf.Clamp(s.x, Min, Max), Mathf.Clamp(s.y, Min, Max), Mathf.Clamp(s.z, Min, Max));
                if ((s - Vector3.one).sqrMagnitude < 1e-6f) return;
                __instance.breechSize = Vector3.Scale(__instance.breechSize, s);
            }
            catch (Exception e) { if (Plugin.DebugLogging.Value) Plugin.Log.LogWarning("Breech scale failed: " + e.Message); }
        }
    }

    

    public static void DrawInfo(Cannon cannon, IGUIElementDrawer drawer)
    {
        var s = Of(cannon);
        drawer.InfoField((s - Vector3.one).sqrMagnitude < 1e-6f
            ? "Breech: standard size. Select the gun and use the scale tool to make the breech bigger or smaller."
            : $"Breech: {s.x * 100f:0}% wide, {s.y * 100f:0}% high, {s.z * 100f:0}% long (scale tool). The gun fires the same.", 2);
    }

    
    [HarmonyPatch(typeof(CannonEditor), nameof(CannonEditor.OnGUI))]
    private static class PanelPatch
    {
        private static void Postfix(CannonEditor __instance, IGUILayout layout)
        {
            try
            {
                var cannon = __instance.Component;
                if (cannon == null || LauncherPart.IsLauncher(cannon) || AutoWeaponPart.IsAuto(cannon)) return;
                var drawer = layout.TryCast<IGUIElementDrawer>();
                if (drawer != null) DrawInfo(cannon, drawer);
            }
            catch (Exception e) { if (Plugin.DebugLogging.Value) Plugin.Log.LogWarning("Breech info failed: " + e.Message); }
        }
    }
}
