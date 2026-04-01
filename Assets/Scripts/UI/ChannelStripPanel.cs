using UnityEngine;
using Shapes;

/// <summary>
/// Channel strip rack: 6 vertical strips (Pos X, Pos Y, Speed, Vel X, Vel Y, Size).
/// Each strip shows a live meter, CC/Note toggle, and mode-specific interactive settings.
/// </summary>
public class ChannelStripPanel : ImmediateModePanel, IFishPanel, IFishPanelDropdown
{
    FishSynthUI _ui;
    FishSynthUI UI => _ui != null ? _ui : (_ui = GetComponentInParent<FishSynthUI>());

    static readonly string[] Labels = { "Pos X", "Pos Y", "Speed", "Vel X", "Vel Y", "Size" };
    static readonly string[] NoteNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
    static readonly string[] NoteOrderNames = { "Sequential", "Random", "Shuffle" };

    // ── Layout variables (serialized) ─────────────────────────────────────────
    [Header("Layout")]
    [SerializeField] float stripPadding = 10f;
    [SerializeField] float stripGap = 6f;
    [SerializeField] float meterHeightRatio = 0.28f;
    [SerializeField] float labelFontSize = 13f;
    [SerializeField] float modeFontSize = 11f;
    [SerializeField] float settingFontSize = 10f;
    [SerializeField] float ccNumFontSize = 18f;
    [SerializeField] float noteNameFontSize = 18f;
    [SerializeField] float channelFontSize = 10f;

    // Smoothed meter values
    private float[] _smoothValues = new float[6];

    // ── Hit zones (per-strip, updated each draw) ──────────────────────────────
    private Rect[] _modeToggleRects = new Rect[6];
    private Rect[] _enableToggleRects = new Rect[6];

    // CC mode hit zones
    private Rect[] _ccNumRects = new Rect[6];
    private Rect[] _holdToggleRects = new Rect[6];

    // Note mode hit zones
    private Rect[] _octaveRects = new Rect[6];
    private Rect[] _octRangeRects = new Rect[6];
    private Rect[] _orderRects = new Rect[6];
    private Rect[] _legatoRects = new Rect[6];

    // MIDI channel hit zones
    private Rect[] _channelRects = new Rect[6];

    // Dropdown state
    private bool _channelDropdownOpen;
    private int _channelDropdownStrip;
    private bool _orderDropdownOpen;
    private int _orderDropdownStrip;
    private Rect _activeDropdownRect; // cached rect of whichever dropdown is open

    // Drag state
    private int _dragStrip = -1;
    enum DragField { None, CCNum, Octave, OctRange, Channel }
    private DragField _dragField;
    private float _dragStartY;
    private int _dragStartVal;

    public override void DrawPanelShapes(Rect rect, ImCanvasContext ctx)
    {
        if (UI == null || UI.midiOutput == null) return;

        UI.DrawPanelBg(rect);

        var midi = UI.midiOutput;
        var tracker = UI.tracker;
        FishTrackData d = tracker != null ? tracker.Data : default;
        MidiChannelMapping[] mappings = { midi.posX, midi.posY, midi.velocityMag, midi.velX, midi.velY, midi.size };
        float[] rawValues = { d.posX, d.posY, d.velocityMagnitude, d.velX, d.velY, d.size };

        float stripW = (rect.width - stripPadding * 2f - stripGap * 5f) / 6f;
        float stripH = rect.height - stripPadding * 2f;

        for (int i = 0; i < 6; i++)
        {
            float sx = rect.xMin + stripPadding + i * (stripW + stripGap);
            Rect stripRect = new Rect(sx, rect.yMin + stripPadding, stripW, stripH);
            DrawStrip(stripRect, mappings[i], Labels[i], rawValues[i], i, d.detected, midi);
        }

        // Draw dropdowns LAST (on top of everything)
        if (_channelDropdownOpen && _channelDropdownStrip >= 0 && _channelDropdownStrip < 6)
        {
            DrawChannelDropdown(mappings[_channelDropdownStrip], _channelDropdownStrip);
        }
        if (_orderDropdownOpen && _orderDropdownStrip >= 0 && _orderDropdownStrip < 6)
        {
            DrawOrderDropdown(mappings[_orderDropdownStrip], _orderDropdownStrip);
        }
    }

    void DrawStrip(Rect rect, MidiChannelMapping mapping, string label, float rawValue, int index, bool detected, FishMidiOutput midi)
    {
        // Strip background
        Color stripBg = mapping.enabled
            ? new Color(0.08f, 0.1f, 0.13f, 0.85f)
            : new Color(0.06f, 0.06f, 0.06f, 0.6f);
        Draw.Rectangle(rect, 6f, stripBg);

        float pad = 8f;
        float cx = rect.center.x;
        float w = rect.width - pad * 2f;
        float top = rect.yMax - pad;

        // ── Enable toggle (top-left corner dot) ────────────────────
        float toggleR = 5f;
        Vector2 togglePos = new Vector2(rect.xMin + pad + toggleR, top - toggleR);
        Color toggleCol = mapping.enabled ? UI.statusGood : new Color(0.3f, 0.3f, 0.3f, 0.6f);
        Draw.Disc(togglePos, toggleR, toggleCol);
        if (!mapping.enabled)
            Draw.Ring(togglePos, toggleR, 1f, UI.textDim);
        _enableToggleRects[index] = new Rect(togglePos.x - 10f, togglePos.y - 10f, 20f, 20f);

        // ── Label ────────────────────────────────────────────────
        UI.SetFontSize(labelFontSize);
        Draw.Color = mapping.enabled ? UI.textColor : new Color(0.3f, 0.3f, 0.3f, 0.4f);
        Draw.Text(new Vector2(cx, top), label, TextAlign.Top);
        top -= 22f;

        if (!mapping.enabled)
        {
            // Clear hit rects for disabled strips
            _modeToggleRects[index] = Rect.zero;
            return;
        }

        // ── Mode indicator (CC / Note) ─────────────────────────────
        bool isNote = mapping.mode == MidiMode.Note;
        Color modeBg = isNote ? new Color(UI.accentNote.r, UI.accentNote.g, UI.accentNote.b, 0.25f)
                              : new Color(UI.accent.r, UI.accent.g, UI.accent.b, 0.2f);
        Rect modeRect = new Rect(cx - w / 2f, top - 24f, w, 24f);
        _modeToggleRects[index] = modeRect;
        Draw.Rectangle(modeRect, 4f, modeBg);
        Draw.RectangleBorder(modeRect, 1f, 4f, isNote ? UI.accentNote * 0.5f : UI.accent * 0.4f);
        UI.SetFontSize(modeFontSize);
        Draw.Color = isNote ? UI.accentNote : UI.accent;
        Draw.Text(modeRect.center, isNote ? "NOTE" : "CC", TextAlign.Center);
        top -= 32f;

        // ── Live meter ─────────────────────────────────────────────
        float meterH = rect.height * meterHeightRatio;
        float meterW = Mathf.Max(24f, w * 0.4f);
        Rect meterBg = new Rect(cx - meterW / 2f, top - meterH, meterW, meterH);

        float t = Mathf.InverseLerp(mapping.inputMin, mapping.inputMax, rawValue);
        t = Mathf.Clamp01(t);
        _smoothValues[index] = Mathf.Lerp(_smoothValues[index], t, Time.deltaTime * 12f);
        float smoothT = _smoothValues[index];

        Draw.Rectangle(meterBg, 4f, new Color(0.03f, 0.03f, 0.05f, 0.9f));
        float fillH = meterH * smoothT;
        float inset = 2f;
        if (fillH > 1f)
        {
            Rect fillRect = new Rect(meterBg.x + inset, meterBg.y + inset, meterW - inset * 2, fillH - inset);
            Color meterCol = detected ? (isNote ? UI.accentNote : UI.accent) : UI.statusWarn;
            Draw.Rectangle(fillRect, 2f, meterCol * 0.7f);
            if (fillH > 4f)
            {
                Rect tipRect = new Rect(fillRect.x, fillRect.yMax - 3f, fillRect.width, 3f);
                Draw.Rectangle(tipRect, 2f, meterCol);
            }
        }
        Draw.RectangleBorder(meterBg, 1.5f, 4f, new Color(0.2f, 0.25f, 0.3f, 0.5f));
        top -= meterH + 8f;

        // ── Mode-specific settings ─────────────────────────────────
        if (isNote)
            DrawNoteSettings(cx, ref top, w, mapping, midi, detected, index);
        else
            DrawCCSettings(cx, ref top, w, mapping, index);

        // ── MIDI Channel at bottom (scrollable) ────────────────────
        UI.SetFontSize(channelFontSize);
        Draw.Color = UI.textDim;
        Rect chRect = new Rect(cx - w / 2f, rect.yMin + stripPadding, w, 18f);
        _channelRects[index] = chRect;
        bool chDragging = _dragStrip == index && _dragField == DragField.Channel;
        Draw.Color = chDragging ? UI.accent : UI.textDim;
        Draw.Text(new Vector2(cx, rect.yMin + stripPadding + 9f), $"ch{mapping.midiChannel}", TextAlign.Center);
        // Up/down arrows
        UI.SetFontSize(8f);
        Draw.Text(new Vector2(chRect.xMax - 2f, chRect.center.y + 4f), "\u25B2", TextAlign.Right);
        Draw.Text(new Vector2(chRect.xMax - 2f, chRect.center.y - 4f), "\u25BC", TextAlign.Right);
    }

    void DrawNoteSettings(float cx, ref float top, float w, MidiChannelMapping mapping, FishMidiOutput midi, bool detected, int index)
    {
        // Current note name (live readout)
        if (mapping.currentNote >= 0 && detected)
        {
            int noteIdx = mapping.currentNote % 12;
            int octave = (mapping.currentNote / 12) - 1;
            string noteName = $"{NoteNames[noteIdx]}{octave}";
            UI.SetFontSize(noteNameFontSize);
            Draw.Color = UI.accentNote;
            Draw.Text(new Vector2(cx, top), noteName, TextAlign.Top);
        }
        else
        {
            UI.SetFontSize(14f);
            Draw.Color = UI.textDim;
            Draw.Text(new Vector2(cx, top), "---", TextAlign.Top);
        }
        top -= 28f;

        // Octave (scrollable)
        Rect octRect = new Rect(cx - w / 2f, top - 18f, w, 18f);
        _octaveRects[index] = octRect;
        bool octDrag = _dragStrip == index && _dragField == DragField.Octave;
        UI.SetFontSize(settingFontSize);
        Draw.Color = UI.textDim;
        Draw.Text(new Vector2(cx - w / 4f, octRect.center.y), "Starting Octave", TextAlign.Left);
        Draw.Color = octDrag ? UI.accent : UI.textColor;
        Draw.Text(new Vector2(cx + w / 4f, octRect.center.y), $"{mapping.rootOctave}", TextAlign.Right);
        top -= 22f;

        // Octave range (scrollable)
        Rect rangeRect = new Rect(cx - w / 2f, top - 18f, w, 18f);
        _octRangeRects[index] = rangeRect;
        bool rangeDrag = _dragStrip == index && _dragField == DragField.OctRange;
        UI.SetFontSize(settingFontSize);
        Draw.Color = UI.textDim;
        Draw.Text(new Vector2(cx - w / 4f, rangeRect.center.y), "Octave Range", TextAlign.Left);
        Draw.Color = rangeDrag ? UI.accent : UI.textColor;
        Draw.Text(new Vector2(cx + w / 4f, rangeRect.center.y), $"{mapping.octaveRange}", TextAlign.Right);
        top -= 22f;

        // Note order (clickable cycle, full names, scroll support)
        Rect orderRect = new Rect(cx - w / 2f, top - 20f, w, 20f);
        _orderRects[index] = orderRect;
        string orderLabel = NoteOrderNames[(int)mapping.noteOrder];
        Color orderBg = new Color(0.12f, 0.14f, 0.18f, 0.7f);
        Draw.Rectangle(orderRect, 3f, orderBg);
        Draw.RectangleBorder(orderRect, 1f, 3f, UI.panelBorder * 0.5f);
        UI.SetFontSize(settingFontSize);
        Draw.Color = UI.textColor;
        Draw.Text(orderRect.center, orderLabel, TextAlign.Center);
        top -= 24f;

        // Legato toggle
        Rect legRect = new Rect(cx - w / 2f, top - 20f, w, 20f);
        _legatoRects[index] = legRect;
        Color legBg = mapping.legato
            ? new Color(UI.accent.r, UI.accent.g, UI.accent.b, 0.2f)
            : new Color(0.12f, 0.14f, 0.18f, 0.5f);
        Draw.Rectangle(legRect, 3f, legBg);
        Draw.RectangleBorder(legRect, 1f, 3f, mapping.legato ? UI.accent * 0.4f : UI.panelBorder * 0.3f);
        UI.SetFontSize(9f);
        Draw.Color = mapping.legato ? UI.accent : UI.textDim;
        Draw.Text(legRect.center, "LEGATO", TextAlign.Center);
        top -= 24f;
    }

    void DrawCCSettings(float cx, ref float top, float w, MidiChannelMapping mapping, int index)
    {
        // CC number (scrollable)
        Rect ccRect = new Rect(cx - w / 2f, top - 24f, w, 24f);
        _ccNumRects[index] = ccRect;
        bool ccDrag = _dragStrip == index && _dragField == DragField.CCNum;
        UI.SetFontSize(ccNumFontSize);
        Draw.Color = ccDrag ? UI.accent : UI.accent * 0.9f;
        Draw.Text(new Vector2(cx, ccRect.center.y), $"CC{mapping.ccNumber}", TextAlign.Center);
        top -= 30f;

        // Input range
        UI.SetFontSize(settingFontSize);
        Draw.Color = UI.textDim;
        Draw.Text(new Vector2(cx, top), $"in: {mapping.inputMin:F1}-{mapping.inputMax:F1}", TextAlign.Top);
        top -= 16f;

        // Output range
        Draw.Text(new Vector2(cx, top), $"out: {mapping.outputMin}-{mapping.outputMax}", TextAlign.Top);
        top -= 20f;

        // Hold toggle
        Rect holdRect = new Rect(cx - w / 2f, top - 20f, w, 20f);
        _holdToggleRects[index] = holdRect;
        Color holdBg = mapping.holdOnLostDetection
            ? new Color(UI.statusWarn.r, UI.statusWarn.g, UI.statusWarn.b, 0.2f)
            : new Color(0.12f, 0.14f, 0.18f, 0.5f);
        Draw.Rectangle(holdRect, 3f, holdBg);
        Draw.RectangleBorder(holdRect, 1f, 3f, mapping.holdOnLostDetection ? UI.statusWarn * 0.4f : UI.panelBorder * 0.3f);
        UI.SetFontSize(8f);
        Draw.Color = mapping.holdOnLostDetection ? UI.statusWarn : UI.textDim;
        Draw.Text(holdRect.center, "Hold On Lost Tracking", TextAlign.Center);
        top -= 24f;
    }

    // ── Dropdown drawing ─────────────────────────────────────────────────────────

    void DrawChannelDropdown(MidiChannelMapping mapping, int stripIndex)
    {
        Rect chRect = _channelRects[stripIndex];
        float itemH = 18f;
        float dropH = itemH * 16f;
        Rect dropRect = new Rect(chRect.x, chRect.yMax, chRect.width, dropH);
        _activeDropdownRect = dropRect;

        Draw.Rectangle(dropRect, 4f, new Color(0.06f, 0.08f, 0.1f, 0.95f));
        Draw.RectangleBorder(dropRect, 1f, 4f, UI.accent * 0.5f);

        Vector2 mousePos = Input.mousePosition;
        // We need panel-local mouse for hover detection
        // Use the stored rect positions directly since they're in panel-local space

        for (int ch = 1; ch <= 16; ch++)
        {
            float itemY = dropRect.yMin + (ch - 1) * itemH;
            Rect itemRect = new Rect(dropRect.x, itemY, dropRect.width, itemH);
            bool selected = ch == mapping.midiChannel;
            if (selected)
                Draw.Rectangle(itemRect, 2f, UI.accent * 0.3f);
            UI.SetFontSize(channelFontSize);
            Draw.Color = selected ? UI.accent : UI.textColor;
            Draw.Text(itemRect.center, $"ch{ch}", TextAlign.Center);
        }
    }

    void DrawOrderDropdown(MidiChannelMapping mapping, int stripIndex)
    {
        Rect orderRect = _orderRects[stripIndex];
        float itemH = 20f;
        float dropH = itemH * NoteOrderNames.Length;
        Rect dropRect = new Rect(orderRect.x, orderRect.yMax, orderRect.width, dropH);
        _activeDropdownRect = dropRect;

        Draw.Rectangle(dropRect, 4f, new Color(0.06f, 0.08f, 0.1f, 0.95f));
        Draw.RectangleBorder(dropRect, 1f, 4f, UI.accent * 0.5f);

        for (int o = 0; o < NoteOrderNames.Length; o++)
        {
            float itemY = dropRect.yMin + o * itemH;
            Rect itemRect = new Rect(dropRect.x, itemY, dropRect.width, itemH);
            bool selected = o == (int)mapping.noteOrder;
            if (selected)
                Draw.Rectangle(itemRect, 2f, UI.accent * 0.3f);
            UI.SetFontSize(settingFontSize);
            Draw.Color = selected ? UI.accent : UI.textColor;
            Draw.Text(itemRect.center, NoteOrderNames[o], TextAlign.Center);
        }
    }

    // ── IFishPanel ───────────────────────────────────────────────────────────────

    MidiChannelMapping[] GetMappings()
    {
        var midi = UI.midiOutput;
        return new[] { midi.posX, midi.posY, midi.velocityMag, midi.velX, midi.velY, midi.size };
    }

    public void OnPress(Vector2 pos)
    {
        if (UI == null || UI.midiOutput == null) return;
        FishSynthInput.InputConsumed = true;
        var mappings = GetMappings();

        // Handle channel dropdown click
        if (_channelDropdownOpen)
        {
            Rect chRect = _channelRects[_channelDropdownStrip];
            float itemH = 18f;
            float dropH = itemH * 16f;
            Rect dropRect = new Rect(chRect.x, chRect.yMax, chRect.width, dropH);

            if (dropRect.Contains(pos))
            {
                int ch = Mathf.FloorToInt((pos.y - dropRect.yMin) / itemH) + 1;
                ch = Mathf.Clamp(ch, 1, 16);
                mappings[_channelDropdownStrip].midiChannel = ch;
                _channelDropdownOpen = false;
                return;
            }
            // Click elsewhere closes dropdown
            _channelDropdownOpen = false;
            return;
        }

        // Handle order dropdown click
        if (_orderDropdownOpen)
        {
            Rect orderRect = _orderRects[_orderDropdownStrip];
            float itemH = 20f;
            float dropH = itemH * NoteOrderNames.Length;
            Rect dropRect = new Rect(orderRect.x, orderRect.yMax, orderRect.width, dropH);

            if (dropRect.Contains(pos))
            {
                int o = Mathf.FloorToInt((pos.y - dropRect.yMin) / itemH);
                o = Mathf.Clamp(o, 0, NoteOrderNames.Length - 1);
                mappings[_orderDropdownStrip].noteOrder = (NoteOrder)o;
                _orderDropdownOpen = false;
                return;
            }
            _orderDropdownOpen = false;
            return;
        }

        for (int i = 0; i < 6; i++)
        {
            // Enable toggle
            if (_enableToggleRects[i].Contains(pos))
            {
                mappings[i].enabled = !mappings[i].enabled;
                return;
            }

            // Mode toggle
            if (_modeToggleRects[i].width > 0 && _modeToggleRects[i].Contains(pos))
            {
                mappings[i].mode = mappings[i].mode == MidiMode.CC ? MidiMode.Note : MidiMode.CC;
                return;
            }

            if (!mappings[i].enabled) continue;

            bool isNote = mappings[i].mode == MidiMode.Note;

            if (isNote)
            {
                // Note order - open dropdown
                if (_orderRects[i].Contains(pos))
                {
                    _orderDropdownOpen = true;
                    _orderDropdownStrip = i;
                    return;
                }
                // Legato toggle
                if (_legatoRects[i].Contains(pos))
                {
                    mappings[i].legato = !mappings[i].legato;
                    return;
                }
            }
            else
            {
                // Hold toggle
                if (_holdToggleRects[i].Contains(pos))
                {
                    mappings[i].holdOnLostDetection = !mappings[i].holdOnLostDetection;
                    return;
                }
            }

            // MIDI channel - open dropdown
            if (_channelRects[i].Contains(pos))
            {
                _channelDropdownOpen = true;
                _channelDropdownStrip = i;
                return;
            }
        }
    }

    public void OnDrag(Vector2 pos) { }

    public void OnRelease(Vector2 pos) { }

    public void OnScroll(Vector2 pos, float delta)
    {
        if (UI == null || UI.midiOutput == null) return;
        var mappings = GetMappings();

        for (int i = 0; i < 6; i++)
        {
            if (!mappings[i].enabled) continue;

            bool isNote = mappings[i].mode == MidiMode.Note;

            if (isNote)
            {
                if (_octaveRects[i].Contains(pos))
                {
                    FishSynthInput.InputConsumed = true;
                    mappings[i].rootOctave = Mathf.Clamp(mappings[i].rootOctave + (delta > 0 ? 1 : -1), 0, 8);
                    return;
                }
                if (_octRangeRects[i].Contains(pos))
                {
                    FishSynthInput.InputConsumed = true;
                    mappings[i].octaveRange = Mathf.Clamp(mappings[i].octaveRange + (delta > 0 ? 1 : -1), 1, 6);
                    return;
                }
                // Note order scroll cycles through values
                if (_orderRects[i].Contains(pos))
                {
                    FishSynthInput.InputConsumed = true;
                    int count = NoteOrderNames.Length;
                    int cur = (int)mappings[i].noteOrder;
                    cur = (cur + (delta > 0 ? 1 : count - 1)) % count;
                    mappings[i].noteOrder = (NoteOrder)cur;
                    return;
                }
            }
            else
            {
                if (_ccNumRects[i].Contains(pos))
                {
                    FishSynthInput.InputConsumed = true;
                    mappings[i].ccNumber = Mathf.Clamp(mappings[i].ccNumber + (delta > 0 ? 1 : -1), 0, 127);
                    return;
                }
            }

            // MIDI channel scroll
            if (_channelRects[i].Contains(pos))
            {
                FishSynthInput.InputConsumed = true;
                mappings[i].midiChannel = Mathf.Clamp(mappings[i].midiChannel + (delta > 0 ? 1 : -1), 1, 16);
                return;
            }
        }
    }

    // ── IFishPanelDropdown ──────────────────────────────────────────────────────

    public bool HasOpenDropdown => _channelDropdownOpen || _orderDropdownOpen;

    public Rect GetDropdownRect() => _activeDropdownRect;
}
