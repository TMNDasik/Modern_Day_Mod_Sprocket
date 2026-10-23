using System;
using UnityEngine;

namespace SoundOverhaul;

public class Controller : MonoBehaviour
{
    public Controller(IntPtr ptr) : base(ptr) { }

    private bool _wasActive = true;
    private int _errors;

    
    private void Start() => Impacts.Install();

    private void Update()
    {
        try
        {
            Library.Pump();
            var active = Plugin.Active;
            if (_wasActive && !active)
            {
                
                GunFire.StopAll();
                Carousel.StopEverything();
                Api.StopAllLoops();
                Voices.StopAll();
                EngineSound.ReleaseAll();
                TrackSound.ReleaseAll();
                DriveSound.ReleaseAll();
                LayerSet.StopEverything();
                Destruction.ReleaseAll();
                Impacts.Clear();
                VanillaMute.UnmuteAll();
            }
            _wasActive = active;
            GunFire.Tick();
            Loading.Tick();
            Impacts.Tick();
            Destruction.Tick();
            Voices.Tick();
            LayerSet.Tick();
        }
        catch (Exception e)
        {
            if (_errors++ < 10) Plugin.Log.LogError("Sound Overhaul update failed: " + e);
        }
    }
}
