using System;
using System.Collections.Generic;
using Properties;
using Sprocket.UI;
using Sprocket.VehicleDesigner.Modules;
using UnityEngine;
using UnityEngine.Events;

namespace ActiveProtection;


internal static class EoPanel
{
    public static void Draw(VehicleObjectEditor editor, IGUILayout layout, IGUIElementDrawer drawer, Module m)
    {
        var all = Modules.OnVehicle(Modules.VehicleOf(m));
        switch (m.Kind)
        {
            case ModuleKind.Warner: DrawWarner(editor, layout, drawer, m, all); break;
            case ModuleKind.Smoke: DrawSmoke(editor, layout, drawer, m, all); break;
            case ModuleKind.Jammer: DrawJammer(editor, layout, drawer, m, all); break;
        }
        DesignerPanel.DrawDisplay(editor, layout, drawer);
    }

    

    private static void DrawWarner(VehicleObjectEditor editor, IGUILayout layout, IGUIElementDrawer drawer, Module m, List<Module> all)
    {
        drawer.Header("EO Sensor");
        DesignerPanel.Section(layout, "Sensor coverage", true, () =>
        {
            drawer.Float(DesignerPanel.Prop("Range", () => m.S.Range, v => DesignerState.Edit(m, s => s.Range = Mathf.Round(v / 50f) * 50f),
                ApsSettings.EoRangeMin, ApsSettings.EoRangeMax, 50f, "0' m'",
                "Farthest laser source or missile launch the sensor reports."));
            DesignerPanel.DrawSector(editor, drawer, m, "watches");
            drawer.InfoField(DesignerPanel.SectorSummary(m.S, "Watches") + $", up to {m.S.Range:0} m. Shtora-1 sensors: −5° to +25°; " +
                             "put several sensors around the turret for all-round cover. The zone is drawn shortened.", 4);
        });

        DesignerPanel.Section(layout, "Detects", true, () =>
        {
            foreach (var t in EoInfo.Each)
            {
                var type = t;
                Il2CppSystem.Action<bool> onToggle = (Action<bool>)(v =>
                {
                    try { DesignerState.Edit(m, s => s.Eo = v ? s.Eo | type : s.Eo & ~type); editor.RequestRedraw(); }
                    catch (Exception e) { Plugin.Log.LogError("EO toggle failed: " + e); }
                });
                drawer.ToggleField(EoInfo.Label(type), (m.S.Eo & type) != 0, onToggle, EoInfo.Name(type) + ": " + EoInfo.Tooltip(type));
            }
            Il2CppSystem.Action<bool> onWarn = (Action<bool>)(v =>
            {
                try { DesignerState.Edit(m, s => s.Warn = v); editor.RequestRedraw(); }
                catch (Exception e) { Plugin.Log.LogError("EO warn toggle failed: " + e); }
            });
            drawer.ToggleField("Warn the crew", m.S.Warn, onWarn,
                "When you drive this vehicle: a tone, the threat type and its direction on screen and an arrow pointing to it.");
        });

        DesignerPanel.Section(layout, "Linked countermeasures", true, () =>
        {
            var cms = new List<Module>();
            foreach (var x in all) if (x.Kind == ModuleKind.Smoke || x.Kind == ModuleKind.Jammer) cms.Add(x);
            if (cms.Count == 0)
            {
                drawer.InfoField("No aerosol grenade launchers or IR jammers on this vehicle yet. New ones link to the nearest sensor by themselves.", 3);
                return;
            }
            var number = DesignerPanel.Number(all, m);
            drawer.InfoField($"Sensor {number}: tick a countermeasure to trigger it from this sensor (each one has one sensor).", 2);
            var where = new List<string>();
            foreach (var c in cms)
            {
                var cm = c;
                var here = c.S.Detector == m.Vuid;
                var other = !here && c.S.Detector != 0 ? Modules.DetectorOf(c, all) : null;
                var name = $"{EoInfo.Abbrev(c.Kind)} {DesignerPanel.Number(all, c)}";
                var status = here ? "" : other != null ? $" — sensor {DesignerPanel.Number(all, other)}" : " — not linked";
                where.Add($"{name}: {DesignerState.WhereShort(m, c)}{status}");
                Il2CppSystem.Action<bool> onToggle = (Action<bool>)(v =>
                {
                    try { DesignerState.Link(cm, v ? m.Vuid : 0); editor.RequestRedraw(); }
                    catch (Exception e) { Plugin.Log.LogError("EO link failed: " + e); }
                });
                drawer.ToggleField(name, here, onToggle, $"{EoInfo.Title(c.Kind)}, {DesignerState.Where(m, c)} from this sensor{status}. " +
                                                         (c.Kind == ModuleKind.Smoke ? $"{c.S.Charges} grenade(s)." : EoInfo.JammerModes[(int)c.S.Mode] + "."));
            }
            drawer.InfoField("Where (from this sensor, m):\n" + string.Join("\n", where), where.Count + 1);
        });
    }

    

    private static void DrawSmoke(VehicleObjectEditor editor, IGUILayout layout, IGUIElementDrawer drawer, Module m, List<Module> all)
    {
        drawer.Header("Aerosol Grenade Launcher");
        SensorLink(editor, layout, drawer, m, all, "Fires automatically when this sensor detects a threat it reacts to.");

        DesignerPanel.Section(layout, "Aerosol screen", true, () =>
        {
            drawer.Float(DesignerPanel.Prop("Screen distance", () => m.S.ScreenDist, v => DesignerState.Edit(m, s => s.ScreenDist = Mathf.Round(v)),
                ApsSettings.ScreenDistMin, ApsSettings.ScreenDistMax, 1f, "0' m'", "How far from the vehicle the grenades burst (3D17: 50-80 m)."));
            drawer.Float(DesignerPanel.Prop("Screen build-up time", () => m.S.BuildTime, v => DesignerState.Edit(m, s => s.BuildTime = v),
                ApsSettings.BuildMin, ApsSettings.BuildMax, 0.1f, "0.0' s'", "Time for the cloud to grow to full density (3D17: 2-4 s, ROSY: under 1 s)."));
            drawer.Float(DesignerPanel.Prop("Screen duration", () => m.S.Duration, v => DesignerState.Edit(m, s => s.Duration = Mathf.Round(v)),
                ApsSettings.DurationMin, ApsSettings.DurationMax, 1f, "0' s'", "How long the screen stays dense before it thins out (3D17: about 20 s)."));
            DesignerPanel.DrawSector(editor, drawer, m, "covers", vertical: false);
            drawer.InfoField($"Each grenade makes a screen about 20 m wide and 10 m high at {m.S.ScreenDist:0} m, dense after {m.S.BuildTime:0.0} s, " +
                             $"for {m.S.Duration:0} s. The launcher covers threats {(m.S.Full360 ? "all around" : $"within ±{m.S.HalfH:0}° of its direction")}; " +
                             "the screen goes up toward the threat. It blocks sight, thermal imagers, laser rangefinders (they show the range " +
                             "to the cloud), missile guidance and AI spotting.", 6);
        });

        DesignerPanel.Section(layout, "Firing", true, () =>
        {
            FloatProperty grenades = null, perSalvo = null;
            grenades = DesignerPanel.Prop("Grenades", () => m.S.Charges, v =>
            {
                DesignerState.Edit(m, s => s.Charges = Mathf.RoundToInt(v));
                if (perSalvo != null)
                {
                    perSalvo.SetLimits(1f, Math.Min(4, m.S.Charges));
                    DesignerPanel.Refresh(perSalvo, m.S.PerTarget);
                }
            }, 1f, ApsSettings.GrenadesMax, 1f, "0", "Grenades loaded in this block (Shtora-1: 12 in total, split between the launchers).");
            perSalvo = DesignerPanel.Prop("Grenades per salvo", () => m.S.PerTarget, v => DesignerState.Edit(m, s => s.PerTarget = Mathf.RoundToInt(v)),
                1f, Math.Max(1, Math.Min(4, m.S.Charges)), 1f, "0", "More grenades side by side make a wider screen (10 m apart).");
            drawer.Float(grenades);
            drawer.Float(perSalvo);
            drawer.Float(DesignerPanel.Prop("Reaction time", () => m.S.Reaction, v => DesignerState.Edit(m, s => s.Reaction = v),
                0.05f, 5f, 0.05f, "0.00' s'", "From the warning to the launch (MUSS: 1-1.5 s)."));
            drawer.Float(DesignerPanel.Prop("Pause between salvos", () => m.S.Pause, v => DesignerState.Edit(m, s => s.Pause = v),
                0.5f, 30f, 0.5f, "0.0' s'", "The launcher does not fire again before this — so one threat doesn't empty it."));
            drawer.InfoField("Fires automatically when the linked sensor reports:", 1);
            foreach (var t in EoInfo.Each)
            {
                var type = t;
                Il2CppSystem.Action<bool> onToggle = (Action<bool>)(v =>
                {
                    try { DesignerState.Edit(m, s => s.Eo = v ? s.Eo | type : s.Eo & ~type); editor.RequestRedraw(); }
                    catch (Exception e) { Plugin.Log.LogError("EO trigger toggle failed: " + e); }
                });
                drawer.ToggleField(EoInfo.Label(type), (m.S.Eo & type) != 0, onToggle,
                    $"Launch automatically when the linked sensor reports {EoInfo.Name(type).ToLowerInvariant()}. " + EoInfo.Tooltip(type));
            }
            drawer.InfoField($"Manual: press {Plugin.SmokeKeyConfig.Value} in battle — the launchers covering the direction you look at fire a salvo.", 2);
        });
    }

    

    private static void DrawJammer(VehicleObjectEditor editor, IGUILayout layout, IGUIElementDrawer drawer, Module m, List<Module> all)
    {
        drawer.Header("IR Jammer");
        DesignerPanel.Section(layout, "Operation", true, () =>
        {
            var options = new Il2CppSystem.Collections.Generic.List<string>();
            foreach (var n in EoInfo.JammerModes) options.Add(n);
            UnityAction<int> onMode = (Action<int>)(index =>
            {
                try { DesignerState.Edit(m, s => s.Mode = (JammerMode)Mathf.Clamp(index, 0, 2)); editor.RequestRedraw(); }
                catch (Exception e) { Plugin.Log.LogError("Jammer mode failed: " + e); }
            });
            drawer.Dropdown("Mode", options.Cast<Il2CppSystem.Collections.Generic.IReadOnlyList<string>>(), (int)m.S.Mode, onMode,
                "Always on: shines all battle long (Shtora-1). On missile warning: switches on when the linked sensor reports a threat.");
            if (m.S.Mode == JammerMode.OnWarning)
                drawer.Float(DesignerPanel.Prop("Active time after a warning", () => m.S.Duration, v => DesignerState.Edit(m, s => s.Duration = Mathf.Round(v)),
                    ApsSettings.DurationMin, ApsSettings.DurationMax, 1f, "0' s'", "How long it keeps jamming after the last warning."));
            drawer.Float(DesignerPanel.Prop("Jamming chance", () => m.S.JamChance * 100f, v => DesignerState.Edit(m, s => s.JamChance = Mathf.Round(v) / 100f),
                0f, 100f, 1f, "0'%'", "Chance that a SACLOS missile aimed at this vehicle loses guidance (Shtora-1: 60-85%)."));
        });
        if (m.S.Mode == JammerMode.OnWarning) SensorLink(editor, layout, drawer, m, all, "Switches the jammer on when this sensor reports a threat.");

        DesignerPanel.Section(layout, "Jamming sector", true, () =>
        {
            drawer.Float(DesignerPanel.Prop("Range", () => m.S.Range, v => DesignerState.Edit(m, s => s.Range = Mathf.Round(v / 50f) * 50f),
                ApsSettings.EoRangeMin, ApsSettings.EoRangeMax, 50f, "0' m'", "Farthest missile launcher it can blind (Shtora-1: 2-2.5 km)."));
            DesignerPanel.DrawSector(editor, drawer, m, "shines over");
            drawer.InfoField(DesignerPanel.SectorSummary(m.S, "Shines") + $", up to {m.S.Range:0} m (Shtora-1: ±20° by ±4.5° along the gun). " +
                             "It imitates the missile's tracer, so a SACLOS launcher with an IR tracker (Konkurs, TOW, Metis) inside the sector steers " +
                             "its missile away. Missiles riding a laser beam (Kornet, Refleks) are not affected — use aerosol grenades against them. " +
                             "The ATGM's guidance type is set in its gun or launcher panel.", 7);
        });
    }

    
    private static void SensorLink(VehicleObjectEditor editor, IGUILayout layout, IGUIElementDrawer drawer, Module m, List<Module> all, string what)
    {
        var sensors = new List<Module>();
        foreach (var x in all) if (x.Kind == ModuleKind.Warner) sensors.Add(x);
        DesignerPanel.Section(layout, "EO Sensor", true, () =>
        {
            var options = new Il2CppSystem.Collections.Generic.List<string>();
            options.Add("None");
            var selected = 0;
            for (var i = 0; i < sensors.Count; i++)
            {
                options.Add($"Sensor {i + 1} — {DesignerState.Where(m, sensors[i])}");
                if (sensors[i].Vuid == m.S.Detector) selected = i + 1;
            }
            UnityAction<int> onSelect = (Action<int>)(index =>
            {
                try
                {
                    DesignerState.Link(m, index > 0 && index <= sensors.Count ? sensors[index - 1].Vuid : 0);
                    editor.RequestRedraw();
                }
                catch (Exception e) { Plugin.Log.LogError("EO link failed: " + e); }
            });
            drawer.Dropdown("Linked sensor", options.Cast<Il2CppSystem.Collections.Generic.IReadOnlyList<string>>(), selected, onSelect, what);
            drawer.InfoField(sensors.Count == 0 ? "This vehicle has no EO Sensor: place one for automatic operation."
                : selected == 0 ? "Not linked: no automatic operation." : what, 2);
        });
    }
}
