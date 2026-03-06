using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Manages the fish tracking debug view:
///   - Video feed RawImage (base layer)
///   - HSV mask RawImage on top in Additive blend (black = transparent)
///   - Bounding box drawn with four UI Image bars
///
/// Setup:
///   1. Create a Canvas (Screen Space - Overlay)
///   2. Add a RawImage for video (videoImage) — sized to fill canvas
///   3. Add a RawImage for mask (maskImage) on top — same size, Additive material
///   4. Add four UI Images as children for bbox lines (bboxTop/Bottom/Left/Right)
///   5. Attach this component, assign all references and the tracker
///
/// The mask blend slider lets you fade the mask overlay in/out at runtime.
/// </summary>
public class FishDebugCanvas : MonoBehaviour
{
    [Header("References")]
    public YellowFishTracker tracker;

    [Header("Video Layers")]
    public RawImage videoImage;
    public RawImage maskImage;

    [Tooltip("0 = video only, 1 = full mask overlay (additive)")]
    [Range(0f, 1f)] public float maskBlend = 1f;

    [Header("Bounding Box")]
    public RectTransform bboxTop;
    public RectTransform bboxBottom;
    public RectTransform bboxLeft;
    public RectTransform bboxRight;

    [Tooltip("Color of the bounding box lines.")]
    public Color bboxColor = Color.green;

    [Tooltip("Thickness of the bounding box lines in pixels.")]
    public float bboxLineThickness = 2f;

    [Tooltip("Show bbox even when fish is not detected (shows dead-reckoned position).")]
    public bool showBboxWhenLost = true;

    [Tooltip("Color tint when fish is not detected.")]
    public Color bboxLostColor = new Color(1f, 0.5f, 0f); // orange

    // ── Internal ──────────────────────────────────────────────────────────────

    private Material _additiveMaterial;
    private Image[]  _bboxImages;
    private RectTransform _videoRect;

    // ─────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        // Create additive material for mask overlay so black = transparent
        _additiveMaterial = new Material(Shader.Find("UI/Default"))
        {
            name = "FishMaskAdditive"
        };

        // Unity UI additive blend: set via shader keywords isn't straightforward,
        // so we use color alpha = 0 for black passthrough by tinting.
        // A proper additive blend requires a custom material; we approximate with
        // a high-alpha white tint and rely on the mask being white-on-black.
        // For true additive, assign a UI-Additive material in the Inspector instead.
        if (maskImage != null)
        {
            maskImage.material = _additiveMaterial;
        }

        _videoRect = videoImage != null ? videoImage.rectTransform : null;

        // Collect bbox Image components for color setting
        _bboxImages = new Image[]
        {
            bboxTop    != null ? bboxTop.GetComponent<Image>()    : null,
            bboxBottom != null ? bboxBottom.GetComponent<Image>() : null,
            bboxLeft   != null ? bboxLeft.GetComponent<Image>()   : null,
            bboxRight  != null ? bboxRight.GetComponent<Image>()  : null,
        };

        SetBboxVisible(false);
    }

    private void Update()
    {
        UpdateMaskBlend();
        UpdateBoundingBox();
    }

    private void UpdateMaskBlend()
    {
        if (maskImage == null) return;
        Color c = maskImage.color;
        c.a = maskBlend;
        maskImage.color = c;
    }

    private void UpdateBoundingBox()
    {
        if (tracker == null || _videoRect == null) return;

        FishTrackData d = tracker.Data;

        bool show = d.detected || (showBboxWhenLost && _lostVisible(d));
        SetBboxVisible(show);

        if (!show) return;

        Color col = d.detected ? bboxColor : bboxLostColor;
        foreach (var img in _bboxImages)
            if (img != null) img.color = col;

        // Video RawImage rect in local space
        Rect rect = _videoRect.rect;
        float w = rect.width;
        float h = rect.height;

        // Convert normalized tracker coords to pixel offsets within videoRect
        // Tracker: X 0=left 1=right, Y 0=bottom 1=top
        // UI rect: origin at center; left=-w/2, bottom=-h/2
        float px0 = d.bboxMinX * w - w * 0.5f;
        float px1 = d.bboxMaxX * w - w * 0.5f;
        float py0 = (1f - d.bboxMaxY) * h - h * 0.5f;
        float py1 = (1f - d.bboxMinY) * h - h * 0.5f;

        float bw = px1 - px0;
        float bh = py1 - py0;
        float t  = bboxLineThickness;

        // Top bar
        if (bboxTop != null)
        {
            bboxTop.anchoredPosition = new Vector2(px0 + bw * 0.5f, py1);
            bboxTop.sizeDelta = new Vector2(bw, t);
        }
        // Bottom bar
        if (bboxBottom != null)
        {
            bboxBottom.anchoredPosition = new Vector2(px0 + bw * 0.5f, py0);
            bboxBottom.sizeDelta = new Vector2(bw, t);
        }
        // Left bar
        if (bboxLeft != null)
        {
            bboxLeft.anchoredPosition = new Vector2(px0, py0 + bh * 0.5f);
            bboxLeft.sizeDelta = new Vector2(t, bh);
        }
        // Right bar
        if (bboxRight != null)
        {
            bboxRight.anchoredPosition = new Vector2(px1, py0 + bh * 0.5f);
            bboxRight.sizeDelta = new Vector2(t, bh);
        }
    }

    private bool _lostVisible(FishTrackData d)
    {
        // Show dead-reckoned box if we have any bbox data
        return d.bboxMaxX > d.bboxMinX && d.bboxMaxY > d.bboxMinY;
    }

    private void SetBboxVisible(bool visible)
    {
        RectTransform[] rts = { bboxTop, bboxBottom, bboxLeft, bboxRight };
        foreach (var rt in rts)
            if (rt != null) rt.gameObject.SetActive(visible);
    }

    private void OnDestroy()
    {
        if (_additiveMaterial != null)
            Destroy(_additiveMaterial);
    }
}
