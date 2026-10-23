using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Sprocket;
using Sprocket.Selection;
using Sprocket.VehicleDesigner;
using Sprocket.VehicleDesigner.Access.Editors;
using Sprocket.Vehicles;
using Sprocket.Vehicles.Cannons.Editor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ModernDay;





internal static class Section
{
    public const string Id = "Modern Day";

    
    
    
    
    private static readonly (string Name, string Tag)[] Tabs =
    {
        ("Active", Api.TagActive),
        ("Passive", Api.TagPassive),
        ("Weapon", Api.TagWeapon),
        ("Wheels", Api.TagWheels)
    };

    private static string CategoryOf(string tag) => tag switch
    {
        Api.TagAps => Api.TagActive,
        Api.TagEo or Api.TagEra => Api.TagPassive,
        Api.TagOther or Api.TagThermal or Api.TagGuns => Api.TagWeapon,
        _ => null
    };

    
    private static void Categorize()
    {
        try
        {
            var loader = Object.FindObjectOfType<Sprocket.Vehicles.PartImporting.PartAssetDatabaseLoader>();
            var db = loader != null ? loader.partDatabase : null;
            if (db?.partDefinitions == null) return;
            var added = 0;
            foreach (var def in db.partDefinitions)
            {
                var tags = def?.tags;
                if (tags == null) continue;
                string category = null;
                var has = false;
                foreach (var t in tags)
                {
                    category ??= CategoryOf(t);
                    if (t is Api.TagActive or Api.TagPassive or Api.TagWeapon) has = true;
                }
                if (category == null || has) continue;
                var merged = new Il2CppStringArray(tags.Length + 1);
                for (var i = 0; i < tags.Length; i++) merged[i] = tags[i];
                merged[tags.Length] = category;
                def.tags = merged;
                if (!db.partDefinitionTagLookup.ContainsKey(category))
                    db.partDefinitionTagLookup[category] = new Il2CppSystem.Collections.Generic.List<Sprocket.PartImporting.PartDefinition>();
                db.partDefinitionTagLookup[category].Add(def);
                added++;
            }
            if (added > 0) Plugin.Log.LogInfo($"Modern Day: {added} part(s) sorted into Active / Passive / Weapon");
        }
        catch (Exception e) { Plugin.Log.LogWarning("Modern Day categories failed: " + e.Message); }
    }

    
    private static bool HasPartsTagged(string tag)
    {
        try
        {
            var loader = Object.FindObjectOfType<Sprocket.Vehicles.PartImporting.PartAssetDatabaseLoader>();
            var db = loader != null ? loader.partDatabase : null;
            return db != null && db.partDefinitionTagLookup.ContainsKey(tag) && db.partDefinitionTagLookup[tag].Count > 0;
        }
        catch { return false; }
    }

    private static VehicleEditorModuleLoadContext _context;
    private static IntPtr _cannonModule;
    private static bool _registering;
    private static readonly HashSet<IntPtr> Ours = new();
    private static readonly List<CannonsEditorModule> Keep = new();

    [HarmonyPatch(typeof(CannonDesignerDefinition), "CreateNewInstance")]
    private static class CapturePatch
    {
        private static void Postfix(VehicleEditorModuleLoadContext context, VehicleEditorModule __result)
        {
            if (_registering || __result == null) return;
            _context = context;
            _cannonModule = __result.Pointer;
        }
    }

    [HarmonyPatch(typeof(VehicleEditModules), nameof(VehicleEditModules.Register))]
    private static class RegisterPatch
    {
        private static void Postfix(VehicleEditModules __instance, VehicleEditorModule item)
        {
            if (_registering || item == null || _context == null || item.Pointer != _cannonModule) return;
            _registering = true;
            try
            {
                var module = Create(_context);
                if (module != null) __instance.Register(module);
                Plugin.Log.LogInfo("Modern Day section added to the designer");
            }
            catch (Exception e) { Plugin.Log.LogError("Modern Day section failed: " + e); }
            finally { _registering = false; }
        }
    }

    private static CannonsEditorModule Create(VehicleEditorModuleLoadContext context)
    {
        Categorize();
        var module = new CannonsEditorModule(context);
        module.Section = Id;
        module.DefaultDisplayName = Id;
        module.Hide = false;
        var tabs = new List<(string Name, string Tag)>();
        foreach (var t in Tabs)
            if (t.Tag != Api.TagWheels || HasPartsTagged(t.Tag)) tabs.Add(t);
        var groups = new Il2CppReferenceArray<ISelectionGroup>(tabs.Count);
        for (var i = 0; i < tabs.Count; i++)
            groups[i] = module.NewAssetPicker(tabs[i].Name, new[] { tabs[i].Tag }).Selection;
        module.selection = groups;
        
        Keep.RemoveAll(m => m == null || m.WasCollected);
        Keep.Add(module);
        Ours.Add(module.Pointer);
        return module;
    }

    
    
    
    [HarmonyPatch(typeof(CannonsEditorModule), nameof(CannonsEditorModule.CreateEditors))]
    private static class EditorsPatch
    {
        private static bool Prefix(CannonsEditorModule __instance, Il2CppSystem.Collections.Generic.IReadOnlyList<VehicleComponent> selection)
        {
            try
            {
                var ours = Ours.Contains(__instance.Pointer);
                var modern = HasModernPart(selection);
                return ours ? modern : !modern;
            }
            catch { return !Ours.Contains(__instance.Pointer); }
        }
    }

    private static bool HasModernPart(Il2CppSystem.Collections.Generic.IReadOnlyList<VehicleComponent> selection)
    {
        if (selection == null) return false;
        var count = selection.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleComponent>>().Count;
        for (var i = 0; i < count; i++)
        {
            var c = selection[i];
            var vo = c != null ? c.VehicleObject : null;
            if (vo != null && Parts.IsModern(vo.guid)) return true;
        }
        return false;
    }

    
    [HarmonyPatch(typeof(GameIcons), nameof(GameIcons.GetOrDefault))]
    private static class IconPatch
    {
        private static void Postfix(string identifier, ref Sprite __result)
        {
            if (identifier != Id) return;
            var icon = Icon.Get();
            if (icon != null) __result = icon;
        }
    }
}


internal static class Parts
{
    private static readonly Dictionary<string, bool> Cache = new();
    private static Sprocket.PartImporting.PartDefinitionDatabase _db;

    public static bool IsModern(string guid)
    {
        if (string.IsNullOrEmpty(guid)) return false;
        if (Cache.TryGetValue(guid, out var known)) return known;
        var result = false;
        try
        {
            if (_db == null)
            {
                var loader = Object.FindObjectOfType<Sprocket.Vehicles.PartImporting.PartAssetDatabaseLoader>();
                _db = loader != null ? loader.partDatabase : null;
            }
            if (_db == null || !_db.partDefinitionLookup.ContainsKey(guid)) return false; 
            var def = _db.partDefinitionLookup[guid];
            if (def?.tags != null)
                foreach (var t in def.tags)
                    if (Array.IndexOf(Api.AllTags, t) >= 0) { result = true; break; }
        }
        catch { return false; }
        Cache[guid] = result;
        return result;
    }
}
