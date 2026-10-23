using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CombatEffects;



public class Controller : MonoBehaviour
{
    public Controller(IntPtr ptr) : base(ptr) { }

    private int _scene = -1;
    private int _errors;

    private void Start()
    {
        ImpactHooks.Install();
        if (Plugin.Enabled.Value && !Library.Ready) Plugin.Log.LogWarning("Combat Effects: effect library unavailable, the game's own effects stay");
    }

    private void Update()
    {
        try
        {
            var scene = SceneManager.GetActiveScene().handle;
            if (scene != _scene)
            {
                _scene = scene;
                Effects.Clear();
            }
            if (!Plugin.Enabled.Value && Effects.Count == 0) return;
            Lighting.Update();
            Effects.Tick();
            Hooks.Tick();
        }
        catch (Exception e) { if (_errors++ < 10) Plugin.Log.LogError("Combat Effects update failed: " + e); }
    }

    private void LateUpdate()
    {
        try { ImpactHooks.Resolve(); }
        catch (Exception e) { if (_errors++ < 10) Plugin.Log.LogError("Combat Effects hits failed: " + e); }
    }
}
