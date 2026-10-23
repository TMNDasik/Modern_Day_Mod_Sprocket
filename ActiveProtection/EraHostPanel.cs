using System;
using HarmonyLib;
using Sprocket.UI;
using Sprocket.VehicleDesigner.Modules;
using Sprocket.Vehicles;
using UnityEngine;
using UnityEngine.Events;

namespace ActiveProtection;







[HarmonyPatch(typeof(VehicleObjectEditor), nameof(VehicleObjectEditor.OnGUI))]
internal static class EraHostPanel
{
    private static VehicleObjectEditor _editor;

    private static void Postfix(VehicleObjectEditor __instance, IGUILayout layout)
    {
        try
        {
            var vo = __instance.target;
            if (vo == null) return;
            var host = EraHosts.Get(vo);
            if (host == null && !EraHosts.Eligible(vo)) return;
            var drawer = layout.TryCast<IGUIElementDrawer>();
            if (drawer == null) return;
            _editor = __instance;
            Draw(layout, drawer, vo, host?.C);
        }
        catch (Exception e) { Plugin.Log.LogError("ERA panel failed: " + e); }
    }

    public static void Redraw()
    {
        try { if (_editor != null && !_editor.WasCollected) _editor.RequestRedraw(); }
        catch { }
    }

    
    private static EraConfig Live(VehicleObject vo) => EraHosts.Get(vo)?.C;

    private static void Draw(IGUILayout layout, IGUIElementDrawer drawer, VehicleObject vo, EraConfig c)
    {
        var on = c != null && c.On;
        DesignerPanel.Section(layout, "Reactive armour", true, () =>
        {
            Il2CppSystem.Action<bool> onToggle = (Action<bool>)(v =>
            {
                try { Toggle(vo, v); }
                catch (Exception e) { Plugin.Log.LogError("ERA toggle failed: " + e); }
            });
            drawer.ToggleField("ERA", on, onToggle,
                "Make this part explosive reactive armour. Inside it you place armour plates and explosive elements; a round " +
                "that runs into an explosive sets it off and loses penetration, and the part is blown off: it disappears and " +
                "no longer counts as armour for the rest of the battle.");
            if (!on) return;

            var (coverage, heat, kinetic, tandem) = c.Summary();
            if (!c.HasGeometry)
                Warn(drawer, "The part's faces aren't measured yet — wait a moment.");
            else if (c.Count(true) == 0)
                Warn(drawer, "No explosive elements: the part is plain armour. Add an explosive.");
            else if (coverage <= 0f)
                Warn(drawer, "The explosives are outside the part's faces — move them back.");
            else
                Info(drawer, $"Explosives cover {coverage * 100f:0}% of the faces. From {EraConfig.FullEffectAngle:0}° off a face: " +
                             $"HEAT −{heat * 100f:0}%, APFSDS/AP −{kinetic * 100f:0}%, tandem ATGM −{tandem * 100f:0}% penetration; " +
                             "straight on about a third of that.");

            drawer.Float(DesignerPanel.Prop("Set off from", () => Live(vo)?.Trigger ?? EraConfig.TriggerDefault,
                v => EraOps.Edit(vo, "ERA trigger", "tc", x => { x.Trigger = Mathf.Round(v); return x; }),
                EraConfig.TriggerMin, EraConfig.TriggerMax, 1f, "0' mm'",
                "Smaller bullets and shells don't set the explosives off. Real ERA ignores rifle and heavy machine-gun bullets; " +
                "Kontakt-5 is made not to go off from 30 mm autocannon rounds either."));

            Button(drawer, "Add armour plate", "A steel plate driven by the explosive next to it. Thicker plates cut HEAT jets more " +
                                               "and only heavy plates (15 mm and more) break APFSDS darts.",
                () => EraOps.Edit(vo, "Add ERA plate", null, x => { x.Elements.Add(x.NewElement(false)); return x; }));
            Button(drawer, "Add explosive", "An explosive element. A round passing through it sets it off and blows the part off. " +
                                            "Two explosives one behind the other also stop tandem missiles: the first one goes off " +
                                            "from the front charge, the second one meets the main jet. A round through a gap between " +
                                            "explosives passes as through plain armour.",
                () => EraOps.Edit(vo, "Add ERA explosive", null, x => { x.Elements.Add(x.NewElement(true)); return x; }));

            var mirrored = c.Panels.Exists(p => p.Mirror);
            Info(drawer, $"The elements repeat on {c.Panels.Count} face(s) of the part{(mirrored ? ", the mirrored half included" : "")}, " +
                         "following their slope. Shown in colour when the part is selected (orange — explosive, grey — plate). " +
                         "Sideways and up/down are % of a face from its middle, depth counts from the outer surface.");
        });
        if (!on) return;

        for (var i = 0; i < c.Elements.Count; i++) DrawElement(layout, drawer, vo, c, i);
    }

    private static void DrawElement(IGUILayout layout, IGUIElementDrawer drawer, VehicleObject vo, EraConfig c, int index)
    {
        var e = c.Elements[index];
        var kind = e.Explosive ? "Explosive" : "Plate";
        var number = c.NumberOf(index);
        LazySection(layout, $"{kind} {number}", () =>
        {
            Info(drawer, $"{kind} {e.W:0} x {e.H:0}% of a face, {e.T:0} mm thick, {e.Depth:0} mm under the surface" +
                         (Mathf.Abs(e.X) >= 0.5f || Mathf.Abs(e.Y) >= 0.5f ? $", moved {e.X:0} / {e.Y:0}%." : ", centred."));
            Slider(drawer, vo, index, "Sideways", "x", -100f, 100f, "0'%'", x => x.X, (x, v) => x.X = v,
                "Moves the element across the face, from its middle (±100% = to the edge).");
            Slider(drawer, vo, index, "Up / down", "y", -100f, 100f, "0'%'", x => x.Y, (x, v) => x.Y = v,
                "Moves the element up or down the face (along its slope), from its middle (±100% = to the edge). " +
                "On a flat roof face up means forward.");
            Slider(drawer, vo, index, "Depth", "d", 0f, c.MaxDepth, "0' mm'", x => x.Depth, (x, v) => x.Depth = v,
                "How deep the element sits under the outer surface of the part.");
            Slider(drawer, vo, index, "Width", "w", EraConfig.SizeMinPct, EraConfig.SizeMaxPct, "0'%'", x => x.W, (x, v) => x.W = v,
                "Width of the element, % of the face's width.");
            Slider(drawer, vo, index, "Height", "h", EraConfig.SizeMinPct, EraConfig.SizeMaxPct, "0'%'", x => x.H, (x, v) => x.H = v,
                "Height of the element, % of the face's height.");
            Slider(drawer, vo, index, "Thickness", "t", EraConfig.ThicknessMin, EraConfig.ThicknessMax, "0' mm'", x => x.T, (x, v) => x.T = v,
                e.Explosive
                    ? "Thickness of the explosive layer. More explosive throws the plates harder (light ERA ~5-8 mm)."
                    : "Thickness of the steel plate (light ERA 2-4 mm, Kontakt-5 front plate ~15-20 mm).");
            Button(drawer, $"Remove {kind.ToLowerInvariant()}", "Takes this element out of the part.",
                () => EraOps.Edit(vo, "Remove ERA element", null, x =>
                {
                    if (index < x.Elements.Count) x.Elements.RemoveAt(index);
                    return x;
                }));
        });
    }

    
    
    private static readonly System.Collections.Generic.Dictionary<string, bool> Open = new();
    private static readonly System.Collections.Generic.Dictionary<string, Il2CppSystem.Object> Headers = new();

    private static void LazySection(IGUILayout layout, string name, Action body)
    {
        Open.TryGetValue(name, out var open);
        if (!Headers.TryGetValue(name, out var header))
            Headers[name] = header = new Il2CppSystem.Object(Il2CppInterop.Runtime.IL2CPP.ManagedStringToIl2Cpp(name));
        Il2CppSystem.Action<bool> onExpand = (Action<bool>)(v =>
        {
            var was = Open.TryGetValue(name, out var o) && o;
            Open[name] = v;
            if (v && !was) Redraw();
        });
        layout.BeginDropdown(header, open, onExpand);
        try { if (open) body(); }
        finally { layout.EndDropdown(); }
    }

    
    private static void Slider(IGUIElementDrawer drawer, VehicleObject vo, int index, string name, string key, float min, float max,
        string format, Func<EraElement, float> get, Action<EraElement, float> set, string tooltip)
    {
        drawer.Float(DesignerPanel.Prop(name, () =>
        {
            var c = Live(vo);
            return c != null && index < c.Elements.Count ? get(c.Elements[index]) : min;
        }, v => EraOps.Edit(vo, "ERA element", $"{index}:{key}", x =>
        {
            if (index < x.Elements.Count) set(x.Elements[index], Mathf.Round(v));
            return x;
        }), min, max, 1f, format, tooltip));
    }

    
    private static void Toggle(VehicleObject vo, bool on)
    {
        EraOps.Edit(vo, on ? "ERA on" : "ERA off", null, x =>
        {
            if (x == null)
            {
                if (!on) return null;
                x = new EraConfig();
                EraHosts.Measure(vo, x);
                x.AddDefaults();
                return x;
            }
            x.On = on;
            if (on && !x.HasGeometry) EraHosts.Measure(vo, x);
            return x;
        });
    }

    private static void Button(IGUIElementDrawer drawer, string text, string tip, Action action)
    {
        UnityAction onClick = (Action)(() =>
        {
            try { action(); }
            catch (Exception e) { Plugin.Log.LogError($"ERA '{text}' failed: " + e); }
        });
        var tooltip = new UITooltip(text, tip);
        drawer.Button(text, onClick, ref tooltip);
    }

    private static void Info(IGUIElementDrawer drawer, string text) =>
        drawer.InfoField(text, Math.Max(1, (int)Math.Ceiling(text.Length / 46.0)));

    private static void Warn(IGUIElementDrawer drawer, string text) =>
        drawer.InfoField(text, Math.Max(1, (int)Math.Ceiling(text.Length / 46.0)), new Color(1f, 0.7f, 0.4f));
}
