using System;
using System.Collections.Generic;
using HarmonyLib;
using Sprocket.Blueprints;
using Sprocket.UI;
using Sprocket.VehicleDesigner.Access;
using Sprocket.Vehicles;
using Sprocket.Vehicles.AmmunitionStorage;
using Sprocket.Vehicles.Cannons;
using Sprocket.Vehicles.Cannons.Editor;
using Sprocket.Vehicles.CrewSystems;
using UnityEngine;
using VehicleDesigner.Compartments.Design;

namespace Munitions;





internal static class AutoConnect
{
    internal sealed class Weapon
    {
        public Cannon Cannon;
        public string Label;
        public int ShellId;
        public Vector3 Position;
    }

    
    public static List<Weapon> WeaponsOf(Component anyPart)
    {
        var list = new List<Weapon>();
        if (anyPart == null) return list;
        var vehicle = anyPart.GetComponentInParent<Vehicle>();
        var root = vehicle != null ? vehicle.transform : anyPart.transform.root;
        foreach (var c in root.GetComponentsInChildren<Cannon>(true))
        {
            if (c == null || LauncherPart.IsLauncher(c)) continue;
            var bp = c.Blueprint;
            if (bp == null) continue;
            var spec = AutoWeaponPart.SpecOf(c);
            string label;
            if (spec != null)
            {
                var cal = AutoWeaponPart.NominalCaliber(spec, bp);
                label = $"{spec.DisplayName} {cal.ToString(spec.Mg ? "0.00" : "0")} mm";
            }
            else label = $"{(string.IsNullOrWhiteSpace(bp.Name) ? "Cannon" : bp.Name)} {bp.Caliber} mm";
            list.Add(new Weapon { Cannon = c, Label = label, ShellId = bp.ShellSlotBlueprintID, Position = c.transform.position });
        }
        
        var seen = new Dictionary<string, int>();
        foreach (var w in list)
        {
            seen.TryGetValue(w.Label, out var n);
            seen[w.Label] = n + 1;
            if (n > 0) w.Label += $" #{n + 1}";
        }
        return list;
    }

    
    public static string LabelOf(Cannon cannon)
    {
        try
        {
            foreach (var w in WeaponsOf(cannon))
                if (w.Cannon.Pointer == cannon.Pointer) return w.Label;
        }
        catch { }
        return "this weapon";
    }

    

    
    private static float _designerAt = -100f;
    private static readonly Dictionary<int, int> InitsPerFrame = new();
    private static readonly Dictionary<IntPtr, int> RackInitFrame = new();

    [HarmonyPatch(typeof(Sprocket.Gameplay.VehicleDesignerPlayerState), nameof(Sprocket.Gameplay.VehicleDesignerPlayerState.Enter))]
    private static class DesignerEnterPatch
    {
        private static void Postfix()
        {
            _designerAt = Time.realtimeSinceStartup;
            RackInitFrame.Clear();
            InitsPerFrame.Clear();
        }
    }

    [HarmonyPatch(typeof(VehicleObject), nameof(VehicleObject.Initiate))]
    private static class ObjectInitPatch
    {
        private static void Postfix()
        {
            var f = Time.frameCount;
            InitsPerFrame.TryGetValue(f, out var n);
            InitsPerFrame[f] = n + 1;
            if (InitsPerFrame.Count > 64)
            {
                var old = new List<int>();
                foreach (var k in InitsPerFrame.Keys) if (k < f - 600) old.Add(k);
                foreach (var k in old) InitsPerFrame.Remove(k);
            }
        }
    }

    [HarmonyPatch(typeof(AmmoRack), nameof(AmmoRack.Initiate))]
    private static class RackInitPatch
    {
        private static void Postfix(AmmoRack __instance)
        {
            try
            {
                if (Time.realtimeSinceStartup - _designerAt < 3f) return; 
                RackInitFrame[__instance.Pointer] = Time.frameCount;
            }
            catch { }
        }
    }

    private static bool TakeNew(AmmoRack rack)
    {
        var ptr = rack.Pointer;
        if (!RackInitFrame.TryGetValue(ptr, out var frame)) return false;
        RackInitFrame.Remove(ptr);
        InitsPerFrame.TryGetValue(frame, out var n);
        return n <= 6; 
    }

    
    public static void Process(AmmoRackEditor editor, AmmoRack rack)
    {
        try
        {
            if (!TakeNew(rack) || Marked(rack)) return;
            var weapons = WeaponsOf(rack);
            if (weapons.Count == 0) return;
            Weapon best = null;
            var bestDist = float.MaxValue;
            var pos = rack.transform.position;
            foreach (var w in weapons)
            {
                var d = (w.Position - pos).sqrMagnitude;
                if (d < bestDist) { bestDist = d; best = w; }
            }
            if (best == null) return;
            if (best.ShellId != rack.Blueprint.ShellSlotBlueprintID && !Connect(editor, best)) return;
            Mark(editor, rack);
            Plugin.Log.LogInfo($"New ammo rack connected to the nearest weapon: {best.Label}");
        }
        catch (Exception e) { Plugin.Log.LogWarning("Ammo rack auto-connect failed: " + e.Message); }
    }

    
    
    private const string FeedMark = "[feed]";

    private static bool Marked(AmmoRack rack)
    {
        try { return (rack.Blueprint?.Description ?? "").Contains(FeedMark); }
        catch { return true; }
    }

    private static void Mark(AmmoRackEditor editor, AmmoRack rack)
    {
        try
        {
            var bp = rack.Blueprint;
            if (bp == null || Marked(rack)) return;
            var d = bp.Description ?? "";
            Il2CppSystem.IDisposable frame = null;
            try { frame = editor.NewBlueprintEditFrame("Ammo rack weapon", bp, null, editor.GetNewOperationGroupID(), VehicleOpExecutionMode.Synchronous); }
            catch { }
            try { bp.Description = d.Length == 0 ? FeedMark : d + " " + FeedMark; }
            finally { if (frame != null) frame.Dispose(); else bp.MarkModified(); }
        }
        catch (Exception e) { if (Plugin.DebugLogging.Value) Plugin.Log.LogWarning("Ammo rack mark failed: " + e.Message); }
    }

    
    public static bool Connect(AmmoRackEditor editor, Weapon w)
    {
        var list = editor.GetBlueprints();
        if (list == null) return false;
        for (var i = 0; i < list.Length; i++)
        {
            var item = list[i];
            if (item == null) continue;
            var rb = new RegisteredBlueprint(item.Pointer);
            if (rb.ID != w.ShellId) continue;
            editor.properties.SetShellSlot(i);
            editor.RequestRedraw();
            return true;
        }
        Plugin.Log.LogWarning($"Ammo rack: shell of {w.Label} not found in the shell list");
        return false;
    }

    
    public static void DrawFeed(AmmoRackEditor editor, AmmoRack rack, IGUIElementDrawer drawer)
    {
        var weapons = WeaponsOf(rack);
        if (weapons.Count == 0) return;
        var current = rack.Blueprint.ShellSlotBlueprintID;
        var selected = weapons.FindIndex(w => w.ShellId == current);
        var options = new Il2CppSystem.Collections.Generic.List<string>();
        foreach (var w in weapons) options.Add(w.Label);
        if (selected < 0)
        {
            options.Add($"Other ({rack.Blueprint.ShellDiameter} mm shells)");
            selected = weapons.Count;
        }
        var list = weapons;
        UnityEngine.Events.UnityAction<int> onPick = (Action<int>)(index =>
        {
            try
            {
                if (index >= 0 && index < list.Count && Connect(editor, list[index]))
                {
                    Mark(editor, rack);
                    Plugin.Log.LogInfo($"Ammo rack now feeds {list[index].Label}");
                }
            }
            catch (Exception e) { Plugin.Log.LogError("Ammo rack weapon change failed: " + e); }
        });
        drawer.Dropdown("Ammo for", options.Cast<Il2CppSystem.Collections.Generic.IReadOnlyList<string>>(), selected, onPick,
            "Which weapon this rack carries ammunition for. A newly placed rack takes the nearest weapon by itself. " +
            "Autocannons and machine guns take rounds from every rack set to them, without a loader.");
    }

    
    public static (int Racks, int Rounds) RacksFor(Cannon cannon)
    {
        int racks = 0, rounds = 0;
        try
        {
            var id = cannon.Blueprint.ShellSlotBlueprintID;
            var vehicle = cannon.GetComponentInParent<Vehicle>();
            var root = vehicle != null ? vehicle.transform : cannon.transform.root;
            foreach (var r in root.GetComponentsInChildren<AmmoRack>(true))
            {
                var bp = r?.Blueprint;
                if (bp == null || bp.ShellSlotBlueprintID != id) continue;
                racks++;
                rounds += Math.Max(0, r.Capacity);
            }
        }
        catch { }
        return (racks, rounds);
    }

    

    
    
    public static bool AssignOperator(CannonEditor editor, Cannon cannon, string what)
    {
        try
        {
            var vehicle = cannon.GetComponentInParent<Vehicle>();
            var root = vehicle != null ? vehicle.transform : cannon.transform.root;
            var main = new HashSet<int>();
            var any = new HashSet<int>();
            foreach (var c in root.GetComponentsInChildren<Cannon>(true))
            {
                if (c == null || c.Pointer == cannon.Pointer) continue;
                any.Add(c.VUID.Value);
                if (!LauncherPart.IsLauncher(c) && !AutoWeaponPart.IsAuto(c)) main.Add(c.VUID.Value);
            }
            CrewSeat best = null;
            var bestRank = int.MaxValue;
            var bestDist = float.MaxValue;
            var pos = cannon.transform.position;
            foreach (var seat in root.GetComponentsInChildren<CrewSeat>(true))
            {
                var sbp = seat?.SeatBlueprint;
                if (sbp == null) continue;
                var ids = sbp.OperatedBehaviourIDs;
                var rank = 2;
                if (ids != null)
                    for (var i = 0; i < ids.Length; i++)
                    {
                        var v = ids[i].Value;
                        if (main.Contains(v)) { rank = 0; break; }
                        if (any.Contains(v)) rank = Math.Min(rank, 1);
                    }
                var dist = (seat.transform.position - pos).sqrMagnitude;
                if (rank < bestRank || (rank == bestRank && dist < bestDist)) { best = seat; bestRank = rank; bestDist = dist; }
            }
            if (best == null) return false;

            var blueprint = best.SeatBlueprint;
            var old = blueprint.OperatedBehaviourIDs;
            var n = old?.Length ?? 0;
            var vuid = cannon.VUID;
            for (var i = 0; i < n; i++) if (old[i].Value == vuid.Value) return true;
            var ids2 = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<VUID>(n + 1);
            for (var i = 0; i < n; i++) ids2[i] = old[i];
            ids2[n] = vuid;

            Il2CppSystem.IDisposable frame = null;
            try { frame = editor.NewBlueprintEditFrame("Crew", blueprint, null, editor.GetNewOperationGroupID(), VehicleOpExecutionMode.Synchronous); }
            catch (Exception e) { if (Plugin.DebugLogging.Value) Plugin.Log.LogWarning("Edit frame unavailable: " + e.Message); }
            try { blueprint.OperatedBehaviourIDs = ids2; }
            finally
            {
                if (frame != null) frame.Dispose();
                else blueprint.MarkModified();
            }
            Plugin.Log.LogInfo($"{what}: operated by the crew member in seat {best.name} " +
                               (bestRank == 0 ? "(the main gunner)" : bestRank == 1 ? "(who already operates a weapon)" : "(the nearest one)"));
            return true;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"{what}: crew assignment failed: " + e.Message);
            return false;
        }
    }
}



[HarmonyPatch(typeof(UnityEngine.UI.ScrollRect), nameof(UnityEngine.UI.ScrollRect.OnScroll))]
internal static class ScrollGuard
{
    private static bool Prefix(UnityEngine.UI.ScrollRect __instance)
    {
        try { return __instance != null && __instance.m_CachedPtr != IntPtr.Zero; }
        catch { return false; }
    }
}
