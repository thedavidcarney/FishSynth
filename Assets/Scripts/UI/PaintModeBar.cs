using UnityEngine;
using Shapes;

/// <summary>
/// Horizontal paint mode bar at the bottom of the screen.
/// Shows brush size slider, exit button, and draws brush cursor ring.
/// Only visible when paint mode is active.
/// </summary>
public class PaintModeBar : ImmediateModePanel, IFishPanel
{
    FishSynthUI _ui;
    FishSynthUI UI => _ui != null ? _ui : (_ui = GetComponentInParent<FishSynthUI>());

    [Header("Layout")]
    [SerializeField] float padding = 12f;
    [SerializeField] float labelFontSize = 14f;
    [SerializeField] float smallFontSize = 10f;
    [SerializeField] float exitButtonWidth = 100f;
    [SerializeField] float sliderToExitGap = 40f;
    [SerializeField] float sliderHeight = 20f;
    [SerializeField] float cursorRadiusMultiplier = 1f;

    // Hit zones
    private Rect _sliderRect;
    private Rect _exitRect;
    private Rect _clearRect;

    // Mask preset hit zones
    private Rect _maskLeftRect, _maskRightRect, _maskSaveRect, _maskNewRect, _maskNameRect;
    [HideInInspector] public string currentMaskPreset = "Default";
    private string[] _maskPresetList = new string[0];
    private int _maskPresetIndex = -1;
    private bool _namingMaskPreset;
    private string _maskNameBuffer = "";

    // Drag state
    private bool _draggingSlider;

    public override void DrawPanelShapes(Rect rect, ImCanvasContext ctx)
    {
        if (UI == null || !UI.paintModeActive) return;

        UI.DrawPanelBg(rect);

        var painter = UI.maskPainter;
        if (painter == null) return;

        float y = rect.center.y;
        float left = rect.xMin + padding;
        float right = rect.xMax - padding;

        // ── "PAINT MODE" label ───────────────────────────────────
        UI.SetFontSize(labelFontSize);
        Draw.Color = UI.statusBad;
        Draw.Text(new Vector2(left, y), "PAINT MODE", TextAlign.Left);
        left += 120f;

        // ── Help text ────────────────────────────────────────────
        UI.SetFontSize(smallFontSize);
        Draw.Color = UI.textDim;
        Draw.Text(new Vector2(left, y + 6f), "L-click: paint", TextAlign.Left);
        Draw.Text(new Vector2(left, y - 6f), "R-click: erase", TextAlign.Left);
        left += 100f;

        // ── Exit button (far right) ──────────────────────────────
        Rect exitRect = new Rect(right - exitButtonWidth, rect.yMin + 4f, exitButtonWidth, rect.height - 8f);
        _exitRect = exitRect;
        Draw.Rectangle(exitRect, 4f, new Color(0.8f, 0.2f, 0.2f, 0.3f));
        Draw.RectangleBorder(exitRect, 1.5f, 4f, UI.statusBad);
        UI.SetFontSize(12f);
        Draw.Color = UI.statusBad;
        Draw.Text(exitRect.center, "Exit", TextAlign.Center);

        // ── Clear button (left of exit) ──────────────────────────
        float clearW = 50f;
        Rect clearRect = new Rect(exitRect.xMin - clearW - 6f, rect.yMin + 4f, clearW, rect.height - 8f);
        _clearRect = clearRect;
        Draw.Rectangle(clearRect, 4f, new Color(0.15f, 0.18f, 0.22f, 0.8f));
        Draw.RectangleBorder(clearRect, 1f, 4f, UI.panelBorder);
        UI.SetFontSize(smallFontSize);
        Draw.Color = UI.textColor;
        Draw.Text(clearRect.center, "Clear", TextAlign.Center);

        // ── Mask preset row (left of clear) ──────────────────────
        float presetRight = clearRect.xMin - 10f;
        float arrowW = 18f;
        float pBtnW = 32f;
        float pGap = 2f;
        float pRowH = 18f;
        float presetTotalW = arrowW * 2 + pBtnW * 2 + pGap * 4 + 80f; // 80 = name width
        float presetLeft = presetRight - presetTotalW;

        UI.SetFontSize(smallFontSize);
        Draw.Color = UI.textDim;
        Draw.Text(new Vector2(presetLeft, y + 14f), "MASK PRESET", TextAlign.Left);

        float pcx = presetLeft;
        _maskLeftRect = new Rect(pcx, y - pRowH / 2f, arrowW, pRowH);
        pcx += arrowW + pGap;
        float nameW = 80f;
        _maskNameRect = new Rect(pcx, y - pRowH / 2f, nameW, pRowH);
        pcx += nameW + pGap;
        _maskRightRect = new Rect(pcx, y - pRowH / 2f, arrowW, pRowH);
        pcx += arrowW + pGap;
        _maskSaveRect = new Rect(pcx, y - pRowH / 2f, pBtnW, pRowH);
        pcx += pBtnW + pGap;
        _maskNewRect = new Rect(pcx, y - pRowH / 2f, pBtnW, pRowH);

        DrawSmallButton(_maskLeftRect, "<");
        DrawSmallButton(_maskRightRect, ">");
        DrawSmallButton(_maskSaveRect, "Save");
        DrawSmallButton(_maskNewRect, "+");

        Draw.Rectangle(_maskNameRect, 2f, new Color(0.1f, 0.12f, 0.15f, 0.8f));
        Draw.RectangleBorder(_maskNameRect, 1f, 2f, UI.panelBorder);
        UI.SetFontSize(smallFontSize);
        Draw.Color = UI.textColor;
        if (_namingMaskPreset)
        {
            string cur = (Time.time % 1f < 0.5f) ? "|" : "";
            Draw.Text(_maskNameRect.center, _maskNameBuffer + cur, TextAlign.Center);
        }
        else
        {
            string dispName = currentMaskPreset;
            if (dispName.Length > 10) dispName = dispName.Substring(0, 10) + "..";
            Draw.Text(_maskNameRect.center, dispName, TextAlign.Center);
        }

        // ── Brush size slider (fills space between help and mask preset) ──
        float sliderRight = presetLeft - sliderToExitGap;
        float sliderLeft = left + 10f;

        UI.SetFontSize(smallFontSize);
        Draw.Color = UI.textDim;
        Draw.Text(new Vector2(sliderLeft, y + 14f), "BRUSH SIZE", TextAlign.Left);

        Rect sliderRect = new Rect(sliderLeft, y - 8f, Mathf.Max(sliderRight - sliderLeft, 40f), sliderHeight);
        _sliderRect = sliderRect;

        float trackY = sliderRect.center.y;

        Draw.Line(new Vector2(sliderRect.xMin, trackY), new Vector2(sliderRect.xMax, trackY),
            4f, LineEndCap.Round, new Color(0.12f, 0.14f, 0.16f, 0.9f));

        float t = Mathf.InverseLerp(5f, 200f, painter.brushRadius);
        float fillX = Mathf.Lerp(sliderRect.xMin, sliderRect.xMax, t);
        Draw.Line(new Vector2(sliderRect.xMin, trackY), new Vector2(fillX, trackY),
            4f, LineEndCap.Round, UI.accent);

        Draw.Disc(new Vector2(fillX, trackY), _draggingSlider ? 7f : 6f, Color.white);
        if (_draggingSlider)
            Draw.Ring(new Vector2(fillX, trackY), 8f, 1.5f, UI.accent * 0.5f);

        // Value readout
        UI.SetFontSize(smallFontSize);
        Draw.Color = UI.textColor;
        Draw.Text(new Vector2(sliderRect.xMax + 6f, trackY), $"{painter.brushRadius}", TextAlign.Left);

        // ── Brush cursor ring (drawn on top of everything) ───────
        if (painter.MouseOverVideo && painter.ScreenBrushDiameter > 0)
        {
            DrawBrushCursor(painter);
        }
    }

    void DrawBrushCursor(MaskPainter painter)
    {
        if (painter.videoImage == null) return;

        RectTransform myRT = transform as RectTransform;

        // Use two known screen-space points to measure the scale between
        // screen pixels and this panel's Shapes draw units
        Vector2 screenA = new Vector2(0, 0);
        Vector2 screenB = new Vector2(100, 0);
        var canvas = UI.GetComponent<Canvas>();
        if (canvas == null) return;
        Camera cam = canvas.worldCamera;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            myRT, screenA, cam, out Vector2 localA);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            myRT, screenB, cam, out Vector2 localB);
        float pixelsToLocal = (localB.x - localA.x) / 100f;
        if (Mathf.Abs(pixelsToLocal) < 0.0001f) return;

        // Convert mouse position to panel local space
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            myRT, Input.mousePosition, cam, out Vector2 localPos);

        // Convert screen brush diameter to local units
        float brushRadiusLocal = painter.ScreenBrushDiameter * 0.5f * pixelsToLocal * cursorRadiusMultiplier;

        Draw.Ring(new Vector2(localPos.x, localPos.y), brushRadiusLocal, 1.5f, Color.white);
    }

    void DrawSmallButton(Rect rect, string label)
    {
        Draw.Rectangle(rect, 2f, new Color(0.12f, 0.14f, 0.18f, 0.8f));
        Draw.RectangleBorder(rect, 1f, 2f, UI.panelBorder);
        UI.SetFontSize(smallFontSize);
        Draw.Color = UI.textColor;
        Draw.Text(rect.center, label, TextAlign.Center);
    }

    void RefreshMaskPresetList()
    {
        _maskPresetList = PresetManager.ListMaskPresets();
        _maskPresetIndex = System.Array.IndexOf(_maskPresetList, currentMaskPreset);
    }

    void CycleMaskPreset(int dir)
    {
        if (_maskPresetList.Length == 0) RefreshMaskPresetList();
        if (_maskPresetList.Length == 0) return;
        _maskPresetIndex = ((_maskPresetIndex + dir) % _maskPresetList.Length + _maskPresetList.Length) % _maskPresetList.Length;
        currentMaskPreset = _maskPresetList[_maskPresetIndex];
        var tex = PresetManager.LoadMaskPreset(currentMaskPreset);
        if (tex != null && UI.maskPainter != null)
            UI.maskPainter.LoadFromTexture(tex);
    }

    private void Update()
    {
        if (!_namingMaskPreset) return;
        foreach (char c in Input.inputString)
        {
            if (c == '\b')
            {
                if (_maskNameBuffer.Length > 0)
                    _maskNameBuffer = _maskNameBuffer.Substring(0, _maskNameBuffer.Length - 1);
            }
            else if (c == '\n' || c == '\r')
            {
                if (_maskNameBuffer.Length > 0 && UI.maskPainter != null)
                {
                    currentMaskPreset = _maskNameBuffer;
                    PresetManager.SaveMaskPreset(UI.maskPainter.GetMaskTexture(), currentMaskPreset);
                    RefreshMaskPresetList();
                }
                _namingMaskPreset = false;
            }
            else if (c == 27) { _namingMaskPreset = false; }
            else if (c >= 32) { _maskNameBuffer += c; }
        }
        if (Input.GetKeyDown(KeyCode.Escape)) _namingMaskPreset = false;
    }

    // ── IFishPanel ───────────────────────────────────────────────

    public void OnPress(Vector2 pos)
    {
        if (UI == null) return;
        FishSynthInput.InputConsumed = true;

        // Mask preset naming mode
        if (_namingMaskPreset)
        {
            if (!_maskNameRect.Contains(pos) && _maskNameBuffer.Length > 0 && UI.maskPainter != null)
            {
                currentMaskPreset = _maskNameBuffer;
                PresetManager.SaveMaskPreset(UI.maskPainter.GetMaskTexture(), currentMaskPreset);
                RefreshMaskPresetList();
            }
            _namingMaskPreset = false;
            return;
        }

        if (_exitRect.Contains(pos)) { ExitPaintMode(); return; }
        if (_clearRect.Contains(pos))
        {
            if (UI.maskPainter != null) UI.maskPainter.ClearMask();
            return;
        }

        // Mask preset buttons
        if (_maskLeftRect.Contains(pos)) { CycleMaskPreset(-1); return; }
        if (_maskRightRect.Contains(pos)) { CycleMaskPreset(1); return; }
        if (_maskSaveRect.Contains(pos))
        {
            if (UI.maskPainter != null)
            {
                PresetManager.SaveMaskPreset(UI.maskPainter.GetMaskTexture(), currentMaskPreset);
                RefreshMaskPresetList();
            }
            return;
        }
        if (_maskNewRect.Contains(pos))
        {
            _namingMaskPreset = true;
            _maskNameBuffer = "";
            return;
        }

        if (_sliderRect.Contains(pos))
        {
            _draggingSlider = true;
            ApplySlider(pos);
            return;
        }
    }

    public void OnDrag(Vector2 pos)
    {
        if (_draggingSlider)
        {
            FishSynthInput.InputConsumed = true;
            ApplySlider(pos);
        }
    }

    public void OnRelease(Vector2 pos)
    {
        _draggingSlider = false;
    }

    public void OnScroll(Vector2 pos, float delta) { }

    void ApplySlider(Vector2 pos)
    {
        var painter = UI != null ? UI.maskPainter : null;
        if (painter == null || _sliderRect.width <= 0) return;
        float t = Mathf.InverseLerp(_sliderRect.xMin, _sliderRect.xMax, pos.x);
        t = Mathf.Clamp01(t);
        painter.brushRadius = Mathf.RoundToInt(Mathf.Lerp(5f, 200f, t));
    }

    void ExitPaintMode()
    {
        if (UI == null) return;
        if (UI.maskPainter != null)
            UI.maskPainter.paintingEnabled = false;
        if (UI.debugCanvas != null)
            UI.debugCanvas.SetPaintMode(false);
        UI.paintModeActive = false;
    }
}
