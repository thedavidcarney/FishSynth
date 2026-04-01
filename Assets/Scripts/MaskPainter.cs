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

    private Texture2D _maskTex;       // white=allow, black=exclude → passed to compute
    private Texture2D _overlayTex;    // transparent=allow, red=exclude → displayed
    private Color32[] _maskPixels;
    private Color32[] _overlayPixels;

    private RawImage _overlayImage;
    private Texture2D _cursorTex;
    private int _cursorTexSize;

    private bool  _dirty;
    private float _saveTimer;

    private static string MaskPath =>
        Path.Combine(Application.streamingAssetsPath, "ExclusionMask.png");

    // ── Lifecycle ────────────────────────────────────────────────────────────────

    private void Start()
    {
        _maskTex    = new Texture2D(MaskWidth, MaskHeight, TextureFormat.RGBA32, false);
        _overlayTex = new Texture2D(MaskWidth, MaskHeight, TextureFormat.RGBA32, false);

        _maskPixels    = new Color32[MaskWidth * MaskHeight];
        _overlayPixels = new Color32[MaskWidth * MaskHeight];

        // Default: all allowed
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
    }

    private void Update()
    {
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

        // Keep overlay aspect ratio matched to video
        if (_overlayImage != null && videoImage != null && videoImage.texture != null)
        {
            var fitter = _overlayImage.GetComponent<AspectRatioFitter>();
            if (fitter != null)
            {
                var tex = videoImage.texture;
                if (tex.height > 0)
                    fitter.aspectRatio = (float)tex.width / tex.height;
            }
        }
    }

    // ── Overlay setup ────────────────────────────────────────────────────────────

    private void CreateOverlay()
    {
        if (videoImage == null) return;

        var go = new GameObject("ExclusionMaskOverlay");
        go.transform.SetParent(videoImage.transform.parent, false);
        // Place right after the video image
        go.transform.SetSiblingIndex(videoImage.transform.GetSiblingIndex() + 1);

        _overlayImage = go.AddComponent<RawImage>();
        _overlayImage.texture = _overlayTex;
        _overlayImage.raycastTarget = false;

        // Stretch to fill parent (same anchoring as video/mask images)
        var rt = _overlayImage.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        // Match video aspect ratio
        var fitter = go.AddComponent<AspectRatioFitter>();
        fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        fitter.aspectRatio = (float)MaskWidth / MaskHeight;
    }

    // ── Input handling ───────────────────────────────────────────────────────────

    private void HandleInput()
    {
        if (videoImage == null) return;

        // Don't handle input if the Shapes UI consumed this frame's input
        if (FishSynthInput.InputConsumed) return;

        // Scroll to adjust brush size
        float scroll = Input.mouseScrollDelta.y;
        if (scroll != 0f)
            brushRadius = Mathf.Clamp(brushRadius + (int)(scroll * scrollStep), 5, 200);

        bool painting = Input.GetMouseButton(0);
        bool erasing  = Input.GetMouseButton(1);
        if (!painting && !erasing) return;

        // Convert screen pos → local point on video RawImage
        Vector2 localPoint;
        RectTransform videoRect = videoImage.rectTransform;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                videoRect, Input.mousePosition, null, out localPoint))
            return;

        // Convert to UV (0–1)
        Rect rect = videoRect.rect;
        float u = (localPoint.x - rect.x) / rect.width;
        float v = (localPoint.y - rect.y) / rect.height;

        if (u < 0f || u > 1f || v < 0f || v > 1f) return;

        // UV to mask pixel (v=0 bottom, v=1 top matches texture y convention)
        int cx = Mathf.RoundToInt(u * (MaskWidth  - 1));
        int cy = Mathf.RoundToInt(v * (MaskHeight - 1));

        PaintCircle(cx, cy, brushRadius, painting);
        ApplyTextures();
        PassMaskToTracker();

        _dirty = true;
        _saveTimer = 1f; // debounce save by 1 second
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
                bool excluded = loaded[i].r < 128; // black = excluded
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

    // ── Brush cursor ─────────────────────────────────────────────────────────────

    private void OnGUI()
    {
        if (videoImage == null) return;

        // Rebuild cursor texture if brush size changed
        int desiredSize = Mathf.Max(brushRadius * 2, 8);
        if (_cursorTex == null || _cursorTexSize != desiredSize)
        {
            if (_cursorTex != null) Destroy(_cursorTex);
            _cursorTexSize = desiredSize;
            _cursorTex = CreateRingTexture(_cursorTexSize);
        }

        // Compute screen-space brush size from mask-pixel brush radius
        Vector3[] corners = new Vector3[4];
        videoImage.rectTransform.GetWorldCorners(corners);
        float screenVideoWidth = corners[2].x - corners[0].x;
        float maskToScreen = screenVideoWidth / MaskWidth;
        float screenDiameter = brushRadius * 2f * maskToScreen;

        // Draw the ring at cursor position
        Vector2 mouse = Event.current.mousePosition;
        float half = screenDiameter * 0.5f;
        GUI.DrawTexture(new Rect(mouse.x - half, mouse.y - half,
            screenDiameter, screenDiameter), _cursorTex);
    }

    private Texture2D CreateRingTexture(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var pixels = new Color32[size * size];
        float center = size * 0.5f;
        float outerR = center;
        float innerR = Mathf.Max(center - 2f, center * 0.9f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - center;
                float dy = y + 0.5f - center;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                bool inRing = dist <= outerR && dist >= innerR;
                pixels[y * size + x] = inRing
                    ? new Color32(255, 255, 255, 200)
                    : new Color32(0, 0, 0, 0);
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply();
        tex.filterMode = FilterMode.Bilinear;
        return tex;
    }

    // ── Cleanup ──────────────────────────────────────────────────────────────────

    private void OnDestroy()
    {
        if (_dirty) SaveMask();
        if (_maskTex != null)    Destroy(_maskTex);
        if (_overlayTex != null) Destroy(_overlayTex);
        if (_cursorTex != null)  Destroy(_cursorTex);
        if (_overlayImage != null) Destroy(_overlayImage.gameObject);
    }
}
