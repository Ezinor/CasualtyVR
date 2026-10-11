using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CasualtyVR
{
    /// <summary>
    /// Phase 4: real thickness for static world objects (user request 2026-10-04: "drop capsules ... and similar
    /// objects extrude"). Every visible BuildingEntity sprite (drop capsules, crates, doors, lamps, machines)
    /// with its own Collider2D gets a mesh extruded from the sprite's own pixels: the sprite on a front and a back
    /// face, and a side wall along every edge between a solid and a see-through pixel (shaded like the voxel
    /// blocks). The mesh is cached per sprite (doors and openables swap sprites) and follows the transform. The
    /// original sprite is hidden only while an eye renders; the monitor is untouched. Sprites attached to the
    /// object (the capsule's glowing emission panel) are placed on the front face by PopupDepth.
    /// </summary>
    internal sealed class ObjectExtruder
    {
        private sealed class Entry
        {
            public SpriteRenderer Source;
            public GameObject Go;
            public MeshFilter Filter;
            public MeshRenderer Renderer;
            public Sprite Built;
        }

        private static readonly Dictionary<Sprite, Mesh> meshes = new Dictionary<Sprite, Mesh>();
        private static readonly HashSet<Sprite> failed = new HashSet<Sprite>();
        internal static readonly HashSet<SpriteRenderer> Extruded = new HashSet<SpriteRenderer>();

        private readonly GameObject root;
        private readonly Dictionary<SpriteRenderer, Entry> entries = new Dictionary<SpriteRenderer, Entry>();
        private readonly List<SpriteRenderer> hidden = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> stale = new List<SpriteRenderer>();
        private readonly MaterialPropertyBlock props = new MaterialPropertyBlock();
        private int lastScan = -1000;

        public int Count => entries.Count;

        /// <summary>Half the object depth in world units: the front face is at -Half, the back at +Half.</summary>
        public static float Half => Plugin.ObjectDepth.Value * 0.5f * PopupDepth.Scale;
        public static bool On => Plugin.ObjectDepthOn.Value && Plugin.VoxelsOn.Value && PopupDepth.Scale > 0f;

        public ObjectExtruder()
        {
            root = new GameObject("CasualtyVR.Extruded") { hideFlags = HideFlags.DontSave };
            Object.DontDestroyOnLoad(root);
            RenderPipelineManager.beginCameraRendering += OnBegin;
            RenderPipelineManager.endCameraRendering += OnEnd;
        }

        public void Update()
        {
            bool on = On;
            if (root.activeSelf != on) root.SetActive(on);
            if (!on) { if (entries.Count > 0) ClearAll(); return; }
            if (Time.frameCount - lastScan >= 30) Rescan();

            stale.Clear();
            foreach (var kv in entries)
            {
                var src = kv.Key;
                var e = kv.Value;
                if (src == null) { stale.Add(src); continue; }
                bool show = src.enabled && src.gameObject.activeInHierarchy && src.sprite != null;
                if (e.Go.activeSelf != show) e.Go.SetActive(show);
                if (!show) continue;
                if (e.Built != src.sprite)
                {
                    var mesh = MeshFor(src.sprite);
                    if (mesh == null) { stale.Add(src); continue; }
                    e.Filter.sharedMesh = mesh;
                    e.Built = src.sprite;
                }
                var t = src.transform;
                var scale = t.lossyScale;
                scale.x *= src.flipX ? -1f : 1f;
                scale.y *= src.flipY ? -1f : 1f;
                scale.z = PopupDepth.Scale > 0f ? Plugin.ObjectDepth.Value * PopupDepth.Scale : 0.001f;   // mesh depth is 1
                // First person: objects of the background bands (machines set into walls) stand against the back wall.
                float z = FirstPerson.Active && src.sortingOrder <= -100 ? VoxelTerrain.BackZ - Half - 0.02f * PopupDepth.Scale : 0f;
                // First person: objects in the tunnel (plants, crates, doors, capsules) turn to face you, keeping their depth.
                var pos = new Vector3(t.position.x, t.position.y, z);
                Quaternion face = src.sortingOrder > -100 ? FirstPerson.FaceYaw(pos) : Quaternion.identity;
                e.Go.transform.SetPositionAndRotation(pos, face * t.rotation);
                e.Go.transform.localScale = scale;
                if (e.Renderer.sharedMaterial != src.sharedMaterial) e.Renderer.sharedMaterial = src.sharedMaterial;
                e.Renderer.sortingLayerID = src.sortingLayerID;
                e.Renderer.sortingOrder = src.sortingOrder;
                props.Clear();
                props.SetTexture("_MainTex", src.sprite.texture);
                props.SetColor("_RendererColor", src.color);
                e.Renderer.SetPropertyBlock(props);
            }
            foreach (var s in stale) Remove(s);
        }

        private void Rescan()
        {
            lastScan = Time.frameCount;
            foreach (var be in Object.FindObjectsOfType<BuildingEntity>())
            {
                var sr = be.GetComponent<SpriteRenderer>();
                if (sr == null || entries.ContainsKey(sr) || failed.Contains(sr.sprite)) continue;
                if (!sr.isVisible || sr.drawMode != SpriteDrawMode.Simple || sr.sprite == null) continue;
                if (be.GetComponent<Collider2D>() == null) continue;   // never moved by PopupDepth: sits at z=0
                var r = sr.sprite.rect;
                if (r.width * r.height > 256 * 256) continue;           // huge backdrops stay flat
                var go = new GameObject("extruded " + be.name) { layer = VoxelTerrain.Layer, hideFlags = HideFlags.DontSave };
                go.transform.SetParent(root.transform, false);
                var mf = go.AddComponent<MeshFilter>();
                var mr = go.AddComponent<MeshRenderer>();
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.lightProbeUsage = LightProbeUsage.Off;
                mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                entries[sr] = new Entry { Source = sr, Go = go, Filter = mf, Renderer = mr };
                Extruded.Add(sr);
            }
        }

        // ---- mesh from the sprite's pixels ------------------------------------------------------------

        /// <summary>
        /// Mesh in the sprite's local space (units = sprite units, z from -0.5 to 0.5): back face, side walls,
        /// front face, in that order (painter's order; sprite shaders do not write depth).
        /// </summary>
        internal static Mesh MeshFor(Sprite sp)
        {
            if (meshes.TryGetValue(sp, out var cached) && cached != null) return cached;
            if (failed.Contains(sp)) return null;
            var tex = sp.texture;
            Rect tr;
            try { tr = sp.textureRect; }
            catch { failed.Add(sp); return null; }   // tightly packed atlas sprites have no rectangle
            int w = Mathf.RoundToInt(tr.width), h = Mathf.RoundToInt(tr.height);
            if (w <= 0 || h <= 0) { failed.Add(sp); return null; }
            var alpha = ReadAlpha(tex, tr, w, h);

            float ppu = sp.pixelsPerUnit;
            // Pixel (0,0) of the texture rectangle in sprite-local units.
            Vector2 origin = (sp.textureRectOffset - sp.pivot) / ppu;
            float tw = tex.width, th = tex.height;
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var cols = new List<Color>();
            var tris = new List<int>();

            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud, float shade)
            {
                int i = verts.Count;
                verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
                uvs.Add(ua); uvs.Add(ub); uvs.Add(uc); uvs.Add(ud);
                var col = new Color(shade, shade, shade, 1f);
                cols.Add(col); cols.Add(col); cols.Add(col); cols.Add(col);
                tris.Add(i); tris.Add(i + 1); tris.Add(i + 2); tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
            }

            Vector2 Uv(float px, float py) => new Vector2((tr.x + px) / tw, (tr.y + py) / th);
            Vector3 P(float px, float py, float z) => new Vector3(origin.x + px / ppu, origin.y + py / ppu, z);
            bool Solid(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && alpha[y * w + x] > 0.5f;

            // Back face (seen from behind, darker), then the walls, then the front face.
            Quad(P(0, 0, 0.5f), P(w, 0, 0.5f), P(w, h, 0.5f), P(0, h, 0.5f), Uv(0, 0), Uv(w, 0), Uv(w, h), Uv(0, h), 0.6f);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (!Solid(x, y)) continue;
                    var c = Uv(x + 0.5f, y + 0.5f);   // the wall takes the colour of its edge pixel
                    if (!Solid(x - 1, y)) Quad(P(x, y, -0.5f), P(x, y + 1, -0.5f), P(x, y + 1, 0.5f), P(x, y, 0.5f), c, c, c, c, 0.75f);
                    if (!Solid(x + 1, y)) Quad(P(x + 1, y, -0.5f), P(x + 1, y, 0.5f), P(x + 1, y + 1, 0.5f), P(x + 1, y + 1, -0.5f), c, c, c, c, 0.75f);
                    if (!Solid(x, y + 1)) Quad(P(x, y + 1, -0.5f), P(x + 1, y + 1, -0.5f), P(x + 1, y + 1, 0.5f), P(x, y + 1, 0.5f), c, c, c, c, 0.95f);
                    if (!Solid(x, y - 1)) Quad(P(x, y, -0.5f), P(x, y, 0.5f), P(x + 1, y, 0.5f), P(x + 1, y, -0.5f), c, c, c, c, 0.55f);
                }
            Quad(P(0, 0, -0.5f), P(w, 0, -0.5f), P(w, h, -0.5f), P(0, h, -0.5f), Uv(0, 0), Uv(w, 0), Uv(w, h), Uv(0, h), 1f);

            var mesh = new Mesh { name = "CasualtyVR.Extruded " + sp.name, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(cols);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            meshes[sp] = mesh;
            return mesh;
        }

        /// <summary>The sprite's alpha via the GPU (game textures are not CPU-readable).</summary>
        private static float[] ReadAlpha(Texture tex, Rect r, int w, int h)
        {
            var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(tex, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var read = new Texture2D(w, h, TextureFormat.RGBA32, false);
            read.ReadPixels(new Rect(r.x, r.y, w, h), 0, 0);
            read.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            var px = read.GetPixels32();
            Object.Destroy(read);
            var a = new float[w * h];
            for (int i = 0; i < a.Length; i++) a[i] = px[i].a / 255f;
            return a;
        }

        // ---- eye-only swap ------------------------------------------------------------------------------

        private void OnBegin(ScriptableRenderContext ctx, Camera cam)
        {
            if (!On || VrRig.Instance == null || !VrRig.Instance.IsEyeCamera(cam) || (cam.cullingMask & (1 << VoxelTerrain.Layer)) == 0) return;
            foreach (var kv in entries)
            {
                var src = kv.Key;
                if (src == null || src.forceRenderingOff || kv.Value.Built == null || !kv.Value.Go.activeSelf) continue;
                src.forceRenderingOff = true;
                hidden.Add(src);
            }
        }

        private void OnEnd(ScriptableRenderContext ctx, Camera cam)
        {
            foreach (var s in hidden) if (s != null) s.forceRenderingOff = false;
            hidden.Clear();
        }

        private void Remove(SpriteRenderer src)
        {
            if (entries.TryGetValue(src, out var e) && e.Go != null) Object.Destroy(e.Go);
            entries.Remove(src);
            Extruded.Remove(src);
        }

        private void ClearAll()
        {
            OnEnd(default, null);
            foreach (var e in entries.Values) if (e.Go != null) Object.Destroy(e.Go);
            entries.Clear();
            Extruded.Clear();
        }

        public void Destroy()
        {
            RenderPipelineManager.beginCameraRendering -= OnBegin;
            RenderPipelineManager.endCameraRendering -= OnEnd;
            ClearAll();
            Object.Destroy(root);
        }
    }
}
