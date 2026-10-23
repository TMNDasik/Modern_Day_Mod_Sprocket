using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace SoundOverhaul;





internal static class Library
{
    internal sealed class Bank
    {
        public string Key;
        public float Gain = 1f;
        public readonly List<string> Files = new();
        public readonly List<AudioClip> Clips = new();
        public int Last = -1;
        public string Group;          
    }

    
    private static readonly System.Text.RegularExpressions.Regex LazyKey = new(@"^(eng\.[^.]+)\.\d\d\.", System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly Dictionary<string, List<string>> GroupFiles = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> LazyFiles = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Requested = new(StringComparer.OrdinalIgnoreCase);
    private static int _lazyMade;

    private static readonly Dictionary<string, Bank> Banks = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, List<Bank>> ByFile = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentQueue<Wav.Data> Parsed = new();
    private static readonly System.Random Rng = new();
    private static int _total, _made, _failed;
    private static bool _reported;
    private static float _bytes;

    public static bool Ready => _total > 0 && _made + _failed >= _total;

    public static void StartLoading()
    {
        try
        {
            var manifest = Path.Combine(Plugin.ModFolder(Plugin.PluginDir, "SoundOverhaul"), "banks.txt");
            if (!File.Exists(manifest))
            {
                Plugin.Log.LogWarning("banks.txt not found next to the mod — sounds are not replaced");
                return;
            }
            var root = Plugin.Root;
            var files = new List<string>();
            var missing = 0;
            foreach (var raw in File.ReadAllLines(manifest))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var parts = line.Split('|');
                if (parts.Length < 3) continue;
                var key = parts[0].Trim();
                var lazy = LazyKey.Match(key);
                var bank = new Bank { Key = key, Group = lazy.Success ? lazy.Groups[1].Value : null };
                if (float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var g)) bank.Gain = Mathf.Clamp(g, 0f, 4f);
                foreach (var pat in parts[2].Split(';'))
                {
                    var p = pat.Trim().Replace('\\', '/');
                    if (p.Length == 0) continue;
                    var slash = p.LastIndexOf('/');
                    var dir = Path.Combine(root, slash > 0 ? p.Substring(0, slash) : "");
                    var name = slash >= 0 ? p.Substring(slash + 1) : p;
                    if (!Directory.Exists(dir)) { missing++; continue; }
                    var found = Directory.GetFiles(dir, name);
                    Array.Sort(found, StringComparer.OrdinalIgnoreCase);
                    if (found.Length == 0) missing++;
                    foreach (var f in found)
                    {
                        bank.Files.Add(f);
                        if (!ByFile.TryGetValue(f, out var list))
                        {
                            ByFile[f] = list = new List<Bank>();
                            files.Add(f);
                        }
                        list.Add(bank);
                    }
                }
                Banks[key] = bank;
            }
            
            var eager = new List<string>();
            foreach (var f in files)
            {
                var banks = ByFile[f];
                var group = banks[0].Group;
                foreach (var b in banks) if (b.Group != group) group = null;
                if (group == null) { eager.Add(f); continue; }
                LazyFiles.Add(f);
                if (!GroupFiles.TryGetValue(group, out var gl)) GroupFiles[group] = gl = new List<string>();
                gl.Add(f);
            }
            _total = eager.Count;
            Plugin.Log.LogInfo($"Sound banks: {Banks.Count}, files: {files.Count} ({eager.Count} at start, {GroupFiles.Count} engine sets on demand)" +
                               $"{(missing > 0 ? $", {missing} pattern(s) found nothing" : "")} (folder {root})");
            if (eager.Count == 0) return;
            Task.Run(() => ReadAll(eager));
        }
        catch (Exception e) { Plugin.Log.LogError("Sound library failed: " + e); }
    }

    private static void ReadAll(List<string> files)
    {
        foreach (var f in files)
        {
            try { Parsed.Enqueue(Read(f)); }
            catch (Exception e)
            {
                System.Threading.Interlocked.Increment(ref _failed);
                Plugin.Log.LogWarning($"Sound '{Path.GetFileName(f)}' can't be read: {e.Message}");
            }
        }
    }

    
    
    private static Wav.Data Read(string file)
    {
        var d = Wav.Read(file);
        if (!ByFile.TryGetValue(file, out var banks) || banks.Count == 0) return d;
        bool vehicle = true, spatial = true;
        foreach (var b in banks)
        {
            var k = b.Key;
            var v = k.StartsWith("eng.", StringComparison.OrdinalIgnoreCase) || k.StartsWith("trk.", StringComparison.OrdinalIgnoreCase) ||
                    k.StartsWith("veh.", StringComparison.OrdinalIgnoreCase) || k.StartsWith("tur.", StringComparison.OrdinalIgnoreCase) ||
                    k.StartsWith("drv.", StringComparison.OrdinalIgnoreCase);
            vehicle &= v;
            
            var impact = Sfx.IsImpact(k) && k.IndexOf(".int", StringComparison.OrdinalIgnoreCase) < 0 &&
                         !k.StartsWith("hit.pen.", StringComparison.OrdinalIgnoreCase);
            
            spatial &= impact || (v && !k.EndsWith(".int", StringComparison.OrdinalIgnoreCase) && !k.StartsWith("veh.int.", StringComparison.OrdinalIgnoreCase) &&
                                  !k.Equals("eng.start", StringComparison.OrdinalIgnoreCase) && !k.Equals("eng.stop", StringComparison.OrdinalIgnoreCase));
        }
        if (spatial) Wav.ToMono(d);
        if (vehicle && d.Frames > d.Rate * 12) Wav.TrimLoop(d, 10f, 0.5f);
        return d;
    }

    
    private static void Request(string group)
    {
        if (!Requested.Add(group) || !GroupFiles.TryGetValue(group, out var files)) return;
        if (Plugin.DebugLogging.Value) Plugin.Log.LogInfo($"Loading sound set {group} ({files.Count} files)");
        var copy = new List<string>(files);
        Task.Run(() => ReadAll(copy));
    }

    
    public static void Pump()
    {
        if (Parsed.IsEmpty)
        {
            if (Ready && !_reported)
            {
                _reported = true;
                Plugin.Log.LogInfo($"Sounds loaded: {_made} clips ({_bytes / 1048576f:0} MB){(_failed > 0 ? $", {_failed} failed" : "")}");
            }
            return;
        }
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 6 && Parsed.TryDequeue(out var d))
        {
            try
            {
                var clip = NativeClip.Create(d.Name, d.Samples, d.Frames, d.Channels, d.Rate);
                if (_made == 0)
                    Plugin.Log.LogInfo($"First sound check: '{d.Name}' {clip.length:0.00} s, {clip.channels} ch, {clip.frequency} Hz " +
                                       $"(file {d.Frames / (float)d.Rate:0.00} s, {d.Channels} ch, {d.Rate} Hz)");
                _bytes += d.Samples.Length * 4f;
                if (ByFile.TryGetValue(d.Path, out var banks))
                    foreach (var b in banks) b.Clips.Add(clip);
                if (LazyFiles.Contains(d.Path)) _lazyMade++;
                else _made++;
            }
            catch (Exception e)
            {
                _failed++;
                Plugin.Log.LogWarning($"Sound '{d.Name}' failed: {e.Message}");
            }
        }
    }

    public static bool Has(string key)
    {
        if (key == null || !Banks.TryGetValue(key, out var b)) return false;
        if (b.Clips.Count > 0) return true;
        if (b.Group != null) Request(b.Group);
        return false;
    }

    public static float Gain(string key) => key != null && Banks.TryGetValue(key, out var b) ? b.Gain : 1f;

    
    public static AudioClip Pick(string key)
    {
        if (key == null || !Banks.TryGetValue(key, out var b) || b.Clips.Count == 0) return null;
        var n = b.Clips.Count;
        var i = n == 1 ? 0 : Rng.Next(n);
        if (n > 1 && i == b.Last) i = (i + 1 + Rng.Next(n - 1)) % n;
        b.Last = i;
        var clip = b.Clips[i];
        return clip != null && !clip.WasCollected ? clip : null;
    }

    
    public static float Length(string key)
    {
        if (key == null || !Banks.TryGetValue(key, out var b) || b.Clips.Count == 0) return 0f;
        var c = b.Clips[0];
        return c != null && !c.WasCollected ? c.length : 0f;
    }
}
