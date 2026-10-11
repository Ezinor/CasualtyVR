# PHASE3_UI_RECEIPT — game UI in the headset (dashboard + HUD on the diorama)

Date 2026-10-03. Environment: dev copy under xr-sim (Quest 3 / VDXR profile) + xr-tape. Grades: LIVE
(substitute runtime), not yet HEADSET. Design agreed with the user: menus and minigames on a dashboard
panel below the diorama; the small HUD pinned to the diorama's corners.

## Method (`src/CasualtyVR.Core/VrUi.cs`)

* Every root `ScreenSpaceOverlay` canvas (found every 0.5 s and on scene load) is switched to
  `ScreenSpaceCamera` on a UI-only camera. The camera is **placed so world units equal screen pixels**:
  position (W/2, H/2, −100), orthographic size H/2, canvases on z=0, layer 30. A canvas therefore has
  exactly the position and scale it had as an overlay. Game code that writes pixel positions into UI
  transforms (drag icon, radial menu, tooltips) and `PlayerCamera.uiScale` work unchanged. The UI
  render texture has the window's size, so EventSystem raycasts map 1:1.
* **HUD mode:** the UI texture is mapped onto the diorama's window opening (= the flat camera's view).
  The game's HUD therefore sits on the diorama's corners and edges, and screen-aligned UI lines up with
  the world.
* **Dashboard mode:** pause, minigame, console, wound view, crafting, trade, end screen or main menu
  (no `PlayerCamera`), or F11. An opaque panel 0.75 m forward and 0.55 m below the seated head, tilted
  to face it, width 1.0 m. Below the diorama's lower edge (≈ −32°).
* **Desktop:** an additive quad in front of the game camera on layer 29 (game camera only), so the
  monitor keeps its UI.
* VR stop reverts every converted canvas (render mode, camera, layer) and destroys the UI objects (XR-004).

## Results

| Check | Result | Grade |
|---|---|---|
| Input still maps after conversion | EventSystem raycast at an active button's screen position hits that button through the UI camera: menu 1/1, gameplay 1/1 | LIVE |
| Dashboard shows menus | Content-warning screen readable on the dashboard (xr-sim capture `menu-*`) | LIVE |
| HUD on diorama corners | Held item top-left, health/first-aid bottom-left, depth gauge right edge, speed bottom-right, message box centred (`hud4-*`) | LIVE |
| Submission contract unchanged | xr-tape 19/19 | LIVE |
| Monitor | UI still visible (via the game camera, so it gets bloom and the 640×360 pixel-perfect pass; slightly soft) | LIVE |

## Findings

1. **URP's 2D renderer writes alpha = 1 into the UI camera's texture** whatever the clear colour, even
   with HDR and MSAA off. Read back in gameplay: empty areas (0,0,0,1) while every full-screen UI image
   has alpha 0. The HUD is therefore composited **additively** (URP `Particles/Unlit`, One/One): black adds
   nothing. Dark UI backgrounds do not darken the world behind them. Revisit if a tooltip or panel proves
   unreadable over bright scenes (options: a second Universal renderer for the UI camera, or a
   black/white dual render to recover alpha).
2. **UGUI cannot draw the URP particle shader** (the `RawImage` mirror showed UV-gradient garbage), hence
   the desktop quad instead of a mirror canvas.
3. **A probe taken too early is misleading:** the first alpha readback ran during world generation, when
   the game's loading screen is legitimately opaque. Diagnostics now wait for the generated world, the
   player body and 3 s.
4. **`-monitor N` does not override the game's saved window placement** (both 1 and 2 opened on the same
   screen). `WindowPlacement` moves the window with `Screen.MoveMainWindowTo` to the display matching
   `Window.PreferredDisplay` (default "1920x1080"), at startup and after every scene load. Log:
   "moving from 'XV271U M3' 2560x1440 to '24G15N' 1920x1080".

## Not yet covered

* Headset acceptance of both modes; dashboard comfort/size.
* Interacting with the dashboard **in VR** (needs the controller laser; Phase 3 next step). Until then
  the mouse on the desktop window drives the UI, and the dashboard mirrors it.
* Minigame-specific layouts; the radial inventory menu while dragging (HUD mode).

## Headset feedback round 2 (user, 2026-10-03) and fixes — LIVE (xr-sim), awaiting HEADSET

| Report | Cause (evidence) | Fix |
|---|---|---|
| Game opens on the 1080p monitor | `WindowPlacement` log | HEADSET pass |
| HUD readable/comfortable; dashboard position liked | user | HEADSET pass |
| Start screen "too low", plus a second "space/stars" window above it | The main menu went to the low dashboard while the menu scene's starfield rendered as the diorama | **Theatre mode:** with no `PlayerCamera`, the whole UI is shown opaque at the diorama's position, 1.3 m wide (`MainMenuWidthMetres`). The first try at diorama width (≈2.7 m) overflowed both eyes |
| Faint visuals stuck to vision while turning the head | Screen-space post effects on the eye cameras (game volume: FilmGrain; Bloom can carry lens dirt) | `VrPostOverride`: global volume on layer 27, in the eye cameras' volume mask only: film grain, lens dirt, vignette, chromatic aberration, lens distortion all off. URP dithering on the eyes hides the 8-bit banding that grain used to mask |
| Water looks weird | Not screen-space: an orthographic pan test moved water/fog/rain/liquid by 42–43 px for an expected 42.7 (`--cvr-spike-liquids`, `tools/liquid_shift.py`). Real causes: (1) water particles sort on `Top/-500`, which the pop-up table put in the fog band, 2 units in front of the terrain; (2) View-aligned billboard particles turn with the head | (1) Top-layer orders ≤ −100 → actor plane. (2) `ParticleAlignment`: billboards set to World alignment while VR runs (pixel-identical for the never-rotating ortho camera; restored on stop) |
| Tooltips/speech too glowy | HUD drawn before the eyes' bloom | HUD/dashboard/theatre quads (layer 31, rendered by no camera) are drawn into each finished eye image by a command buffer at `endCameraRendering`. A first version with URP overlay cameras worked but cost a full 2D-renderer pass per eye, and the frame hid the HUD's corners. `GetGPUProjectionMatrix(..., false)`: `true` drew the HUD upside down |

Also fixed while measuring: the actor-plane depth sign was inverted (front limbs sat behind back limbs
in stereo). The pop-up now skips renderers with no offset and no moved ancestor (1,036 moved instead of
1,632, 0.32 ms) and refreshes its list every 120 frames.

**Performance:** seven seeded runs, steady **90 fps** in gameplay (xr-sim, 1824×1968 per eye). **Caveat:**
the scene is not deterministic beyond the block grid. Some runs (same seed) spawn ~470 extra particle
renderers (6,500 total). Two such runs were seen before these optimisations (73–80 fps) and none since.
Heavy scenes remain to be checked in the headset.

xr-tape after all changes: 19/19.

## Round 3 (user, 2026-10-03): start screen, stuck visuals, water, text, smoothness — all HEADSET pass

**Death/end-of-run screen not usable in VR** (user had to leave VR to get back to the menu).
Reproduction: the console `kill` command only reaches the near-death hallucination within 45 s (the end
screen needs consciousness < 20 *and* the body dead), so the probe gained `--cvr-command "!endsequence"`,
which runs the game's own `PlayerCamera.EndSequence(0)`. Causes:
1. The end screen went to the **low dashboard**. The game slides its stats receipt (the thing you click)
   up from the bottom edge, which landed at the bottom of the headset's view.
2. **No cursor in VR:** the OS mouse cursor never reaches the eye images, so there was nothing to aim with.

Fixes: the end screen is treated like the main menu (**theatre mode**, at eye level, 1.3 m wide), and a
**VR cursor** (black-outlined white arrow, `CursorSizeMetres` 0.025) is drawn at the mouse position on
whichever panel is active (theatre, dashboard or HUD/diorama). Verified in xr-sim: the end-screen image and
the "MSN-CARGO STATUS REPORT" receipt are at eye level, with the cursor visible (`endscreen-*`).
Headset acceptance pending.

## Round 4 (user, 2026-10-03): end screen still unusable after quitting; loading screen see-through

* **Quit = self-destruct = a death.** `PauseHandler.TryQuit` (4th press) calls `StartSelfDestruct`, which
  ends in the same `EndSequence(0)`. The user's log showed theatre mode *was* active, so the panel was
  up but its content was unreadable.
* **Cause (xr-sim, `--cvr-command "!selfdestruct"`):** the game's Renderer2D has four
  `FullScreenPassRendererFeature`s (BonusScreenPass, DamagePass, EffectsPass/MainPassScreen, AberPass).
  Features run on *every* camera using the renderer, including the UI camera, so while dying the whole UI
  texture was painted with the pink "dying" wash. On a flat screen the overlay UI is drawn after them.
* **Fix:** `ScreenPassFilter` disables chosen features per camera, only during that camera's render.
  UI camera: all of them. Eyes: AberPass always (screen-locked colour fringing), and all of them while a
  theatre panel is up. Verified at +75 s after self-destruct: end image, "MSN-CARGO STATUS REPORT"
  receipt and VR cursor at eye level on clean black (`sdclean-*`); xr-tape 19/19 through the sequence.
* **Loading screen:** while the world generates, `PlayerCamera` exists, so the opaque loading UI went to
  the *additive* HUD and the level showed through. World generation now counts as a theatre state. In
  theatre (menu, loading, end screen) the eyes also stop drawing the level, so only the panel and the
  frame show (`loading-*`).
* Not verified headless: clicking the receipt and the path back to the title screen (needs a click). The
  user's mouse plus the VR cursor covers it; the controller laser will too.
