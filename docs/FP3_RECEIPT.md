# FP3_RECEIPT — first person: hands that act (2026-10-05)

Plan: `docs/FIRSTPERSON_PLAN.md` (FP-3). Source backup: `Working/backups/fp3-20261005/`.
Status: **built and verified headless; waiting on the headset**.

## How the game does it (`PlayerCamera`, decompiled)

- **Right-click (`iteminteract`) starts a drag at the mouse** (`HandleStartDragging`). What it does depends on what is
  under the mouse:
  - an Item becomes `dragItem`;
  - the player's own body opens the inventory radial (`radialOpen`);
  - a `UsableObject` (crate, door, machine, lifepod button) gets `SendMessage("OnUse")`.
- **Dropping the drag on a radial hand slot** calls `Body.PickUpItem(item, slot)`. That call checks reach (10 blocks)
  and line of sight (`DoPickupCheck`).
- **Throw:** `Body.ThrowItem(force)` throws the active hand's item toward `targetLookPos`, with force 0–1.
- **Drop:** `Body.DropItem`.

## What it does (`VrGrab.cs`)

In first person, outside menus, both grips are hands; their button bindings (and both-grips ragdoll) are off there.
Every action goes through the game's own methods, so its rules still apply (playbook HAND-018).

| Gesture | Game call |
|---|---|
| Grip with an empty hand near a loose item (0.9 blocks) | `PickUpItem` into that hand's slot. The right controller is the active hand slot; the left is the other hand. |
| Grip near a crate, door or machine | its `OnUse`, as right-click does |
| Grip at your chest (0.22 m below and ahead of the head), empty hand | the inventory radial, shown on the panel (`VrUi.MenuOpen` now includes the radial in first person) |
| Right grip with nothing in reach | the game's own right-click at the aim point, held while the grip is held (pick up or use at range, drag) |
| Let go of a held item, slowly (under 1.4 m/s) | `DropItem` |
| Let go while moving | `ThrowItem` along the hand's motion, with force = speed / 4.5 m/s |
| Let go at your chest | into the first free non-hand slot (a backpack sound plays) |
| Fast swing of the right hand (2.6 m/s or more, away from you along the tunnel; at most one per 0.45 s) | one attack aimed along the swing |
| Trigger | attack or use, as before (aimed with the hand ray) |

- **Motion is measured in room space** (playbook INPUT-010), so walking or falling never counts as a swing or a
  throw.
- **Settings** (*First person* tab):
  - *Grab*: Hold grip (default; keep the grip held to keep the item) or Toggle (press to grab, press again to let go).
  - *Swing to attack* (on).
- **Haptics:** `VrHaptics.Pulse` is a stub until the native bridge exposes vibration (open item).

## Verification (xr-sim, scripted hands)

| Test | Log |
|---|---|
| Right hand swept down to a spawned water bottle, grip | `Grab: hand 1 slot 0 ... nearest item waterbottle 0.08` → `picked up waterbottle: True` |
| Fast hand move while releasing | `let go of waterbottle at 7.00 m/s -> throw` |
| Holding a bottle (`--cvr-dbg-hold`), hand to the chest, press and release | `stored waterbottle in slot 2` |
| Empty hand at the chest, grip | `chest -> inventory radial`, `UI mode: dashboard`; capture `fp3b_radial_left.png` shows the radial on the panel, with the bottle in a slot |
| Hand swings in a cave | `swing attack at 5.9 m/s ...`; backswings are now ignored |
| xr-tape, diorama and first person | 19/19 and 19/19, no exceptions |

## Open

- **Ragdoll:** the both-grips ragdoll is off in first person (both grips are hands). It can be bound to another
  control under *Controls*.
- **Haptics:** need a bridge call.
- **Hand poses:** grip and point poses on the character's hand art.
- **No reach highlight:** there is no highlight on what a hand would grab.
- **Climbing:** uses the right stick (up/down), as in FP-0. Grip-climbing is FP-4.
