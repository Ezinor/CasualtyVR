# FP2_RECEIPT — first person: the character's arms (2026-10-05)

Plan: `docs/FIRSTPERSON_PLAN.md` (FP-2). Source backup: `Working/backups/fp2-20261005/`.
Status: **built and verified headless; waiting on the headset**.

## Rig facts (runtime dump, `Rig:` lines with the verbose log)

- **Limbs:** `Body.limbs` holds Head 0, UpTorso 1, DownTorso 2, UpArmF 3, DownArmF 4, HandF 5, UpArmB 6, DownArmB 7,
  HandB 8, then the legs (9–14). All limbs are flat children of `Body` (a ragdoll joined by HingeJoint2D), not a
  bone hierarchy.
- **Joints, in limb-local space:**

  | Joint | Point | Value |
  |---|---|---|
  | Shoulder | `UpArm.joint.anchor` | (0, 0.51) |
  | Elbow | `DownArm.joint.connectedAnchor` (in the upper arm) | (0, −0.50) |
  | Elbow | `DownArm.joint.anchor` | (0, 0.75) |
  | Wrist | `Hand.joint.connectedAnchor` (in the forearm) | (0, −0.69) |
  | Wrist | `Hand.joint.anchor` | (0, 0.18) |

  Every bone runs along local −y: upper arm 1.01 blocks, forearm 1.44, hand about 0.4. At the automatic 3.07
  blocks/m that is 33 cm and 47 cm.
- **Hand slots:**
  - Slot 0 (main) is a child of HandF; slot 1 is a child of HandB. Held items are children of the hand limbs.
  - `Body.handSlot` is the active hand.
- **Draw order:** the front arm draws at 150–170, the back arm at −60 to −40.

## What it does (`VrArms.cs`, setting *Your arms*, on)

- **Copies, not the real arms.** Both arms are drawn as copies on the VR-only layer of every sprite under the arm
  limbs: the limb art with its material (blood, wounds), clothing, and the held item. The game's ragdoll arms are
  untouched, and the own body stays hidden.
- **IK.** A two-bone solve runs from a shoulder below the head (0.24 m down, 0.19 m out, 0.04 m back, in the head's
  yaw frame) to a wrist just behind the controller's aim point. The elbow bends down and out
  (playbook HAND-005/006), and the hand points along the controller.
- **Facing the eye.** Each segment turns about its own bone to face the eye.
- **Drawn on top.** The copies draw after the world (sorting order 30000 and up) but are still depth-tested against
  the blocks.
- **Hands.** The right controller is the character's active hand (`Body.handSlot`), and switching hands swaps the
  arms. The controllers are read after this frame's input, so they don't lag a frame.

## Verification

| Check | Result |
|---|---|
| Build | 0 warnings, 0 errors |
| xr-tape, diorama | 19/19 |
| xr-tape, first person | 19/19 |
| Exceptions in the log | none |
| Arms in view | `fp2arms_raised_left.png` (lifepod, controllers raised): furry forearms from below, the left hand's pink palm, the right hand holding a spawned lantern (`--cvr-dbg-spawn lantern --cvr-dbg-hold`) |

New debug flag: `--cvr-dbg-hold` puts the first spawned item in the active hand.

## Open

- The hand art is the character's own side-view hand, with no grip or point poses yet. The game's minigame hand
  poses could be added.
- The arms are flat strips that turn to face you, with no volume.
- Shoulder offsets are fixed. Arm length is the character's, so the reach is about 0.8 m.
- Next: FP-3 interactions (pick up by grip, throw by release, building interact, chest to store).
