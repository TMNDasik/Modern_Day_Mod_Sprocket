using System;
using System.Collections.Generic;
using Operations;
using Sprocket.Vehicles;
using GameOp = Operations.Operation;

namespace VehicleMaterials;


internal static class Ops
{
    private static void Run(string name, Action apply, Action revert)
    {
        var exec = Hooks.Executer;
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

    private static void Changed()
    {
        Applier.Changed();
        Panel.RequestRedraw();
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

    
    public static void SetWhole(VehicleObject vo, MaterialKind kind)
    {
        if (vo == null) return;
        var partner = MirrorOf(vo);
        var old = Store.Get(vo)?.Whole ?? MaterialKind.Default;
        var oldPartner = partner != null ? Store.Get(partner)?.Whole ?? MaterialKind.Default : MaterialKind.Default;
        if (old == kind && (partner == null || oldPartner == kind)) return;
        Run("Part material", () =>
        {
            Store.SetWhole(vo, kind);
            if (partner != null) Store.SetWhole(partner, kind);
            Changed();
        }, () =>
        {
            Store.SetWhole(vo, old);
            if (partner != null) Store.SetWhole(partner, oldPartner);
            Changed();
        });
    }

    
    public static void SetFaces(VehicleObject vo, List<int> indices, int tag)
    {
        if (vo == null || indices == null || indices.Count == 0) return;
        var em = FaceTags.Mesh(vo);
        if (em == null) return;
        var idx = indices.ToArray();
        var before = Pick(FaceTags.Read(em), idx);
        var partner = MirrorOf(vo);
        var partnerMesh = SeparateMesh(vo, partner);
        var beforePartner = partnerMesh != null ? Pick(FaceTags.Read(partnerMesh), idx) : null;
        var changed = false;
        foreach (var t in before) if (t != tag) { changed = true; break; }
        if (!changed && beforePartner == null) return;
        var after = new[] { tag };
        Run("Face material", () =>
        {
            Write(vo, idx, after);
            if (partner != null)
            {
                var pm = SeparateMesh(vo, partner);
                if (pm != null) FaceTags.Write(pm, idx, after);
                Snap(partner, pm ?? FaceTags.Mesh(partner));
            }
            Changed();
        }, () =>
        {
            Write(vo, idx, before);
            if (partner != null)
            {
                var pm = SeparateMesh(vo, partner);
                if (pm != null && beforePartner != null) FaceTags.Write(pm, idx, beforePartner);
                Snap(partner, pm ?? FaceTags.Mesh(partner));
            }
            Changed();
        });
    }

    private static void Write(VehicleObject vo, int[] idx, int[] tags)
    {
        var em = FaceTags.Mesh(vo);
        if (em == null) return;
        FaceTags.Write(em, idx, tags);
        Snap(vo, em);
    }

    
    private static void Snap(VehicleObject vo, Sprocket.PlateMesh.EditMesh em)
    {
        if (vo == null) return;
        var m = Store.Ensure(vo);
        if (em != null)
        {
            m.Faces = FaceTags.Read(em);
            m.FacesPending = false;
        }
    }

    
    private static Sprocket.PlateMesh.EditMesh SeparateMesh(VehicleObject vo, VehicleObject partner)
    {
        if (partner == null) return null;
        var own = FaceTags.Mesh(vo);
        var pm = FaceTags.Mesh(partner);
        if (own == null || pm == null || pm.Pointer == own.Pointer) return null;
        return FaceTags.Count(pm) == FaceTags.Count(own) ? pm : null;
    }

    private static int[] Pick(int[] tags, int[] idx)
    {
        var r = new int[idx.Length];
        if (tags == null) return r;
        for (var i = 0; i < idx.Length; i++) r[i] = idx[i] >= 0 && idx[i] < tags.Length ? tags[idx[i]] : 0;
        return r;
    }
}
