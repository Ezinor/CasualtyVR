using System.Collections.Generic;
using UnityEngine;

namespace CasualtyVR
{
    /// <summary>A hand's pose in first person (Phase 4b, FIRSTPERSON_REVIEW FP 9b); Own = the character's own hand art.</summary>
    internal enum HandPose { Own, Open, Half, Fist, Point, PointPress }

    /// <summary>
    /// Hand pose art (FP 9b) in the character's own style: its arm sprites are 4 px wide at 8 px per unit in three greys
    /// (0x28, 0x36, 0x4c) that the game's Limb shader tints with the skin colour and lights; its own hand is a 6x4 px
    /// mitten (--cvr-dbg-dumphands). On the arm, each pose is a small voxel hand in those greys (MeshFor), drawn with the
    /// hand's own material: three fingers and a thumb, with claws, like the character. The grab marker on items is flat
    /// pixel art (Icon): a right hand seen from the back, fingers up, thumb on the left, the pivot at the wrist. The first version drew the minigames' 255x628 px hand
    /// drawings on the arm, which looked nothing like it (headset, 1.3.0-test9).
    /// </summary>
    internal static class HandPoses
    {
        public const float PixelsPerUnit = 8f;

        // The marker's art (Icon): the character's hand has three fingers and a thumb, with claws (C).
        private static readonly Dictionary<HandPose, string[]> art = new Dictionary<HandPose, string[]>
        {
            [HandPose.Open] = new[]
            {
                "....C....",
                "..C.L.C..",
                "..L.L.L..",
                "..L.L.L..",
                "C.LMLML..",
                "LDLLLLLD.",
                ".LLLLLLD.",
                "..DMLLD..",
                "...DMMD..",
            },
            [HandPose.Fist] = new[]
            {
                ".LMLML.",
                "DLLLLLD",
                "DLLLLLD",
                "MLLLLLD",
                ".DMLLD.",
                "..DMMD.",
            },
        };

        private static readonly Dictionary<HandPose, Sprite> icons = new Dictionary<HandPose, Sprite>();
        private static readonly Dictionary<HandPose, Mesh> meshes = new Dictionary<HandPose, Mesh>();
        private static Texture2D palette;

        /// <summary>The pose as a light icon (white, a faint 1 px halo around it), drawn additively as the grab marker.</summary>
        public static Sprite Icon(HandPose pose)
        {
            if (icons.TryGetValue(pose, out var s) && s != null) return s;
            if (!art.TryGetValue(pose, out var rows)) return null;
            s = BuildIcon(pose, rows);
            icons[pose] = s;
            return s;
        }

        // ---- the hand on your arm: a small voxel hand --------------------------------------------------------------
        // A flat picture of a hand cannot work on the arm: holding a controller, you see your hand edge-on from the head
        // (thumb on top, palm facing in), so any sprite turned to the eye smears into a slab (headset, 1.3.0-test9). The
        // hand is built from boxes instead, in the hand's own frame, so it reads from every angle:
        //   z along the hand (the wrist at 0, the fingers forward: where the controller points), y the thumb side (up the
        //   handle), x the back of the hand (+) and the palm (-), for the right hand; the left hand is drawn mirrored in x.
        // Units are voxels (VrArms scales them and places the handle in the curled fingers); the faces take the arm's
        // three greys from a small palette, so the hand's own Limb material tints and lights them like the arms.

        private const int Dark = 0, Mid = 1, Light = 2, Claw = 3, Shades = 4;

        /// <summary>The 4x1 palette the voxel hands sample: the arm's greys 0x28, 0x36, 0x4c, and 0x16 for the claws.</summary>
        public static Texture2D Palette
        {
            get
            {
                if (palette != null) return palette;
                palette = new Texture2D(Shades, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "CasualtyVR.HandPalette" };
                palette.SetPixels32(new[] { new Color32(0x28, 0x28, 0x28, 255), new Color32(0x36, 0x36, 0x36, 255), new Color32(0x4c, 0x4c, 0x4c, 255), new Color32(0x16, 0x16, 0x16, 255) });
                palette.Apply();
                return palette;
            }
        }

        /// <summary>The voxel hand for a pose (null for Own).</summary>
        public static Mesh MeshFor(HandPose pose)
        {
            if (pose == HandPose.Own) return null;
            if (meshes.TryGetValue(pose, out var m) && m != null) return m;
            var b = new BoxBuilder();
            // The palm: 4 voxels across the knuckles (y), 3.5 long, 1.5 thick.
            b.Box(-0.75f, 0.75f, -2f, 2f, 0f, 3.5f);
            // Three fingers (the character's), index on top (y), each 1.2 high with small gaps so they read apart; a claw on
            // each fingertip.
            float[] lengths = { 2.8f, 3.1f, 2.6f };   // index, middle, outer
            for (int i = 0; i < 3; i++)
            {
                float y1 = 2f - i * 1.4f, y0 = y1 - 1.2f, cy0 = y0 + 0.3f, cy1 = y1 - 0.3f;
                bool index = i == 0;
                if (pose == HandPose.Open || (index && pose == HandPose.Point))
                {
                    float tip = 3.5f + lengths[i];
                    b.Box(-0.55f, 0.55f, y0, y1, 3.5f, tip);
                    b.Box(-0.5f, 0.1f, cy0, cy1, tip, tip + 0.8f, Claw);        // the claw, hooking towards the palm
                    b.Box(-0.9f, -0.3f, cy0, cy1, tip + 0.5f, tip + 1f, Claw);
                }
                else if (pose == HandPose.Half || (index && pose == HandPose.PointPress))
                {
                    // Bent at the middle joint: forward, then down towards the palm.
                    b.Box(-0.55f, 0.55f, y0, y1, 3.5f, 5f);
                    b.Box(-1.7f, -0.55f, y0, y1, 4.1f, 5f);
                    b.Box(-2.5f, -1.7f, cy0, cy1, 4.2f, 4.8f, Claw);
                }
                else
                {
                    // Curled into the palm: the knuckle row, then the fingertips folded back under it.
                    b.Box(-1.9f, 0.6f, y0, y1, 3.5f, 4.5f);
                    b.Box(-1.9f, -0.9f, y0, y1, 2.4f, 3.5f);
                    b.Box(-1.8f, -1.2f, cy0, cy1, 1.7f, 2.4f, Claw);
                }
            }
            // The thumb, on the palm side of the top edge: along the index finger when open, across the fingers in a fist.
            bool fist = pose == HandPose.Fist || pose == HandPose.Point || pose == HandPose.PointPress;
            if (fist)
            {
                b.Box(-2.3f, -1.3f, 0.6f, 2.4f, 2.6f, 3.8f);
                b.Box(-2.2f, -1.6f, 0f, 0.6f, 2.9f, 3.5f, Claw);
            }
            else
            {
                float end = pose == HandPose.Open ? 3.6f : 3.1f;
                b.Box(-1.25f, -0.25f, 2f, 2.9f, 0.8f, end);
                b.Box(-1.1f, -0.5f, 2.15f, 2.75f, end, end + 0.7f, Claw);
            }
            m = b.ToMesh("CasualtyVR.VoxelHand " + pose);
            meshes[pose] = m;
            return m;
        }

        private sealed class BoxBuilder
        {
            private readonly List<Vector3> v = new List<Vector3>();
            private readonly List<Vector3> n = new List<Vector3>();
            private readonly List<Vector2> uv = new List<Vector2>();
            private readonly List<int> t = new List<int>();

            public void Box(float x0, float x1, float y0, float y1, float z0, float z1, int all = -1)
            {
                // Shading like the game's arms: the top (thumb side) and the back light, the sides mid, underneath dark;
                // claws one shade all round.
                Face(new Vector3(x0, y1, z0), new Vector3(x0, y1, z1), new Vector3(x1, y1, z1), new Vector3(x1, y1, z0), Vector3.up, all >= 0 ? all : Light);
                Face(new Vector3(x0, y0, z0), new Vector3(x1, y0, z0), new Vector3(x1, y0, z1), new Vector3(x0, y0, z1), Vector3.down, all >= 0 ? all : Dark);
                Face(new Vector3(x1, y0, z0), new Vector3(x1, y1, z0), new Vector3(x1, y1, z1), new Vector3(x1, y0, z1), Vector3.right, all >= 0 ? all : Light);
                Face(new Vector3(x0, y0, z0), new Vector3(x0, y0, z1), new Vector3(x0, y1, z1), new Vector3(x0, y1, z0), Vector3.left, all >= 0 ? all : Mid);
                Face(new Vector3(x0, y0, z1), new Vector3(x1, y0, z1), new Vector3(x1, y1, z1), new Vector3(x0, y1, z1), Vector3.forward, all >= 0 ? all : Mid);
                Face(new Vector3(x0, y0, z0), new Vector3(x0, y1, z0), new Vector3(x1, y1, z0), new Vector3(x1, y0, z0), Vector3.back, all >= 0 ? all : Dark);
            }

            private void Face(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, int shade)
            {
                int i = v.Count;
                v.Add(a); v.Add(b); v.Add(c); v.Add(d);
                var p = new Vector2((shade + 0.5f) / Shades, 0.5f);
                for (int k = 0; k < 4; k++) { n.Add(normal); uv.Add(p); }
                // Both windings: the game's sprite materials do not cull, but the left hand is mirrored.
                t.Add(i); t.Add(i + 1); t.Add(i + 2); t.Add(i); t.Add(i + 2); t.Add(i + 3);
                t.Add(i); t.Add(i + 2); t.Add(i + 1); t.Add(i); t.Add(i + 3); t.Add(i + 2);
            }

            public Mesh ToMesh(string name)
            {
                var m = new Mesh { name = name };
                m.SetVertices(v);
                m.SetNormals(n);
                m.SetUVs(0, uv);
                var cols = new Color[v.Count];
                for (int i = 0; i < cols.Length; i++) cols[i] = Color.white;
                m.colors = cols;
                m.SetTriangles(t, 0);
                m.RecalculateBounds();
                return m;
            }
        }

        private static Sprite BuildIcon(HandPose pose, string[] rows)
        {
            const int pad = 1;
            int w = rows[0].Length + 2 * pad, h = rows.Length + 2 * pad;
            var px = new Color32[w * h];
            for (int y = 0; y < rows.Length; y++)
                for (int x = 0; x < rows[y].Length; x++)
                {
                    char c = rows[y][x];
                    if (c == '.') continue;
                    byte g = c == 'D' ? (byte)130 : c == 'M' ? (byte)190 : c == 'L' ? (byte)225 : (byte)255;   // C: claws brightest
                    px[(h - 1 - (y + pad)) * w + x + pad] = new Color32(g, g, g, 255);   // rows are top-down
                }
            // The halo: every empty pixel next to the hand, faint.
            var halo = (Color32[])px.Clone();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (px[y * w + x].a != 0) continue;
                    bool near = false;
                    for (int dy = -1; dy <= 1 && !near; dy++)
                        for (int dx = -1; dx <= 1 && !near; dx++)
                        {
                            int xx = x + dx, yy = y + dy;
                            near = xx >= 0 && yy >= 0 && xx < w && yy < h && px[yy * w + xx].a != 0;
                        }
                    if (near) halo[y * w + x] = new Color32(70, 70, 70, 255);   // drawn additively: brightness, not alpha
                }
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "CasualtyVR.HandIcon " + pose };
            t.SetPixels32(halo);
            t.Apply();
            // Pivot: the wrist, the middle of the bottom row.
            string wristRow = rows[rows.Length - 1];
            float wristX = (wristRow.Length - wristRow.TrimStart('.').Length + wristRow.TrimEnd('.').Length) * 0.5f;
            var s = Sprite.Create(t, new Rect(0, 0, w, h), new Vector2((wristX + pad) / w, (0.5f + pad) / h), PixelsPerUnit, 0, SpriteMeshType.FullRect);
            s.name = "handicon-" + pose;
            return s;
        }

        internal static Texture2D Readable(Texture tex, Rect r)
        {
            var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prev = RenderTexture.active;
            Graphics.Blit(tex, rt);
            RenderTexture.active = rt;
            var t = new Texture2D(Mathf.Max(1, (int)r.width), Mathf.Max(1, (int)r.height), TextureFormat.RGBA32, false);
            t.ReadPixels(new Rect(r.x, r.y, t.width, t.height), 0, 0);
            t.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return t;
        }
    }
}
