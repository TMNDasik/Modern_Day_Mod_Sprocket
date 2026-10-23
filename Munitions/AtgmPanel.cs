using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Properties;
using Sprocket.UI;
using Sprocket.VehicleDesigner.Access;
using Sprocket.Vehicles.Cannons;
using Sprocket.Vehicles.Cannons.Editor;
using Mathf = UnityEngine.Mathf;

namespace Munitions;



internal static class AtgmPanel
{
    
    private sealed class Panel
    {
        public CannonEditor Editor;
        public int Kind;
        public FloatProperty Speed, Agility, Penetration;
        public int LastUsedFrame;

        public CannonBlueprint Blueprint => Editor?.Component?.Blueprint;
        public int Caliber => Blueprint?.Caliber ?? 0;

        public void Read(out float speed, out float g, out float penetration) =>
            AtgmSettings.Read(Blueprint, Kind, out speed, out g, out penetration);
    }

    private static readonly Dictionary<IntPtr, Panel> Panels = new();

    
    public static void Draw(CannonEditor editor, IGUIElementDrawer drawer, int kind)
    {
        var panel = GetPanel(editor, kind);
        panel.LastUsedFrame = UnityEngine.Time.frameCount;

        var warhead = AtgmSettings.Warhead(panel.Blueprint);
        var heads = new Il2CppSystem.Collections.Generic.List<string>();
        foreach (var n in AtgmSettings.WarheadNames) heads.Add(n);
        UnityEngine.Events.UnityAction<int> onWarhead = (Action<int>)(index =>
        {
            try
            {
                WriteWarhead(editor, index);
                editor.RequestRedraw();
            }
            catch (Exception e) { Plugin.Log.LogError("ATGM warhead change failed: " + e); }
        });
        drawer.Dropdown("Warhead", heads.Cast<Il2CppSystem.Collections.Generic.IReadOnlyList<string>>(), warhead, onWarhead,
            "Tandem HEAT: a shaped charge for tanks (defeats ERA). HE-fragmentation: almost no armour penetration, but a strong blast " +
            "and fragments against light vehicles, infantry positions and external parts (Kornet HE, Hellfire blast-frag). " +
            "Thermobaric: an even stronger blast and a fireball that can set vehicles on fire (Kornet-E thermobaric, 9M133F).");
        drawer.Float(panel.Speed);
        drawer.Float(panel.Agility);
        if (warhead == Kind.WarheadHeat) drawer.Float(panel.Penetration);

        var guide = AtgmSettings.Guide(panel.Blueprint);
        var options = new Il2CppSystem.Collections.Generic.List<string>();
        foreach (var n in AtgmSettings.GuideNames) options.Add(n);
        UnityEngine.Events.UnityAction<int> onGuide = (Action<int>)(index =>
        {
            try
            {
                panel.Read(out var s, out var gg, out var pp);
                Write(editor, s, gg, pp, index == AtgmSettings.GuideSaclos ? AtgmSettings.GuideSaclos : AtgmSettings.GuideLaserBeam);
                editor.RequestRedraw();
            }
            catch (Exception e) { Plugin.Log.LogError("ATGM guidance change failed: " + e); }
        });
        drawer.Dropdown("Guidance", options.Cast<Il2CppSystem.Collections.Generic.IReadOnlyList<string>>(), guide, onGuide,
            "Laser beam-riding (Kornet, Refleks, Svir): the missile flies in a laser beam from the launcher; IR jammers can't touch it, " +
            "but an aerosol screen breaks the beam and a laser warning receiver detects it. SACLOS with IR tracker (Konkurs, TOW, Metis): " +
            "the launcher tracks the missile's flare; an IR jammer (Shtora-type) can make it lose the missile.");
        var wire = false;
        if (kind == Kind.Launcher)
        {
            wire = AtgmSettings.Wire(panel.Blueprint);
            var links = new Il2CppSystem.Collections.Generic.List<string>();
            foreach (var n in AtgmSettings.LinkNames) links.Add(n);
            UnityEngine.Events.UnityAction<int> onLink = (Action<int>)(index =>
            {
                try
                {
                    panel.Read(out var s, out var gg, out var pp);
                    Write(editor, s, gg, pp, -1, index == 1 ? 1 : 0);
                    editor.RequestRedraw();
                }
                catch (Exception e) { Plugin.Log.LogError("ATGM control link change failed: " + e); }
            });
            drawer.Dropdown("Control link", links.Cast<Il2CppSystem.Collections.Generic.IReadOnlyList<string>>(), wire ? 1 : 0, onLink,
                "Wireless: commands by radio or the laser beam itself (Kornet, Hermes). Wire-guided: the missile unreels a thin wire " +
                "behind it (TOW, Konkurs, Metis, Milan) — it sags and falls to the ground along the flight path. The missile can't fly " +
                "farther than the wire: when it runs out, the wire snaps and the missile flies on unguided.");
        }
        panel.Read(out var speed, out var g, out var pen);
        var effective = AtgmSettings.EffectivePenetration(kind, panel.Caliber, pen);
        var range = AtgmSettings.Range(kind);
        if (wire) range = Mathf.Min(range, Plugin.AtgmWireLength.Value);
        var text = $"Flies at {speed:0} m/s, up to {range / 1000f:0.0} km{(wire ? " (wire length)" : "")} ({Mathf.Min(AtgmSettings.MaxFlight(kind, speed), range / Mathf.Max(speed, 50f)):0} s). " +
                   $"Turns with up to {g:0.#} g: more g follows the sight faster and hits moving targets easier. " +
                   WarheadText(Kind.WithWarhead(kind, warhead), panel.Caliber, effective, pen > 0f) +
                   " The missile never hits its own vehicle.";
        drawer.InfoField(text, Math.Max(1, (int)Math.Ceiling(text.Length / 46.0)));
        
        drawer.InfoField(kind == Kind.Launcher
            ? "Saved with the launcher: use the save / load buttons next to its name at the top of this panel."
            : "Saved with the gun: use the gun's save / load buttons at the top of this panel.", 2);
    }

    private static string WarheadText(int kind, int caliber, float heatPenetration, bool heatSet)
    {
        var s = ShellSpec.For(caliber);
        var radius = Mathf.Sqrt(s.MissileBlast(kind) / 10f);
        switch (Kind.WarheadOf(kind))
        {
            case Kind.WarheadHe:
                return $"HE-fragmentation warhead: {s.Filler(kind):0.0} kg of explosive, blast radius about {radius:0} m, " +
                       $"only about {0.5f * caliber:0} mm of armour.";
            case Kind.WarheadTb:
                return $"Thermobaric warhead: blast radius about {radius:0} m and a {s.FireballRadius(kind):0} m fireball that can set " +
                       $"vehicles on fire, only about {0.25f * caliber:0} mm of armour.";
            default:
                return $"Tandem HEAT warhead: {heatPenetration:0} mm{(heatSet ? "" : " (default for the caliber)")}.";
        }
    }

    
    private static void WriteWarhead(CannonEditor editor, int warhead)
    {
        var bp = editor?.Component?.Blueprint;
        if (bp == null) return;
        var text = AtgmSettings.WithWarhead(bp.Description, warhead);
        if (text == (bp.Description ?? "")) return;
        Il2CppSystem.IDisposable frame = null;
        try { frame = editor.NewBlueprintEditFrame("ATGM warhead", bp, null, editor.GetNewOperationGroupID(), VehicleOpExecutionMode.Synchronous); }
        catch (Exception e) { if (Plugin.DebugLogging.Value) Plugin.Log.LogWarning("Edit frame unavailable: " + e.Message); }
        try { bp.Description = text; }
        finally { if (frame != null) frame.Dispose(); else bp.MarkModified(); }
        Plugin.Log.LogInfo($"ATGM warhead: {AtgmSettings.WarheadNames[Math.Clamp(warhead, 0, AtgmSettings.WarheadNames.Length - 1)]}");
    }

    private static Panel GetPanel(CannonEditor editor, int kind)
    {
        if (Panels.TryGetValue(editor.Pointer, out var panel) && panel.Kind == kind) return panel;
        if (Panels.Count > 32)
        {
            var now = UnityEngine.Time.frameCount;
            var stale = new List<IntPtr>();
            foreach (var kv in Panels) if (now - kv.Value.LastUsedFrame > 600) stale.Add(kv.Key);
            foreach (var k in stale) Panels.Remove(k);
        }
        panel = new Panel { Editor = editor, Kind = kind };
        var p = panel;

        Il2CppSystem.Func<float> getSpeed = (Func<float>)(() =>
        {
            try { p.Read(out var s, out _, out _); return s; }
            catch { return AtgmSettings.DefaultSpeed(p.Kind); }
        });
        Il2CppSystem.Action<float> setSpeed = (Action<float>)(v =>
        {
            try
            {
                p.Read(out _, out var g, out var pen);
                Write(p.Editor, (float)Math.Round(v / 5f) * 5f, g, pen);
            }
            catch (Exception e) { Plugin.Log.LogError("ATGM speed change failed: " + e); }
        });
        panel.Speed = new FloatProperty("Missile speed", getSpeed, setSpeed, AtgmSettings.MinSpeed, AtgmSettings.MaxSpeed, 5f, "0", null,
            "Cruise speed of the missile in m/s (Kornet about 300, Konkurs about 200, Hellfire about 420). The range stays the same: a slower missile flies longer.");

        Il2CppSystem.Func<float> getG = (Func<float>)(() =>
        {
            try { p.Read(out _, out var g, out _); return g; }
            catch { return AtgmSettings.DefaultG; }
        });
        Il2CppSystem.Action<float> setG = (Action<float>)(v =>
        {
            try
            {
                p.Read(out var s, out _, out var pen);
                Write(p.Editor, s, (float)Math.Round(v * 2f) / 2f, pen);
            }
            catch (Exception e) { Plugin.Log.LogError("ATGM maneuverability change failed: " + e); }
        });
        panel.Agility = new FloatProperty("Maneuverability", getG, setG, AtgmSettings.MinG, AtgmSettings.MaxG, 0.5f, "0.0", null,
            "Largest turning acceleration in g. Low = smooth but slow to follow the sight, high = snappy.");

        Il2CppSystem.Func<float> getPen = (Func<float>)(() =>
        {
            try
            {
                p.Read(out _, out _, out var pen);
                return AtgmSettings.EffectivePenetration(p.Kind, p.Caliber, pen);
            }
            catch { return AtgmSettings.MinPenetration; }
        });
        Il2CppSystem.Action<float> setPen = (Action<float>)(v =>
        {
            try
            {
                p.Read(out var s, out var g, out _);
                Write(p.Editor, s, g, (float)Math.Round(v / 10f) * 10f);
            }
            catch (Exception e) { Plugin.Log.LogError("ATGM penetration change failed: " + e); }
        });
        panel.Penetration = new FloatProperty("Penetration", getPen, setPen, AtgmSettings.MinPenetration, AtgmSettings.MaxPenetration, 10f, "0", null,
            "Armour penetration of the tandem HEAT warhead in mm (Kornet about 1100-1300, Konkurs about 600-800, gun-launched Refleks about 850-900).");

        Panels[editor.Pointer] = panel;
        return panel;
    }

    
    private static void Write(CannonEditor editor, float speed, float g, float penetration, int guide = -1, int wire = -1)
    {
        var bp = editor?.Component?.Blueprint;
        if (bp == null) return;
        var text = AtgmSettings.With(bp.Description, speed, g, penetration, guide, wire);
        if (text == bp.Description) return;
        Il2CppSystem.IDisposable frame = null;
        try { frame = editor.NewBlueprintEditFrame("ATGM flight", bp, null, editor.GetNewOperationGroupID(), VehicleOpExecutionMode.Synchronous); }
        catch (Exception e) { if (Plugin.DebugLogging.Value) Plugin.Log.LogWarning("Edit frame unavailable: " + e.Message); }
        try { bp.Description = text; }
        finally { if (frame != null) frame.Dispose(); else bp.MarkModified(); }
    }
}
