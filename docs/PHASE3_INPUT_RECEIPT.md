# PHASE3_INPUT_RECEIPT — controllers: laser as mouse, buttons as game keys

Date 2026-10-04. Environment: dev copy under xr-sim (Quest 3 / VDXR profile, Touch controller profile
driven through `tools/xrsim-cmd.ps1`) + xr-tape. Grades: LIVE (substitute runtime), headset pending.

## Design (playbook patterns)

| Pattern | Where |
|---|---|
| INPUT-001 physical transport, semantic mapping | Bridge reports raw per-hand state (`CvrInput`: aim pose, trigger, grip, stick, stick click, A/B/X/Y/menu). `VrInput` maps it to the game's `KeyBinds` actions (user rebinding carries over) |
| HAND-001 aim pose vs grip pose | Laser = aim pose |
| HUD-001 one authoritative pointer | One ray hit per frame (`VrUi.RaycastActiveSurface`: theatre → dashboard → game plane) drives the virtual mouse, the cursor and the beam end. The HUD quad moved to z=0 so world and HUD share that plane |
| XR-002 focus loss | Not FOCUSED: all inputs read as released, so the game sees ordinary key-up edges |
| INPUT-002 press-owner release | Synthetic keys are a per-frame held set; leaving VR (`Enabled=false`) or losing focus empties it |

Harmony postfixes on `UnityEngine.Input` (`mousePosition`, `GetKey/Down/Up(KeyCode)`,
`GetMouseButton/Down/Up(int)`) merge the controller state into what the game and the EventSystem read.
The real mouse keeps working: whichever moved last (real mouse vs laser aim/trigger/grip) owns the
cursor, and the laser claims it only after it has hit a surface.

Default mapping: R trigger attack/click, R grip interact/right-click, A jump, B craft, R stick
up/down throw/switch hands, R stick click expand description, L stick walk/climb/crouch, X inventory,
Y wound view, L trigger switch hands, L grip alt view, L stick click favourite, menu = pause.

## Results (xr-sim)

| Check | Result |
|---|---|
| Profile | `/interaction_profiles/oculus/touch_controller` bound (simple controller suggested as a fallback) |
| L stick right 3 s | body x 0 → 4.09 |
| A held 1.5 s | `held=[Space]` |
| L stick left 3 s | body x → −23.08, y 485.6 → 470.6 (walked out of the lifepod and dropped a level) |
| Laser on gameplay | virtual mouse follows the pointing direction (1153,105 → 933,324) |
| Laser + trigger on main menu | content warning dismissed, the intro began (`click-before/after-*`). Beam and cursor visible |
| Submission contract | xr-tape 19/19 throughout |

## Findings

1. **Deadlock:** the first build called the input update inside the frame mutex, and the input update
   takes that mutex itself. The game froze on its first VR frame (xr-sim state: 1 wait, 0 begins).
   Fixed by calling it outside the lock.
2. **xr-sim keeps one binding per action and ignores subaction paths.** Shared left/right actions read
   the wrong hand there (stick reads were zero). The bridge now uses one action per hand and control,
   which is valid on every runtime, and suggests the fallback profile before Touch (xr-sim takes the last
   suggestion).
3. Pointer ownership must ignore sticks/buttons (walking must not steal the mouse), and must not engage
   before the first hit (else the game aims at pixel 0,0).

## Not yet covered

Headset feel (laser angle and length, thresholds), haptics, left-handed mode (`Controllers.LaserHand`),
scroll-wheel equivalents, and drag-and-drop with the trigger held (should work through the same mouse
path, unverified).

## Headset feedback (user, 2026-10-04) — HEADSET pass

Aiming, clicking and buttons "work very well". Changes requested and made:
* **Laser beam removed** (got in the way); the cursor marks the hit. `Controllers.ShowLaserBeam` (default false).
* **Esc/pause on right stick click** (the left menu button is often reserved by the runtime). Expand
  description is now keyboard-only (Shift). Verified in xr-sim: R stick click opens the pause menu on the
  dashboard, no beam drawn (`pause-*`); xr-tape 19/19.
