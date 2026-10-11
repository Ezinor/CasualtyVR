using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CasualtyVR
{
    /// <summary>
    /// Passthrough greenscreen (user request 2026-10-04): everything outside the game window, and the game's
    /// backmost layer inside it, in one user-chosen key colour, so a runtime's chroma-key passthrough (e.g.
    /// Virtual Desktop) shows the room there.
    /// * Outside: DioramaFrame is drawn after post-processing in the key colour (VrUi.DrawInto), so colour
    ///   grading and bloom cannot shift it.
    /// * Inside: renderers in the "Back" sorting layer (the cave-rock backdrop) and the background walls
    ///   (WorldGeneration.backgrounds) are hidden for the eyes and
    ///   the eye cameras clear to the key colour. That colour does pass through the game's grading
    ///   (ColorAdjustments, LiftGammaGain), so it can differ slightly from the outside.
    /// </summary>
    internal sealed class Greenscreen
    {
        private readonly List<Renderer> back = new List<Renderer>();
        private readonly List<Renderer> hidden = new List<Renderer>();
        private int scannedFrame = -1000;
        private bool wasOn;
        private Color savedColor;
        private CameraClearFlags savedFlags;

        public Greenscreen()
        {
            RenderPipelineManager.beginCameraRendering += OnBegin;
            RenderPipelineManager.endCameraRendering += OnEnd;
        }

        // Mist (default fog for the eyes, user 2026-10-04: the lit haze flickered and swam with head
        // movement, because the 2D light textures are per-eye screen space and the haze plane floats in front
        // of what they light). Unlit and world-anchored: a dark, desaturated veil, clear around the player and
        // thickening with distance, as the game's FogShader does around _PlayerPos. One quad on the VR-only
        // layer, just in front of the block faces.
        private GameObject mist;
        private Material mistMat;
        private const float MistSize = 140f;   // world units: past the edges of the game window

        private void UpdateMist()
        {
            var world = WorldGeneration.world;
            var body = PlayerCamera.main != null ? PlayerCamera.main.body : null;
            bool on = !VrRig.DbgNoFog && Plugin.FogMode.Value == "Mist" && world != null && world.fogSprite != null
                      && world.fogAmount > 0f && body != null && Plugin.VoxelsOn.Value && !FirstPerson.Active;
            if (!on) { if (mist != null && mist.activeSelf) mist.SetActive(false); return; }
            if (mist == null) BuildMist();
            if (!mist.activeSelf) mist.SetActive(true);
            var p = body.transform.position;
            mist.transform.position = new Vector3(p.x, p.y, VoxelTerrain.FrontZ - 0.1f * PopupDepth.Scale);
            mist.transform.localScale = new Vector3(MistSize, MistSize, 1f);
            var c = Plugin.FogMistColour;
            mistMat.color = new Color(c.r, c.g, c.b, Mathf.Clamp01(world.fogAmount * Plugin.FogMist.Value));
        }

        private void BuildMist()
        {
            // Radial ramp: clear inside ~4 blocks of the player, full at ~22 blocks (of a 140-block quad).
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "CasualtyVR.Mist" };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n - 0.5f, dy = (y + 0.5f) / n - 0.5f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) * MistSize;   // world units from the centre
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(4f, 22f, d));
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            mistMat = new Material(Shader.Find("Sprites/Default")) { name = "CasualtyVR.Mist", mainTexture = tex };
            mist = new GameObject("CasualtyVR.Mist") { layer = VoxelTerrain.Layer, hideFlags = HideFlags.DontSave };
            Object.DontDestroyOnLoad(mist);
            var mesh = new Mesh { name = "CasualtyVR.Mist" };
            mesh.vertices = new[] { new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f), new Vector3(0.5f, 0.5f), new Vector3(-0.5f, 0.5f) };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3, 0, 2, 1, 0, 3, 2 };
            mist.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = mist.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mistMat;
            mr.sortingLayerName = "Top";
            mr.sortingOrder = 0;   // where the game's fog sprite draws
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        public void Update(Camera[] eyes, Camera source)
        {
            UpdateMist();
            // The greenscreen key colour, or black for first person's Void sides.
            bool key = Plugin.GreenscreenOn;
            bool on = key || FirstPerson.VoidSides;
            foreach (var eye in eyes)
            {
                if (eye == null) continue;
                if (on)
                {
                    if (!wasOn) { savedColor = eye.backgroundColor; savedFlags = eye.clearFlags; }
                    eye.clearFlags = CameraClearFlags.SolidColor;
                    var c = key ? Plugin.GreenscreenColor.Value : Color.black;
                    eye.backgroundColor = new Color(c.r, c.g, c.b, 1f);
                }
                else if (wasOn)
                {
                    eye.backgroundColor = source != null ? source.backgroundColor : savedColor;
                    eye.clearFlags = source != null ? source.clearFlags : savedFlags;
                }
            }
            wasOn = on;
        }

        private void OnBegin(ScriptableRenderContext ctx, Camera cam)
        {
            if (VrRig.Instance != null && VrRig.Instance.IsEyeCamera(cam)) FogBegin();
            if (!(Plugin.GreenscreenOn || FirstPerson.VoidSides) || VrRig.Instance == null || !VrRig.Instance.IsEyeCamera(cam)) return;
            if (Time.frameCount - scannedFrame > 120)
            {
                scannedFrame = Time.frameCount;
                back.Clear();
                foreach (var r in Object.FindObjectsOfType<Renderer>())
                    if (r.sortingLayerName == "Back") back.Add(r);
                // The background walls behind the tunnels (WorldGeneration.CreateBackground: rock, soil,
                // steel... at order -9999): the backmost layer of the game window (user, 2026-10-04).
                var world = WorldGeneration.world;
                if (world != null && world.backgrounds != null)
                    foreach (var bg in world.backgrounds)
                        if (bg != null) back.AddRange(bg.GetComponentsInChildren<Renderer>());
            }
            foreach (var r in back)
                if (r != null && !r.forceRenderingOff) { r.forceRenderingOff = true; hidden.Add(r); }
        }

        // ---- fog (Foggy layer modifier) ----------------------------------------------------------------
        // The game's fog is one big sprite (WorldGeneration.fogSprite, Shader Graphs/FogShader). In the eye
        // cameras that shader renders as dark opaque blobs that hide the character and most objects (user
        // report 2026-10-04, "unplayable"; reproduced with --cvr-dbg-fog), apparently because it builds its
        // see-through look from a scene texture the eye cameras do not get. For the eyes it is swapped for a
        // haze: the same sprite with the game's lit sprite material at low opacity (glows near lights, fades
        // in the dark, like the flat game), or hidden (VR settings, Fog).
        private Material hazeMat;
        private SpriteRenderer fogSwapped;
        private Material fogOriginalMat;
        private Color fogOriginalColor;

        private void FogBegin()
        {
            var world = WorldGeneration.world;
            var fog = world != null ? world.fogSprite : null;
            if (fog == null || fogSwapped != null || !fog.enabled || world.fogAmount <= 0f) return;
            if (VrRig.DbgNoFog || Plugin.FogMode.Value != "Haze")   // Off, or Mist (drawn by UpdateMist instead)
            {
                if (!fog.forceRenderingOff) { fog.forceRenderingOff = true; hidden.Add(fog); }
                return;
            }
            if (hazeMat == null) hazeMat = new Material(world.defaultMat) { name = "CasualtyVR.FogHaze" };
            fogSwapped = fog;
            fogOriginalMat = fog.sharedMaterial;
            fogOriginalColor = fog.color;
            fog.sharedMaterial = hazeMat;
            var c = fogOriginalColor;
            fog.color = new Color(c.r, c.g, c.b, world.fogAmount * Mathf.Clamp01(Plugin.FogHaze.Value));
        }

        private void FogEnd()
        {
            if (fogSwapped == null) return;
            fogSwapped.sharedMaterial = fogOriginalMat;
            fogSwapped.color = fogOriginalColor;
            fogSwapped = null;
        }

        private void OnEnd(ScriptableRenderContext ctx, Camera cam)
        {
            FogEnd();
            foreach (var r in hidden) if (r != null) r.forceRenderingOff = false;
            hidden.Clear();
        }

        public void Destroy()
        {
            RenderPipelineManager.beginCameraRendering -= OnBegin;
            RenderPipelineManager.endCameraRendering -= OnEnd;
            OnEnd(default, null);
            if (hazeMat != null) Object.Destroy(hazeMat);
            if (mist != null) { Object.Destroy(mist.GetComponent<MeshFilter>().sharedMesh); Object.Destroy(mist); }
            if (mistMat != null) { Object.Destroy(mistMat.mainTexture); Object.Destroy(mistMat); }
        }
    }
}
