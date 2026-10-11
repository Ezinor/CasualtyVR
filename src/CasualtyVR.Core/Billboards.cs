using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace CasualtyVR
{
    /// <summary>
    /// First person (FP-1): items, creatures, other characters and corpses are flat sprites in the play plane, so
    /// looking down a tunnel they are seen edge-on. Each one is turned, as a whole, about its root's vertical axis
    /// to face the player: a creature's limbs, clothes and held items turn together like a cardboard cut-out, so
    /// it reads as the game draws it, in profile.
    ///
    /// The game's transforms are never touched (most carry a Collider2D: PopupDepth.SkipColliders). Copies on the
    /// VR-only layer are drawn instead, with the source's sprite, material (blood and damage), colour and sorting
    /// order, and the originals are hidden while an eye renders (FirstPerson.OnBegin).
    ///
    /// A group is the topmost physics object (Rigidbody2D) or BuildingEntity above a sprite. Not grouped: the
    /// player, terrain, extruded buildings (real 3D already), and what stands on the back wall.
    /// </summary>
    internal sealed class Billboards
    {
        private const float Range = 60f;   // blocks from the player

        private sealed class Group
        {
            public Transform Root;
            public readonly List<SpriteRenderer> Sources = new List<SpriteRenderer>();
            public readonly List<SpriteRenderer> Copies = new List<SpriteRenderer>();
        }

        private readonly GameObject root;
        private readonly Dictionary<Transform, Group> groups = new Dictionary<Transform, Group>();
        private readonly Stack<SpriteRenderer> pool = new Stack<SpriteRenderer>();
        private readonly List<Transform> stale = new List<Transform>();
        private int lastScan = -1000;

        /// <summary>Every source sprite currently drawn as a billboard (hidden in the eyes).</summary>
        public readonly HashSet<SpriteRenderer> Sources = new HashSet<SpriteRenderer>();
        public int GroupCount => groups.Count;

        public Billboards()
        {
            root = new GameObject("CasualtyVR.Billboards") { hideFlags = HideFlags.DontSave };
            Object.DontDestroyOnLoad(root);
        }

        public void SetActive(bool on)
        {
            if (root.activeSelf != on) root.SetActive(on);
            if (!on && groups.Count > 0) Clear();
        }

        public void Update(Vector3 eye, Body player)
        {
            if (Time.frameCount - lastScan >= 30) Rescan(player);
            stale.Clear();
            foreach (var kv in groups)
            {
                var g = kv.Value;
                if (g.Root == null) { stale.Add(kv.Key); continue; }
                Vector3 pivot = g.Root.position;
                Vector3 d = eye - pivot;
                d.y = 0f;
                // Turned so the side the flat game shows (towards -z) faces the eye; yaw only.
                Quaternion r = d.sqrMagnitude < 1e-4f ? Quaternion.identity : Quaternion.FromToRotation(Vector3.back, d.normalized);
                for (int i = 0; i < g.Sources.Count; i++)
                {
                    var src = g.Sources[i];
                    var cp = g.Copies[i];
                    bool show = src != null && src.enabled && src.gameObject.activeInHierarchy && src.sprite != null;
                    if (cp.enabled != show) cp.enabled = show;
                    if (!show) continue;
                    var t = src.transform;
                    cp.transform.SetPositionAndRotation(pivot + r * (t.position - pivot), r * t.rotation);
                    cp.transform.localScale = t.lossyScale;
                    if (cp.sprite != src.sprite) cp.sprite = src.sprite;
                    cp.flipX = src.flipX;
                    cp.flipY = src.flipY;
                    cp.color = src.color;
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

        private static bool IsGroupRoot(Transform t) =>
            t.GetComponent<Rigidbody2D>() != null || t.GetComponent<BuildingEntity>() != null;

        // Terrain chunks and the world grid are never part of a group (they parent wall holes, backgrounds...).
        private static bool IsTerrain(Transform t) =>
            t.GetComponent<Tilemap>() != null || t.GetComponent<TilemapCollider2D>() != null || t.GetComponent<CompositeCollider2D>() != null
            || t.GetComponent<Grid>() != null;

        private void Rescan(Body player)
        {
            lastScan = Time.frameCount;
            var playerT = player != null ? player.transform : null;
            Vector2 center = playerT != null ? (Vector2)playerT.position : Vector2.zero;
            var roots = new HashSet<Transform>();
            void Consider(Transform t)
            {
                if (t == null || (playerT != null && t.IsChildOf(playerT))) return;
                if (((Vector2)t.position - center).sqrMagnitude > Range * Range) return;
                if (IsTerrain(t)) return;
                Transform top = t;
                for (var p = t.parent; p != null && !IsTerrain(p); p = p.parent)
                    if (IsGroupRoot(p)) top = p;
                if (playerT != null && top.IsChildOf(playerT)) return;
                roots.Add(top);
            }
            // Spatial query around the player instead of every physics body in the world (Infested layers have
            // thousands of creature limbs; perf, 2026-10-06).
            foreach (var col in Physics2D.OverlapCircleAll(center, Range))
            {
                if (col.attachedRigidbody != null) Consider(col.attachedRigidbody.transform);
                else if (col.GetComponentInParent<BuildingEntity>() is BuildingEntity be) Consider(be.transform);
            }

            var seen = new HashSet<Transform>();
            foreach (var r in roots)
            {
                var main = r.GetComponent<SpriteRenderer>();
                if (main != null && (ObjectExtruder.Extruded.Contains(main) || FirstPerson.IsBackProxied(main) || main.sortingOrder <= -100)) continue;
                if (r.gameObject.layer == 6 && r.GetComponent<BuildingEntity>() == null) continue;   // Ground layer: terrain
                var srcs = new List<SpriteRenderer>();
                foreach (var sr in r.GetComponentsInChildren<SpriteRenderer>())
                {
                    if (sr.gameObject.layer >= 25 || sr.sortingLayerName != "Default") continue;
                    if (ObjectExtruder.Extruded.Contains(sr) || FirstPerson.IsBackProxied(sr) || FirstPerson.OwnBody.Contains(sr)) continue;
                    srcs.Add(sr);
                }
                if (srcs.Count == 0) continue;
                seen.Add(r);
                if (groups.TryGetValue(r, out var g) && SameSources(g, srcs)) continue;
                if (g != null) Release(r);
                g = new Group { Root = r };
                foreach (var sr in srcs) { g.Sources.Add(sr); g.Copies.Add(Take()); Sources.Add(sr); }
                groups[r] = g;
            }
            stale.Clear();
            foreach (var k in groups.Keys) if (k == null || !seen.Contains(k)) stale.Add(k);
            foreach (var s in stale) Release(s);
        }

        private static bool SameSources(Group g, List<SpriteRenderer> srcs)
        {
            if (g.Sources.Count != srcs.Count) return false;
            for (int i = 0; i < srcs.Count; i++) if (g.Sources[i] != srcs[i]) return false;
            return true;
        }

        private SpriteRenderer Take()
        {
            if (pool.Count > 0)
            {
                var p = pool.Pop();
                p.gameObject.SetActive(true);
                return p;
            }
            var go = new GameObject("billboard") { layer = VoxelTerrain.Layer, hideFlags = HideFlags.DontSave };
            go.transform.SetParent(root.transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            sr.receiveShadows = false;
            return sr;
        }

        private void Release(Transform key)
        {
            if (!groups.TryGetValue(key, out var g)) return;
            foreach (var s in g.Sources) Sources.Remove(s);
            foreach (var cp in g.Copies)
            {
                if (cp == null) continue;
                cp.sprite = null;
                cp.sharedMaterial = null;   // do not keep the game's per-limb materials alive
                cp.gameObject.SetActive(false);
                pool.Push(cp);
            }
            groups.Remove(key);
        }

        public void Clear()
        {
            stale.Clear();
            stale.AddRange(groups.Keys);
            foreach (var s in stale) Release(s);
            groups.Clear();
            Sources.Clear();
        }

        public void Destroy()
        {
            Clear();
            pool.Clear();
            Object.Destroy(root);
        }
    }
}
