using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Sprocket.UI;
using Sprocket.VehicleDesigner.Modules;
using Sprocket.Vehicles;
using UnityEngine;
using UnityEngine.Events;

namespace VehicleMaterials;




[HarmonyPatch(typeof(VehicleObjectEditor), nameof(VehicleObjectEditor.OnGUI))]
internal static class Panel
{
    private static readonly Dictionary<string, bool> Expanded = new();
    private static readonly Dictionary<string, Il2CppSystem.Object> Headers = new();

    private static VehicleObjectEditor _editor;
    private static VehicleObject _target;
    private static int _shownSelection;
    private static float _nextPoll;

    private static void Postfix(VehicleObjectEditor __instance, IGUILayout layout)
    {
        try
        {
            var vo = __instance.target;
            if (vo == null) return;
            var drawer = layout.TryCast<IGUIElementDrawer>();
            if (drawer == null) return;
            _editor = __instance;
            _target = vo;
            Draw(__instance, layout, drawer, vo);
        }
        catch (Exception e) { Plugin.Log.LogError("Material panel failed: " + e); }
    }

    private static void Draw(VehicleObjectEditor editor, IGUILayout layout, IGUIElementDrawer drawer, VehicleObject vo)
    {
        var current = Store.Get(vo);
        var whole = current?.Whole ?? MaterialKind.Default;
        var plate = FaceTags.Plate(vo);
        
        if (plate == null && (current == null || current.IsDefault) && !Eligible(vo)) return;

        Section(layout, "Material", true, () =>
        {
            UnityAction<int> onWhole = (Action<int>)(index =>
            {
                try { Ops.SetWhole(vo, (MaterialKind)Math.Clamp(index, 0, Kinds.Count - 1)); }
                catch (Exception e) { Plugin.Log.LogError("Material change failed: " + e); }
            });
            drawer.Dropdown("Whole part", Options(false, false), (int)whole, onWhole,
                "Material of the whole part. Default = the vehicle's paint. Only the look changes — armour and mass stay the same.");
            drawer.InfoField(Kinds.Tooltip(whole) + (plate != null ? " Faces can get their own material below." : ""), 2);
        });

        if (plate == null) return;
        Section(layout, "Face materials", true, () =>
        {
            var em = FaceTags.Mesh(plate);
            if (em == null)
            {
                drawer.InfoField("The structure's mesh isn't loaded yet.", 1);
                return;
            }
            var tags = FaceTags.Read(em) ?? Array.Empty<int>();
            var selected = FaceTags.Selected(em);
            _shownSelection = FaceTags.SelectionSignature(em);
            var own = 0;
            foreach (var t in tags) if (t != 0) own++;

            if (selected.Count == 0)
            {
                drawer.InfoField("Select faces of this structure (the same way as for moving or thickening them), " +
                                 "then pick their material here.", 3);
            }
            else
            {
                var common = -1;
                var mixed = false;
                foreach (var i in selected)
                {
                    var t = i < tags.Length ? tags[i] : 0;
                    if (common < 0) common = t;
                    else if (common != t) { mixed = true; break; }
                }
                UnityAction<int> onFaces = (Action<int>)(index =>
                {
                    try
                    {
                        if (index < 0 || index > Kinds.Count) return; 
                        var mesh = FaceTags.Mesh(vo);
                        var faces = FaceTags.Selected(mesh);
                        if (faces.Count > 0) Ops.SetFaces(vo, faces, index);
                    }
                    catch (Exception e) { Plugin.Log.LogError("Face material change failed: " + e); }
                });
                drawer.Dropdown(selected.Count == 1 ? "Selected face" : $"Selected faces ({selected.Count})",
                    Options(true, mixed), mixed ? Kinds.Count + 1 : Math.Max(0, common), onFaces,
                    "Material of the selected faces. \"Same as part\" = the material chosen for the whole part.");
                drawer.InfoField(mixed
                    ? "The selected faces have different materials — pick one to give them all the same."
                    : $"Selected: {Kinds.TagName(common)}. The inner surface and edges of a plate follow its face.", 2);
            }

            if (own > 0)
            {
                drawer.InfoField(own == 1 ? "1 face has its own material." : $"{own} faces have their own material.", 1);
                UnityAction onClear = (Action)(() =>
                {
                    try
                    {
                        var mesh = FaceTags.Mesh(vo);
                        var all = FaceTags.Read(mesh);
                        if (all == null) return;
                        var list = new List<int>();
                        for (var i = 0; i < all.Length; i++) if (all[i] != 0) list.Add(i);
                        if (list.Count > 0) Ops.SetFaces(vo, list, 0);
                    }
                    catch (Exception e) { Plugin.Log.LogError("Face material reset failed: " + e); }
                });
                var tip = new UITooltip("Clear face materials", "All faces of this structure go back to the material of the whole part.");
                drawer.Button("Clear face materials", onClear, ref tip);
            }
        });
    }

    
    
    
    

    private const VehiclePartType Allowed = VehiclePartType.PlateStructure | VehiclePartType.Weapon | VehiclePartType.Equipment |
                                            VehiclePartType.Stowage | VehiclePartType.AppliqueArmour |
                                            VehiclePartType.PlateStructureInterior | VehiclePartType.Wheel;

    private static readonly HashSet<string> OwnLook = new()
    {
        "3d9b6e42-1f7a-4c5d-8e20-a64b9c7d1e53", "8b4f2c19-6e3a-47d1-b5c8-0f92e1a7d634",   
        "6a1d3f58-2c9e-4b07-91e4-7f5b0c2d8e16", "d24e8b71-5f3a-4c69-a0d2-1b8e6f4c9a37", "0f7c5e93-8b14-4d2a-b6e1-c39a2d7f5b48", 
        "b3e9a4c7-5d21-4f68-9a0e-7c4d2b1f8e65", "e71c5a2d-9f46-4b83-a1d7-36c0b8e4f259",   
        "e7c4b2a9-5d31-4f86-9b0e-2a6c8d4f1e73"                                             
    };

    private static readonly Dictionary<IntPtr, (string Guid, bool Ok)> EligibleCache = new();

    private static bool Eligible(VehicleObject vo)
    {
        var guid = vo.guid ?? "";
        if (EligibleCache.TryGetValue(vo.Pointer, out var cached) && cached.Guid == guid) return cached.Ok;
        var ok = false;
        try
        {
            if (!OwnLook.Contains(guid))
            {
                foreach (var b in vo.behaviours)
                    if (b != null && b.TryCast<Sprocket.Vehicles.Cannons.Cannon>() != null) { ok = true; break; }
                if (!ok)
                    foreach (var m in vo.GetComponentsInChildren<VehicleObjectModel>(true))
                    {
                        var owner = m != null ? m.GetComponentInParent<VehicleObject>() : null;
                        if (owner == null || owner.Pointer != vo.Pointer) continue; 
                        if ((m.PartType & Allowed) != 0) { ok = true; break; }
                    }
            }
        }
        catch { ok = true; }
        if (EligibleCache.Count > 4096) EligibleCache.Clear();
        EligibleCache[vo.Pointer] = (guid, ok);
        return ok;
    }

    private static Il2CppSystem.Collections.Generic.IReadOnlyList<string> Options(bool faces, bool mixed)
    {
        var options = new Il2CppSystem.Collections.Generic.List<string>();
        if (faces) options.Add("Same as part");
        foreach (var n in Kinds.Names) options.Add(n);
        if (mixed) options.Add("Mixed");
        return options.Cast<Il2CppSystem.Collections.Generic.IReadOnlyList<string>>();
    }

    
    public static void RequestRedraw()
    {
        try { if (_editor != null && !_editor.WasCollected) _editor.RequestRedraw(); }
        catch { }
    }

    
    public static void Poll()
    {
        if (_editor == null || _target == null || Time.unscaledTime < _nextPoll) return;
        _nextPoll = Time.unscaledTime + 0.1f;
        try
        {
            if (_target.WasCollected || !Hooks.IsSelected(_target)) return;
            var em = FaceTags.Mesh(_target);
            if (em == null) return;
            var sig = FaceTags.SelectionSignature(em);
            if (sig == _shownSelection) return;
            _shownSelection = sig;
            RequestRedraw();
        }
        catch { }
    }

    private static void Section(IGUILayout layout, string name, bool defaultOpen, Action body)
    {
        if (!Expanded.TryGetValue(name, out var open)) Expanded[name] = open = defaultOpen;
        if (!Headers.TryGetValue(name, out var header))
            Headers[name] = header = new Il2CppSystem.Object(IL2CPP.ManagedStringToIl2Cpp(name));
        Il2CppSystem.Action<bool> onExpand = (Action<bool>)(v => Expanded[name] = v);
        layout.BeginDropdown(header, open, onExpand);
        try { body(); }
        finally { layout.EndDropdown(); }
    }
}
