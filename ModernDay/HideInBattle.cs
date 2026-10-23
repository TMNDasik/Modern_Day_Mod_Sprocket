using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Sprocket.UI;
using Sprocket.VehicleDesigner.Modules;
using Sprocket.Vehicles;
using Sprocket.Vehicles.Serialization;
using UnityEngine;

namespace ModernDay;











internal static class HideInBattle
{
    private const string Key = "mdhide";

    private sealed class Entry
    {
        public VehicleObject Vo;
        public string Name;
        public bool Hidden;                    
        public readonly List<VehicleRendererGroup> Groups = new();   
        public readonly List<Renderer> Plain = new();                
        public float NextScan;
        public bool Dynamic;                   
    }

    private static readonly Dictionary<IntPtr, Entry> Flagged = new();

    public static bool IsFlagged(IntPtr vehicleObject) => vehicleObject != IntPtr.Zero && Flagged.ContainsKey(vehicleObject);

    
    public static bool IsHiddenNow(IntPtr vehicleObject)
    {
        if (!Flagged.TryGetValue(vehicleObject, out var e)) return false;
        return !(Visibility.RevealHidden && !InBattle(e));
    }

    private static void Set(VehicleObject vo, bool on)
    {
        if (vo == null) return;
        if (on)
        {
            if (Flagged.ContainsKey(vo.Pointer)) return;
            var e = new Entry { Vo = vo, Name = SafeName(vo) };
            Flagged[vo.Pointer] = e;
            MarkDynamic(e);
            _next = 0f;
        }
        else if (Flagged.TryGetValue(vo.Pointer, out var e))
        {
            Restore(e);
            Flagged.Remove(vo.Pointer);
        }
    }

    private static string SafeName(VehicleObject vo)
    {
        try { return vo.name; }
        catch { return "part"; }
    }

    

    [HarmonyPatch(typeof(VehicleObjectEditor), nameof(VehicleObjectEditor.OnGUI))]
    private static class PanelPatch
    {
        private static VehicleObjectEditor _editor;

        private static void Postfix(VehicleObjectEditor __instance, IGUILayout layout)
        {
            try
            {
                var vo = __instance.target;
                if (vo == null) return;
                var drawer = layout.TryCast<IGUIElementDrawer>();
                if (drawer == null) return;
                _editor = __instance;
                var target = vo;
                Il2CppSystem.Action<bool> onToggle = (Action<bool>)(v =>
                {
                    try
                    {
                        Set(target, v);
                        
                        var mirror = MirrorOf(target);
                        if (mirror != null) Set(mirror, v);
                        Plugin.Log.LogInfo($"Hidden: {SafeName(target)} {(v ? "ON — not visible anywhere, still works" : "off")}" +
                                           (mirror != null ? " (mirrored part too)" : ""));
                        if (_editor != null && !_editor.WasCollected) _editor.RequestRedraw();
                    }
                    catch (Exception e) { Plugin.Log.LogError("Hidden toggle failed: " + e); }
                });
                drawer.ToggleField("Hidden", IsFlagged(vo.Pointer), onToggle,
                    "Hides this part everywhere: in the designer, test drive and battle. It keeps working as usual (armour, mass, " +
                    "damage, APS, sights, launchers). To select it again, click where it is, or tick Hidden parts in the " +
                    "Visibility tab to see hidden parts in the designer. Saved with the vehicle.");
            }
            catch (Exception e) { Plugin.Log.LogWarning("Hidden toggle failed: " + e.Message); }
        }
    }

    private static VehicleObject MirrorOf(VehicleObject vo)
    {
        try
        {
            var m = vo?.VehicleTransform?.Mirror?.VehicleObject;
            return m != null && m.Pointer != vo.Pointer ? m : null;
        }
        catch { return null; }
    }

    

    [HarmonyPatch(typeof(VehicleObjectSerialization), nameof(VehicleObjectSerialization.ToBlueprint))]
    private static class SavePatch
    {
        private static void Postfix(VehicleObject obj, VehicleObjectBlueprint __result)
        {
            try
            {
                if (obj == null || !IsFlagged(obj.Pointer)) return;
                var state = __result?.State;
                if (state == null || state.FindElement(Key) >= 0) return;
                state.AddValue(Key, new Il2CppSystem.Object(IL2CPP.ManagedStringToIl2Cpp("1")));
            }
            catch (Exception e) { Plugin.Log.LogError("Hidden save failed: " + e); }
        }
    }

    [HarmonyPatch(typeof(VehicleObjectSerialization), nameof(VehicleObjectSerialization.ApplyBaseObjectState))]
    private static class LoadPatch
    {
        private static void Postfix(VehicleObject obj, Il2CppSystem.Runtime.Serialization.SerializationInfo state)
        {
            try
            {
                if (obj == null || state == null || state.FindElement(Key) < 0) return;
                Set(obj, true);
            }
            catch (Exception e) { Plugin.Log.LogError("Hidden load failed: " + e); }
        }
    }

    
    [HarmonyPatch(typeof(VehicleObjectModel), nameof(VehicleObjectModel.Build))]
    private static class BuildPatch
    {
        private static void Postfix(VehicleObjectModel __instance)
        {
            if (Flagged.Count == 0) return;
            try
            {
                var vo = __instance.VehicleObject;
                if (vo == null || !Flagged.TryGetValue(vo.Pointer, out var e)) return;
                __instance.MarkDynamic();
                e.NextScan = 0f;
                _next = 0f;
            }
            catch { }
        }
    }

    private static void MarkDynamic(Entry e)
    {
        try
        {
            foreach (var m in e.Vo.GetComponentsInChildren<VehicleObjectModel>(true))
            {
                if (m == null) continue;
                var owner = m.VehicleObject;
                if (owner != null && owner.Pointer != e.Vo.Pointer) continue;
                m.MarkDynamic();
            }
            e.Dynamic = true;
        }
        catch { }
    }

    private static bool InBattle(Entry e)
    {
        try
        {
            var vehicle = e.Vo.GetComponentInParent<Vehicle>();
            return vehicle != null && vehicle.BehaviourEnabled;
        }
        catch { return false; }
    }

    

    private static float _next;

    public static void Tick()
    {
        if (Flagged.Count == 0) return;
        var now = Time.unscaledTime;
        if (now < _next) return;
        _next = now + 0.25f;
        List<IntPtr> dead = null;
        foreach (var kv in Flagged)
        {
            var e = kv.Value;
            try
            {
                if (e.Vo == null || e.Vo.WasCollected || !e.Vo)
                {
                    (dead ??= new List<IntPtr>()).Add(kv.Key);
                    continue;
                }
                var battle = InBattle(e);
                
                var hide = battle || !Visibility.RevealHidden;
                if (hide)
                {
                    if (!e.Hidden || now >= e.NextScan) Hide(e, e.Vo.GetComponentInParent<Vehicle>(), now, battle);
                }
                else if (e.Hidden) Restore(e);
                if (!battle && !e.Dynamic) MarkDynamic(e);
            }
            catch (Exception ex) { Plugin.Log.LogWarning("Hidden part failed: " + ex.Message); }
        }
        if (dead != null) foreach (var p in dead) Flagged.Remove(p);
    }

    
    
    private static void Hide(Entry e, Vehicle vehicle, float now, bool battle)
    {
        e.NextScan = now + (battle ? 2f : 0.75f);
        var first = !e.Hidden;
        e.Hidden = true;
        var vuid = e.Vo.VUID.Value;
        var candidates = new List<VehicleRendererGroup>();
        if (vehicle != null)
            foreach (var (id, g) in GroupsOf(vehicle, now))
                if (id == vuid && g != null && !g.WasCollected) candidates.Add(g);
        void Add(VehicleRendererGroup g)
        {
            if (g != null && !g.WasCollected && !candidates.Exists(x => x.Pointer == g.Pointer)) candidates.Add(g);
        }
        foreach (var m in e.Vo.GetComponentsInChildren<VehicleObjectModel>(true))
        {
            if (m == null) continue;
            var owner = m.VehicleObject;
            if (owner != null && owner.Pointer != e.Vo.Pointer) continue;
            Add(m.model?.TryCast<VehicleRendererGroup>());
        }
        
        try
        {
            var cannon = e.Vo.GetComponent<Sprocket.Vehicles.Cannons.Cannon>();
            var barrel = cannon?.Barrel?.TryCast<Sprocket.Vehicles.Cannons.CannonBarrelModel>();
            if (barrel != null)
            {
                var segs = barrel.segments;
                if (segs != null) foreach (var s in segs) Add(s?.model?.TryCast<VehicleRendererGroup>());
                var bore = barrel.boreModel;
                if (bore != null && !bore.WasCollected)
                    foreach (var g in bore.GetComponentsInChildren<VehicleRendererGroup>(true)) Add(g);
            }
        }
        catch { }
        int hidden = 0, baked = 0, plain = 0;
        foreach (var g in candidates)
        {
            if (!g.visible) continue;          
            g.SetVisible(false);
            e.Groups.Add(g);
            hidden++;
            if (!battle) continue;
            try
            {
                var lods = g.lods;
                if (lods != null)
                    foreach (var r in lods)
                        if (r != null && ((int)r.HideFlags & (int)VehicleRendererHideFlags.Baked) != 0) { baked++; break; }
            }
            catch { }
        }
        
        foreach (var r in e.Vo.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null || r.forceRenderingOff) continue;
            var owner = r.GetComponentInParent<VehicleObject>();
            if (owner == null || owner.Pointer != e.Vo.Pointer) continue;
            r.forceRenderingOff = true;
            e.Plain.Add(r);
            plain++;
        }
        if (first || hidden > 0 || plain > 0)
        {
            if (first)
                Plugin.Log.LogInfo($"Hidden: {e.Name} — {hidden + plain} model(s) hidden ({candidates.Count} groups found by part ID {vuid})" +
                                   (battle ? " in battle" : " in the designer"));
            if (baked > 0)
                Plugin.Log.LogWarning($"Hidden: {baked} model(s) of {e.Name} were already baked into the vehicle's static mesh and may still show");
        }
    }

    
    private static readonly Dictionary<IntPtr, (float At, List<(int Id, VehicleRendererGroup G)> Groups)> VehicleGroups = new();

    private static List<(int Id, VehicleRendererGroup G)> GroupsOf(Vehicle vehicle, float now)
    {
        if (VehicleGroups.TryGetValue(vehicle.Pointer, out var c) && now - c.At < 0.7f) return c.Groups;
        var list = new List<(int, VehicleRendererGroup)>();
        foreach (var g in vehicle.GetComponentsInChildren<VehicleRendererGroup>(true))
            if (g != null) list.Add((g.id.Value, g));
        if (VehicleGroups.Count > 32) VehicleGroups.Clear();
        VehicleGroups[vehicle.Pointer] = (now, list);
        return list;
    }

    
    private static void Restore(Entry e)
    {
        foreach (var g in e.Groups)
            try { if (g != null && !g.WasCollected && g) g.SetVisible(true); } catch { }
        e.Groups.Clear();
        foreach (var r in e.Plain)
            try { if (r != null && !r.WasCollected && r) r.forceRenderingOff = false; } catch { }
        e.Plain.Clear();
        e.Hidden = false;
    }
}
