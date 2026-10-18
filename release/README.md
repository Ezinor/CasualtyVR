# CasualtyVR 1.2.0

A VR mod for **Casualties: Unknown** (Demo, game version 7.0.1). It shows the game as a 3D diorama in front of
you: the terrain becomes solid blocks, liquids fill their tunnels, and characters and objects get real depth.
You play with the controllers, and menus appear on a panel below the diorama. The flat game on your monitor is
unchanged. There is also an experimental **first-person mode** (see below).

## Requirements

- Casualties: Unknown Demo 7.0.1 (Steam). Windows, or Linux through Proton (see *Linux* below).
- [BepInEx 5.4.23.x](https://github.com/BepInEx/BepInEx/releases), **x64**, **Unity Mono** build (`BepInEx_win_x64_5.4.23.x.zip`)
- An **OpenXR** runtime with a headset connected: Virtual Desktop (VDXR), SteamVR, Meta Quest Link, or another
  OpenXR runtime with Direct3D 11 support (Windows Mixed Reality, Pimax, Varjo...). It was developed and tested on
  a Meta Quest 3 with Virtual Desktop; other headsets and runtimes should work but are untested.
- A Direct3D 11 GPU. The mod targets 90 fps and was developed on an RTX 3070.

## Install

1. Install BepInEx 5 (x64) into the game folder, which contains `CasualtiesUnknown.exe`. Run the game once so
   that BepInEx creates its folders, then quit.
2. Extract this archive into the game folder, so you get
   `BepInEx\plugins\CasualtyVR\CasualtyVR.Core.dll` and `BepInEx\plugins\CasualtyVR\native\`.
3. Start your OpenXR runtime (Virtual Desktop, SteamVR...), start the game, and press **F10** to switch to VR.
   VR is off at launch by default. To start in VR every time, set `StartVrOnLaunch = true` under `[VR]` in
   `BepInEx\config\casualtyvr.core.cfg`.

To uninstall, delete `BepInEx\plugins\CasualtyVR` (and `BepInEx\config\casualtyvr.core.cfg` if you want to
reset the settings). Each page of the in-game VR settings also has a **Reset page** button (press it twice).

## Controls

Meta Quest Touch (and Pico, through SteamVR or Virtual Desktop):

| Control | Action |
|---|---|
| Right controller pointing | Mouse cursor (aim, click, menus) |
| Right trigger | Attack / click |
| Right grip | Interact / right-click |
| Left stick | Walk, climb, crouch |
| A | Jump |
| B | Craft |
| X | Inventory |
| Y | Wound view |
| Left trigger, right stick down | Switch hands |
| Left grip | Alt view |
| Left stick click, right stick up | Throw (hold to charge, release to throw, tap to drop) |
| Both grips held | Ragdoll |
| Left menu button | Pause |

Other controllers use the same layout, with these differences:

| Controllers | Differences |
|---|---|
| Valve Index | Left A / B are X / Y. There is no menu button: **press the left trackpad firmly** to pause. |
| HP Reverb G2 | Same as Touch. |
| Windows Mixed Reality | Right trackpad click = A, right menu = B, left trackpad click = X, left menu = pause. No Y. |
| HTC Vive wands | Trackpads act as sticks. Right trackpad click = A, right menu = B, left menu = pause. No X / Y. |

Every control can be remapped in the VR settings, under *Controls*. The mapping goes through the game's own key
binds, so rebinding the game's keys also carries over. The Index, Vive and Windows Mixed Reality layouts are new
in 1.2 and have not been tried on real hardware yet: reports are welcome.

Keyboard: **F10** start/stop VR, **F9** recenter, **Page Up / Page Down** zoom, **F11** show the menu panel,
**F12** VR settings, **F8** 3D blocks on/off, **F7** first person on/off.

## VR settings

Open the pause menu and press the cyan **VR** button, or press **F12**. Every row is `<  value  >` and changes
are saved immediately. Rows that can be switched off show *Off*: press `<` at the lowest value to switch them
off, and `>` to switch them back on. **Recenter** and **Reset page** (press twice) are at the top.

- **View:** zoom, diorama distance, window size and edge fade, HUD size, hover highlight, fog style on foggy layers
  (Mist / Haze / Off) and fog strength, menu panel position and size, cursor style (Hand / Cursor / Off) and
  size, controller hands and their size.
- **Blocks:** 3D blocks, block front and back depth, pop-up depth, object depth, character thickness, liquid depth
  (and its surface opacity).
- **1st person** and **Hands:** see *First-person mode*.
- **Controls:** any controller input to any game action (named as in the game's own key-bind settings), and the
  minigame hand offset (shift the game's hand image relative to where clicks land, if it does not line up with
  how you aim; 0 / 0 is the game's own placement).
- **Passthrough:** greenscreen. It paints everything outside the game window, and the game's background, in a
  key colour (green, blue, magenta, black, white, or custom RGB), for chroma-key passthrough such as Virtual
  Desktop's.

All settings are also in `BepInEx\config\casualtyvr.core.cfg`, with a few extra ones (for example
`ThicknessLayers` under `[Voxels]`).

## First-person mode (experimental)

Press **F7**, or open VR settings > *1st person*, to play from the character's eyes. You stand inside the 3D blocks,
looking along the tunnels. Menus, loading screens and the death screen still use the normal view. Press **F9**
(or *Recenter*) to put your head at the character's head.

**Moving**

| Control | Action |
|---|---|
| Left stick forward/back | Walk the way you face |
| Right stick up/down | Climb / crouch |
| Right stick flick sideways | Turn around (or smooth turning, in the settings) |
| A | Jump |

**Your hands**

The right controller is the character's active hand, and the left is the other hand.

| Gesture | Action |
|---|---|
| Grip near an item | Pick it up into that hand |
| Grip near a crate, door or machine | Use it |
| Grip near a rope or ladder, then pull down | Climb, hand over hand |
| Let go (slowly / while moving) | Drop / throw, harder the faster your hand moves |
| Held item to your chest, let go | Store it in the inventory |
| Empty hand to your chest, grip | Open the inventory (use it with the pointer) |
| Food, drink or pills held to your mouth | Eat, drink or take them |
| Fast swing of the right hand | Attack along the swing |
| Trigger | Attack or use, aimed where your hand points |
| Right grip with nothing in reach | The usual interact at the pointer |
| Minigames | Shown within reach: touch them with your hand, click with the trigger |

You see your own arms, with their wounds, clothing and what they hold, and your body and both legs when you look
down. Items, creatures and objects turn to face you. What your character says shows as a caption in front of you;
other characters' speech and damage numbers turn to face you. When lockpicking, the tip of the pick (or your
finger) is exactly where it acts.

**Settings**

- *1st person* tab:
  - Tunnel sides: cave walls, or a void that darkens with distance (none, 30%, 60% or 100%).
  - Characters and objects facing you.
  - *Highlight under your hand*: *Off* (the default), *Blocks only* (the game's striped marker on the block you
    aim at, for mining and building), or *Items and blocks* (also the game's outline on items and objects).
  - *Turning*: *Snap* (flick to turn around) or *Smooth* at 45 to 360 degrees a second.
  - *Movement speed*: how fast your character runs in first person (80% of the game's speed by default; the flat
    game and the diorama always use the game's own speed).
  - *When knocked down*: stay in first person, or switch to the diorama.
  - Fall vignette, blackout when unconscious, camera smoothing, tunnel width, view distance, world scale.
- *Hands* tab:
  - *Character looks where you look*, and *Aim with* hand or head.
  - Your arms and your body (each Off or a thickness), and how far behind your eyes the body sits.
  - Grab mode (hold the grip, or toggle), swing to attack, touch minigames, vibration (Off or a strength).

The both-grips ragdoll control is off in first person, because the grips are your hands; bind ragdoll to another
control in *Controls* if you want it.

## Linux (Proton)

The game is a Windows game, so on Linux it runs through Proton. This has not been tested yet; reports are
welcome. What it needs:

1. BepInEx needs a DLL override to load under Proton. In Steam, open the game's *Properties* > *Launch Options*
   and enter: `WINEDLLOVERRIDES="winhttp=n,b" %command%`
2. A Linux OpenXR runtime that Proton can reach. Proton passes OpenXR to SteamVR for Linux; other runtimes
   (Monado, WiVRn) need their own Proton setup.
3. The game must run with Direct3D 11 (Proton's default, through DXVK). Do not add `-force-vulkan`.

## Known limits

- The game's fog shader cannot render in VR. On foggy layers it is replaced by a VR mist (or a haze, or nothing).
- While the passthrough greenscreen is on, the game's colour grading is turned off in the headset so that the key
  colour stays exact.
- Very busy scenes (heavy rain, many creatures) can drop below 90 fps.
- Only the Demo 7.0.1 has been tested. Other game versions and other mods are untested.
- The controller hands and the hand cursor use the game's own hand art.
- First person is experimental. The game is drawn in 2D, so its lights, flat images and 2D shadows are adapted to
  work from inside the world: lighting is approximate, and creatures are seen side-on.

## Troubleshooting

- **No VR:** if VR cannot start, a message at the top of the game window says why. Check that your OpenXR runtime
  is running and set as the active runtime, then press F10. Details are in `BepInEx\LogOutput.log`; lines start
  with `[CasualtyVR]`.
- **"Different graphics card than the headset" (laptops):** Windows runs the game on the integrated GPU. In
  Windows Settings > Display > Graphics, set `CasualtiesUnknown.exe` to *High performance*.
- **"VR needs Direct3D 11":** remove `-force-vulkan` or `-force-d3d12` from the game's launch options.
- **The view is too low or high, off to one side, or the game is not in front of you:** face the way you want
  the game to be and press F9 (or *Recenter* in the settings). It sets both the position and the direction.
- **"The VR session ended":** the headset disconnected or its PC app (Virtual Desktop, SteamVR...) closed. Start
  it again, then press F10.
- **Diagnostics for a bug report:** set `VerboseLog = true` under `[Debug]` in the config, reproduce the problem,
  and send `BepInEx\LogOutput.log` and `BepInEx\CasualtyVR-bridge.log` (`CasualtyVR-bridge.prev.log` is the
  previous game session's).
