using System;
using System.Collections.Generic;
using Properties;
using Sprocket.UI;
using Sprocket.VehicleDesigner.Access;
using Sprocket.Vehicles.Cannons;
using Sprocket.Vehicles.Cannons.Editor;
using UnityEngine;
using UnityEngine.Events;

namespace Munitions;


internal static class HeatPanel
{
    private sealed class Panel
    {
        public CannonEditor Editor;
        public FloatProperty Penetration, Speed;
        public int LastUsedFrame;

        public CannonBlueprint Blueprint => Editor?.Component?.Blueprint;
        public int Caliber => Blueprint?.Caliber ?? 0;

        public void Read(out float penetration, out float speed) => HeatSettings.Read(Blueprint, out penetration, out speed);
    }

    private static readonly Dictionary<IntPtr, Panel> Panels = new();

    public static void Draw(CannonEditor editor, IGUIElementDrawer drawer)
    {
        var p = GetPanel(editor);
        p.LastUsedFrame = Time.frameCount;
        drawer.Float(p.Penetration);
        drawer.Float(p.Speed);

        var cal = p.Caliber;
        var s = ShellSpec.For(cal);
        p.Read(out var penSet, out var speedSet);
        var pen = HeatSettings.EffectivePenetration(cal, penSet);
        var speed = HeatSettings.EffectiveSpeed(p.Blueprint, speedSet);
        HeatSettings.AutoSpeed(p.Blueprint, out var measured);
        var text = $"{s.HeatShellMass:0.0} kg fin-stabilized shell with a shaped charge, blast radius about {Mathf.Sqrt(s.HeatShellBlast / 10f):0.0} m. " +
                   $"Leaves the barrel at {speed:0} m/s" +
                   (speedSet > 0f ? "" : measured ? " (measured in a test drive)" : " (estimated from the gun; fire it once in a test drive to measure)") +
                   $". Penetration {pen:0} mm{(penSet > 0f ? "" : " (default for the caliber)")} at any range: the jet does not slow down " +
                   "with distance. Spaced armour, ERA and side skirts make it detonate early.";
        drawer.InfoField(text, Math.Max(1, (int)Math.Ceiling(text.Length / 46.0)));

        var tip = new UITooltip("Automatic", "Penetration from the mod settings (calibers), the speed the gun's charge gives.");
        UnityAction onAuto = (Action)(() =>
        {
            try
            {
                Write(editor, 0f, 0f);
                editor.RequestRedraw();
            }
            catch (Exception e) { Plugin.Log.LogError("HEAT reset failed: " + e); }
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

        Il2CppSystem.Func<float> getPen = (Func<float>)(() =>
        {
            try { p.Read(out var pn, out _); return HeatSettings.EffectivePenetration(p.Caliber, pn); }
            catch { return 300f; }
        });
        Il2CppSystem.Action<float> setPen = (Action<float>)(v =>
        {
            try
            {
                p.Read(out _, out var sp);
                Write(p.Editor, Mathf.Round(v / 5f) * 5f, sp);
            }
            catch (Exception e) { Plugin.Log.LogError("HEAT penetration change failed: " + e); }
        });
        panel.Penetration = new FloatProperty("Penetration", getPen, setPen, HeatSettings.MinPenetration, HeatSettings.MaxPenetration, 5f, "0", null,
            "Armour penetration of the shaped charge in mm, the same at any range (105 mm M456 about 400, 120 mm DM12 about 480-500, " +
            "125 mm 3BK29 about 700 with a tandem charge).");

        Il2CppSystem.Func<float> getSpeed = (Func<float>)(() =>
        {
            try { p.Read(out _, out var sp); return HeatSettings.EffectiveSpeed(p.Blueprint, sp); }
            catch { return 900f; }
        });
        Il2CppSystem.Action<float> setSpeed = (Action<float>)(v =>
        {
            try
            {
                p.Read(out var pn, out _);
                Write(p.Editor, pn, Mathf.Round(v / 5f) * 5f);
            }
            catch (Exception e) { Plugin.Log.LogError("HEAT speed change failed: " + e); }
        });
        panel.Speed = new FloatProperty("Muzzle velocity", getSpeed, setSpeed, HeatSettings.MinSpeed, HeatSettings.MaxSpeed, 5f, "0", null,
            "Speed of the HEAT shell when it leaves the barrel, m/s (105 mm M456 about 1170, 120 mm DM12 about 1140, 125 mm 3BK29 about 905).");

        Panels[editor.Pointer] = panel;
        return panel;
    }

    
    private static void Write(CannonEditor editor, float penetration, float speed)
    {
        var bp = editor?.Component?.Blueprint;
        if (bp == null) return;
        var text = HeatSettings.With(bp.Description, penetration, speed);
        if (text == (bp.Description ?? "")) return;
        Il2CppSystem.IDisposable frame = null;
        try { frame = editor.NewBlueprintEditFrame("HEAT shell", bp, null, editor.GetNewOperationGroupID(), VehicleOpExecutionMode.Synchronous); }
        catch (Exception e) { if (Plugin.DebugLogging.Value) Plugin.Log.LogWarning("Edit frame unavailable: " + e.Message); }
        try { bp.Description = text; }
        finally { if (frame != null) frame.Dispose(); else bp.MarkModified(); }
    }
}
