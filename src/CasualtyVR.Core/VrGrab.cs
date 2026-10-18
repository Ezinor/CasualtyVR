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
    /// Grab mode Hold: keep the grip to keep holding (default); Toggle: press to grab, press again to let go.
    /// </summary>
    internal static class VrGrab
    {
        private const float GrabRadius = 0.9f;       // blocks around the hand
        private const float ChestRadiusM = 0.22f;    // metres
        private const float ThrowSpeedM = 1.4f;      // m/s of hand speed to throw instead of drop
        private const float FullThrowSpeedM = 4.5f;  // m/s for full force
        private const float SwingSpeedM = 2.6f;      // m/s for a swing attack

        private sealed class HandState
        {
            public bool Grip, Toggled, Fallback, Climbing;
            public float MouthTime;
            public Vector3[] Track = new Vector3[6];   // room-space positions, last frames
            public float[] Times = new float[6];
            public int Count;
        }

        private static readonly HandState[] hands = { new HandState(), new HandState() };
        private static float nextSwing, nextMouth;
        private static int climbHand = -1;
        public static bool Active { get; private set; }

        /// <summary>Once per frame after VrInput.Update, while first person is on.</summary>
        public static void Update(Body body, bool menuOpen)
        {
            Active = !menuOpen && body != null && body.conscious;
            for (int h = 0; h < 2; h++)
            {
                var st = hands[h];
                if (!VrInput.HandTracking(h, out Vector3 roomPos, out float grip))
                {
                    st.Count = 0;
                    st.Grip = false;
                    continue;
                }
                // A held pose (short tracking dropout) is frozen: motion read across it would be a throw or swing.
                if (VrInput.HandPoseHeld(h)) st.Count = 0;
                else Track(st, roomPos);
                bool gripNow = grip >= (st.Grip ? 0.4f : 0.6f);
                bool pressed = gripNow && !st.Grip, released = !gripNow && st.Grip;
                st.Grip = gripNow;
                if (!Active) continue;
                VrInput.HandPose(h, out Vector3 handPos, out _, out _, out _);
                int slot = HandSlot(body, h);
                if (slot < 0) continue;
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
                if (released && body.HoldingItem(slot) && (!toggle || st.Toggled))
                {
                    st.Toggled = false;
                    LetGo(body, slot, handPos, Velocity(st));
                }
            }
            if (Active && Plugin.FpSwingAttack.Value) Swing(body);
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
        }

        // A fast swing of the right hand: one attack along the swing, with the game's own attack.
        private static void Swing(Body body)
        {
            var st = hands[1];
            if (Time.unscaledTime < nextSwing || st.Count < 3) return;
            Vector3 v = Velocity(st);
            if (v.magnitude < SwingSpeedM) return;
            if (!VrInput.HandPose(1, out Vector3 handPos, out _, out _, out _)) return;
            Vector3 dir = VrSpace.Rotation * v;
            Vector2 d2 = new Vector2(dir.x, dir.y);
            if (d2.sqrMagnitude < 0.25f * dir.sqrMagnitude) return;   // mostly across the tunnel: not a swing at anything
            if (d2.normalized.x * (FirstPerson.FacingRightNow ? 1f : -1f) < -0.3f) return;   // the backswing, towards yourself
            nextSwing = Time.unscaledTime + 0.45f;
            VrInput.AimAt((Vector2)handPos + d2.normalized * 2.5f);
            VrInput.Press("attack");
            if (Plugin.Verbose) Plugin.Log.LogInfo($"Grab: swing attack at {v.magnitude:F1} m/s towards {d2.normalized}");
            VrHaptics.Pulse(1, 0.6f, 0.06f);
        }

        // Hand over hand: while a hand grips the rope, its room-space movement moves the character along it the
        // other way (pull down = climb up). The last hand to grab leads; the game's own climbing does the rest.
        private static void Climb(Body body, HandState st, int h)
        {
            if (!body.currentClimbable || st.Count < 2) { st.Climbing = false; return; }
            if (climbHand != h) return;
            float dy = st.Track[0].y - st.Track[1].y;   // metres, room space
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

        private static void Track(HandState st, Vector3 p)
        {
            int n = st.Track.Length;
            for (int i = n - 1; i > 0; i--) { st.Track[i] = st.Track[i - 1]; st.Times[i] = st.Times[i - 1]; }
            st.Track[0] = p;
            st.Times[0] = Time.unscaledTime;
            if (st.Count < n) st.Count++;
        }

        // Room-space hand velocity (m/s) over the last few frames.
        private static Vector3 Velocity(HandState st)
        {
            int last = Mathf.Min(st.Count, st.Track.Length) - 1;
            if (last < 1) return Vector3.zero;
            last = Mathf.Min(last, 3);
            float dt = st.Times[0] - st.Times[last];
            return dt > 1e-4f ? (st.Track[0] - st.Track[last]) / dt : Vector3.zero;
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
