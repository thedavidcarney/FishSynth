using UnityEngine;
using UnityEngine.Video;

/// <summary>
/// Plays a video file into a RenderTexture for development/testing.
/// Assign the outputTexture to YellowFishTracker.videoTexture.
///
/// Place your video file in Assets/StreamingAssets/ and enter the filename
/// in videoFileName, or set a full absolute path in absolutePath.
///
/// The RenderTexture is auto-created to match the video resolution on load.
/// </summary>
[RequireComponent(typeof(VideoPlayer))]
public class VideoFileInput : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Video Source")]
    [Tooltip("Filename inside Assets/StreamingAssets/  (e.g. 'fishtank.mp4'). " +
             "Ignored if absolutePath is set.")]
    public string videoFileName = "fishtank.mp4";

    [Tooltip("Full absolute path override. Leave empty to use StreamingAssets.")]
    public string absolutePath = "";

    [Header("Playback")]
    public bool loop        = true;
    public bool playOnStart = true;
    [Range(0.1f, 4f)]
    public float playbackSpeed = 1f;

    [Header("Output")]
    [Tooltip("The RenderTexture the video is rendered into. " +
             "Assign this to YellowFishTracker.videoTexture. " +
             "Created automatically at video resolution if left null.")]
    public RenderTexture outputTexture;

    [Tooltip("If true, also display the video on a RawImage or Renderer on this GameObject.")]
    public bool displayOnRenderer = false;

    // ── Internal ──────────────────────────────────────────────────────────────

    private VideoPlayer _vp;

    // ─────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        _vp = GetComponent<VideoPlayer>();
        _vp.renderMode        = VideoRenderMode.RenderTexture;
        _vp.audioOutputMode   = VideoAudioOutputMode.None; // we don't need audio
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

        if (outputTexture == null || outputTexture.width != (int)w || outputTexture.height != (int)h)
        {
            if (outputTexture != null) outputTexture.Release();

            outputTexture = new RenderTexture((int)w, (int)h, 0, RenderTextureFormat.ARGB32)
            {
                enableRandomWrite = true,
                name              = "FishVideoRT"
            };
            outputTexture.Create();

            Debug.Log($"[VideoFileInput] Created RenderTexture {w}x{h} for video.");
        }

        vp.targetTexture = outputTexture;

        if (displayOnRenderer)
        {
            var rend = GetComponent<Renderer>();
            if (rend) rend.material.mainTexture = outputTexture;
        }

        if (playOnStart) vp.Play();
    }

    private void Update()
    {
        _vp.playbackSpeed = playbackSpeed; // allow runtime tweaking
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
    }
}
