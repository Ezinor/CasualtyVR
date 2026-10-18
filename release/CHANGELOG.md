# Changelog

## 1.3.0-test1 — 2026-10-18 (test build)

**A new VR settings menu** (F12, or the pause menu's *VR* button)

- **Its own big panel:** the menu now opens on a panel of its own, in front of where you are looking, about a metre
  wide and 0.8 m away. Everything on it is bigger: every button is at least 2.5° across, the text about twice its
  old size. *Recenter* moves the panel in front of you again.
- **Pages by task:** Comfort, View, First person, Hands & body, Controls, Menus & HUD, Passthrough and About (the
  keys, the versions and where the logs are). The *Blocks* page is now part of *View*.
- **Controls that suit a pointer:** on/off settings are switches (click anywhere on the row); short lists are
  buttons side by side; amounts are **sliders** you drag with the trigger held, with fine < > at the ends and the
  value in real units (m, cm, %, °/s).
- **Point at a setting to see what it does:** its description shows under the list.
- **More settings:** each page shows the common settings; *More settings* at the bottom shows the fine tuning
  (thicknesses, depths, panel placement, minigame hand offsets, key-colour channels).
- **Reset one setting:** a row that differs from its default has a reset button (↺); *Reset page* still resets
  the whole page (press twice).
- **Feedback:** the controller ticks lightly when you point at a control and more firmly when you click; clicks
  make the game's own click sound.
- **Long pages scroll** with either stick (the sticks do nothing else while the menu is open, so you do not walk
  off in first person), the arrows on the right, or the mouse wheel.

## 1.2.2 — 2026-10-17 (test build)

**Other mods' windows in VR** (user report with the Casualties Together co-op mod)

- **Windows that other mods draw are now shown in the headset**, on top of the game's own menus and HUD: the co-op
  mod's menus, its player-interaction window (right-click / grip on another player), its chat and player list, and
  any other mod that draws with Unity's OnGUI. The monitor shows them as before.
- **The laser works on them:** pointing hovers, the trigger clicks (left button) and the grip right-clicks. Your real
  mouse cursor is not moved.
- **First person:** these windows appear on the see-through panel in front of you. Point at a window to use it;
  point anywhere else and you aim into the world as before.
- **Other players' name tags** (co-op mod) show above their heads in first person, facing you at a readable size
  (they were under their feet), and their **status icons** (such as the alt-tab icon) face you instead of being seen
  edge-on.

## 1.2.1 — 2026-10-17 (test build)

**Fixes**

- **Controls no longer stick** when the headset stops showing the game: taking the headset off, opening the
  headset's own menu, or losing head tracking left whatever you were holding (walking, attacking) held. A button
  still held when you come back from the headset's menu now does nothing until you let go of it.
- **Short tracking dropouts** no longer black out the view: when the headset loses its position for a moment, the
  view keeps turning with your head. A controller that drops out keeps its last position for half a second instead
  of vanishing, and that moment never counts as a throw or swing.
- **Recenter faces you:** F9 (or *Recenter*) now also turns the game to face the way you look, not only moves it.
  Recentring from the headset's own menu (e.g. holding the Quest button) is followed straight away.
- **When the VR app closes or the headset disconnects**, VR stops cleanly with a message on the game window (press
  F10 to start again), instead of failing silently every frame. The game also gets its own vsync back whenever the
  headset is not driving the frame rate.
- The game's own frame cap (its console `fps` command) no longer fights the headset's timing while VR runs.

**Robustness and diagnostics**

- If one VR feature hits an error (for example after a game update), only that feature turns off, with a message;
  the view and the controls keep working. Before, one error stopped the whole VR view.
- A note in the log and on the game window when the game version is not the one this release was tested with
  (Demo 7.0.1).
- The native part writes its own log, `BepInEx\CasualtyVR-bridge.log`, saved line by line so it survives a crash
  (the previous game session's is kept as `CasualtyVR-bridge.prev.log`).

## 1.2.0 — 2026-10-07

**First person**

- **Performance:**
  - Default scene: from 40 to 90 fps. The view distance is now limited (setting *View distance*, 60 blocks), and the
    light, wall and object handling is range-limited.
  - Crowded layers: 84 fps, against 86 in the diorama.
- **Smooth fog:** fog grows smoothly with distance on foggy layers and with void sides, and now also covers creatures
  and items. It uses a custom shader built for the game's own render pipeline.
- **Rain:** rain falls around you on rainy layers. The game's flat rain overlay could not be seen from inside the
  world. Setting `Diorama.Rain`.
- **Running speed:** 80% of the game's speed by default (setting *Movement speed*). The flat game and the diorama
  always use the game's own speed, including straight after you switch out of first person.
- **Your body no longer blocks the view** when you set off running. The game leans the character forward as it
  speeds up and towards where it looks; your drawn body now stays upright under your eyes, and the view no longer
  drops when you look down. The view still follows the character down when it crouches or ducks under a low
  ceiling.
- **Both legs show**, side by side (they were drawn one inside the other).
- **Speech is readable:** what your character says is a caption in front of you (it was inside the side wall), and
  other characters' speech, damage numbers and tutorial text turn to face you.
- **No highlights by default:** the game's striped marker on the block you aim at and its outline on items and
  objects are off in first person. Setting *Highlight under your hand* (Off / Blocks only / Items and blocks).

**Minigames**

- **Lockpicking lines up:** in VR the tip of the pick (or your finger) is exactly where it acts. The pick has to start
  on a thin ring, and the click landed below the drawn tip, so aiming with the tip mostly missed.
- The default minigame hand offset is now 0 / 0, the game's own placement (saved settings are kept).

**VR settings**

- **Reset page** on every page (press twice). The first-person and passthrough on/off switches are left as they are.
- **Fewer, clearer rows:** a switch and its amount share one row that shows *Off* or the amount (controller hands,
  object depth, character thickness, liquid depth, your arms and body, vibration, which gains a strength setting).
  One cursor size row; turning and its speed in one row; the void's darkness in the tunnel sides row.
  *Thickness layers* is now only in the config file.
- **Recenter** is a button at the top of every page. The mod version is shown at the bottom.
- The *Controls* page names actions as the game's own key-bind settings do, in the game's language.
- The game's tooltips and hover labels no longer pop up over the settings.

**Compatibility**

- **More controllers:** button layouts for Valve Index, HTC Vive and Windows Mixed Reality controllers, and HP Reverb
  G2 controllers where the runtime supports them (until now only Meta Touch and a minimal fallback). See the README.
- **Canted displays** (Index, Pimax): the view direction is the middle of both eyes.
- **No Visual C++ runtime needed:** the native part no longer depends on the Visual C++ redistributable.
- **More runtimes:** a graphics-adapter mismatch reported by the runtime is no longer fatal by itself (translation
  layers such as Proton can report it wrongly), and runtimes that only offer BGRA images are supported.
- **Linux (Proton):** README section (BepInEx needs `WINEDLLOVERRIDES="winhttp=n,b" %command%`). Untested.
- **When VR cannot start**, a message on the game window says why: no OpenXR runtime, no headset, a laptop running the
  game on the wrong GPU, or the game not using Direct3D 11. VR also refuses to start safely under `-force-vulkan` or
  `-force-d3d12`.
- The game window is no longer moved to another monitor by default (`Window.PreferredDisplay` is empty unless set).

**Also**

- Every layer modifier checked in VR (Foggy, Warm, Hot, Cold, Freezing, Flooded, Dry, Rainy, Infested, Ionized,
  Unchipped).

## 1.1.0 — 2026-10-05

**First-person mode** (experimental, off by default): **F7**, or VR settings > *1st person*.

- **You play from the character's eyes,** inside the 3D blocks, looking along the tunnels. Your head is your own:
  the view follows the character's head position, and the character looks where you look.
- **The tunnel's sides** are the game's own cave walls (or a black void, or the passthrough greenscreen).
- **Lights** are adapted so that the game's 2D lights still light the world from inside it.
- **Facing you:** items, creatures, characters, plants, crates and doors turn to face you.
- **Fog and darkness** grow with distance on foggy layers and with void sides.
- **Your arms** are the character's own arms (with their wounds, clothing and held item) and follow your
  controllers. **Your body** shows when you look down, with its damage, bandages and splints.
- **Hands:**
  - Grip near something to pick it up, use a machine or crate, or take hold of a rope or ladder.
  - Let go to drop; let go while moving to throw.
  - Bring a held item to your chest to store it, or to your mouth to eat or drink.
  - Swing your hand to attack (the trigger still works).
  - Touch minigames on a panel within reach.
  - Grip at your chest for the inventory.
- **Controller vibration** for grabs, uses, climbing and swing attacks.
- **Moving and comfort:**
  - Left stick walks the way you face; right stick climbs or crouches, and a flick turns you around (or smooth
    turning).
  - Blackout when the character is unconscious.
  - Optional fall vignette, and an optional switch to the diorama while knocked down.
- **Settings:** the VR settings have two new tabs, *1st person* and *Hands*.

## 1.0.3 — 2026-10-04

- Minigame hand offset defaults to X 50 / Y -50 (tuned in the headset), so clicks line up with the hand out of
  the box.

## 1.0.2 — 2026-10-04

- Minigames: 1.0.1's fingertip click point is reverted (it made aiming worse). Instead, the minigame hand
  image can be shifted relative to where clicks land, live, in VR settings > Controls (*Minigame hand X/Y*,
  default 0 = the game's own placement; 1.0.3 defaults to 50 / -50).

## 1.0.1 — 2026-10-04

- Minigames: in VR the game's hand now acts at its index fingertip (or the tool's tip) instead of the palm, so
  clicks land under the finger. Option `Controllers.MinigameFingertipClick` (on).

## 1.0.0 — 2026-10-04

First release.

- **Stereo diorama.** The game is shown as a 3D diorama through a native OpenXR bridge (D3D11). It uses a seated
  anchor with recenter and zoom, a soft-edged window that shows what the flat game shows, and pop-up-book depth
  layers. The game window is kept on the chosen monitor.
- **3D terrain.** Blocks are extruded from the game's own tile art, with side faces that continue the tile.
  Glass, leaves and scaffolding are see-through. Blocks rebuild when you dig or build.
- **Depth for everything else.** Characters and items get thickness. World objects (drop capsules, crates, doors,
  machines) are extruded from their sprites. Liquids fill their tunnels and get a surface. Decorations stay
  attached to their objects.
- **UI.** Menus and minigames appear on a dashboard panel. The main menu, loading and end screens appear on a
  large panel. The HUD is pinned to the diorama's corners.
- **Controllers.** The laser works as the mouse, with an arrow or hand-shaped cursor. Buttons map to the game's
  key binds and can be remapped. Optional controller hands use the game's hand art.
- **In-game VR settings menu.** View, blocks, controls and passthrough greenscreen.
- **Fog.** Foggy layers get a steady VR mist, because the game's fog shader renders opaque in VR.
- VR is off at launch: press F10, or set `VR.StartVrOnLaunch`. Released under the MIT licence.
