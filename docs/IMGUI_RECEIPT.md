# IMGUI_RECEIPT — other mods' windows in VR (1.2.2, 2026-10-17)

## The report

The user played co-op (Casualties Together / KrokMP 5.0.2) with 1.2.1 in the headset. Three problems:

1. The co-op mod's menus did not show in VR. That includes the window you get by right-clicking another player
   (actions and further menus).
2. Other players' name tags were under them in first person.
3. The alt-tab icon was sideways in first person.

The user asked that all windows show in VR, for the co-op mod and for other mods.

- Backup before the changes: `Working/backups/pre-imgui-20261017` (the deployed 1.2.1 build and the dev config).
- Code: [`ImguiCapture.cs`](../src/CasualtyVR.Core/ImguiCapture.cs), `VrUi`, `VrInput`, `WorldText`, `VrRig`.
  Test window: `src/CasualtyVR.Probe/ImguiTestWindow.cs`.

## Causes (from the decompiled co-op mod, grade STATIC; see COMPATIBILITY.md)

- **Windows:** the co-op mod's UI is IMGUI (`OnGUI`), not canvases. VrUi only moves canvases into the UI texture.
  IMGUI is drawn straight to the window after every camera, so no eye ever saw it.
- **Name tag:** a TextMeshPro named `CharacterNametag_<name>`, which the mod places every frame 3 units below the
  body's centre. In 2D that is under the feet; in first person it is in the floor.
- **Status icons:** flat sprites on the character's root, beside its Body. First person turns characters to face
  you, but these were not part of the character group, so they stayed edge-on.

## What 1.2.2 does

### Capture

Two behaviours wrap every OnGUI repaint:

- `ImguiFirst`, at GUI.depth +2^28, repaints first. It points `RenderTexture.active` at a window-sized texture and
  clears it.
- `ImguiLast`, at GUI.depth -2^28, repaints last and restores the target.

The texture is drawn over the game's UI on every VR panel (HUD, dashboard, main-menu panel), with premultiplied
blending. On the monitor it is a quad in front of the game camera, so the monitor still shows the windows.

Measured on the way:

- The first version ran both behaviours before the test window. A behaviour with `useGUILayout` off is not sorted
  by depth, so they need it on.
- The panels skip the layer while a readback says nothing was drawn.

### Pointer and clicks

The first attempt rewrote `Event.current` (mouse position, and a Repaint turned into MouseDown) in the first
behaviour. It reached no other window: each behaviour gets its own copy of the event. The logs showed MouseDown
inside the window and no click.

What works: while the laser drives the mouse, its position and buttons are posted to the game window as
`WM_MOUSEMOVE` / `WM_LBUTTONDOWN` / `WM_RBUTTONDOWN` (and up) at the laser's pixel. Unity turns them into genuine
IMGUI events for every mod.

- Left button = whatever is bound to Mouse0 (the trigger, "attack").
- Right button = Mouse1 (the grip, "iteminteract").
- The OS cursor does not move.
- `VrInput` ignores the "real mouse moved" that this causes (`ImguiCapture.IsOwnPosition`), so the laser keeps
  the pointer.

### First person

The HUD panel is in front of you, not on the world. A quarter-size alpha copy of the capture is read back every 6
frames. When the laser ray hits the HUD panel where IMGUI drew, the laser points at the window
(`VrInput.OnModWindow`); otherwise it aims into the world as before.

### Name tags and status icons (first person)

`WorldText` collects the direct children of each nearby character's root, other than its Body. Roots with more
than 16 children are not characters and are skipped.

- **Name tags** (`CharacterNametag_*`) are drawn 1.6 blocks above the head limb, facing you, at 1.7–3° per line.
- **Sprites** with no collider turn to face you, at most 4° tall.

Like other world text, they are moved only while an eye renders: the monitor is unchanged, and no transform with a
Collider2D is touched.

### Test hooks (dev only)

- `--cvr-dbg-imgui`: the probe draws a GUI.Window with a button in the middle of the screen.
- `--cvr-dbg-imgui-dump`: writes the capture to `BepInEx/CasualtyVR/imgui-capture.png`.
- `--cvr-dbg-clone`: clones your character 6 blocks to the right, as the co-op mod makes remote players, and hangs a
  co-op-style name tag and icon on it.
- `--cvr-dbg-attach`: the same labels on a spawned body.
- `--cvr-dbg-noimgui`: turns the capture off (cost A/B).

## Verification (headless, xr-sim, dev copy, seed 1234)

| Check | Result |
|---|---|
| Capture | `IMGUI test: repaints into 'CasualtyVR.ImguiTexture'`; the dump shows the window and label upright |
| Headset image, diorama (`Working/imgui-5_left.png`) | The window is on the HUD, centred over the diorama, with the hand cursor on it; the window border shows hover |
| Laser hover / click, diorama | `pointer inside the window`; two trigger pulls → `button clicked (2)`; the laser stays the pointer (`laser=on`) |
| First person (`Working/imgui-fp_left.png`) | The window is on the panel in front; pointing at it gives `modWindow=True`; a trigger pull gives `button clicked (1)` |
| Name tag / icon, first person (`Working/clone-fp4_left.png`) | "Test player" above the clone's head, facing you, readable; the icon above it, small, facing you |
| `tools/vr-lifecycle.ps1` (now 5 launches, `imgui` scenario added) | **46/46** on the packaged 1.2.2 build. Earlier full runs failed only their "ready" check: whole runs sat at ~74 fps, below its old 80 fps bar, now 50 |
| xr-tape | **19/19** on the packaged 1.2.2 build (`Loading [CasualtyVR 1.2.2]`) |
| `tools/fp-smoke.ps1` | **11/11** (on this code before the version bump) |
| Cost A/B (heading view, 2 runs each) | Capture on: 82.4 / 82.1 fps. Off: 82.2 / 65.4 fps. The difference between runs is larger than any difference between on and off: no measurable cost |

## Headset

**2026-10-18:** the user tested 1.2.2 in co-op (Casualties Together) in the headset and reports that it works
(grade HEADSET). This covers the reported problems: the co-op windows in VR, with the laser, and the first-person
name tags and icons.

## Still open

- **IMGUI behaviours at a GUI.depth beyond ±2^28** would draw outside the capture. None were seen.
- **Typing:** chat still needs the keyboard (the laser has no text input).
