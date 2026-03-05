using UnityEngine;
using UnityEngine.Video;

/// <summary>
/// Tracks the yellow fish using a GPU hue-mask compute shader.
/// Outputs normalized (0–1) position, velocity components, and blob size
/// each frame via the FishData property.
///
/// Attach to any GameObject. Assign ComputeShader, a RenderTexture source,
/// and tune HSV thresholds in the Inspector.
/// </summary>
public class YellowFishTracker : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("References")]
    [Tooltip("Assign FishHueMask.compute here.")]
    public ComputeShader fishComputeShader;

    [Tooltip("RenderTexture receiving the video feed. Assign from VideoFileInput or live camera.")]
    public RenderTexture videoTexture;

    [Tooltip("Optional: displays the raw hue mask for tuning. Can be assigned to a RawImage UI element.")]
    public RenderTexture debugMaskTexture;

    [Header("HSV Thresholds  (0–1 range; Hue 0=red, 0.167=yellow, 0.333=green, 0.667=blue)")]
    [Range(0f, 1f)] public float hueMin = 0.10f;   // ~36°
    [Range(0f, 1f)] public float hueMax = 0.20f;   // ~72°
    [Range(0f, 1f)] public float satMin = 0.50f;
    [Range(0f, 1f)] public float satMax = 1.00f;
    [Range(0f, 1f)] public float valMin = 0.40f;
    [Range(0f, 1f)] public float valMax = 1.00f;

    [Header("Tracking")]
    [Tooltip("Minimum pixel count to consider a valid detection. Filters out noise.")]
    public int minBlobPixels = 80;

    [Tooltip("Smoothing for velocity (EMA alpha, 0=frozen, 1=raw).")]
    [Range(0f, 1f)] public float velocitySmoothing = 0.2f;

    [Tooltip("Smoothing for position (EMA alpha).")]
    [Range(0f, 1f)] public float positionSmoothing = 0.5f;

    // ── Public output data ────────────────────────────────────────────────────

    /// <summary>All tracker outputs, normalized 0–1. Read by FishMidiOutput.</summary>
    public FishTrackData Data { get; private set; }

    // ── Internal ──────────────────────────────────────────────────────────────

    private int _kernelMask;
    private int _kernelMoments;
    private ComputeBuffer _partialBuffer;
    private float[] _partialData;
    private int _numGroupsX, _numGroupsY, _totalGroups;

    private RenderTexture _maskRT;
    private int _texWidth, _texHeight;

    private Vector2 _smoothPos;
    private Vector2 _smoothVel;
    private bool _hadDetection;

    // ─────────────────────────────────────────────────────────────────────────

    private void Start()
    {
        InitCompute();
    }

    private void InitCompute()
    {
        if (videoTexture == null)
        {
            Debug.LogWarning("[YellowFishTracker] No videoTexture assigned.");
            return;
        }

        _texWidth  = videoTexture.width;
        _texHeight = videoTexture.height;

        // Mask render texture (R8 would suffice but float4 keeps shader simple)
        _maskRT = new RenderTexture(_texWidth, _texHeight, 0, RenderTextureFormat.ARGB32)
        {
            enableRandomWrite = true
        };
        _maskRT.Create();

        if (debugMaskTexture == null)
            debugMaskTexture = _maskRT; // expose the same RT for debug viewing

        _numGroupsX = Mathf.CeilToInt(_texWidth  / 8f);
        _numGroupsY = Mathf.CeilToInt(_texHeight / 8f);
        _totalGroups = _numGroupsX * _numGroupsY;

        _partialBuffer = new ComputeBuffer(_totalGroups * 7, sizeof(float));
        _partialData   = new float[_totalGroups * 7];

        _kernelMask    = fishComputeShader.FindKernel("HueMask");
        _kernelMoments = fishComputeShader.FindKernel("ReduceMoments");

        // Static bindings
        fishComputeShader.SetInt("TextureWidth",  _texWidth);
        fishComputeShader.SetInt("TextureHeight", _texHeight);
        fishComputeShader.SetTexture(_kernelMask,    "MaskTexture",   _maskRT);
        fishComputeShader.SetTexture(_kernelMoments, "MaskTexture",   _maskRT);
        fishComputeShader.SetBuffer(_kernelMoments,  "PartialMoments", _partialBuffer);
    }

    private void Update()
    {
        if (videoTexture == null || fishComputeShader == null) return;

        // Re-init if texture size changed (e.g. video loaded after Start)
        if (videoTexture.width != _texWidth || videoTexture.height != _texHeight)
        {
            CleanupCompute();
            InitCompute();
        }

        RunCompute();
        ReadbackAndProcess();
    }

    private void RunCompute()
    {
        // Per-frame HSV params (Inspector-tunable at runtime)
        fishComputeShader.SetFloat("HueMin", hueMin);
        fishComputeShader.SetFloat("HueMax", hueMax);
        fishComputeShader.SetFloat("SatMin", satMin);
        fishComputeShader.SetFloat("SatMax", satMax);
        fishComputeShader.SetFloat("ValMin", valMin);
        fishComputeShader.SetFloat("ValMax", valMax);

        fishComputeShader.SetTexture(_kernelMask, "InputTexture", videoTexture);
        fishComputeShader.Dispatch(_kernelMask, _numGroupsX, _numGroupsY, 1);
        fishComputeShader.Dispatch(_kernelMoments, _numGroupsX, _numGroupsY, 1);
    }

    private void ReadbackAndProcess()
    {
        _partialBuffer.GetData(_partialData);

        // Reduce partial group results on CPU
        float totalCount = 0, sumX = 0, sumY = 0;
        float minX = _texWidth, minY = _texHeight, maxX = 0, maxY = 0;

        for (int i = 0; i < _totalGroups; i++)
        {
            int b = i * 7;
            float cnt = _partialData[b + 0];
            if (cnt < 0.5f) continue;

            totalCount += cnt;
            sumX += _partialData[b + 1];
            sumY += _partialData[b + 2];
            minX  = Mathf.Min(minX, _partialData[b + 3]);
            minY  = Mathf.Min(minY, _partialData[b + 4]);
            maxX  = Mathf.Max(maxX, _partialData[b + 5]);
            maxY  = Mathf.Max(maxY, _partialData[b + 6]);
        }

        float dt = Time.deltaTime > 0 ? Time.deltaTime : 0.016f;

        if (totalCount >= minBlobPixels)
        {
            // Normalized centroid (0–1, Y flipped so 0=bottom)
            float rawX = sumX / totalCount / _texWidth;
            float rawY = 1f - (sumY / totalCount / _texHeight);

            // Normalized bounding box width/height as size proxy
            float bboxW = (maxX - minX) / _texWidth;
            float bboxH = (maxY - minY) / _texHeight;
            float size  = Mathf.Sqrt(bboxW * bboxH); // geometric mean → 0–1

            Vector2 rawPos = new Vector2(rawX, rawY);

            if (!_hadDetection)
            {
                _smoothPos    = rawPos;
                _smoothVel    = Vector2.zero;
                _hadDetection = true;
            }

            // EMA position smoothing
            Vector2 prevPos = _smoothPos;
            _smoothPos = Vector2.Lerp(_smoothPos, rawPos, positionSmoothing);

            // Raw velocity in normalized units/sec, then EMA smooth
            Vector2 rawVel = (_smoothPos - prevPos) / dt;
            _smoothVel = Vector2.Lerp(_smoothVel, rawVel, velocitySmoothing);

            Data = new FishTrackData
            {
                detected         = true,
                posX             = _smoothPos.x,
                posY             = _smoothPos.y,
                velX             = _smoothVel.x,
                velY             = _smoothVel.y,
                velocityMagnitude = _smoothVel.magnitude,
                size             = size,
                blobPixelCount   = (int)totalCount
            };
        }
        else
        {
            // No detection — decay velocity, hold last position
            _smoothVel = Vector2.Lerp(_smoothVel, Vector2.zero, velocitySmoothing);
            _hadDetection = false;

            Data = new FishTrackData
            {
                detected          = false,
                posX              = _smoothPos.x,
                posY              = _smoothPos.y,
                velX              = _smoothVel.x,
                velY              = _smoothVel.y,
                velocityMagnitude = _smoothVel.magnitude,
                size              = 0f,
                blobPixelCount    = 0
            };
        }
    }

    private void CleanupCompute()
    {
        _partialBuffer?.Release();
        _partialBuffer = null;
        if (_maskRT != null) { _maskRT.Release(); _maskRT = null; }
    }

    private void OnDestroy() => CleanupCompute();

    // ── Editor debug gizmos (Scene view) ─────────────────────────────────────
    private void OnGUI()
    {
        if (!Application.isEditor) return;
        var d = Data;
        int x = 10, y = 10, h = 18;
        GUI.Label(new Rect(x, y,      300, h), $"Detected:  {d.detected}");
        GUI.Label(new Rect(x, y+h,    300, h), $"Pos:       ({d.posX:F3}, {d.posY:F3})");
        GUI.Label(new Rect(x, y+h*2,  300, h), $"Vel:       ({d.velX:F3}, {d.velY:F3})");
        GUI.Label(new Rect(x, y+h*3,  300, h), $"Speed:     {d.velocityMagnitude:F3}");
        GUI.Label(new Rect(x, y+h*4,  300, h), $"Size:      {d.size:F3}");
        GUI.Label(new Rect(x, y+h*5,  300, h), $"Pixels:    {d.blobPixelCount}");
    }
}

/// <summary>All tracker outputs for one frame. Positions and size are normalized 0–1.</summary>
[System.Serializable]
public struct FishTrackData
{
    public bool  detected;
    public float posX;              // 0=left,    1=right
    public float posY;              // 0=bottom,  1=top
    public float velX;              // signed, normalized units/sec
    public float velY;              // signed, normalized units/sec
    public float velocityMagnitude; // always >= 0
    public float size;              // geometric mean of bbox dims, 0–1
    public int   blobPixelCount;    // raw pixel count for diagnostics
}
