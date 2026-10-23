using UnityEngine;

namespace GunStabilizer;


public static class Api
{
    
    
    public static int StabState() => StabController.PanelState;

    
    
    public static void TakeOverIndicator() => StabController.TakenOverFrame = Time.frameCount;

    
    
    
    public static float[] Rangefinder(GameObject vehicle) => FireControl.RangefinderSpec(vehicle);
}
