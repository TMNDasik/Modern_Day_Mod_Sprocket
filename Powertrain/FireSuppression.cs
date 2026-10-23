using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using HarmonyLib;
using Properties;
using Sprocket.Blueprints;
using Sprocket.UI;
using Sprocket.VehicleDesigner.Access;
using Sprocket.VehicleDesigner.Access.Editors;
using Sprocket.Vehicles.Engines;
using Sprocket.Vehicles.Fires;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;

namespace Powertrain;

internal enum FssMode { None, Automatic, Manual }








internal static class FireSuppression
{
    public const int DefaultCharges = 2, MaxCharges = 6;
    private static readonly Regex Token = new(@"\s?\[fss:(a|m),(\d+)\]", RegexOptions.Compiled);

    public static FssMode Read(Blueprint bp, out int charges)
    {
        charges = 0;
        try
        {
            var m = Token.Match(bp?.Description ?? "");
            if (!m.Success) return FssMode.None;
            charges = Math.Clamp(int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture), 1, MaxCharges);
            return m.Groups[1].Value == "a" ? FssMode.Automatic : FssMode.Manual;
        }
        catch { return FssMode.None; }
    }

    public static string With(string description, FssMode mode, int charges)
    {
        var d = Token.Replace(description ?? "", "").Trim();
        if (mode == FssMode.None) return d.Length == 0 ? null : d;
        var token = $"[fss:{(mode == FssMode.Automatic ? "a" : "m")},{Math.Clamp(charges, 1, MaxCharges)}]";
        return d.Length == 0 ? token : d + " " + token;
    }

    

    private static VehicleComponentEditor<CombustionEngine> _editor;
    private static UnityAction<int> _onMode;
    private static FloatProperty _charges;
    private static Il2CppSystem.Collections.Generic.List<string> _options;

    private static EngineBlueprint CurrentBp()
    {
        var ed = _editor;
        return ed != null ? EngineCompat.Blueprint(ed) ?? ed.Component?.Blueprint : null;
    }

    public static void Draw(VehicleComponentEditor<CombustionEngine> editor, IGUILayout layout, EngineBlueprint bp)
    {
        var drawer = layout.TryCast<IGUIElementDrawer>();
        if (drawer == null || bp == null) return;
        _editor = editor;
        if (_options == null)
        {
            _options = new Il2CppSystem.Collections.Generic.List<string>();
            _options.Add("None");
            _options.Add("Automatic");
            _options.Add("Manual");
        }
        _onMode ??= (Action<int>)(i =>
        {
            try
            {
                var b = CurrentBp();
                if (b == null) return;
                var mode = (FssMode)Mathf.Clamp(i, 0, 2);
                var old = Read(b, out var n);
                if (mode == old) return;
                Edit("Fire suppression", b, With(b.Description, mode, old == FssMode.None ? DefaultCharges : n));
                Plugin.Log.LogInfo($"Engine fire suppression: {mode}" + (mode == FssMode.None ? "" : $", {(old == FssMode.None ? DefaultCharges : n)} charge(s)"));
            }
            catch (Exception e) { Plugin.Log.LogError("Fire suppression change failed: " + e); }
        });
        var current = Read(bp, out var charges);
        drawer.Dropdown("Extinguisher", _options.Cast<Il2CppSystem.Collections.Generic.IReadOnlyList<string>>(), (int)current, _onMode,
            "Fire suppression system of the engine compartment (fire extinguisher bottles). Automatic: puts out a fire by itself about " +
            $"{Plugin.FssAutoDelay.Value:0.#} s after it starts. Manual: press {KeyName} in battle when the vehicle is burning. " +
            "Every use empties one bottle (Charges); when they are gone, only the crew can fight the fire (the game's X key, slowly).");
        if (current == FssMode.None) return;

        _charges ??= new FloatProperty("Charges",
            (Il2CppSystem.Func<float>)(Func<float>)(() =>
            {
                try { Read(CurrentBp(), out var n); return n; }
                catch { return DefaultCharges; }
            }),
            (Il2CppSystem.Action<float>)(Action<float>)(v =>
            {
                try
                {
                    var b = CurrentBp();
                    var mode = Read(b, out var n);
                    var next = Mathf.Clamp(Mathf.RoundToInt(v), 1, MaxCharges);
                    if (b == null || mode == FssMode.None || next == n) return;
                    Edit("Fire suppression charges", b, With(b.Description, mode, next));
                }
                catch (Exception e) { Plugin.Log.LogError("Fire suppression charges change failed: " + e); }
            }),
            1f, MaxCharges, 1f, "0", null,
            "How many times the fire suppression system can be used in one battle (extinguisher bottles).");
        drawer.Float(_charges);
        var info = current == FssMode.Automatic
            ? $"Automatic fire suppression: {charges} charge(s). Goes off by itself when the engine or a fuel tank catches fire."
            : $"Manual fire suppression: {charges} charge(s). In battle press {KeyName} when the vehicle is on fire.";
        drawer.InfoField(info, Math.Max(1, (int)Math.Ceiling(info.Length / 46.0)));
    }

    private static void Edit(string what, EngineBlueprint b, string next)
    {
        var ed = _editor;
        if (b == null || next == b.Description) return;
        Il2CppSystem.IDisposable frame = null;
        try { frame = ed?.NewBlueprintEditFrame(what, b, null, ed.GetNewOperationGroupID(), VehicleOpExecutionMode.Synchronous); }
        catch { }
        try { b.Description = next; }
        finally { if (frame != null) frame.Dispose(); else b.MarkModified(); }
        try { ed?.RequestRedraw(); } catch { }
    }

    public static string KeyName
    {
        get
        {
            var s = Plugin.FssKey.ToString();
            return s.StartsWith("Digit") ? s.Substring(5) : s.StartsWith("Numpad") ? "Num " + s.Substring(6) : s;
        }
    }

    

    private sealed class State
    {
        public GameObject Go;
        public string Name;
        public FssMode Mode;
        public int Total, Left;
        public FireControlVehicleModule Module;   
        public Transform Engine;                  
        public float AutoAt = -1f;                
        public float DischargeStart = -1f;        
        public float NextScan;
        public bool WasBurning;
    }

    private static readonly Dictionary<IntPtr, State> States = new();
    private static State _player;
    private static bool _keyWasDown;
    private static float _nextCleanup;
    private static string _lastSource;

    private static float DischargeTime => Mathf.Max(0.2f, Plugin.FssDischargeTime.Value);

    
    private static IntPtr KeyOf(GameObject go)
    {
        var p = PlayerTracker.VehicleGo;
        if (p != null && go != null)
        {
            try
            {
                if (go.Pointer == p.Pointer || go.transform.IsChildOf(p.transform) || p.transform.IsChildOf(go.transform)) return p.Pointer;
            }
            catch { }
        }
        return go != null ? go.Pointer : IntPtr.Zero;
    }

    private static bool IsPlayer(State s)
    {
        var p = PlayerTracker.VehicleGo;
        return p != null && s.Go != null && !s.Go.WasCollected && s.Go.Pointer == p.Pointer;
    }

    private static State Get(GameObject go)
    {
        var key = KeyOf(go);
        if (key == IntPtr.Zero) return null;
        if (States.TryGetValue(key, out var s) && s.Go != null && !s.Go.WasCollected) return s;
        if (key == PlayerTracker.VehicleGo?.Pointer) go = PlayerTracker.VehicleGo;
        s = new State { Go = go, Name = SafeName(go) };
        Scan(s);
        States[key] = s;
        return s;
    }

    private static string SafeName(GameObject go)
    {
        try { return go.name; }
        catch { return "vehicle"; }
    }

    
    private static void Scan(State s)
    {
        var mode = FssMode.None;
        var total = 0;
        Transform engine = null;
        try
        {
            foreach (var ce in s.Go.GetComponentsInChildren<CombustionEngine>(true))
            {
                if (ce == null) continue;
                var m = Read(ce.Blueprint, out var n);
                if (m == FssMode.None) continue;
                if (m == FssMode.Automatic || mode == FssMode.None) mode = m;
                total = Math.Max(total, n);
                engine ??= ce.transform;
            }
        }
        catch (Exception e)
        {
            if (Plugin.DebugLogging.Value) Plugin.Log.LogWarning("Fire suppression scan failed: " + e.Message);
        }
        if (total != s.Total || mode != s.Mode)
        {
            
            s.Left = total;
            if (mode != FssMode.None)
                Plugin.Log.LogInfo($"Fire suppression on {s.Name}: {mode}, {total} charge(s)");
        }
        s.Mode = mode;
        s.Total = total;
        s.Engine = engine;
    }

    private static bool Burning(State s)
    {
        try { return s.Module != null && !s.Module.WasCollected && s.Module.OnFire; }
        catch { return false; }
    }

    public static void Tick()
    {
        var now = Time.time;
        var go = PlayerTracker.VehicleGo;
        _player = null;
        if (go != null)
        {
            var s = Get(go);
            if (s != null && Time.unscaledTime >= s.NextScan)
            {
                s.NextScan = Time.unscaledTime + 1f;
                Scan(s);
            }
            _player = s;
        }
        if (_player != null && _player.Mode != FssMode.None) HandleKey(_player, now);
        else _keyWasDown = false;

        if (States.Count == 0) return;
        foreach (var s in new List<State>(States.Values))
        {
            try { Update(s, now); }
            catch (Exception e)
            {
                s.DischargeStart = s.AutoAt = -1f;
                Plugin.Log.LogWarning("Fire suppression update failed: " + e.Message);
            }
        }
        if (Time.unscaledTime >= _nextCleanup)
        {
            _nextCleanup = Time.unscaledTime + 10f;
            var dead = new List<IntPtr>();
            foreach (var kv in States) if (kv.Value.Go == null || kv.Value.Go.WasCollected || !kv.Value.Go) dead.Add(kv.Key);
            foreach (var k in dead) States.Remove(k);
        }
    }

    
    private static void HandleKey(State s, float now)
    {
        var kb = Keyboard.current;
        if (kb == null || Time.timeScale <= 0f) return;
        bool down, pressed;
        try
        {
            var key = kb[Plugin.FssKey];
            down = key.isPressed;
            pressed = key.wasPressedThisFrame || (down && !_keyWasDown);
        }
        catch { down = pressed = false; }
        _keyWasDown = down;
        if (!pressed) return;
        if (s.DischargeStart >= 0f) { Plugin.Log.LogInfo("Fire suppression: already discharging"); return; }
        if (s.Left <= 0) { Plugin.Log.LogInfo("Fire suppression: no charges left"); return; }
        if (!Burning(s)) { Plugin.Log.LogInfo("Fire suppression: the vehicle is not on fire — no charge used"); return; }
        Discharge(s, now, "by hand");
    }

    private static void Update(State s, float now)
    {
        var burning = Burning(s);
        if (burning != s.WasBurning)
        {
            s.WasBurning = burning;
            if (!burning && s.Mode != FssMode.None) Plugin.Log.LogInfo($"Fire on {s.Name} is out ({s.Left}/{s.Total} extinguisher charge(s) left)");
        }

        
        if (burning && s.Left > 0 && s.DischargeStart < 0f && s.AutoAt < 0f &&
            (s.Mode == FssMode.Automatic || (s.Mode == FssMode.Manual && !IsPlayer(s))))
            s.AutoAt = now + Mathf.Max(0f, s.Mode == FssMode.Automatic ? Plugin.FssAutoDelay.Value : Plugin.FssCrewDelay.Value);
        if (s.AutoAt >= 0f && now >= s.AutoAt)
        {
            s.AutoAt = -1f;
            var allowed = s.Mode == FssMode.Automatic || (s.Mode == FssMode.Manual && !IsPlayer(s));
            if (allowed && burning && s.Left > 0 && s.DischargeStart < 0f)
                Discharge(s, now, s.Mode == FssMode.Automatic ? "automatically" : "by the crew");
        }

        if (s.DischargeStart < 0f) return;
        var k = Mathf.Clamp01((now - s.DischargeStart) / DischargeTime);
        var fc = s.Module != null && !s.Module.WasCollected ? s.Module.fireControl : null;
        if (fc != null && fc.onFire)
        {
            
            var target = fc.currentFireDuration * k * 0.95f;
            if (fc.extinguishPerformed < target) fc.extinguishPerformed = target;
        }
        if (k < 1f) return;
        s.DischargeStart = -1f;
        if (fc != null && fc.onFire)
        {
            fc.Extinguish();
            Plugin.Log.LogInfo($"Fire suppression: fire on {s.Name} put out ({s.Left}/{s.Total} charge(s) left)");
        }
        else Plugin.Log.LogInfo($"Fire suppression: the fire on {s.Name} was already out");
    }

    private static void Discharge(State s, float now, string how)
    {
        s.Left--;
        s.DischargeStart = now;
        s.AutoAt = -1f;
        Plugin.Log.LogInfo($"Fire suppression on {s.Name} fired {how} ({s.Left}/{s.Total} charge(s) left)");
        PlaySound(s);
    }

    private static void PlaySound(State s)
    {
        try
        {
            var clip = Sounds.FssDischarge;
            var volume = Mathf.Clamp(Plugin.FssVolume.Value, 0f, 2f);
            if (clip == null || volume <= 0f) return;
            var parent = s.Engine != null && !s.Engine.WasCollected ? s.Engine : s.Go.transform;
            var go = new GameObject("Powertrain fire suppression sound");
            go.transform.SetParent(parent, false);
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.spatialBlend = 1f;
            src.rolloffMode = AudioRolloffMode.Logarithmic;
            src.minDistance = 5f;
            src.maxDistance = 800f;
            src.dopplerLevel = 0f;
            src.priority = 40;
            src.volume = Mathf.Min(1f, volume);
            try
            {
                
                var any = s.Go.GetComponentInChildren<AudioSource>(true);
                if (any != null && any.Pointer != src.Pointer) src.outputAudioMixerGroup = any.outputAudioMixerGroup;
            }
            catch { }
            if(src.isActiveAndEnabled)src.Play();
            Object.Destroy(go, clip.length + 0.5f);
        }
        catch (Exception e) { Plugin.Log.LogWarning("Fire suppression sound failed: " + e.Message); }
    }

    
    [HarmonyPatch(typeof(FireControlVehicleModule), nameof(FireControlVehicleModule.OnFireSourceFireStarted))]
    private static class FireSourcePatch
    {
        private static void Prefix(Il2CppSystem.Object sender)
        {
            try { _lastSource = sender?.GetIl2CppType()?.Name; }
            catch { _lastSource = null; }
        }
    }

    [HarmonyPatch(typeof(FireControlVehicleModule), nameof(FireControlVehicleModule.StartFire))]
    private static class StartFirePatch
    {
        private static void Postfix(FireControlVehicleModule __instance, float fireDuration)
        {
            try
            {
                var go = VehicleOf(__instance);
                if (go == null) { Plugin.Log.LogWarning("Fire started, but its vehicle was not found"); return; }
                var s = Get(go);
                if (s == null) return;
                s.Module = __instance;
                var player = IsPlayer(s);
                if (player || s.Mode != FssMode.None)
                    Plugin.Log.LogInfo($"Fire: {(player ? "your vehicle" : s.Name)} caught fire ({Source()}, fire size {fireDuration:0.#})" +
                                       (s.Mode != FssMode.None ? $", extinguisher {s.Mode} {s.Left}/{s.Total}" : ", no extinguisher system"));
                _lastSource = null;
            }
            catch (Exception e) { Plugin.Log.LogWarning("Fire tracking failed: " + e.Message); }
        }
    }

    private static string Source() => _lastSource switch
    {
        null => "unknown source",
        var n when n.Contains("Engine") => "engine",
        var n when n.Contains("Fuel") => "fuel tank",
        var n => n
    };

    private static GameObject VehicleOf(FireControlVehicleModule module)
    {
        var r = module.receiver;
        if (r == null) return null;
        var stage = r.TryCast<FireControlAssemblyStage>();
        return stage != null ? stage.GameObject : r.GameObject;
    }

    
    [HarmonyPatch(typeof(Sprocket.Gameplay.VehicleDesignerPlayerState), nameof(Sprocket.Gameplay.VehicleDesignerPlayerState.Enter))]
    private static class DesignerEnterPatch
    {
        private static void Postfix()
        {
            States.Clear();
            _player = null;
        }
    }

    
    
    public static Vector4 Status()
    {
        var s = _player;
        if (s == null || s.Mode == FssMode.None || PlayerTracker.VehicleGo == null) return Vector4.zero;
        var state = s.DischargeStart >= 0f ? 1f : Burning(s) ? 2f : 0f;
        return new Vector4(s.Left, state, s.Total, s.Mode == FssMode.Automatic ? 1f : 2f);
    }
}
