using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>
/// Static utility for saving/loading named presets.
/// Tracking presets: StreamingAssets/Presets/Tracking/*.json
/// MIDI presets:     StreamingAssets/Presets/MIDI/*.json
/// Mask presets:     StreamingAssets/Presets/Masks/*.png
/// </summary>
public static class PresetManager
{
    static string TrackingDir => Path.Combine(Application.streamingAssetsPath, "Presets", "Tracking");
    static string MidiDir    => Path.Combine(Application.streamingAssetsPath, "Presets", "MIDI");
    static string MaskDir    => Path.Combine(Application.streamingAssetsPath, "Presets", "Masks");

    static void EnsureDir(string dir)
    {
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
    }

    // ── Tracking ────────────────────────────────────────────────────────────────

    public static void SaveTrackingPreset(YellowFishTracker tracker, string name)
    {
        EnsureDir(TrackingDir);
        var cfg = tracker.ToConfig();
        string json = JsonUtility.ToJson(cfg, true);
        File.WriteAllText(Path.Combine(TrackingDir, name + ".json"), json);
        Debug.Log($"[PresetManager] Saved tracking preset: {name}");
    }

    public static bool LoadTrackingPreset(YellowFishTracker tracker, string name)
    {
        string path = Path.Combine(TrackingDir, name + ".json");
        if (!File.Exists(path)) return false;
        var cfg = JsonUtility.FromJson<YellowFishTracker.TrackerConfig>(File.ReadAllText(path));
        if (cfg == null) return false;
        tracker.FromConfig(cfg);
        Debug.Log($"[PresetManager] Loaded tracking preset: {name}");
        return true;
    }

    public static string[] ListTrackingPresets()
    {
        if (!Directory.Exists(TrackingDir)) return new string[0];
        return Directory.GetFiles(TrackingDir, "*.json")
            .Select(p => Path.GetFileNameWithoutExtension(p))
            .OrderBy(n => n)
            .ToArray();
    }

    public static void DeleteTrackingPreset(string name)
    {
        string path = Path.Combine(TrackingDir, name + ".json");
        if (File.Exists(path)) File.Delete(path);
    }

    // ── MIDI ────────────────────────────────────────────────────────────────────

    public static void SaveMidiPreset(FishMidiOutput midi, string name)
    {
        EnsureDir(MidiDir);
        var cfg = MidiPresetConfig.From(midi);
        string json = JsonUtility.ToJson(cfg, true);
        File.WriteAllText(Path.Combine(MidiDir, name + ".json"), json);
        Debug.Log($"[PresetManager] Saved MIDI preset: {name}");
    }

    public static bool LoadMidiPreset(FishMidiOutput midi, string name)
    {
        string path = Path.Combine(MidiDir, name + ".json");
        if (!File.Exists(path)) return false;
        var cfg = JsonUtility.FromJson<MidiPresetConfig>(File.ReadAllText(path));
        if (cfg == null) return false;
        cfg.ApplyTo(midi);
        Debug.Log($"[PresetManager] Loaded MIDI preset: {name}");
        return true;
    }

    public static string[] ListMidiPresets()
    {
        if (!Directory.Exists(MidiDir)) return new string[0];
        return Directory.GetFiles(MidiDir, "*.json")
            .Select(p => Path.GetFileNameWithoutExtension(p))
            .OrderBy(n => n)
            .ToArray();
    }

    public static void DeleteMidiPreset(string name)
    {
        string path = Path.Combine(MidiDir, name + ".json");
        if (File.Exists(path)) File.Delete(path);
    }

    // ── Masks ───────────────────────────────────────────────────────────────────

    public static void SaveMaskPreset(Texture2D maskTex, string name)
    {
        EnsureDir(MaskDir);
        byte[] png = maskTex.EncodeToPNG();
        File.WriteAllBytes(Path.Combine(MaskDir, name + ".png"), png);
        Debug.Log($"[PresetManager] Saved mask preset: {name}");
    }

    public static Texture2D LoadMaskPreset(string name)
    {
        string path = Path.Combine(MaskDir, name + ".png");
        if (!File.Exists(path)) return null;
        byte[] data = File.ReadAllBytes(path);
        var tex = new Texture2D(2, 2);
        tex.LoadImage(data);
        Debug.Log($"[PresetManager] Loaded mask preset: {name}");
        return tex;
    }

    public static string[] ListMaskPresets()
    {
        if (!Directory.Exists(MaskDir)) return new string[0];
        return Directory.GetFiles(MaskDir, "*.png")
            .Select(p => Path.GetFileNameWithoutExtension(p))
            .OrderBy(n => n)
            .ToArray();
    }

    public static void DeleteMaskPreset(string name)
    {
        string path = Path.Combine(MaskDir, name + ".png");
        if (File.Exists(path)) File.Delete(path);
    }
}
