using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using HarmonyLib;
using Sprocket.UI;
using Sprocket.VehicleDesigner.Access;
using Sprocket.Vehicles.Cannons;
using Sprocket.Vehicles.Cannons.Editor;
using UnityEngine;
using UnityEngine.Events;

namespace Munitions;








internal static class Bore
{
    public const int Unset = 0, Smooth = 1, Rifled = 2;
    private static readonly Regex Token = new(@"\[bore:([sr])\]", RegexOptions.Compiled);

    public static int Get(CannonBlueprint bp)
    {
        try
        {
            var m = Token.Match(bp?.Description ?? "");
            if (!m.Success) return Unset;
            return m.Groups[1].Value == "s" ? Smooth : Rifled;
        }
        catch { return Unset; }
    }

    public static string With(string description, int type)
    {
        var d = description ?? "";
        if (type == Unset) return Token.IsMatch(d) ? Token.Replace(d, "", 1).Trim() : d;
        var token = type == Smooth ? "[bore:s]" : "[bore:r]";
        if (Token.IsMatch(d)) return Token.Replace(d, token, 1);
        return d.Length == 0 ? token : d + " " + token;
    }

    private static void Set(CannonEditor editor, int type)
    {
        var bp = editor?.Component?.Blueprint;
        if (bp == null) return;
        var text = With(bp.Description, type);
        if (text == (bp.Description ?? "")) return;
        Il2CppSystem.IDisposable frame = null;
        try { frame = editor.NewBlueprintEditFrame("Barrel bore", bp, null, editor.GetNewOperationGroupID(), VehicleOpExecutionMode.Synchronous); }
        catch { }
        try { bp.Description = text; }
        finally { if (frame != null) frame.Dispose(); else bp.MarkModified(); }
        Cache.Clear();
    }

    

    private static CannonEditor _editor;
    private static readonly string[] Names = { "Not chosen (as before)", "Smoothbore", "Rifled" };

    
    
    private static CannonEditor _drawing;

    
    internal static CannonEditor Drawing => _drawing;

    [HarmonyPatch(typeof(CannonEditor), nameof(CannonEditor.OnGUI))]
    private static class ContextPatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(CannonEditor __instance) => _drawing = __instance;

        private static void Postfix() => _drawing = null;
    }

    [HarmonyPatch(typeof(CannonInstancePropertyDrawer), nameof(CannonInstancePropertyDrawer.DrawScopeSelect))]
    private static class PanelPatch
    {
        private static void Postfix(IGUILayout layout)
        {
            try
            {
                var editor = _drawing;
                var cannon = editor?.Component;
                
                if (cannon == null || LauncherPart.IsLauncher(cannon) || AutoWeaponPart.IsAuto(cannon)) return;
                var drawer = layout.TryCast<IGUIElementDrawer>();
                if (drawer != null) Draw(editor, drawer);
            }
            catch (Exception e) { if (Plugin.DebugLogging.Value) Plugin.Log.LogWarning("Bore panel failed: " + e.Message); }
        }
    }

    public static void Draw(CannonEditor editor, IGUIElementDrawer drawer)
    {
        var cannon = editor?.Component;
        if (cannon == null) return;
        _editor = editor;
        var bp = cannon.Blueprint;
        var auto = AutoWeaponPart.IsAuto(cannon);
        var options = new Il2CppSystem.Collections.Generic.List<string>();
        foreach (var n in Names) options.Add(n);
        UnityAction<int> onSelect = (Action<int>)(index =>
        {
            try
            {
                Set(_editor, Mathf.Clamp(index, 0, 2));
                _editor?.RequestRedraw();
            }
            catch (Exception e) { Plugin.Log.LogError("Bore change failed: " + e); }
        });
        var type = Get(bp);
        drawer.Dropdown("Barrel bore", options.Cast<Il2CppSystem.Collections.Generic.IReadOnlyList<string>>(), type, onSelect,
            "Smoothbore: fin-stabilized rounds (APFSDS, HEAT, HE-FRAG, gun-launched ATGM) fly as designed; full-calibre spin-stabilized " +
            "shells (the game's AP, APHE, HE) are not stable — wider spread and less penetration. Rifled: full-calibre shells as " +
            "designed; APFSDS loses ~8% penetration and HEAT ~20% (spin). Not chosen: no effect. " +
            "A smoothbore barrel also looks smooth inside (no rifling grooves).");
        drawer.InfoField(Summary(type, auto), 2);
    }

    private static string Summary(int type, bool auto) => type switch
    {
        Smooth => auto
            ? "Smoothbore: APFSDS and HEAT normal; ordinary bullets and full-calibre shells tumble (±1.2° spread, 40% less penetration)."
            : "Smoothbore: APFSDS, HEAT, HE and ATGM normal; the game's AP / APHE / HE shells: ±0.4° spread, 20% less penetration.",
        Rifled => "Rifled: full-calibre shells normal; APFSDS −8% penetration, HEAT shells −20%.",
        _ => "No bore chosen: rounds fly as before."
    };

    

    private static readonly Dictionary<IntPtr, (int Type, float At)> Cache = new();

    public static int OfWeapon(IntPtr weapon, Transform t)
    {
        var now = Time.unscaledTime;
        if (Cache.TryGetValue(weapon, out var c) && now - c.At < 3f && now >= c.At) return c.Type;
        var type = Unset;
        try { type = Get(ApfsdsSettings.BlueprintOf(t)); } catch { }
        if (Cache.Count > 512) Cache.Clear();
        Cache[weapon] = (type, now);
        return type;
    }

    
    public static void Effect(int type, int kind, bool auto, out float penetration, out float spreadDeg)
    {
        penetration = 1f;
        spreadDeg = 0f;
        if (type == Smooth)
        {
            
            if (kind >= 0 && kind is not (Kind.Ap or Kind.Aphe or Kind.Hesh)) return;
            penetration = auto ? 0.6f : 0.8f;
            spreadDeg = auto ? 1.2f : 0.4f;
        }
        else if (type == Rifled)
        {
            if (kind == Kind.Apfsds) penetration = 0.92f;
            else if (kind == Kind.Heat) penetration = 0.8f;
            else if (kind == Kind.Hesh) penetration = 0.9f;
        }
    }

    
    public static Vector3 Deviate(Vector3 vel, float maxDeg)
    {
        var speed = vel.magnitude;
        if (speed < 1f || maxDeg <= 0f) return vel;
        var dir = vel / speed;
        var side = Vector3.Cross(dir, Vector3.up);
        if (side.sqrMagnitude < 1e-4f) side = Vector3.Cross(dir, Vector3.right);
        side.Normalize();
        var angle = maxDeg * Mathf.Sqrt(UnityEngine.Random.value);
        var around = UnityEngine.Random.value * 360f;
        var tilted = Quaternion.AngleAxis(angle, Quaternion.AngleAxis(around, dir) * side) * dir;
        return tilted * speed;
    }
}
