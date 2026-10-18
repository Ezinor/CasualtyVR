using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace CasualtyVR
{
    /// <summary>
    /// Maps the controllers onto the game's own inputs (playbook INPUT-001: the bridge transports physical
    /// state; the semantic mapping lives here, at the game boundary).
    ///
    /// * The laser (aim pose, HAND-001) is the mouse: one hit record per frame (HUD-001) drives the virtual
    ///   mouse position, the drawn beam and the cursor, on whichever surface is active: the theatre panel,
    ///   the dashboard, or the game plane (z=0, where the HUD is also mapped).
    /// * Buttons become the game's actions, looked up through the game's own KeyBinds, so user rebinding
    ///   carries over.
    /// * Not focused (XR-002) or VR stopped: everything is released; the game sees ordinary key-up edges.
    /// The game reads Unity's legacy Input class; Harmony postfixes on it (InputPatches) merge this state in.
    /// </summary>
    internal static class VrInput
    {
        // Default game action (KeyBinds name) per physical control; the live mapping is the [Controls] config
        // section, editable from the VR settings menu. Defaults agreed with the user, 2026-10-04: the left
        // menu button works as pause on Quest (VDXR), so R stick click is free; throw on L stick click
        // (hold to charge, release to throw, tap to drop); ragdoll on both grips held.
        internal static readonly (string control, string action)[] Defaults =
        {
            ("R.trigger", "attack"), ("R.grip", "iteminteract"), ("A", "jump"), ("B", "craft"),
            ("R.stickUp", "throw"), ("R.stickDown", "switchhands"), ("R.stickClick", "none"),
            ("L.stickLeft", "left"), ("L.stickRight", "right"), ("L.stickUp", "up"), ("L.stickDown", "down"),
            ("X", "toggleinventory"), ("Y", "woundview"), ("L.trigger", "switchhands"), ("L.grip", "altview"),
            ("L.stickClick", "throw"), ("Menu", "pause"), ("BothGrips", "ragdoll"),
        };

        /// <summary>Every game action a control can be set to (the game's KeyBinds names), plus none.</summary>
        internal static readonly string[] Actions =
        {
            "none", "attack", "iteminteract", "jump", "craft", "throw", "switchhands", "favourite", "pause",
            "left", "right", "up", "down", "toggleinventory", "woundview", "altview", "ragdoll", "expanddesc", "bark",
        };

        public static bool Enabled;
        public static bool LaserMode { get; private set; }
        public static Vector3 VirtualMouse { get; private set; }
        public static bool LaserHit { get; private set; }
        public static Vector3 LaserOrigin { get; private set; }
        public static Vector3 LaserEnd { get; private set; }
        public static bool LaserVisible { get; private set; }
        public static string Profile { get; private set; } = "";
        /// <summary>First person: the laser points at a mod's OnGUI window on the HUD panel.</summary>
        public static bool OnModWindow { get; private set; }
        /// <summary>While the VR settings menu is open: either stick's up/down (it scrolls the menu; the game gets no stick).</summary>
        public static float MenuStickY { get; private set; }

        private static HashSet<KeyCode> held = new HashSet<KeyCode>();
        private static HashSet<KeyCode> prevHeld = new HashSet<KeyCode>();
        private static readonly HashSet<string> controlsOn = new HashSet<string>();
        private static readonly HashSet<string> suppressed = new HashSet<string>();   // held at focus regain
        private static bool wasFocused;
        private static Vector3 lastRealMouse;
        private static Vector3 lastAimDir;
        private static float lastControllerActivity = -100f, lastMouseActivity = -100f;
        private static bool everHit;

        public static string HeldList() => string.Join(",", held);

        // Last frame's controller state and diorama mapping, for the hand visuals (VrHands).
        private static XrNative.Input lastInput;
        private static Vector3 lastFocus, lastSeated;
        private static float lastScale;

        /// <summary>Hand h (0 left, 1 right) aim pose in world units, plus trigger and grip.</summary>
        public static bool HandPose(int h, out Vector3 pos, out Vector3 forward, out float trigger, out float grip)
        {
            var hand = h == 0 ? lastInput.left : lastInput.right;
            pos = forward = Vector3.zero;
            trigger = hand.trigger;
            grip = hand.squeeze;
            if (!Enabled || lastInput.focused == 0 || hand.poseValid == 0) return false;
            pos = VrSpace.Point(VrSpace.ToUnity(hand.aimPosition));
            forward = VrSpace.Rot(VrSpace.ToUnity(hand.aimOrientation)) * Vector3.forward;
            return true;
        }

        /// <summary>Hand h's aim position in room space (Unity axes, metres) and grip, for motion (playbook INPUT-010).</summary>
        public static bool HandTracking(int h, out Vector3 roomPos, out float grip)
        {
            var hand = h == 0 ? lastInput.left : lastInput.right;
            roomPos = VrSpace.ToUnity(hand.aimPosition);
            grip = hand.squeeze;
            return Enabled && lastInput.focused != 0 && hand.poseValid != 0;
        }

        /// <summary>VR (re)started: the first focused frame counts as regaining focus (held controls wait for a release).</summary>
        public static void ResetFocus()
        {
            wasFocused = false;
            suppressed.Clear();
            controlsOn.Clear();
        }

        /// <summary>Hand h's pose is the last tracked one, held through a short dropout (no motion in it).</summary>
        public static bool HandPoseHeld(int h) => (h == 0 ? lastInput.left : lastInput.right).poseHeld != 0;

        /// <summary>Hold a game action's key for this frame (first person grabs, swings).</summary>
        public static void Press(string action)
        {
            if (!Enabled) return;
            var k = KeyBinds.GetBind(action);
            if (k != KeyCode.None) held.Add(k);
        }

        /// <summary>Point the game's mouse at a world point for this frame (a swing attack's direction).</summary>
        public static void AimAt(Vector2 world)
        {
            var pc = PlayerCamera.main;
            var cam = pc != null ? pc.GetComponent<Camera>() : null;
            if (cam == null) return;
            Vector3 sp = cam.WorldToScreenPoint(new Vector3(world.x, world.y, cam.transform.position.z + 10f));
            VirtualMouse = new Vector3(sp.x, sp.y, 0f);
            LaserMode = true;
        }

        public static bool Held(KeyCode k) => Enabled && held.Contains(k);
        public static bool Down(KeyCode k) => Enabled && held.Contains(k) && !prevHeld.Contains(k);
        public static bool Up(KeyCode k) => Enabled && !held.Contains(k) && prevHeld.Contains(k);

        /// <summary>
        /// Exactly once per game frame while VR runs, rendered or not (playbook FAIL-INPUT-025: input must not
        /// depend on rendering, or keys held when the headset stops rendering stay held, and a key edge repeats
        /// on a frame that skipped this call). posesFresh: this frame's poses are known and the UI panels placed,
        /// so the laser can be updated; otherwise it keeps last frame's pointer.
        /// The game consumes the result in the next frame's Update (one frame of input latency).
        /// </summary>
        public static void Update(XrNative.Input input, VrUi ui, Vector3 focus, Vector3 seatedOrigin, float unitsPerMetre, bool posesFresh = true)
        {
            prevHeld = held;
            held = new HashSet<KeyCode>();
            lastInput = input;
            lastFocus = focus;
            lastSeated = seatedOrigin;
            lastScale = unitsPerMetre;
            if (!Enabled || input.focused == 0)
            {
                MenuStickY = 0f;
                controlsOn.Clear();
                LaserVisible = false;
                LaserMode = false;
                wasFocused = false;
                return;
            }
            Profile = input.profile ?? "";
            bool regained = !wasFocused;
            wasFocused = true;

            var left = input.left;
            var right = input.right;
            Sample("R.trigger", right.trigger, 0.6f, 0.4f);
            Sample("R.grip", right.squeeze, 0.6f, 0.4f);
            Sample("L.trigger", left.trigger, 0.6f, 0.4f);
            Sample("L.grip", left.squeeze, 0.6f, 0.4f);
            Sample("R.stickUp", right.stickY, 0.7f, 0.5f);
            Sample("R.stickDown", -right.stickY, 0.7f, 0.5f);
            Sample("L.stickRight", left.stickX, 0.5f, 0.35f);
            Sample("L.stickLeft", -left.stickX, 0.5f, 0.35f);
            Sample("L.stickUp", left.stickY, 0.6f, 0.45f);
            Sample("L.stickDown", -left.stickY, 0.6f, 0.45f);
            Set("R.stickClick", right.stickClick != 0);
            Set("L.stickClick", left.stickClick != 0);
            Set("A", (input.buttons & 1) != 0);
            Set("B", (input.buttons & 2) != 0);
            Set("X", (input.buttons & 4) != 0);
            Set("Y", (input.buttons & 8) != 0);
            Set("Menu", (input.buttons & 16) != 0);
            // First-person stick directions (used by FirstPersonSticks), sampled here so focus regain sees them.
            // Analog walking (FP 8a): a light push already moves (and walks); see FirstPersonSticks.
            bool analog = Plugin.FpAnalogWalk.Value;
            Sample("FP.forward", left.stickY, analog ? 0.25f : 0.5f, analog ? 0.15f : 0.35f);
            Sample("FP.back", -left.stickY, analog ? 0.25f : 0.5f, analog ? 0.15f : 0.35f);
            Sample("FP.climb", right.stickY, 0.6f, 0.45f);
            Sample("FP.crouch", -right.stickY, 0.6f, 0.45f);
            // Both grips together are their own control (ragdoll); the single-grip actions pause meanwhile.
            bool both = controlsOn.Contains("R.grip") && controlsOn.Contains("L.grip");
            Set("BothGrips", both);
            // Back in focus (or VR just started) with a control already held: it acts only after a release, so the
            // press that went to the system menu does not reach the game (playbook ch. 09, neutralize on focus loss).
            if (regained)
            {
                suppressed.Clear();
                suppressed.UnionWith(controlsOn);
                if (suppressed.Count > 0) Plugin.Log.LogInfo($"Input: focus regained with [{string.Join(",", suppressed)}] held; ignored until released");
            }
            else if (suppressed.Count > 0) suppressed.IntersectWith(controlsOn);

            bool fp = VrSpace.FirstPerson;
            // The VR settings menu is modal for the sticks: they scroll it (VRMENU_REVIEW M1b), and nobody walks off.
            bool settings = VrSettingsMenu.IsOpen;
            MenuStickY = !settings ? 0f : Mathf.Abs(right.stickY) >= Mathf.Abs(left.stickY) ? right.stickY : left.stickY;
            if (fp && !settings) FirstPersonSticks(left, right);
            else { FirstPerson.TurnAxis = 0f; FirstPerson.Walking = false; }
            foreach (var kv in Plugin.ControlBinds)
            {
                if (!controlsOn.Contains(kv.Key) || suppressed.Contains(kv.Key)) continue;
                if (settings && kv.Key.IndexOf("stick", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (fp && FpStickControls.Contains(kv.Key)) continue;   // the sticks walk, climb and turn in first person
                // First person, outside menus: the grips are hands (VrGrab), not buttons.
                if (fp && !ui.DashboardShown && (kv.Key == "R.grip" || kv.Key == "L.grip" || kv.Key == "BothGrips")) continue;
                if (both && (kv.Key == "R.grip" || kv.Key == "L.grip")) continue;
                string action = kv.Value.Value;
                if (string.IsNullOrEmpty(action) || action == "none") continue;
                var key = KeyBinds.GetBind(action);
                if (key != KeyCode.None) held.Add(key);
            }

            if (posesFresh && ui != null) UpdateLaser(Plugin.LaserHand.Value == "Left" ? left : right, ui, focus, seatedOrigin, unitsPerMetre);
        }

        // First person (FIRSTPERSON_PLAN.md): left stick forward/back walks along the way you face, right stick
        // up/down climbs/crouches, a right-stick flick sideways turns around (the world is a line).
        private static readonly HashSet<string> FpStickControls = new HashSet<string>
            { "L.stickLeft", "L.stickRight", "L.stickUp", "L.stickDown", "R.stickUp", "R.stickDown" };
        private static bool turnWas;

        private static void FirstPersonSticks(XrNative.Hand left, XrNative.Hand right)
        {
            if (Plugin.FpTurning.Value == "Smooth")
            {
                FirstPerson.TurnAxis = Mathf.Abs(right.stickX) > 0.2f ? right.stickX : 0f;
                turnWas = false;
            }
            else
            {
                FirstPerson.TurnAxis = 0f;
                Sample("FP.turn", Mathf.Abs(right.stickX), 0.7f, 0.3f);
                bool turn = controlsOn.Contains("FP.turn");
                if (turn && !turnWas) FirstPerson.RequestTurn();
                turnWas = turn;
            }
            // Walk or run (FP 8a): part way = walk, all the way = run, with a little hysteresis so the speed does not
            // flicker at the boundary. No push (keyboard) leaves the game's own speed.
            float push = Mathf.Abs(left.stickY);
            if (push < 0.15f || push >= 0.82f) FirstPerson.Walking = false;
            else if (push <= 0.72f) FirstPerson.Walking = true;
            bool facingRight = FirstPerson.FacingRightNow;
            void Key(string control, string action)
            {
                if (!controlsOn.Contains(control) || suppressed.Contains(control)) return;
                var k = KeyBinds.GetBind(action);
                if (k != KeyCode.None) held.Add(k);
            }
            Key("FP.forward", facingRight ? "right" : "left");
            Key("FP.back", facingRight ? "left" : "right");
            Key("FP.climb", "up");
            Key("FP.crouch", "down");
        }

        private static void Sample(string control, float value, float on, float off)
        {
            if (value >= on) controlsOn.Add(control);
            else if (value <= off) controlsOn.Remove(control);
        }

        private static void Set(string control, bool on)
        {
            if (on) controlsOn.Add(control); else controlsOn.Remove(control);
        }

        private static void UpdateLaser(XrNative.Hand hand, VrUi ui, Vector3 focus, Vector3 seatedOrigin, float s)
        {
            LaserVisible = hand.poseValid != 0;
            if (!LaserVisible) { LaserMode = false; return; }

            s = VrSpace.Scale;
            Vector3 origin = VrSpace.Point(VrSpace.ToUnity(hand.aimPosition));
            Vector3 dir = VrSpace.Rot(VrSpace.ToUnity(hand.aimOrientation)) * Vector3.forward;
            LaserOrigin = origin;

            // Who drives the mouse: whichever moved last (real mouse vs controller aim/trigger).
            float now = Time.unscaledTime;
            // Only pointer-related activity claims the mouse: walking with the stick while aiming with the
            // real mouse must not hand the cursor to the laser.
            bool pointerActivity = Vector3.Angle(dir, lastAimDir) > 0.4f
                || controlsOn.Contains("R.trigger") || controlsOn.Contains("R.grip");
            if (pointerActivity) lastControllerActivity = now;
            lastAimDir = dir;
            Vector3 real = InputPatches.RealMousePosition();
            // The laser's own pointer messages (ImguiCapture) move Unity's mouse too: that is not the player's mouse.
            if ((real - lastRealMouse).sqrMagnitude > 4f && !ImguiCapture.IsOwnPosition(real)) lastMouseActivity = now;
            lastRealMouse = real;
            // One hit record (HUD-001): the active surface decides.
            Vector2 screen;
            Vector3 hitPoint;
            OnModWindow = false;
            if (VrSpace.FirstPerson && !ui.DashboardShown)
            {
                if (Plugin.FpAimWith.Value == "Head")
                {
                    origin = VrSpace.HeadPosition;
                    dir = VrSpace.HeadForward;
                    LaserOrigin = origin;
                    lastControllerActivity = Time.unscaledTime;   // the gaze owns the mouse
                }
                // A mod's OnGUI window (ImguiCapture) shows on the HUD panel in front of you: pointing at it uses it.
                if (ImguiCapture.Running && ui.RaycastHud(new Ray(origin, dir), out screen, out hitPoint) && ImguiCapture.Covers(screen))
                {
                    OnModWindow = true;
                    LaserHit = true;
                }
                else LaserHit = WorldAim(origin, dir, out screen, out hitPoint);
            }
            else if (VrSpace.FirstPerson && MinigameBase.main != null && MinigameBase.main.currentMinigame && Plugin.FpMinigameTouch.Value
                     && ui.TouchDashboard(origin, out screen, out hitPoint))
            {
                LaserHit = true;   // the hand touches the minigame panel
                lastControllerActivity = Time.unscaledTime;
            }
            else LaserHit = ui.RaycastActiveSurface(new Ray(origin, dir), out screen, out hitPoint);
            LaserEnd = LaserHit ? hitPoint : origin + dir * (3f * s);
            if (LaserHit)
            {
                VirtualMouse = new Vector3(screen.x, screen.y, 0f);
                everHit = true;
            }
            // The laser owns the mouse only once it has a real position to give (else the game would aim at 0,0).
            LaserMode = everHit && lastControllerActivity >= lastMouseActivity;
        }

        /// <summary>
        /// First-person aim: the game only knows a 2D point under the mouse. March the hand's ray through the
        /// corridor and stop at the first thing the game could act on: a block, an item or creature (the game's
        /// own Item/Body/Limb layers, not the player's own body), or a side wall. That point becomes the mouse.
        /// Range is the game's own interaction range (Body.interactionRange).
        /// </summary>
        private static bool WorldAim(Vector3 origin, Vector3 dir, out Vector2 screen, out Vector3 hit)
        {
            screen = Vector2.zero;
            hit = origin;
            var world = WorldGeneration.world;
            var pc = PlayerCamera.main;
            var cam = pc != null ? pc.GetComponent<Camera>() : null;
            if (world == null || cam == null || world.generatingWorld) return false;
            var body = pc.body;
            int mask = LayerMask.GetMask("Item", "Body", "Limb");
            float zf = VoxelTerrain.FrontZ - 0.05f, zb = VoxelTerrain.BackZ + 0.05f;
            // Coarse steps (a third of a block), then the hit is refined back over the last step (perf, 2026-10-06:
            // 100 physics probes a frame at 0.1).
            const float step = 0.33f, fine = 0.08f;
            float max = Body.interactionRange;
            hit = origin + dir * max;
            HitBlock = false;
            Vector3 prev = origin;
            bool refining = false, found = false;
            float dEnd = max;
            for (float d = step; d <= dEnd + 1e-4f; d += refining ? fine : step)
            {
                var p = origin + dir * d;
                bool stop = p.z < zf || p.z > zb || world.GetBlock((Vector2)p) != 0;
                if (!stop) { var cc = Physics2D.OverlapPoint(p, mask); stop = cc != null && !IsOwn(cc, body); }
                if (stop && !refining && d > step)
                {
                    // Step back and walk the last stretch finely.
                    refining = true;
                    dEnd = d;
                    d -= step;
                    prev = origin + dir * d;
                    continue;
                }
                if (!stop) { prev = p; continue; }
                found = true;
                if (p.z < zf || p.z > zb) { hit = p; break; }
                if (world.GetBlock((Vector2)p) != 0)
                {
                    // The face the ray came in through: x or y changed block since the previous step.
                    hit = p;
                    HitBlock = true;
                    var a = world.WorldToBlockPos(prev);
                    HitBlockPos = world.WorldToBlockPos(p);
                    HitNormal = a.x != HitBlockPos.x ? new Vector3(-Mathf.Sign(dir.x), 0f, 0f)
                              : a.y != HitBlockPos.y ? new Vector3(0f, -Mathf.Sign(dir.y), 0f)
                              : new Vector3(-Mathf.Sign(dir.x), 0f, 0f);
                    break;
                }
                prev = p;
                hit = p;   // an item, creature or limb (the probe above)
                break;
            }
            if (refining && !found) hit = origin + dir * dEnd;
            Vector3 sp = cam.WorldToScreenPoint(new Vector3(hit.x, hit.y, cam.transform.position.z + 10f));
            screen = new Vector2(sp.x, sp.y);
            return true;
        }

        /// <summary>First person: the last aim ray stopped in a block (HitBlockPos), entering through HitNormal.</summary>
        public static bool HitBlock { get; private set; }
        public static Vector2Int HitBlockPos { get; private set; }
        public static Vector3 HitNormal { get; private set; }

        private static bool IsOwn(Collider2D c, Body body)
        {
            if (body == null) return false;
            if (c.TryGetComponent<Limb>(out var limb)) return limb.body == body;
            var b = c.GetComponentInParent<Body>();
            return b == body;
        }
    }

    /// <summary>Harmony postfixes on UnityEngine.Input that merge VrInput into what the game reads.</summary>
    [HarmonyPatch]
    internal static class InputPatches
    {
        [System.ThreadStatic] private static bool bypass;

        public static Vector3 RealMousePosition()
        {
            bypass = true;
            try { return Input.mousePosition; }
            finally { bypass = false; }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(Input), nameof(Input.mousePosition), MethodType.Getter)]
        private static void MousePosition(ref Vector3 __result)
        {
            if (!bypass && VrInput.Enabled && VrInput.LaserMode) __result = VrInput.VirtualMouse;
        }

        [HarmonyPostfix, HarmonyPatch(typeof(Input), nameof(Input.GetKey), typeof(KeyCode))]
        private static void GetKey(KeyCode key, ref bool __result) { if (!__result) __result = VrInput.Held(key); }

        [HarmonyPostfix, HarmonyPatch(typeof(Input), nameof(Input.GetKeyDown), typeof(KeyCode))]
        private static void GetKeyDown(KeyCode key, ref bool __result) { if (!__result) __result = VrInput.Down(key); }

        [HarmonyPostfix, HarmonyPatch(typeof(Input), nameof(Input.GetKeyUp), typeof(KeyCode))]
        private static void GetKeyUp(KeyCode key, ref bool __result) { if (!__result) __result = VrInput.Up(key); }

        // UI (EventSystem's BaseInput) and a few game paths read mouse buttons by index.
        [HarmonyPostfix, HarmonyPatch(typeof(Input), nameof(Input.GetMouseButton), typeof(int))]
        private static void GetMouseButton(int button, ref bool __result) { if (!__result) __result = VrInput.Held(MouseKey(button)); }

        [HarmonyPostfix, HarmonyPatch(typeof(Input), nameof(Input.GetMouseButtonDown), typeof(int))]
        private static void GetMouseButtonDown(int button, ref bool __result) { if (!__result) __result = VrInput.Down(MouseKey(button)); }

        [HarmonyPostfix, HarmonyPatch(typeof(Input), nameof(Input.GetMouseButtonUp), typeof(int))]
        private static void GetMouseButtonUp(int button, ref bool __result) { if (!__result) __result = VrInput.Up(MouseKey(button)); }

        private static KeyCode MouseKey(int button) => button == 0 ? KeyCode.Mouse0 : button == 1 ? KeyCode.Mouse1 : KeyCode.Mouse2;
    }
}
