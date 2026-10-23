using System;
using HarmonyLib;
using Sprocket.UI;
using Sprocket.VehicleDesigner.Access;
using Sprocket.Vehicles.Transmissions;
using Sprocket.Vehicles.Transmissions.Editor;

namespace Powertrain;



[HarmonyPatch(typeof(TransmissionEditor), nameof(TransmissionEditor.OnGUI))]
internal static class GearboxPanel
{
    private static int _errors;

    private static void Prefix(TransmissionEditor __instance, IGUILayout layout)
    {
        try
        {
            var drawer = layout.TryCast<IGUIElementDrawer>();
            var block = __instance.Component;
            var bp = block != null ? block.Blueprint : null;
            if (drawer == null || bp == null) return;
            var manual = GearboxSettings.IsManual(bp);
            var editor = __instance;
            Il2CppSystem.Action<bool> onToggle = (Action<bool>)(automatic =>
            {
                try { SetManual(editor, !automatic); }
                catch (Exception e) { Plugin.Log.LogError("Gearbox type change failed: " + e); }
            });
            drawer.ToggleField("Automatic", !manual, onToggle,
                "On: automatic gearbox — you choose D, N or R and the driver changes the gears. Off: manual gearbox — you choose " +
                "every gear yourself (R, N, 1, 2, …). In battle E shifts up, Q down, or hold Alt and move the mouse.");
        }
        catch (Exception e)
        {
            if (_errors++ < 5) Plugin.Log.LogError("Gearbox panel failed: " + e);
        }
    }

    private static void SetManual(TransmissionEditor editor, bool manual)
    {
        var bp = editor.Component?.Blueprint;
        if (bp == null) return;
        var text = GearboxSettings.With(bp.Description, manual);
        if (text == bp.Description) return;
        Il2CppSystem.IDisposable frame = null;
        try { frame = editor.NewBlueprintEditFrame("Gearbox type", bp, null, editor.GetNewOperationGroupID(), VehicleOpExecutionMode.Synchronous); }
        catch (Exception e) { if (Plugin.DebugLogging.Value) Plugin.Log.LogWarning("Edit frame unavailable: " + e.Message); }
        try { bp.Description = text; }
        finally
        {
            if (frame != null) frame.Dispose();
            else bp.MarkModified();
        }
        try { editor.RequestRedraw(); }
        catch { }
    }
}
