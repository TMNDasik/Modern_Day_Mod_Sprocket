using System;
using System.Collections.Generic;
using Properties;
using Sprocket.UI;
using Sprocket.VehicleDesigner.Modules;
using UnityEngine;

namespace ActiveProtection;



internal static class BlowoffPanel
{
    public static void Draw(VehicleObjectEditor editor, IGUILayout layout, IGUIElementDrawer drawer, Module m)
    {
        drawer.Header("Blow-off Panel");
        var vehicle = Modules.VehicleOf(m);
        var racks = Blowoff.RacksOf(vehicle);

        DesignerPanel.Section(layout, "Panel", true, () =>
        {
            var size = m.Size * 1000f;
            drawer.InfoField($"{size.x:0} x {size.y:0} mm, {size.z:0} mm thick. Resize it with the scale tool " +
                             "(depth = sheet thickness). Abrams turret bustle: two panels ~1200 x 600 mm.", 2);
            drawer.Readonly(EraPanel.Readonly("Mass", () => m.SyncScale().PanelMass, "0' kg'", "Steel sheet, added to the vehicle as armour mass."));
            drawer.InfoField("The panel is only a cover: build the compartment walls, floor and roof armour yourself, " +
                             "and keep the crew out of it.", 2);
        });

        DesignerPanel.Section(layout, "Protected ammo", true, () =>
        {
            Il2CppSystem.Action<bool> onAuto = (Action<bool>)(v =>
            {
                try
                {
                    DesignerState.Edit(m, s =>
                    {
                        s.AutoRacks = v;
                        if (v) s.Racks = Blowoff.AutoLinked(m, racks);
                    });
                    editor.RequestRedraw();
                }
                catch (Exception e) { Plugin.Log.LogError("Blow-off auto toggle failed: " + e); }
            });
            drawer.ToggleField("Racks below", m.S.AutoRacks, onAuto,
                "On: the panel protects every ammo rack under it (its centre within the panel's outline, up to " +
                "2.5 m deep), and follows when you move racks. Off: tick the racks yourself below.");

            if (racks.Count == 0)
            {
                drawer.InfoField("No ammo racks on this vehicle yet. Place racks in the compartment under the panel.", 2);
                return;
            }

            var where = new List<string>();
            var protectedRacks = 0;
            var protectedRounds = 0;
            var litres = 0f;
            for (var i = 0; i < racks.Count; i++)
            {
                var r = racks[i];
                var vuid = r.Vuid;
                var on = m.S.Protects(vuid);
                if (on)
                {
                    protectedRacks++;
                    protectedRounds += Math.Max(0, r.Capacity);
                    litres += Math.Max(0, r.Capacity) * Blowoff.RoundLitres(r.Caliber, r.Length);
                }
                var under = Blowoff.Under(m, r.Center);
                var size = r.Caliber > 0 ? $"{r.Caliber} mm" : "?";
                where.Add($"{i + 1}: {size} x{r.Capacity} — {DesignerState.WhereShort(m, r.Center)}{(under ? " (under)" : "")}");
                Il2CppSystem.Action<bool> onToggle = (Action<bool>)(v =>
                {
                    try
                    {
                        DesignerState.Edit(m, s =>
                        {
                            var list = new List<int>(s.Racks);
                            list.Remove(vuid);
                            if (v) list.Add(vuid);
                            s.Racks = list.ToArray();
                            s.AutoRacks = false;
                        });
                        editor.RequestRedraw();
                    }
                    catch (Exception e) { Plugin.Log.LogError("Blow-off rack toggle failed: " + e); }
                });
                drawer.ToggleField($"Rack {i + 1}", on, onToggle,
                    $"{size} rack, {r.Capacity} round(s), {DesignerState.WhereShort(m, r.Center)} from this panel" +
                    $"{(under ? " (under it)" : "")}. Ticked: its cook-off vents through this panel.");
            }
            drawer.InfoField("Where (from this panel, m):\n" + string.Join("\n", where), where.Count + 1);
            drawer.InfoField(protectedRacks == 0
                ? "No racks protected: a cook-off anywhere in this vehicle works as usual (the crew compartment burns)."
                : $"Protects {protectedRacks} rack(s), up to {protectedRounds} round(s) (~{litres:0} l of propellant). " +
                  "All of them burn out together when one cooks off.", 2);
        });

        DesignerPanel.Section(layout, "Blast door", true, () =>
        {
            drawer.Float(DesignerPanel.Prop("Door open time", () => m.S.DoorTime, v => DesignerState.Edit(m, s => s.DoorTime = Mathf.Round(v * 10f) / 10f),
                0f, ApsSettings.DoorTimeMax, 0.1f, "0.0' s'",
                "The ammo door between the compartment and the crew is open this long each time the loader takes a round from " +
                "a protected rack (Abrams: ~2 s, opened with a knee switch). A cook-off while it is open goes into the crew " +
                "compartment as usual. 0 = always closed (rounds fed by an autoloader through a small hatch, like Object 640)."));
            drawer.InfoField(m.S.DoorTime <= 0f
                ? "Always closed: every cook-off of the protected racks vents through the roof."
                : $"Open {m.S.DoorTime:0.0} s per round taken from these racks; the rest of the time the crew is protected.", 2);
        });

        DesignerPanel.Section(layout, "In battle", false, () =>
        {
            drawer.InfoField("When a protected rack cooks off with the door closed, the panels over it fly off and a jet of flame " +
                             "and black smoke roars out of the roof for several seconds (longer with more propellant). All ammo in " +
                             "the compartment is lost; the crew and the rest of the vehicle are untouched and keep fighting. " +
                             "Racks that no panel protects cook off as usual.", 5);
        });
    }
}
