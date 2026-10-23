using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Sprocket.UI;
using Sprocket.Vehicles.Cannons;
using Sprocket.Vehicles.Cannons.Editor;
using UnityEngine;
using UnityEngine.Events;

namespace Munitions;





internal static class AmmoPanel
{
    private static bool _expanded = true;
    private static int _selected = Kind.Apfsds;   
    private static Il2CppSystem.Object _header;

    private static Il2CppSystem.Object Header =>
        _header ??= new Il2CppSystem.Object(IL2CPP.ManagedStringToIl2Cpp("Ammunition"));

    public static string Title(int kind) => kind switch
    {
        Kind.Apfsds => "APFSDS (sabot)",
        Kind.Ap => "AP (solid shot)",
        Kind.Aphe => "APHE (bursting)",
        Kind.Heat => "HEAT (shaped charge)",
        Kind.He => "HE (fragmentation)",
        Kind.Hesh => "HESH (squash head)",
        Kind.Atgm => "ATGM (guided missile)",
        _ => Kind.Names[kind]
    };

    [HarmonyPatch(typeof(CannonEditor), nameof(CannonEditor.OnGUI))]
    private static class GunPanel
    {
        private static void Postfix(CannonEditor __instance, IGUILayout layout)
        {
            try
            {
                var cannon = __instance.Component;
                if (cannon == null || LauncherPart.IsLauncher(cannon)) return;
                var bp = cannon.Blueprint;
                if (bp == null || !Design.Managed(bp.Caliber)) return;
                var drawer = layout.TryCast<IGUIElementDrawer>();
                if (drawer == null) return;
                Il2CppSystem.Action<bool> onExpand = (Action<bool>)(v => _expanded = v);
                layout.BeginDropdown(Header, _expanded, onExpand);
                try { Draw(__instance, drawer, bp, AutoWeaponPart.IsAuto(cannon)); }
                finally { layout.EndDropdown(); }
            }
            catch (Exception e) { Plugin.Log.LogError("Gun ammunition panel failed: " + e); }
        }
    }

    private static void Draw(CannonEditor editor, IGUIElementDrawer drawer, CannonBlueprint bp, bool auto)
    {
        var cal = (int)bp.Caliber;
        var kinds = new List<int>();
        foreach (var k in Kind.Order)
            if (k != Kind.Atgm || (!auto && Design.AtgmAllowed(cal))) kinds.Add(k);
        var index = Math.Max(0, kinds.IndexOf(_selected));
        var kind = kinds[index];

        var options = new Il2CppSystem.Collections.Generic.List<string>();
        foreach (var k in kinds) options.Add(Title(k));
        UnityAction<int> onPick = (Action<int>)(i =>
        {
            try
            {
                if (i < 0 || i >= kinds.Count) return;
                _selected = kinds[i];
                editor.RequestRedraw();
            }
            catch (Exception e) { Plugin.Log.LogError("Shell type change failed: " + e); }
        });
        drawer.Dropdown("Shell type", options.Cast<Il2CppSystem.Collections.Generic.IReadOnlyList<string>>(), index, onPick,
            "Every shell this gun can fire. Pick one to see its numbers and set it up. How many of each the vehicle carries " +
            "is set in the Ammunition section of its ammo racks; in battle the number keys choose the next round.");

        switch (kind)
        {
            case Kind.Apfsds:
                ApfsdsPanel.Draw(editor, drawer);
                break;
            case Kind.Heat:
                HeatPanel.Draw(editor, drawer);
                break;
            case Kind.Atgm:
                AtgmPanel.Draw(editor, drawer, Kind.Atgm);
                break;
            default:
                Info(drawer, Stats(kind, bp));
                Info(drawer, "Fixed by the caliber and the gun (strength: Ammunition in the Munitions mod settings).");
                break;
        }
        if (!auto && !Design.AtgmAllowed(cal))
            Info(drawer, $"ATGM needs a gun of {Plugin.AtgmMinCaliber.Value} mm or more.");
    }

    
    private static string Stats(int kind, CannonBlueprint bp)
    {
        var cal = (int)bp.Caliber;
        var s = ShellSpec.For(cal);
        float gun;
        try { gun = bp.MuzzleVelocity; }
        catch { gun = 800f; }
        switch (kind)
        {
            case Kind.Ap:
            {
                var v = ShellSpec.EstimateSpeed(gun, s.ApLength, cal);
                return $"{s.ApMass:0.0} kg solid steel shot with armour-piercing and ballistic caps. About {v:0} m/s from this gun, " +
                       $"~{ShellSpec.Penetration(cal, s.ApMass, v, s.ApConstant):0} mm at the muzzle, less at long range as it slows down. " +
                       "No explosive: it wrecks what it hits behind the armour and throws spall. Heavy, so it keeps its speed well.";
            }
            case Kind.Aphe:
            {
                var v = ShellSpec.EstimateSpeed(gun, s.ApheLength, cal);
                return $"{s.ApheMass:0.0} kg armour-piercing shell with {s.ApheFiller:0.00} kg of explosive in its base. About {v:0} m/s, " +
                       $"~{ShellSpec.Penetration(cal, s.ApheMass, v, s.ApheConstant):0} mm at the muzzle (a bit less than solid AP). After " +
                       $"penetrating it explodes about {Mathf.Clamp(Plugin.ApheFuseDistance.Value, 0.2f, 4f):0.0} m behind the armour " +
                       $"(blast radius {Mathf.Sqrt(s.ApheBlast / 10f):0.0} m) — deadly for the crew. If it fails to penetrate, it bursts on the armour.";
            }
            case Kind.Hesh:
            {
                var v = ShellSpec.EstimateSpeed(gun, s.HeshLength, cal);
                return $"{s.HeshMass:0.0} kg thin-walled shell with {s.HeshFiller:0.0} kg of plastic explosive. It squashes against the armour " +
                       $"and bursts on it: a much stronger blast than HE (radius {Mathf.Sqrt(s.HeshPower / 10f):0.0} m) that shakes the crew and " +
                       $"wrecks parts behind and around the plate, ~{ShellSpec.Penetration(cal, s.HeshMass, v, s.HeshConstant):0} mm penetration. " +
                       $"Slower than other shells: about {v:0} m/s.";
            }
            default:
            {
                var v = ShellSpec.EstimateSpeed(gun, 2.46f * cal / 1000f, cal);
                return $"{s.HeMass:0.0} kg shell with {s.HeFiller:0.0} kg of explosive, blast radius {Mathf.Sqrt(s.HePower / 10f):0.0} m. " +
                       "Bursts on impact: blast and fragments wreck tracks, optics, external parts and light vehicles, " +
                       $"but only ~{ShellSpec.Penetration(cal, s.HeMass, v, s.HeConstant):0} mm of armour. About {v:0} m/s.";
            }
        }
    }

    private static void Info(IGUIElementDrawer drawer, string text) =>
        drawer.InfoField(text, Math.Max(1, (int)Math.Ceiling(text.Length / 46.0)));
}
