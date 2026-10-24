# PHASE3_RECEIPT — the menu in the game's look, the controls picture (2026-10-18)

Phase 3 of [BUILD_PLAN.md](BUILD_PLAN.md): M3a–d and M4c of [VRMENU_REVIEW.md](VRMENU_REVIEW.md), with decisions V1 (the
game's look, "full if practical"), V5 (controller picture) and V7 (zoom the game's own menus: yes, decided
2026-10-18 after test3). Test build: `1.3.0-test7`, which includes Phases 1 and 2. Both of those passed the headset on 2026-10-18.

- Backup before the changes: `Working/backups/pre-phase3-20261018`.
- Code:
  - [`MenuArt.cs`](../src/CasualtyVR.Core/MenuArt.cs) (new);
  - `VrSettingsMenu` (look, Controls picture, picker, pause button);
  - `VrUi` (the panel's compositing);
  - the probe's `UiArtDump.cs` (new, `--cvr-dbg-uidump`).
- Test: `tools/vr-menu.ps1` (new scenarios `controls` and `pause`).

## The game's look: what it is made of (`--cvr-dbg-uidump`)

The dump lists every Image in the crafting panel, the pause menu and the wound view, and saves their sprites.

- **Frames:** the crafting panel is the sliced sprite `uiBlock` (64 px, border 3). Its boxes, the search bar, the
  recipe list and the category tabs are `uiBlockNano` (16 px, border 1/2/1/3). The selected tab is
  `darkenedUiBlockNano`.
- **What the sprites are:** white 1-texel outlines with rounded corners on opaque **black**, at 33.3 pixels per
  unit. On a 1080p canvas a texel is 3 px.
- **The pause menu:** three 40-px round icon buttons (`pauseMenuReturn`, `pauseMenuDeath`, `pauseMenuSettings`) on
  an arc below the 200-px `pauseMenuCircle`.
- **The glow and the see-through panels** are not in the sprites: they come from how the screen is composited. The
  black adds nothing, the white lines glow (bloom) and are tinted blue. The mod's HUD already uses the same additive
  composite in VR.

So V1 is practical in full.

## What was built

| Item | How |
|---|---|
| **M3a** Frames, glow, see-through panel | The panel has the game's `uiBlock` frame in pale periwinkle, with a soft generated glow around it. Rows, the description strip and buttons are `uiBlockNano` outlines. `VrUi` draws the settings panel the way the game composites its screen: first a dark see-through backing (alpha 0.86, inside the frame), then the UI texture **added** on top. The scene shows dimly through the panel; lines and text glow. The panel stays modal: its black root covers the game's own UI in the crop |
| Sprites from the game | `MenuArt.Load` finds `uiBlock`, `uiBlockNano` and `pauseMenuSettings` by name (`Resources.FindObjectsOfTypeAll<Sprite>`). Until a level has loaded them, as in the main menu, identical generated copies stand in, and the menu is rebuilt with the game's own on the next open. The log says which: "frames from the game's own sprites" |
| **M3b** Rows and accents | Each row is an outline box, as in the crafting list. Chosen tabs and segments are filled with the accent blue; the others are outlines. Labels are periwinkle and values white. On-switches are filled green. The armed *Reset page* is yellow |
| **M3c** Tab icons | 8×8 pixel icons drawn in the game's style (heart, eye, figure, hand, controller, list, window, i), at 4 px a texel, in the crafting categories' colours |
| **M3d** Round VR button | The gear button's ring, with the gear taken out (from the game's own sprite), and "VR" inside. It continues the arc of the game's three buttons by one equal step after the gear, around the big circle's centre. It no longer sits below the gear, where it was cut off at the screen's edge |
| **M4c** Controls picture | Both controllers are drawn in the game's line style: face, stick, two buttons, menu button, tracking ring, trigger, handle, grip. The right one is mirrored. Beside them: a box per input, 10 left and 8 right, each "control · action", with a leader line to the input on the drawing. Pointing at a box lights its line and shows its text under the drawings. **Clicking opens a picker:** all 19 game actions as buttons, the current one lit and the default marked, plus *Back*. *Reset page* still resets all bindings. The laser hand, the beam and the minigame hand offsets are under *More settings* |

## V7: the game's own menus zoomed

You decided after test3: "Zoom the game's own menus (crafting, inventory, wound view) on the dashboard like the VR
settings panel."

**What is zoomed.** `VrUi.GameMenuRect` checks the open menus in this order; the first one open is zoomed:

1. the wound view;
2. the crafting panel;
3. the trade screen, together with the inventory beside it;
4. the inventory, in first person only.

**Not zoomed:**

- **The pause menu, minigames and the console:** while any of them is up, nothing is zoomed. They would be cropped
  away; minigames keep their touch panel.
- **The inventory in the diorama:** it is part of the HUD, lined up with the world so items can be dragged out of
  it, and it is already big there.

**Framing.**

- **The crafting panel:** its own rectangle (1229 × 1005 px with the margin).
- **The wound view and the inventory:** their own rectangles are the whole screen, so they are framed by their visible
  parts. Skipped: parts wider than 60 % or taller than 90 % of the screen. These are the full-screen fades, the
  inventory's 5000-px drag edges and its 1280-px clothing strip, found with the verbose `UI zoom part` lines. The
  inventory comes out at 785 × 757 px.
- **Animations:** the frame is taken at the menu's final size, because the crafting panel opens from 10 % scale and
  the inventory from 1 %. The crop does not grow with the animation, and the inventory's crop follows it as it moves
  with the character.

**Size and look.**

- **Size:** as many metres per pixel as the settings panel, so their text is the same size, up to 1.0 m wide. The
  wound view spans the whole screen; at 1.2 m its right-hand buttons fell outside one eye's view.
- **Placement and look:** like the settings panel: opened in front of you, a see-through backing with the menu added
  on top, and the laser and cursor mapped through the crop.
- **Setting:** `Dashboard.ZoomGameMenus` (*Menus & HUD › Zoom the game's menus*), on by default.

**Test:** `vr-menu.ps1 -Only zoom`.

- B opens crafting: zoomed. B again closes it: the zoom ends.
- Y opens the wound view: zoomed.
- X in first person opens the inventory: zoomed.

Captures: `zoom-crafting_left.png`, `zoom-woundview_left.png`, `zoom-inventory-fp_left.png`.

## Checks (headless, xr-sim Quest 3 profile)

### `tools/vr-menu.ps1`

| Scenario | Result |
|---|---|
| **layout:** all pages, with and without *More settings* | Every control at least **2.6°** (the bottom-left Controls box) or 2.7° elsewhere. The Controls boxes are 64 px on a 65 px pitch; at 60 px the corner box was 2.4° and failed, so they were enlarged |
| **laser** | As in Phases 1 and 2: switch, segment, slider press and drag, preset, *More settings*, tab, stick scroll |
| **controls** | Click on *Right trigger* → "choosing an action for R.trigger" → click *Jump* → `Controls.R.trigger = jump` |
| **pause** | The menu button opens the pause menu. The round VR button sits at (396, −152), beside the gear at (240, −349), on the arc |
| **fp** | As before |

**One environmental failure on the way:** in one run, the PC's real mouse moved during the laser scenario. The mod
correctly gave the pointer back to the mouse (`laser=off`), so a tab click missed. The rerun passed.

**Text:** the panel is 4 % smaller per pixel than in Phase 1 (the glow margin is inside the crop). At the top-right
corner, the 30-px tab text measures 10 dp, as the version text did before. On the Controls page, long action names
("Show item labels", "Stick click") shrink to 24 px, which is 8 dp at the outer columns. The headset pass decides
whether they are readable.

**Captures:** `Working/menu-phase3/` (local):

- every tab;
- `controls_left.png`, `picker_left.png`;
- `pause_left.png` (the round button on the arc).

### Regression (test4, with the V7 zoom)

| Suite | Result |
|---|---|
| `vr-menu.ps1` (all scenarios, including `zoom`) | All pass. The *controls* scenario failed once when the PC's mouse moved (the log shows the pointer handed back to the mouse, `laser=off`); its rerun passed |
| `fp-smoke.ps1` | 11/11. The chest-store scenario opens the inventory in first person, which is now zoomed |
| `vr-lifecycle.ps1` | 46/46 |
| `fp-comfort.ps1` | 0 failures |
| xr-tape | 19/19 (1 skipped, as before) |

In the test3 run, `fp-smoke` *chest-store* failed once and passed on two reruns; the PC's mouse was in use then too.
The dev config was restored after every run.

## Headset pass of test4 (2026-10-23) and test5

Your verdict on test4:

1. "The look is nice."
2. The pause button "looks good".
3. The Controls page's lines "all over the place" do not look great, though the highlight on hover does. Can they
   show only while pointing at a control?
4. The zoomed menus look good, but something is cut off at the bottom: the HUD buttons along the bottom of the
   health screen (you picked this from a list of possibilities).

What test5 changes:

- **Lines:** each line and its end dot are invisible until you point at the box, then light up.
- **Health screen:** framed by its own parts it ended just above the HUD buttons that are used with it: the heart
  that toggles it, and the icons at the bottom right. It now gets the whole screen (at most 1 m wide, so the same
  size as before but in front of you).

**Why it was not seen headless:** the captures did show the HUD buttons half cut, but that read as a window edge.
Your log also shows the game at 2560 × 1440. The mod moves the window to your 1920 × 1080 monitor, and the game then
switches itself back to 2560 × 1440. So `vr-menu.ps1` has a new `-Size 2560x1440` option, which runs without the
monitor setting. In that run the game itself settled back to 1920 × 1080 for the diorama, so the crop sizes differ
from yours, but the framing is the same.

**Checks (test5):** `vr-menu.ps1`, all scenarios, 0 failures. The health screen's crop is now 1920 × 1080. The changes
touch only the Controls page and the health screen's crop, so the other suites were not rerun (test4's results
stand).

## Headset pass of test5 (2026-10-23) and test6

Your verdict on test5:

- The Controls page "looks much better", but some control and action names overlap for lack of room.
- The health screen "looks great".
- The crafting screen now has "the bottom and top part almost completely cut off". From the list of possibilities you
  picked: the whole panel is there but taller than the headset shows (in the diorama).

What test6 changes:

- **Controls boxes:** two lines. The control's name is small (24 px) at the top-left; the action (30 px,
  shrinking to 24 for the longest) is at the bottom-right, across the box's full width.
- **Zoomed game menus fit the view:** they are at most 40° tall at the panel's distance, which is 0.58 m at 0.8 m.
  At the settings panel's pixel size the crafting screen was 0.72 m tall (48°). It now comes out about 0.71 m wide.
  The health screen (0.56 m tall at 1.0 m wide) and the inventory are unchanged.

**Checks (test6):** `vr-menu.ps1 -Only layout,controls,zoom`, 0 failures. The Controls boxes are still 2.6° at the
corner. The captures `controls_left.png` and `zoom-crafting_left.png` show the new layout.

## Headset pass of test6 (2026-10-23) and test7

Your verdict on test6:

- Crafting "is fine now".
- The Controls page is "good enough now".
- From a headset screenshot: the tab titles of some pages "look wonky".

**Cause:** the tab text had a 30-px floor, so the long names ("First person", "Hands & body", "Menus & HUD",
"Passthrough") could not shrink. They overflowed their 238-px text area, over the icon on the left and past the
tab's right edge.

**What test7 changes:**

- The text area runs from 56 px to 12 px short of the right edge, and its floor is 24 px.
- After building, every tab gets the size the longest name needed (`ForceMeshUpdate`, then the smallest), so all
  eight match.
- The headless layout check passes, with every control still at least 2.6°. The tab text now measures 9 dp at the
  top corners.

## Test build

`dist/CasualtyVR-1.3.0-test7.zip` (1026 KB), SHA-256 `765276CEFB6602AE411283DA5EFBE9EC0B63D0BF2F09A82318228AA14F51C69D`.
It contains Phases 1–3, V7, and the fixes from the test4, test5 and test6 headset passes.

## For the headset pass

1. **The look:** does the menu read as part of the game: the glowing frame, outline boxes, the see-through panel?
   Is it as readable as test2 over a bright scene?
2. **Pause menu:** is the round VR button in a good place, and does it look like the other three?
3. **Controls page:** can you tell which box belongs to which button? Change an action with the picker and use it.
   Is the text in the boxes big enough?
4. **Zoomed game menus (V7):** open crafting (B), the health screen (Y) and, in first person, the inventory (X or
   the chest). Are they readable and comfortable in front of you? Is anything cut off? Dragging items in the
   inventory and crafting still works?
