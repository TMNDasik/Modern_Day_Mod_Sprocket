using System;
using System.Collections.Generic;
using Sprocket.ArtificialIntelligence;
using Sprocket.Vehicles;
using UnityEngine;

namespace GunStabilizer;





internal static class FcsAi
{
    private sealed class Layer
    {
        public GunLayerAI Ai;
        public float Range, Velocity;   
        public float AppliedRange = -1f, AppliedVelocity = -1f;
    }

    private static readonly Dictionary<IntPtr, Layer> Layers = new();
    private static readonly HashSet<int> Logged = new();
    private static float _next;
    private static int _errors;

    public static void Tick()
    {
        if (!Plugin.FcsAffectAI.Value || Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + 2f;
        try
        {
            var vehicles = UnityEngine.Object.FindObjectsOfType<Vehicle>();
            foreach (var v in vehicles)
            {
                if (v == null) continue;
                var fcs = FcsRegistry.ForVehicle(v);
                if (fcs == null) continue;
                CommanderAI commander;
                try { commander = v.behaviour?.OrderReciever?.TryCast<CommanderAI>(); }
                catch { continue; }
                var layers = commander?.gunLayers;
                if (layers == null) continue;
                var h = FcsDamage.Of(fcs.Id);
                var health = h != null ? h.Value : 1f;
                var s = fcs.S;
                var range = s.Laser ? (s.IsAnalog ? 0.25f : 0.12f) : 1f;
                var velocity = s.Lead ? (s.IsAnalog ? 0.5f : 0.3f) : 1f;
                
                range = Mathf.Lerp(1f, range, health);
                velocity = Mathf.Lerp(1f, velocity, health);
                for (var i = 0; i < layers.Length; i++) Apply(layers[i], range, velocity);
                var key = v.gameObject.GetInstanceID();
                if (Logged.Add(key))
                    Plugin.Log.LogInfo($"AI '{v.name}': fire control computer ({s.Label}) — range error ×{range:0.00}, target speed error ×{velocity:0.00}");
            }
        }
        catch (Exception e) { if (_errors++ < 5) Plugin.Log.LogWarning("AI fire control failed: " + e.Message); }
    }

    private static void Apply(GunLayerAI ai, float range, float velocity)
    {
        if (ai == null) return;
        var ptr = ai.Pointer;
        if (!Layers.TryGetValue(ptr, out var l) || l.Ai == null || l.Ai.WasCollected)
        {
            l = new Layer { Ai = ai, Range = ai.maxRangeError, Velocity = ai.maxVelocityError };
            Layers[ptr] = l;
        }
        if (Mathf.Abs(l.AppliedRange - range) < 1e-3f && Mathf.Abs(l.AppliedVelocity - velocity) < 1e-3f) return;
        ai.maxRangeError = l.Range * range;
        ai.maxVelocityError = l.Velocity * velocity;
        l.AppliedRange = range;
        l.AppliedVelocity = velocity;
    }

    public static void Forget()
    {
        Layers.Clear();
        Logged.Clear();
    }
}
