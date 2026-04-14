using UnityEngine;
using Shapes;

/// <summary>
/// Song Settings panel (right side): scale picker, root note, and a
/// Shapes-drawn piano keyboard that reflects the current scale.
/// Piano keys are clickable when Custom scale is selected.
/// Click root dots to change root note. Click scale name to open dropdown.
/// </summary>
public class SongSettingsPanel : ImmediateModePanel, IFishPanel, IFishPanelDropdown
{
    FishSynthUI _ui;
    FishSynthUI UI => _ui != null ? _ui : (_ui = GetComponentInParent<FishSynthUI>());

    // Piano key layout constants
    static readonly bool[] IsBlackKey = { false, true, false, true, false, false, true, false, true, false, true, false };
    static readonly string[] NoteNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

    // ── Layout variables (serialized) ─────────────────────────────────────────
    [Header("Layout")]
    [SerializeField] float padding = 12f;
    [SerializeField] float headerFontSize = 16f;
    [SerializeField] float labelFontSize = 11f;
    [SerializeField] float rootNameFontSize = 22f;
    [SerializeField] float scaleNameFontSize = 16f;
    [SerializeField] float dotRadius = 4.5f;
    [SerializeField] float dotRadiusActive = 7f;
    [SerializeField] float keyboardHeight = 120f;
    [SerializeField] float dotNameFontSize = 7f;

    // Hit zones (in panel-local space, updated each draw)
    private Rect[] _keyRects = new Rect[12];
    private Rect[] _rootDotRects = new Rect[12];
    private Rect _scaleNameRect;
    private Rect _lastRect;

    // Scale dropdown state
    private bool _scaleDropdownOpen;
    private Rect _scaleDropdownRect;

    public override void DrawPanelShapes(Rect rect, ImCanvasContext ctx)
    {
        if (UI == null || UI.midiOutput == null) return;
        if (UI.paintModeActive) return;
        _lastRect = rect;

        UI.DrawPanelBg(rect);

        float x = rect.xMin + padding;
        float top = rect.yMax - padding;
        float w = rect.width - padding * 2;

        // ── Header ───────────────────────────────────────────────
        UI.SetFontSize(headerFontSize);
        Draw.Color = UI.textColor;
        Draw.Text(new Vector2(x, top), "Song Settings", TextAlign.TopLeft);
        top -= 28f;

        // ── Root Note ────────────────────────────────────────────
        UI.SetFontSize(labelFontSize);
        Draw.Color = UI.textDim;
        Draw.Text(new Vector2(x, top), "ROOT", TextAlign.TopLeft);
        top -= 16f;

        var midi = UI.midiOutput;
        string rootName = NoteNames[(int)midi.rootNote];
        UI.SetFontSize(rootNameFontSize);
        Draw.Color = UI.accentNote;
        Draw.Text(new Vector2(x, top), rootName, TextAlign.TopLeft);

        // Root selector dots (clickable) with ALL note names shown
        float dotStartX = x + 50f;
        float dotSpacing = (w - 50f) / 12f;
        for (int i = 0; i < 12; i++)
        {
            float dx = dotStartX + i * dotSpacing;
            float dy = top - 10f;
            bool isRoot = i == (int)midi.rootNote;
            Color dotCol = isRoot ? UI.accentNote : new Color(0.2f, 0.22f, 0.25f, 0.8f);
            float r = isRoot ? dotRadiusActive : dotRadius;
            Draw.Disc(new Vector2(dx, dy), r, dotCol);
            _rootDotRects[i] = new Rect(dx - 10f, dy - 10f, 20f, 20f);

            // Show all note names, larger for root
            UI.SetFontSize(isRoot ? 8f : dotNameFontSize);
            Draw.Color = isRoot ? UI.accentNote : UI.textDim;
            Draw.Text(new Vector2(dx, dy - 10f), NoteNames[i], TextAlign.Top);
        }
        top -= 34f;

        // ── Scale Type (clickable to open dropdown, scrollable) ──
        UI.SetFontSize(labelFontSize);
        Draw.Color = UI.textDim;
        Draw.Text(new Vector2(x, top), "SCALE", TextAlign.TopLeft);
        top -= 16f;

        string scaleName = FormatScaleName(midi.scaleType);
        UI.SetFontSize(scaleNameFontSize);
        Draw.Color = midi.scaleType == ScaleType.Custom ? UI.accent : UI.textColor;
        Draw.Text(new Vector2(x, top), scaleName, TextAlign.TopLeft);
        _scaleNameRect = new Rect(x, top - 20f, w, 24f);
        top -= 30f;

        // ── Piano Keyboard ───────────────────────────────────────
        UI.SetFontSize(labelFontSize);
        Draw.Color = UI.textDim;
        bool isCustom = midi.scaleType == ScaleType.Custom;
        Draw.Text(new Vector2(x, top), isCustom ? "TAP KEYS TO TOGGLE" : "ACTIVE NOTES", TextAlign.TopLeft);
        top -= 14f;

        DrawPianoKeyboard(x, top, w, keyboardHeight, midi);

        // Draw scale dropdown LAST (on top)
        if (_scaleDropdownOpen)
        {
            DrawScaleDropdown(midi);
        }
    }

    void DrawPianoKeyboard(float x, float top, float width, float height, FishMidiOutput midi)
    {
        int rootSemitone = (int)midi.rootNote;
        int[] activeIntervals = midi.scaleType == ScaleType.Custom
            ? MidiScales.GetCustomIntervals(midi)
            : MidiScales.GetIntervals(midi.scaleType);

        bool[] activeNotes = new bool[12];
        foreach (int interval in activeIntervals)
        {
            int note = (rootSemitone + interval) % 12;
            activeNotes[note] = true;
        }

        float whiteKeyW = width / 7f;
        float whiteKeyH = height;
        float blackKeyW = whiteKeyW * 0.6f;
        float blackKeyH = height * 0.6f;
        float keyGap = 1.5f;
        float bottom = top - whiteKeyH;

        // ── White keys ───────────────────────────────────────────
        int whiteIdx = 0;
        for (int i = 0; i < 12; i++)
        {
            if (IsBlackKey[i]) continue;
            float kx = x + whiteIdx * whiteKeyW + keyGap / 2f;
            float kw = whiteKeyW - keyGap;
            Rect keyRect = new Rect(kx, bottom, kw, whiteKeyH);
            _keyRects[i] = keyRect;

            bool active = activeNotes[i];
            Color keyCol;
            if (active)
                keyCol = UI.accentNote;  // orange for active
            else
                keyCol = Color.white;    // white for inactive white keys

            Draw.Rectangle(keyRect, 3f, keyCol);
            UI.SetFontSize(10f);
            Draw.Color = active ? new Color(0.1f, 0.1f, 0.1f, 0.8f) : new Color(0.5f, 0.5f, 0.5f, 0.5f);
            Draw.Text(new Vector2(kx + kw / 2f, bottom + 6f), NoteNames[i], TextAlign.Bottom);
            whiteIdx++;
        }

        // ── Black keys ───────────────────────────────────────────
        // Black keys sit between white keys at these positions
        // C# between C(0) and D(1), D# between D(1) and E(2),
        // F# between F(3) and G(4), G# between G(4) and A(5), A# between A(5) and B(6)
        float[] blackPositions = { 0.67f, 1.67f, 3.67f, 4.67f, 5.67f };
        int[] blackNotes = { 1, 3, 6, 8, 10 };
        for (int b = 0; b < 5; b++)
        {
            int noteIdx = blackNotes[b];
            float bx = x + blackPositions[b] * whiteKeyW - blackKeyW / 2f;
            Rect keyRect = new Rect(bx, bottom + whiteKeyH - blackKeyH, blackKeyW, blackKeyH);
            _keyRects[noteIdx] = keyRect;

            bool active = activeNotes[noteIdx];
            Color keyCol;
            if (active)
                keyCol = UI.accentNote;  // orange for active
            else
                keyCol = new Color(0.08f, 0.09f, 0.1f, 0.9f);  // black for inactive

            Draw.Rectangle(keyRect, 2f, keyCol);
            Draw.RectangleBorder(keyRect, 1f, 2f, new Color(0f, 0f, 0f, 0.5f));
        }
    }

    void DrawScaleDropdown(FishMidiOutput midi)
    {
        int count = System.Enum.GetValues(typeof(ScaleType)).Length;
        float itemH = 22f;
        float dropH = itemH * count;
        // Draw below the scale name rect (which is in panel-local space with Y-up)
        Rect dropRect = new Rect(_scaleNameRect.x, _scaleNameRect.yMin - dropH, _scaleNameRect.width, dropH);
        _scaleDropdownRect = dropRect;

        Draw.Rectangle(dropRect, 4f, new Color(0.06f, 0.08f, 0.1f, 0.95f));
        Draw.RectangleBorder(dropRect, 1f, 4f, UI.accent * 0.5f);

        for (int i = 0; i < count; i++)
        {
            // Draw from top of dropdown downward
            float itemY = dropRect.yMax - (i + 1) * itemH;
            Rect itemRect = new Rect(dropRect.x, itemY, dropRect.width, itemH);
            bool selected = i == (int)midi.scaleType;
            if (selected)
                Draw.Rectangle(itemRect, 2f, UI.accent * 0.3f);
            UI.SetFontSize(12f);
            Draw.Color = selected ? UI.accent : UI.textColor;
            Draw.Text(new Vector2(itemRect.x + 8f, itemRect.center.y), FormatScaleName((ScaleType)i), TextAlign.Left);
        }
    }

    // ── IFishPanel interaction ───────────────────────────────────

    public void OnPress(Vector2 pos)
    {
        if (UI == null || UI.midiOutput == null) return;
        FishSynthInput.InputConsumed = true;
        var midi = UI.midiOutput;

        // Handle scale dropdown click
        if (_scaleDropdownOpen)
        {
            int count = System.Enum.GetValues(typeof(ScaleType)).Length;
            float itemH = 22f;
            float dropH = itemH * count;
            Rect dropRect = new Rect(_scaleNameRect.x, _scaleNameRect.yMin - dropH, _scaleNameRect.width, dropH);

            if (dropRect.Contains(pos))
            {
                // Which item was clicked? Items drawn from top down
                int idx = Mathf.FloorToInt((dropRect.yMax - pos.y) / itemH);
                idx = Mathf.Clamp(idx, 0, count - 1);
                midi.scaleType = (ScaleType)idx;
                _scaleDropdownOpen = false;
                return;
            }
            _scaleDropdownOpen = false;
            return;
        }

        // Check root note dots
        for (int i = 0; i < 12; i++)
        {
            if (_rootDotRects[i].Contains(pos))
            {
                midi.rootNote = (RootNote)i;
                return;
            }
        }

        // Check scale name area (open dropdown)
        if (_scaleNameRect.Contains(pos))
        {
            _scaleDropdownOpen = true;
            return;
        }

        // Check piano keys (Custom mode only)
        if (midi.scaleType == ScaleType.Custom)
        {
            // Check black keys first (they overlap white keys)
            int[] blackNotes = { 1, 3, 6, 8, 10 };
            foreach (int n in blackNotes)
            {
                if (_keyRects[n].Contains(pos))
                {
                    ToggleCustomNote(midi, n);
                    return;
                }
            }
            // Then white keys
            for (int i = 0; i < 12; i++)
            {
                if (IsBlackKey[i]) continue;
                if (_keyRects[i].Contains(pos))
                {
                    ToggleCustomNote(midi, i);
                    return;
                }
            }
        }
    }

    public void OnDrag(Vector2 pos) { }
    public void OnRelease(Vector2 pos) { }

    public void OnScroll(Vector2 pos, float delta)
    {
        if (UI == null || UI.midiOutput == null) return;

        // Scroll on scale name to cycle scales
        if (_scaleNameRect.Contains(pos))
        {
            FishSynthInput.InputConsumed = true;
            int count = System.Enum.GetValues(typeof(ScaleType)).Length;
            int cur = (int)UI.midiOutput.scaleType;
            cur = (cur + (delta > 0 ? 1 : count - 1)) % count;
            UI.midiOutput.scaleType = (ScaleType)cur;
        }
    }

    void ToggleCustomNote(FishMidiOutput midi, int semitone)
    {
        switch (semitone)
        {
            case 0:  midi.customC  = !midi.customC;  break;
            case 1:  midi.customCs = !midi.customCs; break;
            case 2:  midi.customD  = !midi.customD;  break;
            case 3:  midi.customDs = !midi.customDs; break;
            case 4:  midi.customE  = !midi.customE;  break;
            case 5:  midi.customF  = !midi.customF;  break;
            case 6:  midi.customFs = !midi.customFs; break;
            case 7:  midi.customG  = !midi.customG;  break;
            case 8:  midi.customGs = !midi.customGs; break;
            case 9:  midi.customA  = !midi.customA;  break;
            case 10: midi.customAs = !midi.customAs; break;
            case 11: midi.customB  = !midi.customB;  break;
        }
    }

    string FormatScaleName(ScaleType s)
    {
        switch (s)
        {
            case ScaleType.NaturalMinor:    return "Natural Minor";
            case ScaleType.HarmonicMinor:   return "Harmonic Minor";
            case ScaleType.MelodicMinor:    return "Melodic Minor";
            case ScaleType.PentatonicMajor: return "Pentatonic Maj";
            case ScaleType.PentatonicMinor: return "Pentatonic Min";
            case ScaleType.WholeTone:       return "Whole Tone";
            default:                        return s.ToString();
        }
    }

    // ── IFishPanelDropdown ──────────────────────────────────────────────────────

    public bool HasOpenDropdown => _scaleDropdownOpen;

    public Rect GetDropdownRect() => _scaleDropdownRect;
}
