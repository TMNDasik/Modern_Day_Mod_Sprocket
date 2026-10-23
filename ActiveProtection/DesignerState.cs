using System;
using System.Collections.Generic;
using HarmonyLib;
using Sprocket.VehicleDesigner;
using Sprocket.Vehicles;
using UnityEngine;

namespace ActiveProtection;



internal static class DesignerState
{
    private static VehicleEditor _editor;
    private static int _lastFrame = -100;
    private static readonly List<Module> Selection = new();
    private static int _selectionFrame = -1;

    [HarmonyPatch(typeof(VehicleEditor), nameof(VehicleEditor.EditorUpdate))]
    private static class EditorHook
    {
        private static void Postfix(VehicleEditor __instance)
        {
            _editor = __instance;
            _lastFrame = Time.frameCount;
        }
    }

    public static bool Active => _editor != null && Time.frameCount - _lastFrame <= 3;

    
    public static Operations.IOperatable Executer
    {
        get
        {
            if (!Active) return null;
            try { return _editor.OperationExecuter; }
            catch { return null; }
        }
    }

    
    private static readonly HashSet<IntPtr> SelectedObjects = new();
    private static int _objectsFrame = -1;

    public static bool IsSelected(IntPtr vo)
    {
        if (_objectsFrame != Time.frameCount)
        {
            _objectsFrame = Time.frameCount;
            SelectedObjects.Clear();
            if (Active)
                try
                {
                    var reader = _editor.SelectionReader;
                    var items = reader?.Items;
                    var count = reader?.Count ?? 0;
                    for (var i = 0; items != null && i < count; i++)
                    {
                        var o = items[i]?.VehicleObject;
                        if (o != null) SelectedObjects.Add(o.Pointer);
                    }
                }
                catch { }
        }
        return SelectedObjects.Contains(vo);
    }

    
    public static List<Module> Selected()
    {
        if (_selectionFrame == Time.frameCount) return Selection;
        _selectionFrame = Time.frameCount;
        Selection.Clear();
        if (!Active) return Selection;
        try
        {
            var reader = _editor.SelectionReader;
            if (reader == null) return Selection;
            var count = reader.Count;
            var items = reader.Items;
            if (items == null) return Selection;
            for (var i = 0; i < count; i++)
            {
                var c = items[i];
                var vo = c != null ? c.VehicleObject : null;
                var m = vo != null ? Modules.Get(vo) : null;
                if (m != null && !Selection.Contains(m)) Selection.Add(m);
            }
        }
        catch (Exception e)
        {
            if (Plugin.DebugLogging.Value) Plugin.Log.LogWarning("APS selection read failed: " + e.Message);
        }
        return Selection;
    }

    

    
    public static void Edit(Module m, Action<ApsSettings> change)
    {
        var s = m.S.Clone();
        change(s);
        m.SetSettings(s);
        SyncMirror(m);
    }

    public static void SyncMirror(Module m)
    {
        var partner = Modules.MirrorOf(m);
        if (partner == null || partner == m || partner.Kind != m.Kind) return;
        var s = m.S.Mirrored();
        if (EoInfo.LinkTarget(m.Kind) != null) s.Detector = MirrorDetectorVuid(m);
        if (m.Kind == ModuleKind.Panel)
        {
            
            var racks = Blowoff.RacksOf(Modules.VehicleOf(m));
            if (s.AutoRacks) s.Racks = Blowoff.AutoLinked(partner, racks);
            else
            {
                var list = new List<int>();
                foreach (var r in m.S.Racks)
                {
                    var mr = Blowoff.MirrorVuid(racks, r);
                    if (!list.Contains(mr)) list.Add(mr);
                }
                s.Racks = list.ToArray();
            }
        }
        partner.SetSettings(s);
    }

    private static int MirrorDetectorVuid(Module launcher)
    {
        var det = launcher.S.Detector;
        if (det == 0) return 0;
        var v = Modules.VehicleOf(launcher);
        var target = EoInfo.LinkTarget(launcher.Kind);
        if (target == null) return det;
        foreach (var d in Modules.OnVehicle(v, target.Value))
        {
            if (d.Vuid != det) continue;
            var dm = Modules.MirrorOf(d);
            return dm != null ? dm.Vuid : det;
        }
        return det;
    }

    
    public static void Link(Module launcher, int detectorVuid)
    {
        Edit(launcher, s =>
        {
            s.Detector = detectorVuid;
            s.LinkSet = true;
        });
    }

    

    private static float _nextFixup;

    public static void Tick()
    {
        if (!Active || Time.unscaledTime < _nextFixup) return;
        _nextFixup = Time.unscaledTime + 0.3f;
        try
        {
            var all = Modules.Snapshot();
            if (all.Count == 0) return;
            var list = new List<Module>(all);
            Dictionary<IntPtr, List<Blowoff.RackInfo>> racksByVehicle = null;
            foreach (var m in list)
            {
                var v = Modules.VehicleOf(m);
                if (v == null) continue;
                try { if (v.BehaviourEnabled) continue; } catch { }

                
                if (m.Kind == ModuleKind.Panel && m.S.AutoRacks)
                {
                    try { if (m.Vo.VehicleTransform == null || !m.Vo.VehicleTransform.IsAttached) continue; } catch { continue; }
                    racksByVehicle ??= new Dictionary<IntPtr, List<Blowoff.RackInfo>>();
                    if (!racksByVehicle.TryGetValue(v.Pointer, out var racks)) racksByVehicle[v.Pointer] = racks = Blowoff.RacksOf(v);
                    var auto = Blowoff.AutoLinked(m, racks);
                    if (!Blowoff.SameList(auto, m.S.Racks))
                    {
                        var s = m.S.Clone();
                        s.Racks = auto;
                        m.SetSettings(s);
                    }
                    continue;
                }

                
                var partner = Modules.MirrorOf(m);
                if (partner != null && partner.Kind == m.Kind && Modules.IsMirroredCopy(m) &&
                    Mathf.Abs(m.S.Azimuth) > 0.01f && m.S.SameSector(partner.S))
                {
                    var s = partner.S.Mirrored();
                    if (EoInfo.LinkTarget(m.Kind) != null)
                    {
                        s.Detector = m.S.Detector;
                        s.LinkSet = m.S.LinkSet;
                    }
                    m.SetSettings(s);
                }

                
                var linkTarget = EoInfo.LinkTarget(m.Kind);
                if (linkTarget != null && !m.AutoLinkDone)
                {
                    if (m.FromFile || m.S.LinkSet || m.S.Detector != 0) { m.AutoLinkDone = true; continue; }
                    
                    try { if (m.Vo.VehicleTransform == null || !m.Vo.VehicleTransform.IsAttached) continue; } catch { continue; }
                    var detectors = Modules.OnVehicle(v, linkTarget.Value);
                    if (detectors.Count == 0) continue; 
                    Module best = null;
                    var bestDist = float.MaxValue;
                    foreach (var d in detectors)
                    {
                        var dist = (d.Center - m.Center).sqrMagnitude;
                        if (dist < bestDist) { bestDist = dist; best = d; }
                    }
                    if (best != null)
                    {
                        var s = m.S.Clone();
                        s.Detector = best.Vuid;
                        m.SetSettings(s);
                        m.AutoLinkDone = true;
                    }
                }
            }
        }
        catch (Exception e)
        {
            if (Plugin.DebugLogging.Value) Plugin.Log.LogWarning("APS designer tick failed: " + e.Message);
        }
    }

    
    public static string Where(Module from, Module to)
    {
        try
        {
            var v = Modules.VehicleOf(from);
            var frame = v != null ? v.transform : from.T;
            var local = frame.InverseTransformDirection(to.Center - from.Center);
            string Part(float value, string plus, string minus) =>
                Mathf.Abs(value) < 0.05f ? null : $"{Mathf.Abs(value):0.0} m {(value > 0 ? plus : minus)}";
            var parts = new List<string>();
            var x = Part(local.x, "right", "left");
            var z = Part(local.z, "ahead", "behind");
            var y = Part(local.y, "above", "below");
            if (x != null) parts.Add(x);
            if (z != null) parts.Add(z);
            if (y != null) parts.Add(y);
            return parts.Count == 0 ? "right here" : string.Join(", ", parts);
        }
        catch { return "?"; }
    }

    
    public static string WhereShort(Module from, Module to) => WhereShort(from, to.Center);

    public static string WhereShort(Module from, Vector3 to)
    {
        try
        {
            var v = Modules.VehicleOf(from);
            var frame = v != null ? v.transform : from.T;
            var local = frame.InverseTransformDirection(to - from.Center);
            string Part(float value, string plus, string minus) =>
                Mathf.Abs(value) < 0.05f ? null : $"{(value > 0 ? plus : minus)} {Mathf.Abs(value):0.0}";
            var parts = new List<string>();
            var x = Part(local.x, "right", "left");
            var z = Part(local.z, "ahead", "behind");
            var y = Part(local.y, "up", "down");
            if (x != null) parts.Add(x);
            if (z != null) parts.Add(z);
            if (y != null) parts.Add(y);
            return parts.Count == 0 ? "right here" : string.Join(", ", parts);
        }
        catch { return "?"; }
    }
}
