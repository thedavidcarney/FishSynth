using UnityEngine;
using UnityEngine.UI;
using Shapes;

/// <summary>
/// Wires up the FishSynth UI references and ensures the video canvas
/// renders behind the Shapes overlay canvas.
///
/// Setup:
///   1. Your existing video Canvas stays as-is (Screen Space Overlay, sort 0)
///   2. Create a SECOND Canvas in the scene for the Shapes UI:
///      - Add FishSynthUI (ImmediateModeCanvas) component
///      - Set to Screen Space Camera, assign Main Camera, plane distance 1
///      - Sort order 0 (same as video canvas — Shapes draws in camera pass, after canvas)
///   3. Make the VIDEO canvas Screen Space Camera too, same camera, plane distance 10
///      (further from camera = behind the Shapes canvas)
///   4. Add panel GameObjects as children of the Shapes canvas:
///      - Each gets a RectTransform (position/size in the editor)
///      - Each gets its panel component (TrackingPanel, SongSettingsPanel, etc.)
///      - They auto-register with FishSynthUI via ImmediateModePanel.OnEnable
///   5. Put this component anywhere, wire the references.
///
/// This script just wires cross-references at startup. All layout is
/// done manually in the Unity editor via RectTransforms.
/// </summary>
public class FishSynthUILayout : MonoBehaviour
{
    [Header("References")]
    public FishSynthUI fishSynthUI;
    public YellowFishTracker tracker;
    public FishMidiOutput midiOutput;
    public VideoFileInput videoInput;

    [Header("Panels (assign from scene)")]
    public TrackingPanel trackingPanel;
    public SongSettingsPanel songPanel;
    public ChannelStripPanel channelPanel;
    public StatusBarPanel statusBarPanel;

    void Awake()
    {
        // Wire references into the UI root
        if (fishSynthUI != null)
        {
            fishSynthUI.tracker = tracker;
            fishSynthUI.midiOutput = midiOutput;
            fishSynthUI.videoInput = videoInput;
        }

        // Wire MIDI output references
        if (midiOutput != null)
        {
            midiOutput.statusBar = statusBarPanel;
            midiOutput.synthUI = fishSynthUI;
        }
    }
}
