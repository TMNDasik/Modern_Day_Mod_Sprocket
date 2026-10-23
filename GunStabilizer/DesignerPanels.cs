using System;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Sprocket.Blueprints;
using Sprocket.UI;
using Sprocket.VehicleDesigner.Access;
using Sprocket.Vehicles.Weapons;
using VehicleDesigner.Compartments.Design;

namespace GunStabilizer;




internal static class DesignerPanels
{
    private static bool _motorExpanded = true, _driveExpanded = true;
    private static Il2CppSystem.Object _header;

    private static Il2CppSystem.Object Header =>
        _header ??= new Il2CppSystem.Object(IL2CPP.ManagedStringToIl2Cpp("Stabilizer"));

    [HarmonyPatch(typeof(TraverseMotorEditor), nameof(TraverseMotorEditor.OnGUI))]
    private static class TraverseMotorPanel
    {
        private static void Postfix(TraverseMotorEditor __instance, IGUILayout layout)
        {
            try
            {
                var motor = __instance.Component;
                var bp = StabSettings.Blueprint(motor);
                if (bp == null) return;
                var on = StabSettings.Has(bp);
                var powered = StabSettings.PoweredTraverse(motor);
                var info = on ? StabSpec.HorizontalInfo + " " + StabSpec.CommonInfo : StabSpec.OffInfo;
                Draw(layout, on, _motorExpanded, v => _motorExpanded = v,
                    "Horizontal (azimuth) channel of a two-plane gun stabilizer",
                    info, on && !powered ? StabSpec.NoPowerTraverse : null,
                    v => SetFlag(bp, v, () => __instance.NewBlueprintEditFrame("Stabilizer", bp, null,
                        __instance.GetNewOperationGroupID(), VehicleOpExecutionMode.Synchronous)),
                    () => __instance.RequestRedraw());
            }
            catch (Exception e) { Plugin.Log.LogError("Traverse motor stabilizer panel failed: " + e); }
        }
    }

    [HarmonyPatch(typeof(LayingDriveEditor), nameof(LayingDriveEditor.OnGUI))]
    private static class LayingDrivePanel
    {
        private static void Postfix(LayingDriveEditor __instance, IGUILayout layout)
        {
            try
            {
                var drive = __instance.Component;
                var bp = StabSettings.Blueprint(drive);
                if (bp == null) return;
                var on = StabSettings.Has(bp);
                var info = on ? StabSpec.VerticalInfo + " " + StabSpec.CommonInfo : StabSpec.OffInfo;
                Draw(layout, on, _driveExpanded, v => _driveExpanded = v,
                    "Vertical (elevation) channel of a two-plane gun stabilizer",
                    info, null,
                    v => SetFlag(bp, v, () => __instance.NewBlueprintEditFrame("Stabilizer", bp, null,
                        __instance.GetNewOperationGroupID(), VehicleOpExecutionMode.Synchronous)),
                    () => __instance.RequestRedraw());
            }
            catch (Exception e) { Plugin.Log.LogError("Laying drive stabilizer panel failed: " + e); }
        }
    }

    private static void Draw(IGUILayout layout, bool on, bool expanded, Action<bool> setExpanded, string tooltip,
        string info, string warning, Action<bool> setFlag, Action redraw)
    {
        var drawer = layout.TryCast<IGUIElementDrawer>();
        if (drawer == null) return;

        Il2CppSystem.Action<bool> onExpand = (Action<bool>)(v => setExpanded(v));
        Il2CppSystem.Action<bool> onToggle = (Action<bool>)(v =>
        {
            try
            {
                setFlag(v);
                redraw();
            }
            catch (Exception e) { Plugin.Log.LogError("Stabilizer toggle failed: " + e); }
        });

        layout.BeginDropdown(Header, expanded, onExpand);
        try
        {
            drawer.ToggleField("Stabilizer", on, onToggle, tooltip);
            if (warning != null) drawer.InfoField(warning, Lines(warning), new UnityEngine.Color(1f, 0.55f, 0.35f));
            drawer.InfoField(info, Lines(info));
        }
        finally { layout.EndDropdown(); }
    }

    
    private static int Lines(string text) => Math.Max(1, (int)Math.Ceiling(text.Length / 46.0));

    
    private static void SetFlag(Blueprint bp, bool on, Func<Il2CppSystem.IDisposable> openFrame)
    {
        if (StabSettings.Has(bp) == on) return;
        Il2CppSystem.IDisposable frame = null;
        try { frame = openFrame(); }
        catch (Exception e)
        {
            if (Plugin.DebugLogging.Value) Plugin.Log.LogWarning("Edit frame unavailable: " + e.Message);
        }
        try { bp.Description = StabSettings.With(bp.Description, on); }
        finally
        {
            if (frame != null) frame.Dispose();
            else bp.MarkModified();
        }
        if (Plugin.DebugLogging.Value) Plugin.Log.LogInfo($"Stabilizer {(on ? "on" : "off")}: {bp.GetType().Name}");
    }
}
