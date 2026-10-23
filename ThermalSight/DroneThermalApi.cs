using System.Linq;
using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using Object=UnityEngine.Object;

namespace ThermalSight;


public static class DroneThermalApi
{
    public static bool Available=>HeatModel.Instance!=null && GpuResources.TryLoad();
    public static bool Active=>view!=null && view.Active;
    private static GpuThermalView view;
    private static GameObject root;
    private static Camera camera,source;
    private static HDAdditionalCameraData sourceHd;
    private static LayerMask oldMask;
    private static int oldCulling;
    private static float nextHeat,nextRead,lastHeat;
    private static int lastFrame=-1;
    private static Material paletteMaterial;
    private static RenderTexture color;
    public static bool Enable(Camera reference)
    {
        if(Active)return true;
        if(reference==null || HeatModel.Instance==null || !GpuResources.TryLoad())return false;
        source=reference;oldCulling=source.cullingMask;source.cullingMask&=~(1<<31);root=new GameObject("Drone thermal sensor");camera=root.AddComponent<Camera>();camera.CopyFrom(reference);
        camera.targetTexture=null;camera.depth=reference.depth+1;camera.cullingMask&=~(1<<31);
        sourceHd=reference.GetComponent<HDAdditionalCameraData>();
        var hd=root.AddComponent<HDAdditionalCameraData>();
        if(sourceHd!=null){sourceHd.CopyTo(hd);oldMask=sourceHd.volumeLayerMask;sourceHd.volumeLayerMask=oldMask.value&~(1<<30);}
        hd.volumeLayerMask=1<<30;
        view=new GpuThermalView{ExternalOutput=true};
        view.Enable(camera,GenerationProfile.Get(3),9341);
        if(!view.Active){Disable();return false;}
        nextHeat=nextRead=lastHeat=Time.unscaledTime;lastFrame=-1;
        return true;
    }
    public static Texture Update(Camera reference,bool ironbow)
    {
        if(!Active)return null;
        camera.transform.SetPositionAndRotation(reference.transform.position,reference.transform.rotation);
        camera.fieldOfView=reference.fieldOfView;camera.aspect=reference.aspect;camera.enabled=true;
        if(lastFrame!=Time.frameCount)
        {
            lastFrame=Time.frameCount;
            var now=Time.unscaledTime;
            if(now>=nextHeat){HeatModel.Instance.Update(Math.Min(now-lastHeat,.25f),true);lastHeat=now;nextHeat=now+.1f;}
            HeatModel.Instance.UpdateProxies(true,camera);view.Update();
            
        }
        if(!ironbow)return view.Output;
        if(paletteMaterial==null)
        {
            var type=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("FpvPilot.DroneAssets")).FirstOrDefault(t=>t!=null);
            var shader=type?.GetProperty("IronbowShader")?.GetValue(null) as Shader;
            if(shader==null)return view.Output;
            paletteMaterial=new Material(shader);
        }
        var output=view.Output;
        if(color==null){color=new RenderTexture(output.width,output.height,0,RenderTextureFormat.ARGB32);color.Create();}
        Graphics.Blit(output,color,paletteMaterial);return color;
    }
    public static void Disable()
    {
        
        if(camera!=null)camera.enabled=false;
        view?.Disable();view=null;
        HeatModel.Instance?.UpdateProxies(false,null);
        if(sourceHd!=null)sourceHd.volumeLayerMask=oldMask;
        if(source!=null){source.enabled=true;source.cullingMask=oldCulling;}
        if(root!=null)Object.Destroy(root);root=null;camera=null;source=null;sourceHd=null;
        if(color!=null){color.Release();Object.Destroy(color);}color=null;
        if(paletteMaterial!=null)Object.Destroy(paletteMaterial);paletteMaterial=null;
    }
}
