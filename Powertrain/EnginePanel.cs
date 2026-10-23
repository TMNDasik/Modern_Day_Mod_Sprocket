using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Properties;
using Sprocket.UI;
using Sprocket.VehicleDesigner.Access;
using Sprocket.VehicleDesigner.Access.Editors;
using Sprocket.Vehicles.Engines;
using UnityEngine.Events;

namespace Powertrain;




[HarmonyPatch]
internal static class EnginePanel
{
    private static bool Prepare() => EngineCompat.Method(EngineCompat.EditorType, "OnGUI") != null;
    private static MethodBase TargetMethod() => EngineCompat.Method(EngineCompat.EditorType, "OnGUI");

    private sealed class Panel
    {
        public VehicleComponentEditor<CombustionEngine> Editor;
        public EngineBlueprint Bp;
        public readonly List<FloatProperty> Turbine = new();
        public readonly List<FloatProperty> Rpm = new();
        public FloatProperty Preview, Output, PerTonne, Mass, Cost;
        public float PreviewRpm = -1f;
        public UnityAction<int> OnType, OnSound;
        public Il2CppSystem.Action<bool> OnOverride;
        public int LastFrame;
    }

    private static readonly Dictionary<IntPtr, Panel> Panels = new();
    private static Il2CppSystem.Collections.Generic.List<string> _typeOptions, _soundOptions;
    private static Il2CppSystem.Object _hTurbine, _hRpm, _hPreview, _hResult;
    private static bool _turbineOpen = true, _rpmOpen = true, _previewOpen = true, _resultOpen = true;
    private static int _errors;

    private static readonly UnityEngine.Color Warning = new(1f, 0.7f, 0.4f);

    private static Il2CppSystem.Object Header(ref Il2CppSystem.Object cache, string text) =>
        cache ??= new Il2CppSystem.Object(IL2CPP.ManagedStringToIl2Cpp(text));

    private static int Lines(string text)
    {
        var lines = 0;
        foreach (var part in text.Split('\n')) lines += Math.Max(1, (int)Math.Ceiling(part.Length / 46.0));
        return lines;
    }

    private static bool Prefix(VehicleComponentEditor<CombustionEngine> __instance, IGUILayout layout)
    {
        try
        {
            var bp = EngineCompat.Blueprint(__instance);
            var drawer = layout.TryCast<IGUIElementDrawer>();
            if (bp == null || drawer == null) return true;
            var p = GetPanel(__instance, bp);
            p.LastFrame = UnityEngine.Time.frameCount;

            if (_typeOptions == null)
            {
                _typeOptions = new Il2CppSystem.Collections.Generic.List<string>();
                _typeOptions.Add("Diesel");
                _typeOptions.Add("Gas turbine");
            }
            var spec = SpecCache.GetAny(bp);
            var turbine = spec != null && spec.Enabled;
            drawer.Dropdown("Engine type", _typeOptions.Cast<Il2CppSystem.Collections.Generic.IReadOnlyList<string>>(), turbine ? 1 : 0, p.OnType,
                "Diesel: the game's own piston engine. Gas turbine: a free power turbine with its own settings, turbine lag, sounds and fuel use.");
            if (!turbine)
            {
                
                if (_soundOptions == null)
                {
                    _soundOptions = new Il2CppSystem.Collections.Generic.List<string>();
                    foreach (var o in EngineSoundChoice.Options) _soundOptions.Add(o.Name);
                }
                var current = EngineSoundChoice.IndexOf(EngineSoundChoice.Read(bp.Description));
                drawer.Dropdown("Engine sound", _soundOptions.Cast<Il2CppSystem.Collections.Generic.IReadOnlyList<string>>(), current, p.OnSound,
                    "Which recordings this vehicle's diesel uses (needs the Sound Overhaul mod). Auto: chosen by displacement and cylinders.");
                return true;
            }

            DrawTurbine(p, layout, drawer, spec);
            return false;
        }
        catch (Exception e)
        {
            if (_errors++ < 5) Plugin.Log.LogError("Engine panel failed: " + e);
            return true;
        }
    }

    private static void DrawTurbine(Panel p, IGUILayout layout, IGUIElementDrawer drawer, TurbineSpec s)
    {
        drawer.Readonly(p.Mass);
        drawer.Readonly(p.Cost);

        var peak = TurbineModel.PeakRpm(s);
        Il2CppSystem.Action<bool> onTurbine = (Action<bool>)(v => _turbineOpen = v);
        layout.BeginDropdown(Header(ref _hTurbine, "Turbine"), _turbineOpen, onTurbine);
        try
        {
            foreach (var f in p.Turbine) drawer.Float(f);
            var info = $"Free power turbine: the torque is highest at a standstill ({TurbineModel.StallTorque(s):0} N·m, twice the torque at full power) " +
                       $"and falls as the output shaft speeds up. Full power at {peak:0} rpm of the output shaft " +
                       $"(power turbine {TurbineModel.PowerTurbineRpm:0} rpm ÷ {s.Ratio:0.#}). Inertia is taken at the output shaft.";
            drawer.InfoField(info, Lines(info));
            string warn = null;
            if (peak > s.Safe * 1.05f)
                warn = $"Full power is above the safe limit: at {s.Safe} rpm the turbine gives {TurbineModel.PowerAt(s, s.Safe):0} kW. " +
                       "Raise the gear ratio to bring full power into the working range.";
            else if (peak < s.Operating * 0.75f)
                warn = $"Full power is far below the operating speed: at {s.Upshift} rpm only {TurbineModel.PowerAt(s, s.Upshift):0} kW is left. " +
                       "Lower the gear ratio.";
            if (warn != null) drawer.InfoField(warn, Lines(warn), Warning);
        }
        finally { layout.EndDropdown(); }

        Il2CppSystem.Action<bool> onRpm = (Action<bool>)(v => _rpmOpen = v);
        layout.BeginDropdown(Header(ref _hRpm, "RPM"), _rpmOpen, onRpm);
        try
        {
            foreach (var f in p.Rpm) drawer.Float(f);
            drawer.ToggleField("Override limiter", s.Override, p.OnOverride,
                "Off: the limiter is set automatically just above the upshift speed. On: the Limit slider is used.");
        }
        finally { layout.EndDropdown(); }

        Il2CppSystem.Action<bool> onPreview = (Action<bool>)(v => _previewOpen = v);
        layout.BeginDropdown(Header(ref _hPreview, "Power (Preview)"), _previewOpen, onPreview);
        try
        {
            p.Preview.SetLimits(0f, s.Safe);
            drawer.Float(p.Preview);
            drawer.Readonly(p.Output);
            drawer.Readonly(p.PerTonne);
        }
        finally { layout.EndDropdown(); }

        Il2CppSystem.Action<bool> onResult = (Action<bool>)(v => _resultOpen = v);
        layout.BeginDropdown(Header(ref _hResult, "Result"), _resultOpen, onResult);
        try
        {
            var fullL = TurbineModel.FuelFullPerSecond(s) * 3600f;
            var text =
                $"Top power {TurbineModel.MaxPowerUpTo(s, s.Safe):0} kW, torque {TurbineModel.StallTorque(s):0} N·m at a standstill, " +
                $"{TurbineModel.RatedTorque(s):0} N·m at full power.\n" +
                $"Power turbine at the limiter: {TurbineModel.RevLimit(s) * s.Ratio:0} rpm.\n" +
                $"Engine block: {TurbineModel.Volume(s):0.00} m³.\n" +
                $"Fuel: {fullL:0} L/h at full power, {fullL * TurbineModel.IdleFuelFraction:0} L/h at idle.\n" +
                $"Spool-up from idle to full power: about {Plugin.SpoolUpTime.Value:0.0} s. Does not stall: the gas generator keeps " +
                "running when the output shaft is held.\n" +
                "Ignition key (I) starts and stops the engine; the vehicle can move once the start-up sound is over.";
            drawer.InfoField(text, Lines(text));
        }
        finally { layout.EndDropdown(); }

        EngineCompat.DrawPowertrainInfo(p.Editor, layout);
    }

    private static Panel GetPanel(VehicleComponentEditor<CombustionEngine> editor, EngineBlueprint bp)
    {
        var key = editor.Pointer;
        if (Panels.TryGetValue(key, out var panel) && panel.Bp != null && panel.Bp.Pointer == bp.Pointer) return panel;
        if (Panels.Count > 16)
        {
            var stale = new List<IntPtr>();
            foreach (var kv in Panels) if (UnityEngine.Time.frameCount - kv.Value.LastFrame > 600) stale.Add(kv.Key);
            foreach (var k in stale) Panels.Remove(k);
        }
        var p = new Panel { Editor = editor, Bp = bp };

        p.OnType = (Action<int>)(i =>
        {
            try { SetType(p, i == 1); }
            catch (Exception e) { Plugin.Log.LogError("Engine type change failed: " + e); }
        });
        p.OnSound = (Action<int>)(i =>
        {
            try
            {
                if (i < 0 || i >= EngineSoundChoice.Options.Length) return;
                var key = EngineSoundChoice.Options[i].Key;
                if (EngineSoundChoice.Read(p.Bp.Description) == key) return;
                WriteDescription(p, "Engine sound", EngineSoundChoice.With(p.Bp.Description, key));
            }
            catch (Exception e) { Plugin.Log.LogError("Engine sound change failed: " + e); }
        });
        p.OnOverride = (Action<bool>)(v =>
        {
            try
            {
                Change(p, s =>
                {
                    
                    if (v && !s.Override) s.Limit = TurbineModel.RevLimit(s);
                    s.Override = v;
                    s.Normalize(v ? 3 : 2);
                });
            }
            catch (Exception e) { Plugin.Log.LogError("Limiter change failed: " + e); }
        });

        FloatProperty Prop(string name, string tip, float min, float max, float step, string format,
            Func<TurbineSpec, float> get, Action<TurbineSpec, float> set, int chain)
        {
            Il2CppSystem.Func<float> g = (Func<float>)(() =>
            {
                try
                {
                    var s = SpecCache.GetAny(p.Bp);
                    return s != null ? get(s) : min;
                }
                catch { return min; }
            });
            Il2CppSystem.Action<float> a = (Action<float>)(v =>
            {
                try
                {
                    var value = Math.Clamp((float)Math.Round(v / step) * step, min, max);
                    var cur = SpecCache.GetAny(p.Bp);
                    if (cur == null || Math.Abs(get(cur) - value) < step * 0.25f) return;
                    Change(p, s =>
                    {
                        set(s, value);
                        if (chain >= 0) s.Normalize(chain);
                    });
                }
                catch (Exception e) { Plugin.Log.LogError("Gas turbine setting failed: " + e); }
            });
            return new FloatProperty(name, g, a, min, max, step, format, null, tip);
        }

        p.Turbine.Add(Prop("Power (kW)", "Power of the turbine at full throttle, at the output shaft speed of full power.",
            TurbineSpec.MinPower, TurbineSpec.MaxPower, 10f, "0", s => s.Power, (s, v) => s.Power = v, -1));
        p.Turbine.Add(Prop("Inertia (kg·m²)", "Moment of inertia of the rotating parts reduced to the output shaft. Less = the engine speed changes faster.",
            TurbineSpec.MinInertia, TurbineSpec.MaxInertia, 0.5f, "0.0", s => s.Inertia, (s, v) => s.Inertia = v, -1));
        p.Turbine.Add(Prop("Gear ratio (:1)", "Reduction gear between the power turbine (22 500 rpm at full power) and the output shaft. " +
                                              "Higher = full power at a lower engine speed, with more torque.",
            TurbineSpec.MinRatio, TurbineSpec.MaxRatio, 0.1f, "0.0", s => s.Ratio, (s, v) => s.Ratio = v, -1));

        p.Rpm.Add(Prop("Safe limit", "Above this speed of the output shaft the engine takes damage, eventually breaking.",
            TurbineSpec.MinSafe, TurbineSpec.MaxSafe, 50f, "0", s => s.Safe, (s, v) => s.Safe = (int)v, 4));
        p.Rpm.Add(Prop("Idle", "Speed of the output shaft with no throttle (the gas generator idles).",
            TurbineSpec.MinIdle, TurbineSpec.MaxIdle, 50f, "0", s => s.Idle, (s, v) => s.Idle = (int)v, 0));
        p.Rpm.Add(Prop("Limit", "Fuel is cut gradually as the speed gets close to this value. Moving it turns on Override limiter.",
            TurbineSpec.MinLimit, TurbineSpec.MaxLimit, 50f, "0", s => TurbineModel.RevLimit(s), (s, v) => { s.Limit = (int)v; s.Override = true; }, 3));
        p.Rpm.Add(Prop("Upshift", "If the speed goes above this, the driver goes to a higher gear.",
            TurbineSpec.MinUpshift, TurbineSpec.MaxUpshift, 50f, "0", s => s.Upshift, (s, v) => s.Upshift = (int)v, 2));
        p.Rpm.Add(Prop("Operating speed", "The speed the driver tries to keep the engine above (goes to a lower gear below it).",
            TurbineSpec.MinOperating, TurbineSpec.MaxOperating, 50f, "0", s => s.Operating, (s, v) => s.Operating = (int)v, 1));

        Il2CppSystem.Func<float> previewGet = (Func<float>)(() =>
        {
            var s = SpecCache.GetAny(p.Bp);
            if (s == null) return 0f;
            if (p.PreviewRpm < 0f) p.PreviewRpm = Math.Min(TurbineModel.PeakRpm(s), s.Safe);
            return Math.Min(p.PreviewRpm, s.Safe);
        });
        Il2CppSystem.Action<float> previewSet = (Action<float>)(v => p.PreviewRpm = (float)Math.Round(v / 50f) * 50f);
        p.Preview = new FloatProperty("Power @ (RPM)", previewGet, previewSet, 0f, TurbineSpec.MaxSafe, 50f, "0", null,
            "Preview the power output at this speed of the output shaft.");

        float PreviewPower()
        {
            var s = SpecCache.GetAny(p.Bp);
            return s == null ? 0f : TurbineModel.PowerAt(s, Math.Min(Math.Max(0f, p.PreviewRpm), s.Safe));
        }
        Il2CppSystem.Action<float> none = (Action<float>)(_ => { });
        p.Output = new FloatProperty("Output (kW)", (Il2CppSystem.Func<float>)(Func<float>)PreviewPower, none, 0f, 100000f, 1f, "0", null,
            "Power at the selected speed with full throttle.");
        p.PerTonne = new FloatProperty("Per tonne (kW/t)", (Il2CppSystem.Func<float>)(Func<float>)(() =>
        {
            var mass = EngineCompat.ContextMass(p.Editor);
            return mass > 0.01f ? PreviewPower() / mass : 0f;
        }), none, 0f, 100000f, 0.1f, "0.0", null,
            "The power applied relative to the mass of the vehicle, higher values result in higher acceleration.");
        p.Mass = new FloatProperty("Mass (kg)", (Il2CppSystem.Func<float>)(Func<float>)(() =>
        {
            try { return p.Bp.Mass; }
            catch { return 0f; }
        }), none, 0f, 1000000f, 1f, "0", null, "Mass of the engine.");
        p.Cost = new FloatProperty("Cost", (Il2CppSystem.Func<float>)(Func<float>)(() =>
        {
            try { return p.Bp.MaterialCost + p.Bp.AssemblyCost; }
            catch { return 0f; }
        }), none, 0f, 100000000f, 1f, "0", null, "Cost of the engine.");

        Panels[key] = p;
        return p;
    }

    

    private static void Change(Panel p, Action<TurbineSpec> edit)
    {
        var cur = SpecCache.GetAny(p.Bp);
        if (cur == null || !cur.Enabled) return;
        var s = cur.Clone();
        edit(s);
        Write(p, s, bp => ApplyTurbineFields(bp, s));
    }

    private static void SetType(Panel p, bool turbine)
    {
        var bp = p.Bp;
        var stored = SpecCache.GetAny(bp);
        if (turbine)
        {
            if (stored != null && stored.Enabled) return;
            var s = stored?.Clone() ?? new TurbineSpec();
            if (stored == null)
            {
                
                try
                {
                    var diesel = bp.AveragePower;
                    if (diesel > 1f) s.Power = Math.Clamp((float)Math.Round(diesel * 1.15f / 10f) * 10f, TurbineSpec.MinPower, TurbineSpec.MaxPower);
                }
                catch { }
            }
            s.Enabled = true;
            s.HasBackup = true;
            s.BackupCylinders = bp.CylinderCount;
            s.BackupVolume = bp.CylinderVolume;
            s.BackupUpshift = bp.Upshift;
            s.BackupDownshift = bp.Downshift;
            s.BackupRevLimit = bp.CustomRevLimit;
            s.Normalize(-1);
            Write(p, s, b => ApplyTurbineFields(b, s));
        }
        else
        {
            if (stored == null || !stored.Enabled) return;
            var s = stored.Clone();
            var backup = stored.Clone();
            s.Enabled = false;
            s.HasBackup = false;
            Write(p, s, b =>
            {
                if (!backup.HasBackup) return;
                b.CylinderCount = backup.BackupCylinders;
                b.CylinderVolume = backup.BackupVolume;
                b.Upshift = backup.BackupUpshift;
                b.Downshift = backup.BackupDownshift;
                b.CustomRevLimit = backup.BackupRevLimit;
            });
        }
    }

    private static void ApplyTurbineFields(EngineBlueprint bp, TurbineSpec s) => BlueprintPatches.ApplyFields(bp, s);

    
    private static void WriteDescription(Panel p, string label, string text)
    {
        var bp = p.Bp;
        Il2CppSystem.IDisposable frame = null;
        try { frame = p.Editor.NewBlueprintEditFrame(label, bp, null, p.Editor.GetNewOperationGroupID(), VehicleOpExecutionMode.Synchronous); }
        catch (Exception e) { if (Plugin.DebugLogging.Value) Plugin.Log.LogWarning("Edit frame unavailable: " + e.Message); }
        try { bp.Description = text; }
        finally
        {
            if (frame != null) frame.Dispose();
            else bp.MarkModified();
        }
        try { p.Editor.RequestRedraw(); }
        catch { }
    }

    private static void Write(Panel p, TurbineSpec s, Action<EngineBlueprint> fields)
    {
        var bp = p.Bp;
        var text = TurbineSpec.With(bp.Description, s);
        Il2CppSystem.IDisposable frame = null;
        try { frame = p.Editor.NewBlueprintEditFrame("Gas turbine", bp, null, p.Editor.GetNewOperationGroupID(), VehicleOpExecutionMode.Synchronous); }
        catch (Exception e) { if (Plugin.DebugLogging.Value) Plugin.Log.LogWarning("Edit frame unavailable: " + e.Message); }
        try
        {
            bp.Description = text;
            fields(bp);
        }
        finally
        {
            if (frame != null) frame.Dispose();
            else bp.MarkModified();
        }
        try { p.Editor.RequestRedraw(); }
        catch { }
    }
}
