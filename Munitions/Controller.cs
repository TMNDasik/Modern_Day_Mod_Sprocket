using System;
using System.Collections.Generic;
using System.Text;
using Sprocket.Vehicles.Weapons;
using Sprocket.WeaponFramework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Munitions;


public class Controller : MonoBehaviour
{
    public Controller(IntPtr ptr) : base(ptr) { }

    
    internal static Key[] Keys = { Key.Digit1, Key.Digit2, Key.Digit4, Key.Digit3, Key.Digit5, Key.Digit7, Key.Digit8 };
    internal static int Selected = Kind.Apfsds;
    internal static PlayerView LastView;    

    private readonly float[] _lastPress = { -10f, -10f, -10f, -10f, -10f, -10f, -10f };
    private int _vehicleKey;
    private float _nextCleanup;
    private string _message;
    private float _messageUntil;

    private readonly AmmoBar _bar = new();
    private int _barErrors;
    private readonly TankPanel _panel = new();
    private int _panelErrors;

    private void Update()
    {
        try
        {
            DynamicSightCompat.Tick();
            LauncherPart.EnsureInjected();
            AutoWeaponPart.EnsureInjected();
            EjectorPart.EnsureInjected();
            Launchers.Tick();
            AutoFeed.Tick();
            Autoloader.Tick(Time.deltaTime);
            PortVisuals.Tick();
            BoreLook.Tick();
            Casings.Tick(Time.deltaTime);

            var inControl = PlayerTracker.InControl;
            PlayerView view = default;
            if (inControl) view = PlayerView.Read();
            LastView = view;

            if (inControl && PlayerTracker.VehicleKey != _vehicleKey)
            {
                _vehicleKey = PlayerTracker.VehicleKey;
                Selected = Kind.Apfsds;
                if (view.Valid && view.Counts[Kind.Apfsds] == 0)
                    foreach (var k in Kind.AutoOrder)
                        if (view.Counts[k] > 0) { Selected = k; break; }
            }

            if (inControl && view.Valid && !view.Launcher && !view.AutoVanilla && Time.timeScale > 0f) HandleKeys(view);
            var tubes = inControl ? Launchers.Groups(PlayerTracker.VehicleKey) : null;
            try { _bar.Update(inControl, view, tubes, _message != null && Time.unscaledTime < _messageUntil ? _message : null); }
            catch (Exception e)
            {
                _bar.Hide();
                if (_barErrors++ < 5) Plugin.Log.LogError("Ammo panel failed: " + e);
            }
            try { _panel.Update(inControl, view, tubes); }
            catch (Exception e)
            {
                try { _panel.Hide(); } catch { }
                if (_panelErrors++ < 5) Plugin.Log.LogError("Vehicle panel failed: " + e);
            }

            if (Time.unscaledTime >= _nextCleanup)
            {
                _nextCleanup = Time.unscaledTime + 5f;
                Racks.Cleanup();
            }
        }
        catch (Exception e) { Plugin.Log.LogError("Munitions controller failed: " + e); }
    }

    private void LateUpdate()
    {
        try { Guidance.UpdateVisuals(); }
        catch (Exception e) { Plugin.Log.LogError("Missile visuals failed: " + e); }
    }

    
    private static bool _renderHooked;
    private static int _renderErrors;

    private void Start()
    {
        if (_renderHooked) return;
        _renderHooked = true;
        try
        {
            UnityEngine.Events.UnityAction cb = (Action)BeforeRender;
            Application.add_onBeforeRender(cb);
        }
        catch (Exception e) { Plugin.Log.LogWarning("Effects render hook failed: " + e.Message); }
    }

    private static void BeforeRender()
    {
        try
        {
            Effects.DrawTracers();
            Particles.Tick(ViewCamera());
            Wires.Tick(ViewCamera());
        }
        catch (Exception e)
        {
            if (_renderErrors++ < 5) Plugin.Log.LogError("Effects render failed: " + e);
        }
    }

    internal static Camera ViewCamera()
    {
        
        
        if (AppDomain.CurrentDomain.GetData("SprocketMods.ActiveObserverCamera") is Camera observer && observer != null && !observer.WasCollected)
            return observer;
        var cam = PlayerTracker.InControl ? PlayerTracker.Camera : null;
        if (cam != null && !cam.WasCollected && cam.isActiveAndEnabled) return cam;
        return Camera.main;
    }

    private void HandleKeys(PlayerView view)
    {
        var kb = Keyboard.current;
        if (kb == null) return;
        for (var k = 0; k < Kind.Count; k++)
        {
            bool pressed;
            try { pressed = kb[Keys[k]].wasPressedThisFrame; }
            catch { pressed = false; }
            if (!pressed) continue;

            if (k == Kind.Atgm && !view.AtgmPossible)
            {
                Flash($"NO ATGM FOR {view.Caliber} MM GUN");
                continue;
            }
            var now = Time.unscaledTime;
            var doubleTap = Selected == k && now - _lastPress[k] < 0.45f;
            _lastPress[k] = now;
            Selected = k;
            
            if (view.Carousel != null)
            {
                if (view.Counts[k] == 0) Flash($"NO {Kind.Names[k]} {Autoloader.MagazineWhere(view.Carousel)}");
                else if (view.LoadedKind >= 0 && view.LoadedKind != k) Flash($"NEXT: {Kind.Names[k]}  [{Autoloader.KeyName}] after the shot");
                else Flash($"{Kind.Names[k]} SELECTED");
                Plugin.Log.LogInfo($"Selected {Kind.Names[k]}");
                continue;
            }
            if (doubleTap && view.Task != null && ReloadNow(view)) Flash($"RELOADING {Kind.Names[k]}");
            else if (view.Counts[k] == 0) Flash($"NO {Kind.Names[k]} LEFT");
            else if (view.LoadedKind >= 0 && view.LoadedKind != k) Flash($"NEXT: {Kind.Names[k]}  (press again to reload now)");
            else Flash($"{Kind.Names[k]} SELECTED");
            Plugin.Log.LogInfo($"Selected {Kind.Names[k]}");
        }

        
        if (view.Carousel != null)
        {
            bool load;
            try { load = kb[Plugin.AutoloaderKey].wasPressedThisFrame; }
            catch { load = false; }
            if (load)
            {
                var result = Autoloader.Request(view.Carousel, Selected);
                if (result != null) Flash(result);
            }
        }
    }

    
    private static bool ReloadNow(PlayerView view)
    {
        if (view.Counts[Selected] == 0) return false;
        var state = (int)view.Task.state;
        var current = state == (int)LoadState.Loaded ? view.LoadedKind
            : state == (int)LoadState.Loading && Racks.HeldKind(view.Task.Pointer, out var held) ? held : -1;
        if (current == Selected) return false;
        if (state != (int)LoadState.Loaded && state != (int)LoadState.Loading) return false;
        view.Task.RequestReload(LoadRequest.Unused);
        return true;
    }

    private void Flash(string text)
    {
        _message = text;
        _messageUntil = Time.unscaledTime + 2.5f;
    }
}


internal struct PlayerView
{
    public bool Valid;
    public int Caliber;
    public bool AtgmPossible;
    public int[] Counts;
    public int LoadedKind, LoadingKind;
    public LoadTask Task;
    public bool Launcher;
    public int LauncherGroup;
    public IntPtr Weapon;
    public Carousel Carousel;   
    public AutoFeed.Gun Auto;   
    public bool AutoVanilla;    
    public int VanillaRounds;
    public SizeTypes Types;     

    public static PlayerView Read()
    {
        var v = new PlayerView { Counts = new int[Kind.Count], LoadedKind = -1, LoadingKind = -1 };
        var cannon = PlayerTracker.Cannon;
        if (cannon == null || cannon.WasCollected) return v;
        var weapon = cannon.Behaviour;
        if (weapon == null) return v;

        v.Weapon = weapon.Pointer;
        
        if (Launchers.GroupOf(weapon.Pointer, out var launcherGroup))
        {
            v.Launcher = true;
            v.LauncherGroup = launcherGroup;
            v.Valid = true;
            return v;
        }
        var itask = weapon.loadTask;
        var task = itask?.TryCast<LoadTask>();
        if (task == null) return v;
        v.Task = task;

        var sizeValue = task.Request.SizeID.Value;
        List<RackState> racks;
        
        if (Autoloader.Enabled && Autoloader.ForWeapon(weapon.Pointer, out var carousel))
        {
            v.Carousel = carousel;
            racks = new List<RackState> { carousel.Rack };
        }
        else
        {
            racks = Racks.Of(PlayerTracker.VehicleKey, sizeValue);
            racks.RemoveAll(Autoloader.IsCarouselRack);
        }
        AutoFeed.TryGet(weapon.Pointer, out v.Auto);
        if (racks.Count == 0)
        {
            if (v.Auto == null) return v;
            v.AutoVanilla = true;
            v.VanillaRounds = AutoFeed.RoundsLeft(v.Auto, sizeValue);
            v.Caliber = (sizeValue >> 16) & 0xFFFF;
            v.Valid = true;
            return v;
        }
        foreach (var r in racks)
        {
            Racks.Reconcile(r);
            for (var k = 0; k < Kind.Count; k++) v.Counts[k] += r.Count[k];
        }
        var types = racks[0].Types;
        v.Types = types;
        v.Caliber = types.Caliber;
        v.AtgmPossible = types.AtgmAllowed && types.TypeIds[Kind.Atgm] != 0;

        var state = (int)task.state;
        if (state == (int)LoadState.Loaded &&
            TypeRegistry.TryGetKind(weapon._LoadedProjectileTypeID_k__BackingField.Value, out _, out var loaded))
            v.LoadedKind = loaded;
        if (state == (int)LoadState.Loading && Racks.HeldKind(task.Pointer, out var held)) v.LoadingKind = held;
        if (v.Carousel != null && v.Carousel.Busy) v.LoadingKind = v.Carousel.LoadingKind;
        v.Valid = true;
        return v;
    }
}
