# FishSynth — Claude Code Context

## What This Project Is
A Unity project that tracks a yellow fish in a video feed using GPU compute shaders and outputs the fish's real-time position, velocity, and size as MIDI messages (CC or Notes) for generative audio/visual performance. The fish controls the music.

## Platform
- **Development machine:** Mac
- **MIDI:** IAC Driver (built-in macOS virtual MIDI bus) via `jp.keijiro.rtmidi`
- **Unity version:** 2022.3.62f3

## Pipeline Overview
```
Video (RenderTexture)
  → GPU Compute (HSV mask → Erode → Dilate → Erode → ReduceMoments)
  → CPU readback
  → EMA smoothing + dead reckoning
  → MIDI CC or Note output (RtMidi / CoreMIDI)
```

---

## Scripts

### `FishHueMask.compute`
GPU compute shader with 5 kernels run in sequence each frame:
1. **HueMask** — HSV threshold → white/black into `MaskA`
2. **Erode** (MaskA → MaskB) — removes noise
3. **Dilate** (MaskB → MaskA) — bridges gaps
4. **Erode** (MaskA → MaskB) — shrinks to fish size
5. **ReduceMoments** — group-shared reduction, outputs `[count, sumX, sumY, minX, minY, maxX, maxY]` per threadgroup into a `ComputeBuffer`

Hue wraps correctly for red range. Morphology ping-pongs between `MaskA`/`MaskB` via `MorphSrc`/`MorphDst`.

---

### `YellowFishTracker.cs`
Main tracking MonoBehaviour. Owns the compute pipeline, outputs `FishTrackData` via `Data` property.

**Per-frame:**
- Dispatches all 5 compute passes
- CPU readback of `ComputeBuffer` (7 floats × numGroups)
- Accumulates partial moments → centroid + bbox
- If `totalCount >= minBlobPixels`: detected, update EMA position/velocity
- If not detected: dead-reckon on last velocity for `deadReckonDuration` seconds, then freeze

**Config:** Auto-saves all tunable values to `Assets/StreamingAssets/FishTrackerConfig.json` on any Inspector change. Loaded on Start.

**Key Inspector fields:**
- HSV: `hueMin/Max`, `satMin/Max`, `valMin/Max` (0–1; yellow ≈ 0.167)
- Morphology: `erode1Radius`, `dilateRadius`, `erode2Radius`
- `minBlobPixels` — detection threshold
- `positionSmoothing`, `velocitySmoothing` — EMA alphas
- `deadReckonDuration`, `maxVelocity`

**`FishTrackData` struct:**
```csharp
bool  detected
float posX, posY              // normalized 0–1, Y=0 bottom Y=1 top
float velX, velY
float velocityMagnitude
float size                    // sqrt(bboxW * bboxH)
int   blobPixelCount
float bboxMinX/Y, bboxMaxX/Y  // normalized bbox
```

---

### `FishMidiOutput.cs`
Reads `tracker.Data` each Update, sends MIDI via RtMidi. Each of the 6 mappings is independently configurable in **CC mode** or **Note mode**.

**6 mappings (defaults):**
| Label    | CC# | Input Range |
|----------|-----|-------------|
| Pos X    | 20  | 0–1         |
| Pos Y    | 21  | 0–1         |
| Speed    | 22  | 0–3         |
| Vel X    | 23  | -3–3        |
| Vel Y    | 24  | -3–3        |
| Size     | 25  | 0–0.3       |

**CC mode:** Maps input range → 0–127, suppresses unchanged values, optional hold-on-lost.

**Note mode:**
- `pitchSource` — which `TrackerField` drives pitch (PosX, PosY, VelocityMag, VelX, VelY, Size)
- `rootNote` (C default) + `rootOctave` (4 default)
- `scaleType` — 14 options (see below)
- `octaveRange` — how many octaves the input spans (1–6)
- `velocitySource` — Fixed (default 100) or any TrackerField
- Legato: note-on only fires when scale degree changes; note-off sent before each new note-on
- Fish lost: immediate note-off, silence until redetected
- `OnDestroy`: all held notes silenced (no stuck notes on Play exit)

**Supported scales:** Chromatic, Major, Natural Minor, Harmonic Minor, Melodic Minor, Pentatonic Major, Pentatonic Minor, Blues, Dorian, Phrygian, Lydian, Mixolydian, Whole Tone, Diminished

**Port selection:** `midiPortName` does partial string match on output ports. For IAC: set to `"IAC Driver"` or `"Bus 1"`. Leave empty to use port 0.

**Enums:** `MidiMode`, `RootNote`, `ScaleType`, `TrackerField`, `VelocitySource`

**Static helper:** `MidiScales.GetIntervals(ScaleType)` returns semitone interval array.

---

### `VideoFileInput.cs`
Wraps `VideoPlayer` for testing with a local file. Creates a `RenderTexture` on prepare, pushes it to `YellowFishTracker.videoTexture`, calls `tracker.InitCompute()`, wires debug UI textures. Video files go in `Assets/StreamingAssets/` or use `absolutePath` override.

---

### `FishDebugCanvas.cs`
UI overlay:
- `videoImage` (RawImage) — raw video
- `maskImage` (RawImage) — HSV mask with `maskBlend` alpha
- 4 UI `Image` bars forming a bounding box from normalized bbox coords
- `showBboxWhenLost` — orange bbox during dead-reckoning
- Y-axis flip handled: tracker Y=0 is bottom, UI Y=0 is bottom

---

### Diagnostic Scripts
- **`MidiPortLister.cs`** — logs all MIDI output ports on Start, then disposes. Use to verify IAC Driver is visible.
- **`MidiLoopbackTester.cs`** — opens a MIDI input port by name, logs all incoming CC/Note messages via callback. `inputPortName` should match the IAC port (e.g. `"IAC Driver Bus 1"`). Inspector shows `lastCcNumber/Value/Channel` for live monitoring.

---

## Scene Setup
1. `VideoFileInput` on a GameObject → drives tracker + debug UI
2. `YellowFishTracker` (same or separate GO) — assign `fishComputeShader`
3. `FishMidiOutput` — references tracker, set `midiPortName`
4. Debug Canvas with `FishDebugCanvas` (optional)
5. `MidiPortLister` and/or `MidiLoopbackTester` on any GO for diagnostics (remove after confirming)

---

## Dependencies
- `jp.keijiro.rtmidi` — MIDI I/O via RtMidi (CoreMIDI on Mac)
- Unity VideoPlayer (built-in)
- Compute shader requires GPU with CS support

---

## MIDI Setup (Mac)
- **Audio MIDI Setup** → Window → Show MIDI Studio → double-click **IAC Driver** → check "Device is online"
- Default port name: `"IAC Driver Bus 1"` (rename to `"IAC Driver FishSynth"` etc. if desired)
- Set `midiPortName` in FishMidiOutput Inspector to partial match, e.g. `"IAC Driver"`
- IAC is simultaneously input + output — loopback testing works on the same port

---

## Current State
- Mac development, CoreMIDI/IAC working
- CC mode confirmed working: CC20–25 sending with suppression and hold logic
- Note mode implemented: legato, scale quantization, per-mapping pitch source and velocity source
- Windows MIDI (WinMM) was abandoned due to driver issues — do not pursue

---

## Known Design Decisions
- **1 synth assumed** — if multiple mappings are in Note mode simultaneously they'll all send on their respective MIDI channels. Keep only one in Note mode for clean monophonic behavior.
- **Velocity source is read-only** — setting velocitySource to VelocityMag does not affect that field's own CC output; the two are independent.
- **Scale degree change = retrigger** — same note number never retriggered (no per-frame spam).
- **`FishTrackerConfig.json`** — can be committed to git for shared HSV/morphology defaults, or .gitignored for per-machine tuning.
