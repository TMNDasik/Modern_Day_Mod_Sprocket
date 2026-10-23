using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Operations;
using Sprocket.Vehicles;
using Sprocket.Vehicles.PlateStructures;
using Sprocket.Vehicles.Serialization;
using UnityEngine;
using GameOp = Operations.Operation;

namespace ActiveProtection;


internal sealed class EraHost
{
    public VehicleObject Vo;
    public IntPtr Ptr;
    public EraConfig C;
    public int Version;                                   
    public readonly HashSet<int> Spent = new();           
    public bool InBattle;
    
    
    public bool BlownMain, BlownMirror;
    public bool BlownOff => C != null && C.HasMirrorBox ? BlownMain && BlownMirror : BlownMain;
    public readonly List<Renderer> Hidden = new();
    public readonly List<(MeshFilter Filter, Mesh Original, Mesh Cut)> CutMeshes = new();
    public readonly List<(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> Tris, int T, int A, int B, int C)> Removed = new();
    public Vehicle VehicleCached;
    public float VehicleAt = -100f;
    public bool MarkedDynamic;
    
    public Vector3 MeasuredPos;
    public Quaternion MeasuredRot;
    public int MeasuredRebuild = -1;

    public bool Alive
    {
        get
        {
            try { return Vo != null && !Vo.WasCollected && Vo.transform != null; }
            catch { return false; }
        }
    }

    public Transform T => Vo.transform;

    public Vector3 WorldCenter => T.TransformPoint(C.LocalBounds.center);
}



internal static class EraHosts
{
    public const string Key = "era";
    private static readonly Dictionary<IntPtr, EraHost> All = new();
    private static readonly List<EraHost> Buffer = new();

    public static EraHost Get(VehicleObject vo)
    {
        if (vo == null) return null;
        try { return All.TryGetValue(vo.Pointer, out var h) && h.Alive && h.C != null ? h : null; }
        catch { return null; }
    }

    
    public static void Set(VehicleObject vo, EraConfig c)
    {
        if (vo == null) return;
        if (c == null)
        {
            All.Remove(vo.Pointer);
            return;
        }
        if (!All.TryGetValue(vo.Pointer, out var h) || !h.Alive)
        {
            h = new EraHost { Vo = vo, Ptr = vo.Pointer };
            All[vo.Pointer] = h;
        }
        c.Sanitize();
        h.C = c;
        h.Version++;
        if (c.On && !h.MarkedDynamic)
        {
            h.MarkedDynamic = true;
            MarkDynamic(vo);
        }
    }

    
    public static void MarkDynamic(VehicleObject vo)
    {
        try
        {
            foreach (var m in vo.GetComponentsInChildren<VehicleObjectModel>(true))
            {
                var owner = m != null ? m.GetComponentInParent<VehicleObject>() : null;
                if (owner != null && owner.Pointer == vo.Pointer) m.MarkDynamic();
            }
        }
        catch { }
    }

    public static bool IsOn(VehicleObject vo) => Get(vo)?.C?.On == true;

    public static bool Has(IntPtr vo) => All.TryGetValue(vo, out var h) && h.Alive && h.C != null && h.C.On;

    public static void SetToken(VehicleObject vo, string token) => Set(vo, token == null ? null : EraConfig.FromToken(token));

    public static List<EraHost> Snapshot()
    {
        Buffer.Clear();
        List<IntPtr> dead = null;
        foreach (var kv in All)
        {
            if (kv.Value.Alive && kv.Value.C != null) Buffer.Add(kv.Value);
            else (dead ??= new List<IntPtr>()).Add(kv.Key);
        }
        if (dead != null) foreach (var k in dead) All.Remove(k);
        return Buffer;
    }

    public static Vehicle VehicleOf(EraHost h)
    {
        var now = Time.unscaledTime;
        try
        {
            if (h.VehicleCached != null && now - h.VehicleAt < 1f && now >= h.VehicleAt && !h.VehicleCached.WasCollected && h.VehicleCached)
                return h.VehicleCached;
        }
        catch { }
        try { h.VehicleCached = h.T.GetComponentInParent<Vehicle>(); }
        catch { h.VehicleCached = null; }
        h.VehicleAt = now;
        return h.VehicleCached;
    }

    public static VehicleObject MirrorOf(VehicleObject vo)
    {
        try
        {
            var m = vo?.VehicleTransform?.Mirror?.VehicleObject;
            return m != null && m.Pointer != vo.Pointer ? m : null;
        }
        catch { return null; }
    }

    
    
    

    private static readonly Dictionary<IntPtr, (string Guid, bool Ok)> EligibleCache = new();

    public static bool Eligible(VehicleObject vo)
    {
        if (vo == null) return false;
        var guid = vo.guid ?? "";
        if (EligibleCache.TryGetValue(vo.Pointer, out var cached) && cached.Guid == guid) return cached.Ok;
        var ok = false;
        try
        {
            if (!ApsParts.IsOurs(vo))
            {
                var plate = PlateOf(vo);
                if (plate != null) ok = plate.Applique;
                else
                    foreach (var m in vo.GetComponentsInChildren<VehicleObjectModel>(true))
                    {
                        var owner = m != null ? m.GetComponentInParent<VehicleObject>() : null;
                        if (owner == null || owner.Pointer != vo.Pointer) continue;
                        if ((m.PartType & VehiclePartType.AppliqueArmour) != 0) { ok = true; break; }
                    }
            }
        }
        catch { ok = false; }
        if (EligibleCache.Count > 4096) EligibleCache.Clear();
        EligibleCache[vo.Pointer] = (guid, ok);
        return ok;
    }

    public static PlateStructure PlateOf(VehicleObject vo)
    {
        try
        {
            foreach (var c in vo.behaviours)
            {
                var p = c != null ? c.TryCast<PlateStructure>() : null;
                if (p != null) return p;
            }
        }
        catch { }
        return null;
    }

    
    

    
    
    public static bool Measure(VehicleObject vo, EraConfig c)
    {
        var panels = new List<EraFace>();
        var plate = PlateOf(vo);
        var ok = false;
        Vector3 min = Vector3.zero, max = Vector3.zero, mirMin = Vector3.zero, mirMax = Vector3.zero;
        if (plate != null)
            try
            {
                ok = EraGeometry.FromStructure(vo, plate, panels);
                min = EraGeometry.PartMin * 1000f;
                max = EraGeometry.PartMax * 1000f;
                
                if (panels.Exists(p => p.Mirror) && EraGeometry.MirMax.x >= EraGeometry.MirMin.x)
                {
                    mirMin = EraGeometry.MirMin * 1000f;
                    mirMax = EraGeometry.MirMax * 1000f;
                }
            }
            catch (Exception e)
            {
                if (Plugin.DebugLogging.Value) Plugin.Log.LogWarning("ERA faces failed: " + e.Message);
                ok = false;
            }
        if (!ok)
        {
            panels.Clear();
            ok = BoxPanel(vo, panels, out min, out max);
        }
        if (!ok) return false;
        min = Round(min);
        max = Round(max);
        mirMin = Round(mirMin);
        mirMax = Round(mirMax);
        if (EraGeometry.Same(c.Panels, panels) && (c.PartMin - min).sqrMagnitude < 1f && (c.PartMax - max).sqrMagnitude < 1f &&
            (c.MirMin - mirMin).sqrMagnitude < 1f && (c.MirMax - mirMax).sqrMagnitude < 1f) return false;
        c.Panels = panels;
        c.PartMin = min;
        c.PartMax = max;
        c.MirMin = mirMin;
        c.MirMax = mirMax;
        c.Sanitize();
        return true;
    }

    private static Vector3 Round(Vector3 v) => new(Mathf.Round(v.x), Mathf.Round(v.y), Mathf.Round(v.z));

    
    private static bool BoxPanel(VehicleObject vo, List<EraFace> panels, out Vector3 partMin, out Vector3 partMax)
    {
        partMin = partMax = Vector3.zero;
        var t = vo.transform;
        var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        var max = -min;
        var any = false;

        void Add(MeshFilter f)
        {
            var mesh = f != null ? f.sharedMesh : null;
            if (mesh == null) return;
            var b = mesh.bounds;
            if (b.size.sqrMagnitude < 1e-10f) return;
            var ft = f.transform;
            for (var i = 0; i < 8; i++)
            {
                var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) != 0 ? 1 : -1, (i & 2) != 0 ? 1 : -1, (i & 4) != 0 ? 1 : -1));
                var local = t.InverseTransformPoint(ft.TransformPoint(corner));
                min = Vector3.Min(min, local);
                max = Vector3.Max(max, local);
                any = true;
            }
        }

        try
        {
            var plate = PlateOf(vo);
            if (plate != null)
            {
                var r = plate.highDetailRenderer;
                if (r != null) Add(r.Filter);
            }
            if (!any)
                foreach (var f in vo.GetComponentsInChildren<MeshFilter>(true))
                {
                    var owner = f != null ? f.GetComponentInParent<VehicleObject>() : null;
                    if (owner == null || owner.Pointer != vo.Pointer) continue;
                    Add(f);
                }
        }
        catch { }
        if (!any) return false;

        var size = (max - min) * 1000f;
        var center = (max + min) * 500f;
        partMin = min * 1000f;
        partMax = max * 1000f;
        var axis = 0;
        for (var i = 1; i < 3; i++) if (size[i] < size[axis]) axis = i;
        var vehicle = vo.GetComponentInParent<Vehicle>();
        var vUp = t.InverseTransformDirection(vehicle != null ? vehicle.transform.up : Vector3.up).normalized;
        var vFwd = t.InverseTransformDirection(vehicle != null ? vehicle.transform.forward : Vector3.forward).normalized;
        var reference = t.InverseTransformPoint(vehicle != null ? vehicle.transform.position + vehicle.transform.up : t.root.position) * 1000f;
        var n = AxisVec(axis) * (center[axis] - reference[axis] >= 0f ? 1f : -1f);
        
        int a1 = axis == 0 ? 1 : 0, a2 = axis == 2 ? 1 : 2;
        var guide = Mathf.Max(Mathf.Abs(vUp[a1]), Mathf.Abs(vUp[a2])) > 0.3f ? vUp : vFwd;
        var upAxis = Mathf.Abs(guide[a1]) >= Mathf.Abs(guide[a2]) ? a1 : a2;
        var frameUp = AxisVec(upAxis) * (guide[upAxis] >= 0f ? 1f : -1f);
        var panel = new EraFace { Out = n, Up = frameUp, D = size[axis], H = size[upAxis], W = size[3 - axis - upAxis] };
        panel.Center = center + n * (size[axis] * 0.5f);
        panels.Add(EraGeometry.Rounded(panel));
        return true;
    }

    private static Vector3 AxisVec(int axis)
    {
        var v = Vector3.zero;
        v[axis] = 1f;
        return v;
    }

    
    
    private static float _nextMeasure;
    private static int _rebuilds;

    [HarmonyPatch(typeof(PlateStructureMesh), nameof(PlateStructureMesh.ExecuteRebuild))]
    private static class RebuildHook
    {
        private static void Postfix() => _rebuilds++;
    }

    public static void Tick()
    {
        if (!DesignerState.Active || Time.unscaledTime < _nextMeasure || All.Count == 0) return;
        _nextMeasure = Time.unscaledTime + 0.5f;
        var measured = 0;
        foreach (var h in Snapshot())
        {
            if (measured >= 6) break;
            try
            {
                var v = VehicleOf(h);
                if (v == null || v.BehaviourEnabled) continue;
                var t = h.T;
                var pos = t.position;
                var rot = t.rotation;
                if (h.MeasuredRebuild == _rebuilds && (pos - h.MeasuredPos).sqrMagnitude < 1e-8f && Quaternion.Angle(rot, h.MeasuredRot) < 0.01f) continue;
                h.MeasuredRebuild = _rebuilds;
                h.MeasuredPos = pos;
                h.MeasuredRot = rot;
                measured++;
                if (Measure(h.Vo, h.C)) h.Version++;
            }
            catch { }
        }
    }

    

    [HarmonyPatch(typeof(VehicleObjectSerialization), nameof(VehicleObjectSerialization.ToBlueprint))]
    private static class SavePatch
    {
        private static void Postfix(VehicleObject obj, VehicleObjectBlueprint __result)
        {
            try
            {
                var h = Get(obj);
                var state = __result?.State;
                if (h == null || state == null || state.FindElement(Key) >= 0) return;
                state.AddValue(Key, new Il2CppSystem.Object(IL2CPP.ManagedStringToIl2Cpp(h.C.ToToken())));
            }
            catch (Exception e) { Plugin.Log.LogError("ERA settings save failed: " + e); }
        }
    }

    [HarmonyPatch(typeof(VehicleObjectSerialization), nameof(VehicleObjectSerialization.ApplyBaseObjectState))]
    private static class LoadPatch
    {
        private static void Postfix(VehicleObject obj, Il2CppSystem.Runtime.Serialization.SerializationInfo state)
        {
            try
            {
                if (obj == null || state == null || state.FindElement(Key) < 0) return;
                var c = EraConfig.FromToken(state.GetString(Key));
                if (c != null) Set(obj, c);
            }
            catch (Exception e) { Plugin.Log.LogError("ERA settings load failed: " + e); }
        }
    }
}



internal static class EraOps
{
    private sealed class Pending
    {
        public IntPtr Vo;
        public string After, AfterMirror;
    }

    private static string _lastKey;
    private static float _lastAt;
    private static Pending _last;

    public static void Edit(VehicleObject vo, string name, string key, Func<EraConfig, EraConfig> change)
    {
        if (vo == null) return;
        var current = EraHosts.Get(vo)?.C;
        var before = current?.ToToken();
        var next = change(current?.Clone());
        next?.Sanitize();
        var after = next?.ToToken();
        if (after == before) return;

        var partner = EraHosts.MirrorOf(vo);
        string beforeMirror = null, afterMirror = null;
        if (partner != null)
        {
            var pc = EraHosts.Get(partner)?.C;
            beforeMirror = pc?.ToToken();
            if (next != null)
            {
                var m = next.Mirrored();
                
                if (pc != null && pc.HasGeometry) m.Panels = pc.Panels;
                else
                {
                    m.Panels = new List<EraFace>();
                    EraHosts.Measure(partner, m);
                }
                afterMirror = m.ToToken();
            }
        }

        var now = Time.unscaledTime;
        
        if (key != null) _redrawAt = now + 0.6f;
        if (key != null && key == _lastKey && _last != null && _last.Vo == vo.Pointer && now - _lastAt < 0.8f && now >= _lastAt)
        {
            _lastAt = now;
            _last.After = after;
            _last.AfterMirror = afterMirror;
            Apply(vo, partner, after, afterMirror, false);
            return;
        }
        var pending = new Pending { Vo = vo.Pointer, After = after, AfterMirror = afterMirror };
        _lastKey = key;
        _lastAt = now;
        _last = pending;
        var redraw = key == null;
        Run(name, () => Apply(vo, partner, pending.After, pending.AfterMirror, redraw), () => Apply(vo, partner, before, beforeMirror, true));
    }

    private static float _redrawAt = -1f;

    public static void Tick()
    {
        if (_redrawAt < 0f || Time.unscaledTime < _redrawAt) return;
        
        try
        {
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null && mouse.leftButton.isPressed)
            {
                _redrawAt = Time.unscaledTime + 0.2f;
                return;
            }
        }
        catch { }
        _redrawAt = -1f;
        EraHostPanel.Redraw();
    }

    private static void Apply(VehicleObject vo, VehicleObject partner, string token, string mirrorToken, bool redraw)
    {
        EraHosts.SetToken(vo, token);
        if (partner != null) EraHosts.SetToken(partner, mirrorToken);
        if (redraw) EraHostPanel.Redraw();
    }

    private static void Run(string name, Action apply, Action revert)
    {
        var exec = DesignerState.Executer;
        var applied = false;
        void Apply()
        {
            applied = true;
            try { apply(); }
            catch (Exception e) { Plugin.Log.LogError($"{name} failed: {e}"); }
        }
        if (exec == null)
        {
            Apply();
            return;
        }
        try
        {
            var op = new GameOp(name, null);
            op.Flags = OperationFlags.Undo | OperationFlags.Redo | OperationFlags.Register;
            op.Execute = (Func<GameOp, OperatorContext, ExecutionResult>)((_, _) =>
            {
                Apply();
                return ExecutionResult.Executed;
            });
            op.Revert = (Action<GameOp, OperatorContext>)((_, _) =>
            {
                try { revert(); }
                catch (Exception e) { Plugin.Log.LogError($"{name} undo failed: {e}"); }
            });
            exec.Execute(op, -1, ExecuteMode.Synchronous);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"{name}: undo history unavailable ({e.Message}), applying directly");
            if (!applied) Apply();
        }
    }
}
