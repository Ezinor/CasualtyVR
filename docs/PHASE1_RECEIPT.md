# PHASE1_RECEIPT — VR menu framework (2026-10-18)

Phase 1 of [BUILD_PLAN.md](BUILD_PLAN.md): items M1a–c, M2a–e, M4a, M4b and M5b of [VRMENU_REVIEW.md](VRMENU_REVIEW.md),
with decisions V2 (own enlarged panel), V3 (sliders), V4 (*More settings*) and V6 (keep the number rows). The game's
look (M3), the controller picture (M4c) and touch (M2f) are later phases. Test build: `1.3.0-test1`.

- Backup before the changes: `Working/backups/pre-phase1-20261018` (the deployed 1.2.2 build and the dev config).
- Code:
  - [`VrSettingsMenu.cs`](../src/CasualtyVR.Core/VrSettingsMenu.cs): rewritten;
  - `VrUi` (the cropped panel);
  - `VrInput` (sticks scroll the menu);
  - `VrSpace` (head pose in tracking space, the inverse mapping);
  - `Plugin` (three placement settings, the version).
- Test: [`tools/vr-menu.ps1`](../tools/vr-menu.ps1).

## What was built

| Item | How |
|---|---|
| **M1a** Own panel | While the menu is open, `VrUi` crops the dashboard to the menu's rectangle of the UI texture: the quad's UVs are the crop. The panel is **0.95 m wide at 0.8 m**, centred 12 cm below the eyes. The laser, the touch point and the cursor all map through the crop. The menu is modal, so it also replaces the theatre panel in the main menu. New settings: `Dashboard.SettingsDistanceMetres`, `SettingsHeightMetres` and `SettingsWidthMetres` (*Menus & HUD › More settings*) |
| **M5b** Opens in front | The pose is taken once when the menu opens: the head's position and yaw in tracking space, so it opens in front of wherever you look. *Recenter* re-places it |
| **M1b** Bigger rows | Seven rows of 72 px on an 80 px pitch. The whole row is the hit area: a switch row toggles anywhere on it. Pages that need more rows scroll: either stick (while the menu is open the sticks reach nothing else, so you do not walk off in first person), the ▲▼ buttons with a thumb, or the mouse wheel |
| **M1c** Description | The pointed-at row's own config description, in a strip under the list. A few rows have shorter, menu-specific text |
| **M2a** Switches | A pill switch: green *On* with the knob on the right, grey *Off* |
| **M2b** Choices | Segmented buttons the full height of the row; the chosen one in the accent colour |
| **M2c** Sliders | Press on the track and drag (`MenuSlider`: no drag threshold, the value follows the pointer). Fine `<` and `>` buttons at the ends, and the value in real units: m, cm, %, °/s, blocks, × |
| **M2d** Feedback | Hover: the control lifts (scale 1.04) and the laser hand gets a light tick (0.1 for 10 ms; at most one tick per 50 ms). A click gives a firmer tick (0.35 for 20 ms) and the game's own `PlayUISound(Click)`. Slider steps tick like detents, and releasing a slider plays `MiniClick` |
| **M2e** Row reset | A pixel-art ↺ button. It shows only while one of the row's settings differs from its default. *Reset page* (press twice) is kept |
| **M4a** Tabs by task | Comfort · View · First person · Hands & body · Controls · Menus & HUD · Passthrough · About. *Blocks* went into *View*. *About* has the versions, the keys and the log paths |
| **M4b** *More settings* | Tuning rows sit behind a *More settings (n)* button at the bottom. They appear under a *More settings* heading, and the button then reads *Fewer settings* |

**Departures from the review, and why:**

- **Tabs are in two rows of four.** Eight across left *Passthrough* at 9 dp (first capture). In two rows every tab
  label is full size. That costs one list row (seven, not eight).
- **The version** stays at the bottom right, though it is also under *About*.
- **The Controls page** keeps one stepper row per control (18 rows, scrolling) until the controller picture
  (Phase 3, M4c).

## Checks (headless, xr-sim Quest 3 profile)

### `tools/vr-menu.ps1`: 0 failures

| Scenario | Result |
|---|---|
| **layout:** every tab, then every tab with *More settings* | 16/16 pages: smallest control **2.7°** (the *Done* button in the top corner), all ≥ 2.5° |
| **laser:** the right hand's laser, aimed with xr-sim `hand r point` angles computed by the mod (`--cvr-dbg-menu-aim`) | Click on a switch row → toggled. Segment → chosen. Slider press at 20 % → 0.2. Drag to 80 % → 0.8. *More settings* → tuning rows shown. Tab → page changed. Stick down → scrolled. No exceptions |
| **fp:** the same in first person, in the test tunnel | The panel opens in front of the head. A click works. With the left stick pushed while the menu is open, the game sees no keys (`held=[]`) |

**How the angular check works:** each control's rectangle is placed on the panel at its default pose, seen from the
eye with the panel facing the eye. The check takes the smaller side's angle and accounts for the obliquity off the
panel's centre. Every hit area is at least 72 px (53 mm): **3.8°** in the middle, 2.7° in the far corners.

**Text:** labels are 36 px, values and the description 32–34 px. At the panel's centre that is about 16 dp (Meta:
14 dp minimum, 18 dp comfortable). The smallest text the check found is **11 dp**: the version at the bottom-right
corner, and slider values that auto-shrink. Everything you read for meaning is at least 14 dp. The headset pass
decides whether that is enough.

**Captures:** `Working/menu-phase1/` (local): `<Tab>_left.png` and `<Tab>-more_left.png` for every tab, `laser_left.png`
(View, scrolled, with the hand cursor on the tab bar) and `fp_left.png` (first person, the description showing).

### Regression (Phase 1 code)

| Suite | Result |
|---|---|
| `tools/vr-lifecycle.ps1` | 46/46 |
| `tools/fp-smoke.ps1` | 11/11 |
| xr-tape | 19/19 (1 skipped, as before) |

The dev config was restored after every run (SHA-256 `d1d36925…`).

## Test build

`dist/CasualtyVR-1.3.0-test1.zip` (1014 KB), SHA-256 `91B3A984AD95F958F96027C9DD94C2B877ADDB13F517F5AAE3CED054E824AB2F`.
`Plugin.Version` is `1.3.0-test1`. BepInEx gets `NumericVersion` (`1.3.0`), because its plugin attribute needs a
plain version number. Changelog and README: [release/CHANGELOG.md](../release/CHANGELOG.md),
[release/README.md](../release/README.md) (*VR settings*).

## Headset pass

**2026-10-18: passed.** You tested 1.3.0-test2 (Phases 1 and 2) in the headset: "Both Phases look good." V7 is still
open.

## For the headset pass

1. **Readability:** open F12 standing and seated. Can you read the rows and the description without leaning in? Is
   the panel too big or too close? Its distance, height and width are under *Menus & HUD › More settings*.
2. **Hitting:** click switches, segments, ↺ and the tabs in the corners. Drag a slider; use its `<` `>`.
3. **Feedback:** the tick on hover. Too much, or too little?
4. **Scrolling:** *Controls* with the stick and the arrows.
5. **First person:** open the menu, push the sticks (you should not move), close it.
6. **V7:** having seen this panel, should the game's own menus (crafting, inventory) also be zoomed on the dashboard?
