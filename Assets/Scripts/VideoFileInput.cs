using UnityEngine;
using UnityEngine.Video;
using UnityEngine.UI;

/// <summary>
/// Plays a video file into a RenderTexture for development/testing.
/// Assign the YellowFishTracker and optional debug RawImage —
/// both are wired up automatically at runtime when the video prepares.
///
/// Place your video file in Assets/StreamingAssets/ and enter the filename
/// in videoFileName, or set a full absolute path in absolutePath.
/// </summary>
[RequireComponent(typeof(VideoPlayer))]
public class VideoFileInput : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Video Source")]
    [Tooltip("Filename inside Assets/StreamingAssets/ (e.g. 'fishtank.mov'). " +
             "Ignored if absolutePath is set.")]
    public string videoFileName = "fishtank.mov";

    [Tooltip("Full absolute path override. Leave empty to use StreamingAssets.")]
    public string absolutePath = "";

    [Header("Playback")]
    public bool loop        = true;
    public bool playOnStart = true;
    [Range(0.1f, 4f)]
    public float playbackSpeed = 1f;

    [Header("Resolution")]
    [Tooltip("Divide native resolution by this factor to reduce GPU load. 1 = native, 2 = half, 4 = quarter.")]
    [Range(1, 4)]
    public int downsampleFactor = 1;

    [Header("Output")]
    [Tooltip("Assign the YellowFishTracker here — videoTexture will be set automatically at runtime.")]
    public YellowFishTracker tracker;

    [Tooltip("Optional RawImage to display the hue mask for HSV tuning. Assigned automatically at runtime.")]
    public RawImage debugMaskImage;

    [Tooltip("Optional RawImage to display the raw video feed. Assigned automatically at runtime.")]
    public RawImage videoImage;

    [Tooltip("If true, also display the video on a Renderer on this GameObject.")]
    public bool displayOnRenderer = false;

    // ── Internal ──────────────────────────────────────────────────────────────

    private VideoPlayer _vp;
    private RenderTexture _outputTexture;
    private int _activeDownsampleFactor = 1;

    // ─────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        _vp = GetComponent<VideoPlayer>();
        _vp.renderMode        = VideoRenderMode.RenderTexture;
        _vp.audioOutputMode   = VideoAudioOutputMode.None;
        _vp.isLooping         = loop;
        _vp.playbackSpeed     = playbackSpeed;
        _vp.prepareCompleted += OnPrepareCompleted;
        _vp.errorReceived    += (vp, msg) => Debug.LogError($"[VideoFileInput] {msg}");
    }

    private void Start()
    {
        string path = string.IsNullOrEmpty(absolutePath)
            ? System.IO.Path.Combine(Application.streamingAssetsPath, videoFileName)
            : absolutePath;

        _vp.url = path;
        _vp.Prepare();
    }

    private void OnPrepareCompleted(VideoPlayer vp)
    {
        ApplyDownsample();

        if (playOnStart) vp.Play();
    }

    private void ApplyDownsample()
    {
        if (!_vp.isPrepared) return;

        int factor = Mathf.Clamp(downsampleFactor, 1, 4);
        int w = (int)_vp.width  / factor;
        int h = (int)_vp.height / factor;
        _activeDownsampleFactor = factor;

        if (_outputTexture == null || _outputTexture.width != w || _outputTexture.height != h)
        {
            if (_outputTexture != null) _outputTexture.Release();

            _outputTexture = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32)
            {
                enableRandomWrite = true,
                name              = "FishVideoRT"
            };
            _outputTexture.Create();

            Debug.Log($"[VideoFileInput] Created RenderTexture {w}x{h}" +
                      (factor > 1 ? $" (native {_vp.width}x{_vp.height}, downsample {factor}x)" : "") +
                      ".");
        }

        _vp.targetTexture = _outputTexture;

        if (tracker != null)
        {
            tracker.videoTexture = _outputTexture;
            tracker.InitCompute();

            if (debugMaskImage != null)
                debugMaskImage.texture = tracker.debugMaskTexture;
        }
        else
        {
            Debug.LogWarning("[VideoFileInput] No tracker assigned — set videoTexture manually.");
        }

        if (videoImage != null)
            videoImage.texture = _outputTexture;

        if (displayOnRenderer)
        {
            var rend = GetComponent<Renderer>();
            if (rend) rend.material.mainTexture = _outputTexture;
        }
    }

    private void Update()
    {
        _vp.playbackSpeed = playbackSpeed;

        int factor = Mathf.Clamp(downsampleFactor, 1, 4);
        if (factor != _activeDownsampleFactor)
            ApplyDownsample();
    }

    // ── Public controls ───────────────────────────────────────────────────────

    public void Play()  => _vp.Play();
    public void Pause() => _vp.Pause();
    public void Stop()  => _vp.Stop();

    /// <summary>Seek to a normalized position (0–1).</summary>
    public void SeekNormalized(float t)
    {
        if (_vp.isPrepared)
            _vp.time = t * _vp.length;
    }

    /// <summary>Load and play a new video file at runtime.</summary>
    public void LoadFile(string path)
    {
        _vp.Stop();
        absolutePath = path;
        videoFileName = System.IO.Path.GetFileName(path);
        _vp.url = path;
        _vp.Prepare();
    }

    /// <summary>Stop playback and release the output texture.</summary>
    public void StopAndRelease()
    {
        _vp.Stop();
        _vp.targetTexture = null;
        if (_outputTexture != null)
        {
            _outputTexture.Release();
            _outputTexture = null;
        }
    }

    private void OnDestroy()
    {
        if (_vp != null) _vp.prepareCompleted -= OnPrepareCompleted;
        if (_outputTexture != null) _outputTexture.Release();
    }
}
