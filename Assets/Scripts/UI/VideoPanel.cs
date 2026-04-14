using System.Collections.Generic;
using UnityEngine;
using Shapes;

/// <summary>
/// Video source panel (right side, below Song Settings): device list with
/// immediate switching, file browsing, downsample selector, and playback controls.
/// </summary>
public class VideoPanel : ImmediateModePanel, IFishPanel
{
    FishSynthUI _ui;
    FishSynthUI UI => _ui != null ? _ui : (_ui = GetComponentInParent<FishSynthUI>());

    [Header("Layout")]
    [SerializeField] float padding = 12f;
    [SerializeField] float headerFontSize = 16f;
    [SerializeField] float labelFontSize = 10f;
    [SerializeField] float itemFontSize = 11f;
    [SerializeField] float itemHeight = 22f;
    [SerializeField] float sectionGap = 14f;
    [SerializeField] float buttonHeight = 28f;
    [SerializeField] float knobSize = 48f;
    [SerializeField] float knobLabelFontSize = 9f;
    [SerializeField] float knobValueFontSize = 9f;

    // Hit zones
    private Rect _refreshRect;
    private List<Rect> _sourceRects = new List<Rect>();
    private List<VideoSourceInfo> _sources = new List<VideoSourceInfo>();
    private Rect[] _downsampleRects = new Rect[4]; // 1x, 2x, 3x, 4x
    private Rect _browseRect;

    // Webcam mode hit zones
    private List<Rect> _modeRects = new List<Rect>();
    private List<Vector3Int> _modeValues = new List<Vector3Int>(); // w, h, fps

    // Playback hit zones
    private Rect _playRect;
    private Rect _pauseRect;
    private Rect _stopRect;
    private Rect _loopRect;

    // Speed knob
    struct KnobZone
    {
        public Vector2 center;
        public float radius;
        public float min, max;
        public System.Action<float> setter;
        public System.Func<float> getter;
    }
    private KnobZone _speedKnob;
    private bool _hasSpeedKnob;

    // Drag state
    private bool _draggingKnob;
    private float _dragStartValue;
    private Vector2 _dragStartPos;

    // Device list refresh
    private float _lastRefreshTime;

    public override void OnEnable()
    {
        base.OnEnable();
        RefreshSources();
    }

    void RefreshSources()
    {
        var mgr = UI != null ? UI.videoSourceManager : null;
        if (mgr != null)
            _sources = mgr.GetAvailableSources();
        else
            _sources.Clear();
        _lastRefreshTime = Time.time;
    }

    public override void DrawPanelShapes(Rect rect, ImCanvasContext ctx)
    {
        if (UI == null) return;
        if (UI.paintModeActive) return;
        UI.DrawPanelBg(rect);

        var mgr = UI.videoSourceManager;
        var vfi = UI.videoInput;

        float x = rect.xMin + padding;
        float top = rect.yMax - padding;
        float w = rect.width - padding * 2;
        _hasSpeedKnob = false;

        // ── Header + Refresh ─────────────────────────────────────
        UI.SetFontSize(headerFontSize);
        Draw.Color = UI.textColor;
        Draw.Text(new Vector2(x, top), "Video", TextAlign.TopLeft);

        // Refresh button (right side of header)
        float refreshSize = 18f;
        Rect refreshRect = new Rect(x + w - refreshSize, top - refreshSize + 2f, refreshSize, refreshSize);
        _refreshRect = refreshRect;
        Draw.Color = UI.textDim;
        // Circular arrow icon
        float rcx = refreshRect.center.x, rcy = refreshRect.center.y;
        float rr = 6f;
        Draw.Arc(new Vector2(rcx, rcy), rr, rr - 1.5f,
            30f * Mathf.Deg2Rad, 330f * Mathf.Deg2Rad, UI.textDim);
        // Arrow tip
        float tipAngle = 30f * Mathf.Deg2Rad;
        Vector2 tipPos = new Vector2(rcx + Mathf.Cos(tipAngle) * rr, rcy + Mathf.Sin(tipAngle) * rr);
        Draw.Triangle(
            new Vector3(tipPos.x - 3f, tipPos.y + 1f, 0f),
            new Vector3(tipPos.x + 2f, tipPos.y + 3f, 0f),
            new Vector3(tipPos.x + 1f, tipPos.y - 3f, 0f),
            UI.textDim);

        top -= 26f;

        // ── Source list ──────────────────────────────────────────
        UI.SetFontSize(labelFontSize);
        Draw.Color = UI.textDim;
        Draw.Text(new Vector2(x, top), "SOURCES", TextAlign.TopLeft);
        top -= 14f;

        _sourceRects.Clear();
        string activeSource = mgr != null ? mgr.ActiveSourceName : "";

        // Draw device items (skip "Browse File..." — we draw it separately)
        for (int i = 0; i < _sources.Count; i++)
        {
            var src = _sources[i];
            bool isBrowse = src.type == VideoSourceType.VideoFile && string.IsNullOrEmpty(src.identifier);
            if (isBrowse)
            {
                _sourceRects.Add(Rect.zero); // placeholder
                continue;
            }

            Rect itemRect = new Rect(x, top - itemHeight, w, itemHeight);
            _sourceRects.Add(itemRect);

            bool isActive = !string.IsNullOrEmpty(activeSource) && src.name == activeSource;

            if (isActive)
            {
                Draw.Rectangle(itemRect, 3f, UI.accent * 0.2f);
                Draw.RectangleBorder(itemRect, 1f, 3f, UI.accent * 0.4f);
            }
            else
            {
                Draw.Rectangle(itemRect, 3f, new Color(0.1f, 0.12f, 0.15f, 0.6f));
            }

            // Icon
            float iconX = itemRect.xMin + 10f;
            float iconY = itemRect.center.y;

            if (src.type == VideoSourceType.Webcam)
            {
                Draw.Color = isActive ? UI.accent : UI.textDim;
                Draw.Disc(new Vector2(iconX, iconY), 3f, Draw.Color);
            }
            else
            {
                Draw.Color = isActive ? UI.accent : UI.textDim;
                Draw.Rectangle(new Rect(iconX - 4f, iconY - 3f, 8f, 6f), 1f, Draw.Color);
            }

            // Label
            UI.SetFontSize(itemFontSize);
            Draw.Color = isActive ? UI.accent : UI.textColor;
            string label = src.name;
            if (label.Length > 24)
                label = label.Substring(0, 24) + "..";
            Draw.Text(new Vector2(iconX + 12f, iconY), label, TextAlign.Left);

            top -= itemHeight + 2f;
        }

        // Browse File button
        top -= 4f;
        Rect browseRect = new Rect(x, top - buttonHeight, w, buttonHeight);
        _browseRect = browseRect;
        Draw.Rectangle(browseRect, 4f, new Color(0.12f, 0.14f, 0.18f, 0.8f));
        Draw.RectangleBorder(browseRect, 1f, 4f, UI.panelBorder);

        // Folder icon
        float bx = browseRect.xMin + 14f;
        float by = browseRect.center.y;
        Draw.Color = UI.textDim;
        Draw.Rectangle(new Rect(bx - 4f, by - 3f, 10f, 7f), 1f, UI.textDim);
        Draw.Rectangle(new Rect(bx - 4f, by + 2f, 5f, 3f), 1f, UI.textDim);

        UI.SetFontSize(itemFontSize);
        Draw.Color = UI.textColor;
        Draw.Text(new Vector2(bx + 12f, by), "Browse File...", TextAlign.Left);

        top -= buttonHeight + sectionGap;

        // ── Webcam mode selector (resolution + FPS) ─────────────
        _modeRects.Clear();
        _modeValues.Clear();

        bool isWebcam = mgr != null && mgr.ActiveType == VideoSourceType.Webcam;
        if (isWebcam)
        {
            UI.SetFontSize(labelFontSize);
            Draw.Color = UI.textDim;
            Draw.Text(new Vector2(x, top), "CAMERA MODE", TextAlign.TopLeft);
            top -= 14f;

            // Try to query device, fall back to presets
            var deviceRes = mgr.GetAvailableResolutions(mgr.ActiveSourceName);
            int reqW = mgr.RequestedWidth;
            int reqH = mgr.RequestedHeight;
            int reqFPS = mgr.RequestedFPS;

            if (deviceRes != null && deviceRes.Length > 0)
            {
                // Use device-reported modes (deduplicated, sorted)
                var seen = new HashSet<string>();
                for (int i = 0; i < deviceRes.Length; i++)
                {
                    int mw = deviceRes[i].width;
                    int mh = deviceRes[i].height;
                    int mf = Mathf.RoundToInt((float)deviceRes[i].refreshRateRatio.value);
                    if (mf <= 0) mf = 30;
                    string key = $"{mw}x{mh}@{mf}";
                    if (!seen.Add(key)) continue;

                    Vector3Int mode = new Vector3Int(mw, mh, mf);
                    Rect modeRect = new Rect(x, top - itemHeight, w, itemHeight);
                    _modeRects.Add(modeRect);
                    _modeValues.Add(mode);

                    bool active = mw == reqW && mh == reqH && mf == reqFPS;
                    if (active)
                    {
                        Draw.Rectangle(modeRect, 3f, UI.accent * 0.2f);
                        Draw.RectangleBorder(modeRect, 1f, 3f, UI.accent * 0.4f);
                    }
                    else
                    {
                        Draw.Rectangle(modeRect, 3f, new Color(0.1f, 0.12f, 0.15f, 0.4f));
                    }

                    UI.SetFontSize(itemFontSize);
                    Draw.Color = active ? UI.accent : UI.textColor;
                    Draw.Text(new Vector2(x + 10f, modeRect.center.y), $"{mw}x{mh}", TextAlign.Left);
                    Draw.Color = active ? UI.accent : UI.textDim;
                    UI.SetFontSize(knobLabelFontSize);
                    Draw.Text(new Vector2(x + w - 10f, modeRect.center.y), $"{mf}fps", TextAlign.Right);

                    top -= itemHeight + 1f;
                }
            }
            else
            {
                // Use fallback presets
                for (int i = 0; i < VideoSourceManager.FallbackModes.Length; i++)
                {
                    var mode = VideoSourceManager.FallbackModes[i];
                    Rect modeRect = new Rect(x, top - itemHeight, w, itemHeight);
                    _modeRects.Add(modeRect);
                    _modeValues.Add(mode);

                    bool active = mode.x == reqW && mode.y == reqH && mode.z == reqFPS;
                    if (active)
                    {
                        Draw.Rectangle(modeRect, 3f, UI.accent * 0.2f);
                        Draw.RectangleBorder(modeRect, 1f, 3f, UI.accent * 0.4f);
                    }
                    else
                    {
                        Draw.Rectangle(modeRect, 3f, new Color(0.1f, 0.12f, 0.15f, 0.4f));
                    }

                    UI.SetFontSize(itemFontSize);
                    Draw.Color = active ? UI.accent : UI.textColor;
                    Draw.Text(new Vector2(x + 10f, modeRect.center.y), $"{mode.x}x{mode.y}", TextAlign.Left);
                    Draw.Color = active ? UI.accent : UI.textDim;
                    UI.SetFontSize(knobLabelFontSize);
                    Draw.Text(new Vector2(x + w - 10f, modeRect.center.y), $"{mode.z}fps", TextAlign.Right);

                    top -= itemHeight + 1f;
                }
            }

            top -= sectionGap - 4f;
        }

        // ── Downsample selector ──────────────────────────────────
        UI.SetFontSize(labelFontSize);
        Draw.Color = UI.textDim;
        Draw.Text(new Vector2(x, top), "RESOLUTION", TextAlign.TopLeft);
        top -= 14f;

        int currentDS = 1;
        if (mgr != null) currentDS = mgr.downsampleFactor;
        else if (vfi != null) currentDS = vfi.downsampleFactor;

        float segW = w / 4f;
        for (int i = 0; i < 4; i++)
        {
            int factor = i + 1;
            Rect segRect = new Rect(x + i * segW, top - 24f, segW, 24f);
            _downsampleRects[i] = segRect;

            bool selected = factor == currentDS;
            Color bg = selected ? UI.accent * 0.3f : new Color(0.1f, 0.12f, 0.15f, 0.6f);
            Draw.Rectangle(segRect, i == 0 ? 3f : (i == 3 ? 3f : 0f), bg);
            if (selected)
                Draw.RectangleBorder(segRect, 1f, i == 0 ? 3f : (i == 3 ? 3f : 0f), UI.accent * 0.5f);

            UI.SetFontSize(itemFontSize);
            Draw.Color = selected ? UI.accent : UI.textColor;
            string dsLabel = factor == 1 ? "Full" : $"1/{factor}";
            Draw.Text(segRect.center, dsLabel, TextAlign.Center);
        }

        // Outer border for the whole segmented control
        Rect segGroup = new Rect(x, top - 24f, w, 24f);
        Draw.RectangleBorder(segGroup, 1f, 3f, UI.panelBorder);

        top -= 24f + 6f;

        // Resolution readout
        if (mgr != null)
        {
            var native = mgr.NativeResolution;
            var effective = mgr.EffectiveResolution;
            if (native.x > 0)
            {
                UI.SetFontSize(knobLabelFontSize);
                Draw.Color = UI.textDim;
                string resText = $"{native.x}x{native.y}";
                if (effective.x > 0 && (effective.x != native.x || effective.y != native.y))
                    resText += $" → {effective.x}x{effective.y}";
                Draw.Text(new Vector2(x, top), resText, TextAlign.TopLeft);
                top -= 12f;
            }
        }

        top -= sectionGap - 6f;

        // ── Playback controls (file sources only) ────────────────
        bool isFile = mgr != null && mgr.ActiveType == VideoSourceType.VideoFile;
        if (isFile && vfi != null)
        {
            UI.SetFontSize(labelFontSize);
            Draw.Color = UI.textDim;
            Draw.Text(new Vector2(x, top), "PLAYBACK", TextAlign.TopLeft);
            top -= 14f;

            // Play / Pause / Stop buttons in a row
            float btnW = (w - 8f) / 3f;
            float btnH = 26f;

            Rect playR = new Rect(x, top - btnH, btnW, btnH);
            Rect pauseR = new Rect(x + btnW + 4f, top - btnH, btnW, btnH);
            Rect stopR = new Rect(x + (btnW + 4f) * 2f, top - btnH, btnW, btnH);
            _playRect = playR;
            _pauseRect = pauseR;
            _stopRect = stopR;

            DrawButton(playR, "Play", false);
            DrawButton(pauseR, "Pause", false);
            DrawButton(stopR, "Stop", false);

            top -= btnH + 10f;

            // Speed knob + Loop toggle side by side
            float knobR = knobSize / 2f;
            float knobCX = x + knobR;
            float knobCY = top - knobR;

            _speedKnob = new KnobZone
            {
                center = new Vector2(knobCX, knobCY),
                radius = knobR,
                min = 0.1f, max = 4f,
                setter = v => vfi.playbackSpeed = v,
                getter = () => vfi.playbackSpeed
            };
            _hasSpeedKnob = true;
            DrawKnob(knobCX, knobCY, knobR, "Speed", vfi.playbackSpeed, 0.1f, 4f);

            // Loop toggle to the right of the knob
            float loopX = x + knobSize + 16f;
            float loopY = top - knobR;
            Rect loopRect = new Rect(loopX, loopY - 12f, w - knobSize - 16f, 24f);
            _loopRect = loopRect;

            bool looping = vfi.loop;
            Color loopBg = looping ? UI.accent * 0.25f : new Color(0.1f, 0.12f, 0.15f, 0.6f);
            Draw.Rectangle(loopRect, 4f, loopBg);
            Draw.RectangleBorder(loopRect, 1f, 4f, looping ? UI.accent * 0.5f : UI.panelBorder);
            UI.SetFontSize(itemFontSize);
            Draw.Color = looping ? UI.accent : UI.textColor;
            Draw.Text(loopRect.center, looping ? "LOOP ON" : "LOOP OFF", TextAlign.Center);
        }
        else
        {
            _playRect = Rect.zero;
            _pauseRect = Rect.zero;
            _stopRect = Rect.zero;
            _loopRect = Rect.zero;
        }
    }

    void DrawButton(Rect rect, string label, bool active)
    {
        Color bg = active ? UI.accent * 0.3f : new Color(0.12f, 0.14f, 0.18f, 0.8f);
        Draw.Rectangle(rect, 4f, bg);
        Draw.RectangleBorder(rect, 1f, 4f, active ? UI.accent * 0.5f : UI.panelBorder);
        UI.SetFontSize(itemFontSize);
        Draw.Color = active ? UI.accent : UI.textColor;
        Draw.Text(rect.center, label, TextAlign.Center);
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
        Draw.Disc(dotPos, 3.5f, Color.white);

        // Drag highlight
        if (_draggingKnob)
            Draw.Ring(new Vector2(cx, cy), radius + 2f, 1.5f, UI.accent * 0.5f);

        // Label
        UI.SetFontSize(knobLabelFontSize);
        Draw.Color = UI.textDim;
        Draw.Text(new Vector2(cx, cy - radius - 8f), label, TextAlign.Top);

        // Value
        UI.SetFontSize(knobValueFontSize);
        Draw.Color = UI.textColor;
        Draw.Text(new Vector2(cx, cy - 2f), $"{value:F1}x", TextAlign.Center);
    }

    // ── IFishPanel ───────────────────────────────────────────────

    public void OnPress(Vector2 pos)
    {
        if (UI == null) return;
        FishSynthInput.InputConsumed = true;
        var mgr = UI.videoSourceManager;

        // Refresh button
        if (_refreshRect.Contains(pos))
        {
            RefreshSources();
            return;
        }

        // Source list items
        for (int i = 0; i < _sourceRects.Count && i < _sources.Count; i++)
        {
            if (_sourceRects[i].width > 0 && _sourceRects[i].Contains(pos))
            {
                var src = _sources[i];
                if (mgr != null)
                {
                    if (src.type == VideoSourceType.Webcam)
                        mgr.SwitchToWebcam(src.identifier);
                    else if (src.type == VideoSourceType.VideoFile)
                        mgr.SwitchToFile(src.identifier);
                }
                RefreshSources();
                return;
            }
        }

        // Browse file button
        if (_browseRect.Contains(pos))
        {
            if (mgr != null)
                mgr.BrowseForFile();
            RefreshSources();
            return;
        }

        // Webcam mode selector
        for (int i = 0; i < _modeRects.Count && i < _modeValues.Count; i++)
        {
            if (_modeRects[i].Contains(pos))
            {
                var mode = _modeValues[i];
                if (mgr != null)
                    mgr.SetWebcamMode(mode.x, mode.y, mode.z);
                return;
            }
        }

        // Downsample selector
        for (int i = 0; i < 4; i++)
        {
            if (_downsampleRects[i].Contains(pos))
            {
                int factor = i + 1;
                if (mgr != null) mgr.downsampleFactor = factor;
                if (UI.videoInput != null) UI.videoInput.downsampleFactor = factor;
                return;
            }
        }

        // Playback controls
        var vfi = UI.videoInput;
        if (vfi != null)
        {
            if (_playRect.width > 0 && _playRect.Contains(pos)) { vfi.Play(); return; }
            if (_pauseRect.width > 0 && _pauseRect.Contains(pos)) { vfi.Pause(); return; }
            if (_stopRect.width > 0 && _stopRect.Contains(pos)) { vfi.Stop(); return; }
            if (_loopRect.width > 0 && _loopRect.Contains(pos))
            {
                vfi.loop = !vfi.loop;
                return;
            }
        }

        // Speed knob
        if (_hasSpeedKnob && Vector2.Distance(pos, _speedKnob.center) <= _speedKnob.radius + 6f)
        {
            _draggingKnob = true;
            _dragStartValue = _speedKnob.getter();
            _dragStartPos = pos;
            return;
        }
    }

    public void OnDrag(Vector2 pos)
    {
        if (!_draggingKnob) return;
        FishSynthInput.InputConsumed = true;

        float dy = pos.y - _dragStartPos.y;
        float sensitivity = (_speedKnob.max - _speedKnob.min) / (_speedKnob.radius * 4f);
        float newVal = _dragStartValue + dy * sensitivity;
        newVal = Mathf.Clamp(newVal, _speedKnob.min, _speedKnob.max);
        _speedKnob.setter(newVal);
    }

    public void OnRelease(Vector2 pos)
    {
        _draggingKnob = false;
    }

    public void OnScroll(Vector2 pos, float delta)
    {
        if (UI == null) return;

        // Speed knob scroll
        if (_hasSpeedKnob && Vector2.Distance(pos, _speedKnob.center) <= _speedKnob.radius + 6f)
        {
            FishSynthInput.InputConsumed = true;
            float step = (_speedKnob.max - _speedKnob.min) * 0.03f;
            float newVal = _speedKnob.getter() + delta * step;
            newVal = Mathf.Clamp(newVal, _speedKnob.min, _speedKnob.max);
            _speedKnob.setter(newVal);
            return;
        }

        // Downsample scroll
        for (int i = 0; i < 4; i++)
        {
            if (_downsampleRects[i].Contains(pos))
            {
                FishSynthInput.InputConsumed = true;
                var mgr = UI.videoSourceManager;
                int current = mgr != null ? mgr.downsampleFactor : (UI.videoInput != null ? UI.videoInput.downsampleFactor : 1);
                int next = Mathf.Clamp(current + (delta > 0 ? -1 : 1), 1, 4);
                if (mgr != null) mgr.downsampleFactor = next;
                if (UI.videoInput != null) UI.videoInput.downsampleFactor = next;
                return;
            }
        }
    }
}
