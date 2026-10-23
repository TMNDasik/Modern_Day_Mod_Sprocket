using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Properties;
using Sprocket.Blueprints;
using Sprocket.UI;
using Sprocket.VehicleDesigner.Access;
using Sprocket.Vehicles.Weapons;
using VehicleDesigner.Compartments.Design;

namespace DriveTuning;


internal static class Panels
{
    private static bool _traverseExpanded = true, _layingExpanded = true;
    private static Il2CppSystem.Object _header;

    private static Il2CppSystem.Object Header =>
        _header ??= new Il2CppSystem.Object(IL2CPP.ManagedStringToIl2Cpp("Drive speed"));

    private const string TraverseInfo =
        "0 = the game's own value (from the motor torque, gear ratio and the mass of the turret). Set a value to override it " +
        "without editing any files. Also works for a turret ring mounted on its side to elevate a gun.";

    private const string LayingInfo =
        "0 = the game's own value (from the drive's torque and ratio and the mass of the gun). Set a value to override it " +
        "without editing any files. The acceleration already accounts for the weight of the gun.";

    private sealed class Panel
    {
        public Blueprint Blueprint;
        public bool Laying;
        public Action<DriveSettings> Write;
        public readonly List<FloatProperty> Props = new();
        public int LastUsedFrame;
    }

    private static readonly Dictionary<IntPtr, Panel> PanelsByEditor = new();

    [HarmonyPatch(typeof(TraverseMotorEditor), nameof(TraverseMotorEditor.OnGUI))]
    private static class TraversePanel
    {
        private static void Postfix(TraverseMotorEditor __instance, IGUILayout layout)
        {
            try
            {
                var motor = __instance.Component;
                Blueprint bp = null;
                try { bp = motor?.TraverseBlueprintSlot?.Blueprint; } catch { }
                if (bp == null) return;
                var panel = GetPanel(__instance.Pointer, bp, false, s => Write(bp, s, false,
                    () => __instance.NewBlueprintEditFrame("Drive speed", bp, null, __instance.GetNewOperationGroupID(), VehicleOpExecutionMode.Synchronous)));
                Draw(layout, panel, _traverseExpanded, v => _traverseExpanded = v, TraverseInfo);
            }
            catch (Exception e) { Plugin.Log.LogError("Traverse drive panel failed: " + e); }
        }
    }

    [HarmonyPatch(typeof(LayingDriveEditor), nameof(LayingDriveEditor.OnGUI))]
    private static class LayingPanel
    {
        private static void Postfix(LayingDriveEditor __instance, IGUILayout layout)
        {
            try
            {
                var drive = __instance.Component;
                Blueprint bp = null;
                try { bp = drive?.BlueprintSlot?.Blueprint; } catch { }
                if (bp == null) return;
                var panel = GetPanel(__instance.Pointer, bp, true, s => Write(bp, s, true,
                    () => __instance.NewBlueprintEditFrame("Drive speed", bp, null, __instance.GetNewOperationGroupID(), VehicleOpExecutionMode.Synchronous)));
                Draw(layout, panel, _layingExpanded, v => _layingExpanded = v, LayingInfo);
            }
            catch (Exception e) { Plugin.Log.LogError("Laying drive panel failed: " + e); }
        }
    }

    private static void Draw(IGUILayout layout, Panel panel, bool expanded, Action<bool> setExpanded, string info)
    {
        var drawer = layout.TryCast<IGUIElementDrawer>();
        if (drawer == null) return;
        panel.LastUsedFrame = UnityEngine.Time.frameCount;
        Il2CppSystem.Action<bool> onExpand = (Action<bool>)(v => setExpanded(v));
        layout.BeginDropdown(Header, expanded, onExpand);
        try
        {
            
            var showAz = panel.Laying && HasAzimuth(panel.Blueprint);
            for (var i = 0; i < panel.Props.Count; i++)
            {
                if (i >= 2 && !showAz) break;
                drawer.Float(panel.Props[i]);
            }
            drawer.InfoField(info, Math.Max(1, (int)Math.Ceiling(info.Length / 46.0)));
        }
        finally { layout.EndDropdown(); }
    }

    private static bool HasAzimuth(Blueprint bp)
    {
        try
        {
            var az = bp.TryCast<LayingDriveBlueprint>()?.Azimuth;
            return az != null && az.Max - az.Min > 0.01f;
        }
        catch { return false; }
    }

    private static Panel GetPanel(IntPtr editor, Blueprint bp, bool laying, Action<DriveSettings> write)
    {
        if (PanelsByEditor.TryGetValue(editor, out var panel) && panel.Blueprint != null && panel.Blueprint.Pointer == bp.Pointer)
        {
            panel.Write = write;
            return panel;
        }
        if (PanelsByEditor.Count > 32)
        {
            var now = UnityEngine.Time.frameCount;
            var stale = new List<IntPtr>();
            foreach (var kv in PanelsByEditor) if (now - kv.Value.LastUsedFrame > 600) stale.Add(kv.Key);
            foreach (var k in stale) PanelsByEditor.Remove(k);
        }
        panel = new Panel { Blueprint = bp, Laying = laying, Write = write };
        var p = panel;
        var speedMax = Math.Max(10f, Plugin.MaxSpeedLimit.Value);
        var accelMax = Math.Max(10f, Plugin.MaxAccelLimit.Value);

        FloatProperty Prop(string name, string tip, float max, float step, Func<DriveSettings, float> get, Func<DriveSettings, float, DriveSettings> set)
        {
            Il2CppSystem.Func<float> g = (Func<float>)(() =>
            {
                try { return get(DriveSettings.Read(p.Blueprint)); }
                catch { return 0f; }
            });
            Il2CppSystem.Action<float> s = (Action<float>)(v =>
            {
                try
                {
                    var value = v < step * 0.5f ? 0f : (float)Math.Round(v / step) * step;
                    p.Write(set(DriveSettings.Read(p.Blueprint), value));
                }
                catch (Exception e) { Plugin.Log.LogError("Drive setting change failed: " + e); }
            });
            return new FloatProperty(name, g, s, 0f, max, step, "0", null, tip);
        }

        if (laying)
        {
            p.Props.Add(Prop("Elevation speed", "Largest elevation speed of the gun, °/s. 0 = the game's value.", speedMax, 1f,
                x => x.Speed, (x, v) => { x.Speed = v; return x; }));
            p.Props.Add(Prop("Elevation accel.", "Elevation acceleration, °/s². 0 = the game's value.", accelMax, 5f,
                x => x.Accel, (x, v) => { x.Accel = v; return x; }));
            p.Props.Add(Prop("Azimuth speed", "Largest traverse speed of the mantlet, °/s. 0 = the game's value.", speedMax, 1f,
                x => x.AzSpeed, (x, v) => { x.AzSpeed = v; return x; }));
            p.Props.Add(Prop("Azimuth accel.", "Traverse acceleration of the mantlet, °/s². 0 = the game's value.", accelMax, 5f,
                x => x.AzAccel, (x, v) => { x.AzAccel = v; return x; }));
        }
        else
        {
            p.Props.Add(Prop("Rotation speed", "Largest rotation speed of the turret ring, °/s. 0 = the game's value.", speedMax, 1f,
                x => x.Speed, (x, v) => { x.Speed = v; return x; }));
            p.Props.Add(Prop("Rotation accel.", "Rotation acceleration of the turret ring, °/s². 0 = the game's value.", accelMax, 5f,
                x => x.Accel, (x, v) => { x.Accel = v; return x; }));
        }
        PanelsByEditor[editor] = panel;
        return panel;
    }

    
    private static void Write(Blueprint bp, DriveSettings s, bool laying, Func<Il2CppSystem.IDisposable> openFrame)
    {
        var text = DriveSettings.With(bp.Description, s, laying);
        if (text == bp.Description) return;
        Il2CppSystem.IDisposable frame = null;
        try { frame = openFrame(); }
        catch (Exception e) { if (Plugin.DebugLogging.Value) Plugin.Log.LogWarning("Edit frame unavailable: " + e.Message); }
        try { bp.Description = text; }
        finally
        {
            if (frame != null) frame.Dispose();
            else bp.MarkModified();
        }
    }
}
