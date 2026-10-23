using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes;
using Sprocket.Vehicles.Engines;
using UnityEngine;

namespace Powertrain;





internal static class EngineCompat
{
    public static readonly Type EditorType = FindType("Sprocket.Vehicles.Engines.Editor.CombustionEngineComponentEditor");
    public static readonly Type PropertiesType = FindType("Sprocket.Engines.Editor.EngineProperties");

    private static readonly PropertyInfo EditorBlueprint = EditorType?.GetProperty("blueprint");
    private static readonly PropertyInfo EditorDrawer = EditorType?.GetProperty("propertyDrawer");
    private static readonly PropertyInfo EditorProperties = EditorType?.GetProperty("properties");

    
    private static readonly PropertyInfo EngineSize = typeof(CombustionEngine).GetProperty("size");
    private static readonly PropertyInfo EngineBounds = typeof(CombustionEngine).GetProperty("boundsCollider");
    private static readonly PropertyInfo EngineModel = typeof(CombustionEngine).GetProperty("model");
    private static readonly PropertyInfo ModelParent = typeof(CombustionEngineModel).GetProperty("parent");

    
    private static readonly PropertyInfo EngineGenerator = typeof(CombustionEngine).GetProperty("modelGenerator");

    public static bool LegacyEngineModel => EngineSize != null;

    private static Type FindType(string fullName)
    {
        foreach (var asm in new[] { "Sprocket.EngineDesigner.Editor", "Sprocket.EngineDesigner" })
        {
            try
            {
                var t = Assembly.Load(asm)?.GetType(fullName, false);
                if (t != null) return t;
            }
            catch { }
        }
        try { return AccessTools.TypeByName(fullName); }
        catch { return null; }
    }

    public static MethodBase Method(Type type, string name) =>
        type == null ? null : AccessTools.GetDeclaredMethods(type).FirstOrDefault(m => m.Name == name);

    public static EngineBlueprint Blueprint(object editor)
    {
        try { return EditorBlueprint?.GetValue(editor) as EngineBlueprint; }
        catch { return null; }
    }

    public static float ContextMass(object editor)
    {
        try
        {
            var props = EditorProperties?.GetValue(editor);
            var p = props?.GetType().GetProperty("ContextMass");
            return p != null ? Convert.ToSingle(p.GetValue(props)) : 0f;
        }
        catch { return 0f; }
    }

    
    public static void DrawPowertrainInfo(object editor, Sprocket.UI.IGUILayout layout)
    {
        try
        {
            var drawer = EditorDrawer?.GetValue(editor);
            var info = drawer?.GetType().GetProperty("PowertrainInfoProperties")?.GetValue(drawer);
            info?.GetType().GetMethod("OnGUI", new[] { typeof(Sprocket.UI.IGUILayout) })?.Invoke(info, new object[] { layout });
        }
        catch { }
    }

    public static void MultiplySize(CombustionEngine engine, Vector3 s)
    {
        if (EngineSize == null) return;
        var size = (Vector3)EngineSize.GetValue(engine);
        EngineSize.SetValue(engine, Vector3.Scale(size, s));
    }

    public static Sprocket.Vehicles.Colliders.IVehicleBoxCollider Bounds(CombustionEngine engine)
    {
        try { return EngineBounds?.GetValue(engine) as Sprocket.Vehicles.Colliders.IVehicleBoxCollider; }
        catch { return null; }
    }

    public static Transform ModelRoot(CombustionEngine engine)
    {
        try
        {
            if (ModelParent == null || EngineModel?.GetValue(engine) is not Il2CppObjectBase model) return null;
            var m = model.TryCast<CombustionEngineModel>();
            return m != null ? ModelParent.GetValue(m) as Transform : null;
        }
        catch { return null; }
    }

    public static IntPtr Generator(CombustionEngine engine)
    {
        try { return EngineGenerator?.GetValue(engine) is Il2CppObjectBase g ? g.Pointer : IntPtr.Zero; }
        catch { return IntPtr.Zero; }
    }
}
