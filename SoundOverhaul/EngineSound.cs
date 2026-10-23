using System;
using System.Collections.Generic;
using HarmonyLib;
using Sprocket.Powertrains.EngineAudioEffects;
using Sprocket.Vehicles.Powertrains;
using UnityEngine;

namespace SoundOverhaul;



public enum EngineVoice { Auto, T72, T64, Leopard, T55, M60, BMP1, Bradley, M113, Marder, BRDM2, M923 }





[HarmonyPatch(typeof(EngineAudioController), nameof(EngineAudioController.UpdateAudio))]
internal static class EngineSound
{
    private sealed class Stage
    {
        public float At;
        public string On, Off, Int;
    }

    private sealed class Def
    {
        public string Name;
        public Stage[] Stages;
    }

    private sealed class State
    {
        public LayerSet Set;
        public AudioSource[] Vanilla;
        public Def Def;
        public string Choice;
        public int LastState = -1;
        public float StateSince;
        public float Rpm, Throttle;
        public bool First = true;
        public bool Destroyed;   
    }

    private const int EngineField = 0x18, MinPriorityField = 0x10;
    private const int Off = 1, Starting = 2, Running = 4, ShuttingDown = 8;

    private static readonly Dictionary<IntPtr, State> States = new();
    private static readonly Dictionary<EngineVoice, Def> Defs = new();
    private static int _errors;

    private static Def Make(string key, bool loads)
    {
        var stages = new List<Stage>();
        foreach (var (at, id) in new[] { (0f, "00"), (0.33f, "33"), (0.66f, "66"), (1f, "99") })
            stages.Add(new Stage
            {
                At = at,
                On = $"eng.{key}.{id}.on",
                Off = loads ? $"eng.{key}.{id}.off" : null,
                Int = $"eng.{key}.{id}.int"
            });
        return new Def { Name = key, Stages = stages.ToArray() };
    }

    private static Def DefFor(EngineVoice v)
    {
        if (Defs.TryGetValue(v, out var d)) return d;
        d = v switch
        {
            EngineVoice.T72 => Make("t72", true),
            EngineVoice.T64 => Make("t64", true),
            EngineVoice.Leopard => Make("leo", true),
            EngineVoice.T55 => Make("t55", false),
            EngineVoice.M60 => Make("m60", false),
            EngineVoice.BMP1 => Make("bmp1", false),
            EngineVoice.Bradley => Make("m2", false),
            EngineVoice.M113 => Make("m113", false),
            EngineVoice.Marder => Make("marder", false),
            EngineVoice.BRDM2 => Make("brdm", false),
            EngineVoice.M923 => Make("m923", false),
            _ => Make("t72", true)
        };
        Defs[v] = d;
        return d;
    }

    
    private static EngineVoice Choose(string key, float litres, int cylinders)
    {
        switch (key)
        {
            case "t72": return EngineVoice.T72;
            case "t64": return EngineVoice.T64;
            case "leo": return EngineVoice.Leopard;
            case "t55": return EngineVoice.T55;
            case "m60": return EngineVoice.M60;
            case "bmp1": return EngineVoice.BMP1;
            case "m2": return EngineVoice.Bradley;
            case "m113": return EngineVoice.M113;
            case "marder": return EngineVoice.Marder;
            case "brdm": return EngineVoice.BRDM2;
            case "m923": return EngineVoice.M923;
        }
        if (litres <= 0f) return EngineVoice.T72;
        if (litres < 8f) return EngineVoice.M113;
        if (litres < 15f) return EngineVoice.Bradley;
        if (litres < 20f) return EngineVoice.BMP1;
        if (litres < 27f) return EngineVoice.Marder;
        if (litres < 33f) return EngineVoice.M60;
        if (cylinders == 10) return EngineVoice.Leopard;
        if (cylinders <= 6) return EngineVoice.T64;
        return ((int)litres % 2) == 0 ? EngineVoice.T72 : EngineVoice.T55;
    }

    private static unsafe void Prefix(EngineAudioController __instance, float volumeMultiplier, float distanceFromListener)
    {
        var key = __instance.Pointer;
        try
        {
            if (!Plugin.Active) { Release(key); return; }
            var engine = *(IntPtr*)((byte*)key + EngineField);
            if (PowertrainBridge.IsTurbine(engine)) { Release(key); return; }

            if (!States.TryGetValue(key, out var s) || s.Set == null || !s.Set.Alive)
            {
                s = Create(__instance);
                if (s == null) return;
                States[key] = s;
            }

            var info = __instance.engine.Info;
            var choice = PowertrainBridge.EngineSound(engine);
            if (s.Def == null || s.Choice != choice)
            {
                s.Choice = choice;
                s.Def = DefFor(Choose(choice, info.CylinderDisplacement * info.CylinderCount, info.CylinderCount));
                if (Plugin.DebugLogging.Value)
                    Plugin.Log.LogInfo($"Engine {info.CylinderCount} x {info.CylinderDisplacement:0.##} L ({choice}) → {s.Def.Name} sound set");
            }
            var def = s.Def;
            if (!s.Set.HasAny(def.Stages[0].On, def.Stages[0].Off, def.Stages[1].On))
            {
                Release(key, false); 
                return;
            }
            foreach (var v in s.Vanilla) VanillaMute.Mute(v);
            Update(s, info, volumeMultiplier, distanceFromListener, *(float*)((byte*)key + MinPriorityField));
        }
        catch (Exception e)
        {
            if (_errors++ < 5) Plugin.Log.LogError("Engine sound failed: " + e);
        }
    }

    private static void Postfix() => VanillaMute.Demote();

    private static State Create(EngineAudioController c)
    {
        var a = c.aChannel?.source;
        var b = c.bChannel?.source;
        var cc = c.cChannel?.source;
        if (a == null) return null;
        var set = LayerSet.Create(a, "engine", 6f);
        if (set == null) return null;
        var vanilla = new List<AudioSource> { a };
        if (b != null) vanilla.Add(b);
        if (cc != null) vanilla.Add(cc);
        if (States.Count > 256) Cleanup();
        return new State { Set = set, Vanilla = vanilla.ToArray() };
    }

    private static void Update(State s, EngineAudioParameters info, float volumeMultiplier, float distance, float minPriorityDistance)
    {
        var now = Time.time;
        var dt = Mathf.Max(0f, Time.deltaTime);
        var set = s.Set;
        var st = (int)info.CurrentState;
        if (st != s.LastState)
        {
            
            if (!s.First)
            {
                var lvl = Mathf.Max(0f, Plugin.EngineVolume.Value) * Mathf.Max(0f, volumeMultiplier);
                
                
                if (info.ShutdownReason == Sprocket.Powertrains.Engines.EngineShutdownReason.Destroyed && st != Running && st != Starting)
                {
                    if (!s.Destroyed)
                    {
                        s.Destroyed = true;
                        var hit = Mathf.Max(0f, Plugin.ImpactVolume.Value) * Mathf.Max(0.3f, volumeMultiplier);
                        set.OneShot("destroy.engine.bang", hit);
                        set.OneShot("destroy.engine", hit, UnityEngine.Random.Range(0.92f, 1.04f));
                    }
                }
                else if (st == Starting) set.OneShot("eng.start", lvl);
                else if (st == ShuttingDown) set.OneShot("eng.stop", lvl);
            }
            if (st == Running) s.Destroyed = false; 
            s.First = false;
            s.LastState = st;
            s.StateSince = now;
        }

        set.Flat = View.Inside(set.Template);
        var range = Mathf.Max(100f, info.MaxRPM - info.IdleRPM);
        var r = Mathf.Clamp01((info.RPM - info.IdleRPM) / range);
        s.Rpm = Mathf.Lerp(s.Rpm, r, 1f - Mathf.Exp(-dt * 10f));
        s.Throttle = Mathf.Lerp(s.Throttle, Mathf.Clamp01(info.Throttle), 1f - Mathf.Exp(-dt * 6f));

        
        var running = st == Running || (st == Starting && now - s.StateSince > 1.1f);
        set.Priority = LayerSet.PriorityFor(distance, minPriorityDistance);
        if (running)
        {
            var level = Mathf.Max(0f, Plugin.EngineVolume.Value) * Mathf.Max(0f, volumeMultiplier) * (0.72f + 0.28f * s.Throttle);
            var stages = s.Def.Stages;
            var k = 0;
            while (k < stages.Length - 2 && s.Rpm > stages[k + 1].At) k++;
            var t = Mathf.Clamp01((s.Rpm - stages[k].At) / Mathf.Max(0.01f, stages[k + 1].At - stages[k].At));
            AddStage(set, stages[k], Mathf.Cos(t * Mathf.PI * 0.5f), s, level);
            AddStage(set, stages[k + 1], Mathf.Sin(t * Mathf.PI * 0.5f), s, level);
            
            set.Want("eng.turbo", level * 0.25f * s.Rpm * s.Throttle, 0.85f + 0.45f * s.Rpm);
        }
        set.Apply(running ? 3f : 5f, st == ShuttingDown ? 2.5f : 4f);
    }

    private static void AddStage(LayerSet set, Stage stage, float weight, State s, float level)
    {
        if (weight < 0.001f) return;
        
        var pitch = Mathf.Clamp(1f + (s.Rpm - stage.At) * 0.5f, 0.82f, 1.25f);
        var v = level * weight;
        if (set.Flat && Library.Has(stage.Int))
        {
            set.Want(stage.Int, v, pitch);
            return;
        }
        var hasOn = Library.Has(stage.On);
        var hasOff = stage.Off != null && Library.Has(stage.Off);
        if (hasOn && hasOff)
        {
            var load = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.15f, 0.55f, s.Throttle));
            set.Want(stage.On, v * load, pitch);
            set.Want(stage.Off, v * (1f - load), pitch);
        }
        else set.Want(hasOn ? stage.On : stage.Off, v, pitch);
    }

    private static void Release(IntPtr key, bool remove = true)
    {
        if (!States.TryGetValue(key, out var s)) return;
        foreach (var v in s.Vanilla) VanillaMute.Unmute(v);
        if (remove)
        {
            s.Set?.Destroy();
            States.Remove(key);
        }
        else s.Set?.StopAll();
    }

    private static void Cleanup()
    {
        var dead = new List<IntPtr>();
        foreach (var kv in States) if (kv.Value.Set == null || !kv.Value.Set.Alive) dead.Add(kv.Key);
        foreach (var k in dead) States.Remove(k);
    }

    public static void ReleaseAll()
    {
        foreach (var k in new List<IntPtr>(States.Keys)) Release(k);
    }
}
