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

**Global settings on FishMidiOutput (shared by all channels):**
- `rootNote` (C default) — root of the scale
- `scaleType` — 15 options including Custom
- `customC` through `customB` — 12 booleans defining the Custom scale (which semitones are active)

**6 mappings (defaults):**
| Label    | CC# | Input Range |
|----------|-----|-------------|
| Pos X    | 20  | 0–1         |
| Pos Y    | 21  | 0–1         |
| Speed    | 22  | 0–3         |
| Vel X    | 23  | -3–3        |
| Vel Y    | 24  | -3–3        |
| Size     | 25  | 0–0.3       |

Each mapping's pitch source is implicit — Pos X mapping always reads `d.posX`, etc. No `pitchSource` field.

**CC mode:** Maps input range → 0–127, suppresses unchanged values, optional hold-on-lost.

**Note mode (per-channel settings):**
- `rootOctave` (4 default) — octave of the root note
- `octaveRange` — how many octaves the input spans (1–6)
- `velocitySource` — Fixed (default 100) or any TrackerField
- Legato: note-on only fires when scale degree changes; note-off sent before each new note-on
- `noteOrder` — Sequential, Random, or Shuffle
- Fish lost: immediate note-off, silence until redetected
- `OnDestroy`: all held notes silenced (no stuck notes on Play exit)

**Supported scales:** Chromatic, Major, Natural Minor, Harmonic Minor, Melodic Minor, Pentatonic Major, Pentatonic Minor, Blues, Dorian, Phrygian, Lydian, Mixolydian, Whole Tone, Diminished, Custom

**Port selection:** `midiPortName` does partial string match on output ports. For IAC: set to `"IAC Driver"` or `"Bus 1"`. Leave empty to use port 0.

**Enums:** `MidiMode`, `RootNote`, `ScaleType`, `TrackerField`, `VelocitySource`, `NoteOrder`

**Static helpers:** `MidiScales.GetIntervals(ScaleType)` returns semitone interval array. `MidiScales.GetCustomIntervals(FishMidiOutput)` builds interval array from the 12 custom booleans.

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

### `MaskPainter.cs`
Runtime-drawable exclusion mask for noise filtering. Lets the user paint regions to exclude from tracking.

**Controls:**
- **Left-click** — paint (exclude region, shown as red overlay)
- **Right-click** — erase (re-allow region)
- **Scroll wheel** — adjust brush radius (5–200 mask pixels)

**Mask:** 1920×1080 `Texture2D`, white = allow, black = exclude. Saved as `Assets/StreamingAssets/ExclusionMask.png` (auto-saved 1s after last edit, auto-loaded on Start).

**Integration:** Sets `tracker.exclusionMask` at runtime. The compute shader's `HueMask` kernel samples the mask via UV and discards excluded pixels before HSV thresholding.

**Setup:** Attach to any GameObject, assign `tracker` and `videoImage` (the video RawImage from FishDebugCanvas). A red semi-transparent overlay and brush cursor are created automatically.

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
5. `MaskPainter` on any GO — assign tracker + videoImage for runtime noise masking
6. `MidiPortLister` and/or `MidiLoopbackTester` on any GO for diagnostics (remove after confirming)

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
- Scale settings (rootNote, scaleType, custom booleans) are global on FishMidiOutput — shared by all channels
- Custom scale type added: 12 booleans (one per semitone) define which notes are playable
- pitchSource removed — each mapping always uses its own natural tracker field
- Octave + octaveRange are per-channel (in MidiChannelMapping), not global
- Windows MIDI (WinMM) was abandoned due to driver issues — do not pursue

---

## Known Design Decisions
- **1 synth assumed** — if multiple mappings are in Note mode simultaneously they'll all send on their respective MIDI channels. Keep only one in Note mode for clean monophonic behavior.
- **Velocity source is read-only** — setting velocitySource to VelocityMag does not affect that field's own CC output; the two are independent.
- **Scale degree change = retrigger** — same note number never retriggered (no per-frame spam).
- **`FishTrackerConfig.json`** — can be committed to git for shared HSV/morphology defaults, or .gitignored for per-machine tuning.
- **`ExclusionMask.png`** — user-painted noise exclusion mask (1920×1080). Saved in StreamingAssets, loaded on Start. White = allow tracking, black = exclude. Can be deleted to reset.

---

## UI Architecture

The UI is built with **Shapes by Freya Holmer** for procedural drawing and a playful, audio-gear-inspired visual style. The target audience is non-technical audio/music/guitar people, not programmers.

### Visual Style
- **Audio plugin / guitar pedal aesthetic** — knobs, sliders, channel strips inspired by parametric EQs, mixers, and synth UIs (think FabFilter, Serum, Vital)
- **Dark semi-transparent panels** over a full-background video feed — the tank is always visible
- **Clean lineart icons** with live feedback (spectrum squiggles, pulsing fish, bouncing meters)
- **Fun and playful** — this is a silly joke project, lean into it. Fish-themed accents welcome
- **Shapes-drawn controls** — arcs for knobs, rounded rects for keys, procedural everything so it stays crisp

### Layout

```
┌─────────────────────────────────────────────────────────────┐
│                                                             │
│                    VIDEO / TANK                             │
│               (full background, always visible)              │
│                                                             │
│  ┌──────────────┐                    ┌────────────────────┐ │
│  │  Tracking &  │                    │   Song Settings    │ │
│  │    Vision    │                    │                    │ │
│  │              │                    │  Scale picker      │ │
│  │  HSV bars    │                    │  Root note         │ │
│  │  Knobs       │                    │  Piano keyboard    │ │
│  │  Presets     │                    │  (clickable when   │ │
│  │  🖌 Mask btn │                    │   Custom selected) │ │
│  └──────────────┘                    └────────────────────┘ │
│                                                             │
│  ┌─────────────────────────────────────────────────────────┐ │
│  │                 Channel Strip Rack                      │ │
│  │  ┌───────┐┌───────┐┌───────┐┌───────┐┌───────┐┌─────┐ │ │
│  │  │ Pos X ││ Pos Y ││ Speed ││ Vel X ││ Vel Y ││Size │ │ │
│  │  │       ││       ││       ││       ││       ││     │ │ │
│  │  │ live  ││ live  ││ live  ││ live  ││ live  ││live │ │ │
│  │  │ meter ││ meter ││ meter ││ meter ││ meter ││metr │ │ │
│  │  └───────┘└───────┘└───────┘└───────┘└───────┘└─────┘ │ │
│  └─────────────────────────────────────────────────────────┘ │
│                                                             │
│  ┌─────────────────────────────────────────────────────────┐ │
│  │ 🐟● 60fps │ CC20=64 NoteOn C4 v100... │ 🔇MUTE │ 🎥▾ 🎹▾ │
│  └─────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
```

### Panel Groups

#### Group 1+2: Tracking & Vision (left panel)
Contains both vision tuning and tracking quality. All settings saveable/loadable as named presets.
- **HSV picker**: horizontal gradient strips with draggable range handles (like a band-pass EQ). Hue strip = rainbow gradient, Sat strip = gray→vivid, Val strip = dark→bright. Live mask preview shows what's being captured.
- **Morphology knobs**: erode1, dilate, erode2 — pedal-style knobs with Shapes arcs
- **Tracking knobs**: minBlobPixels, positionSmoothing, velocitySmoothing, deadReckonDuration, maxVelocity
- **Preset selector**: save/load named presets (captures HSV + morphology + tracking params)
- **Mask paint toggle**: button to enter/exit exclusion mask painting mode. Painting is a modal state, not always active.

#### Group 3: Song Settings (right panel)
Global musical settings shared by all channels.
- **Scale type picker**: dropdown or segmented selector (Major, Minor, Blues, Custom, etc.)
- **Root note selector**: clickable note name
- **Piano keyboard**: Shapes-drawn octave, always visible, always reflecting current scale + root note. Root note highlighted distinctly. Active scale degrees lit up. **Read-only when a preset scale is selected. Clickable toggles when Custom is selected.** This is both a control and a visualization.

#### Group 4: Channel Strip Rack (bottom, wide)
6 vertical channel strips side by side, mixer-style. Each strip shows only settings relevant to its current mode.

**Always visible per strip:**
- Channel label + live value meter (bouncing bar/dot)
- Enabled toggle
- CC / Note mode toggle
- MIDI channel selector
- Lineart waveform/spectrum squiggle animating with live output

**CC mode shows:**
- CC number
- Input/output range (parametric EQ-style slider with live value dot)
- Hold on lost detection toggle

**Note mode shows:**
- Octave + octave range
- Velocity source config (Fixed value knob, or tracker field picker + range)
- Legato toggle
- Note order (Sequential / Random / Shuffle)
- Live note name readout (e.g. "C4", "E5")

#### Group 5: System Status Bar (bottom edge)
Single horizontal bar across the full width.
- **Fish detection indicator**: fish silhouette icon — solid+green (tracking), outline+orange (dead reckoning), dim+red (lost)
- **FPS display**
- **MIDI message log**: scrolling ticker showing recent CC/Note messages, one line default, expandable
- **MUTE MIDI button**: kills all output instantly, all-notes-off
- **Video input**: source selector dropdown (webcam, capture device, video file), open file button, resolution/downsample, flip H/V
- **MIDI port**: output port dropdown, connection status

### Control Types
- **Knobs**: pedal-style, Shapes arcs showing value range, optional glow/color accent
- **Range sliders**: parametric EQ style, horizontal bar with draggable min/max handles, live value dot
- **Toggles**: switch-style, not checkboxes
- **Segmented buttons**: for mode selection (CC/Note, Sequential/Random/Shuffle)
- **Piano keys**: Shapes-drawn, white/black key layout, clickable in Custom mode
- **HSV strips**: gradient bars with range handles

### Exclusion Mask
- Standalone feature, not part of any preset
- Persists to `ExclusionMask.png` between sessions (current behavior)
- Painting is a **modal toggle** accessed from the Tracking panel — enter paint mode, paint, exit
- While in paint mode: left-click paints (exclude), right-click erases, scroll adjusts brush size

### Live Feedback Philosophy
Every control should show what's happening right now when possible:
- Channel meters bounce with actual tracker values
- Piano keys flash when their note is currently sounding
- HSV strips show the live mask result
- Fish icon reflects detection state
- MIDI log scrolls with real output
- Knob positions update if presets are loaded
