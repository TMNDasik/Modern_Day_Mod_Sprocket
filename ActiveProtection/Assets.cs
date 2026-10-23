using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Audio;
using Object = UnityEngine.Object;

namespace ActiveProtection;



internal static class Assets
{
    private const string Root = "assets/activeprotection/";

    private static AssetBundle _bundle;
    private static bool _loadFailed;
    private static Material _zone, _zoneHidden, _line, _flash;
    private static Texture2D _detectorIcon, _launcherIcon, _warnerIcon, _smokeIcon, _jammerIcon, _radioJammerIcon, _arrow;
    private static AudioClip _burst, _launch, _warning, _grenade, _grenadeBurst;
    private static AudioMixerGroup _mixer;
    private static bool _mixerSearched;

    public static Material ZoneMat => Ensure() ? Alive(_zone) : null;
    public static Material ZoneHiddenMat => Ensure() ? Alive(_zoneHidden) : null;
    public static Material LineMat => Ensure() ? Alive(_line) : null;
    public static Material FlashMat => Ensure() ? Alive(_flash) : null;
    public static Texture2D DetectorIcon => Ensure() ? Alive(_detectorIcon) : null;
    public static Texture2D LauncherIcon => Ensure() ? Alive(_launcherIcon) : null;
    public static AudioClip BurstClip => Ensure() ? Alive(_burst) : null;
    public static AudioClip LaunchClip => Ensure() ? Alive(_launch) : null;
    public static AudioClip WarningClip => Ensure() ? Alive(_warning) : null;
    public static AudioClip GrenadeClip => Ensure() ? Alive(_grenade) : null;
    public static AudioClip GrenadeBurstClip => Ensure() ? Alive(_grenadeBurst) : null;
    public static Texture2D Arrow => Ensure() ? Alive(_arrow) : null;

    public static Texture2D Icon(ModuleKind kind)
    {
        if (!Ensure()) return null;
        return kind switch
        {
            ModuleKind.Detector => Alive(_detectorIcon),
            ModuleKind.RadioJammer => Alive(_radioJammerIcon) ?? Alive(_detectorIcon), 
            ModuleKind.Launcher => Alive(_launcherIcon),
            ModuleKind.Warner => Alive(_warnerIcon),
            ModuleKind.Smoke => Alive(_smokeIcon),
            _ => Alive(_jammerIcon)
        };
    }

    private static T Alive<T>(T o) where T : Object => o != null && !o.WasCollected ? o : null;

    private static bool Ensure()
    {
        if (_bundle != null && Alive(_zone) != null) return true;
        if (_loadFailed) return false;
        try
        {
            if (_bundle == null)
            {
                string path = null;
                foreach (var p in new[]
                         {
                             Path.Combine(Plugin.PluginDir, "activeprotection.bundle"),
                             Path.Combine(Plugin.PluginDir, "MDActiveProtection", "activeprotection.bundle"),
                             Path.Combine(Plugin.PluginDir, "ActiveProtection", "activeprotection.bundle")
                         })
                    if (File.Exists(p)) { path = p; break; }
                if (path == null)
                {
                    _loadFailed = true;
                    Plugin.Log.LogWarning("activeprotection.bundle not found — APS zones, icons and sounds disabled");
                    return false;
                }
                _bundle = BundleLoader.LoadFromFile(path);
                if (_bundle == null) throw new InvalidOperationException("bundle load returned null");
            }
            _zone = Keep(BundleLoader.LoadAsset<Material>(_bundle, Root + "zonemat.mat"));
            _zoneHidden = Keep(BundleLoader.LoadAsset<Material>(_bundle, Root + "zonehiddenmat.mat"));
            _line = Keep(BundleLoader.LoadAsset<Material>(_bundle, Root + "linemat.mat"));
            _flash = Keep(BundleLoader.LoadAsset<Material>(_bundle, Root + "flashmat.mat"));
            _detectorIcon = Keep(BundleLoader.LoadAsset<Texture2D>(_bundle, Root + "textures/detectoricon.png"));
            _launcherIcon = Keep(BundleLoader.LoadAsset<Texture2D>(_bundle, Root + "textures/launchericon.png"));
            _burst = Keep(BundleLoader.LoadAsset<AudioClip>(_bundle, Root + "audio/aps_burst.wav"));
            _launch = Keep(BundleLoader.LoadAsset<AudioClip>(_bundle, Root + "audio/aps_launch.wav"));
            _warnerIcon = Keep(BundleLoader.LoadAsset<Texture2D>(_bundle, Root + "textures/warnericon.png"));
            _smokeIcon = Keep(BundleLoader.LoadAsset<Texture2D>(_bundle, Root + "textures/smokeicon.png"));
            _jammerIcon = Keep(BundleLoader.LoadAsset<Texture2D>(_bundle, Root + "textures/jammericon.png"));
            try { _radioJammerIcon = Keep(BundleLoader.LoadAsset<Texture2D>(_bundle, Root + "textures/radiojammericon.png")); }
            catch { _radioJammerIcon = null; }
            _arrow = Keep(BundleLoader.LoadAsset<Texture2D>(_bundle, Root + "textures/warningarrow.png"));
            _warning = Keep(BundleLoader.LoadAsset<AudioClip>(_bundle, Root + "audio/eo_warning.wav"));
            _grenade = Keep(BundleLoader.LoadAsset<AudioClip>(_bundle, Root + "audio/eo_grenade.wav"));
            _grenadeBurst = Keep(BundleLoader.LoadAsset<AudioClip>(_bundle, Root + "audio/eo_grenade_burst.wav"));
            if (_zone == null) throw new InvalidOperationException("zone material missing in bundle");
            Plugin.Log.LogInfo($"APS assets loaded (zone shader supported: {_zone.shader != null && _zone.shader.isSupported}, " +
                               $"lines: {_line != null}, icons: {_detectorIcon != null && _launcherIcon != null}, sound: {_burst != null})");
            return true;
        }
        catch (Exception e)
        {
            _loadFailed = true;
            Plugin.Log.LogError("APS bundle failed: " + e);
            return false;
        }
    }

    private static T Keep<T>(T o) where T : Object
    {
        if (o != null) o.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        return o;
    }

    

    
    public static void Play(AudioClip clip, Vector3 position, float volume, float minDistance, float pitch = 1f, bool spatial = true)
    {
        try
        {
            volume *= Mathf.Clamp(Plugin.SoundVolume.Value, 0f, 2f);
            if (clip == null || volume <= 0f) return;
            var go = new GameObject("APS sound");
            go.transform.position = position;
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.spatialBlend = spatial ? 1f : 0f;
            src.rolloffMode = AudioRolloffMode.Logarithmic;
            src.minDistance = minDistance;
            src.maxDistance = 4000f;
            src.dopplerLevel = 0f;
            src.priority = 24;
            src.volume = Mathf.Min(1f, volume);
            src.pitch = pitch;
            var mixer = Mixer();
            if (mixer != null) src.outputAudioMixerGroup = mixer;
            if(src.isActiveAndEnabled)src.Play();
            Object.Destroy(go, clip.length / Mathf.Max(0.1f, pitch) + 0.5f);
        }
        catch (Exception e)
        {
            if (Plugin.DebugLogging.Value) Plugin.Log.LogWarning("APS sound failed: " + e.Message);
        }
    }

    
    private static AudioMixerGroup Mixer()
    {
        if (_mixer != null && !_mixer.WasCollected) return _mixer;
        if (_mixerSearched) return null;
        _mixerSearched = true;
        try
        {
            var counts = new Dictionary<IntPtr, (AudioMixerGroup Group, int Count)>();
            foreach (var s in Resources.FindObjectsOfTypeAll<AudioSource>())
            {
                var g = s != null ? s.outputAudioMixerGroup : null;
                if (g == null) continue;
                var n = (g.name ?? "").ToLowerInvariant();
                if (n.Contains("music") || n.Contains("ui") || n.Contains("voice") || n.Contains("ambien")) continue;
                if (n.Contains("sfx") || n.Contains("effect") || n.Contains("weapon") || n.Contains("combat") || n.Contains("explos"))
                {
                    _mixer = g;
                    break;
                }
                counts[g.Pointer] = counts.TryGetValue(g.Pointer, out var c) ? (g, c.Count + 1) : (g, 1);
            }
            if (_mixer == null)
            {
                var best = 0;
                foreach (var c in counts.Values)
                    if (c.Count > best) { best = c.Count; _mixer = c.Group; }
            }
            if (_mixer == null) _mixerSearched = false;
        }
        catch { }
        return _mixer;
    }
}
