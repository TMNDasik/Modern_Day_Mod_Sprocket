using System;
using UnityEngine;

namespace ActiveProtection;

public class Controller : MonoBehaviour
{
    public Controller(IntPtr ptr) : base(ptr) { }

    private static bool _renderHooked;
    private static int _renderErrors;

    private void Update()
    {
        try
        {
            ApsParts.EnsureInjected();
            ModuleVisual.Tick();
            DesignerState.Tick();
            EraHosts.Tick();
            EraOps.Tick();
            Interceptor.Refresh();
            Blowoff.Tick();
            EoSystem.Tick();
        }
        catch (Exception e) { Plugin.Log.LogError("Active Protection controller failed: " + e); }
    }

    
    private void Start()
    {
        if (_renderHooked) return;
        _renderHooked = true;
        try
        {
            UnityEngine.Events.UnityAction cb = (Action)BeforeRender;
            Application.add_onBeforeRender(cb);
        }
        catch (Exception e) { Plugin.Log.LogWarning("APS render hook failed: " + e.Message); }
    }

    private static void BeforeRender()
    {
        try
        {
            ModuleVisual.FollowScale();
            DesignerView.Render();
            Fx.Update(DesignerView.ViewCamera());
            ModuleVisual.UpdateGlows();
        }
        catch (Exception e)
        {
            if (_renderErrors++ < 5) Plugin.Log.LogError("APS render failed: " + e);
        }
    }
}
