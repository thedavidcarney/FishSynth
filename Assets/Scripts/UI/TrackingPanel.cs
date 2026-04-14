using UnityEngine;
using Shapes;

/// <summary>
/// Tracking & Vision panel (left side): HSV color strips with draggable range handles,
/// morphology/tracking knobs with drag-to-adjust, preset selector, and mask paint toggle.
/// </summary>
public class TrackingPanel : ImmediateModePanel, IFishPanel
{
    FishSynthUI _ui;
    FishSynthUI UI => _ui != null ? _ui : (_ui = GetComponentInParent<FishSynthUI>());

    // Preset system
    [HideInInspector] public string currentPresetName = "Default";
    private string[] _presetList = new string[0];
    private int _presetIndex = -1;
    private bool _namingPreset;
    private string _nameBuffer = "";
    private Rect _presetLeftRect, _presetRightRect, _presetSaveRect, _presetNewRect, _presetNameRect;

    // ── Layout variables (serialized) ─────────────────────────────────────────
    [Header("Layout")]
    [SerializeField] float padding = 12f;
    [SerializeField] float headerFontSize = 16f;
    [SerializeField] float headerGap = 28f;
    [SerializeField] float labelFontSize = 10f;
    [SerializeField] float knobSize = 52f;
    [SerializeField] float knobLabelFontSize = 9f;
    [SerializeField] float knobValueFontSize = 9f;
    [SerializeField] float stripHeight = 28f;
[SerializeField] float stripGap = 8f;
    [SerializeField] float sectionGap = 16f;
    [SerializeField] float labelToKnobGap = 14f;
    [SerializeField] float knobRowGap = 24f;
    [SerializeField] float trackingKnobSpacing = 10f;
    [SerializeField] float cleanupKnobSpacing = 10f;
    [SerializeField] float maskButtonHeight = 36f;

    // ── Hit zones (updated each draw) ──────────────────────────────────────────
    private Rect _maskBtnRect;

    // Knob hit zones + values
    struct KnobZone
    {
        public Vector2 center;
        public float radius;
        public float min, max;
        public System.Action<float> setter;
        public System.Func<float> getter;
    }
    private KnobZone[] _knobs = new KnobZone[7];
    private int _knobCount;

    // HSV strip hit zones
    struct StripZone
    {
        public Rect rect;
        public System.Action<float> setMin;
        public System.Action<float> setMax;
        public System.Func<float> getMin;
        public System.Func<float> getMax;
    }
    private StripZone[] _strips = new StripZone[3];

    // Drag state
    enum DragTarget { None, Knob, StripMin, StripMax }
    private DragTarget _dragTarget;
    private int _dragIndex;
    private float _dragStartValue;
    private Vector2 _dragStartPos;

    private void Update()
    {
        if (!_namingPreset) return;
        foreach (char c in Input.inputString)
        {
            if (c == '\b')
            {
                if (_nameBuffer.Length > 0)
                    _nameBuffer = _nameBuffer.Substring(0, _nameBuffer.Length - 1);
            }
            else if (c == '\n' || c == '\r')
            {
                if (_nameBuffer.Length > 0)
                {
                    currentPresetName = _nameBuffer;
                    PresetManager.SaveTrackingPreset(UI.tracker, currentPresetName);
                    RefreshPresetList();
                }
                _namingPreset = false;
            }
            else if (c == 27) { _namingPreset = false; }
            else if (c >= 32) { _nameBuffer += c; }
        }
        if (Input.GetKeyDown(KeyCode.Escape)) _namingPreset = false;
    }

    public override void DrawPanelShapes(Rect rect, ImCanvasContext ctx)
    {
        if (UI == null || UI.tracker == null) return;
        if (UI.paintModeActive) return;

        UI.DrawPanelBg(rect);
        _knobCount = 0;

        var t = UI.tracker;
        float x = rect.xMin + padding;
        float top = rect.yMax - padding;
        float w = rect.width - padding * 2;

        // ── Header ───────────────────────────────────────────────
        UI.SetFontSize(headerFontSize);
        Draw.Color = UI.textColor;
        Draw.Text(new Vector2(x, top), "Tracking", TextAlign.TopLeft);
        top -= headerGap;

        // ── Preset row: [◀] Name [▶] [Save] [+] ─────────────────
        top -= 2f;
        float rowH = 22f;
        float arrowW = 20f;
        float btnW = 36f;
        float gap = 3f;
        float nameW = w - arrowW * 2 - btnW * 2 - gap * 4;

        float cx = x;
        Rect leftArr = new Rect(cx, top - rowH, arrowW, rowH);
        _presetLeftRect = leftArr;
        cx += arrowW + gap;

        Rect nameRect = new Rect(cx, top - rowH, nameW, rowH);
        _presetNameRect = nameRect;
        cx += nameW + gap;

        Rect rightArr = new Rect(cx, top - rowH, arrowW, rowH);
        _presetRightRect = rightArr;
        cx += arrowW + gap;

        Rect saveBtn = new Rect(cx, top - rowH, btnW, rowH);
        _presetSaveRect = saveBtn;
        cx += btnW + gap;

        Rect newBtn = new Rect(cx, top - rowH, btnW, rowH);
        _presetNewRect = newBtn;

        // Arrows
        DrawPresetButton(leftArr, "<");
        DrawPresetButton(rightArr, ">");
        DrawPresetButton(saveBtn, "Save");
        DrawPresetButton(newBtn, "+");

        // Name display / edit
        Draw.Rectangle(nameRect, 3f, new Color(0.1f, 0.12f, 0.15f, 0.8f));
        Draw.RectangleBorder(nameRect, 1f, 3f, UI.panelBorder);
        UI.SetFontSize(10f);
        Draw.Color = UI.textColor;
        if (_namingPreset)
        {
            string cursor = (Time.time % 1f < 0.5f) ? "|" : "";
            Draw.Text(nameRect.center, _nameBuffer + cursor, TextAlign.Center);
        }
        else
        {
            Draw.Text(nameRect.center, currentPresetName, TextAlign.Center);
        }

        top -= rowH + 10f;

        // ── HSV Color Strips ─────────────────────────────────────
        UI.SetFontSize(labelFontSize);
        Draw.Color = UI.textDim;
        Draw.Text(new Vector2(x + w / 2f, top), "COLOR FILTER", TextAlign.Top);
        top -= labelToKnobGap;

        top = DrawHueStrip(x, top, w, stripHeight, t.hueMin, t.hueMax, 0);
        top -= stripGap;
        top = DrawSatStrip(x, top, w, stripHeight, t.satMin, t.satMax, 1);
        top -= stripGap;
        top = DrawValStrip(x, top, w, stripHeight, t.valMin, t.valMax, 2);
        top -= sectionGap;

        // ── Morphology Knobs ─────────────────────────────────────
        UI.SetFontSize(labelFontSize);
        Draw.Color = UI.textDim;
        Draw.Text(new Vector2(x + w / 2f, top), "CLEANUP", TextAlign.Top);
        top -= labelToKnobGap;

        float knobR = knobSize / 2f;
        float row3W = knobSize * 3 + cleanupKnobSpacing * 2;
        float row3X = x + (w - row3W) / 2f;

        RegisterKnob(row3X + knobR, top - knobR, knobR, "Erode 1", t.erode1Radius, 1, 20,
            v => t.erode1Radius = Mathf.RoundToInt(v), () => t.erode1Radius);
        RegisterKnob(row3X + knobSize + cleanupKnobSpacing + knobR, top - knobR, knobR, "Dilate", t.dilateRadius, 1, 40,
            v => t.dilateRadius = Mathf.RoundToInt(v), () => t.dilateRadius);
        RegisterKnob(row3X + (knobSize + cleanupKnobSpacing) * 2 + knobR, top - knobR, knobR, "Erode 2", t.erode2Radius, 1, 20,
            v => t.erode2Radius = Mathf.RoundToInt(v), () => t.erode2Radius);
        top -= knobSize + knobRowGap;

        // ── Tracking Knobs (2x2 centered) ───────────────────────
        UI.SetFontSize(labelFontSize);
        Draw.Color = UI.textDim;
        Draw.Text(new Vector2(x + w / 2f, top), "TRACKING", TextAlign.Top);
        top -= labelToKnobGap;

        float row2W = knobSize * 2 + trackingKnobSpacing;
        float rowOffsetX = x + (w - row2W) / 2f;

        RegisterKnob(rowOffsetX + knobR, top - knobR, knobR, "Sensitivity", t.minBlobPixels, 10, 500,
            v => t.minBlobPixels = Mathf.RoundToInt(v), () => t.minBlobPixels);
        RegisterKnob(rowOffsetX + knobSize + trackingKnobSpacing + knobR, top - knobR, knobR, "Position Smooth", t.positionSmoothing, 0f, 1f,
            v => t.positionSmoothing = v, () => t.positionSmoothing);
        top -= knobSize + knobRowGap;

        RegisterKnob(rowOffsetX + knobR, top - knobR, knobR, "Velocity Smooth", t.velocitySmoothing, 0f, 1f,
            v => t.velocitySmoothing = v, () => t.velocitySmoothing);
        RegisterKnob(rowOffsetX + knobSize + trackingKnobSpacing + knobR, top - knobR, knobR, "Dead Reckon", t.deadReckonDuration, 0f, 2f,
            v => t.deadReckonDuration = v, () => t.deadReckonDuration);
        top -= knobSize + knobRowGap;

        // ── Mask Paint Toggle ────────────────────────────────────
        Rect maskBtnRect = new Rect(x, top - maskButtonHeight, w, maskButtonHeight);
        _maskBtnRect = maskBtnRect;
        Color maskBg = new Color(0.15f, 0.15f, 0.18f, 0.8f);
        Draw.Rectangle(maskBtnRect, 4f, maskBg);
        Draw.RectangleBorder(maskBtnRect, 1f, 4f, UI.panelBorder);

        // Paintbrush icon
        float bx = maskBtnRect.xMin + 16f;
        float by = maskBtnRect.center.y;
        Draw.Line(new Vector2(bx - 4f, by - 4f), new Vector2(bx + 4f, by + 4f), 2f, LineEndCap.Round, UI.textDim);
        Draw.Disc(new Vector2(bx + 4f, by + 4f), 2.5f, UI.textDim);

        UI.SetFontSize(11f);
        Draw.Color = UI.textColor;
        Draw.Text(new Vector2(maskBtnRect.center.x + 8f, by), "Paint Exclusion Mask", TextAlign.Left);
    }

    void DrawPresetButton(Rect rect, string label)
    {
        Draw.Rectangle(rect, 3f, new Color(0.12f, 0.14f, 0.18f, 0.8f));
        Draw.RectangleBorder(rect, 1f, 3f, UI.panelBorder);
        UI.SetFontSize(10f);
        Draw.Color = UI.textColor;
        Draw.Text(rect.center, label, TextAlign.Center);
    }

    void RefreshPresetList()
    {
        _presetList = PresetManager.ListTrackingPresets();
        _presetIndex = System.Array.IndexOf(_presetList, currentPresetName);
    }

    void CyclePreset(int dir)
    {
        if (_presetList.Length == 0) { RefreshPresetList(); }
        if (_presetList.Length == 0) return;
        _presetIndex = ((_presetIndex + dir) % _presetList.Length + _presetList.Length) % _presetList.Length;
        currentPresetName = _presetList[_presetIndex];
        PresetManager.LoadTrackingPreset(UI.tracker, currentPresetName);
    }

    // ── Knob registration + drawing ────────────────────────────────────────────

    void RegisterKnob(float cx, float cy, float radius, string label, float value, float min, float max,
        System.Action<float> setter, System.Func<float> getter)
    {
        if (_knobCount < _knobs.Length)
        {
            _knobs[_knobCount] = new KnobZone
            {
                center = new Vector2(cx, cy),
                radius = radius,
                min = min, max = max,
                setter = setter, getter = getter
            };
            _knobCount++;
        }
        DrawKnob(cx, cy, radius, label, value, min, max);
    }

    void DrawKnob(float cx, float cy, float radius, string label, float value, float min, float max)
    {
        float t = Mathf.InverseLerp(min, max, value);
        t = Mathf.Clamp01(t);

        float startAngle = 225f;
        float sweepAngle = 270f;
        float endAngle = startAngle - sweepAngle;
        float innerR = radius * 0.55f;

        // Background arc
        Draw.Arc(new Vector2(cx, cy), radius, innerR,
            startAngle * Mathf.Deg2Rad, endAngle * Mathf.Deg2Rad,
            new Color(0.12f, 0.14f, 0.16f, 0.9f));

        // Value arc
        float valueAngle = startAngle - sweepAngle * t;
        if (t > 0.01f)
        {
            Draw.Arc(new Vector2(cx, cy), radius, innerR,
                startAngle * Mathf.Deg2Rad, valueAngle * Mathf.Deg2Rad,
                UI.accent);
        }

        // Pointer dot
        float dotAngle = valueAngle * Mathf.Deg2Rad;
        float dotR = (radius + innerR) / 2f;
        Vector2 dotPos = new Vector2(cx + Mathf.Cos(dotAngle) * dotR, cy + Mathf.Sin(dotAngle) * dotR);
        Draw.Disc(dotPos, 4f, Color.white);

        // Highlight if being dragged
        bool dragging = _dragTarget == DragTarget.Knob && _dragIndex < _knobCount && _knobs[_dragIndex].center == new Vector2(cx, cy);
        if (dragging)
        {
            Draw.Ring(new Vector2(cx, cy), radius + 2f, 1.5f, UI.accent * 0.5f);
        }

        // Label below
        UI.SetFontSize(knobLabelFontSize);
        Draw.Color = UI.textDim;
        Draw.Text(new Vector2(cx, cy - radius - 8f), label, TextAlign.Top);

        // Value readout
        string valText = value < 1f ? $"{value:F2}" : $"{value:F0}";
        UI.SetFontSize(knobValueFontSize);
        Draw.Color = UI.textColor;
        Draw.Text(new Vector2(cx, cy - 2f), valText, TextAlign.Center);
    }

    // ── HSV Strip Drawers ────────────────────────────────────────────────────────

    float DrawHueStrip(float x, float top, float w, float h, float min, float max, int stripIdx)
    {
        Rect stripRect = new Rect(x, top - h, w, h);
        _strips[stripIdx] = new StripZone
        {
            rect = stripRect,
            setMin = v => UI.tracker.hueMin = Mathf.Clamp01(v),
            setMax = v => UI.tracker.hueMax = Mathf.Clamp01(v),
            getMin = () => UI.tracker.hueMin,
            getMax = () => UI.tracker.hueMax
        };

        // Rainbow gradient
        int segments = 48;
        float segW = w / segments;
        for (int i = 0; i < segments; i++)
        {
            float t0 = (float)i / segments;
            float t1 = (float)(i + 1) / segments;
            Color c0 = Color.HSVToRGB(t0, 1f, 1f);
            Color c1 = Color.HSVToRGB(t1, 1f, 1f);
            Draw.Rectangle(new Rect(x + i * segW, top - h, segW + 0.5f, h), Color.Lerp(c0, c1, 0.5f));
        }

        // Dim outside range
        if (min > 0f)
            Draw.Rectangle(new Rect(x, top - h, w * min, h), new Color(0f, 0f, 0f, 0.7f));
        if (max < 1f)
            Draw.Rectangle(new Rect(x + w * max, top - h, w - w * max, h), new Color(0f, 0f, 0f, 0.7f));

        // Range handles (thick, draggable)
        float handleW = 4f;
        bool draggingMin = _dragTarget == DragTarget.StripMin && _dragIndex == stripIdx;
        bool draggingMax = _dragTarget == DragTarget.StripMax && _dragIndex == stripIdx;

        Color minHandleCol = draggingMin ? UI.accent : Color.white;
        Color maxHandleCol = draggingMax ? UI.accent : Color.white;
        Draw.Line(new Vector2(x + w * min, top + 2f), new Vector2(x + w * min, top - h - 2f), handleW, minHandleCol);
        Draw.Line(new Vector2(x + w * max, top + 2f), new Vector2(x + w * max, top - h - 2f), handleW, maxHandleCol);

        // Handle grab triangles (visual affordance)
        float triSize = 5f;
        Draw.Triangle(
            new Vector3(x + w * min - triSize, top + 3f, 0f),
            new Vector3(x + w * min + triSize, top + 3f, 0f),
            new Vector3(x + w * min, top - 2f, 0f),
            minHandleCol);
        Draw.Triangle(
            new Vector3(x + w * max - triSize, top + 3f, 0f),
            new Vector3(x + w * max + triSize, top + 3f, 0f),
            new Vector3(x + w * max, top - 2f, 0f),
            maxHandleCol);

        // Label
        UI.SetFontSize(knobLabelFontSize);
        Draw.Color = UI.textDim;
        Draw.Text(new Vector2(x + 4f, top - h / 2f), "H", TextAlign.Left);

        return top - h;
    }

    float DrawSatStrip(float x, float top, float w, float h, float min, float max, int stripIdx)
    {
        Rect stripRect = new Rect(x, top - h, w, h);
        _strips[stripIdx] = new StripZone
        {
            rect = stripRect,
            setMin = v => UI.tracker.satMin = Mathf.Clamp01(v),
            setMax = v => UI.tracker.satMax = Mathf.Clamp01(v),
            getMin = () => UI.tracker.satMin,
            getMax = () => UI.tracker.satMax
        };

        int segments = 16;
        float segW = w / segments;
        for (int i = 0; i < segments; i++)
        {
            float t = (float)i / segments;
            Color c = Color.HSVToRGB(0.15f, t, 1f);
            Draw.Rectangle(new Rect(x + i * segW, top - h, segW + 0.5f, h), c);
        }

        if (min > 0f)
            Draw.Rectangle(new Rect(x, top - h, w * min, h), new Color(0f, 0f, 0f, 0.7f));
        if (max < 1f)
            Draw.Rectangle(new Rect(x + w * max, top - h, w - w * max, h), new Color(0f, 0f, 0f, 0.7f));

        bool draggingMin = _dragTarget == DragTarget.StripMin && _dragIndex == stripIdx;
        bool draggingMax = _dragTarget == DragTarget.StripMax && _dragIndex == stripIdx;
        Draw.Line(new Vector2(x + w * min, top + 2f), new Vector2(x + w * min, top - h - 2f), 4f,
            draggingMin ? UI.accent : Color.white);
        Draw.Line(new Vector2(x + w * max, top + 2f), new Vector2(x + w * max, top - h - 2f), 4f,
            draggingMax ? UI.accent : Color.white);

        UI.SetFontSize(knobLabelFontSize);
        Draw.Color = UI.textDim;
        Draw.Text(new Vector2(x + 4f, top - h / 2f), "S", TextAlign.Left);

        return top - h;
    }

    float DrawValStrip(float x, float top, float w, float h, float min, float max, int stripIdx)
    {
        Rect stripRect = new Rect(x, top - h, w, h);
        _strips[stripIdx] = new StripZone
        {
            rect = stripRect,
            setMin = v => UI.tracker.valMin = Mathf.Clamp01(v),
            setMax = v => UI.tracker.valMax = Mathf.Clamp01(v),
            getMin = () => UI.tracker.valMin,
            getMax = () => UI.tracker.valMax
        };

        int segments = 16;
        float segW = w / segments;
        for (int i = 0; i < segments; i++)
        {
            float t = (float)i / segments;
            Color c = Color.HSVToRGB(0.15f, 0.8f, t);
            Draw.Rectangle(new Rect(x + i * segW, top - h, segW + 0.5f, h), c);
        }

        if (min > 0f)
            Draw.Rectangle(new Rect(x, top - h, w * min, h), new Color(0f, 0f, 0f, 0.7f));
        if (max < 1f)
            Draw.Rectangle(new Rect(x + w * max, top - h, w - w * max, h), new Color(0f, 0f, 0f, 0.7f));

        bool draggingMin = _dragTarget == DragTarget.StripMin && _dragIndex == stripIdx;
        bool draggingMax = _dragTarget == DragTarget.StripMax && _dragIndex == stripIdx;
        Draw.Line(new Vector2(x + w * min, top + 2f), new Vector2(x + w * min, top - h - 2f), 4f,
            draggingMin ? UI.accent : Color.white);
        Draw.Line(new Vector2(x + w * max, top + 2f), new Vector2(x + w * max, top - h - 2f), 4f,
            draggingMax ? UI.accent : Color.white);

        UI.SetFontSize(knobLabelFontSize);
        Draw.Color = UI.textDim;
        Draw.Text(new Vector2(x + 4f, top - h / 2f), "V", TextAlign.Left);

        return top - h;
    }

    // ── IFishPanel ───────────────────────────────────────────────────────────────

    public void OnPress(Vector2 pos)
    {
        if (UI == null || UI.tracker == null) return;
        FishSynthInput.InputConsumed = true;

        // Preset buttons
        if (_namingPreset)
        {
            // Click outside name field commits
            if (!_presetNameRect.Contains(pos) && _nameBuffer.Length > 0)
            {
                currentPresetName = _nameBuffer;
                PresetManager.SaveTrackingPreset(UI.tracker, currentPresetName);
                RefreshPresetList();
                _namingPreset = false;
            }
            else if (!_presetNameRect.Contains(pos))
            {
                _namingPreset = false;
            }
            return;
        }

        if (_presetLeftRect.Contains(pos)) { CyclePreset(-1); return; }
        if (_presetRightRect.Contains(pos)) { CyclePreset(1); return; }
        if (_presetSaveRect.Contains(pos))
        {
            PresetManager.SaveTrackingPreset(UI.tracker, currentPresetName);
            RefreshPresetList();
            return;
        }
        if (_presetNewRect.Contains(pos))
        {
            _namingPreset = true;
            _nameBuffer = "";
            return;
        }

        // Check mask paint toggle / exit button
        if (_maskBtnRect.Contains(pos))
        {
            var painter = UI.maskPainter;
            if (painter != null)
            {
                if (UI.paintModeActive)
                {
                    // Exit paint mode
                    painter.paintingEnabled = false;
                    UI.paintModeActive = false;
                    if (UI.debugCanvas != null)
                        UI.debugCanvas.SetPaintMode(false);
                }
                else
                {
                    // Enter paint mode
                    painter.paintingEnabled = true;
                    UI.paintModeActive = true;
                    if (UI.debugCanvas != null)
                        UI.debugCanvas.SetPaintMode(true);
                }
            }
            return;
        }

        // In paint mode, don't process other clicks
        if (UI.paintModeActive) return;

        // Check knob hits
        for (int i = 0; i < _knobCount; i++)
        {
            var k = _knobs[i];
            if (Vector2.Distance(pos, k.center) <= k.radius + 6f)
            {
                _dragTarget = DragTarget.Knob;
                _dragIndex = i;
                _dragStartValue = k.getter();
                _dragStartPos = pos;
                return;
            }
        }

        // Check HSV strip handle hits (grab zone = 20px around the handle line)
        for (int i = 0; i < 3; i++)
        {
            var s = _strips[i];
            if (s.rect.width <= 0) continue;

            float stripW = s.rect.width;
            float stripX = s.rect.xMin;
            float expandedY = s.rect.yMin - 8f;
            float expandedH = s.rect.height + 16f;
            Rect expandedRect = new Rect(s.rect.xMin - 10f, expandedY, s.rect.width + 20f, expandedH);

            if (!expandedRect.Contains(pos)) continue;

            float minHandleX = stripX + stripW * s.getMin();
            float maxHandleX = stripX + stripW * s.getMax();
            float grabThreshold = 14f;

            // Check max handle first (so it wins when they overlap)
            if (Mathf.Abs(pos.x - maxHandleX) < grabThreshold)
            {
                _dragTarget = DragTarget.StripMax;
                _dragIndex = i;
                _dragStartPos = pos;
                return;
            }
            if (Mathf.Abs(pos.x - minHandleX) < grabThreshold)
            {
                _dragTarget = DragTarget.StripMin;
                _dragIndex = i;
                _dragStartPos = pos;
                return;
            }
        }
    }

    public void OnDrag(Vector2 pos)
    {
        if (_dragTarget == DragTarget.None) return;
        FishSynthInput.InputConsumed = true;

        if (_dragTarget == DragTarget.Knob && _dragIndex < _knobCount)
        {
            var k = _knobs[_dragIndex];
            // Vertical drag: up = increase, sensitivity scaled to knob range
            float dy = pos.y - _dragStartPos.y;
            float sensitivity = (k.max - k.min) / (k.radius * 4f);
            float newVal = _dragStartValue + dy * sensitivity;
            newVal = Mathf.Clamp(newVal, k.min, k.max);
            k.setter(newVal);
        }
        else if (_dragTarget == DragTarget.StripMin || _dragTarget == DragTarget.StripMax)
        {
            var s = _strips[_dragIndex];
            float t = Mathf.InverseLerp(s.rect.xMin, s.rect.xMax, pos.x);
            t = Mathf.Clamp01(t);

            if (_dragTarget == DragTarget.StripMin)
            {
                float maxVal = s.getMax();
                s.setMin(Mathf.Min(t, maxVal - 0.01f));
            }
            else
            {
                float minVal = s.getMin();
                s.setMax(Mathf.Max(t, minVal + 0.01f));
            }
        }
    }

    public void OnRelease(Vector2 pos)
    {
        if (_dragTarget != DragTarget.None)
        {
            _dragTarget = DragTarget.None;
            // Save config after adjustment
            if (UI != null && UI.tracker != null)
                UI.tracker.SaveConfig();
        }
    }

    public void OnScroll(Vector2 pos, float delta)
    {
        if (UI == null || UI.tracker == null) return;

        // Check if scrolling over a knob — fine-tune it
        for (int i = 0; i < _knobCount; i++)
        {
            var k = _knobs[i];
            if (Vector2.Distance(pos, k.center) <= k.radius + 6f)
            {
                FishSynthInput.InputConsumed = true;
                float step = (k.max - k.min) * 0.03f;
                float newVal = k.getter() + delta * step;
                newVal = Mathf.Clamp(newVal, k.min, k.max);
                k.setter(newVal);
                return;
            }
        }
    }

}
