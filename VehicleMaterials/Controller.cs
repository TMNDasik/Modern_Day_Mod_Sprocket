using System;
using UnityEngine;

namespace VehicleMaterials;

public class Controller : MonoBehaviour
{
    public Controller(IntPtr ptr) : base(ptr) { }

    private void LateUpdate()
    {
        try { Applier.Tick(); }
        catch (Exception e) { Plugin.Log.LogError("Vehicle materials failed: " + e); }
    }
}
