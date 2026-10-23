using System;
using Properties;
using Sprocket.UI;
using Sprocket.VehicleDesigner.Modules;
using UnityEngine;

namespace ActiveProtection;



internal static class EraPanel
{
    public static void Draw(VehicleObjectEditor editor, IGUILayout layout, IGUIElementDrawer drawer, Module m)
    {
        drawer.Header("ERA Block");

        DesignerPanel.Section(layout, "Tiles", true, () =>
        {
            var size = m.Size * 1000f;
            drawer.InfoField($"Block {size.x:0} x {size.y:0} mm, {size.z:0} mm thick. Resize it with the scale tool " +
                             "(depth = how far it sticks out of the armour).", 2);
            drawer.Float(DesignerPanel.Prop("Tile width", () => m.S.TileWidth, v => DesignerState.Edit(m, s => s.TileWidth = Mathf.Round(v)),
                ApsSettings.TileMin, ApsSettings.TileMax, 5f, "0' mm'",
                "Width of one ERA element. Each element goes off once: a second hit on the same element gets no protection. " +
                "Kontakt-1 brick ~250 mm, Kontakt-5 element ~300-500 mm."));
            drawer.Float(DesignerPanel.Prop("Tile height", () => m.S.TileHeight, v => DesignerState.Edit(m, s => s.TileHeight = Mathf.Round(v)),
                ApsSettings.TileMin, ApsSettings.TileMax, 5f, "0' mm'", "Height of one ERA element (Kontakt-1 brick ~130 mm)."));
            drawer.Readonly(Readonly("Tiles in the block", () => m.SyncScale().TilesX * m.S.TilesY, "0",
                "Number of elements (the grid lines on the box). The last row/column may be smaller."));
            drawer.InfoField("Seen only here in the designer (orange box): invisible in test drives, battles and menus. " +
                             "Build the bricks you see from thin plates and put this block over them.", 3);
        });

        DesignerPanel.Section(layout, "Protection", true, () =>
        {
            drawer.Float(Percent("Against HEAT shells", () => m.S.CutHeat, v => DesignerState.Edit(m, s => s.CutHeat = v),
                "How much penetration a HEAT shell (single shaped charge) loses when it sets off a tile. " +
                "Light ERA (Kontakt-1, Blazer) ~50-60%, heavy (Kontakt-5) ~60-70%, Relikt ~70%."));
            drawer.Float(Percent("Against APFSDS/AP", () => m.S.CutKinetic, v => DesignerState.Edit(m, s => s.CutKinetic = v),
                "Penetration lost by APFSDS darts and full-calibre AP shells. Light ERA: 0% (it can't stop a rod), " +
                "Kontakt-5 ~20-40%, Relikt ~40-50%."));
            drawer.Float(Percent("Against tandem ATGM", () => m.S.CutTandem, v => DesignerState.Edit(m, s => s.CutTandem = v),
                "Guided missiles have tandem warheads: the small front charge sets the ERA off before the main jet arrives. " +
                "Light and heavy ERA: ~0-20%, two-layer ERA built against tandems (Relikt) ~40-50%."));
            drawer.Float(Percent("Straight-on effect", () => m.S.HeadOn, v => DesignerState.Edit(m, s => s.HeadOn = v),
                "ERA plates work by moving across the jet or rod, so they need a slanted hit: full effect from " +
                $"{ApsSettings.FullEffectAngle:0}° off the block's face direction, this share of it for a hit straight into the face " +
                "(in between it changes smoothly). Real ERA is mounted at 60-70° to the expected threat."));
            drawer.Float(DesignerPanel.Prop("Set off from", () => m.S.TriggerCaliber, v => DesignerState.Edit(m, s => s.TriggerCaliber = Mathf.Round(v)),
                ApsSettings.TriggerMin, ApsSettings.TriggerMax, 1f, "0' mm'",
                "Smaller bullets and shells don't set the tiles off (they pass as if there were no ERA). Real ERA ignores rifle " +
                "and heavy machine-gun bullets; Kontakt-5 is made not to go off from 30 mm autocannon rounds either."));
            drawer.InfoField("HE shells also set tiles off (wasting them) but are not weakened. Your own vehicle's shots never set its ERA off.", 2);
        });

        DesignerPanel.Section(layout, "Mass", true, () =>
        {
            drawer.Float(DesignerPanel.Prop("Mass per m²", () => m.S.ArealMass, v => DesignerState.Edit(m, s => s.ArealMass = Mathf.Round(v)),
                ApsSettings.ArealMassMin, ApsSettings.ArealMassMax, 5f, "0' kg/m²'",
                "Weight of the ERA per square metre of the block's face (containers, explosive, plates). Kontakt-1 ~150, " +
                "Kontakt-5 ~250-300, Relikt ~300."));
            drawer.Readonly(Readonly("Block mass", () => m.SyncScale().EraMass, "0' kg'", "Added to the vehicle as armour mass."));
        });
    }

    private static FloatProperty Percent(string name, Func<float> get, Action<float> set, string tooltip) =>
        DesignerPanel.Prop(name, () => get() * 100f, v => set(Mathf.Round(v) / 100f), 0f, 100f, 1f, "0'%'", tooltip);

    internal static FloatProperty Readonly(string name, Func<float> get, string format, string tooltip)
    {
        Il2CppSystem.Func<float> g = (Func<float>)(() =>
        {
            try { return get(); }
            catch { return 0f; }
        });
        Il2CppSystem.Action<float> none = (Action<float>)(_ => { });
        return new FloatProperty(name, g, none, 0f, 1000000f, 1f, format, null, tooltip);
    }
}
