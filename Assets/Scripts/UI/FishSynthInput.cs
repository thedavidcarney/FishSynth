using UnityEngine;

/// <summary>
/// Central input handler for the FishSynth Shapes UI.
/// Converts screen mouse position to canvas-local coordinates and
/// dispatches press/drag/release to panels via the FishSynthUI root.
///
/// Attach to the ShapesOverlay canvas GameObject (same as FishSynthUI).
/// </summary>
public class FishSynthInput : MonoBehaviour
{
    public FishSynthUI ui;
    public FishSynthUILayout layout;

    private Canvas _canvas;
    private RectTransform _canvasRT;
    private Camera _cam;

    // Drag state
    private IFishPanel _activePanel;
    private bool _dragging;

    /// <summary>True when the Shapes UI consumed the current frame's click/drag. Check this to suppress other input handlers.</summary>
    public static bool InputConsumed { get; set; }

    void Awake()
    {
        _canvas = GetComponent<Canvas>();
        _canvasRT = GetComponent<RectTransform>();
    }

    void Start()
    {
        _cam = _canvas.worldCamera;
    }

    void Update()
    {
        InputConsumed = false;
        if (_canvas == null || ui == null || layout == null) return;

        // ── Escape key handling ──────────────────────────────────────
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            // If paint mode is active, exit paint mode first
            if (ui.paintModeActive)
            {
                if (ui.maskPainter != null)
                    ui.maskPainter.paintingEnabled = false;
                if (ui.debugCanvas != null)
                    ui.debugCanvas.SetPaintMode(false);
                ui.paintModeActive = false;
            }
            else
            {
                // Toggle all panels
                ui.panelsVisible = !ui.panelsVisible;
            }
            return;
        }

        // If panels are hidden, skip all panel input processing
        if (!ui.panelsVisible) return;

        Vector2 mousePos = Input.mousePosition;

        // Convert screen position to canvas-local position
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _canvasRT, mousePos, _cam, out Vector2 localPos);

        if (Input.GetMouseButtonDown(0))
        {
            _activePanel = FindPanelAt(localPos);
            if (_activePanel != null)
            {
                Vector2 panelLocal = ToPanelLocal(_activePanel, localPos);
                _activePanel.OnPress(panelLocal);
                _dragging = true;
                InputConsumed = true;
            }
        }
        else if (Input.GetMouseButton(0) && _dragging && _activePanel != null)
        {
            Vector2 panelLocal = ToPanelLocal(_activePanel, localPos);
            _activePanel.OnDrag(panelLocal);
            InputConsumed = true;
        }
        else if (Input.GetMouseButtonUp(0) && _dragging)
        {
            if (_activePanel != null)
            {
                Vector2 panelLocal = ToPanelLocal(_activePanel, localPos);
                _activePanel.OnRelease(panelLocal);
            }
            _activePanel = null;
            _dragging = false;
            InputConsumed = true;
        }

        // Scroll wheel
        float scroll = Input.mouseScrollDelta.y;
        if (scroll != 0f)
        {
            IFishPanel panel = FindPanelAt(localPos);
            if (panel != null)
            {
                Vector2 panelLocal = ToPanelLocal(panel, localPos);
                panel.OnScroll(panelLocal, scroll);
                // InputConsumed is set by the panel if it handled the scroll
            }
        }
    }

    IFishPanel FindPanelAt(Vector2 canvasLocal)
    {
        // In paint mode, only the paint bar is interactive — other panels are hidden
        // and clicks on them should fall through to MaskPainter on the video.
        if (ui.paintModeActive)
        {
            if (HitsPanel(layout.paintModeBar, canvasLocal)) return layout.paintModeBar as IFishPanel;
            return null;
        }

        // Check panels in front-to-back priority order
        if (HitsPanel(layout.statusBarPanel, canvasLocal)) return layout.statusBarPanel as IFishPanel;
        if (HitsPanel(layout.channelPanel, canvasLocal)) return layout.channelPanel as IFishPanel;
        if (HitsPanel(layout.songPanel, canvasLocal)) return layout.songPanel as IFishPanel;
        if (HitsPanel(layout.videoPanel, canvasLocal)) return layout.videoPanel as IFishPanel;
        if (HitsPanel(layout.trackingPanel, canvasLocal)) return layout.trackingPanel as IFishPanel;
        return null;
    }

    bool HitsPanel(Component panel, Vector2 canvasLocal)
    {
        if (panel == null) return false;
        RectTransform rt = panel.GetComponent<RectTransform>();
        Vector2 panelLocal = (Vector2)rt.InverseTransformPoint(_canvasRT.TransformPoint(canvasLocal));

        // Base rect check
        if (rt.rect.Contains(panelLocal)) return true;

        // If a dropdown is open on this panel, expand the hit zone to include it
        if (panel is IFishPanelDropdown dropdown && dropdown.HasOpenDropdown)
        {
            Rect expanded = dropdown.GetDropdownRect();
            if (expanded.width > 0 && expanded.Contains(panelLocal)) return true;
        }

        return false;
    }

    Vector2 ToPanelLocal(IFishPanel panel, Vector2 canvasLocal)
    {
        RectTransform rt = ((MonoBehaviour)panel).GetComponent<RectTransform>();
        return (Vector2)rt.InverseTransformPoint(_canvasRT.TransformPoint(canvasLocal));
    }
}

/// <summary>
/// Interface for panels that handle input.
/// Coordinates are in the panel's local RectTransform space.
/// </summary>
public interface IFishPanel
{
    void OnPress(Vector2 localPos);
    void OnDrag(Vector2 localPos);
    void OnRelease(Vector2 localPos);
    void OnScroll(Vector2 localPos, float delta);
}

/// <summary>
/// Optional interface for panels with dropdowns that extend beyond panel bounds.
/// When HasOpenDropdown is true, the input system expands the hit zone to include
/// GetDropdownRect() so clicks on the dropdown area are routed to this panel.
/// </summary>
public interface IFishPanelDropdown
{
    bool HasOpenDropdown { get; }
    Rect GetDropdownRect();
}
