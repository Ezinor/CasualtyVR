# PHASE1_SPIKE_RECEIPT — perspective and off-axis stereo cameras (flat, no headset)

Date 2026-10-03. Run `Working\game-dev\BepInEx\CasualtyVR\run-20261003-201840\`, seed 1234, lifepod start.
Command: `tools\launch-dev.ps1 -Windowed --cvr-autostart --cvr-seed 1234 --cvr-spike --cvr-eye-sep 1.5 --cvr-quit`.
Code: `src\CasualtyVR.Probe\StereoSpike.cs`. Environment: in-process extra cameras rendering to 1920×1080 RTs
on the desktop. **No VR runtime, no headset.** Grades below are LIVE (flat harness), not HEADSET.

## Question

Does the game's URP 2D Renderer (2D lights, post-processing, sorting) render correctly through a camera
that is (a) not the game's camera, (b) perspective, and (c) off-axis per eye? (Risk R7 in VR_PLAN.)

## Setup

The game camera is left untouched (it remains the logic camera). Extra cameras are created with
`CopyFrom(main)`, without `PixelPerfectCamera`:

| Image | Camera |
|---|---|
| `spike-ortho.png` | Orthographic, size 22.5, at the game camera position (reference) |
| `spike-persp.png` | Perspective, vertical FOV 40°, 61.82 units in front of z=0, framing the same 45-unit slice |
| `spike-eyeL/R.png` | Perspective, ±0.75 units in x, off-axis `Matrix4x4.Frustum` with the zero-parallax plane at z=0 |

## Results

| Check | Result | Grade |
|---|---|---|
| 2D point lights, global light, fog, bloom/colour grading on an extra camera | Present and visually identical to the game view (`spike-ortho` vs the game's screenshot) | LIVE |
| Same under perspective | Identical framing and lighting (`spike-persp`). `WorldToViewportPoint` of focus ±10 units equals ortho (0.63 / 0.72) | LIVE |
| Same under off-axis per-eye projection | Lighting, fog and post intact in both eyes. `m02 = ±0.0188 = ±0.75/40` as derived | LIVE |
| Real parallax in the pair | **Almost none.** Block matching: 23 of 35 textured blocks have 0 px disparity, and the rest −6…+2 px (objects off z=0). Mean abs L−R = 0.80/255 | LIVE |
| `PixelPerfectCamera` removed | The world renders at full RT resolution (crisper than the game's 640×360 upscale). Point filtering keeps the pixel-art look | LIVE |

**R7 is cleared for the centred and off-axis perspective cases at these values.** Not yet covered:
head *rotation* (canted eyes), large off-centre offsets, headset FOVs and resolutions. Those are covered
by the xr-sim / headset steps.

## Findings that change the plan

1. **Reset cloned cameras' matrices.** The first attempt rendered a ~2.6× zoomed image although the
   transform was at z=−61.8: the view matrix stayed at the game camera's z=−3 (`worldToCameraMatrix.m23
   = −3.00`). Calling `ResetWorldToCameraMatrix()` and `ResetProjectionMatrix()` after `CopyFrom` fixed
   it. **Rule:** every VR camera created from the game camera resets both matrices before its pose is
   applied, and sets `projectionMatrix` explicitly every frame from the OpenXR FOV.
2. **Plain stereo shows a flat card.** The game draws almost everything on z≈0, so the zero-parallax
   pair is a picture hanging in space. This is LIVE evidence that the **Phase 2 pop-up-book z
   offsets** (and later the voxel geometry) are what create depth. Stereo transport alone is not enough.
3. **The VR route can use ordinary Unity cameras rendering to RenderTextures.** Nothing here needed
   Unity's XR subsystem, so the native OpenXR bridge route (1b) is viable regardless of the UUVR result.

## Next discriminators

* Pop-up test: apply z offsets per sorting layer/category on the spike cameras only (or on a render-only
  copy), and re-measure disparity. Check that 2D sort order still holds under a perspective camera (the
  `TransparencySortMode` for perspective cameras sorts by distance).
* Native OpenXR bridge under **xr-sim**: submit `spike-eyeL/R` style RTs every frame, then run
  `xr-tape --Check` (`eye_subimages_distinct`, `submitted_fov_matches_located`, `eye_order`).
* UUVR result from the user (Unity XR route), recorded separately.
