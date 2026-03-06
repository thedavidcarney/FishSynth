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
        uint w = vp.width;
        uint h = vp.height;

        if (_outputTexture == null || _outputTexture.width != (int)w || _outputTexture.height != (int)h)
        {
            if (_outputTexture != null) _outputTexture.Release();

            _outputTexture = new RenderTexture((int)w, (int)h, 0, RenderTextureFormat.ARGB32)
            {
                enableRandomWrite = true,
                name              = "FishVideoRT"
            };
            _outputTexture.Create();

            Debug.Log($"[VideoFileInput] Created RenderTexture {w}x{h}.");
        }

        vp.targetTexture = _outputTexture;

        if (tracker != null)
        {
            // Push video RT to tracker and force compute init so debugMaskTexture exists
            tracker.videoTexture = _outputTexture;
            tracker.InitCompute();

            // Now the mask RT exists — wire up the debug RawImage
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

        if (playOnStart) vp.Play();
    }

    private void Update()
    {
        _vp.playbackSpeed = playbackSpeed;
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

    private void OnDestroy()
    {
        if (_vp != null) _vp.prepareCompleted -= OnPrepareCompleted;
        if (_outputTexture != null) _outputTexture.Release();
    }
}
