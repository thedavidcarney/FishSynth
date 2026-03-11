using System;
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Reads FishTrackData from a YellowFishTracker and sends MIDI CC or Note
/// messages via RtMidi (keijiro/jp.keijiro.rtmidi).
///
/// Each mapping can independently operate in CC mode or Note mode.
/// In Note mode: input value is mapped to a scale degree within a note range,
/// a new note-on fires only when the scale degree changes (legato), and a
/// note-off is sent when detection is lost.
///
/// Requires: jp.keijiro.rtmidi package in the project.
/// </summary>
public class FishMidiOutput : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("References")]
    public YellowFishTracker tracker;

    [Header("MIDI Device")]
    [Tooltip("Partial name match of the MIDI output port to open. Leave empty to use port 0.")]
    public string midiPortName = "";

    [Header("Channel Mappings")]
    public MidiChannelMapping posX        = new MidiChannelMapping { label = "Pos X",  ccNumber = 20, midiChannel = 1, inputMin = 0f,   inputMax = 1f   };
    public MidiChannelMapping posY        = new MidiChannelMapping { label = "Pos Y",  ccNumber = 21, midiChannel = 1, inputMin = 0f,   inputMax = 1f   };
    public MidiChannelMapping velocityMag = new MidiChannelMapping { label = "Speed",  ccNumber = 22, midiChannel = 1, inputMin = 0f,   inputMax = 3f   };
    public MidiChannelMapping velX        = new MidiChannelMapping { label = "Vel X",  ccNumber = 23, midiChannel = 1, inputMin = -3f,  inputMax = 3f   };
    public MidiChannelMapping velY        = new MidiChannelMapping { label = "Vel Y",  ccNumber = 24, midiChannel = 1, inputMin = -3f,  inputMax = 3f   };
    public MidiChannelMapping size        = new MidiChannelMapping { label = "Size",   ccNumber = 25, midiChannel = 1, inputMin = 0f,   inputMax = 0.3f };

    [Header("Debug")]
    [Tooltip("Log every attempted send to console.")]
    public bool debugLogging = true;

    // ── Internal ──────────────────────────────────────────────────────────────

    private RtMidi.MidiOut _midiOut;
    private bool _midiReady;

    // CC suppression: key = channel * 1000 + ccNumber
    private readonly Dictionary<int, int> _lastSentCC = new Dictionary<int, int>();

    // ─────────────────────────────────────────────────────────────────────────

    private void Start()
    {
        OpenMidiPort();
    }

    private void OpenMidiPort()
    {
        _midiOut = RtMidi.MidiOut.Create();
        int portCount = _midiOut.PortCount;

        if (portCount == 0)
        {
            Debug.LogWarning("[FishMidiOutput] No MIDI output ports found.");
            return;
        }

        int targetPort = 0;
        if (!string.IsNullOrEmpty(midiPortName))
        {
            for (int i = 0; i < portCount; i++)
            {
                if (_midiOut.GetPortName(i).Contains(midiPortName))
                {
                    targetPort = i;
                    break;
                }
            }
        }

        _midiOut.OpenPort(targetPort);
        _midiReady = true;
        Debug.Log($"[FishMidiOutput] Opened MIDI port {targetPort}: {_midiOut.GetPortName(targetPort)}");
    }

    private void Update()
    {
        if (!_midiReady || tracker == null) return;

        FishTrackData d = tracker.Data;

        ProcessMapping(posX,        GetTrackerValue(posX.pitchSource,        d), d);
        ProcessMapping(posY,        GetTrackerValue(posY.pitchSource,        d), d);
        ProcessMapping(velocityMag, GetTrackerValue(velocityMag.pitchSource, d), d);
        ProcessMapping(velX,        GetTrackerValue(velX.pitchSource,        d), d);
        ProcessMapping(velY,        GetTrackerValue(velY.pitchSource,        d), d);
        ProcessMapping(size,        GetTrackerValue(size.pitchSource,        d), d);
    }

    // Returns the raw tracker value for whichever field a mapping uses as its source.
    // In CC mode pitchSource is irrelevant, but we still pass the mapping's own
    // "natural" value via the label so CC behaviour is unchanged.
    private float GetTrackerValue(TrackerField field, FishTrackData d)
    {
        switch (field)
        {
            case TrackerField.PosX:             return d.posX;
            case TrackerField.PosY:             return d.posY;
            case TrackerField.VelocityMag:      return d.velocityMagnitude;
            case TrackerField.VelX:             return d.velX;
            case TrackerField.VelY:             return d.velY;
            case TrackerField.Size:             return d.size;
            default:                            return 0f;
        }
    }

    // Returns the "natural" value for a mapping based on its label —
    // used so CC mode always reads the correct field regardless of pitchSource.
    private float GetNaturalValue(MidiChannelMapping mapping, FishTrackData d)
    {
        switch (mapping.label)
        {
            case "Pos X":  return d.posX;
            case "Pos Y":  return d.posY;
            case "Speed":  return d.velocityMagnitude;
            case "Vel X":  return d.velX;
            case "Vel Y":  return d.velY;
            case "Size":   return d.size;
            default:       return 0f;
        }
    }

    private void ProcessMapping(MidiChannelMapping mapping, float pitchValue, FishTrackData d)
    {
        if (!mapping.enabled) return;

        if (mapping.mode == MidiMode.CC)
        {
            float ccValue = GetNaturalValue(mapping, d);
            SendCC(mapping, ccValue, d.detected);
        }
        else
        {
            SendNote(mapping, pitchValue, d);
        }
    }

    // ── CC mode ───────────────────────────────────────────────────────────────

    private void SendCC(MidiChannelMapping mapping, float rawValue, bool detected)
    {
        if (!detected && mapping.holdOnLostDetection)
        {
            if (debugLogging) Debug.Log($"[FishMidiOutput] CC{mapping.ccNumber} skipped: not detected + holdOnLostDetection");
            return;
        }

        float t   = Mathf.InverseLerp(mapping.inputMin, mapping.inputMax, rawValue);
        t         = Mathf.Clamp01(t);
        int ccVal = Mathf.RoundToInt(Mathf.Lerp(mapping.outputMin, mapping.outputMax, t));
        ccVal     = Mathf.Clamp(ccVal, 0, 127);

        int key = mapping.midiChannel * 1000 + mapping.ccNumber;
        if (_lastSentCC.TryGetValue(key, out int last) && last == ccVal)
        {
            if (debugLogging) Debug.Log($"[FishMidiOutput] CC{mapping.ccNumber} suppressed: unchanged ({ccVal})");
            return;
        }
        _lastSentCC[key] = ccVal;

        byte status = (byte)(0xB0 | ((mapping.midiChannel - 1) & 0x0F));
        if (debugLogging) Debug.Log($"[FishMidiOutput] SENDING CC{mapping.ccNumber} = {ccVal} (raw={rawValue:F3}) ch{mapping.midiChannel}");
        _midiOut.SendMessage(new ReadOnlySpan<byte>(new byte[] { status, (byte)mapping.ccNumber, (byte)ccVal }));
    }

    // ── Note mode ─────────────────────────────────────────────────────────────

    private void SendNote(MidiChannelMapping mapping, float rawValue, FishTrackData d)
    {
        if (!d.detected)
        {
            // Fish lost — send note-off for whatever is currently sounding
            if (mapping.currentNote >= 0)
            {
                SendNoteOff(mapping, mapping.currentNote);
                mapping.currentNote = -1;
            }
            return;
        }

        int[] scale     = MidiScales.GetIntervals(mapping.scaleType);
        int rootMidi    = NoteNameToMidi(mapping.rootNote, mapping.rootOctave);
        int totalNotes  = scale.Length * mapping.octaveRange;

        // Map input range → scale index
        float t         = Mathf.InverseLerp(mapping.inputMin, mapping.inputMax, rawValue);
        t               = Mathf.Clamp01(t);
        int scaleIndex  = Mathf.Clamp(Mathf.FloorToInt(t * totalNotes), 0, totalNotes - 1);

        // Convert scale index to MIDI note number
        int octave      = scaleIndex / scale.Length;
        int degree      = scaleIndex % scale.Length;
        int noteNumber  = Mathf.Clamp(rootMidi + octave * 12 + scale[degree], 0, 127);

        // Only retrigger if scale degree has changed
        if (noteNumber == mapping.currentNote) return;

        // Send note-off for previous note first (legato)
        if (mapping.currentNote >= 0)
            SendNoteOff(mapping, mapping.currentNote);

        // Resolve velocity
        int velocity = mapping.velocitySource == VelocitySource.Fixed
            ? mapping.fixedVelocity
            : Mathf.Clamp(
                Mathf.RoundToInt(
                    Mathf.InverseLerp(mapping.velocityInputMin, mapping.velocityInputMax,
                        GetTrackerValue(mapping.velocityTrackerField, d)) * 127f),
                1, 127);

        SendNoteOn(mapping, noteNumber, velocity);
        mapping.currentNote = noteNumber;
    }

    private void SendNoteOn(MidiChannelMapping mapping, int note, int velocity)
    {
        byte status = (byte)(0x90 | ((mapping.midiChannel - 1) & 0x0F));
        if (debugLogging) Debug.Log($"[FishMidiOutput] NOTE ON  {note} vel={velocity} ch{mapping.midiChannel}");
        _midiOut.SendMessage(new ReadOnlySpan<byte>(new byte[] { status, (byte)note, (byte)velocity }));
    }

    private void SendNoteOff(MidiChannelMapping mapping, int note)
    {
        byte status = (byte)(0x80 | ((mapping.midiChannel - 1) & 0x0F));
        if (debugLogging) Debug.Log($"[FishMidiOutput] NOTE OFF {note} ch{mapping.midiChannel}");
        _midiOut.SendMessage(new ReadOnlySpan<byte>(new byte[] { status, (byte)note, 0 }));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private int NoteNameToMidi(RootNote note, int octave)
    {
        // MIDI note 60 = C4; octave param is added directly
        int semitone = (int)note; // enum values match semitone offsets from C
        return (octave + 1) * 12 + semitone; // +1 because MIDI octave -1 = 0
    }

    private void OnDestroy()
    {
        // Silence any held notes before shutting down
        AllNotesOff();
        _midiOut?.ClosePort();
        _midiOut?.Dispose();
    }

    private void AllNotesOff()
    {
        if (!_midiReady) return;
        MidiChannelMapping[] all = { posX, posY, velocityMag, velX, velY, size };
        foreach (var m in all)
        {
            if (m.mode == MidiMode.Note && m.currentNote >= 0)
            {
                SendNoteOff(m, m.currentNote);
                m.currentNote = -1;
            }
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Enums
// ─────────────────────────────────────────────────────────────────────────────

public enum MidiMode
{
    CC,
    Note
}

public enum RootNote
{
    C  = 0,
    Cs = 1,  // C#/Db
    D  = 2,
    Ds = 3,  // D#/Eb
    E  = 4,
    F  = 5,
    Fs = 6,  // F#/Gb
    G  = 7,
    Gs = 8,  // G#/Ab
    A  = 9,
    As = 10, // A#/Bb
    B  = 11
}

public enum ScaleType
{
    Chromatic,
    Major,
    NaturalMinor,
    HarmonicMinor,
    MelodicMinor,
    PentatonicMajor,
    PentatonicMinor,
    Blues,
    Dorian,
    Phrygian,
    Lydian,
    Mixolydian,
    WholeTone,
    Diminished
}

public enum TrackerField
{
    PosX,
    PosY,
    VelocityMag,
    VelX,
    VelY,
    Size
}

public enum VelocitySource
{
    Fixed,
    PosX,
    PosY,
    VelocityMag,
    VelX,
    VelY,
    Size
}

// ─────────────────────────────────────────────────────────────────────────────
// Scale definitions
// ─────────────────────────────────────────────────────────────────────────────

public static class MidiScales
{
    public static int[] GetIntervals(ScaleType type)
    {
        switch (type)
        {
            case ScaleType.Chromatic:        return new[] { 0,1,2,3,4,5,6,7,8,9,10,11 };
            case ScaleType.Major:            return new[] { 0,2,4,5,7,9,11 };
            case ScaleType.NaturalMinor:     return new[] { 0,2,3,5,7,8,10 };
            case ScaleType.HarmonicMinor:    return new[] { 0,2,3,5,7,8,11 };
            case ScaleType.MelodicMinor:     return new[] { 0,2,3,5,7,9,11 };
            case ScaleType.PentatonicMajor:  return new[] { 0,2,4,7,9 };
            case ScaleType.PentatonicMinor:  return new[] { 0,3,5,7,10 };
            case ScaleType.Blues:            return new[] { 0,3,5,6,7,10 };
            case ScaleType.Dorian:           return new[] { 0,2,3,5,7,9,10 };
            case ScaleType.Phrygian:         return new[] { 0,1,3,5,7,8,10 };
            case ScaleType.Lydian:           return new[] { 0,2,4,6,7,9,11 };
            case ScaleType.Mixolydian:       return new[] { 0,2,4,5,7,9,10 };
            case ScaleType.WholeTone:        return new[] { 0,2,4,6,8,10 };
            case ScaleType.Diminished:       return new[] { 0,2,3,5,6,8,9,11 };
            default:                         return new[] { 0,2,4,5,7,9,11 };
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Mapping data class
// ─────────────────────────────────────────────────────────────────────────────

[System.Serializable]
public class MidiChannelMapping
{
    [HideInInspector] public string label;

    [Tooltip("Enable/disable this channel without removing it.")]
    public bool enabled = true;

    [Tooltip("CC mode: sends continuous controller. Note mode: sends MIDI notes on a scale.")]
    public MidiMode mode = MidiMode.CC;

    [Tooltip("MIDI channel (1–16).")]
    [Range(1, 16)]
    public int midiChannel = 1;

    // ── CC mode fields ────────────────────────────────────────────────────────

    [Header("CC Mode")]
    [Tooltip("MIDI CC number (0–127). Used in CC mode only.")]
    [Range(0, 127)]
    public int ccNumber;

    [Tooltip("Tracker value that maps to outputMin.")]
    public float inputMin = 0f;

    [Tooltip("Tracker value that maps to outputMax.")]
    public float inputMax = 1f;

    [Tooltip("MIDI CC value at inputMin.")]
    [Range(0, 127)]
    public int outputMin = 0;

    [Tooltip("MIDI CC value at inputMax.")]
    [Range(0, 127)]
    public int outputMax = 127;

    [Tooltip("When fish is not detected, hold the last sent value instead of sending 0.")]
    public bool holdOnLostDetection = true;

    // ── Note mode fields ──────────────────────────────────────────────────────

    [Header("Note Mode")]
    [Tooltip("Which tracker field drives pitch (used in Note mode).")]
    public TrackerField pitchSource = TrackerField.PosX;

    [Tooltip("Root note of the scale.")]
    public RootNote rootNote = RootNote.C;

    [Tooltip("Octave of the root note (4 = middle C octave).")]
    [Range(0, 8)]
    public int rootOctave = 4;

    [Tooltip("Scale type to quantize pitch to.")]
    public ScaleType scaleType = ScaleType.Major;

    [Tooltip("Number of octaves the input range spans.")]
    [Range(1, 6)]
    public int octaveRange = 2;

    [Tooltip("Fixed: always use fixedVelocity. Any other: read from that tracker field.")]
    public VelocitySource velocitySource = VelocitySource.Fixed;

    [Tooltip("Velocity value when velocitySource = Fixed.")]
    [Range(1, 127)]
    public int fixedVelocity = 100;

    [Tooltip("Tracker value that maps to velocity 0 (used when velocitySource != Fixed).")]
    public float velocityInputMin = 0f;

    [Tooltip("Tracker value that maps to velocity 127 (used when velocitySource != Fixed).")]
    public float velocityInputMax = 3f;

    [Tooltip("Tracker field to read for velocity (used when velocitySource != Fixed).")]
    public TrackerField velocityTrackerField = TrackerField.VelocityMag;

    // ── Runtime state (not serialized) ────────────────────────────────────────

    [System.NonSerialized] public int currentNote = -1; // -1 = no note playing
}
