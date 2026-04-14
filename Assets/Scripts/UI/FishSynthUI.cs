using UnityEngine;
using Shapes;
using TMPro;

/// <summary>
/// Root Shapes canvas for the FishSynth UI. Draws all panels on top of the
/// video feed in Screen Space Overlay mode.
///
/// Attach to the same GameObject as the Canvas component, along with
/// ImmediateModeCanvas (added automatically if missing).
/// Child GameObjects with ImmediateModePanel subclasses are drawn automatically.
/// </summary>
[ExecuteAlways]
public class FishSynthUI : ImmediateModeCanvas
{
    [Header("References")]
    public YellowFishTracker tracker;
    public FishMidiOutput midiOutput;
    public VideoFileInput videoInput;
    public VideoSourceManager videoSourceManager;
    public MaskPainter maskPainter;
    public FishDebugCanvas debugCanvas;

    [Header("Style")]
    [Tooltip("Background color for panels (semi-transparent dark).")]
    public Color panelBackground = new Color(0.05f, 0.07f, 0.1f, 0.82f);

    [Tooltip("Panel border color.")]
    public Color panelBorder = new Color(0.3f, 0.4f, 0.5f, 0.6f);

    [Tooltip("Accent color for active/highlighted elements.")]
    public Color accent = new Color(0.2f, 0.8f, 1f, 1f);

    [Tooltip("Accent color for note/musical elements.")]
    public Color accentNote = new Color(1f, 0.6f, 0.2f, 1f);

    [Tooltip("Text color.")]
    public Color textColor = new Color(0.9f, 0.92f, 0.95f, 1f);

    [Tooltip("Dimmed text color for labels and secondary info.")]
    public Color textDim = new Color(0.5f, 0.55f, 0.6f, 1f);

    [Tooltip("Positive/tracking indicator color.")]
    public Color statusGood = new Color(0.2f, 0.9f, 0.4f, 1f);

    [Tooltip("Warning/dead-reckoning indicator color.")]
    public Color statusWarn = new Color(1f, 0.7f, 0.15f, 1f);

    [Tooltip("Error/lost indicator color.")]
    public Color statusBad = new Color(1f, 0.25f, 0.2f, 1f);

    [Tooltip("Panel corner radius.")]
    public float cornerRadius = 8f;

    [Tooltip("Panel border thickness.")]
    public float borderThickness = 1.5f;

    [Tooltip("Font size multiplier. Shapes canvas text uses large units (~10-20x pixel size).")]
    public float fontScale = 16f;

    [Header("Font")]
    [Tooltip("TMP font for all UI text. If null, uses TMP default.")]
    public TMP_FontAsset font;

    // ── Global MIDI mute ──────────────────────────────────────────────────────

    [HideInInspector]
    public bool midiMuted = false;

    // ── Panel visibility (toggled by Escape) ─────────────────────────────────

    [HideInInspector]
    public bool panelsVisible = true;

    // ── Paint mode flag ──────────────────────────────────────────────────────

    [HideInInspector]
    public bool paintModeActive = false;

    // ── Shared drawing helpers ────────────────────────────────────────────────

    public override void DrawCanvasShapes(ImCanvasContext ctx)
    {
        // Ensure we have a font for text rendering
        if (font == null)
            font = TMP_Settings.defaultFontAsset;
        if (font == null)
            font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        if (font != null)
            Draw.Font = font;
        else if (Time.frameCount % 300 == 0)
            Debug.LogWarning("[FishSynthUI] No TMP font found! Text will not render.");

        // If panels are hidden, don't draw anything (unless paint mode — show paint bar)
        if (!panelsVisible && !paintModeActive)
            return;

        // Draw all child ImmediateModePanel objects
        // (individual panels check paintModeActive to hide/show themselves)
        base.DrawPanels();
    }

    /// <summary>Draw a standard panel background with border.</summary>
    public void DrawPanelBg(Rect rect)
    {
        Draw.Rectangle(rect, cornerRadius, panelBackground);
        Draw.RectangleBorder(rect, borderThickness, cornerRadius, panelBorder);
    }

    /// <summary>Set font size scaled for Shapes canvas coordinate system.</summary>
    public void SetFontSize(float size)
    {
        Draw.FontSize = size * fontScale;
    }

    /// <summary>Draw a section header label inside a panel.</summary>
    public void DrawSectionLabel(Vector2 pos, string text, float fontSize = 14f)
    {
        Draw.FontSize = fontSize * fontScale;
        Draw.Color = textDim;
        Draw.Text(pos, text, TextAlign.TopLeft);
    }
}
