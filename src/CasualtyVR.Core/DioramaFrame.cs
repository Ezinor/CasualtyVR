using System.Collections.Generic;
using UnityEngine;

namespace CasualtyVR
{
    /// <summary>
    /// A black "window frame" that only the eye cameras see. Its opening matches what the game's own
    /// orthographic camera shows (times <see cref="Plugin.FrameMargin"/>), so the wider VR view reveals no
    /// more of the map than the flat game does (user report 2026-10-03: "I could look down and around and
    /// see far more of the map").
    ///
    /// The edge is a soft band (user request 2026-10-03: "fade a bit instead of a hard edge"): alpha
    /// rises along a smoothstep from 0 to 1 across <see cref="Plugin.FrameFade"/> × the view's half-height,
    /// centred on the opening, so on average the window still shows what the flat game shows.
    ///
    /// It is drawn last by sorting (LineOfSight / max order), so it masks every layer regardless of depth,
    /// and it is placed at the front of the pop-up stack (z = -3 x PopupScale), so the opening reads as a window you
    /// look through. A margin of 0 disables it.
    /// </summary>
    internal sealed class DioramaFrame
    {
        public const int Layer = 26;   // VR-only, drawn by the eye cameras (unused by the game: probe saw layers 0-10)
        private const float OuterScale = 40f;
        private const int FadeSteps = 6;   // rings across the fade band

        private readonly GameObject go;
        private readonly Mesh mesh;
        private readonly MeshRenderer renderer;
        private readonly Material material, keyMaterial;

        /// <summary>Passthrough greenscreen: the frame is drawn after post in the key colour (VrUi.DrawInto).</summary>
        public bool DrawnAfterPost => Plugin.GreenscreenOn && go.activeSelf;
        public Mesh Mesh => mesh;
        public Matrix4x4 Matrix => go.transform.localToWorldMatrix;
        public Material KeyMaterial { get { keyMaterial.color = Plugin.GreenscreenColor.Value; return keyMaterial; } }
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Color> colors = new List<Color>();
        private readonly List<int> triangles = new List<int>();
        private float builtMargin = -1f, builtFade = -1f, builtHalfH = -1f, builtAspect = -1f;

        public DioramaFrame()
        {
            go = new GameObject("CasualtyVR.DioramaFrame") { layer = Layer, hideFlags = HideFlags.DontSave };
            Object.DontDestroyOnLoad(go);
            mesh = new Mesh { name = "CasualtyVR.DioramaFrame" };
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            renderer = go.AddComponent<MeshRenderer>();
            // Sprites/Default multiplies vertex colour (white, per-vertex alpha) by the material colour:
            // black normally, the key colour for the passthrough greenscreen.
            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            material = new Material(shader) { color = Color.black, name = "CasualtyVR.DioramaFrame" };
            keyMaterial = new Material(shader) { color = Color.green, name = "CasualtyVR.DioramaFrameKey" };
            renderer.sharedMaterial = material;
            renderer.sortingLayerName = "LineOfSight";
            renderer.sortingOrder = short.MaxValue - 1;   // the HUD (VrUi) draws above it
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        /// <summary>Opening = the logic camera's view rectangle, centred on <paramref name="focus"/>.</summary>
        public void Update(Camera logic, Vector3 focus)
        {
            float margin = Plugin.FrameMargin.Value;
            go.SetActive(margin > 0f && logic != null);
            if (!go.activeSelf) return;
            renderer.forceRenderingOff = Plugin.GreenscreenOn;

            float halfH = logic.orthographic ? logic.orthographicSize : 22.5f;
            float aspect = logic.aspect;
            float fade = Mathf.Max(0f, Plugin.FrameFade.Value);
            if (margin != builtMargin || fade != builtFade || halfH != builtHalfH || aspect != builtAspect)
            {
                Rebuild(halfH * margin, halfH * aspect * margin, halfH * fade);
                builtMargin = margin; builtFade = fade; builtHalfH = halfH; builtAspect = aspect;
            }
            go.transform.position = new Vector3(focus.x, focus.y, -3f * PopupDepth.Scale);   // front of the pop-up stack
        }

        private void Rebuild(float halfH, float halfW, float band)
        {
            // Concentric rectangles from the inner edge of the fade band outwards, each with one alpha.
            var rings = new List<(float hw, float hh, float a)>();
            if (band <= 0f)
            {
                rings.Add((halfW, halfH, 1f));
            }
            else
            {
                for (int i = 0; i <= FadeSteps; i++)
                {
                    float t = (float)i / FadeSteps;
                    float offset = (t - 0.5f) * band;               // centred on the opening
                    rings.Add((halfW + offset, halfH + offset, t * t * (3f - 2f * t)));
                }
            }
            rings.Add((halfW * OuterScale, halfH * OuterScale, 1f));

            vertices.Clear(); colors.Clear(); triangles.Clear();
            foreach (var (hw, hh, a) in rings)
            {
                var c = new Color(1f, 1f, 1f, a);
                vertices.Add(new Vector3(-hw, -hh)); vertices.Add(new Vector3(hw, -hh));
                vertices.Add(new Vector3(hw, hh));   vertices.Add(new Vector3(-hw, hh));
                for (int k = 0; k < 4; k++) colors.Add(c);
            }
            for (int r = 0; r + 1 < rings.Count; r++)
            {
                int inner = r * 4, outer = inner + 4;
                for (int side = 0; side < 4; side++)
                {
                    int a = inner + side, b = inner + (side + 1) % 4, c = outer + (side + 1) % 4, d = outer + side;
                    // Both windings, so it shows regardless of the eye projection's Y flip.
                    triangles.AddRange(new[] { a, b, c, a, c, d, a, c, b, a, d, c });
                }
            }
            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
        }

        public void Destroy()
        {
            if (go) Object.Destroy(go);
            if (mesh) Object.Destroy(mesh);
            Object.Destroy(material);
            Object.Destroy(keyMaterial);
        }
    }
}
