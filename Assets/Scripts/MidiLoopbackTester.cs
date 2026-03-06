using System;
using UnityEngine;
using System.Collections.Concurrent;

/// <summary>
/// Loopback MIDI tester. Listens on a MIDI input port and logs all incoming
/// CC messages to the console. Use alongside FishMidiOutput to confirm
/// messages are actually arriving.
///
/// Setup:
///   1. Attach to any GameObject
///   2. Set inputPortName to match your loopMIDI port (e.g. "loopMIDI Port Unity")
///   3. Hit Play — incoming CCs will log to the console
///   4. Remove when done testing
/// </summary>
public class MidiLoopbackTester : MonoBehaviour
{
    [Header("MIDI Input")]
    [Tooltip("Partial name match of the MIDI input port to listen on.")]
    public string inputPortName = "loopMIDI Port Unity";

    [Tooltip("Log every incoming message (can be spammy — turn off once confirmed working).")]
    public bool logAllMessages = true;

    [Tooltip("Only log CC messages on this channel (0 = all channels).")]
    [Range(0, 16)]
    public int filterChannel = 0;

    // ── Internal ──────────────────────────────────────────────────────────────

    private RtMidi.MidiIn _midiIn;
    private bool _ready;

    // Thread-safe queue — callback fires on native thread, we drain on main thread
    private readonly ConcurrentQueue<byte[]> _messageQueue = new ConcurrentQueue<byte[]>();

    // Latest received value per CC number for Inspector monitoring
    [Header("Last Received (read-only monitor)")]
    public int lastCcNumber;
    public int lastCcValue;
    public int lastChannel;

    // ─────────────────────────────────────────────────────────────────────────

    private void Start()
    {
        _midiIn = RtMidi.MidiIn.Create();
        int count = _midiIn.PortCount;

        Debug.Log($"[MidiLoopbackTester] Found {count} MIDI input port(s):");
        for (int i = 0; i < count; i++)
            Debug.Log($"  [{i}] {_midiIn.GetPortName(i)}");

        int targetPort = 0;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            if (_midiIn.GetPortName(i).Contains(inputPortName))
            {
                targetPort = i;
                found = true;
                break;
            }
        }

        if (!found)
        {
            Debug.LogWarning($"[MidiLoopbackTester] Port '{inputPortName}' not found. Listening on port 0.");
        }

        _midiIn.MessageReceived += OnMessageReceived;
        _midiIn.OpenPort(targetPort);
        _ready = true;

        Debug.Log($"[MidiLoopbackTester] Listening on port {targetPort}: {_midiIn.GetPortName(targetPort)}");
    }

    private void OnMessageReceived(double timeStamp, ReadOnlySpan<byte> data)
    {
        // Copy immediately — ReadOnlySpan<byte> is stack-only and the native
        // buffer may be recycled before the main thread processes it.
        _messageQueue.Enqueue(data.ToArray());
    }

    private void Update()
    {
        if (!_ready) return;

        while (_messageQueue.TryDequeue(out byte[] msg))
        {
            if (msg.Length < 3) continue;

            int msgType = msg[0] & 0xF0;
            int channel = (msg[0] & 0x0F) + 1; // 1-indexed

            if (filterChannel > 0 && channel != filterChannel) continue;

            if (msgType == 0xB0) // CC message
            {
                lastCcNumber = msg[1];
                lastCcValue  = msg[2];
                lastChannel  = channel;

                if (logAllMessages)
                    Debug.Log($"[MidiLoopbackTester] CC ch{channel} cc{msg[1]} = {msg[2]}");
            }
            else if (logAllMessages)
            {
                Debug.Log($"[MidiLoopbackTester] MSG status=0x{msg[0]:X2} d1={msg[1]} d2={msg[2]} ch{channel}");
            }
        }
    }

    private void OnDestroy()
    {
        if (_midiIn != null)
        {
            _midiIn.ClosePort();
            _midiIn.Dispose();
        }
    }
}
