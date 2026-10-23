using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Properties;
using Sprocket.UI;
using Sprocket.VehicleDesigner.Access;
using Sprocket.Vehicles;
using Sprocket.Vehicles.Cannons;
using Sprocket.Vehicles.Cannons.Editor;
using UnityEngine;

namespace Munitions;




internal static class EjectionPanel
{
    private static bool _expanded = true;
    private static Il2CppSystem.Object _header;

    private static Il2CppSystem.Object Header =>
        _header ??= new Il2CppSystem.Object(IL2CPP.ManagedStringToIl2Cpp("Casing ejection"));

    private sealed class Panel
    {
        public CannonEditor Editor;
        public FloatProperty Port;
        public int LastUsedFrame;
    }

    private static readonly Dictionary<IntPtr, Panel> Panels = new();

    [HarmonyPatch(typeof(CannonEditor), nameof(CannonEditor.OnGUI))]
    private static class GunPanel
    {
        private static void Postfix(CannonEditor __instance, IGUILayout layout)
        {
            try
            {
                var cannon = __instance.Component;
                if (cannon == null || LauncherPart.IsLauncher(cannon) || AutoWeaponPart.IsAuto(cannon)) return;
                var bp = cannon.Blueprint;
                if (bp == null || bp.Caliber < Plugin.MinCaliber.Value) return;
                var ports = Casings.PortsOf(Casings.VehicleRoot(cannon));
                if (ports.Count == 0) return;
                var drawer = layout.TryCast<IGUIElementDrawer>();
                if (drawer == null) return;

                var panel = GetPanel(__instance);
                panel.LastUsedFrame = Time.frameCount;
                panel.Port.SetLimits(0f, ports.Count);

                Il2CppSystem.Action<bool> onExpand = (Action<bool>)(v => _expanded = v);
                layout.BeginDropdown(Header, _expanded, onExpand);
                try
                {
                    drawer.Float(panel.Port);
                    var info = Describe(cannon, ports);
                    drawer.InfoField(info, Math.Max(1, (int)Math.Ceiling(info.Length / 46.0)));
                }
                finally { layout.EndDropdown(); }
            }
            catch (Exception e) { Plugin.Log.LogError("Casing ejection panel failed: " + e); }
        }
    }

    
    private static string Describe(Cannon cannon, List<VehicleObject> ports)
    {
        var index = BoundIndex(cannon, ports);
        if (index == 0)
        {
            var auto = Casings.PortFor(cannon);
            var autoIndex = auto != null ? ports.FindIndex(p => p.transform == auto) + 1 : 0;
            return "0 = the nearest free port (ports bound to other guns are skipped). " +
                   (autoIndex > 0 ? $"Now: port {autoIndex} of {ports.Count}, {Where(cannon, auto)}." : "Now: none — the other guns are nearer to all free ports.") +
                   " Cases fly out where the port faces, when the next round is loaded.";
        }
        var port = ports[index - 1].transform;
        return $"Port {index} of {ports.Count}: {Where(cannon, port)}. Cases fly out where the port faces, when the next round is loaded.";
    }

    
    private static string Where(Cannon cannon, Transform port)
    {
        var local = cannon.transform.InverseTransformPoint(port.position);
        string Part(float v, string plus, string minus) => Mathf.Abs(v) < 0.05f ? null : $"{Mathf.Abs(v):0.0} m {(v > 0 ? plus : minus)}";
        var parts = new List<string>();
        var x = Part(local.x, "right", "left");
        var z = Part(local.z, "ahead", "behind");
        var y = Part(local.y, "above", "below");
        if (x != null) parts.Add(x);
        if (z != null) parts.Add(z);
        if (y != null) parts.Add(y);
        return parts.Count == 0 ? "at the gun" : string.Join(", ", parts) + " of the gun";
    }

    private static int BoundIndex(Cannon cannon, List<VehicleObject> ports)
    {
        var bound = EjectBinding.Read(cannon);
        if (bound == 0) return 0;
        for (var i = 0; i < ports.Count; i++) if (Casings.Vuid(ports[i]) == bound) return i + 1;
        return 0; 
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
        Il2CppSystem.Func<float> get = (Func<float>)(() =>
        {
            try
            {
                var cannon = p.Editor.Component;
                return cannon == null ? 0f : BoundIndex(cannon, Casings.PortsOf(Casings.VehicleRoot(cannon)));
            }
            catch { return 0f; }
        });
        Il2CppSystem.Action<float> set = (Action<float>)(v =>
        {
            try { Bind(p.Editor, (int)Math.Round(v)); }
            catch (Exception e) { Plugin.Log.LogError("Casing ejection binding failed: " + e); }
        });
        panel.Port = new FloatProperty("Ejection port", get, set, 0f, 1f, 1f, "0", null,
            "Which casing ejection port this gun throws its spent cases out of. 0 = the nearest free one.");
        Panels[editor.Pointer] = panel;
        return panel;
    }

    
    private static void Bind(CannonEditor editor, int index)
    {
        var cannon = editor.Component;
        var ib = cannon?.InstanceBlueprint;
        if (ib == null) return;
        var ports = Casings.PortsOf(Casings.VehicleRoot(cannon));
        var vuid = index > 0 && index <= ports.Count ? Casings.Vuid(ports[index - 1]) : 0;
        var text = EjectBinding.With(ib.Description, vuid);
        if (text == ib.Description) return;
        Il2CppSystem.IDisposable frame = null;
        try { frame = editor.NewBlueprintEditFrame("Casing ejection", ib, null, editor.GetNewOperationGroupID(), VehicleOpExecutionMode.Synchronous); }
        catch (Exception e) { if (Plugin.DebugLogging.Value) Plugin.Log.LogWarning("Edit frame unavailable: " + e.Message); }
        try { ib.Description = text; }
        finally { if (frame != null) frame.Dispose(); else ib.MarkModified(); }
    }
}
