# CasualtyVR 1.2.0: Nexus Mods page

Paste each block into the matching Nexus field. The description and the per-file text use Nexus BBCode.

---

## Mod name

```
CasualtyVR - VR for Casualties Unknown
```

## Summary (the short text under the title, max 350 characters)

```
Play Casualties: Unknown in VR. The world becomes a 3D diorama of solid blocks in front of you, with full motion-controller support, menus on a panel within reach, and an experimental first-person mode where you see your own arms and body, grab things with your hands and climb hand over hand. OpenXR, BepInEx 5.
```

## Category and tags

- **Category:** Gameplay, or "Visuals and Graphics" / "Utilities" if Nexus has no VR category for this game.
- **Tags:** VR, OpenXR, BepInEx, First Person, Immersion, Controller.

## Requirements (Nexus "Requirements" section)

- **Off-site requirement:** BepInEx 5 (x64, Unity Mono), https://github.com/BepInEx/BepInEx/releases. Note: "Version 5.4.23.x, the `BepInEx_win_x64` zip."
- **Off-site requirement:** an OpenXR runtime for your headset. Note: "Virtual Desktop, SteamVR, Meta Quest Link, or another OpenXR runtime."

## Description (BBCode)

```
[center][size=6][b]CasualtyVR[/b][/size]
[size=4]Play [i]Casualties: Unknown[/i] in virtual reality[/size][/center]

[size=4][b]What it does[/b][/size]
[list]
[*][b]3D diorama.[/b] The game plays out on a tabletop-sized diorama in front of you. The terrain is built from solid blocks, liquids fill their tunnels, and characters and objects get real depth. The flat game on your monitor stays exactly as it was.
[*][b]Motion controls.[/b] Point with the right controller to aim and click. Every button maps to a game action, and everything can be remapped in-game.
[*][b]Menus within reach.[/b] Inventory, crafting, the health panel and the minigames appear on a panel below the diorama, and the HUD sits around the view.
[*][b]First-person mode (experimental, F7).[/b] Stand inside the world at your character's eyes and look down the tunnels:
[list]
[*]Your own arms follow your controllers, with their wounds, clothing and whatever you hold. Your body and both legs are there when you look down.
[*]Grab items with your hands, throw them, and climb ropes and ladders hand over hand.
[*]Bring food or drink to your mouth to eat or drink, put items in your inventory at your chest, and swing to attack.
[*]Touch the minigames (bandaging, lockpicking, keypadsâ€¦) on a panel within reach.
[*]Items, creatures and plants turn to face you. What your character says appears as a caption in front of you.
[*]Smooth fog, rain and lighting adapted to work from inside the world.
[*]Comfort options: snap or smooth turning, movement speed, camera smoothing, fall vignette, blackout when unconscious, and switching to the diorama while knocked down.
[/list]
[*][b]In-game VR settings.[/b] A cyan [b]VR[/b] button on the pause menu (or F12) opens them. View, blocks, first person, hands, controls and passthrough can all be tuned while you play, with a reset button on every page.
[*][b]Passthrough greenscreen[/b] for mixed reality with Virtual Desktop and similar.
[/list]

[size=4][b]Requirements[/b][/size]
[list]
[*][i]Casualties: Unknown[/i] [b]Demo 7.0.1[/b] (Steam).
[*][url=https://github.com/BepInEx/BepInEx/releases]BepInEx 5.4.23.x[/url], [b]x64, Unity Mono[/b] build ([font=Courier New]BepInEx_win_x64_5.4.23.x.zip[/font]).
[*]A PC VR headset with an [b]OpenXR[/b] runtime: Virtual Desktop, SteamVR, Meta Quest Link, Windows Mixed Reality, Pimax, Varjoâ€¦
[*]A Direct3D 11 graphics card. It targets 90 fps; developed on an RTX 3070.
[/list]

[size=4][b]Installation[/b][/size]
[list=1]
[*]Install BepInEx 5 (x64) into the game folder, the one with [font=Courier New]CasualtiesUnknown.exe[/font]. Start the game once so BepInEx creates its folders, then quit.
[*]Extract this mod's zip into the same game folder. You should now have [font=Courier New]BepInEx\plugins\CasualtyVR\CasualtyVR.Core.dll[/font].
[*]Start your headset's PC app (Virtual Desktop, SteamVR, Quest Linkâ€¦), start the game, and press [b]F10[/b] to switch to VR.
[/list]
VR is off at launch by default. To start in VR every time, set [font=Courier New]StartVrOnLaunch = true[/font] under [font=Courier New][VR][/font] in [font=Courier New]BepInEx\config\casualtyvr.core.cfg[/font].

[b]Uninstall:[/b] delete [font=Courier New]BepInEx\plugins\CasualtyVR[/font] (and [font=Courier New]BepInEx\config\casualtyvr.core.cfg[/font] to forget the settings).

[size=4][b]Controls[/b][/size]
[b]Meta Quest Touch[/b] (also Pico through Virtual Desktop or SteamVR):
[list]
[*]Right controller pointing: cursor (aim, click, menus)
[*]Right trigger: attack / click. Right grip: interact
[*]Left stick: walk, climb, crouch
[*]A: jump. B: craft. X: inventory. Y: wound view
[*]Left trigger or right stick down: switch hands. Left grip: alt view
[*]Left stick click or right stick up: throw (hold to charge)
[*]Both grips: ragdoll. Left menu button: pause
[/list]
[b]Other controllers[/b] use the same layout, with these differences:
[list]
[*][b]Valve Index:[/b] left A/B are X/Y; press the [b]left trackpad firmly[/b] to pause.
[*][b]Windows Mixed Reality and Vive wands:[/b] trackpad clicks and the menu buttons stand in for the missing buttons. See the README in the download.
[/list]
[b]Keyboard:[/b] F10 VR on/off, F9 recenter, F7 first person, F12 VR settings, F11 menu panel, F8 3D blocks on/off, Page Up/Down zoom.

[b]First person:[/b] left stick walks the way you face, right stick climbs or crouches, and a flick of the right stick sideways turns you around (or turn smoothly). Grip near things to grab them.

[size=4][b]Linux (Proton)[/b][/size]
Untested, but it should work. Add this to the game's Steam launch options so BepInEx loads:
[font=Courier New]WINEDLLOVERRIDES="winhttp=n,b" %command%[/font]
You also need an OpenXR runtime that Proton can reach (SteamVR for Linux). Reports are welcome.

[size=4][b]Known limits[/b][/size]
[list]
[*]Only the Demo 7.0.1 has been tested. Other game versions and other mods are untested.
[*]Tested on a Meta Quest 3 with Virtual Desktop. Index, Vive and Windows Mixed Reality controller layouts are new and untested on real hardware.
[*]First person is experimental. The game is drawn in 2D, so lighting is approximate and creatures are seen side-on.
[*]Very busy scenes (heavy rain, many creatures) can drop below 90 fps.
[/list]

[size=4][b]Troubleshooting[/b][/size]
[list]
[*][b]Nothing happens on F10:[/b] a message at the top of the game window says why. Usually the OpenXR runtime isn't running or isn't set as the active one.
[*][b]"Different graphics card than the headset" (laptops):[/b] in Windows Settings > Display > Graphics, set [font=Courier New]CasualtiesUnknown.exe[/font] to High performance.
[*][b]View too high, low or off to one side:[/b] look straight ahead and press F9.
[*][b]Bug reports:[/b] set [font=Courier New]VerboseLog = true[/font] under [font=Courier New][Debug][/font] in the config, reproduce the problem, and attach [font=Courier New]BepInEx\LogOutput.log[/font].
[/list]

[size=4][b]Credits[/b][/size]
[list]
[*][url=https://github.com/BepInEx/BepInEx]BepInEx[/url] and HarmonyX
[*][url=https://www.khronos.org/openxr/]OpenXR[/url] loader by the Khronos Group (Apache 2.0)
[*][i]Casualties: Unknown[/i] by Orsoniks
[/list]
Source licence: MIT.
```

## Main file (Files tab)

- **File name:** `CasualtyVR 1.2.0`
- **Version:** `1.2.0`
- **Category:** Main file
- **Upload:** `dist/CasualtyVR-1.2.0.zip` (SHA-256 `E03419F8883869B413D08BE3A8E13A9AE6CCCBBEB8108E762C43F2F3E846466F`)
- **File description:**

```
VR mod for Casualties: Unknown Demo 7.0.1. Requires BepInEx 5 (x64). Extract into the game folder and press F10 in game. New in 1.2: first-person polish, lockpicking fix, more controller layouts, consolidated VR settings.
```

## Changelog (Nexus "Logs" tab, version 1.2.0)

```
- First person: performance 40 -> 90 fps, smooth fog, rain
- First person: slower running by default (Movement speed), upright body that no longer blocks the view, both legs, readable speech captions, no highlights under the hand by default
- Lockpicking: the tip of the pick (or finger) is now exactly where it acts
- VR settings: Reset page, fewer and clearer rows, Recenter button, game tooltips no longer cover the settings
- Controllers: Valve Index, HTC Vive, Windows Mixed Reality and HP Reverb G2 layouts
- Compatibility: no Visual C++ runtime needed, better error messages when VR cannot start, Linux/Proton notes
- The game window is no longer moved to another monitor by default
```

## Images

- **Main image (1920x1080, 16:9):** `release/nexus/casualtyvr_nexus_main.png`. Made from real in-game captures, the diorama on the left and first person on the right.
- **Banner (1920x480):** `release/nexus/casualtyvr_nexus_banner.png`. Use it as the page header if Nexus offers one for this game. Otherwise put it at the very top of the description: upload it in the Images tab, copy its link, and start the description with `[center][img]<image link>[/img][/center]`. Rebuild with `make_banner.py`.
- **More gallery images** from the same captures (`Working/xrsim-shots/`), if you want them:
  - `nxfp_hands_left.png`: first person, arms raised in the lifepod.
  - `lock2_ringtop_left.png`: lockpicking on the touch panel.
  - `talk2_a_left.png`: speech caption.
- **VR settings** (`release/nexus/gallery/`, captured 2026-10-11 from the 1.2.0 build):
  - `vr_settings_in_game.png`: the settings panel below the diorama, as you see it in the headset.
  - `vr_settings_view.png`, `vr_settings_blocks.png`, `vr_settings_first_person.png`, `vr_settings_hands.png`, `vr_settings_controls.png`, `vr_settings_passthrough.png`: one per tab.
  - The values shown are from the dev config (smooth turning at 300°/s, fall vignette 0), not all defaults.
- Screenshots from your headset will look better than these 1032x1104 simulator captures, especially the diorama.

## Before you publish

- **Game page:** if Nexus has no page for *Casualties: Unknown* yet, request one through Nexus's "add a game" form first.
- **Permissions:** the code is MIT, so open permissions fit. The OpenXR loader is Apache 2.0, and its licence is in the zip.
- **AI disclosure:** the mod's code and text were written with an AI coding assistant (Claude); the images are real game captures. Nexus's AI-content tagging rules apply mainly to generated assets. Add a line such as "Developed with the help of Claude (Anthropic)" to the Credits if you want to be transparent.
