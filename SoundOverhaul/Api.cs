using System.Collections.Generic;
using UnityEngine;

namespace SoundOverhaul;



public static class Api
{
    private static readonly Dictionary<int, Voice> Loops = new();
    private static int _nextLoop = 1;

    public static bool Active => Plugin.Active;

    
    
    public static bool Play(string bank, Vector3 position, Transform part, float volume, float pitch)
    {
        if (!Plugin.Active || string.IsNullOrEmpty(bank)) return false;
        var inside = part != null && View.Inside(part) && Library.Has(bank + ".int");
        var key = bank;
        if (inside) key = bank + ".int";
        else
        {
            var d = Vector3.Distance(position, View.Listener);
            if (d > 700f && Library.Has(bank + ".far")) key = bank + ".far";
            else if (d > 200f && Library.Has(bank + ".distant")) key = bank + ".distant";
        }
        var clip = Library.Pick(key);
        if (clip == null) return false;
        var vol = Mathf.Max(0f, volume) * Library.Gain(key) * Category(bank);
        VoiceSpec s;
        if (inside) s = VoiceSpec.Flat(vol);
        else
        {
            s = VoiceSpec.At(position, vol, MinDistance(bank), 3000f);
            s.Delay = Voices.SoundDelay(position);
            if (s.Delay < 0.03f) s.Delay = 0f;
            if (part != null && !part.WasCollected) s.Follow = part;
        }
        s.Pitch = pitch <= 0f ? 1f : pitch;
        
        s.Priority = inside ? 32 : part != null && View.OwnVehicle(part) ? 40 : 85;
        return Voices.Play(clip, s) != null;
    }

    
    public static int Loop(string bank, Transform follow, float volume)
    {
        if (!Plugin.Active || follow == null) return 0;
        var clip = Library.Pick(bank);
        if (clip == null) return 0;
        var s = VoiceSpec.At(follow.position, Mathf.Max(0f, volume) * Library.Gain(bank) * Category(bank), MinDistance(bank), 2500f);
        s.Follow = follow;
        s.Loop = true;
        s.Priority = 85;
        s.StartTime = Random.Range(0f, clip.length * 0.5f);
        var v = Voices.Play(clip, s);
        if (v == null) return 0;
        var id = _nextLoop++;
        Loops[id] = v;
        return id;
    }

    public static void StopLoop(int id, float fade)
    {
        if (!Loops.TryGetValue(id, out var v)) return;
        Loops.Remove(id);
        Voices.Stop(v, fade);
    }

    
    public static bool Carousel(int id, int evt, Transform gun, bool mz, float value) =>
        SoundOverhaul.Carousel.Event(id, evt, gun, mz, value);

    
    public static float CarouselLoadLength(bool mz) => Plugin.Active ? SoundOverhaul.Carousel.LoadLength(mz) : 0f;

    public static float Length(string bank) => Plugin.Active ? Library.Length(bank) : 0f;

    private static float Category(string bank)
    {
        if (bank.StartsWith("casing.") || bank.StartsWith("load.")) return Plugin.LoadingVolume.Value;
        if (Sfx.IsImpact(bank)) return Plugin.ImpactVolume.Value;
        return Plugin.MechanismVolume.Value;
    }

    private static float MinDistance(string bank)
    {
        if (bank.StartsWith("casing.")) return 2.5f;
        if (bank.StartsWith("atgm.") || bank.StartsWith("rocket.")) return 15f;
        if (bank.StartsWith("gl.")) return 10f;
        if (bank.StartsWith("vent.")) return bank == "vent.land" ? 6f : 25f;
        if (bank.StartsWith("aps.")) return 25f;
        if (bank.StartsWith("smoke.")) return 15f;
        return 8f;
    }

    internal static void StopAllLoops()
    {
        foreach (var v in Loops.Values) Voices.Stop(v, 0.1f);
        Loops.Clear();
    }
}
