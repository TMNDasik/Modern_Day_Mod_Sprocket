using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Properties;
using Sprocket.UI;
using Sprocket.VehicleDesigner.Modules;
using UnityEngine;
using UnityEngine.Events;

namespace ActiveProtection;




[HarmonyPatch(typeof(VehicleObjectEditor), nameof(VehicleObjectEditor.OnGUI))]
internal static class DesignerPanel
{
    private static readonly Dictionary<string, bool> Expanded = new();
    private static readonly Dictionary<string, Il2CppSystem.Object> Headers = new();

    private static void Postfix(VehicleObjectEditor __instance, IGUILayout layout)
    {
        try
        {
            var target = __instance.target;
            if (target == null || !ApsParts.IsOurs(target)) return;
            var m = Modules.Ensure(target);
            if (m == null) return;
            var drawer = layout.TryCast<IGUIElementDrawer>();
            if (drawer == null) return;
            switch (m.Kind)
            {
                case ModuleKind.RadioJammer: DrawRadioJammer(__instance, layout, drawer, m); break;
                case ModuleKind.Detector: DrawDetector(__instance, layout, drawer, m); break;
                case ModuleKind.Launcher: DrawLauncher(__instance, layout, drawer, m); break;
                case ModuleKind.Era: EraPanel.Draw(__instance, layout, drawer, m); break;
                case ModuleKind.Panel: BlowoffPanel.Draw(__instance, layout, drawer, m); break;
                default: EoPanel.Draw(__instance, layout, drawer, m); break;
            }
        }
        catch (Exception e) { Plugin.Log.LogError("APS panel failed: " + e); }
    }

    private static void DrawRadioJammer(VehicleObjectEditor editor, IGUILayout layout, IGUIElementDrawer drawer, Module m)
    {
        drawer.Header("Radio Jammer");
        drawer.ToggleField("Enabled",m.S.Mode!=JammerMode.Off,(Il2CppSystem.Action<bool>)(Action<bool>)(v=>DesignerState.Edit(m,s=>s.Mode=v?JammerMode.AlwaysOn:JammerMode.Off)),"Video interference only; fiber-optic drones are immune.");
        drawer.Float(Prop("Radius",()=>m.S.Range,v=>DesignerState.Edit(m,s=>s.Range=v),10,3000,10,"0' m'","Maximum interference radius."));
        drawer.Float(Prop("Strength",()=>m.S.JamChance,v=>DesignerState.Edit(m,s=>s.JamChance=v),0,1,.05f,"0%","Signal degradation near the module."));
        DrawSector(editor,drawer,m,"jams");
        DrawDisplay(editor,layout,drawer);
    }

    

    private static void DrawDetector(VehicleObjectEditor editor, IGUILayout layout, IGUIElementDrawer drawer, Module m)
    {
        drawer.Header("APS Detector");
        var vehicle = Modules.VehicleOf(m);
        var all = Modules.OnVehicle(vehicle);
        var number = Number(all, m);

        Section(layout, "Coverage", true, () =>
        {
            drawer.Float(Prop("Range", () => m.S.Range, v => DesignerState.Edit(m, s => s.Range = v),
                ApsSettings.RangeMin, ApsSettings.RangeMax, 1f, "0' m'",
                "How far the radar sees. Fast rounds need a long range: an APFSDS dart flies 85 m during a 0.05 s reaction."));
            DrawSector(editor, drawer, m, "watches");
            drawer.InfoField(SectorSummary(m.S, "Watches"), 2);
        });

        Section(layout, "Detected threats", true, () => DrawThreatToggles(editor, drawer, m,
            "The radar reports this kind of threat to its launchers."));

        Section(layout, "Linked launchers", true, () =>
        {
            var launchers = new List<Module>();
            foreach (var x in all) if (x.Kind == ModuleKind.Launcher) launchers.Add(x);
            var linked = 0;
            foreach (var l in launchers) if (l.S.Detector == m.Vuid) linked++;
            if (launchers.Count == 0)
            {
                drawer.InfoField("No APS launchers on this vehicle yet. Place an APS Launcher — it links to the nearest detector by itself.", 2);
                return;
            }
            drawer.InfoField($"Detector {number}: {linked} of {launchers.Count} launcher(s) linked. Tick a launcher to link it here " +
                             "(a launcher has only one detector — it moves from its old one).", 3);
            var index = 0;
            var where = new List<string>();
            foreach (var l in launchers)
            {
                index++;
                var launcher = l;
                var here = l.S.Detector == m.Vuid;
                var other = !here && l.S.Detector != 0 ? Modules.DetectorOf(l, all) : null;
                var status = here ? "" : other != null ? $" — detector {Number(all, other)}" : " — not linked";
                where.Add($"{index}: {DesignerState.WhereShort(m, l)}{status}");
                Il2CppSystem.Action<bool> onToggle = (Action<bool>)(v =>
                {
                    try
                    {
                        DesignerState.Link(launcher, v ? m.Vuid : 0);
                        editor.RequestRedraw();
                    }
                    catch (Exception e) { Plugin.Log.LogError("APS link failed: " + e); }
                });
                
                drawer.ToggleField($"Launcher {index}", here, onToggle,
                    $"{DesignerState.Where(m, l)} from this detector{status}. {launcher.S.Charges} charge(s), reaction {launcher.S.Reaction:0.###} s.");
            }
            drawer.InfoField("Where (from this detector, m):\n" + string.Join("\n", where), where.Count + 1);
        });

        DrawDisplay(editor, layout, drawer);
    }

    

    private static void DrawLauncher(VehicleObjectEditor editor, IGUILayout layout, IGUIElementDrawer drawer, Module m)
    {
        drawer.Header("APS Launcher");
        var vehicle = Modules.VehicleOf(m);
        var all = Modules.OnVehicle(vehicle);
        var detectors = new List<Module>();
        foreach (var x in all) if (x.Kind == ModuleKind.Detector) detectors.Add(x);

        Section(layout, "Detector", true, () =>
        {
            var options = new Il2CppSystem.Collections.Generic.List<string>();
            options.Add("None");
            var selected = 0;
            for (var i = 0; i < detectors.Count; i++)
            {
                options.Add($"Detector {i + 1} — {DesignerState.Where(m, detectors[i])}");
                if (detectors[i].Vuid == m.S.Detector) selected = i + 1;
            }
            UnityAction<int> onSelect = (Action<int>)(index =>
            {
                try
                {
                    DesignerState.Link(m, index > 0 && index <= detectors.Count ? detectors[index - 1].Vuid : 0);
                    editor.RequestRedraw();
                }
                catch (Exception e) { Plugin.Log.LogError("APS link failed: " + e); }
            });
            drawer.Dropdown("Linked detector", options.Cast<Il2CppSystem.Collections.Generic.IReadOnlyList<string>>(), selected, onSelect,
                "The radar that tells this launcher about threats. Without a working linked detector the launcher never fires.");
            string info;
            if (detectors.Count == 0) info = "This vehicle has no APS Detector: place one, otherwise this launcher can't fire.";
            else if (selected == 0) info = "Not linked: this launcher will not fire.";
            else
            {
                var d = detectors[selected - 1];
                info = $"Fires at threats detected by detector {selected} ({DesignerState.Where(m, d)}), range {d.S.Range:0} m.";
                var missing = m.S.Types & ~d.S.Types;
                if (missing != 0) info += " That detector doesn't report: " + TypesText(missing) + ".";
            }
            drawer.InfoField(info, 2);
        });

        Section(layout, "Interception zone", true, () =>
        {
            FloatProperty near = null, far = null;
            near = Prop("Near boundary", () => m.S.MinDist, v =>
            {
                DesignerState.Edit(m, s => s.MinDist = v);
                Refresh(far, m.S.MaxDist);
            }, ApsSettings.DistMin, ApsSettings.DistMax, 0.5f, "0.0' m'",
                "Closest distance from the launcher at which it can still destroy a threat (the charge needs room to work).");
            far = Prop("Far boundary", () => m.S.MaxDist, v =>
            {
                DesignerState.Edit(m, s => s.MaxDist = v);
                Refresh(near, m.S.MinDist);
            }, ApsSettings.DistMin, ApsSettings.DistMax, 0.5f, "0.0' m'",
                "Farthest distance from the launcher at which its charge can reach a threat.");
            drawer.Float(near);
            drawer.Float(far);
            DrawSector(editor, drawer, m, "covers");
            drawer.InfoField(SectorSummary(m.S, "Covers") + $", from {m.S.MinDist:0.#} to {m.S.MaxDist:0.#} m.", 2);
        });

        Section(layout, "Threats", true, () =>
        {
            DrawThreatToggles(editor, drawer, m, "The launcher fires at this kind of threat.");
            FloatProperty lo = null, hi = null;
            lo = Prop("Min threat speed", () => m.S.MinSpeed, v =>
            {
                DesignerState.Edit(m, s => s.MinSpeed = v);
                Refresh(hi, m.S.MaxSpeed);
            }, ApsSettings.SpeedMin, ApsSettings.SpeedMax, 10f, "0' m/s'", "Slower threats are ignored.");
            hi = Prop("Max threat speed", () => m.S.MaxSpeed, v =>
            {
                DesignerState.Edit(m, s => s.MaxSpeed = v);
                Refresh(lo, m.S.MinSpeed);
            }, ApsSettings.SpeedMin, ApsSettings.SpeedMax, 10f, "0' m/s'",
                "Faster threats are ignored. ATGM ~300 m/s, HE 500-800 m/s, AP 600-1000 m/s, APFSDS 1500-1800 m/s.");
            drawer.Float(lo);
            drawer.Float(hi);
        });

        Section(layout, "Fire control", true, () =>
        {
            drawer.Float(Prop("Reaction time", () => m.S.Reaction, v => DesignerState.Edit(m, s => s.Reaction = v),
                ApsSettings.ReactionMin, ApsSettings.ReactionMax, 0.005f, "0.000' s'",
                "Time from detection to the shot. Real systems: 0.05-0.07 s."));
            drawer.Float(Prop("Pause between shots", () => m.S.Pause, v => DesignerState.Edit(m, s => s.Pause = v),
                ApsSettings.PauseMin, ApsSettings.PauseMax, 0.01f, "0.00' s'",
                "Time before this launcher can fire again. Two threats closer together than this get through."));

            var options = new Il2CppSystem.Collections.Generic.List<string>();
            foreach (var n in ThreatInfo.PriorityNames) options.Add(n);
            UnityAction<int> onPriority = (Action<int>)(index =>
            {
                try { DesignerState.Edit(m, s => s.Priority = (TargetPriority)Mathf.Clamp(index, 0, 2)); editor.RequestRedraw(); }
                catch (Exception e) { Plugin.Log.LogError("APS priority failed: " + e); }
            });
            drawer.Dropdown("Target priority", options.Cast<Il2CppSystem.Collections.Generic.IReadOnlyList<string>>(), (int)m.S.Priority, onPriority,
                "Which threat this launcher takes first when several come at once. Launchers of the same vehicle share the threats " +
                "between them: one launcher per threat, another one fires only if the first misses.");

            FloatProperty charges = null, perTarget = null;
            charges = Prop("Charges", () => m.S.Charges, v =>
            {
                DesignerState.Edit(m, s => s.Charges = Mathf.RoundToInt(v));
                if (perTarget != null)
                {
                    perTarget.SetLimits(1f, m.S.Charges);
                    Refresh(perTarget, m.S.PerTarget);
                }
            }, 1f, ApsSettings.ChargesMax, 1f, "0", "Protective charges loaded in this launcher. Each shot uses one; when they are gone it stops firing.");
            perTarget = Prop("Charges per target", () => m.S.PerTarget, v => DesignerState.Edit(m, s => s.PerTarget = Mathf.RoundToInt(v)),
                1f, Math.Max(1, m.S.Charges), 1f, "0", "How many charges are fired at one threat at once (more = surer hit, fewer threats stopped).");
            drawer.Float(charges);
            drawer.Float(perTarget);
            drawer.InfoField(FireSummary(m.S), 3);
        });

        DrawDisplay(editor, layout, drawer);
    }

    

    internal static void DrawSector(VehicleObjectEditor editor, IGUIElementDrawer drawer, Module m, string verb, bool vertical = true)
    {
        drawer.Float(Prop("Direction (horizontal)", () => m.S.Azimuth, v => DesignerState.Edit(m, s => s.Azimuth = v),
            -180f, 180f, 1f, "0'°'", "Turns the sector left (-) or right (+) from the way the module faces (out of the surface it stands on)."));
        if (vertical)
            drawer.Float(Prop("Direction (vertical)", () => m.S.Elevation, v => DesignerState.Edit(m, s => s.Elevation = v),
                -90f, 90f, 1f, "0'°'", "Tilts the sector down (-) or up (+) from the horizon."));
        Il2CppSystem.Action<bool> onFull = (Action<bool>)(v =>
        {
            try
            {
                DesignerState.Edit(m, s =>
                {
                    s.Full360 = v;
                    if (!v && s.HArc >= 360f) s.HArc = 180f;
                });
                editor.RequestRedraw();
            }
            catch (Exception e) { Plugin.Log.LogError("APS 360 toggle failed: " + e); }
        });
        drawer.ToggleField("All around", m.S.Full360, onFull,
            $"360° horizontally: the module {verb} the full circle. Vertically it still keeps its own angle — this is not a sphere.");
        if (!m.S.Full360)
            drawer.Float(Prop("Horizontal angle", () => m.S.HArc, v => DesignerState.Edit(m, s => { s.HArc = Mathf.Min(v, 359f); s.Full360 = false; }),
                5f, 359f, 1f, "0'°'", "Width of the sector, left to right."));
        if (vertical)
            drawer.Float(Prop("Vertical angle", () => m.S.VArc, v => DesignerState.Edit(m, s => s.VArc = v),
                2f, 180f, 1f, "0'°'", "Height of the sector, bottom to top (180° = from straight down to straight up)."));
    }

    private static void DrawThreatToggles(VehicleObjectEditor editor, IGUIElementDrawer drawer, Module m, string what)
    {
        foreach (var t in ThreatInfo.Each)
        {
            var type = t;
            Il2CppSystem.Action<bool> onToggle = (Action<bool>)(v =>
            {
                try
                {
                    DesignerState.Edit(m, s => s.Types = v ? s.Types | type : s.Types & ~type);
                    editor.RequestRedraw();
                }
                catch (Exception e) { Plugin.Log.LogError("APS threat toggle failed: " + e); }
            });
            drawer.ToggleField(ThreatInfo.Name(type), (m.S.Types & type) != 0, onToggle, what + " " + ThreatInfo.Tooltip(type));
        }
    }

    
    internal static void DrawDisplay(VehicleObjectEditor editor, IGUILayout layout, IGUIElementDrawer drawer)
    {
        Section(layout, "Zone colours", false, () =>
        {
            drawer.InfoField("APS — blue: detection zone; orange: interception zone, yellow = near boundary, red = far boundary. " +
                             "EO — violet: sensor; grey: where the aerosol screen forms; red: IR jammer (long ranges are drawn shortened). " +
                             "Every module's zone at once: 'APS zones' in the Visibility tab.", 5);
        });
    }

    internal static void Section(IGUILayout layout, string name, bool defaultOpen, Action body)
    {
        if (!Expanded.TryGetValue(name, out var open)) Expanded[name] = open = defaultOpen;
        if (!Headers.TryGetValue(name, out var header))
            Headers[name] = header = new Il2CppSystem.Object(IL2CPP.ManagedStringToIl2Cpp(name));
        Il2CppSystem.Action<bool> onExpand = (Action<bool>)(v => Expanded[name] = v);
        layout.BeginDropdown(header, open, onExpand);
        try { body(); }
        finally { layout.EndDropdown(); }
    }

    internal static FloatProperty Prop(string name, Func<float> get, Action<float> set, float min, float max, float step, string format, string tooltip)
    {
        Il2CppSystem.Func<float> g = (Func<float>)(() =>
        {
            try { return get(); }
            catch { return min; }
        });
        Il2CppSystem.Action<float> s = (Action<float>)(v =>
        {
            try { set(v); }
            catch (Exception e) { Plugin.Log.LogError($"APS setting '{name}' failed: " + e); }
        });
        return new FloatProperty(name, g, s, min, max, step, format, null, tooltip);
    }

    
    internal static void Refresh(FloatProperty p, float value)
    {
        if (p == null) return;
        try
        {
            if (Mathf.Abs(p.Value - value) > 1e-4f) p.Value = value;
        }
        catch { }
    }

    internal static int Number(List<Module> all, Module m)
    {
        var n = 0;
        foreach (var x in all)
        {
            if (x.Kind != m.Kind) continue;
            n++;
            if (x == m) return n;
        }
        return 0;
    }

    internal static string SectorSummary(ApsSettings s, string verb)
    {
        var h = s.Full360 ? "all around" : $"{s.HArc:0}° wide";
        var dir = s.Full360 ? "" : Mathf.Abs(s.Azimuth) < 0.5f ? " straight out" : $" turned {Mathf.Abs(s.Azimuth):0}° {(s.Azimuth > 0 ? "right" : "left")}";
        return $"{verb} {h}{dir}, {s.ElevationLow:0}° to {s.ElevationHigh:0}° vertically";
    }

    private static string FireSummary(ApsSettings s)
    {
        var volleys = Math.Max(1, s.Charges / Math.Max(1, s.PerTarget));
        return $"Can stop up to {volleys} threat(s): {s.Charges} charge(s), {s.PerTarget} per threat. " +
               $"Threats between {s.MinSpeed:0} and {s.MaxSpeed:0} m/s; shot {s.Reaction:0.###} s after detection; " +
               $"ready again after {s.Pause:0.##} s.";
    }

    private static string TypesText(ThreatTypes types)
    {
        var parts = new List<string>();
        foreach (var t in ThreatInfo.Each) if ((types & t) != 0) parts.Add(ThreatInfo.Name(t));
        return string.Join(", ", parts);
    }
}
