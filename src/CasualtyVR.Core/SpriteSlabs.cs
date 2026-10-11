using System.Collections.Generic;
using UnityEngine;

namespace CasualtyVR
{
    /// <summary>
    /// Phase 4: thickness for characters and items ("sprite slabs"). Each sprite that carries its own
    /// Collider2D (limbs, items, creatures, crates and other interactables: the actor plane at z=0, which
    /// PopupDepth never moves) gets a few
    /// copies stacked behind it, slightly darker, so from an angle it reads as a solid cut-out instead of a
    /// paper sheet. The copies live in the VR-only layer (VoxelTerrain.Layer), outside the game's hierarchy,
    /// and reuse the source's material (the limb material carries blood, wetness and damage).
    ///
    /// Copies sit only behind the source: sprites do not write depth, so a copy in front would paint over
    /// clothing and overlays drawn at the source's z. Same sorting order as the source; equal orders sort
    /// back to front by distance for perspective cameras, so the stack draws behind its source.
    /// </summary>
    internal sealed class SpriteSlabs
    {
        private const int MaxSources = 600;
        private const int RescanFrames = 30;

        private sealed class Slab
        {
            public SpriteRenderer Source;
            public SpriteRenderer[] Copies;
        }

        private readonly GameObject root;
        private readonly Dictionary<SpriteRenderer, Slab> slabs = new Dictionary<SpriteRenderer, Slab>();
        private readonly Stack<SpriteRenderer> pool = new Stack<SpriteRenderer>();
        private readonly List<SpriteRenderer> stale = new List<SpriteRenderer>();
        private int lastScan = -1000;
        private int layers;

        public int SourceCount => slabs.Count;

        public SpriteSlabs()
        {
            root = new GameObject("CasualtyVR.Slabs") { hideFlags = HideFlags.DontSave };
            Object.DontDestroyOnLoad(root);
        }

        /// <summary>Once per frame, after the game has animated (LateUpdate) and before the eyes render.</summary>
        public void Update()
        {
            bool on = Plugin.SlabsOn.Value && Plugin.VoxelsOn.Value && PopupDepth.Scale > 0f && !(FirstPerson.Active && Plugin.FpBillboards.Value);
            if (root.activeSelf != on) root.SetActive(on);
            if (!on) return;

            int wanted = Mathf.Clamp(Plugin.SlabLayers.Value, 1, 8);
            if (wanted != layers) { ClearAll(); layers = wanted; }
            if (Time.frameCount - lastScan >= RescanFrames) Rescan();

            float step = Plugin.SlabThickness.Value * PopupDepth.Scale / layers;
            stale.Clear();
            foreach (var kv in slabs)
            {
                var src = kv.Key;
                if (src == null) { stale.Add(src); continue; }
                bool show = src.enabled && src.gameObject.activeInHierarchy && src.isVisible && src.sprite != null
                            && !(FirstPerson.Active && (FirstPerson.OwnBody.Contains(src) || FirstPerson.IsBackProxied(src)));
                var t = src.transform;
                Vector3 p = t.position;
                Quaternion q = t.rotation;
                Vector3 s = t.lossyScale;
                Color c = src.color;
                Color dark = new Color(c.r * 0.7f, c.g * 0.7f, c.b * 0.7f, c.a);
                var copies = kv.Value.Copies;
                for (int i = 0; i < copies.Length; i++)
                {
                    var cp = copies[i];
                    if (cp.enabled != show) cp.enabled = show;
                    if (!show) continue;
                    cp.transform.SetPositionAndRotation(new Vector3(p.x, p.y, p.z + step * (i + 1)), q);
                    cp.transform.localScale = s;
                    if (cp.sprite != src.sprite) cp.sprite = src.sprite;
                    cp.flipX = src.flipX;
                    cp.flipY = src.flipY;
                    cp.color = dark;
                    if (cp.sharedMaterial != src.sharedMaterial) cp.sharedMaterial = src.sharedMaterial;
                    cp.sortingLayerID = src.sortingLayerID;
                    cp.sortingOrder = src.sortingOrder;
                    cp.maskInteraction = src.maskInteraction;
                    if (cp.drawMode != src.drawMode) cp.drawMode = src.drawMode;
                    if (src.drawMode != SpriteDrawMode.Simple) cp.size = src.size;
                }
            }
            foreach (var s in stale) Release(s);
        }

        private bool diagnosed;

        // One-shot log of the interactables in view and whether each qualifies (crates looked flat, 2026-10-04).
        private void Diagnose()
        {
            diagnosed = true;
            int n = 0;
            foreach (var be in Object.FindObjectsOfType<BuildingEntity>())
            {
                var sr = be.GetComponent<SpriteRenderer>();
                var col = be.GetComponent<Collider2D>();
                if (sr == null || !sr.isVisible) continue;
                if (n++ >= 15) break;
                Plugin.Log.LogInfo($"Slabs: building '{be.name}' layer {be.gameObject.layer} sorting {sr.sortingLayerName}/{sr.sortingOrder} " +
                                   $"collider {(col == null ? "none" : col.isTrigger ? "trigger" : "solid")} childColliders {be.GetComponentsInChildren<Collider2D>().Length} slabbed {slabs.ContainsKey(sr)}");
            }
        }

        private void Rescan()
        {
            lastScan = Time.frameCount;
            var found = new HashSet<SpriteRenderer>();
            foreach (var sr in Object.FindObjectsOfType<SpriteRenderer>())
            {
                if (found.Count >= MaxSources) break;
                var go = sr.gameObject;
                int layer = go.layer;
                if (layer == VoxelTerrain.Layer || layer >= 25) continue;
                // Ground (6) holds the terrain, but also crates, doors and lamps built into it (BuildingEntity).
                if (layer == 6 && !go.TryGetComponent<BuildingEntity>(out _)) continue;
                if (!sr.isVisible || sr.sortingLayerName != "Default") continue;
                // Own Collider2D: on the actor plane, never moved by PopupDepth (see PopupDepth.SkipColliders).
                var col = go.GetComponent<Collider2D>();
                if (col == null) continue;
                if (ObjectExtruder.Extruded.Contains(sr)) continue;   // has a real extruded mesh instead
                // Trigger colliders count only on what the game itself lets you hover and right-click
                // (PlayerCamera.HandleWorldHover: Item, BuildingEntity: crates, furniture, machines), so
                // decorative trigger volumes stay flat.
                if (col.isTrigger && !go.TryGetComponent<Item>(out _) && !go.TryGetComponent<BuildingEntity>(out _)) continue;
                found.Add(sr);
            }
            stale.Clear();
            foreach (var src in slabs.Keys) if (src == null || !found.Contains(src)) stale.Add(src);
            foreach (var s in stale) Release(s);
            foreach (var sr in found)
            {
                if (slabs.ContainsKey(sr)) continue;
                var copies = new SpriteRenderer[layers];
                for (int i = 0; i < layers; i++) copies[i] = Take();
                slabs[sr] = new Slab { Source = sr, Copies = copies };
            }
            if (Plugin.Verbose && !diagnosed && Time.timeSinceLevelLoad > 15f) Diagnose();
        }

        private SpriteRenderer Take()
        {
            if (pool.Count > 0)
            {
                var r = pool.Pop();
                r.gameObject.SetActive(true);
                return r;
            }
            var go = new GameObject("slab") { layer = VoxelTerrain.Layer, hideFlags = HideFlags.DontSave };
            go.transform.SetParent(root.transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            sr.receiveShadows = false;
            return sr;
        }

        private void Release(SpriteRenderer src)
        {
            if (!slabs.TryGetValue(src, out var slab)) return;
            foreach (var cp in slab.Copies)
            {
                if (cp == null) continue;
                cp.sprite = null;
                cp.sharedMaterial = null;   // do not keep the game's per-limb materials alive
                cp.gameObject.SetActive(false);
                pool.Push(cp);
            }
            slabs.Remove(src);
        }

        private void ClearAll()
        {
            stale.Clear();
            stale.AddRange(slabs.Keys);
            foreach (var s in stale) Release(s);
            slabs.Clear();
        }

        public void Destroy()
        {
            slabs.Clear();
            pool.Clear();
            Object.Destroy(root);
        }
    }
}
