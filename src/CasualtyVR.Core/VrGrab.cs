using UnityEngine;

namespace CasualtyVR
{
    /// <summary>
    /// First person (FP-3): physical interaction, through the game's own methods so its rules (reach, line of
    /// sight, slot limits, item weight) still apply (playbook HAND-018). Outside menus, both grips are hands:
    ///
    /// * Grip near an item (empty hand): Body.PickUpItem into that hand's slot (the right controller is the
    ///   active hand slot, the left the other hand slot). Grip near a machine, crate or door: its OnUse, as the
    ///   game's right-click does. Grip at your chest with an empty hand: the inventory radial (on the panel).
    /// * Right grip with nothing in reach: the game's own right-click at the aim point (pick up or use at range).
    /// * Releasing a held item: slow = Body.DropItem; moving = Body.ThrowItem along the hand's motion, force from
    ///   the hand's speed; at your chest = into the first free inventory slot.
    /// * A fast swing of the right hand attacks along the swing (FirstPerson.SwingAttack); the trigger still does.
    /// Hand motion is measured in room space (playbook INPUT-010), so walking does not count as a swing or throw.
    /// Phase 4a: everything is at the grip pose (where the hand holds things); swings and throws follow the recipes in
    /// HandMotion (re-armed swings, gated by what the hand holds; grip-drop release, peak velocity, thrown from your hand).
    /// Grab mode Hold: keep the grip to keep holding (default); Toggle: press to grab, press again to let go.
    /// </summary>
    internal static class VrGrab
    {
        private const float GrabRadius = 0.9f;       // blocks around the hand
        private const float ChestRadiusM = 0.22f;    // metres
        public const float ThrowSpeedM = 1.4f;       // m/s of hand speed to throw instead of drop
        private const float FullThrowSpeedM = 4.5f;  // m/s for full force

        private sealed class HandState
        {
            public bool Grip, Toggled, Fallback, Climbing;
            public float MouthTime;
            public readonly HandMotion Motion = new HandMotion();
        }

        private static readonly HandState[] hands = { new HandState(), new HandState() };
        private static float nextMouth;
        private static int gated, cooling;
        private static string lastGatedItem;
        private static int climbHand = -1;
        public static bool Active { get; private set; }

        /// <summary>Once per frame after VrInput.Update, while first person is on.</summary>
        public static void Update(Body body, bool menuOpen)
        {
            Active = !menuOpen && body != null && body.conscious;
            MotionTakes.Update(body);
            for (int h = 0; h < 2; h++)
            {
                var st = hands[h];
                if (!VrInput.GripTracking(h, out Vector3 roomPos, out _, out float grip, out _))
                {
                    st.Motion.Reset();
                    st.Grip = false;
                    st.Motion.Cancel();
                    continue;
                }
                // A held pose (short tracking dropout) is frozen: motion read across it would be a throw or swing.
                if (VrInput.HandPoseHeld(h)) st.Motion.Reset();
                else st.Motion.Add(roomPos, Time.unscaledTime);
                int slot = body != null ? HandSlot(body, h) : -1;
                bool holding = slot >= 0 && body.HoldingItem(slot);
                bool gripNow = grip >= (st.Grip ? 0.4f : 0.6f);
                bool pressed = gripNow && !st.Grip, released = !gripNow && st.Grip;
                // Holding an item: it is let go when the analog grip falls 30 % below its peak (HandMotion.Release).
                bool letGo = st.Motion.Release(holding, grip);
                st.Grip = gripNow;
                if (!Active || slot < 0) continue;
                VrInput.GripPose(h, out Vector3 handPos, out _);
                bool toggle = Plugin.FpGrabMode.Value == "Toggle";
                if (pressed)
                {
                    if (body.HoldingItem(slot))
                    {
                        if (toggle) st.Toggled = true;    // second press: let go on release
                    }
                    else if (!Grab(body, slot, handPos, h)) st.Fallback = h == 1;
                    else st.Toggled = false;
                }
                // Right grip with nothing in reach: the game's own right-click at the aim point, held while the grip is.
                if (!gripNow) { st.Fallback = false; st.Climbing = false; }
                if (st.Climbing) Climb(body, st, h);
                Mouth(body, st, slot, handPos, h);
                if (st.Fallback) VrInput.Press("iteminteract");
                if (holding && (toggle ? released && st.Toggled : letGo))
                {
                    st.Toggled = false;
                    // A throw cancels a swing still waiting to be confirmed: that flick was the throw.
                    if (st.Motion.Cancel() && Plugin.Verbose) Plugin.Log.LogInfo("Grab: swing cancelled, the grip let go (a throw)");
                    LetGo(body, slot, handPos, st.Motion.PeakVelocity());
                }
            }
            if (Active && Plugin.FpSwingAttack.Value) Swing(body);
            else hands[1].Motion.Cancel();
        }

        /// <summary>Whether hand h (0 left, 1 right) holds an item now.</summary>
        public static bool HoldingInHand(Body body, int h)
        {
            int slot = HandSlot(body, h);
            return slot >= 0 && body.HoldingItem(slot);
        }

        /// <summary>Verbose perf line: the right hand's swing recipe since the last report.</summary>
        public static string Report()
        {
            string r = $"swing: {hands[1].Motion.Report()}, gated {gated} (last '{lastGatedItem ?? "-"}'), cooling down {cooling}";
            gated = cooling = 0;
            return r;
        }

        // The right controller is the character's active hand slot, the left the other hand slot.
        private static int HandSlot(Body body, int h)
        {
            if (body.slots == null) return -1;
            int active = body.handSlot >= 0 && body.handSlot < body.slots.Length && body.slots[body.handSlot] != null && body.slots[body.handSlot].isHand ? body.handSlot : -1;
            if (h == 1) return active >= 0 ? active : FirstHand(body, -1);
            return FirstHand(body, active);
        }

        private static int FirstHand(Body body, int except)
        {
            for (int i = 0; i < body.slots.Length; i++)
                if (i != except && body.slots[i] != null && body.slots[i].isHand) return i;
            return -1;
        }

        private static Vector3 Chest()
        {
            Vector3 fwd = VrSpace.HeadForward;
            fwd.y = 0f;
            fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.right;
            return VrSpace.HeadPosition + (-0.38f * Vector3.up + 0.1f * fwd) * VrSpace.Scale;
        }

        private static bool AtChest(Vector3 handPos) => (handPos - Chest()).magnitude < ChestRadiusM * VrSpace.Scale;

        private static bool Grab(Body body, int slot, Vector3 handPos, int h)
        {
            var pc = PlayerCamera.main;
            if (AtChest(handPos))
            {
                if (pc != null) pc.radialOpen = true;
                if (Plugin.Verbose) Plugin.Log.LogInfo("Grab: chest -> inventory radial");
                return true;
            }
            Vector2 at = handPos;
            // Nearest loose item that is not the player's own.
            Item best = null;
            float bestD = float.MaxValue;
            foreach (var c in Physics2D.OverlapCircleAll(at, GrabRadius, LayerMask.GetMask("Item")))
            {
                if (!c.TryGetComponent<Item>(out var item) || body.HoldingItem(item) || item.transform.IsChildOf(body.transform)) continue;
                float d = ((Vector2)c.bounds.ClosestPoint(at) - at).sqrMagnitude;
                if (d < bestD) { bestD = d; best = item; }
            }
            if (Plugin.Verbose) Plugin.Log.LogInfo($"Grab: hand {h} slot {slot} at {at} nearest item {(best != null ? best.name + " " + Mathf.Sqrt(bestD).ToString("F2") : "none")}");
            if (best != null)
            {
                body.PickUpItem(best, slot);
                if (Plugin.Verbose) Plugin.Log.LogInfo($"Grab: picked up {best.name}: {body.HoldingItem(best)}");
                if (body.HoldingItem(best))
                {
                    VrHaptics.Pulse(h, 0.4f, 0.05f);
                    return true;
                }
                return true;   // refused by the game (blocked, too far): its own alert explains
            }
            // A rope or ladder: the game's own climbing starts, and the gripping hand pulls (Climb).
            var climbable = Climbable.GetClosestClimbable(body.transform.position);
            if (Plugin.Verbose && climbable != null) Plugin.Log.LogInfo($"Grab: climbable {climbable.name} hand distance {climbable.GetGrabInfo(at).distanceToPlayer:F2}, body distance {climbable.GetGrabInfo(body.transform.position).distanceToPlayer:F2}");
            if (climbable != null && climbable.GetGrabInfo(at).distanceToPlayer < GrabRadius)
            {
                if (!body.currentClimbable) body.StartClimbing(climbable);
                if (body.currentClimbable)
                {
                    hands[h].Climbing = true;
                    climbHand = h;
                    VrHaptics.Pulse(h, 0.35f, 0.04f);
                    if (Plugin.Verbose) Plugin.Log.LogInfo($"Grab: hand {h} took hold of {climbable.name}");
                    return true;
                }
            }
            // A machine, crate or door: its OnUse, as the game's right-click does.
            foreach (var c in Physics2D.OverlapCircleAll(at, GrabRadius))
            {
                var usable = c.GetComponentInParent<UsableObject>();
                if (usable == null) continue;
                if (Vector2.Distance(body.transform.position, usable.transform.position) >= 10f * usable.rangeMultiplier) continue;
                usable.gameObject.SendMessage("OnUse", SendMessageOptions.DontRequireReceiver);
                pc?.PlayUISound(PlayerCamera.UISoundType.Click);
                VrHaptics.Pulse(h, 0.3f, 0.04f);
                return true;
            }
            return false;
        }

        private static void LetGo(Body body, int slot, Vector3 handPos, Vector3 roomVel)
        {
            var item = body.GetItem(slot);
            if (item == null) return;
            MotionTakes.Note($"release {roomVel.magnitude:F2}");
            // Into the inventory: the first free slot that is not a hand.
            if (AtChest(handPos))
            {
                for (int i = 0; i < body.slots.Length; i++)
                {
                    var s = body.slots[i];
                    if (s == null || s.isHand || !s.canPickUp || body.HoldingItem(i)) continue;
                    body.DropItem(item);
                    body.PickUpItem(item, i, force: true);
                    if (body.HoldingItem(item)) { PlayerCamera.main?.PlayBackpackSound(); if (Plugin.Verbose) Plugin.Log.LogInfo($"Grab: stored {item.name} in slot {i}"); return; }
                    break;
                }
            }
            float speed = roomVel.magnitude;
            if (Plugin.Verbose) Plugin.Log.LogInfo($"Grab: let go of {item.name} at {speed:F2} m/s -> {(speed < ThrowSpeedM ? "drop" : "throw")}");
            if (speed < ThrowSpeedM)
            {
                body.DropItem(item);
                return;
            }
            // Throw along the hand's motion (world), with the game's own throw.
            Vector3 dir = VrSpace.Rotation * roomVel;
            Vector2 d2 = new Vector2(dir.x, dir.y);
            if (d2.sqrMagnitude < 1e-4f) d2 = new Vector2(FirstPerson.FacingRightNow ? 1f : -1f, 0.2f);
            int saved = body.handSlot;
            body.handSlot = slot;
            body.targetLookPos = (Vector2)item.transform.position + d2.normalized * 5f;
            body.ThrowItem(Mathf.Clamp01(speed / FullThrowSpeedM));
            body.handSlot = saved;
            // From where your hand was at the release (Quake VR: not where the game's own hand is), if nothing solid is
            // between the body and the hand, and within arm's reach.
            Vector2 from = body.transform.position, at = handPos;
            bool placed = Vector2.Distance(from, at) < 4f && !Physics2D.Linecast(from, at, LayerMask.GetMask("Ground"));
            if (placed && item.rb != null)
            {
                item.rb.position = at;
                item.transform.position = new Vector3(at.x, at.y, item.transform.position.z);
            }
            if (Plugin.Verbose) Plugin.Log.LogInfo($"Grab: threw {item.name} at {speed:F2} m/s (peak over 150 ms) towards {d2.normalized}, from {(placed ? "the hand" : "the game's hand")}");
        }

        // A fast swing of the right hand: one attack along the swing, with the game's own attack (HandMotion.Swing).
        private static void Swing(Body body)
        {
            var st = hands[1];
            Vector3 shoulder = VrSpace.HeadTrackingPos + new Vector3(0f, -0.25f, 0f);
            if (st.Motion.Swing(shoulder, v =>
                {
                    Vector3 w = VrSpace.Rotation * v;
                    Vector2 w2 = new Vector2(w.x, w.y);
                    if (w2.sqrMagnitude < 0.25f * w.sqrMagnitude) return false;   // mostly across the tunnel: not a swing at anything
                    return w2.normalized.x * (FirstPerson.FacingRightNow ? 1f : -1f) >= -0.3f;   // not the backswing, towards yourself
                }, out Vector3 vel))
            {
                // The held-item gate (FAIL-HAND-006): the attack key uses an item with its own left-button action (a gun
                // fires, scrap metal is placed, a light switches). Only bare hands, items without one (the game punches)
                // and melee tools (the game's "tool" tag: every one of them attacks) swing.
                var item = body.GetItem(body.handSlot);
                bool ok = item == null || !item.Stats.usable || !item.Stats.usableWithLMB || item.Stats.HasTag("tool");
                if (!ok)
                {
                    gated++;
                    if (Plugin.Verbose && lastGatedItem != item.name) Plugin.Log.LogInfo($"Grab: swing not used with {item.name}: its attack key uses it (gun, placeable, light)");
                    lastGatedItem = item.name;
                    MotionTakes.Note("swing gated");
                }
                else if (HoldingInHand(body, 1)) st.Motion.Hold(vel, Time.unscaledTime);   // could be a throw: wait for the peak
                else Attack(body, vel);
            }
            if (st.Motion.Due(Time.unscaledTime, out Vector3 held)) Attack(body, held);
        }

        private static void Attack(Body body, Vector3 vel)
        {
            // The game's own rate: still recovering from the last attack, no new one (and none queued).
            if (body.attackCooldown > 0f) { cooling++; return; }
            if (!VrInput.GripPose(1, out Vector3 handPos, out _)) return;
            Vector3 dir = VrSpace.Rotation * vel;
            Vector2 d2 = new Vector2(dir.x, dir.y).normalized;
            VrInput.AimAt((Vector2)handPos + d2 * 2.5f);
            VrInput.Press("attack");
            MotionTakes.Note("swing");
            if (Plugin.Verbose) Plugin.Log.LogInfo($"Grab: swing attack at {vel.magnitude:F1} m/s towards {d2}");
            VrHaptics.Pulse(1, 0.6f, 0.06f);
        }

        // Hand over hand: while a hand grips the rope, its room-space movement moves the character along it the
        // other way (pull down = climb up). The last hand to grab leads; the game's own climbing does the rest.
        private static void Climb(Body body, HandState st, int h)
        {
            if (!body.currentClimbable || st.Motion.Count < 2) { st.Climbing = false; return; }
            if (climbHand != h) return;
            float dy = st.Motion.LastStep.y;   // metres, room space
            float len = body.currentClimbable.totalLength;
            body.climbableProgress = Mathf.Clamp(body.climbableProgress - dy * VrSpace.Scale, 0.01f, len);
            body.climbVelocity = 0f;
        }

        // Bringing a held food, drink or drug to your mouth for a moment uses it, as the game's own use does.
        private static void Mouth(Body body, HandState st, int slot, Vector3 handPos, int h)
        {
            var item = body.GetItem(slot);
            bool edible = item != null && item.Stats.usable && (item.Stats.category == "food" || item.Stats.category == "water" || item.Stats.category == "drug");
            Vector3 fwd = VrSpace.HeadForward;
            fwd.y = 0f;
            fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.right;
            Vector3 mouth = VrSpace.HeadPosition + (-0.08f * Vector3.up + 0.05f * fwd) * VrSpace.Scale;
            bool near = edible && (handPos - mouth).magnitude < 0.15f * VrSpace.Scale;
            st.MouthTime = near ? st.MouthTime + Time.unscaledDeltaTime : 0f;
            if (!near || st.MouthTime < 0.35f || Time.unscaledTime < nextMouth) return;
            nextMouth = Time.unscaledTime + 1.2f;
            st.MouthTime = 0f;
            if (Plugin.Verbose) Plugin.Log.LogInfo($"Grab: {item.name} to the mouth -> use");
            body.UseItem(item);
            VrHaptics.Pulse(h, 0.25f, 0.08f);
        }

    }

    /// <summary>Controller vibration through the bridge (xrApplyHapticFeedback); setting Controllers.Haptics.</summary>
    internal static class VrHaptics
    {
        private static bool logged, failed;

        public static void Pulse(int hand, float amplitude, float seconds)
        {
            if (Plugin.Verbose && !logged) { logged = true; Plugin.Log.LogInfo($"Haptics: first pulse hand {hand} ({(Plugin.Haptics.Value ? "on" : "off")}, VR {(VrRig.Instance != null && VrRig.Instance.Active)})"); }
            if (!Plugin.Haptics.Value || VrRig.Instance == null || !VrRig.Instance.Active) return;
            try { XrNative.cvr_xr_haptic(hand, amplitude * Plugin.HapticStrength.Value, seconds); }
            catch (System.Exception ex) { if (!failed) { failed = true; Plugin.Log.LogWarning("Haptics unavailable: " + ex.Message); } }
        }
    }
}
