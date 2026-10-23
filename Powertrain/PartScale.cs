using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using Sprocket.Blueprints;
using Sprocket.UI;
using Sprocket.VehicleDesigner.Access.Editors;
using Sprocket.Vehicles;
using Sprocket.Vehicles.Colliders;
using Sprocket.Vehicles.Engines;
using Sprocket.Vehicles.Powertrains.Layouts;
using Sprocket.Vehicles.Transmissions;
using Sprocket.Vehicles.Transmissions.Editor;
using UnityEngine;

namespace Powertrain;







internal static class PartScale
{
    public const float Min = 0.4f, Max = 2f;
    private static readonly Regex Token = new(@"\s?\[scale:(\d+)\]", RegexOptions.Compiled);

    
    private static float Legacy(Blueprint bp)
    {
        try
        {
            var m = Token.Match(bp?.Description ?? "");
            if (!m.Success) return 1f;
            return Mathf.Clamp(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) / 100f, Min, Max);
        }
        catch { return 1f; }
    }

    
    public static Vector3 Get(Blueprint bp, VehicleComponent part)
    {
        var s = ScaleTool.Of(part) * Legacy(bp);
        return new Vector3(Mathf.Clamp(s.x, Min, Max), Mathf.Clamp(s.y, Min, Max), Mathf.Clamp(s.z, Min, Max));
    }

    private static bool IsOne(Vector3 s) => Mathf.Abs(s.x - 1f) < 0.001f && Mathf.Abs(s.y - 1f) < 0.001f && Mathf.Abs(s.z - 1f) < 0.001f;

    

    
    [ThreadStatic] private static Vector3 _portScale;
    [ThreadStatic] private static bool _inBuild;

    private readonly struct Written
    {
        public readonly Vector3 A, B, BaseA, BaseB;
        public Written(Vector3 a, Vector3 b, Vector3 baseA, Vector3 baseB) { A = a; B = b; BaseA = baseA; BaseB = baseB; }
    }

    private static readonly Dictionary<IntPtr, Written> Colliders = new(), Transforms = new();
    private static int _errors;

    [HarmonyPatch(typeof(CombustionEngine), nameof(CombustionEngine.BuildEngineBlueprint))]
    private static class EngineBuildPatch
    {
        private static void Prefix(CombustionEngine __instance, out Vector3 __state)
        {
            __state = Vector3.one;
            try { __state = Get(__instance.Blueprint, __instance); }
            catch { }
            _portScale = __state;
            _inBuild = true;
            if (!EngineCompat.LegacyEngineModel) ForceRebuild(__instance, __state);
        }

        private static void Postfix(CombustionEngine __instance, Vector3 __state)
        {
            _inBuild = false;
            try
            {
                var s = __state;
                if (EngineCompat.LegacyEngineModel)
                {
                    
                    if (!IsOne(s)) EngineCompat.MultiplySize(__instance, s);
                    ScaleCollider(EngineCompat.Bounds(__instance), s);
                    ScaleTransform(EngineCompat.ModelRoot(__instance), s);
                    if (!IsOne(s)) __instance.centerOfMass_LocalSpace = __instance.CalculateCenterOfMass_LocalSpace();
                }
                else
                {
                    
                    var g = EngineCompat.Generator(__instance);
                    if (g != IntPtr.Zero) GeneratorScale[g] = s;
                    if (!IsOne(s)) __instance.centerOfMass_LocalSpace = Vector3.Scale(__instance.CalculateCenterOfMass_LocalSpace(), s);
                }
            }
            catch (Exception e) { if (_errors++ < 5) Plugin.Log.LogWarning("Engine scale failed: " + e.Message); }
        }

        private static Exception Finalizer(Exception __exception)
        {
            _inBuild = false;
            return __exception;
        }
    }

    
    
    

    private static readonly Dictionary<IntPtr, Vector3> GeneratorScale = new();

    
    private static void ForceRebuild(CombustionEngine engine, Vector3 s)
    {
        try
        {
            var g = EngineCompat.Generator(engine);
            if (g == IntPtr.Zero) return;
            var old = GeneratorScale.TryGetValue(g, out var v) ? v : Vector3.one;
            if ((old - s).sqrMagnitude < 1e-6f) return;
            var model = engine.modelGenerator?.TryCast<CombustionEngineModel>();
            if (model != null) model.builtMeshHash = 0;
        }
        catch { }
    }

    [HarmonyPatch]
    private static class MeshScalePatch
    {
        private static MethodBase Target => EngineCompat.LegacyEngineModel ? null : EngineCompat.Method(typeof(CombustionEngineModel), "BuildMeshes");
        private static bool Prepare() => Target != null;
        private static MethodBase TargetMethod() => Target;

        private static void Prefix(CombustionEngineModel __instance, out Matrix4x4 __state)
        {
            __state = default;
            try
            {
                var s = _inBuild ? _portScale : GeneratorScale.TryGetValue(__instance.Pointer, out var v) ? v : Vector3.one;
                if (IsOne(s)) return;
                __state = __instance.transformMatrix;
                __instance.transformMatrix = Matrix4x4.Scale(s) * __state;
            }
            catch (Exception e) { if (_errors++ < 5) Plugin.Log.LogWarning("Engine mesh scale failed: " + e.Message); }
        }

        
        private static void Postfix(CombustionEngineModel __instance, Matrix4x4 __state)
        {
            try { if (__state != default) __instance.transformMatrix = __state; }
            catch { }
        }
    }

    [HarmonyPatch(typeof(TransmissionBlock), nameof(TransmissionBlock.Build))]
    private static class TransmissionBuildPatch
    {
        private static void Prefix(TransmissionBlock __instance, out Vector3 __state)
        {
            __state = Vector3.one;
            try { __state = Get(__instance.Blueprint, __instance); }
            catch { }
            _portScale = __state;
            _inBuild = true;
        }

        private static void Postfix(TransmissionBlock __instance, Vector3 __state)
        {
            _inBuild = false;
            try
            {
                var s = __state;
                ScaleCollider(__instance.obstructionCollider, s);
                ScaleCollider(__instance.selectionCollider, s);
                ScaleTransform(__instance.model?.TryCast<TransmissionBlockModel>()?.parent, s);
                ScaleTransform(__instance.gearVisualizerModel?.TryCast<TransmissionGearVisualizer>()?.parent, s);
            }
            catch (Exception e) { if (_errors++ < 5) Plugin.Log.LogWarning("Gearbox scale failed: " + e.Message); }
        }

        private static Exception Finalizer(Exception __exception)
        {
            _inBuild = false;
            return __exception;
        }
    }

    
    [HarmonyPatch(typeof(PowertrainComponent), nameof(PowertrainComponent.SetPortTransform))]
    private static class PortPatch
    {
        private static void Prefix(ref Vector3 localPosition)
        {
            if (_inBuild && !IsOne(_portScale)) localPosition = Vector3.Scale(localPosition, _portScale);
        }
    }

    
    private static void ScaleCollider(IVehicleBoxCollider collider, Vector3 s)
    {
        if (collider == null) return;
        var key = collider.Pointer;
        var size = collider.Size;
        var centre = collider.Centre;
        Vector3 baseSize = size, baseCentre = centre;
        if (Colliders.TryGetValue(key, out var w) && size == w.A && centre == w.B)
        {
            baseSize = w.BaseA;
            baseCentre = w.BaseB;
        }
        var newSize = Vector3.Scale(baseSize, s);
        var newCentre = Vector3.Scale(baseCentre, s);
        if (newSize != size || newCentre != centre) collider.Set(newSize, newCentre);
        if (Colliders.Count > 1024) Colliders.Clear();
        Colliders[key] = new Written(newSize, newCentre, baseSize, baseCentre);
    }

    
    private static void ScaleTransform(Transform t, Vector3 s)
    {
        if (t == null) return;
        var key = t.Pointer;
        var pos = t.localPosition;
        var scale = t.localScale;
        Vector3 basePos = pos, baseScale = scale;
        if (Transforms.TryGetValue(key, out var w) && pos == w.A && scale == w.B)
        {
            basePos = w.BaseA;
            baseScale = w.BaseB;
        }
        var newPos = Vector3.Scale(basePos, s);
        var newScale = Vector3.Scale(baseScale, s);
        if (newPos != pos) t.localPosition = newPos;
        if (newScale != scale) t.localScale = newScale;
        if (Transforms.Count > 1024) Transforms.Clear();
        Transforms[key] = new Written(newPos, newScale, basePos, baseScale);
    }

    

    private static void Info(IGUILayout layout, string what, Vector3 s)
    {
        var drawer = layout.TryCast<IGUIElementDrawer>();
        if (drawer == null) return;
        drawer.InfoField(IsOne(s)
            ? $"Size: standard. Resize the {what} with the scale tool (each axis separately); power, mass and cost stay the same."
            : $"Size: {s.x * 100f:0}% wide, {s.y * 100f:0}% high, {s.z * 100f:0}% long (scale tool). Power, mass and cost stay the same.", 2);
    }

    
    [HarmonyPatch]
    private static class EnginePanelPatch
    {
        private static bool Prepare() => EngineCompat.Method(EngineCompat.EditorType, "OnGUI") != null;
        private static MethodBase TargetMethod() => EngineCompat.Method(EngineCompat.EditorType, "OnGUI");

        private static void Postfix(VehicleComponentEditor<CombustionEngine> __instance, IGUILayout layout)
        {
            try
            {
                var engine = __instance.Component;
                if (engine == null) return;
                var bp = EngineCompat.Blueprint(__instance) ?? engine.Blueprint;
                DrawTda(__instance, layout, bp);
                try { FireSuppression.Draw(__instance, layout, bp); }
                catch (Exception e) { if (_errors++ < 5) Plugin.Log.LogWarning("Fire suppression panel failed: " + e.Message); }
                Info(layout, "engine block", Get(bp, engine));
            }
            catch (Exception e) { if (_errors++ < 5) Plugin.Log.LogWarning("Engine size info failed: " + e.Message); }
        }
    }

    
    private static VehicleComponentEditor<CombustionEngine> _tdaEditor;
    private static Il2CppSystem.Action<bool> _onTda;

    private static void DrawTda(VehicleComponentEditor<CombustionEngine> editor, IGUILayout layout, EngineBlueprint bp)
    {
        var drawer = layout.TryCast<IGUIElementDrawer>();
        if (drawer == null || bp == null) return;
        _tdaEditor = editor;
        _onTda ??= (Action<bool>)(v =>
        {
            try
            {
                var ed = _tdaEditor;
                var b = ed != null ? EngineCompat.Blueprint(ed) ?? ed.Component?.Blueprint : null;
                if (b == null || Tda.Has(b) == v) return;
                Il2CppSystem.IDisposable frame = null;
                try { frame = ed.NewBlueprintEditFrame("TDA smoke", b, null, ed.GetNewOperationGroupID(), Sprocket.VehicleDesigner.Access.VehicleOpExecutionMode.Synchronous); }
                catch { }
                try { b.Description = Tda.With(b.Description, v); }
                finally { if (frame != null) frame.Dispose(); else b.MarkModified(); }
                try { ed.RequestRedraw(); } catch { }
            }
            catch (Exception e) { Plugin.Log.LogError("TDA change failed: " + e); }
        });
        drawer.ToggleField("TDA smoke", Tda.Has(bp), _onTda,
            "Thermal smoke equipment (TDA, as on T-55/T-62/T-72/T-80): in battle press " + Plugin.TdaKey +
            " and the engine sprays fuel into the exhaust — thick white smoke pours from the exhaust pipes and hangs behind the " +
            "vehicle, hiding it from enemy eyes (not from thermal sights). Works while the engine runs; " +
            $"{Plugin.TdaTime.Value:0} s of smoke that counts down while it is on and comes back while it is off.");
    }

    [HarmonyPatch(typeof(TransmissionEditor), nameof(TransmissionEditor.OnGUI))]
    private static class GearboxPanelPatch
    {
        private static void Postfix(TransmissionEditor __instance, IGUILayout layout)
        {
            try
            {
                var block = __instance.Component;
                if (block?.Blueprint == null) return;
                Info(layout, "gearbox", Get(block.Blueprint, block));
            }
            catch (Exception e) { if (_errors++ < 5) Plugin.Log.LogWarning("Gearbox size info failed: " + e.Message); }
        }
    }
}
