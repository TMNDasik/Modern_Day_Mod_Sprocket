using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Properties;
using Sprocket.UI;
using Sprocket.VehicleDesigner.Access;
using Sprocket.Vehicles.Cannons;
using Sprocket.Vehicles.Cannons.Editor;
using UnityEngine;
using UnityEngine.Events;

namespace Munitions;


internal static class ApfsdsPanel
{
    private sealed class Panel
    {
        public CannonEditor Editor;
        public FloatProperty Dart, Speed, Penetration;
        public int LastUsedFrame;

        public CannonBlueprint Blueprint => Editor?.Component?.Blueprint;
        public int Caliber => Blueprint?.Caliber ?? 0;

        public void Read(out float dart, out float speed, out float pen) => ApfsdsSettings.Read(Blueprint, out dart, out speed, out pen);
    }

    private static readonly Dictionary<IntPtr, Panel> Panels = new();

    public static void Draw(CannonEditor editor, IGUIElementDrawer drawer)
    {
        var p = GetPanel(editor);
        p.LastUsedFrame = Time.frameCount;
        var cal = p.Caliber;
        p.Dart.SetLimits(ApfsdsSettings.MinDart(cal), ApfsdsSettings.MaxDart(cal));
        drawer.Float(p.Dart);
        drawer.Float(p.Speed);
        drawer.Float(p.Penetration);

        p.Read(out var dartSet, out var speedSet, out var penSet);
        var dart = ApfsdsSettings.EffectiveDart(cal, dartSet);
        var speed = ApfsdsSettings.EffectiveSpeed(p.Blueprint, speedSet);
        ApfsdsSettings.AutoSpeed(p.Blueprint, out var measured);
        var pen = ApfsdsSettings.EffectivePenetration(cal, dart, speed, penSet);
        var text = $"{dart:0} mm tungsten dart in a {cal} mm sabot (subcaliber {dart / Mathf.Max(1, cal):0.00}), " +
                   $"{ApfsdsSettings.RodLength(cal, dart):0} mm long, {ApfsdsSettings.RodMass(cal, dart):0.0} kg. " +
                   $"Leaves the barrel at {speed:0} m/s" +
                   (speedSet > 0f ? "" : measured ? " (measured in a test drive)" : " (estimated from the gun; fire it once in a test drive to measure)") +
                   $". Penetration {pen:0} mm at the muzzle{(penSet > 0f ? "" : " (calculated from the dart and the speed)")}, " +
                   "less at long range as the dart slows down.";
        drawer.InfoField(text, Math.Max(1, (int)Math.Ceiling(text.Length / 46.0)));

        var tip = new UITooltip("Automatic", "Dart 0.2 of the caliber, the speed the gun's charge gives, penetration from the formula.");
        UnityAction onAuto = (Action)(() =>
        {
            try
            {
                Write(editor, 0f, 0f, 0f);
                editor.RequestRedraw();
            }
            catch (Exception e) { Plugin.Log.LogError("APFSDS reset failed: " + e); }
        });
        drawer.Button("Reset to automatic", onAuto, ref tip);
        
        drawer.InfoField("Saved with the gun: use the gun's save / load buttons at the top of this panel.", 2);
    }

    private static Panel GetPanel(CannonEditor editor)
    {
        if (Panels.TryGetValue(editor.Pointer, out var panel)) return panel;
        if (Panels.Count > 32)
        {
            var now = Time.frameCount;
            var stale = new List<IntPtr>();
            foreach (var kv in Panels) if (now - kv.Value.LastUsedFrame > 600) stale.Add(kv.Key);
            foreach (var k in stale) Panels.Remove(k);
        }
        panel = new Panel { Editor = editor };
        var p = panel;

        Il2CppSystem.Func<float> getDart = (Func<float>)(() =>
        {
            try { p.Read(out var d, out _, out _); return ApfsdsSettings.EffectiveDart(p.Caliber, d); }
            catch { return 20f; }
        });
        Il2CppSystem.Action<float> setDart = (Action<float>)(v =>
        {
            try
            {
                p.Read(out _, out var s, out var pn);
                Write(p.Editor, Mathf.Round(v), s, pn);
                Refresh(p);
            }
            catch (Exception e) { Plugin.Log.LogError("APFSDS dart change failed: " + e); }
        });
        panel.Dart = new FloatProperty("Dart caliber", getDart, setDart, 3f, 100f, 1f, "0", null,
            "Diameter of the tungsten dart in mm — the subcaliber (the gun keeps its own caliber, the sabot fills the bore). " +
            "Real darts: about 0.2 of the gun caliber (120 mm gun — 22-27 mm dart).");

        Il2CppSystem.Func<float> getSpeed = (Func<float>)(() =>
        {
            try { p.Read(out _, out var s, out _); return ApfsdsSettings.EffectiveSpeed(p.Blueprint, s); }
            catch { return 1500f; }
        });
        Il2CppSystem.Action<float> setSpeed = (Action<float>)(v =>
        {
            try
            {
                p.Read(out var d, out _, out var pn);
                Write(p.Editor, d, Mathf.Round(v / 5f) * 5f, pn);
                Refresh(p);
            }
            catch (Exception e) { Plugin.Log.LogError("APFSDS speed change failed: " + e); }
        });
        panel.Speed = new FloatProperty("Muzzle velocity", getSpeed, setSpeed, ApfsdsSettings.MinSpeed, ApfsdsSettings.MaxSpeed, 5f, "0", null,
            "Speed of the dart when it leaves the barrel, m/s (modern 120/125 mm: 1650-1800).");

        Il2CppSystem.Func<float> getPen = (Func<float>)(() =>
        {
            try
            {
                p.Read(out var d, out var s, out var pn);
                var c = p.Caliber;
                return ApfsdsSettings.EffectivePenetration(c, ApfsdsSettings.EffectiveDart(c, d), ApfsdsSettings.EffectiveSpeed(p.Blueprint, s), pn);
            }
            catch { return 300f; }
        });
        Il2CppSystem.Action<float> setPen = (Action<float>)(v =>
        {
            try
            {
                p.Read(out var d, out var s, out _);
                Write(p.Editor, d, s, Mathf.Round(v / 5f) * 5f);
            }
            catch (Exception e) { Plugin.Log.LogError("APFSDS penetration change failed: " + e); }
        });
        panel.Penetration = new FloatProperty("Penetration", getPen, setPen, ApfsdsSettings.MinPenetration, ApfsdsSettings.MaxPenetration, 5f, "0", null,
            "Armour penetration at the muzzle in mm (DM53 about 650-700, 3BM60 about 600-650, M829A3 about 750-800). " +
            "Until you move this slider it follows the dart and the speed.");

        Panels[editor.Pointer] = panel;
        return panel;
    }

    
    private static void Refresh(Panel p)
    {
        try
        {
            var handler = p.Penetration.ValueChanged;
            handler?.Invoke(p.Penetration, p.Penetration.Value);
        }
        catch { }
    }

    
    private static void Write(CannonEditor editor, float dart, float speed, float penetration)
    {
        var bp = editor?.Component?.Blueprint;
        if (bp == null) return;
        var text = ApfsdsSettings.With(bp.Description, dart, speed, penetration);
        if (text == (bp.Description ?? "")) return;
        Il2CppSystem.IDisposable frame = null;
        try { frame = editor.NewBlueprintEditFrame("APFSDS round", bp, null, editor.GetNewOperationGroupID(), VehicleOpExecutionMode.Synchronous); }
        catch (Exception e) { if (Plugin.DebugLogging.Value) Plugin.Log.LogWarning("Edit frame unavailable: " + e.Message); }
        try { bp.Description = text; }
        finally { if (frame != null) frame.Dispose(); else bp.MarkModified(); }
    }
}
