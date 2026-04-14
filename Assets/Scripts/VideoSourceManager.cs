using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

public enum VideoSourceType { None, VideoFile, Webcam }

[System.Serializable]
public struct VideoSourceInfo
{
    public VideoSourceType type;
    public string name;
    public string identifier; // device name for webcam, path for file
}

/// <summary>
/// Manages switching between video file playback and webcam/capture device input.
/// Provides a unified RenderTexture output to YellowFishTracker.
/// </summary>
[DefaultExecutionOrder(-100)]
public class VideoSourceManager : MonoBehaviour
{
    [Header("References")]
    public YellowFishTracker tracker;
    public VideoFileInput videoFileInput;
    public RawImage debugMaskImage;
    public RawImage videoImage;

    [Header("Resolution")]
    [Range(1, 4)]
    public int downsampleFactor = 1;

    // Current state
    public VideoSourceType ActiveType => _activeType;
    public string ActiveSourceName => _activeSourceName;

    private VideoSourceType _activeType = VideoSourceType.None;
    private string _activeSourceName = "";
    private string _activeIdentifier = "";

    // Webcam
    private WebCamTexture _webcamTexture;
    private RenderTexture _webcamRT;
    private bool _webcamInitializing;
    private int _activeDownsampleFactor = 1;

    // Webcam resolution/FPS settings
    private int _requestedWidth;
    private int _requestedHeight;
    private int _requestedFPS;

    /// <summary>Requested webcam resolution width (0 = device default).</summary>
    public int RequestedWidth => _requestedWidth;
    /// <summary>Requested webcam resolution height (0 = device default).</summary>
    public int RequestedHeight => _requestedHeight;
    /// <summary>Requested webcam FPS (0 = device default).</summary>
    public int RequestedFPS => _requestedFPS;

    /// <summary>Native resolution of the current source (before downsampling).</summary>
    public Vector2Int NativeResolution
    {
        get
        {
            if (_activeType == VideoSourceType.Webcam && _webcamTexture != null && _webcamTexture.width > 16)
                return new Vector2Int(_webcamTexture.width, _webcamTexture.height);
            if (_activeType == VideoSourceType.VideoFile && videoFileInput != null)
            {
                var vp = videoFileInput.GetComponent<UnityEngine.Video.VideoPlayer>();
                if (vp != null && vp.isPrepared)
                    return new Vector2Int((int)vp.width, (int)vp.height);
            }
            return Vector2Int.zero;
        }
    }

    /// <summary>Effective resolution after downsampling (what the tracker sees).</summary>
    public Vector2Int EffectiveResolution
    {
        get
        {
            if (tracker != null && tracker.videoTexture != null)
                return new Vector2Int(tracker.videoTexture.width, tracker.videoTexture.height);
            return Vector2Int.zero;
        }
    }

    // Config persistence
    private string ConfigPath => Path.Combine(Application.streamingAssetsPath, "VideoSourceConfig.json");

    [System.Serializable]
    private class SourceConfig
    {
        public string type = "none";
        public string identifier = "";
    }

    private void Start()
    {
        LoadAndRestoreSource();
    }

    private void Update()
    {
        // Wait for webcam to finish initializing
        if (_webcamInitializing && _webcamTexture != null && _webcamTexture.width > 16)
        {
            _webcamInitializing = false;
            CreateWebcamRT();
        }

        // Check if downsample factor changed for active webcam
        if (_activeType == VideoSourceType.Webcam && !_webcamInitializing
            && _webcamTexture != null && _webcamTexture.isPlaying
            && downsampleFactor != _activeDownsampleFactor)
        {
            CreateWebcamRT();
        }

        // Blit webcam to RT each frame
        if (_activeType == VideoSourceType.Webcam
            && _webcamTexture != null && _webcamTexture.isPlaying
            && _webcamRT != null && _webcamTexture.didUpdateThisFrame)
        {
            Graphics.Blit(_webcamTexture, _webcamRT);
        }
    }

    // ── Public API ──────────────────────────────────────────────────────────

    public List<VideoSourceInfo> GetAvailableSources()
    {
        var sources = new List<VideoSourceInfo>();

        // Current source at top if active
        if (_activeType == VideoSourceType.VideoFile && !string.IsNullOrEmpty(_activeSourceName))
        {
            sources.Add(new VideoSourceInfo
            {
                type = VideoSourceType.VideoFile,
                name = _activeSourceName,
                identifier = _activeIdentifier
            });
        }
        else if (_activeType == VideoSourceType.Webcam && !string.IsNullOrEmpty(_activeSourceName))
        {
            // Active webcam will appear in the device list below
        }

        // Webcam / capture devices
        var devices = WebCamTexture.devices;
        for (int i = 0; i < devices.Length; i++)
        {
            sources.Add(new VideoSourceInfo
            {
                type = VideoSourceType.Webcam,
                name = devices[i].name,
                identifier = devices[i].name
            });
        }

        // Browse file option always last
        sources.Add(new VideoSourceInfo
        {
            type = VideoSourceType.VideoFile,
            name = "Browse File...",
            identifier = ""
        });

        return sources;
    }

    public void SwitchToWebcam(string deviceName)
    {
        SwitchToWebcam(deviceName, _requestedWidth, _requestedHeight, _requestedFPS);
    }

    public void SwitchToWebcam(string deviceName, int width, int height, int fps)
    {
        StopCurrentSource();

        _activeType = VideoSourceType.Webcam;
        _activeSourceName = deviceName;
        _activeIdentifier = deviceName;
        _requestedWidth = width;
        _requestedHeight = height;
        _requestedFPS = fps;

        if (width > 0 && height > 0 && fps > 0)
            _webcamTexture = new WebCamTexture(deviceName, width, height, fps);
        else if (width > 0 && height > 0)
            _webcamTexture = new WebCamTexture(deviceName, width, height);
        else
            _webcamTexture = new WebCamTexture(deviceName);

        _webcamTexture.Play();
        _webcamInitializing = true;

        Debug.Log($"[VideoSourceManager] Switching to webcam: {deviceName}" +
                  (width > 0 ? $" @ {width}x{height}" : "") +
                  (fps > 0 ? $" {fps}fps" : ""));
        SaveConfig();
    }

    /// <summary>Change resolution/FPS on the active webcam (restarts it).</summary>
    public void SetWebcamMode(int width, int height, int fps)
    {
        if (_activeType != VideoSourceType.Webcam || string.IsNullOrEmpty(_activeIdentifier)) return;
        SwitchToWebcam(_activeIdentifier, width, height, fps);
    }

    /// <summary>Get available resolutions for a named webcam device. Falls back to common presets if the device doesn't report modes.</summary>
    public Resolution[] GetAvailableResolutions(string deviceName)
    {
        var devices = WebCamTexture.devices;
        for (int i = 0; i < devices.Length; i++)
        {
            if (devices[i].name == deviceName && devices[i].availableResolutions != null
                && devices[i].availableResolutions.Length > 0)
            {
                return devices[i].availableResolutions;
            }
        }
        return null; // caller should use presets
    }

    // Common resolution presets when device doesn't report available modes
    public static readonly Vector3Int[] FallbackModes = new Vector3Int[]
    {
        new Vector3Int(640, 360, 30),
        new Vector3Int(640, 480, 30),
        new Vector3Int(1280, 720, 30),
        new Vector3Int(1280, 720, 60),
        new Vector3Int(1920, 1080, 30),
        new Vector3Int(1920, 1080, 60),
        new Vector3Int(3840, 2160, 30),
    };

    public void SwitchToFile(string path)
    {
        if (string.IsNullOrEmpty(path)) return;

        StopCurrentSource();

        _activeType = VideoSourceType.VideoFile;
        _activeSourceName = Path.GetFileName(path);
        _activeIdentifier = path;

        videoFileInput.LoadFile(path);

        Debug.Log($"[VideoSourceManager] Switching to file: {_activeSourceName}");
        SaveConfig();
    }

    public void BrowseForFile()
    {
        string path = MacFileDialog.OpenVideoFileDialog();
        if (!string.IsNullOrEmpty(path))
            SwitchToFile(path);
    }

    // ── Internals ───────────────────────────────────────────────────────────

    private void StopCurrentSource()
    {
        if (_activeType == VideoSourceType.Webcam)
        {
            if (_webcamTexture != null)
            {
                _webcamTexture.Stop();
                Destroy(_webcamTexture);
                _webcamTexture = null;
            }
            if (_webcamRT != null)
            {
                _webcamRT.Release();
                _webcamRT = null;
            }
            _webcamInitializing = false;
        }
        else if (_activeType == VideoSourceType.VideoFile)
        {
            if (videoFileInput != null)
                videoFileInput.StopAndRelease();
        }

        _activeType = VideoSourceType.None;
        _activeSourceName = "";
        _activeIdentifier = "";
    }

    private void CreateWebcamRT()
    {
        int factor = Mathf.Clamp(downsampleFactor, 1, 4);
        _activeDownsampleFactor = factor;
        int w = _webcamTexture.width / factor;
        int h = _webcamTexture.height / factor;

        if (_webcamRT != null) _webcamRT.Release();

        _webcamRT = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32)
        {
            enableRandomWrite = true,
            name = "WebcamRT"
        };
        _webcamRT.Create();

        Debug.Log($"[VideoSourceManager] Webcam ready: {_webcamTexture.width}x{_webcamTexture.height}" +
                  $" → RT {w}x{h}");

        // Wire to tracker
        if (tracker != null)
        {
            tracker.videoTexture = _webcamRT;
            tracker.InitCompute();

            if (debugMaskImage != null)
                debugMaskImage.texture = tracker.debugMaskTexture;
        }

        if (videoImage != null)
            videoImage.texture = _webcamRT;
    }

    // ── Persistence ─────────────────────────────────────────────────────────

    private void SaveConfig()
    {
        var cfg = new SourceConfig();
        switch (_activeType)
        {
            case VideoSourceType.Webcam:
                cfg.type = "webcam";
                cfg.identifier = _activeIdentifier;
                break;
            case VideoSourceType.VideoFile:
                cfg.type = "file";
                cfg.identifier = _activeIdentifier;
                break;
            default:
                cfg.type = "none";
                break;
        }

        try
        {
            File.WriteAllText(ConfigPath, JsonUtility.ToJson(cfg, true));
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[VideoSourceManager] Failed to save config: {e.Message}");
        }
    }

    private void LoadAndRestoreSource()
    {
        if (!File.Exists(ConfigPath)) return;

        try
        {
            var cfg = JsonUtility.FromJson<SourceConfig>(File.ReadAllText(ConfigPath));
            if (cfg == null) return;

            if (cfg.type == "webcam" && !string.IsNullOrEmpty(cfg.identifier))
            {
                // Verify device still exists
                var devices = WebCamTexture.devices;
                for (int i = 0; i < devices.Length; i++)
                {
                    if (devices[i].name == cfg.identifier)
                    {
                        SwitchToWebcam(cfg.identifier);
                        return;
                    }
                }
                Debug.LogWarning($"[VideoSourceManager] Saved webcam '{cfg.identifier}' not found.");
            }
            else if (cfg.type == "file" && !string.IsNullOrEmpty(cfg.identifier))
            {
                if (File.Exists(cfg.identifier))
                {
                    SwitchToFile(cfg.identifier);
                }
                else
                {
                    Debug.LogWarning($"[VideoSourceManager] Saved file '{cfg.identifier}' not found.");
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[VideoSourceManager] Failed to load config: {e.Message}");
        }
    }

    private void OnDestroy()
    {
        if (_webcamTexture != null)
        {
            _webcamTexture.Stop();
            Destroy(_webcamTexture);
        }
        if (_webcamRT != null)
            _webcamRT.Release();
    }
}
