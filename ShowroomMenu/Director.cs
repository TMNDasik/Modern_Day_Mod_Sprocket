using System;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace ShowroomMenu;



internal static class Director
{
    private static Sprocket.MainMenu _menu;
    private static int _menuId, _closedId;     
    private static bool _active;

    private static Stage _stage;
    private static TitleCard _title;
    private static Overlay _overlay;
    private static Showcase _showcase;
    private static FlickerWatch _watch;

    private static bool _haveCamPose;
    private static Vector3 _camPos;
    private static Quaternion _camRot;
    private static bool _rebuildVehicle;
    private static HDAdditionalCameraData _hdCam;
    private static HDAdditionalCameraData.ClearColorMode _savedClear;
    private static HDAdditionalCameraData.AntialiasingMode _savedAA;
    private static HDAdditionalCameraData.TAAQualityLevel _savedTaaQuality;
    private static bool _aaOverridden;
    private static Transform _camT;
    private static Vector3 _savedCamPos;
    private static Quaternion _savedCamRot;

    public static void RequestVehicleRebuild() => _rebuildVehicle = true;

    public static void Tick(Sprocket.MainMenu menu)
    {
        if (menu == null) return;
        var id = menu.GetInstanceID();
        if (id == _closedId) return;            
        if (id != _menuId)
        {
            Shutdown("another main menu", false);
            Begin(menu);
        }
        if (!_active) return;
        try { Update(); }
        catch (Exception e) { Fail(e); }
    }

    private static void Begin(Sprocket.MainMenu menu)
    {
        _menu = menu;
        _menuId = menu.GetInstanceID();
        _active = true;
        MenuScreen.Main = menu.activeMenu == null;
        _haveCamPose = false;
        _rebuildVehicle = false;
        try
        {
            var scene = menu.gameObject.scene;
            _title = new TitleCard();
            _title.Capture(scene);              
            _stage = new Stage();
            _stage.Begin(menu, scene, _title.BeltRoot);
            _title.Attach(scene);
            _overlay = new Overlay();
            _overlay.Begin(menu, scene);
            _showcase = new Showcase();
            _showcase.Begin(scene);
            _watch = Plugin.WatchLighting.Value ? new FlickerWatch() : null;
            _camT = menu.mainCamera != null ? menu.mainCamera.transform : null;
            if (_camT != null)
            {
                _savedCamPos = _camT.position;
                _savedCamRot = _camT.rotation;
            }
            
            _hdCam = menu.mainCamera != null ? menu.mainCamera.GetComponent<HDAdditionalCameraData>() : null;
            if (_hdCam != null)
            {
                _savedClear = _hdCam.clearColorMode;
                _hdCam.clearColorMode = HDAdditionalCameraData.ClearColorMode.Sky;
                _aaOverridden = false;
                Plugin.Debug($"Menu camera: clear {_savedClear}, custom frame settings {_hdCam.customRenderingSettings}, AA {_hdCam.antialiasing}");
            }
            Plugin.Log.LogInfo("Showroom main menu: started");
        }
        catch (Exception e) { Fail(e); }
    }

    private static void Update()
    {
        var dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
        _stage.Update();
        if (_stage.Failed)
        {
            Fail(new InvalidOperationException("the Showroom scene could not be loaded"));
            return;
        }
        if (_rebuildVehicle)
        {
            _rebuildVehicle = false;
            _showcase.Rebuild();
        }
        _showcase.Update(dt, _stage.Ready);

        var cam = _menu.mainCamera;
        if (cam != null)
        {
            if (_stage.Ready) _watch?.CheckCamera(cam);
            Framing.Compute(cam, _showcase, out var pos, out var rot);
            if (!_haveCamPose)
            {
                _camPos = pos;
                _camRot = rot;
                _haveCamPose = true;
            }
            else
            {
                var k = 1f - Mathf.Exp(-dt / 0.45f);
                _camPos = Vector3.Lerp(_camPos, pos, k);
                _camRot = Quaternion.Slerp(_camRot, rot, k);
                
                if ((_camPos - pos).sqrMagnitude < 1e-8f && Quaternion.Angle(_camRot, rot) < 0.005f)
                {
                    _camPos = pos;
                    _camRot = rot;
                }
            }
            cam.transform.SetPositionAndRotation(_camPos, _camRot);
            _watch?.CameraSet(cam);
        }
        ApplyAntialiasing();

        
        var onMainScreen = MenuScreen.Main;
        if (cam != null) _title.Draw(cam, onMainScreen);
        _overlay.Update(_menu, _title, onMainScreen, _stage.Ready || _stage.Elapsed > 8f, dt);
        if (_stage.Ready) _watch?.Update(cam, _hdCam, _showcase);
    }

    
    
    
    
    private static void ApplyAntialiasing()
    {
        if (_hdCam == null || _hdCam.WasCollected || !_hdCam) return;
        if (Plugin.TemporalAA.Value)
        {
            if (!_aaOverridden)
            {
                _savedAA = _hdCam.antialiasing;
                _savedTaaQuality = _hdCam.TAAQuality;
                _aaOverridden = true;
            }
            if (_hdCam.antialiasing != HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing)
                _hdCam.antialiasing = HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing;
            if (_hdCam.TAAQuality != HDAdditionalCameraData.TAAQualityLevel.High)
                _hdCam.TAAQuality = HDAdditionalCameraData.TAAQualityLevel.High;
        }
        else RestoreAntialiasing();
    }

    private static void RestoreAntialiasing()
    {
        if (!_aaOverridden) return;
        _aaOverridden = false;
        if (_hdCam == null || _hdCam.WasCollected || !_hdCam) return;
        _hdCam.antialiasing = _savedAA;
        _hdCam.TAAQuality = _savedTaaQuality;
    }

    public static void Shutdown(string reason) => Shutdown(reason, false);

    private static void Shutdown(string reason, bool restoreMenu)
    {
        if (!_active && _stage == null) return;
        Plugin.Debug($"Showroom main menu: shutdown ({reason})");
        _active = false;
        if (_menuId != 0) _closedId = _menuId;
        Safe(() => _watch?.Dispose(), "lighting watch");
        _watch = null;
        Safe(() => _showcase?.Dispose(), "vehicle");
        Safe(() => _title?.Dispose(restoreMenu), "title");
        Safe(() => _overlay?.Dispose(restoreMenu), "overlay");
        Safe(() => _stage?.Dispose(restoreMenu), "showroom");
        Safe(() =>
        {
            RestoreAntialiasing();
            if (!restoreMenu) return;
            if (_hdCam != null && !_hdCam.WasCollected && _hdCam) _hdCam.clearColorMode = _savedClear;
            if (_camT != null && !_camT.WasCollected && _camT) _camT.SetPositionAndRotation(_savedCamPos, _savedCamRot);
        }, "camera");
        _hdCam = null;
        _camT = null;
        _showcase = null;
        _title = null;
        _overlay = null;
        _stage = null;
        _menu = null;
        _menuId = 0;
    }

    
    private static void Fail(Exception e)
    {
        Plugin.Log.LogError("Showroom main menu failed, restoring the normal menu: " + e);
        Shutdown("failure", true);
    }

    
    public static void Background()
    {
        Stage.ProcessPending();
        Showcase.ProcessPending();
        if (!_active) return;
        if (_menu == null || _menu.WasCollected || !_menu) Shutdown("main menu destroyed");
    }

    private static void Safe(Action a, string what)
    {
        try { a(); }
        catch (Exception e) { Plugin.Log.LogWarning($"Cleanup of {what} failed: {e.Message}"); }
    }
}
