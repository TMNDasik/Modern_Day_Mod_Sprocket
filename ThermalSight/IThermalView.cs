using UnityEngine;

namespace ThermalSight;


internal interface IThermalView
{
    bool Active { get; }
    int ActiveGen { get; }
    Camera Camera { get; }
    void Enable(Camera cam, GenerationProfile profile, int seed);
    void Disable();
    void Update();
    void SetDigitalZoom(float zoom);
    void SetZoomText(string text);
    void SetStatus(in SightStatus status);
    void SaveSnapshot(string tag);

    
    
    bool CameraToScreen(Vector2 cameraViewport, out Vector2 screenViewport);

    
    bool ScreenRect(out Rect rect);
}
