using System;
using System.IO;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Powertrain;





internal static class Sounds
{
    private static AssetBundle _bundle;
    private static AudioClip _idle, _start, _stop, _fss;
    private static Texture2D _ring, _disc, _glow, _panel, _chip;
    private static bool _failed, _loaded;

    
    public const float StartCrossfade = 2.4f;

    
    
    private const float StartEndPitch = 5839f / 5872f, StartPitchSlope = 11.8f / 5872f;
    public const float StopHeadPitch = 5872f / 5211f, StopPitchGlide = 1.4f;

    
    
    public static float StartSettle(float sinceEnd)
    {
        if (sinceEnd <= -StartCrossfade - 0.5f) return 1f;
        if (sinceEnd < 0f) return StartEndPitch + StartPitchSlope * sinceEnd;
        var settle = 2f * (1f - StartEndPitch) / StartPitchSlope;
        var u = Mathf.Clamp01(sinceEnd / settle);
        return StartEndPitch + (1f - StartEndPitch) * (1f - (1f - u) * (1f - u));
    }

    public static AudioClip Idle => Ensure() ? Alive(_idle) : null;
    public static AudioClip Start => Ensure() ? Alive(_start) : null;
    public static AudioClip Stop => Ensure() ? Alive(_stop) : null;
    
    public static AudioClip FssDischarge => Ensure() ? Alive(_fss) : null;

    public static Texture2D Ring => Ensure() ? Alive(_ring) : null;
    public static Texture2D Disc => Ensure() ? Alive(_disc) : null;
    public static Texture2D Glow => Ensure() ? Alive(_glow) : null;
    public static Texture2D Panel => Ensure() ? Alive(_panel) : null;
    public static Texture2D Chip => Ensure() ? Alive(_chip) : null;

    public static float StartLength => Length(Start, 21.92f);
    public static float StopLength => Length(Stop, 6.641f);

    private static float Length(AudioClip c, float fallback)
    {
        try { return c != null ? c.length : fallback; }
        catch { return fallback; }
    }

    private static T Alive<T>(T o) where T : Object => o != null && !o.WasCollected ? o : null;

    public static bool Ensure()
    {
        if (_loaded && _bundle != null && Alive(_idle) != null) return true;
        if (_failed) return false;
        try
        {
            if (_bundle == null)
            {
                string path = null;
                foreach (var p in new[]
                         {
                             Path.Combine(Plugin.PluginDir, "powertrain.bundle"),
                             Path.Combine(Plugin.PluginDir, "MDPowertrain", "powertrain.bundle"),
                             Path.Combine(Plugin.PluginDir, "Powertrain", "powertrain.bundle")
                         })
                    if (File.Exists(p)) { path = p; break; }
                if (path == null)
                {
                    _failed = true;
                    Plugin.Log.LogWarning("powertrain.bundle not found — gas turbine sounds and gear panel graphics disabled");
                    return false;
                }
                _bundle = BundleLoader.LoadFromFile(path);
                if (_bundle == null) throw new InvalidOperationException("bundle load returned null");
            }
            const string root = "assets/powertrain/";
            _idle = Keep(BundleLoader.LoadAsset<AudioClip>(_bundle, root + "audio/gt_idle_loop.wav"));
            _start = Keep(BundleLoader.LoadAsset<AudioClip>(_bundle, root + "audio/gt_start.wav"));
            _stop = Keep(BundleLoader.LoadAsset<AudioClip>(_bundle, root + "audio/gt_stop.wav"));
            _fss = Keep(BundleLoader.LoadAsset<AudioClip>(_bundle, root + "audio/fss_discharge.wav"));
            _ring = Keep(BundleLoader.LoadAsset<Texture2D>(_bundle, root + "hud/ring.png"));
            _disc = Keep(BundleLoader.LoadAsset<Texture2D>(_bundle, root + "hud/disc.png"));
            _glow = Keep(BundleLoader.LoadAsset<Texture2D>(_bundle, root + "hud/glow.png"));
            _panel = Keep(BundleLoader.LoadAsset<Texture2D>(_bundle, root + "hud/panel.png"));
            _chip = Keep(BundleLoader.LoadAsset<Texture2D>(_bundle, root + "hud/chip.png"));
            if (_idle == null) throw new InvalidOperationException("idle clip missing in bundle");
            _loaded = true;
            return true;
        }
        catch (Exception e)
        {
            _failed = true;
            Plugin.Log.LogError("Powertrain bundle failed: " + e);
            return false;
        }
    }

    private static T Keep<T>(T o) where T : Object
    {
        if (o != null) o.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        return o;
    }
}
