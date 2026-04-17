using UnityEngine;
using UnityEngine.UI;
using System.IO;

/// <summary>
/// Lets the user paint an exclusion mask over the video feed at runtime.
/// Left-click draws (excludes region from tracking), right-click erases.
/// Scroll wheel adjusts brush size. The mask is saved as a PNG in
/// StreamingAssets and loaded automatically on startup.
///
/// Setup: attach to any GameObject, assign tracker and videoImage references.
/// A red semi-transparent overlay is created automatically on the debug canvas.
/// </summary>
public class MaskPainter : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The fish tracker — exclusion mask will be passed to it automatically.")]
    public YellowFishTracker tracker;

    [Tooltip("The video RawImage used for coordinate mapping (from FishDebugCanvas).")]
    public RawImage videoImage;

    [Tooltip("When true, painting input is processed. Set by paint mode toggle.")]
    [HideInInspector]
    public bool paintingEnabled;

    [Header("Brush")]
    [Tooltip("Brush radius in mask pixels (1920x1080 space).")]
    [Range(5, 200)]
    public int brushRadius = 30;

    [Tooltip("How much the scroll wheel changes brush radius per tick.")]
    public int scrollStep = 5;

    [Header("Display")]
    [Tooltip("Alpha of the red exclusion overlay.")]
    [Range(0f, 1f)]
    public float overlayAlpha = 0.4f;

    // ── Constants ────────────────────────────────────────────────────────────────

    const int MaskWidth  = 1920;
    const int MaskHeight = 1080;

    static readonly Color32 MaskAllow   = new Color32(255, 255, 255, 255);
    static readonly Color32 MaskExclude = new Color32(0, 0, 0, 255);

    // ── Internal ─────────────────────────────────────────────────────────────────

    private Texture2D _maskTex;
    private Texture2D _overlayTex;
    private Color32[] _maskPixels;
    private Color32[] _overlayPixels;

    private RawImage _overlayImage;

    private bool  _dirty;
    private float _saveTimer;

    private bool _initialized;

    // True if the current left/right drag started while a UI panel consumed the click.
    // Suppresses painting for the whole drag so entering paint mode via the button
    // doesn't also paint a stroke where you clicked.
    private bool _leftDragConsumed;
    private bool _rightDragConsumed;

    private static string MaskPath =>
        Path.Combine(Application.streamingAssetsPath, "ExclusionMask.png");

    /// <summary>Screen-space brush diameter for cursor drawing (updated each frame).</summary>
    public float ScreenBrushDiameter { get; private set; }

    /// <summary>Whether the mouse is currently over the video area.</summary>
    public bool MouseOverVideo { get; private set; }

    // ── Lifecycle ────────────────────────────────────────────────────────────────

    private void OnEnable()
    {
        if (!_initialized)
            Initialize();

        if (_overlayImage != null)
            _overlayImage.enabled = true;
    }

    private void OnDisable()
    {
        // Keep overlay visible so exclusion mask is always shown
        MouseOverVideo = false;
    }

    private void Initialize()
    {
        _maskTex    = new Texture2D(MaskWidth, MaskHeight, TextureFormat.RGBA32, false);
        _overlayTex = new Texture2D(MaskWidth, MaskHeight, TextureFormat.RGBA32, false);

        _maskPixels    = new Color32[MaskWidth * MaskHeight];
        _overlayPixels = new Color32[MaskWidth * MaskHeight];

        Color32 overlayAllow = new Color32(0, 0, 0, 0);
        for (int i = 0; i < _maskPixels.Length; i++)
        {
            _maskPixels[i]    = MaskAllow;
            _overlayPixels[i] = overlayAllow;
        }

        LoadMask();
        ApplyTextures();
        CreateOverlay();
        PassMaskToTracker();
        _initialized = true;
    }

    private void Update()
    {
        UpdateScreenBrushSize();
        HandleInput();

        if (_dirty)
        {
            _saveTimer -= Time.deltaTime;
            if (_saveTimer <= 0f)
            {
                SaveMask();
                _dirty = false;
            }
        }

        // Overlay is parented to videoImage, no manual sync needed
    }

    private void UpdateScreenBrushSize()
    {
        if (videoImage == null) { ScreenBrushDiameter = 0; return; }
        Vector3[] corners = new Vector3[4];
        videoImage.rectTransform.GetWorldCorners(corners);
        float screenVideoWidth = corners[2].x - corners[0].x;
        float maskToScreen = screenVideoWidth / MaskWidth;
        ScreenBrushDiameter = brushRadius * 2f * maskToScreen;
    }

    // ── Overlay setup ────────────────────────────────────────────────────────────

    private void CreateOverlay()
    {
        if (videoImage == null) return;

        var go = new GameObject("ExclusionMaskOverlay");
        // Parent to the videoImage itself so it follows all scaling/positioning
        go.transform.SetParent(videoImage.transform, false);

        _overlayImage = go.AddComponent<RawImage>();
        _overlayImage.texture = _overlayTex;
        _overlayImage.raycastTarget = false;

        // Stretch to fill videoImage exactly
        var rt = _overlayImage.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    // ── Input handling ───────────────────────────────────────────────────────────

    private void HandleInput()
    {
        if (videoImage == null || !paintingEnabled) return;

        // Check if mouse is over the video area
        RectTransform videoRect = videoImage.rectTransform;
        Camera cam = null;
        var canvas = videoImage.GetComponentInParent<Canvas>();
        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            cam = canvas.worldCamera;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            videoRect, Input.mousePosition, cam, out Vector2 localPoint);
        Rect rect = videoRect.rect;
        float u = (localPoint.x - rect.x) / rect.width;
        float v = (localPoint.y - rect.y) / rect.height;
        MouseOverVideo = u >= 0f && u <= 1f && v >= 0f && v <= 1f;

        // Scroll to adjust brush size (only when not consumed by UI)
        if (!FishSynthInput.InputConsumed)
        {
            float scroll = Input.mouseScrollDelta.y;
            if (scroll != 0f)
                brushRadius = Mathf.Clamp(brushRadius + (int)(scroll * scrollStep), 5, 200);
        }

        // Track whether each button's press was consumed by the UI.
        // This prevents painting mid-drag if the click that started the drag hit a UI panel.
        if (Input.GetMouseButtonDown(0))
            _leftDragConsumed = FishSynthInput.InputConsumed;
        if (Input.GetMouseButtonUp(0))
            _leftDragConsumed = false;
        if (Input.GetMouseButtonDown(1))
            _rightDragConsumed = FishSynthInput.InputConsumed;
        if (Input.GetMouseButtonUp(1))
            _rightDragConsumed = false;

        bool painting = Input.GetMouseButton(0) && !_leftDragConsumed;
        bool erasing  = Input.GetMouseButton(1) && !_rightDragConsumed;
        if (!painting && !erasing) return;

        if (!MouseOverVideo) return;

        int cx = Mathf.RoundToInt(u * (MaskWidth  - 1));
        int cy = Mathf.RoundToInt(v * (MaskHeight - 1));

        PaintCircle(cx, cy, brushRadius, painting);
        ApplyTextures();
        PassMaskToTracker();

        _dirty = true;
        _saveTimer = 1f;
    }

    private void PaintCircle(int cx, int cy, int radius, bool exclude)
    {
        Color32 maskColor    = exclude ? MaskExclude : MaskAllow;
        Color32 overlayColor = exclude
            ? new Color32(255, 0, 0, (byte)(overlayAlpha * 255))
            : new Color32(0, 0, 0, 0);

        int r2 = radius * radius;

        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (dx * dx + dy * dy > r2) continue;

                int px = cx + dx;
                int py = cy + dy;
                if (px < 0 || px >= MaskWidth || py < 0 || py >= MaskHeight) continue;

                int idx = py * MaskWidth + px;
                _maskPixels[idx]    = maskColor;
                _overlayPixels[idx] = overlayColor;
            }
        }
    }

    private void ApplyTextures()
    {
        _maskTex.SetPixels32(_maskPixels);
        _maskTex.Apply();

        _overlayTex.SetPixels32(_overlayPixels);
        _overlayTex.Apply();
    }

    private void PassMaskToTracker()
    {
        if (tracker != null)
            tracker.exclusionMask = _maskTex;
    }

    // ── Preset API ──────────────────────────────────────────────────────────────

    /// <summary>Get the current mask texture for saving as a preset.</summary>
    public Texture2D GetMaskTexture()
    {
        return _maskTex;
    }

    /// <summary>Load a mask from a texture (e.g. from a preset). Texture is consumed.</summary>
    public void LoadFromTexture(Texture2D loadTex)
    {
        if (loadTex == null || !_initialized) return;
        if (loadTex.width != MaskWidth || loadTex.height != MaskHeight)
        {
            Debug.LogWarning($"[MaskPainter] Preset mask size mismatch ({loadTex.width}x{loadTex.height}), ignoring.");
            Object.Destroy(loadTex);
            return;
        }

        Color32[] loaded = loadTex.GetPixels32();
        byte overlayA = (byte)(overlayAlpha * 255);
        for (int i = 0; i < _maskPixels.Length; i++)
        {
            bool excluded = loaded[i].r < 128;
            _maskPixels[i]    = excluded ? MaskExclude : MaskAllow;
            _overlayPixels[i] = excluded
                ? new Color32(255, 0, 0, overlayA)
                : new Color32(0, 0, 0, 0);
        }
        Object.Destroy(loadTex);

        ApplyTextures();
        PassMaskToTracker();
        SaveMask();
    }

    /// <summary>Clear the mask (all white / no exclusions).</summary>
    public void ClearMask()
    {
        if (!_initialized) return;
        for (int i = 0; i < _maskPixels.Length; i++)
        {
            _maskPixels[i]    = MaskAllow;
            _overlayPixels[i] = new Color32(0, 0, 0, 0);
        }
        ApplyTextures();
        PassMaskToTracker();
        SaveMask();
    }

    // ── Save / Load ──────────────────────────────────────────────────────────────

    private void SaveMask()
    {
        try
        {
            byte[] png = _maskTex.EncodeToPNG();

            string dir = Path.GetDirectoryName(MaskPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            File.WriteAllBytes(MaskPath, png);
            Debug.Log($"[MaskPainter] Mask saved to {MaskPath}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[MaskPainter] Failed to save mask: {e.Message}");
        }
    }

    private void LoadMask()
    {
        if (!File.Exists(MaskPath))
        {
            Debug.Log("[MaskPainter] No exclusion mask found, starting fresh.");
            return;
        }

        try
        {
            byte[] data = File.ReadAllBytes(MaskPath);
            var loadTex = new Texture2D(2, 2);
            loadTex.LoadImage(data);

            if (loadTex.width != MaskWidth || loadTex.height != MaskHeight)
            {
                Debug.LogWarning($"[MaskPainter] Mask size mismatch " +
                    $"({loadTex.width}x{loadTex.height}), expected {MaskWidth}x{MaskHeight}. Ignoring.");
                Destroy(loadTex);
                return;
            }

            Color32[] loaded = loadTex.GetPixels32();
            byte overlayA = (byte)(overlayAlpha * 255);

            for (int i = 0; i < _maskPixels.Length; i++)
            {
                bool excluded = loaded[i].r < 128;
                _maskPixels[i]    = excluded ? MaskExclude : MaskAllow;
                _overlayPixels[i] = excluded
                    ? new Color32(255, 0, 0, overlayA)
                    : new Color32(0, 0, 0, 0);
            }

            Destroy(loadTex);
            Debug.Log("[MaskPainter] Exclusion mask loaded.");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[MaskPainter] Failed to load mask: {e.Message}");
        }
    }

    // ── Cleanup ──────────────────────────────────────────────────────────────────

    private void OnDestroy()
    {
        if (_dirty) SaveMask();
        if (_maskTex != null)    Destroy(_maskTex);
        if (_overlayTex != null) Destroy(_overlayTex);
        if (_overlayImage != null) Destroy(_overlayImage.gameObject);
    }
}
