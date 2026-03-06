using UnityEngine;

/// <summary>
/// Attach to any GameObject, hit Play — lists all available MIDI output ports to the console.
/// Remove after debugging.
/// </summary>
public class MidiPortLister : MonoBehaviour
{
    private void Start()
    {
        var midi = RtMidi.MidiOut.Create();
        int count = midi.PortCount;

        Debug.Log($"[MidiPortLister] Found {count} MIDI output port(s):");
        for (int i = 0; i < count; i++)
            Debug.Log($"  [{i}] {midi.GetPortName(i)}");

        midi.ClosePort();
        midi.Dispose();
    }
}
