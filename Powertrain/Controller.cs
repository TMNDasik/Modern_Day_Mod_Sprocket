using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Powertrain;


public class Controller : MonoBehaviour
{
    public Controller(IntPtr ptr) : base(ptr) { }

    private readonly GearHud _hud = new();
    private float _mouseAccum;
    private int _errors;

    private void Update()
    {
        try
        {
            ScaleTool.EnsureDefinitions();
            InputBridge.Update();
            PlayerTracker.Refresh();
            try { Tda.Tick(); }
            catch (Exception e) { if (_errors++ < 5) Plugin.Log.LogError("Engine smoke failed: " + e); }
            try { FireSuppression.Tick(); }
            catch (Exception e) { if (_errors++ < 5) Plugin.Log.LogError("Fire suppression failed: " + e); }
            var sel = Plugin.GearboxEnabled.Value && PlayerTracker.InControl ? PlayerTracker.Current : null;
            var kb = Keyboard.current;
            var alt = false;
            if (sel != null && kb != null)
            {
                if (kb[Plugin.ShiftUpKey].wasPressedThisFrame) sel.Shift(1);
                if (kb[Plugin.ShiftDownKey].wasPressedThisFrame) sel.Shift(-1);

                
                alt = kb.leftAltKey.isPressed;
                if (alt && Mouse.current != null)
                {
                    var step = Math.Max(10f, Plugin.MouseShiftStep.Value);
                    _mouseAccum += Mouse.current.delta.ReadValue().y;
                    while (_mouseAccum >= step) { _mouseAccum -= step; if (!sel.Shift(1)) { _mouseAccum = 0f; break; } }
                    while (_mouseAccum <= -step) { _mouseAccum += step; if (!sel.Shift(-1)) { _mouseAccum = 0f; break; } }
                }
                else _mouseAccum = 0f;
            }
            else _mouseAccum = 0f;
            InputBridge.MouseShifting = alt;
            _hud.Update(sel, sel != null, alt);
        }
        catch (Exception e)
        {
            if (_errors++ < 5) Plugin.Log.LogError("Gearbox controls failed: " + e);
        }
    }
}
