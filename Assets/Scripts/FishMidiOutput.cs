using System;
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Reads FishTrackData from a YellowFishTracker and sends MIDI CC messages
/// via RtMidi (keijiro/jp.keijiro.rtmidi).
///
/// Each data channel is independently configurable: CC number, MIDI channel,
/// input range, output range, smoothing, and an optional "hold last value on
/// lost detection" toggle.
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

    // Track last sent value per channel to suppress redundant sends
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

        SendMapping(posX,        d.posX,              d.detected);
        SendMapping(posY,        d.posY,              d.detected);
        SendMapping(velocityMag, d.velocityMagnitude, d.detected);
        SendMapping(velX,        d.velX,              d.detected);
        SendMapping(velY,        d.velY,              d.detected);
        SendMapping(size,        d.size,              d.detected);
    }

    private void SendMapping(MidiChannelMapping mapping, float rawValue, bool detected)
    {
        if (!mapping.enabled)
        {
            if (debugLogging) Debug.Log($"[FishMidiOutput] CC{mapping.ccNumber} skipped: disabled");
            return;
        }
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
            if (debugLogging) Debug.Log($"[FishMidiOutput] CC{mapping.ccNumber} suppressed: value unchanged ({ccVal})");
            return;
        }
        _lastSentCC[key] = ccVal;

        byte status = (byte)(0xB0 | ((mapping.midiChannel - 1) & 0x0F));
        if (debugLogging) Debug.Log($"[FishMidiOutput] SENDING CC{mapping.ccNumber} = {ccVal} (raw={rawValue:F3}) on ch{mapping.midiChannel}");
        _midiOut.SendMessage(new ReadOnlySpan<byte>(new byte[] { status, (byte)mapping.ccNumber, (byte)ccVal }));
    }

    private void OnDestroy()
    {
        _midiOut?.ClosePort();
        _midiOut?.Dispose();
    }
}

// ─────────────────────────────────────────────────────────────────────────────

[System.Serializable]
public class MidiChannelMapping
{
    [HideInInspector] public string label;

    [Tooltip("Enable/disable this channel without removing it.")]
    public bool enabled = true;

    [Tooltip("MIDI CC number (0–127).")]
    [Range(0, 127)]
    public int ccNumber;

    [Tooltip("MIDI channel (1–16).")]
    [Range(1, 16)]
    public int midiChannel = 1;

    [Header("Input Range (tracker units)")]
    [Tooltip("Tracker value that maps to outputMin.")]
    public float inputMin = 0f;

    [Tooltip("Tracker value that maps to outputMax.")]
    public float inputMax = 1f;

    [Header("Output Range (MIDI)")]
    [Tooltip("MIDI CC value at inputMin.")]
    [Range(0, 127)]
    public int outputMin = 0;

    [Tooltip("MIDI CC value at inputMax.")]
    [Range(0, 127)]
    public int outputMax = 127;

    [Tooltip("When fish is not detected, hold the last sent value instead of sending 0.")]
    public bool holdOnLostDetection = true;
}
