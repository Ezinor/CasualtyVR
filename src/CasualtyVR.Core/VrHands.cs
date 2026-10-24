using UnityEngine;
using UnityEngine.Rendering;

namespace CasualtyVR
{
    /// <summary>
    /// Hand representations at the controllers, drawn with the game's own minigame hand art
    /// (MinigameBase.handSprites: 0-1 open grasp, 2 closed grasp, 3-4 point, 5 point pressed, 6-7 tweezers,
    /// 10 stump). Each hand is a billboard facing the eye, rotated in the view plane so the hand points
    /// along the controller's pointing direction; open hand at rest, trigger = pressing finger, grip = fist. The left hand is
    /// mirrored, as the game mirrors its hand for the left hand slot. Drawn after post with the UI overlays
    /// (VrUi.DrawInto); the game's own minigame hand still follows the cursor on the dashboard.
    /// </summary>
    internal static class VrHands
    {
        private static Material material;
        private static readonly System.Collections.Generic.Dictionary<Sprite, Mesh> quads = new System.Collections.Generic.Dictionary<Sprite, Mesh>();
        private static readonly MaterialPropertyBlock props = new MaterialPropertyBlock();

        private static bool dumped;

        /// <summary>--cvr-dbg-dumphands: save every MinigameBase.handSprites entry as a PNG (to pick the art).</summary>
        private static void Dump(MinigameBase mb)
        {
            dumped = true;
            string dir = System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "CasualtyVR", "hands");
            System.IO.Directory.CreateDirectory(dir);
            for (int i = 0; i < mb.handSprites.Length; i++)
            {
                var sp = mb.handSprites[i];
                if (sp == null) continue;
                var tex = sp.texture;
                var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(tex, rt);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var r = sp.textureRect;
                var outTex = new Texture2D((int)r.width, (int)r.height, TextureFormat.RGBA32, false);
                outTex.ReadPixels(new Rect(r.x, r.y, r.width, r.height), 0, 0);
                outTex.Apply();
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, $"hand{i:00}-{sp.name}.png"), outTex.EncodeToPNG());
                // The tool tip (top-most opaque pixel) as a fraction of the whole sprite rect, which the UI image
                // stretches over its rect; textureRect is trimmed to the opaque pixels (tight packing).
                var px = outTex.GetPixels32();
                int w = (int)r.width, hgt = (int)r.height, tipX = -1, tipY = -1;
                for (int y = hgt - 1; y >= 0 && tipX < 0; y--)
                    for (int x = 0; x < w; x++)
                        if (px[y * w + x].a > 100) { tipX = x; tipY = y; break; }
                var off = sp.textureRectOffset;
                var full = sp.rect.size;
                Plugin.Log.LogInfo($"Hands: {i:00} {sp.name} rect {full} textureRect {r} offset {off} pivot {sp.pivot} tip px ({tipX},{tipY}) -> fraction ({(off.x + tipX + 0.5f) / full.x:F3}, {(off.y + tipY + 0.5f) / full.y:F3})");
                Object.Destroy(outTex);
            }
            Plugin.Log.LogInfo($"Hands: dumped {mb.handSprites.Length} sprites to {dir}");
        }

        private static bool loggedMinigameHand;

        // ---- minigame hand offset -----------------------------------------------------------------------

        // Minigames act at the mouse (UI raycasts) or at MinigameBase.handPos, which is the hand image's pivot, at
        // (0.25, 0.86) of a 768x1890 image (every hand sprite is 256x630, stretched 3x). The game puts each tool's
        // working point close to that pivot. Measured on the full sprite rects (--cvr-dbg-dumphands, 2026-10-07;
        // the earlier measurement used the trimmed texture rects and was wrong):
        //   pointing fingertip (0.248, 0.907): 89 px above the click; lockpick tip (0.244, 0.885): 47 px above.
        // The hand image can be shifted relative to the click point (VR settings, Controls: Minigame hand X/Y, image
        // pixels; mirrored with the game's left-hand scale); 0/0 is the game's own geometry.
        private static RectTransform pivotHand;
        private static Vector2 gamePivot;

        /// <summary>Once per frame after the game's Update (VrRig.LateUpdate); vr = VR running.</summary>
        public static void UpdateMinigamePivot(bool vr)
        {
            var mb = MinigameBase.main;
            var ht = mb != null ? mb.handTransform : null;
            if (ht != pivotHand)
            {
                pivotHand = ht;
                if (ht != null) gamePivot = ht.pivot;
            }
            if (ht == null) return;
            Vector2 want = gamePivot;
            var game = mb.currentMinigame;
            if (vr && game is LockpingMinigame lockpick)
            {
                // Lockpicking (user, 2026-10-07: "seems off now while the other hand minigames feel good"): the pick
                // has to start on a ring only 52 px wide (195-247 px from the lock's centre, LockpingMinigame.Update),
                // about 3 cm on the touch panel, and the click lands 47 px below the drawn pick tip (89 px below the
                // fingertip without a lockpicking kit): aiming with the tip mostly missed the ring. In VR the click
                // point is exactly the tool's tip, whatever the hand offsets; the idle sprite's tip, kept while the
                // pick turns so the image does not jump.
                want = lockpick.HandType() == Minigame.HandSpriteType.Lockpick ? LockpickTip : FingerTip;
                if (Plugin.Verbose && Time.unscaledTime >= nextLockLog)
                {
                    nextLockLog = Time.unscaledTime + 1f;
                    Plugin.Log.LogInfo($"Lockpick: hand {mb.handPos} distance {lockpick.HandDistance:F0} angle {lockpick.HandAngle:F0} (ring 195-247, 0-180) clicking {mb.handClicking} inside {lockpick.clickingInside} progress {lockpick.lockProgress:F2}");
                }
            }
            else if (vr)
            {
                // Moving the image right/up by (x, y) px = pivot left/down by that fraction of the image.
                var size = ht.rect.size;
                if (size.x > 1f && size.y > 1f)
                    want -= new Vector2(Plugin.MinigameHandX.Value / size.x, Plugin.MinigameHandY.Value / size.y);
            }
            if (ht.pivot != want)
            {
                ht.pivot = want;
                if (Plugin.Verbose && game != lastLoggedGame)
                {
                    lastLoggedGame = game;
                    Plugin.Log.LogInfo($"Minigame hand: {game?.GetType().Name ?? "none"} {(game != null ? game.HandType().ToString() : "")} pivot {want} (the game's {gamePivot})");
                }
            }
        }

        // Tips as fractions of the hand image (x from the left, y from the bottom).
        private static readonly Vector2 LockpickTip = new Vector2(0.244f, 0.885f), FingerTip = new Vector2(0.248f, 0.907f);
        private static Minigame lastLoggedGame;
        private static float nextLockLog;

        public static int Draw(CommandBuffer cmd, Camera eye)
        {
            var mbLog = MinigameBase.main;
            if (Plugin.Verbose && !loggedMinigameHand && mbLog != null && mbLog.handTransform != null)
            {
                loggedMinigameHand = true;
                var ht = mbLog.handTransform;
                var img = ht.GetComponent<UnityEngine.UI.Image>();
                Plugin.Log.LogInfo($"Minigame hand: pivot {ht.pivot} size {ht.rect.size} anchors {ht.anchorMin}/{ht.anchorMax} scale {ht.localScale} sprite {img?.sprite?.name} spriteRect {img?.sprite?.rect} spritePivot {img?.sprite?.pivot} preserveAspect {img?.preserveAspect} type {img?.type}");
            }
            var mb = MinigameBase.main;
            if (mb == null || mb.handSprites == null || mb.handSprites.Length < 6) return 0;
            if (!dumped && System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "--cvr-dbg-dumphands") >= 0) Dump(mb);
            if (!Plugin.ShowHands.Value || !VrInput.Enabled) return 0;
            if (material == null)
            {
                material = new Material(Shader.Find("Sprites/Default")) { name = "CasualtyVR.Hand" };
            }
            int drawn = 0;
            for (int h = 0; h < 2; h++)
            {
                if (!VrInput.HandPose(h, out _, out _, out float trigger, out float grip)) continue;
                // The hand art follows the grip pose (Phase 4a): the forearm along the controller's handle.
                if (!VrInput.GripPose(h, out Vector3 pos, out Quaternion gripRot)) continue;
                Vector3 forward = gripRot * Vector3.forward;
                // Three clearly different poses: open hand at rest, pressing finger on the trigger, fist on the grip.
                Sprite sp = grip > 0.5f ? mb.handSprites[2] : trigger > 0.5f ? mb.handSprites[5] : mb.handSprites[0];
                if (sp == null) continue;

                Vector3 toEye = (eye.transform.position - pos).normalized;
                // The game's hand art is an upright forearm with the hand at the top (dumped with
                // --cvr-dbg-dumphands; the first version laid it sideways, user: "rotate 90 degrees toward
                // centre"). The sprite's up axis follows the pointing direction projected onto the
                // billboard plane; the left hand is mirrored, as the game does for its left hand slot.
                Vector3 point = Vector3.ProjectOnPlane(forward, toEye);
                if (point.sqrMagnitude < 1e-6f) point = eye.transform.up;
                point.Normalize();
                bool left = h == 0;
                var rot = Quaternion.LookRotation(-toEye, point);
                float size = Plugin.HandSize.Value * VrSpace.Scale;
                float aspect = sp.rect.width / Mathf.Max(1f, sp.rect.height);
                var scale = new Vector3(size * aspect * (left ? -1f : 1f), size, 1f);
                // Put the hand (top of the sprite), not the sprite centre, at the controller's aim point.
                var m = Matrix4x4.TRS(pos - point * size * 0.4f, rot, scale);

                props.Clear();
                props.SetTexture("_MainTex", sp.texture);
                cmd.DrawMesh(QuadFor(sp), m, material, 0, 0, props);
                drawn++;
            }
            return drawn;
        }

        // ---- hand cursor ----------------------------------------------------------------------------

        // The hand art is a forearm with the hand at the top; the top quarter is the hand cut at the wrist.
        private const float WristCut = 0.26f;
        // The click point is the index fingertip of each pose, measured on the dumped art (fraction of the
        // cut hand's width from the left, and of its height from the top). The first version used one point
        // between the index and middle fingers (user: "a bit off ... more focused on the index finger").
        private static Vector2 Hot(int pose)
        {
            switch (pose)
            {
                case 5: return new Vector2(0.07f, 0.06f);   // pressing: the extended index finger
                case 2: return new Vector2(0.21f, 0.03f);   // fist: the index knuckle
                default: return new Vector2(0.15f, 0.07f);  // open hand
            }
        }
        private static readonly System.Collections.Generic.Dictionary<Sprite, Mesh> cuts = new System.Collections.Generic.Dictionary<Sprite, Mesh>();

        /// <summary>
        /// The game's hand, cut at the wrist, as the mouse cursor (user request 2026-10-04): open hand at rest,
        /// pressing finger while the laser hand's trigger is held, fist on its grip. The character's active hand
        /// picks the side: the right-hand art for hand slot 0, mirrored for slot 1 (as MinigameBase does).
        /// <paramref name="tip"/> places the hot spot; its rotation and scale are the panel's (scale = height).
        /// </summary>
        public static bool DrawCursor(CommandBuffer cmd, Matrix4x4 tip)
        {
            var mb = MinigameBase.main;
            if (mb == null || mb.handSprites == null || mb.handSprites.Length < 6) return false;
            if (material == null) material = new Material(Shader.Find("Sprites/Default")) { name = "CasualtyVR.Hand" };
            int laser = Plugin.LaserHand.Value == "Left" ? 0 : 1;
            VrInput.HandPose(laser, out _, out _, out float trigger, out float grip);
            bool mouseDown = Input.GetMouseButton(0) || Input.GetMouseButton(1);
            int pose = grip > 0.5f ? 2 : (trigger > 0.5f || mouseDown) ? 5 : 0;
            Sprite sp = mb.handSprites[pose];
            if (sp == null) return false;
            var body = PlayerCamera.main != null ? PlayerCamera.main.body : null;
            bool leftHand = body != null && body.handSlot != 0;
            var m = tip * Matrix4x4.Scale(new Vector3(leftHand ? -1f : 1f, 1f, 1f));
            props.Clear();
            props.SetTexture("_MainTex", sp.texture);
            cmd.DrawMesh(CutFor(sp, Hot(pose)), m, material, 0, 0, props);
            return true;
        }

        // The top WristCut of the sprite as a quad one unit tall, hot spot at the origin.
        private static Mesh CutFor(Sprite sp, Vector2 hot)
        {
            if (cuts.TryGetValue(sp, out var cached) && cached != null) return cached;
            var r = sp.textureRect;
            float w = sp.texture.width, hgt = sp.texture.height;
            float u0 = r.xMin / w, u1 = r.xMax / w, v1 = r.yMax / hgt, v0 = (r.yMax - r.height * WristCut) / hgt;
            float aspect = r.width / (r.height * WristCut);
            float x0 = -hot.x * aspect, x1 = (1f - hot.x) * aspect;
            float y0 = -(1f - hot.y), y1 = hot.y;
            var m = new Mesh { name = "CasualtyVR.HandCursor " + sp.name };
            m.vertices = new[] { new Vector3(x0, y0), new Vector3(x1, y0), new Vector3(x1, y1), new Vector3(x0, y1) };
            m.uv = new[] { new Vector2(u0, v0), new Vector2(u1, v0), new Vector2(u1, v1), new Vector2(u0, v1) };
            m.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            m.triangles = new[] { 0, 1, 2, 0, 2, 3, 0, 2, 1, 0, 3, 2 };
            cuts[sp] = m;
            return m;
        }

        // Unit quad with the sprite's atlas rectangle as UVs (Sprites/Default ignores texture tiling).
        private static Mesh QuadFor(Sprite sp)
        {
            if (quads.TryGetValue(sp, out var cached) && cached != null) return cached;
            var r = sp.textureRect;
            float w = sp.texture.width, hgt = sp.texture.height;
            float u0 = r.xMin / w, u1 = r.xMax / w, v0 = r.yMin / hgt, v1 = r.yMax / hgt;
            var m = new Mesh { name = "CasualtyVR.Hand " + sp.name };
            m.vertices = new[] { new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f), new Vector3(0.5f, 0.5f), new Vector3(-0.5f, 0.5f) };
            m.uv = new[] { new Vector2(u0, v0), new Vector2(u1, v0), new Vector2(u1, v1), new Vector2(u0, v1) };
            m.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            // Both windings: the eye projection's Y flip and the mirrored left hand invert it.
            m.triangles = new[] { 0, 1, 2, 0, 2, 3, 0, 2, 1, 0, 3, 2 };
            quads[sp] = m;
            return m;
        }
    }
}
