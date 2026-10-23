using UnityEngine;

namespace SoundOverhaul;



internal static class Sfx
{
    public static bool IsImpact(string bank) =>
        bank.StartsWith("hit.") || bank.StartsWith("expl.") || bank.StartsWith("fire.") || bank.StartsWith("tree.") ||
        bank.StartsWith("debris.") || bank.StartsWith("destroy.") || bank.StartsWith("aps.") || bank.StartsWith("smoke.");

    
    private static float Level(string key, float volume) => Mathf.Max(0f, volume) * Library.Gain(key) * Mathf.Max(0f, Plugin.ImpactVolume.Value);

    
    
    public static bool At(string bank, Vector3 position, Transform follow, float volume, float pitch, float minDistance, int priority,
        bool delayed = true)
    {
        if (!Plugin.Active || bank == null) return false;
        var key = bank;
        var d = Vector3.Distance(position, View.Listener);
        if (d > 700f && Library.Has(bank + ".far")) key = bank + ".far";
        else if (d > 200f && Library.Has(bank + ".distant")) key = bank + ".distant";
        var clip = Library.Pick(key);
        if (clip == null) return false;
        var s = VoiceSpec.At(position, Level(key, volume), minDistance, 3000f);
        if (delayed)
        {
            s.Delay = Voices.SoundDelay(position);
            if (s.Delay < 0.03f) s.Delay = 0f;
        }
        if (follow != null && !follow.WasCollected) s.Follow = follow;
        s.Pitch = pitch <= 0f ? 1f : pitch;
        s.Priority = priority;
        return Voices.Play(clip, s) != null;
    }

    
    public static bool Flat(string bank, float volume, float pitch, int priority)
    {
        if (!Plugin.Active || bank == null) return false;
        var clip = Library.Pick(bank);
        if (clip == null) return false;
        var s = VoiceSpec.Flat(Level(bank, volume));
        s.Pitch = pitch <= 0f ? 1f : pitch;
        s.Priority = priority;
        return Voices.Play(clip, s) != null;
    }
}
