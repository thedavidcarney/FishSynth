using UnityEngine;
using Shapes;

/// <summary>
/// Bottom status bar: fish detection icon, FPS, scrolling MIDI log,
/// MUTE button, video source, MIDI port.
/// </summary>
public class StatusBarPanel : ImmediateModePanel, IFishPanel
{
    FishSynthUI _ui;
    FishSynthUI UI => _ui != null ? _ui : (_ui = GetComponentInParent<FishSynthUI>());

    [Header("Layout")]
    public float padding = 8f;
    public float separatorInset = 6f;
    public float separatorThickness = 1f;
    public float fishIconRadius = 5f;
    public float fishIconOffset = 8f;
    public float fpsWidth = 50f;
    public float fpsFontSize = 13f;
    public float logFontSize = 13f;
    public int logMaxEntries = 6;
    public float muteButtonWidth = 60f;
    public float muteButtonInset = 4f;
    public float muteFontSize = 11f;
    public float rightSectionWidth = 220f;
    public float videoSectionOffset = 140f;
    public float midiSectionOffset = 60f;
    public float iconFontSize = 12f;
    public float portLabelFontSize = 12f;
    public int portTruncateLength = 10;

    // Hit zones
    private Rect _muteRect;
    private Rect _portRect;

    // Port text editing
    private bool _editingPort;
    private string _editBuffer;

    // FPS tracking
    private float _fpsTimer;
    private int _fpsFrames;
    private float _displayFps;

    // MIDI log ring buffer
    private string[] _logEntries = new string[32];
    private int _logHead = 0;
    private int _logCount = 0;

    private void Update()
    {
        // FPS counter
        _fpsFrames++;
        _fpsTimer += Time.unscaledDeltaTime;
        if (_fpsTimer >= 0.5f)
        {
            _displayFps = _fpsFrames / _fpsTimer;
            _fpsFrames = 0;
            _fpsTimer = 0f;
        }

        // Port text editing input
        if (_editingPort)
        {
            // Handle typed characters
            foreach (char c in Input.inputString)
            {
                if (c == '\b') // Backspace
                {
                    if (_editBuffer.Length > 0)
                        _editBuffer = _editBuffer.Substring(0, _editBuffer.Length - 1);
                }
                else if (c == '\n' || c == '\r') // Enter - commit
                {
                    CommitPortEdit();
                }
                else if (c == 27) // Escape - cancel
                {
                    _editingPort = false;
                }
                else
                {
                    _editBuffer += c;
                }
            }

            // Also check Escape key directly
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                _editingPort = false;
            }
        }
    }

    /// <summary>Call from FishMidiOutput to log a MIDI message to the ticker.</summary>
    public void LogMidi(string msg)
    {
        _logEntries[_logHead] = msg;
        _logHead = (_logHead + 1) % _logEntries.Length;
        if (_logCount < _logEntries.Length) _logCount++;
    }

    public override void DrawPanelShapes(Rect rect, ImCanvasContext ctx)
    {
        if (UI == null) return;
        UI.DrawPanelBg(rect);

        float y = rect.center.y;
        float left = rect.xMin + padding;
        float right = rect.xMax - padding;

        UI.SetFontSize(fpsFontSize);

        // ── Fish detection icon ──────────────────────────────────
        var tracker = UI.tracker;
        bool detected = tracker != null && tracker.Data.detected;
        bool hasData = tracker != null && (tracker.Data.bboxMaxX > tracker.Data.bboxMinX);
        Color fishColor;
        if (detected)
            fishColor = UI.statusGood;
        else if (hasData)
            fishColor = UI.statusWarn;
        else
            fishColor = UI.statusBad;

        // Fish silhouette: disc body + triangle tail via 3 vertices
        Vector2 fishPos = new Vector2(left + fishIconOffset, y);
        Draw.Color = fishColor;
        Draw.Disc(fishPos, fishIconRadius);
        Vector3 tailA = new Vector3(fishPos.x - fishIconRadius, fishPos.y + fishIconRadius, 0f);
        Vector3 tailB = new Vector3(fishPos.x - fishIconRadius, fishPos.y - fishIconRadius, 0f);
        Vector3 tailC = new Vector3(fishPos.x - fishIconRadius * 2.2f, fishPos.y, 0f);
        Draw.Triangle(tailA, tailB, tailC, fishColor);
        // Eye
        Draw.Disc(fishPos + new Vector2(fishIconRadius * 0.4f, fishIconRadius * 0.3f), fishIconRadius * 0.24f, Color.black);

        left += fishIconOffset + fishIconRadius * 2f + 6f;

        // ── FPS ──────────────────────────────────────────────────
        UI.SetFontSize(fpsFontSize);
        Draw.Color = UI.textColor;
        string fpsText = $"{_displayFps:F0}fps";
        Draw.Text(new Vector2(left, y), fpsText, TextAlign.Left);
        left += fpsWidth;

        // ── Separator ────────────────────────────────────────────
        Draw.Line(new Vector2(left, rect.yMin + separatorInset), new Vector2(left, rect.yMax - separatorInset), separatorThickness, UI.panelBorder);
        left += 10f;

        // ── MIDI log ticker ──────────────────────────────────────
        UI.SetFontSize(logFontSize);
        Draw.Color = UI.textDim;
        if (_logCount > 0)
        {
            string logLine = "";
            int idx = (_logHead - 1 + _logEntries.Length) % _logEntries.Length;
            for (int i = 0; i < Mathf.Min(_logCount, logMaxEntries); i++)
            {
                string entry = _logEntries[idx];
                if (entry != null)
                    logLine = entry + "  " + logLine;
                idx = (idx - 1 + _logEntries.Length) % _logEntries.Length;
            }
            Draw.Text(new Vector2(left, y), logLine, TextAlign.Left);
        }
        else
        {
            Draw.Text(new Vector2(left, y), "no MIDI activity", TextAlign.Left);
        }

        // ── MUTE button ─────────────────────────────────────────
        float muteX = right - rightSectionWidth + 30f;
        Rect muteRect = new Rect(muteX - muteButtonWidth / 2f, rect.yMin + muteButtonInset, muteButtonWidth, rect.height - muteButtonInset * 2f);
        _muteRect = muteRect;
        bool muted = UI.midiMuted;
        Color muteBg = muted ? UI.statusBad : new Color(0.15f, 0.18f, 0.22f, 0.9f);
        Draw.Rectangle(muteRect, 4f, muteBg);
        Draw.RectangleBorder(muteRect, 1f, 4f, muted ? UI.statusBad : UI.panelBorder);
        Draw.Color = muted ? Color.white : UI.textColor;
        UI.SetFontSize(muteFontSize);
        Draw.Text(muteRect.center, muted ? "MUTED" : "MUTE", TextAlign.Center);

        // ── Separator ────────────────────────────────────────────
        float sepX = muteRect.xMax + 10f;
        Draw.Line(new Vector2(sepX, rect.yMin + separatorInset), new Vector2(sepX, rect.yMax - separatorInset), separatorThickness, UI.panelBorder);

        // ── Video source indicator ───────────────────────────────
        float vidX = right - videoSectionOffset;
        Draw.Color = UI.textDim;
        UI.SetFontSize(iconFontSize);
        // Camera icon: small rect
        Rect camIcon = new Rect(vidX, y - 4f, 8f, 8f);
        Draw.Rectangle(camIcon, 1f, UI.textDim);
        // Small lens triangle
        Draw.Triangle(
            new Vector3(vidX + 9f, y + 3f, 0f),
            new Vector3(vidX + 9f, y - 3f, 0f),
            new Vector3(vidX + 13f, y, 0f),
            UI.textDim);
        Draw.Color = UI.textColor;
        UI.SetFontSize(iconFontSize);
        string vidLabel = UI.videoInput != null ? "Video" : "No Input";
        Draw.Text(new Vector2(vidX + 16f, y), vidLabel, TextAlign.Left);

        // ── Separator ────────────────────────────────────────────
        float sep2X = right - midiSectionOffset - 10f;
        Draw.Line(new Vector2(sep2X, rect.yMin + separatorInset), new Vector2(sep2X, rect.yMax - separatorInset), separatorThickness, UI.panelBorder);

        // ── MIDI port indicator ──────────────────────────────────
        float midiX = right - midiSectionOffset;
        // Musical note icon
        Draw.Disc(new Vector2(midiX + 3f, y - 2f), 3f, UI.accent);
        Draw.Line(new Vector2(midiX + 5.5f, y - 2f), new Vector2(midiX + 5.5f, y + 6f), 1.5f, UI.accent);

        float portTextX = midiX + 12f;
        float portW = right - portTextX;
        _portRect = new Rect(portTextX - 2f, rect.yMin + 2f, portW + 2f, rect.height - 4f);

        if (_editingPort)
        {
            // Draw text input box with accent border
            Draw.Rectangle(_portRect, 3f, new Color(0.08f, 0.1f, 0.14f, 0.95f));
            Draw.RectangleBorder(_portRect, 1.5f, 3f, UI.accent);
            Draw.Color = UI.textColor;
            UI.SetFontSize(portLabelFontSize);
            string cursor = (Time.time % 1f < 0.5f) ? "|" : "";
            Draw.Text(new Vector2(portTextX + 2f, y), _editBuffer + cursor, TextAlign.Left);
        }
        else
        {
            Draw.Color = UI.textColor;
            UI.SetFontSize(portLabelFontSize);
            string portLabel = "MIDI";
            if (UI.midiOutput != null && !string.IsNullOrEmpty(UI.midiOutput.midiPortName))
                portLabel = UI.midiOutput.midiPortName;
            if (portLabel.Length > portTruncateLength)
                portLabel = portLabel.Substring(0, portTruncateLength) + "..";
            Draw.Text(new Vector2(portTextX, y), portLabel, TextAlign.Left);
        }
    }

    // ── IFishPanel ───────────────────────────────────────────────

    public void OnPress(Vector2 pos)
    {
        if (UI == null) return;
        FishSynthInput.InputConsumed = true;

        // If editing port and click outside, commit and exit
        if (_editingPort)
        {
            if (!_portRect.Contains(pos))
            {
                CommitPortEdit();
            }
            return;
        }

        if (_muteRect.Contains(pos))
        {
            UI.midiMuted = !UI.midiMuted;
            // If unmuting, send all-notes-off to clear stuck notes
            if (!UI.midiMuted && UI.midiOutput != null)
            {
                // AllNotesOff is private, but mute flag in SendNote handles it
            }
            return;
        }

        // Click on port label to enter edit mode
        if (_portRect.Contains(pos))
        {
            _editingPort = true;
            _editBuffer = UI.midiOutput != null ? (UI.midiOutput.midiPortName ?? "") : "";
            return;
        }
    }

    void CommitPortEdit()
    {
        if (UI.midiOutput != null)
            UI.midiOutput.midiPortName = _editBuffer;
        _editingPort = false;
    }

    public void OnDrag(Vector2 pos) { }
    public void OnRelease(Vector2 pos) { }
    public void OnScroll(Vector2 pos, float delta) { }
}
