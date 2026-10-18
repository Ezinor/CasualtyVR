using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Tilemaps;

namespace CasualtyVR
{
    /// <summary>
    /// First-person view (docs/FIRSTPERSON_PLAN.md, FP-0). The player's head is placed at the character's head,
    /// inside the voxel slab, looking along the tunnels: the world is a corridor from FrontZ to BackZ.
    ///
    /// * Position follows the head limb (smoothed, FirstPerson.Smoothing); rotation is always the player's own
    ///   (playbook CAM-001). It stays in first person through ragdoll and knockdowns (user, 2026-10-05: "this
    ///   is an intense game so it's ok if the camera is intense"); only menus, loading and the end screen go
    ///   back to the diorama/theatre view.
    /// * Facing: the player's forward maps to the character's facing along world x; the right stick flicks
    ///   it around (180Â°).
    /// * Scale: the character's standing eye height (measured) maps to 1.6 m (CAM-018), unless set.
    /// * Sides (FirstPerson.Sides): CaveWalls = the game's background walls on the back side (PopupDepth puts
    ///   them at BackZ) and a darkened, mirrored copy on the front side; Void = black.
    /// * In the eyes: the character's own body (incl. held items), the game's line-of-sight mask and the
    ///   diorama frame are hidden.
    /// </summary>
    internal sealed class FirstPerson
    {
        public const float StandingEyeMetres = 1.6f;

        /// <summary>First person is in effect this frame (wanted, in a level, no full-screen UI).</summary>
        public static bool Active { get; private set; }
        public static bool VoidSides => Active && Plugin.FpSides.Value == "Void";
        /// <summary>Renderers of the player's own character, hidden in the eyes while Active.</summary>
        public static readonly HashSet<Renderer> OwnBody = new HashSet<Renderer>();

        // Point lights at least this big that contain the player light the view evenly (the lifepod's ceiling light);
        // smaller ones (glow plants) flooded the whole view with their colour that way (2026-10-05).
        private const float AmbientMinRadius = 15f;

        public static float HalfWidth => Mathf.Max(0.5f, Plugin.FpCorridor.Value * 0.5f);
        public static float EyeZ => DbgEyeZ;   // the play plane, centred in the corridor
        private static readonly float DbgEyeZ = float.TryParse(ArgAfter("--cvr-dbg-eyez"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float ez) ? ez : 0f;

        private static bool turnRequested;
        public static void RequestTurn() => turnRequested = true;
        /// <summary>Smooth turning (FirstPerson.Turning = Smooth): right stick x, set by VrInput each frame.</summary>
        public static float TurnAxis;

        private bool facingRight = true;
        private float yaw = 90f;   // tracking -> world yaw: 90 = the player's forward is world +x
        private readonly Billboards billboards = new Billboards();
        private readonly VrArms arms = new VrArms();
        private readonly BodyView bodyView = new BodyView();
        private readonly ShaderFog shaderFog = new ShaderFog();
        private readonly List<(GameObject go, Material mat)> fogSheets = new List<(GameObject, Material)>();
        private static readonly float[] FogDistances = { 2f, 4f, 6.5f, 9.5f, 13f, 17.5f, 23f, 30f };
        private static float blackout, vignette;
        private static Material overlayMat;
        private static Mesh overlayQuad;
        private static Texture2D vignetteTex;
        private static readonly bool DbgOverlay = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "--cvr-dbg-overlay") >= 0;
        private Vector3 eye;
        private bool hasEye;
        private float measuredEyeUnits = -1f;
        private float eyeAboveNeck = -1f, standingFor;
        private Body lastBody;
        private int lastRefresh = -1000;
        private readonly List<Renderer> extraHidden = new List<Renderer>();   // line-of-sight mask
        private readonly List<Renderer> hidden = new List<Renderer>();

        // 2D lights face the eye (risk 1 in FIRSTPERSON_PLAN.md). URP's 2D renderer draws each light's mesh, which
        // lies in the light's local xy plane, into a screen-space light texture that lit sprites sample at their
        // own screen position. From inside the play plane every light is seen edge-on and lights almost nothing
        // (FP-0 capture: the lit half of the view was black). Turned to face the eye while it renders, a point
        // light becomes a disc around its screen position: roughly a sphere of light. Global lights are left alone.
        private readonly List<Light2D> lights = new List<Light2D>();
        private readonly List<(Transform t, Quaternion rot, Vector3 pos, Light2D light, bool shadows)> turnedLights = new List<(Transform, Quaternion, Vector3, Light2D, bool)>();
        private int lastLightScan = -1000;
        private readonly List<Light2D> physicsLights = new List<Light2D>();
        // Copies of the lights on physics objects (which cannot be turned): a light of our own per original, with
        // its serialized settings copied, lit only while an eye renders (intensity 0 otherwise, so the monitor
        // view is unchanged) and turned like any other light; the original is dark in the eyes.
        private readonly Dictionary<Light2D, Light2D> lightProxies = new Dictionary<Light2D, Light2D>();
        private GameObject proxyRoot;
        private readonly List<(Light2D l, float intensity)> dimmed = new List<(Light2D, float)>();
        private int lastLightLog;
        private bool lightsLogged;
        private static readonly int DbgLightMode = int.TryParse(ArgAfter("--cvr-dbg-lightmode"), out int m) ? m : 1;
        private static string ArgAfter(string f) { var a = System.Environment.GetCommandLineArgs(); int i = System.Array.IndexOf(a, f); return i >= 0 && i + 1 < a.Length ? a[i + 1] : null; }

        // Front cave wall: mirrored copies of the game's per-chunk background sprites, on the VR-only layer.
        private readonly GameObject wallRoot;
        private readonly Dictionary<SpriteRenderer, SpriteRenderer> frontWall = new Dictionary<SpriteRenderer, SpriteRenderer>();
        private readonly List<SpriteRenderer> staleWall = new List<SpriteRenderer>();
        // ...and copies of background tilemaps (the lifepod's and buildings' interiors), unmirrored.
        private readonly Dictionary<Tilemap, GameObject> tileMirrors = new Dictionary<Tilemap, GameObject>();
        private int lastTileScan = -1000;
        private int lastWallRefresh = -1000;

        // Back wall: sprites of the background bands that carry a Collider2D (machines and buttons set into the
        // walls: the lifepod's pump, heater and buttons) can never be moved (PopupDepth.SkipColliders), so in
        // first person they would stand edge-on in the middle of the corridor. Copies are drawn on the back wall
        // instead and the originals hidden in the eyes; clicks still reach them (the game is 2D).
        private readonly GameObject backRoot;
        private readonly Dictionary<SpriteRenderer, SpriteRenderer> backProxies = new Dictionary<SpriteRenderer, SpriteRenderer>();
        private int lastBackScan = -1000;
        private readonly HashSet<string> loggedBig = new HashSet<string>();
        public static bool IsBackProxied(SpriteRenderer sr) => instance != null && instance.backProxies.ContainsKey(sr);
        private static FirstPerson instance;

        public FirstPerson()
        {
            wallRoot = new GameObject("CasualtyVR.FrontWall") { hideFlags = HideFlags.DontSave };
            Object.DontDestroyOnLoad(wallRoot);
            instance = this;
            backRoot = new GameObject("CasualtyVR.BackWall") { hideFlags = HideFlags.DontSave };
            Object.DontDestroyOnLoad(backRoot);
            RenderPipelineManager.beginCameraRendering += OnBegin;
            RenderPipelineManager.endCameraRendering += OnEnd;
        }

        public float Scale
        {
            get
            {
                if (Plugin.FpUnitsPerMetre.Value > 0f) return Plugin.FpUnitsPerMetre.Value;
                return (measuredEyeUnits > 0f ? measuredEyeUnits : 2.4f) / StandingEyeMetres;
            }
        }

        /// <summary>
        /// Once per frame before the views are applied. Returns whether first person is in effect; when it is,
        /// VrSpace has been set up for it.
        /// </summary>
        public bool Update(bool fullScreenUi, Vector3 seatedOrigin)
        {
            var body = PlayerCamera.main != null ? PlayerCamera.main.body : null;
            var world = WorldGeneration.world;
            bool want = Plugin.FpWanted && body != null && body.limbs != null && body.limbs.Length > 0 && body.limbs[0] != null
                        && !fullScreenUi && world != null && !world.generatingWorld
                        // Comfort option: the diorama while the body is down (ragdoll, knocked down), off by default.
                        && !(Plugin.FpRagdollView.Value == "Diorama" && !body.standing);
            if (want != Active)
            {
                Active = want;
                PopupDepth.Invalidate();   // FP bands differ (PopupDepth.DepthFor)
                hasEye = false;
                if (want) { facingRight = body.isRight; yaw = facingRight ? 90f : -90f; }
                Plugin.Log.LogInfo($"First person: {(want ? "on" : "off")} (running speed {(want ? Mathf.Clamp(Plugin.FpMoveSpeed.Value, 0.3f, 1f) : 1f) * 100f:F0}% of the game's)");
            }
            UpdateFrontWall();
            if (backRoot.activeSelf != Active) backRoot.SetActive(Active);
            billboards.SetActive(Active && Plugin.FpBillboards.Value);
            if (!Active)
            {
                turnRequested = false;
                ShowFog(false);
                shaderFog.Set(false, Color.black, 0f, 1f, 0f);
                blackout = vignette = 0f;
                return false;
            }
            UpdateBackProxies();

            if (body != lastBody) { lastBody = body; measuredEyeUnits = -1f; eyeAboveNeck = -1f; standingFor = 0f; lastRefresh = -1000; }
            if (Time.frameCount - lastRefresh >= 60) Refresh(body);
            Measure(body);
            if (Plugin.Verbose && !rigDumped && Time.timeSinceLevelLoad > 18f) DumpRig(body);

            // Turning: a flick turns around (Snap); or the right stick turns smoothly (Smooth). Walking forward follows
            // whichever way along the tunnel the turned view faces.
            if (turnRequested) { yaw += 180f; turnRequested = false; }
            if (Plugin.FpTurning.Value == "Smooth") yaw += TurnAxis * Plugin.FpTurnSpeed.Value * Time.unscaledDeltaTime;
            yaw = Mathf.Repeat(yaw + 180f, 360f) - 180f;
            float s = Mathf.Sin(yaw * Mathf.Deg2Rad);
            if (Mathf.Abs(s) > 0.2f) facingRight = s > 0f;
            FacingRightNow = facingRight;
            // The character faces the way you face (else you would see its tail ahead and its feet behind you);
            // not mid-attack, so an attack aimed behind still turns it as the game does.
            if (body.standing && body.conscious && body.attackCooldown <= 0f && body.isRight != facingRight) body.SwitchDir();

            // The game turns the head and torso towards where the character looks, and flips the whole body when
            // it looks behind: the head limb swings sideways. Standing, the view keeps to the body's centre line
            // (only the head's height is used); ragdolled, it rides the head (user, 2026-10-05: pointing the hand
            // moved the camera).
            var head = body.limbs[0].transform.position;
            float x = body.standing ? body.transform.position.x : head.x;
            // Standing, the eye height is a fixed height above the neck (the head limb's joint, which the game turns
            // the head about) rather than the head limb's own: the game turns the head towards where the character
            // looks, which lowered the view by 0.4 blocks when you looked down (2026-10-06, with "running from a
            // stop, my vision was obscured"). The neck still follows the spine when the character crouches, ducks
            // under a low ceiling or leans. 2026-10-07: was a height above the hips, which stayed up when the game
            // bent the spine under low ceilings. Climbing and ragdoll use the head as before, and the eye never goes
            // into a solid block.
            var headLimb = body.limbs[0];
            var neck = headLimb.joint != null ? headLimb.transform.TransformPoint(headLimb.joint.anchor) : head;
            if (body.standing && body.grounded && !body.crouching && body.crouchAmount < 0.05f && Mathf.Abs(body.accelRot) < 1f && standingFor > 1f)
            {
                // Highest when the head is upright (it turns about the neck either way).
                float h = head.y - neck.y;
                if (h > 0.05f && h < 3f && h > eyeAboveNeck)
                {
                    if (eyeAboveNeck < 0f) Plugin.Log.LogInfo($"First person: eye {h:F2} units above the neck");
                    eyeAboveNeck = h;
                }
            }
            standingFor = body.standing ? standingFor + Time.deltaTime : 0f;
            float y = head.y;
            if (body.standing && eyeAboveNeck > 0f && body.currentClimbable == null)
            {
                y = Mathf.Clamp(neck.y + eyeAboveNeck, head.y - 0.6f, head.y + 0.6f);
                if (y > head.y && world.GetBlock(new Vector2(x, y + 0.15f)) != 0) y = head.y;
            }
            var target = new Vector3(x, y, EyeZ);
            // Smoothing 0 = locked to the head limb; 1 = heavy damping.
            float rate = Mathf.Lerp(60f, 4f, Mathf.Clamp01(Plugin.FpSmoothing.Value));
            if (!hasEye || (eye - target).sqrMagnitude > 25f) { eye = target; hasEye = true; }   // spawn, teleport
            else eye = Vector3.Lerp(eye, target, 1f - Mathf.Exp(-rate * Time.unscaledDeltaTime));
            if (Plugin.Verbose) RecordPath(target, eye, Scale, body.actualMaxSpeed);
            VrSpace.SetFirstPerson(eye, seatedOrigin, yaw, Scale);
            UpdateComfort(body);
            return true;
        }

        // ---- eye path statistics (verbose; BUILD_PLAN Phase 0, IP 9 / FP 8d) -----------------------------------
        // Per frame, in metres: the target (the head limb, moved by 50 Hz physics) and the smoothed eye. Judder shows
        // as uneven horizontal steps while running (many zero steps between physics ticks); bumps as vertical travel
        // far larger than the net height change.
        private static Vector3 pathLastTarget, pathLastEye;
        private static bool pathHas;
        private static int pathMoving, pathTargetStalls;
        private static double pathEyeSum, pathEyeSq, pathTargetSum, pathTargetSq;
        private static float pathMaxDy, pathVertTravel, pathStartY, pathEndY;

        private static void RecordPath(Vector3 target, Vector3 eye, float scale, float maxSpeedUnits)
        {
            if (!pathHas) { pathHas = true; pathLastTarget = target; pathLastEye = eye; pathStartY = eye.y / scale; return; }
            float s = 1f / Mathf.Max(scale, 1e-3f), dt = Mathf.Max(Time.unscaledDeltaTime, 1e-4f);
            // Velocities (m/s), so uneven frame times are not counted as uneven motion; only frames at steady top speed
            // (target over 80 % of the character's cap), so speeding up and slowing down are not counted either.
            float tv = Mathf.Abs(target.x - pathLastTarget.x) * s / dt, ev = Mathf.Abs(eye.x - pathLastEye.x) * s / dt;
            float edy = (eye.y - pathLastEye.y) * s;
            if (tv > 0.8f * maxSpeedUnits * s || (ev > 0.8f * maxSpeedUnits * s && tv < 1e-3f))
            {
                pathMoving++;
                pathEyeSum += ev; pathEyeSq += ev * ev;
                pathTargetSum += tv; pathTargetSq += tv * tv;
                if (tv < 1e-3f) pathTargetStalls++;
            }
            pathMaxDy = Mathf.Max(pathMaxDy, Mathf.Abs(edy));
            pathVertTravel += Mathf.Abs(edy);
            pathEndY = eye.y * s;
            pathLastTarget = target;
            pathLastEye = eye;
        }

        /// <summary>The window's eye path summary, then reset (null when first person has not run).</summary>
        public static string PathStats()
        {
            if (!pathHas) return null;
            string Cv(double sum, double sq, int n)
            {
                if (n < 2 || sum <= 0) return "-";
                double mean = sum / n, sd = System.Math.Sqrt(System.Math.Max(0, sq / n - mean * mean));
                return (sd / mean).ToString("F2");
            }
            string r = $"steady-speed {pathMoving} frames, speed CV eye {Cv(pathEyeSum, pathEyeSq, pathMoving)} target {Cv(pathTargetSum, pathTargetSq, pathMoving)}, " +
                       $"target stalled {pathTargetStalls}/{pathMoving}, vertical max step {pathMaxDy * 100f:F1} cm, travel {pathVertTravel * 100f:F0} cm, net {(pathEndY - pathStartY) * 100f:F0} cm";
            pathMoving = pathTargetStalls = 0;
            pathEyeSum = pathEyeSq = pathTargetSum = pathTargetSq = 0;
            pathMaxDy = pathVertTravel = 0f;
            pathStartY = pathEndY;
            return r;
        }

        /// <summary>
        /// The character looks where the player looks (outside menus): the game's own look override drives the
        /// head and torso turn and "peeking behind", while the mouse (hand) keeps aiming attacks and clicks.
        /// User, 2026-10-05: "the hand/mouse controls where the character's head looks ... pinned to the head/eye".
        /// </summary>
        private bool menuOpenForInput;

        public void AfterHead(bool menuOpen)
        {
            menuOpenForInput = menuOpen;
            var body = PlayerCamera.main != null ? PlayerCamera.main.body : null;
            if (Plugin.FpBillboards.Value) billboards.Update(VrSpace.HeadPosition, body);
            UpdateFog();
            PinLook(body, menuOpen);
        }

        /// <summary>After the controllers are read (VrInput.Update): the character's arms follow them (FP-2).</summary>
        public void AfterInput()
        {
            var body = PlayerCamera.main != null ? PlayerCamera.main.body : null;
            bool on = Active && Plugin.FpArms.Value && body != null;
            arms.SetActive(on);
            if (on) arms.Update(body);
            bool bodyOn = Active && Plugin.FpBody.Value && body != null;
            bodyView.SetActive(bodyOn);
            if (bodyOn) bodyView.Update(body);
            if (Active) VrGrab.Update(body, menuOpenForInput);
        }

        private void PinLook(Body body, bool menuOpen)
        {
            if (body == null || menuOpen || !Plugin.FpLookWithHead.Value) return;
            Vector3 f = VrSpace.HeadForward;
            Vector2 dir = new Vector2(f.x, f.y);
            // Looking at a side wall: straight ahead along the corridor, at the same pitch.
            if (dir.sqrMagnitude < 0.04f) dir = new Vector2(facingRight ? 1f : -1f, f.y);
            Vector2 from = VrSpace.HeadPosition;
            body.overrideLookPos = from + dir.normalized * 8f;
            body.overrideLookTime = 0.25f;
        }

        /// <summary>
        /// Running speed in first person (user, 2026-10-06: "player movement needs to be a little slower in 1st
        /// person"): the game's top speed for the player, times FirstPerson.MoveSpeed. Acceleration is the game's.
        /// </summary>
        [HarmonyLib.HarmonyPatch(typeof(Body), nameof(Body.actualMaxSpeed), HarmonyLib.MethodType.Getter)]
        private static class MoveSpeedPatch
        {
            private static void Postfix(Body __instance, ref float __result)
            {
                if (Active && PlayerCamera.main != null && __instance == PlayerCamera.main.body)
                    __result *= Mathf.Clamp(Plugin.FpMoveSpeed.Value, 0.3f, 1f);
            }
        }

        private static Transform PlayerRoot => PlayerCamera.main != null && PlayerCamera.main.body != null ? PlayerCamera.main.body.transform : null;

        public bool FacingRight => facingRight;

        /// <summary>
        /// First person: the yaw that turns an object standing at pivot so its art faces the player's head (user,
        /// 2026-10-05: "objects like the plants should be facing me directly, not sideways"). Identity when off.
        /// </summary>
        public static Quaternion FaceYaw(Vector3 pivot)
        {
            if (!Active || !Plugin.FpFaceObjects.Value) return Quaternion.identity;
            Vector3 d = VrSpace.HeadPosition - pivot;
            d.y = 0f;
            return d.sqrMagnitude < 1e-4f ? Quaternion.identity : Quaternion.FromToRotation(Vector3.back, d.normalized);
        }
        public static int BillboardGroups => instance != null ? instance.billboards.GroupCount : 0;
        public static bool FacingRightNow { get; private set; } = true;
        /// <summary>Verbose perf: milliseconds spent in first-person code this frame (update) and in the eye-render hooks.</summary>
        public static readonly System.Diagnostics.Stopwatch UpdateTime = new System.Diagnostics.Stopwatch(), RenderTime = new System.Diagnostics.Stopwatch();

        // ---- fog and darkness by distance (FP-1) --------------------------------------------------------------
        // The diorama's mist is one flat veil in front of the game window; in first person it would be a pane through
        // the tunnel. Instead a few see-through sheets stand across the view at increasing distances, each facing
        // the head: they are depth-tested against the blocks, so a wall nearer than a sheet hides it and the
        // haze grows with distance. They draw right after the blocks (sorting order), so characters and items are
        // not fogged. Used for Foggy layers (mist colour) and for the Void sides (black, distance darkness).
        private void UpdateFog()
        {
            var world = WorldGeneration.world;
            float total = 0f;
            Color col = Color.black;
            if (world != null && world.fogAmount > 0f && Plugin.FogMode.Value != "Off" && !VrRig.DbgNoFog)
            {
                total = Mathf.Clamp01(world.fogAmount * Plugin.FogMist.Value);
                col = Plugin.FogMistColour;
            }
            else if (VoidSides) total = Mathf.Clamp01(Plugin.FpVoidDarkness.Value);
            if (total <= 0.001f || Plugin.GreenscreenOn) { ShowFog(false); shaderFog.Set(false, col, 0f, 1f, 0f); return; }
            // Smooth per-pixel fog through the shader bundle (ShaderFog); the stepped sheets only without it.
            if (ShaderFog.Available)
            {
                ShowFog(false);
                float s = PopupDepth.Scale;
                float end = VoidSides && !(world != null && world.fogAmount > 0f) ? Plugin.FpViewDistance.Value : Mathf.Min(30f, Plugin.FpViewDistance.Value);
                shaderFog.Set(true, col, 2f * s, end * s, total);
                return;
            }
            if (fogSheets.Count == 0) BuildFog();
            ShowFog(true);
            float a = 1f - Mathf.Pow(1f - total, 1f / FogDistances.Length);
            Vector3 head = VrSpace.HeadPosition, fwd = VrSpace.HeadForward;
            for (int i = 0; i < fogSheets.Count; i++)
            {
                float d = FogDistances[i] * PopupDepth.Scale;
                var (go, mat) = fogSheets[i];
                go.transform.SetPositionAndRotation(head + fwd * d, Quaternion.LookRotation(fwd, Vector3.up));
                go.transform.localScale = new Vector3(d * 6f, d * 6f, 1f);
                mat.color = new Color(col.r, col.g, col.b, a);
            }
        }

        private void BuildFog()
        {
            var mesh = new Mesh { name = "CasualtyVR.FogSheet" };
            mesh.vertices = new[] { new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f), new Vector3(0.5f, 0.5f), new Vector3(-0.5f, 0.5f) };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3, 0, 2, 1, 0, 3, 2 };
            for (int i = 0; i < FogDistances.Length; i++)
            {
                var go = new GameObject("CasualtyVR.FogSheet" + i) { layer = VoxelTerrain.Layer, hideFlags = HideFlags.DontSave };
                Object.DontDestroyOnLoad(go);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                var mat = new Material(Shader.Find("Sprites/Default")) { name = "CasualtyVR.FogSheet" };
                mr.sharedMaterial = mat;
                mr.sortingLayerName = "Default";
                mr.sortingOrder = short.MinValue + 2;   // after the blocks' depth and colour passes, before every sprite
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                fogSheets.Add((go, mat));
            }
        }

        private void ShowFog(bool on)
        {
            foreach (var (go, _) in fogSheets) if (go != null && go.activeSelf != on) go.SetActive(on);
        }

        // ---- comfort: blackout and vignette (drawn over each eye after post, VrUi.DrawInto) ---------------------
        private void UpdateComfort(Body body)
        {
            // Unconscious or asleep: the view goes dark, as it does flat (the game's own darkening is screen UI,
            // which the headset's see-through HUD cannot show).
            float target = 0f;
            if (Plugin.FpBlackout.Value)
                target = body.sleeping ? 1f : Mathf.Clamp01((40f - body.consciousness) / 20f);
            if (DbgOverlay) target = 0.5f;
            blackout = Mathf.MoveTowards(blackout, target, Time.unscaledDeltaTime * 1.5f);
            // Vignette on fast vertical motion (falls, jumps, bounce pads), off by default.
            float vy = body.rb != null ? Mathf.Abs(body.rb.velocity.y) : 0f;
            float v = DbgOverlay ? 1f : Plugin.FpVignette.Value * Mathf.Clamp01((vy - 4f) / 12f);
            vignette = Mathf.MoveTowards(vignette, v, Time.unscaledDeltaTime * 3f);
        }

        /// <summary>Darkening overlays for an eye (call inside VrUi.DrawInto); returns the number of draws.</summary>
        public static int DrawOverlay(CommandBuffer cmd, Camera eye)
        {
            if (!Active || (blackout <= 0.001f && vignette <= 0.001f)) return 0;
            if (overlayMat == null)
            {
                overlayMat = new Material(Shader.Find("Sprites/Default")) { name = "CasualtyVR.FpOverlay" };
                overlayQuad = new Mesh { name = "CasualtyVR.FpOverlay" };
                overlayQuad.vertices = new[] { new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f), new Vector3(0.5f, 0.5f), new Vector3(-0.5f, 0.5f) };
                overlayQuad.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
                overlayQuad.colors = new[] { Color.white, Color.white, Color.white, Color.white };
                overlayQuad.triangles = new[] { 0, 1, 2, 0, 2, 3, 0, 2, 1, 0, 3, 2 };
                const int n = 64;
                vignetteTex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "CasualtyVR.Vignette" };
                var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float dx = (x + 0.5f) / n - 0.5f, dy = (y + 0.5f) / n - 0.5f;
                        float r = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
                        px[y * n + x] = new Color32(255, 255, 255, (byte)(Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 0.9f, r)) * 255f));
                    }
                vignetteTex.SetPixels32(px);
                vignetteTex.Apply(false, true);
            }
            float s = VrSpace.Scale;
            var m = eye.transform.localToWorldMatrix * Matrix4x4.TRS(new Vector3(0f, 0f, 0.2f * s), Quaternion.identity, new Vector3(1.2f * s, 1.2f * s, 1f));
            int drawn = 0;
            var props = new MaterialPropertyBlock();
            if (vignette > 0.001f)
            {
                props.SetTexture("_MainTex", vignetteTex);
                props.SetColor("_Color", new Color(0f, 0f, 0f, vignette));
                cmd.DrawMesh(overlayQuad, m, overlayMat, 0, 0, props);
                drawn++;
            }
            if (blackout > 0.001f)
            {
                props.SetTexture("_MainTex", Texture2D.whiteTexture);
                props.SetColor("_Color", new Color(0f, 0f, 0f, blackout));
                cmd.DrawMesh(overlayQuad, m, overlayMat, 0, 0, props);
                drawn++;
            }
            return drawn;
        }

        private bool rigDumped;

        // Diagnostic (FP-2): the character's limbs, joints and hand slots.
        private void DumpRig(Body body)
        {
            rigDumped = true;
            Plugin.Log.LogInfo($"Rig: body isRight {body.isRight} handSlot {body.handSlot} scale {body.transform.lossyScale} pos {body.transform.position}");
            for (int i = 0; i < body.limbs.Length; i++)
            {
                var l = body.limbs[i];
                if (l == null) continue;
                var sr = l.GetComponent<SpriteRenderer>();
                var j = l.joint;
                Plugin.Log.LogInfo($"Rig: limb {i} {l.name} parent {l.transform.parent?.name} pos {l.transform.position} rot {l.transform.eulerAngles.z:F0} lossy {l.transform.lossyScale} " +
                    $"joint {(j != null ? $"to {j.connectedBody?.name} anchor {j.anchor} connAnchor {j.connectedAnchor}" : "-")} " +
                    $"sprite {sr?.sprite?.name} pivot {(sr?.sprite != null ? (sr.sprite.pivot / sr.sprite.pixelsPerUnit).ToString() : "-")} size {(sr?.sprite != null ? sr.sprite.bounds.size.ToString() : "-")} order {sr?.sortingOrder} flipX {sr?.flipX} " +
                    $"children {string.Join(",", System.Linq.Enumerable.Select(l.GetComponentsInChildren<SpriteRenderer>(), c => c.name + "/" + c.sortingOrder))}");
            }
            if (body.slots != null)
                for (int i = 0; i < body.slots.Length; i++)
                {
                    var sl = body.slots[i];
                    if (sl == null) continue;
                    Plugin.Log.LogInfo($"Rig: slot {i} hand {sl.isHand} main {sl.isMainHand} limb {sl.limb?.name} parent {sl.transform.parent?.name} local {sl.transform.localPosition} children {sl.transform.childCount}");
                }
        }

        // Standing eye height above the feet, in world units: sets the automatic scale (CAM-018).
        private void Measure(Body body)
        {
            if (measuredEyeUnits > 0f || !body.standing || !body.grounded || body.crouching || body.col == null) return;
            float h = body.limbs[0].transform.position.y - body.col.bounds.min.y;
            if (h < 0.5f || h > 10f) return;
            measuredEyeUnits = h;
            Plugin.Log.LogInfo($"First person: character eye height {h:F2} units (collider {body.col.bounds.size.y:F2} tall) -> {Scale:F2} units/m");
        }

        private void Refresh(Body body)
        {
            lastRefresh = Time.frameCount;
            OwnBody.Clear();
            foreach (var r in body.GetComponentsInChildren<Renderer>(true)) OwnBody.Add(r);
            foreach (var limb in body.limbs)
                if (limb != null) foreach (var r in limb.GetComponentsInChildren<Renderer>(true)) OwnBody.Add(r);
            extraHidden.Clear();
            foreach (var vm in Object.FindObjectsOfType<VisionMask>())
                extraHidden.AddRange(vm.GetComponentsInChildren<Renderer>());
        }

        private void UpdateFrontWall()
        {
            var world = WorldGeneration.world;
            bool on = Active && Plugin.FpSides.Value == "CaveWalls" && !Plugin.GreenscreenOn && world != null && world.backgrounds != null;
            if (wallRoot.activeSelf != on) wallRoot.SetActive(on);
            if (!on) return;
            UpdateTileMirrors();
            staleWall.Clear();
            foreach (var kv in frontWall) if (kv.Key == null) staleWall.Add(kv.Key);
            foreach (var s in staleWall) { if (frontWall[s] != null) Object.Destroy(frontWall[s].gameObject); frontWall.Remove(s); }
            float z = VoxelTerrain.FrontZ - 0.02f;
            // The walls never move: full refresh twice a second; otherwise only near ones are shown (perf, 2026-10-06).
            bool full = Time.frameCount - lastWallRefresh >= 30;
            if (full) lastWallRefresh = Time.frameCount;
            Vector2 eyeXY = eye;
            float near = Plugin.FpViewDistance.Value + 20f;
            if (!full)
            {
                foreach (var kv in frontWall)
                {
                    if (kv.Key == null || kv.Value == null) continue;
                    bool inRange = ((Vector2)kv.Key.transform.position - eyeXY).sqrMagnitude < near * near;
                    bool show = inRange && kv.Key.enabled && kv.Key.gameObject.activeInHierarchy && kv.Key.sprite != null;
                    if (kv.Value.enabled != show) kv.Value.enabled = show;
                }
                return;
            }
            foreach (var bg in world.backgrounds)
            {
                if (bg == null || !bg.TryGetComponent<SpriteRenderer>(out var src)) continue;
                if (!frontWall.TryGetValue(src, out var cp) || cp == null)
                {
                    var go = new GameObject("wall") { layer = VoxelTerrain.Layer, hideFlags = HideFlags.DontSave };
                    go.transform.SetParent(wallRoot.transform, false);
                    cp = go.AddComponent<SpriteRenderer>();
                    cp.shadowCastingMode = ShadowCastingMode.Off;
                    cp.receiveShadows = false;
                    frontWall[src] = cp;
                }
                bool show = src.enabled && src.gameObject.activeInHierarchy && src.sprite != null
                            && ((Vector2)src.transform.position - eyeXY).sqrMagnitude < near * near;
                if (cp.enabled != show) cp.enabled = show;
                if (!show) continue;
                var p = src.transform.position;
                cp.transform.SetPositionAndRotation(new Vector3(p.x, p.y, z), src.transform.rotation);
                cp.transform.localScale = src.transform.lossyScale;
                if (cp.sprite != src.sprite) cp.sprite = src.sprite;
                if (cp.drawMode != src.drawMode) cp.drawMode = src.drawMode;
                if (src.drawMode != SpriteDrawMode.Simple) cp.size = src.size;
                cp.flipX = true;   // seen from the other side
                if (cp.sharedMaterial != src.sharedMaterial) cp.sharedMaterial = src.sharedMaterial;
                var c = src.color;
                cp.color = new Color(c.r * 0.7f, c.g * 0.7f, c.b * 0.7f, c.a);
                cp.sortingLayerID = src.sortingLayerID;
                cp.sortingOrder = src.sortingOrder;
            }
        }

        private void UpdateTileMirrors()
        {
            if (Time.frameCount - lastTileScan >= 120)
            {
                lastTileScan = Time.frameCount;
                var dead = new List<Tilemap>();
                foreach (var kv in tileMirrors) if (kv.Key == null) { dead.Add(kv.Key); if (kv.Value != null) Object.Destroy(kv.Value); }
                foreach (var d in dead) tileMirrors.Remove(d);
                foreach (var tr in Object.FindObjectsOfType<TilemapRenderer>())
                {
                    if (tr.gameObject.layer >= 25 || tr.sortingOrder > -8000) continue;
                    var tm = tr.GetComponent<Tilemap>();
                    if (tm == null || tileMirrors.ContainsKey(tm)) continue;
                    tileMirrors[tm] = MirrorTilemap(tm, tr);
                }
            }
            float z = VoxelTerrain.FrontZ - 0.02f;
            foreach (var kv in tileMirrors)
            {
                if (kv.Key == null || kv.Value == null) continue;
                var t = kv.Key.transform;
                bool show = kv.Key.gameObject.activeInHierarchy;
                if (kv.Value.activeSelf != show) kv.Value.SetActive(show);
                kv.Value.transform.SetPositionAndRotation(new Vector3(t.position.x, t.position.y, z), t.rotation);
                kv.Value.transform.localScale = t.lossyScale;
            }
        }

        private GameObject MirrorTilemap(Tilemap src, TilemapRenderer srcRenderer)
        {
            var gridGo = new GameObject("tiles " + src.name) { layer = VoxelTerrain.Layer, hideFlags = HideFlags.DontSave };
            gridGo.transform.SetParent(wallRoot.transform, false);
            var grid = gridGo.AddComponent<Grid>();
            var sg = src.layoutGrid as Grid;
            if (sg != null) { grid.cellSize = sg.cellSize; grid.cellGap = sg.cellGap; grid.cellLayout = sg.cellLayout; grid.cellSwizzle = sg.cellSwizzle; }
            var go = new GameObject("map") { layer = VoxelTerrain.Layer, hideFlags = HideFlags.DontSave };
            go.transform.SetParent(gridGo.transform, false);
            var dst = go.AddComponent<Tilemap>();
            var dr = go.AddComponent<TilemapRenderer>();
            dst.tileAnchor = src.tileAnchor;
            dst.orientation = src.orientation;
            var b = src.cellBounds;
            dst.SetTilesBlock(b, src.GetTilesBlock(b));
            foreach (var pos in b.allPositionsWithin)
            {
                if (!src.HasTile(pos)) continue;
                dst.SetTileFlags(pos, TileFlags.None);
                dst.SetTransformMatrix(pos, src.GetTransformMatrix(pos));
                dst.SetColor(pos, src.GetColor(pos));
            }
            var c = src.color;
            dst.color = new Color(c.r * 0.7f, c.g * 0.7f, c.b * 0.7f, c.a);
            dr.sharedMaterial = srcRenderer.sharedMaterial;
            dr.sortingLayerID = srcRenderer.sortingLayerID;
            dr.sortingOrder = srcRenderer.sortingOrder;
            dr.mode = srcRenderer.mode;
            Plugin.Log.LogInfo($"First person: front-wall copy of tilemap '{src.name}' ({b.size.x}x{b.size.y})");
            return gridGo;
        }

        private void UpdateBackProxies()
        {
            if (Time.frameCount - lastBackScan >= 30)
            {
                lastBackScan = Time.frameCount;
                var found = new HashSet<SpriteRenderer>();
                // They all carry a Collider2D: a spatial query around the view, not every sprite in the world
                // (perf, 2026-10-06: frame spikes on crowded layers).
                var seen = new HashSet<SpriteRenderer>();
                foreach (var col in Physics2D.OverlapCircleAll(eye, Plugin.FpViewDistance.Value + 10f))
                {
                    var sr = col.GetComponent<SpriteRenderer>();
                    if (sr == null || !seen.Add(sr)) continue;
                    if (sr.gameObject.layer >= 25 || sr.sortingLayerName != "Default" || !sr.enabled) continue;
                    if (!sr.TryGetComponent<Collider2D>(out _) || ObjectExtruder.Extruded.Contains(sr) || OwnBody.Contains(sr)) continue;
                    // Background bands, and big flat backdrops on the play plane (too big to extrude, e.g. the
                    // lifepod's hull): edge-on through the middle of the corridor they read as a tinted pane
                    // splitting the view (user: "brightness seam"). Items, limbs and creatures stay.
                    bool back = sr.sortingOrder <= -100;
                    if (!back)
                    {
                        var bs = sr.bounds.size;
                        back = bs.x * bs.y > 24f && bs.x > 3f && bs.y > 3f
                               && !sr.TryGetComponent<Item>(out _) && !sr.TryGetComponent<Limb>(out _) && sr.GetComponentInParent<Body>() == null;
                        if (back && Plugin.Verbose && loggedBig.Add(sr.name)) Plugin.Log.LogInfo($"First person: '{sr.name}' ({bs.x:F1}x{bs.y:F1}, order {sr.sortingOrder}) drawn on the back wall");
                    }
                    if (back) found.Add(sr);
                }
                var drop = new List<SpriteRenderer>();
                foreach (var kv in backProxies) if (kv.Key == null || !found.Contains(kv.Key)) drop.Add(kv.Key);
                foreach (var d in drop) { if (backProxies[d] != null) Object.Destroy(backProxies[d].gameObject); backProxies.Remove(d); }
                foreach (var sr in found)
                {
                    if (backProxies.ContainsKey(sr)) continue;
                    var go = new GameObject("back " + sr.name) { layer = VoxelTerrain.Layer, hideFlags = HideFlags.DontSave };
                    go.transform.SetParent(backRoot.transform, false);
                    var cp = go.AddComponent<SpriteRenderer>();
                    cp.shadowCastingMode = ShadowCastingMode.Off;
                    cp.receiveShadows = false;
                    backProxies[sr] = cp;
                }
            }
            float z = VoxelTerrain.BackZ - 0.05f * PopupDepth.Scale;
            Vector2 eyeXY = eye;
            float near = Plugin.FpViewDistance.Value + 10f;
            foreach (var kv in backProxies)
            {
                var src = kv.Key;
                var cp = kv.Value;
                if (src == null || cp == null) continue;
                bool show = src.enabled && src.gameObject.activeInHierarchy && src.sprite != null
                            && ((Vector2)src.transform.position - eyeXY).sqrMagnitude < near * near;
                if (cp.enabled != show) cp.enabled = show;
                if (!show) continue;
                var t = src.transform;
                cp.transform.SetPositionAndRotation(new Vector3(t.position.x, t.position.y, z), t.rotation);
                cp.transform.localScale = t.lossyScale;
                if (cp.sprite != src.sprite) cp.sprite = src.sprite;
                cp.flipX = src.flipX;
                cp.flipY = src.flipY;
                cp.color = src.color;
                if (cp.sharedMaterial != src.sharedMaterial) cp.sharedMaterial = src.sharedMaterial;
                cp.sortingLayerID = src.sortingLayerID;
                cp.sortingOrder = src.sortingOrder;
                if (cp.drawMode != src.drawMode) cp.drawMode = src.drawMode;
                if (src.drawMode != SpriteDrawMode.Simple) cp.size = src.size;
            }
        }

        private readonly List<Light2D> proxiesLit = new List<Light2D>();
        private readonly List<(Light2D l, float inner, float outer, float intensity)> ambientLights = new List<(Light2D, float, float, float)>();

        private IEnumerable<Light2D> Enumerate()
        {
            foreach (var l in lights) yield return l;
            foreach (var l in proxiesLit) yield return l;
        }

        private Light2D ProxyFor(Light2D original)
        {
            if (lightProxies.TryGetValue(original, out var px) && px != null) return px;
            if (proxyRoot == null)
            {
                proxyRoot = new GameObject("CasualtyVR.LightProxies") { hideFlags = HideFlags.DontSave };
                Object.DontDestroyOnLoad(proxyRoot);
            }
            var go = new GameObject("light " + original.name) { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(proxyRoot.transform, false);
            go.SetActive(false);
            px = go.AddComponent<Light2D>();
            try
            {
                // Every serialized setting (type, shape, falloff, blend style, target sorting layers...).
                JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(original), px);
            }
            catch (System.Exception ex) { Plugin.Log.LogWarning($"First person: light copy of '{original.name}' failed: {ex.Message}"); }
            px.shadowsEnabled = false;
            px.intensity = 0f;
            go.SetActive(true);
            lightProxies[original] = px;
            if (Plugin.Verbose && original.intensity > 0f && PlayerCamera.main != null && PlayerCamera.main.body != null
                && ((Vector2)(original.transform.position - PlayerCamera.main.body.transform.position)).magnitude < Mathf.Max(original.pointLightOuterRadius, AreaExtent(original)) + 4f)
            {
                string path = original.name;
                for (var tt = original.transform.parent; tt != null; tt = tt.parent) path = tt.name + "/" + path;
                Plugin.Log.LogInfo($"First person: physics light reaching the player: {path} {original.lightType} r {original.pointLightOuterRadius:F1} area {AreaExtent(original):F1} int {original.intensity:F2} col {original.color} pos {original.transform.position} vol {original.volumeIntensity:F2}");
            }
            if (Plugin.Verbose && lightProxies.Count <= 3)
                Plugin.Log.LogInfo($"First person: light copy '{original.name}' {original.lightType}->{px.lightType} r {original.pointLightOuterRadius:F1}->{px.pointLightOuterRadius:F1} int {original.intensity:F2} blend {original.blendStyleIndex}->{px.blendStyleIndex} order {original.lightOrder}->{px.lightOrder} json {JsonUtility.ToJson(px).Length} chars");
            return px;
        }

        // Half-size of an area light in world units (from its shape), to tell whether the player stands inside it.
        private static float AreaExtent(Light2D l)
        {
            var sc = l.transform.lossyScale;
            float k = Mathf.Max(Mathf.Abs(sc.x), Mathf.Abs(sc.y));
            switch (l.lightType)
            {
                case Light2D.LightType.Freeform:
                    float m = 0f;
                    if (l.shapePath != null) foreach (var v in l.shapePath) m = Mathf.Max(m, ((Vector2)v).magnitude);
                    return m * k;
                case Light2D.LightType.Parametric:
                    return l.shapeLightParametricRadius * k;
                case Light2D.LightType.Sprite:
                    return l.lightCookieSprite != null ? l.lightCookieSprite.bounds.extents.magnitude * k : 0f;
                default:
                    return 0f;
            }
        }

        private static bool IsEye(Camera cam) => VrRig.Instance != null && VrRig.Instance.IsEyeCamera(cam);

        private void OnBegin(ScriptableRenderContext ctx, Camera cam)
        {
            if (!Active || !IsEye(cam)) return;
            RenderTime.Start();
            try { OnBeginInner(cam); } finally { RenderTime.Stop(); }
        }

        private void OnBeginInner(Camera cam)
        {
            foreach (var r in OwnBody) Hide(r);
            foreach (var r in extraHidden) Hide(r);
            foreach (var r in backProxies.Keys) Hide(r);
            if (Plugin.FpBillboards.Value) foreach (var r in billboards.Sources) Hide(r);
            if (Plugin.FpFaceLights.Value) FaceLights(cam.transform);
            if (Plugin.Verbose && !planesLogged && Time.timeSinceLevelLoad > 22f) LogPlanesAtEye(cam.transform.position);
        }

        private bool planesLogged;

        // Diagnostic: renderers whose surface passes within a unit of the eye (seam hunt, 2026-10-05).
        private void LogPlanesAtEye(Vector3 e)
        {
            planesLogged = true;
            Plugin.Log.LogInfo($"Plane at eye: scanning around {e}");
            var box = new Bounds(e, new Vector3(0.6f, 0.6f, 1.2f));
            foreach (var r in Object.FindObjectsOfType<Renderer>())
            {
                if (!r.enabled || r.forceRenderingOff || !r.gameObject.activeInHierarchy) continue;
                var b = r.bounds;
                if (!b.Intersects(box)) continue;
                string path = r.name;
                for (var tt = r.transform.parent; tt != null; tt = tt.parent) path = tt.name + "/" + path;
                Plugin.Log.LogInfo($"Plane at eye: {path} {r.GetType().Name} {r.sortingLayerName}/{r.sortingOrder} layer {r.gameObject.layer} z {b.center.z:F2} size {b.size} mat {(r.sharedMaterial != null ? r.sharedMaterial.name : "-")}");
            }
        }

        private void FaceLights(Transform eye)
        {
            Vector3 eyePos = eye.position;
            if (Time.frameCount - lastLightScan >= 60)
            {
                lastLightScan = Time.frameCount;
                lights.Clear();
                physicsLights.Clear();
                int skipped = 0;
                // Only lights that can reach what is in view (perf, 2026-10-06: 1830 lights per eye cost ~2 ms).
                Vector2 lc = eyePos;
                float reach = Plugin.FpViewDistance.Value + 10f;
                foreach (var l in Object.FindObjectsOfType<Light2D>())
                {
                    if (l.lightType == Light2D.LightType.Global) continue;
                    float r = Mathf.Max(l.pointLightOuterRadius, AreaExtent(l));
                    if (((Vector2)l.transform.position - lc).sqrMagnitude > (reach + r) * (reach + r)) continue;
                    // Never move a transform with a Collider2D (lamps held or lying around): Physics2D resyncs it
                    // (PopupDepth.SkipColliders). Those stay edge-on for now (FP-1: proxy lights).
                    if (l.GetComponent<Collider2D>() != null) { skipped++; physicsLights.Add(l); continue; }
                    lights.Add(l);
                }
                // Drop proxies of lights that are gone.
                var deadProxies = new List<Light2D>();
                foreach (var kv in lightProxies) if (kv.Key == null) deadProxies.Add(kv.Key);
                foreach (var dp in deadProxies) { if (lightProxies[dp] != null) Object.Destroy(lightProxies[dp].gameObject); lightProxies.Remove(dp); }
                if (Plugin.Verbose && !lightsLogged && Time.timeSinceLevelLoad > 20f && PlayerCamera.main != null && PlayerCamera.main.body != null)
                {
                    lightsLogged = true;
                    var bp = PlayerCamera.main.body.transform.position;
                    foreach (var l in Object.FindObjectsOfType<Light2D>())
                    {
                        if (((Vector2)(l.transform.position - bp)).magnitude > 8f && l.lightType != Light2D.LightType.Global) continue;
                        string path = l.name;
                        for (var tt = l.transform.parent; tt != null; tt = tt.parent) path = tt.name + "/" + path;
                        Plugin.Log.LogInfo($"Light: {path} {l.lightType} r {l.pointLightInnerRadius:F1}-{l.pointLightOuterRadius:F1} int {l.intensity:F2} col {l.color} blend {l.blendStyleIndex} vol {l.volumeIntensity:F2} pos {l.transform.position} col2d {(l.GetComponent<Collider2D>() != null)} enabled {l.isActiveAndEnabled}");
                    }
                }
                if (Plugin.Verbose && lastLightLog++ % 20 == 0) Plugin.Log.LogInfo($"First person: {lights.Count} lights face the eye, {skipped} on physics objects left as they are");
            }
            // Lights on physics objects cannot be turned (moving them would make Physics2D resync), and edge-on
            // in the play plane their mesh lights everything on one side of it: half the view (the "brightness
            // seam", 2026-10-05). They are dark in the eyes for now (FP-1: proxy lights).
            foreach (var l in physicsLights)
            {
                if (l == null || !l.isActiveAndEnabled) continue;
                var px = ProxyFor(l);
                if (px != null)
                {
                    var ot = l.transform;
                    px.transform.SetPositionAndRotation(ot.position, ot.rotation);
                    px.transform.localScale = ot.lossyScale;
                    px.color = l.color;
                    px.intensity = l.intensity;
                    px.pointLightOuterRadius = l.pointLightOuterRadius;
                    px.pointLightInnerRadius = l.pointLightInnerRadius;
                    proxiesLit.Add(px);
                }
                if (l.intensity == 0f) continue;
                dimmed.Add((l, l.intensity));
                l.intensity = 0f;
            }
            foreach (var l in Enumerate())
            {
                if (l == null || !l.isActiveAndEnabled) continue;
                var t = l.transform;
                // Lights' z means nothing to the 2D renderer (a wall-hole light sat at z=1090): kept inside the corridor.
                Vector3 p = new Vector3(t.position.x, t.position.y, Mathf.Clamp(t.position.z, VoxelTerrain.FrontZ, VoxelTerrain.BackZ));
                Vector3 dir = p - eyePos;
                if (((Vector2)dir).sqrMagnitude > (l.pointLightOuterRadius + 60f) * (l.pointLightOuterRadius + 60f)) continue;
                turnedLights.Add((t, t.rotation, t.position, l, l.shadowsEnabled));
                // 2D shadows are drawn as flat shapes in the play plane: seen from inside it they split the view
                // along that plane into a shadowed and a lit half (the "brightness seam"). The blocks occlude in 3D.
                if (Plugin.FpNoLightShadows.Value && l.shadowsEnabled) l.shadowsEnabled = false;
                // A point light around the player (the character's own glow, a held lamp) is at the eye: facing
                // the eye it would collapse to nothing. It is drawn as a disc in front of the eye instead, half
                // its radius away, so the view is lit like a headlamp, brightest where you look.
                if (l.lightType == Light2D.LightType.Point && dir.magnitude < l.pointLightOuterRadius * 0.5f && l.transform.IsChildOf(PlayerRoot))
                {
                    if (DbgLightMode == 0) { turnedLights.RemoveAt(turnedLights.Count - 1); continue; }
                    // Its 2D shadows are cast from its 2D position: moved ahead it would sit inside the rock and
                    // shadow everything, so it casts none while moved (the blocks occlude in 3D anyway).
                    float d = DbgLightMode == 3 ? 2f : Mathf.Max(0.2f, l.pointLightOuterRadius * 0.5f);
                    if (DbgLightMode != 2) l.shadowsEnabled = false;
                    t.SetPositionAndRotation(eyePos + eye.forward * d, Quaternion.LookRotation(eye.forward, eye.up));
                    continue;
                }
                // Any other point light near the eye: facing the eye, its disc plane would pass beside the eye and
                // split the view into a lit and an unlit half (the "brightness seam"). It keeps its direction from
                // the eye, but its disc is pushed ahead of the eye (at least a third of its radius) facing forward.
                // Drawn as a disc ahead of the eye, its brightness would depend on where you look rather than where
                // things are. Instead it becomes an even light over the whole view, as strong as the light is at
                // the player's position (linear falloff from the inner to the outer radius): standing under the
                // lifepod's ceiling light lights the pod; walking out of its range fades it.
                if (l.lightType == Light2D.LightType.Point && dir.magnitude < l.pointLightOuterRadius && l.pointLightOuterRadius >= AmbientMinRadius)
                {
                    float inner = l.pointLightInnerRadius, outer = l.pointLightOuterRadius;
                    float f = Mathf.Clamp01(1f - (((Vector2)dir).magnitude - inner) / Mathf.Max(0.01f, outer - inner));
                    ambientLights.Add((l, inner, outer, l.intensity));
                    l.shadowsEnabled = false;
                    l.intensity *= f;
                    l.pointLightInnerRadius = 900f;
                    l.pointLightOuterRadius = 1000f;
                    t.SetPositionAndRotation(eyePos + eye.forward * 1f, Quaternion.LookRotation(eye.forward, eye.up));
                    continue;
                }
                // An area light (freeform, parametric, sprite) around the player, e.g. the lifepod's room light: its
                // plane also passes beside the eye. It is drawn just ahead of the eye, facing it, so it covers the
                // whole view as the room light it is.
                if (l.lightType != Light2D.LightType.Point && ((Vector2)dir).magnitude < AreaExtent(l))
                {
                    l.shadowsEnabled = false;
                    t.SetPositionAndRotation(eyePos + eye.forward * 0.5f, Quaternion.LookRotation(eye.forward, eye.up));
                    continue;
                }
                // A small light close by (a glow plant, a lamp on the floor): facing the eye from beside it, its disc
                // would split the view; it is drawn ahead of the eye in its own direction, as a local glow.
                if (l.lightType == Light2D.LightType.Point && dir.magnitude < l.pointLightOuterRadius)
                {
                    Vector3 fwd = eye.forward;
                    float along = Vector3.Dot(dir, fwd);
                    float ahead = Mathf.Max(along, l.pointLightOuterRadius * 0.5f);
                    l.shadowsEnabled = false;
                    t.SetPositionAndRotation(eyePos + fwd * ahead + (dir - fwd * along), Quaternion.LookRotation(fwd, eye.up));
                    continue;
                }
                if (dir.sqrMagnitude < 1e-6f) continue;
                // Keep the light's own in-plane roll (sprite and cookie lights), facing the eye.
                t.SetPositionAndRotation(p, Quaternion.LookRotation(dir, Vector3.up) * Quaternion.Euler(0f, 0f, t.eulerAngles.z));
            }
        }

        private void Hide(Renderer r)
        {
            if (r == null || r.forceRenderingOff) return;
            r.forceRenderingOff = true;
            hidden.Add(r);
        }

        private void OnEnd(ScriptableRenderContext ctx, Camera cam)
        {
            RenderTime.Start();
            try { OnEndInner(); } finally { RenderTime.Stop(); }
        }

        private void OnEndInner()
        {
            foreach (var r in hidden) if (r != null) r.forceRenderingOff = false;
            hidden.Clear();
            for (int i = turnedLights.Count - 1; i >= 0; i--)
            {
                var e = turnedLights[i];
                if (e.t != null) e.t.SetPositionAndRotation(e.pos, e.rot);
                if (e.light != null && e.light.shadowsEnabled != e.shadows) e.light.shadowsEnabled = e.shadows;
            }
            turnedLights.Clear();
            foreach (var d in dimmed) if (d.l != null) d.l.intensity = d.intensity;
            dimmed.Clear();
            for (int i = ambientLights.Count - 1; i >= 0; i--)
            {
                var a = ambientLights[i];
                if (a.l == null) continue;
                a.l.pointLightInnerRadius = a.inner;
                a.l.pointLightOuterRadius = a.outer;
                a.l.intensity = a.intensity;
            }
            ambientLights.Clear();
            foreach (var px in proxiesLit) if (px != null) px.intensity = 0f;
            proxiesLit.Clear();
        }

        public void Destroy()
        {
            RenderPipelineManager.beginCameraRendering -= OnBegin;
            RenderPipelineManager.endCameraRendering -= OnEnd;
            OnEnd(default, null);
            if (Active) { Active = false; PopupDepth.Invalidate(); }
            OwnBody.Clear();
            Object.Destroy(wallRoot);
            Object.Destroy(backRoot);
            billboards.Destroy();
            arms.Destroy();
            shaderFog.Destroy();
            bodyView.Destroy();
            foreach (var (go, mat) in fogSheets) { Object.Destroy(go); Object.Destroy(mat); }
            fogSheets.Clear();
            blackout = vignette = 0f;
            if (proxyRoot != null) Object.Destroy(proxyRoot);
            lightProxies.Clear();
            frontWall.Clear();
            tileMirrors.Clear();
            backProxies.Clear();
        }
    }
}
