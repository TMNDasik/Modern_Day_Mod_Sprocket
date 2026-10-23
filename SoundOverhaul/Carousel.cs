using System.Collections.Generic;
using UnityEngine;

namespace SoundOverhaul;








internal static class Carousel
{
    private sealed class State
    {
        public string P;
        public Transform Gun;
        public bool Inside;
        public Voice Start, In, Loop, Rattle, Out, Shell;
        public float Volume;
        public bool Rotating;
    }

    private static readonly Dictionary<int, State> States = new();

    public static float LoadLength(bool mz)
    {
        var p = mz ? "mz" : "az";
        return Mathf.Max(Library.Length(p + ".shell"), Library.Length(p + ".shell.int"));
    }

    public static bool Event(int id, int evt, Transform gun, bool mz, float value)
    {
        if (!Plugin.Active) { Stop(id, 0.1f); return false; }
        var p = mz ? "mz" : "az";
        if (!Library.Has(p + ".loop") || !Library.Has(p + ".shell")) return false;
        if (!States.TryGetValue(id, out var s))
        {
            if (evt == 3 || evt == 4) return true;
            s = new State();
            States[id] = s;
        }
        s.P = p;
        if (gun != null) s.Gun = gun;
        switch (evt)
        {
            case 0: RotationStart(s); break;
            case 1: RotationStop(s, true); break;
            case 2: LoadStart(s); break;
            case 3: Stop(id, 0.25f); break;
            case 4: Speed(s, value); break;
        }
        return true;
    }

    private static void RotationStart(State s)
    {
        StopAll(s, 0.03f);
        s.Inside = View.Inside(s.Gun);
        s.Volume = Plugin.MechanismVolume.Value * (s.Inside ? 1f : 0.85f);
        var inLen = Len(s, "in");
        s.Start = Play(s, "start", 0f, false);
        s.In = Play(s, "in", 0f, false);
        
        s.Loop = Play(s, "loop", inLen, true);
        s.Rattle = Play(s, "rattle", inLen, true, 0.6f);
        s.Rotating = true;
    }

    private static void RotationStop(State s, bool playOut)
    {
        if (!s.Rotating) return;
        s.Rotating = false;
        Voices.Stop(s.In, 0.03f);
        Voices.Stop(s.Loop, 0.03f);
        Voices.Stop(s.Rattle, 0.03f);
        s.In = s.Loop = s.Rattle = null;
        if (playOut) s.Out = Play(s, "out", 0f, false);
    }

    private static void LoadStart(State s)
    {
        
        if (s.Rotating) RotationStop(s, true);
        if (s.Gun != null) s.Inside = View.Inside(s.Gun);
        s.Volume = Plugin.MechanismVolume.Value * (s.Inside ? 1f : 0.85f);
        s.Shell = Play(s, "shell", 0f, false);
    }

    private static void Speed(State s, float speed01)
    {
        if (!s.Rotating) return;
        var k = Mathf.Sqrt(Mathf.Clamp01(speed01));
        var pitch = 0.85f + 0.15f * k;
        Voices.SetPitch(s.Loop, pitch);
        Voices.SetPitch(s.Rattle, pitch);
        var level = s.Volume * (0.55f + 0.45f * k);
        if (Voices.Alive(s.Loop)) Voices.SetVolume(s.Loop, level * Library.Gain(s.P + ".loop"));
        if (Voices.Alive(s.Rattle)) Voices.SetVolume(s.Rattle, level * 0.6f * Library.Gain(s.P + ".rattle"));
    }

    private static void Stop(int id, float fade)
    {
        if (!States.TryGetValue(id, out var s)) return;
        StopAll(s, fade);
        States.Remove(id);
    }

    private static void StopAll(State s, float fade)
    {
        Voices.Stop(s.Start, fade);
        Voices.Stop(s.In, fade);
        Voices.Stop(s.Loop, fade);
        Voices.Stop(s.Rattle, fade);
        Voices.Stop(s.Out, fade);
        Voices.Stop(s.Shell, fade);
        s.Start = s.In = s.Loop = s.Rattle = s.Out = s.Shell = null;
        s.Rotating = false;
    }

    public static void StopEverything()
    {
        foreach (var s in States.Values) StopAll(s, 0.1f);
        States.Clear();
    }

    private static float Len(State s, string part)
    {
        var bank = s.P + "." + part + (s.Inside && Library.Has(s.P + "." + part + ".int") ? ".int" : "");
        return Library.Length(bank);
    }

    private static Voice Play(State s, string part, float delay, bool loop, float volume = 1f)
    {
        var bank = s.P + "." + part;
        if (s.Inside && Library.Has(bank + ".int")) bank += ".int";
        var clip = Library.Pick(bank);
        if (clip == null) return null;
        var vol = s.Volume * volume * Library.Gain(bank);
        VoiceSpec spec;
        var own = false;
        if (s.Inside || s.Gun == null) spec = VoiceSpec.Flat(vol);
        else
        {
            
            own = View.OwnVehicle(s.Gun);
            spec = VoiceSpec.At(s.Gun.position, vol, own ? 7f : 4f, 400f);
            spec.Follow = s.Gun;
        }
        spec.Delay = delay;
        spec.Loop = loop;
        
        spec.Priority = s.Inside ? 24 : own ? 40 : 85;
        return Voices.Play(clip, spec);
    }
}
