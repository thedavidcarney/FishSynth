using UnityEngine;
using System.IO;

/// <summary>
/// Tracks the yellow fish using a GPU hue-mask + morphology compute shader.
/// Pipeline per frame:
///   HueMask → Erode1 → Dilate → Erode2 → ReduceMoments
///
/// Outputs normalized (0–1) position, velocity, bbox, and size each frame
/// via the Data property. Dead-reckoning holds position when fish is occluded.
///
/// Tunable settings are saved to/loaded from:
///   Assets/StreamingAssets/FishTrackerConfig.json
/// This file can be committed to git for shared defaults, or .gitignored for
/// per-machine overrides.
/// </summary>
public class YellowFishTracker : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("References")]
    [Tooltip("Assign FishHueMask.compute here.")]
    public ComputeShader fishComputeShader;

    [Tooltip("RenderTexture receiving the video feed. Set automatically by VideoFileInput.")]
    public RenderTexture videoTexture;

    [Tooltip("The processed mask RT after morphology. Wired to debug RawImage by VideoFileInput.")]
    public RenderTexture debugMaskTexture;

    [Header("HSV Thresholds  (0–1; Hue: 0=red  0.167=yellow  0.333=green  0.667=blue)")]
    [Range(0f, 1f)] public float hueMin = 0.10f;
    [Range(0f, 1f)] public float hueMax = 0.20f;
    [Range(0f, 1f)] public float satMin = 0.50f;
    [Range(0f, 1f)] public float satMax = 1.00f;
    [Range(0f, 1f)] public float valMin = 0.40f;
    [Range(0f, 1f)] public float valMax = 1.00f;

    [Header("Morphology")]
    [Tooltip("Radius for first Erode pass. Removes isolated noise specs.")]
    [Range(1, 10)] public int erode1Radius = 2;

    [Tooltip("Radius for Dilate pass. Bridges gaps between disconnected fish blobs.")]
    [Range(1, 20)] public int dilateRadius = 6;

    [Tooltip("Radius for second Erode pass. Brings blob back to roughly fish size.")]
    [Range(1, 10)] public int erode2Radius = 4;

    [Header("Tracking")]
    [Tooltip("Minimum pixel count in final mask to consider a valid detection.")]
    public int minBlobPixels = 80;

    [Tooltip("EMA alpha for position smoothing (0=frozen, 1=raw).")]
    [Range(0f, 1f)] public float positionSmoothing = 0.5f;

    [Tooltip("EMA alpha for velocity smoothing.")]
    [Range(0f, 1f)] public float velocitySmoothing = 0.2f;

    [Header("Dead Reckoning (occlusion handling)")]
    [Tooltip("How long (seconds) to coast on last velocity before freezing position.")]
    public float deadReckonDuration = 0.5f;

    [Tooltip("Max speed clamp in normalized units/sec to prevent runaway on reacquisition.")]
    public float maxVelocity = 5f;

    // ── Public output ─────────────────────────────────────────────────────────

    public FishTrackData Data { get; private set; }

    // ── Internal ──────────────────────────────────────────────────────────────

    private int _kMask, _kErode, _kDilate, _kMoments;

    private RenderTexture _maskA;
    private RenderTexture _maskB;
    private ComputeBuffer _partialBuffer;
    private float[]       _partialData;
    private int _numGroupsX, _numGroupsY, _totalGroups;
    private int _texWidth, _texHeight;

    private Vector2 _smoothPos;
    private Vector2 _smoothVel;
    private bool    _hadDetection;
    private float   _lostTimer;

    private RenderTexture _finalMaskRT;

    // Cached values to detect Inspector changes
    private float _prev_hueMin, _prev_hueMax;
    private float _prev_satMin, _prev_satMax;
    private float _prev_valMin, _prev_valMax;
    private int   _prev_erode1, _prev_dilate, _prev_erode2;
    private int   _prev_minBlob;
    private float _prev_posSm, _prev_velSm;
    private float _prev_deadReckon, _prev_maxVel;

    private static string ConfigPath =>
        Path.Combine(Application.streamingAssetsPath, "FishTrackerConfig.json");

    // ── Serializable config ───────────────────────────────────────────────────

    [System.Serializable]
    private class TrackerConfig
    {
        public float hueMin           = 0.10f;
        public float hueMax           = 0.20f;
        public float satMin           = 0.50f;
        public float satMax           = 1.00f;
        public float valMin           = 0.40f;
        public float valMax           = 1.00f;
        public int   erode1Radius     = 2;
        public int   dilateRadius     = 6;
        public int   erode2Radius     = 4;
        public int   minBlobPixels    = 80;
        public float positionSmoothing  = 0.5f;
        public float velocitySmoothing  = 0.2f;
        public float deadReckonDuration = 0.5f;
        public float maxVelocity        = 5f;
    }

    // ─────────────────────────────────────────────────────────────────────────

    private void Start()
    {
        LoadConfig();
        CacheValues();
        InitCompute();
    }

    public void InitCompute()
    {
        CleanupCompute();

        if (videoTexture == null)
        {
            Debug.LogWarning("[YellowFishTracker] No videoTexture assigned.");
            return;
        }

        _texWidth  = videoTexture.width;
        _texHeight = videoTexture.height;

        _maskA = MakeRT("FishMaskA");
        _maskB = MakeRT("FishMaskB");

        _numGroupsX  = Mathf.CeilToInt(_texWidth  / 8f);
        _numGroupsY  = Mathf.CeilToInt(_texHeight / 8f);
        _totalGroups = _numGroupsX * _numGroupsY;

        _partialBuffer = new ComputeBuffer(_totalGroups * 7, sizeof(float));
        _partialData   = new float[_totalGroups * 7];

        _kMask    = fishComputeShader.FindKernel("HueMask");
        _kErode   = fishComputeShader.FindKernel("Erode");
        _kDilate  = fishComputeShader.FindKernel("Dilate");
        _kMoments = fishComputeShader.FindKernel("ReduceMoments");

        fishComputeShader.SetInt("TextureWidth",  _texWidth);
        fishComputeShader.SetInt("TextureHeight", _texHeight);

        fishComputeShader.SetTexture(_kMask, "InputTexture", videoTexture);
        fishComputeShader.SetTexture(_kMask, "MaskA", _maskA);

        debugMaskTexture = _maskA;
    }

    private RenderTexture MakeRT(string name)
    {
        var rt = new RenderTexture(_texWidth, _texHeight, 0, RenderTextureFormat.ARGB32)
        {
            enableRandomWrite = true,
            name = name
        };
        rt.Create();
        return rt;
    }

    private void Update()
    {
        if (videoTexture == null || fishComputeShader == null) return;

        if (videoTexture.width != _texWidth || videoTexture.height != _texHeight)
            InitCompute();

        CheckAndSaveConfig();
        RunCompute();
        ReadbackAndProcess();
    }

    // ── Config save / load ────────────────────────────────────────────────────

    private void LoadConfig()
    {
        if (!File.Exists(ConfigPath))
        {
            Debug.Log("[YellowFishTracker] No config file found, using defaults.");
            return;
        }

        try
        {
            string json = File.ReadAllText(ConfigPath);
            var cfg = JsonUtility.FromJson<TrackerConfig>(json);

            hueMin             = cfg.hueMin;
            hueMax             = cfg.hueMax;
            satMin             = cfg.satMin;
            satMax             = cfg.satMax;
            valMin             = cfg.valMin;
            valMax             = cfg.valMax;
            erode1Radius       = cfg.erode1Radius;
            dilateRadius       = cfg.dilateRadius;
            erode2Radius       = cfg.erode2Radius;
            minBlobPixels      = cfg.minBlobPixels;
            positionSmoothing  = cfg.positionSmoothing;
            velocitySmoothing  = cfg.velocitySmoothing;
            deadReckonDuration = cfg.deadReckonDuration;
            maxVelocity        = cfg.maxVelocity;

            Debug.Log($"[YellowFishTracker] Config loaded from {ConfigPath}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[YellowFishTracker] Failed to load config: {e.Message}");
        }
    }

    private void SaveConfig()
    {
        try
        {
            var cfg = new TrackerConfig
            {
                hueMin             = hueMin,
                hueMax             = hueMax,
                satMin             = satMin,
                satMax             = satMax,
                valMin             = valMin,
                valMax             = valMax,
                erode1Radius       = erode1Radius,
                dilateRadius       = dilateRadius,
                erode2Radius       = erode2Radius,
                minBlobPixels      = minBlobPixels,
                positionSmoothing  = positionSmoothing,
                velocitySmoothing  = velocitySmoothing,
                deadReckonDuration = deadReckonDuration,
                maxVelocity        = maxVelocity,
            };

            string dir = Path.GetDirectoryName(ConfigPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            File.WriteAllText(ConfigPath, JsonUtility.ToJson(cfg, prettyPrint: true));
            Debug.Log($"[YellowFishTracker] Config saved to {ConfigPath}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[YellowFishTracker] Failed to save config: {e.Message}");
        }
    }

    private void CacheValues()
    {
        _prev_hueMin     = hueMin;       _prev_hueMax  = hueMax;
        _prev_satMin     = satMin;       _prev_satMax  = satMax;
        _prev_valMin     = valMin;       _prev_valMax  = valMax;
        _prev_erode1     = erode1Radius;
        _prev_dilate     = dilateRadius;
        _prev_erode2     = erode2Radius;
        _prev_minBlob    = minBlobPixels;
        _prev_posSm      = positionSmoothing;
        _prev_velSm      = velocitySmoothing;
        _prev_deadReckon = deadReckonDuration;
        _prev_maxVel     = maxVelocity;
    }

    private void CheckAndSaveConfig()
    {
        if (hueMin != _prev_hueMin || hueMax != _prev_hueMax ||
            satMin != _prev_satMin || satMax != _prev_satMax ||
            valMin != _prev_valMin || valMax != _prev_valMax ||
            erode1Radius != _prev_erode1 || dilateRadius != _prev_dilate ||
            erode2Radius != _prev_erode2 || minBlobPixels != _prev_minBlob ||
            positionSmoothing != _prev_posSm || velocitySmoothing != _prev_velSm ||
            deadReckonDuration != _prev_deadReckon || maxVelocity != _prev_maxVel)
        {
            SaveConfig();
            CacheValues();
        }
    }

    // ── Compute pipeline ──────────────────────────────────────────────────────

    private void RunCompute()
    {
        fishComputeShader.SetFloat("HueMin", hueMin);
        fishComputeShader.SetFloat("HueMax", hueMax);
        fishComputeShader.SetFloat("SatMin", satMin);
        fishComputeShader.SetFloat("SatMax", satMax);
        fishComputeShader.SetFloat("ValMin", valMin);
        fishComputeShader.SetFloat("ValMax", valMax);

        // Pass 1: HueMask → MaskA
        fishComputeShader.SetTexture(_kMask, "InputTexture", videoTexture);
        fishComputeShader.SetTexture(_kMask, "MaskA", _maskA);
        fishComputeShader.Dispatch(_kMask, _numGroupsX, _numGroupsY, 1);

        // Pass 2: Erode MaskA → MaskB
        fishComputeShader.SetInt("MorphRadius", erode1Radius);
        fishComputeShader.SetTexture(_kErode, "MorphSrc", _maskA);
        fishComputeShader.SetTexture(_kErode, "MorphDst", _maskB);
        fishComputeShader.Dispatch(_kErode, _numGroupsX, _numGroupsY, 1);

        // Pass 3: Dilate MaskB → MaskA
        fishComputeShader.SetInt("MorphRadius", dilateRadius);
        fishComputeShader.SetTexture(_kDilate, "MorphSrc", _maskB);
        fishComputeShader.SetTexture(_kDilate, "MorphDst", _maskA);
        fishComputeShader.Dispatch(_kDilate, _numGroupsX, _numGroupsY, 1);

        // Pass 4: Erode MaskA → MaskB
        fishComputeShader.SetInt("MorphRadius", erode2Radius);
        fishComputeShader.SetTexture(_kErode, "MorphSrc", _maskA);
        fishComputeShader.SetTexture(_kErode, "MorphDst", _maskB);
        fishComputeShader.Dispatch(_kErode, _numGroupsX, _numGroupsY, 1);

        _finalMaskRT     = _maskB;
        debugMaskTexture = _maskB;

        // Pass 5: ReduceMoments
        fishComputeShader.SetTexture(_kMoments, "MorphSrc",      _finalMaskRT);
        fishComputeShader.SetBuffer (_kMoments, "PartialMoments", _partialBuffer);
        fishComputeShader.Dispatch(_kMoments, _numGroupsX, _numGroupsY, 1);
    }

    private void ReadbackAndProcess()
    {
        _partialBuffer.GetData(_partialData);

        float totalCount = 0, sumX = 0, sumY = 0;
        float minX = _texWidth, minY = _texHeight, maxX = 0, maxY = 0;

        for (int i = 0; i < _totalGroups; i++)
        {
            int b = i * 7;
            float cnt = _partialData[b];
            if (cnt < 0.5f) continue;

            totalCount += cnt;
            sumX += _partialData[b + 1];
            sumY += _partialData[b + 2];
            minX  = Mathf.Min(minX, _partialData[b + 3]);
            minY  = Mathf.Min(minY, _partialData[b + 4]);
            maxX  = Mathf.Max(maxX, _partialData[b + 5]);
            maxY  = Mathf.Max(maxY, _partialData[b + 6]);
        }

        float dt = Mathf.Max(Time.deltaTime, 0.001f);

        if (totalCount >= minBlobPixels)
        {
            _lostTimer = 0f;

            float rawX = sumX / totalCount / _texWidth;
            float rawY = 1f - (sumY / totalCount / _texHeight);

            float bboxMinX = minX / _texWidth;
            float bboxMaxX = maxX / _texWidth;
            float bboxMinY = 1f - (maxY / _texHeight);
            float bboxMaxY = 1f - (minY / _texHeight);

            float bboxW = bboxMaxX - bboxMinX;
            float bboxH = bboxMaxY - bboxMinY;
            float size  = Mathf.Sqrt(bboxW * bboxH);

            Vector2 rawPos = new Vector2(rawX, rawY);

            if (!_hadDetection)
            {
                _smoothPos    = rawPos;
                _smoothVel    = Vector2.zero;
                _hadDetection = true;
            }

            Vector2 prevPos = _smoothPos;
            _smoothPos = Vector2.Lerp(_smoothPos, rawPos, positionSmoothing);

            Vector2 rawVel = (_smoothPos - prevPos) / dt;
            rawVel     = Vector2.ClampMagnitude(rawVel, maxVelocity);
            _smoothVel = Vector2.Lerp(_smoothVel, rawVel, velocitySmoothing);

            Data = new FishTrackData
            {
                detected          = true,
                posX              = _smoothPos.x,
                posY              = _smoothPos.y,
                velX              = _smoothVel.x,
                velY              = _smoothVel.y,
                velocityMagnitude = _smoothVel.magnitude,
                size              = size,
                blobPixelCount    = (int)totalCount,
                bboxMinX          = bboxMinX,
                bboxMinY          = bboxMinY,
                bboxMaxX          = bboxMaxX,
                bboxMaxY          = bboxMaxY,
            };
        }
        else
        {
            _lostTimer += dt;
            _hadDetection = false;

            if (_lostTimer < deadReckonDuration)
            {
                _smoothPos += _smoothVel * dt;
                _smoothPos  = new Vector2(Mathf.Clamp01(_smoothPos.x), Mathf.Clamp01(_smoothPos.y));
            }

            _smoothVel = Vector2.Lerp(_smoothVel, Vector2.zero, velocitySmoothing);

            Data = new FishTrackData
            {
                detected          = false,
                posX              = _smoothPos.x,
                posY              = _smoothPos.y,
                velX              = _smoothVel.x,
                velY              = _smoothVel.y,
                velocityMagnitude = _smoothVel.magnitude,
                size              = Data.size,
                blobPixelCount    = 0,
                bboxMinX          = Data.bboxMinX,
                bboxMinY          = Data.bboxMinY,
                bboxMaxX          = Data.bboxMaxX,
                bboxMaxY          = Data.bboxMaxY,
            };
        }
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────

    private void CleanupCompute()
    {
        _partialBuffer?.Release();
        _partialBuffer = null;
        if (_maskA != null) { _maskA.Release(); _maskA = null; }
        if (_maskB != null) { _maskB.Release(); _maskB = null; }
    }

    private void OnDestroy() => CleanupCompute();

    // ── OnGUI diagnostics ─────────────────────────────────────────────────────
    private void OnGUI()
    {
        if (!Application.isEditor) return;
        var d = Data;
        int x = 10, y = 10, lh = 18;
        GUI.Label(new Rect(x, y,      300, lh), $"Detected:  {d.detected}  (lost: {_lostTimer:F2}s)");
        GUI.Label(new Rect(x, y+lh,   300, lh), $"Pos:       ({d.posX:F3}, {d.posY:F3})");
        GUI.Label(new Rect(x, y+lh*2, 300, lh), $"Vel:       ({d.velX:F3}, {d.velY:F3})");
        GUI.Label(new Rect(x, y+lh*3, 300, lh), $"Speed:     {d.velocityMagnitude:F3}");
        GUI.Label(new Rect(x, y+lh*4, 300, lh), $"Size:      {d.size:F3}");
        GUI.Label(new Rect(x, y+lh*5, 300, lh), $"Pixels:    {d.blobPixelCount}");
        GUI.Label(new Rect(x, y+lh*6, 300, lh), $"BBox:      ({d.bboxMinX:F2},{d.bboxMinY:F2}) → ({d.bboxMaxX:F2},{d.bboxMaxY:F2})");
    }
}

/// <summary>
/// All tracker outputs for one frame.
/// Positions, bbox coords, and size are normalized 0–1.
/// Y=0 is bottom, Y=1 is top.
/// </summary>
[System.Serializable]
public struct FishTrackData
{
    public bool  detected;
    public float posX;
    public float posY;
    public float velX;
    public float velY;
    public float velocityMagnitude;
    public float size;
    public int   blobPixelCount;
    public float bboxMinX;
    public float bboxMinY;
    public float bboxMaxX;
    public float bboxMaxY;
}
