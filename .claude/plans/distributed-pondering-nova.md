# Auto-scale preview + downsampling option

## Context
The user brought in a 4K video feed, and the debug canvas preview (hardcoded at 1728x972) gets cropped. They want:
1. The preview to automatically scale to fit the screen
2. A downsampling option so they can use up to 4K source but process at a lower resolution to save GPU

## Plan

### 1. Auto-scale preview to fit screen — `FishDebugCanvas.cs`

Add an `AspectRatioFitter` component to the video RawImage at runtime (or require it in Inspector). In `Update()`, read the video texture dimensions and set the RawImage to fit within the screen while preserving aspect ratio.

- In `Awake()`: add/get `AspectRatioFitter` on `videoImage`, set mode to `FitInParent`
- In `Update()`: when video texture is assigned, update the aspect ratio from `texture.width / texture.height`
- Do the same for `maskImage` so it stays aligned
- Set both RawImages to stretch-anchor (0,0)→(1,1) so the fitter can work within the full canvas

This way any resolution feed (720p, 1080p, 4K) auto-fits without cropping.

### 2. Downsampling option — `VideoFileInput.cs`

Add Inspector fields:
- `downsampleFactor` (int, Range 1-4, default 1): divides native resolution by this factor
  - 1 = native, 2 = half res, 4 = quarter res

In `OnPrepareCompleted()`, apply the factor when creating the RenderTexture:
```
int outW = (int)w / downsampleFactor;
int outH = (int)h / downsampleFactor;
```

The tracker already re-inits its compute buffers when `videoTexture` dimensions change (YellowFishTracker.cs:182-183), so the downstream pipeline handles this automatically.

### Files to modify
- `Assets/Scripts/FishDebugCanvas.cs` — auto-scale logic
- `Assets/Scripts/VideoFileInput.cs` — downsample factor

### Verification
- Run with 4K video: preview should fit in window without cropping, maintaining aspect ratio
- Change `downsampleFactor` in Inspector from 1→2→4: RenderTexture resolution should halve/quarter, reducing GPU load
- Bounding boxes should still align correctly (they use normalized coords)
