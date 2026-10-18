using System.Collections.Generic;
using UnityEngine;

namespace CasualtyVR
{
    /// <summary>
    /// First person (FP-2): the character's own arms, following the controllers. Each arm is the game's three arm
    /// limbs (upper arm, forearm, hand; Body.limbs 3-5 front, 6-8 back), drawn as copies on the VR-only layer with
    /// everything they carry: their materials (blood, wounds, bandages), child sprites (clothing) and the held
    /// item (hand slots are children of the hand limbs). The game's own ragdoll arms are never moved.
    ///
    /// * Joint points come from the game's hinge joints: a limb's joint.anchor is its proximal joint in its own
    ///   space, its child's joint.connectedAnchor the distal one (shoulder -> elbow -> wrist), so bone lengths are
    ///   the character's (about 1.0 and 1.4 blocks, plus the hand).
    /// * Two-bone IK from a shoulder below and beside the head to the controller, the elbow bending down and out
    ///   (pole built from the head's frame, playbook HAND-005/006); the hand points along the controller.
    /// * Each segment turns about its own bone to face the eye (the art is flat), and the copies draw after the
    ///   world (sorting order 30000+) while still depth-tested against the blocks.
    /// * The right controller is the character's active hand (Body.handSlot); switching hands swaps the arms.
    /// </summary>
    internal sealed class VrArms
    {
        private sealed class Segment
        {
            public Limb Limb;
            public Vector2 A, B;   // proximal and distal joint, limb-local
            public SpriteRenderer Own;
            public readonly List<(SpriteRenderer src, Block copy)> Parts = new List<(SpriteRenderer, Block)>();
        }

        // One arm piece as a pixel-extruded block (user, 2026-10-05: "add thickness/blockiness to the arms"),
        // like the body (BodyView) and the crates (ObjectExtruder); a flat quad if the sprite cannot be extruded.
        private sealed class Block
        {
            public GameObject Go;
            public MeshFilter Filter;
            public MeshRenderer Renderer;
            public Sprite Built;
            public Mesh Flat;
            public bool enabled
            {
                get => Go.activeSelf;
                set { if (Go.activeSelf != value) Go.SetActive(value); }
            }
            public Transform transform => Go.transform;
        }

        private static readonly MaterialPropertyBlock props = new MaterialPropertyBlock();

        private sealed class Arm
        {
            public readonly Segment[] Segs = new Segment[3];
            public float Upper, Fore, Hand;
        }

        private readonly GameObject root;
        private readonly Stack<Block> pool = new Stack<Block>();
        private Arm front, back;
        private Body built;
        private int lastScan = -1000;
        public bool Built => front != null;

        public VrArms()
        {
            root = new GameObject("CasualtyVR.Arms") { hideFlags = HideFlags.DontSave };
            Object.DontDestroyOnLoad(root);
        }

        public void SetActive(bool on)
        {
            if (root.activeSelf != on) root.SetActive(on);
        }

        public void Update(Body body)
        {
            if (body == null || body.limbs == null || body.limbs.Length < 9) { SetActive(false); return; }
            if (body != built || Scans.Due(lastScan, 30, 14)) { long t0 = Scans.Start(); Rebuild(body); Scans.Stop("arms", t0); }
            if (front == null) return;

            // Right controller = the character's active hand.
            bool mainIsFront = true;
            if (body.slots != null && body.handSlot >= 0 && body.handSlot < body.slots.Length && body.slots[body.handSlot] != null && body.slots[body.handSlot].isHand)
                mainIsFront = body.slots[body.handSlot].limb == body.limbs[5];
            Pose(mainIsFront ? front : back, 1, +1f);
            Pose(mainIsFront ? back : front, 0, -1f);
        }

        private void Pose(Arm arm, int hand, float side)
        {
            bool valid = VrInput.HandPose(hand, out Vector3 aimPos, out Vector3 aimFwd, out _, out _);
            foreach (var seg in arm.Segs) foreach (var (_, cp) in seg.Parts) if (cp.enabled != valid) cp.enabled = valid;
            if (!valid) return;

            float s = VrSpace.Scale;
            Vector3 head = VrSpace.HeadPosition;
            Vector3 fwd = VrSpace.HeadForward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.right;
            fwd.Normalize();
            Vector3 up = Vector3.up;
            Vector3 right = Vector3.Cross(up, fwd);
            Vector3 shoulder = head + (-0.24f * up + side * 0.19f * right - 0.04f * fwd) * s;

            // The wrist sits behind the controller's aim point, the hand points along the aim.
            Vector3 wristTarget = aimPos - aimFwd * (arm.Hand * 0.5f);
            float a = arm.Upper, b = arm.Fore;
            Vector3 toT = wristTarget - shoulder;
            float d = Mathf.Clamp(toT.magnitude, Mathf.Abs(a - b) + 1e-3f, a + b - 1e-3f);
            Vector3 dir = toT.sqrMagnitude > 1e-8f ? toT.normalized : fwd;
            Vector3 wrist = shoulder + dir * d;
            // Elbow: bends down and out, a little back (pole from the head frame).
            Vector3 pole = (-up + side * 0.6f * right - 0.3f * fwd).normalized;
            Vector3 perp = Vector3.ProjectOnPlane(pole, dir);
            if (perp.sqrMagnitude < 1e-6f) perp = Vector3.ProjectOnPlane(-up, dir);
            perp.Normalize();
            float cosA = Mathf.Clamp((a * a + d * d - b * b) / (2f * a * d), -1f, 1f);
            Vector3 elbow = shoulder + dir * (a * cosA) + perp * (a * Mathf.Sqrt(1f - cosA * cosA));
            Vector3 handEnd = wrist + aimFwd * arm.Hand;

            Place(arm.Segs[0], shoulder, elbow, head);
            Place(arm.Segs[1], elbow, wrist, head);
            Place(arm.Segs[2], wrist, handEnd, head);
        }

        // Map the segment's limb space so A lands on p, the bone A->B points along p->q, and the sprite faces the eye.
        private static void Place(Segment seg, Vector3 p, Vector3 q, Vector3 eye)
        {
            var lt = seg.Limb.transform;
            Vector3 sc = lt.lossyScale;
            sc = new Vector3(Mathf.Abs(sc.x), Mathf.Abs(sc.y), 1f);
            Vector3 boneLocal = Vector3.Scale(new Vector3(seg.B.x - seg.A.x, seg.B.y - seg.A.y, 0f), sc);
            if (boneLocal.sqrMagnitude < 1e-8f) boneLocal = Vector3.down;
            Vector3 u = q - p;
            if (u.sqrMagnitude < 1e-8f) u = Vector3.down;
            u.Normalize();
            Vector3 n = Vector3.ProjectOnPlane(eye - (p + q) * 0.5f, u);
            if (n.sqrMagnitude < 1e-8f) n = Vector3.ProjectOnPlane(Vector3.back, u);
            n.Normalize();
            // Local frame: -z (the side the flat game shows) towards the eye, bone along the art's own axis.
            Quaternion world = Quaternion.LookRotation(-n, u);
            Quaternion local = Quaternion.LookRotation(Vector3.forward, boneLocal.normalized);
            Quaternion r = world * Quaternion.Inverse(local);
            Matrix4x4 m = Matrix4x4.TRS(p, r, Vector3.one) * Matrix4x4.Scale(sc) * Matrix4x4.Translate(-(Vector3)seg.A);
            Matrix4x4 limbInv = lt.worldToLocalMatrix;
            foreach (var (src, cp) in seg.Parts)
            {
                if (src == null) continue;
                bool show = src.enabled && src.gameObject.activeInHierarchy && src.sprite != null;
                if (cp.enabled != show) cp.enabled = show;
                if (!show) continue;
                Matrix4x4 w = m * (limbInv * src.transform.localToWorldMatrix);
                if (cp.Built != src.sprite)
                {
                    var mesh = ObjectExtruder.MeshFor(src.sprite);
                    if (mesh == null) { if (cp.Flat != null) Object.Destroy(cp.Flat); mesh = cp.Flat = FlatMesh(src.sprite); }
                    cp.Filter.sharedMesh = mesh;
                    cp.Built = src.sprite;
                }
                var scale = w.lossyScale;
                scale.x *= src.flipX ? -1f : 1f;
                scale.y *= src.flipY ? -1f : 1f;
                // Limbs about 0.4 blocks thick (13 cm), what they carry a little proud of them.
                scale.z = Mathf.Max(0.01f, (src == seg.Own ? 0.42f : 0.5f) * Plugin.FpArmThickness.Value);
                cp.transform.SetPositionAndRotation(w.GetColumn(3), w.rotation);
                cp.transform.localScale = scale;
                if (cp.Renderer.sharedMaterial != src.sharedMaterial) cp.Renderer.sharedMaterial = src.sharedMaterial;
                cp.Renderer.sortingLayerID = src.sortingLayerID;
                cp.Renderer.sortingOrder = 30000 + Mathf.Clamp(src.sortingOrder, -2000, 2000);
                props.Clear();
                props.SetTexture("_MainTex", src.sprite.texture);
                props.SetColor("_RendererColor", src.color);
                cp.Renderer.SetPropertyBlock(props);
            }
        }

        private void Rebuild(Body body)
        {
            lastScan = Time.frameCount;
            if (body != built) { Clear(); built = body; }
            front = Build(front, body.limbs[3], body.limbs[4], body.limbs[5]);
            back = Build(back, body.limbs[6], body.limbs[7], body.limbs[8]);
        }

        private Arm Build(Arm arm, Limb upper, Limb fore, Limb hand)
        {
            if (upper == null || fore == null || hand == null || fore.joint == null || hand.joint == null) return arm;
            arm = arm ?? new Arm();
            // Joint points (limb-local): shoulder = upper's own anchor, elbow = fore's connectedAnchor (in upper) and
            // fore's anchor, wrist = hand's connectedAnchor (in fore) and hand's anchor; the hand's far end is past
            // its sprite centre, the same distance again.
            Vector2 shoulder = upper.joint != null ? upper.joint.anchor : Vector2.zero;
            var hs = hand.GetComponent<SpriteRenderer>();
            Vector2 handCentre = hs != null && hs.sprite != null ? (Vector2)hs.sprite.bounds.center : Vector2.zero;
            Vector2 handTip = hand.joint.anchor + (handCentre - hand.joint.anchor) * 2f;
            if ((handTip - hand.joint.anchor).sqrMagnitude < 0.01f) handTip = hand.joint.anchor + Vector2.down * 0.4f;
            Sync(ref arm.Segs[0], upper, shoulder, fore.joint.connectedAnchor);
            Sync(ref arm.Segs[1], fore, fore.joint.anchor, hand.joint.connectedAnchor);
            Sync(ref arm.Segs[2], hand, hand.joint.anchor, handTip);
            arm.Upper = Length(arm.Segs[0]);
            arm.Fore = Length(arm.Segs[1]);
            arm.Hand = Length(arm.Segs[2]);
            return arm;
        }

        private static float Length(Segment s)
        {
            var sc = s.Limb.transform.lossyScale;
            return Vector2.Scale(s.B - s.A, new Vector2(Mathf.Abs(sc.x), Mathf.Abs(sc.y))).magnitude;
        }

        // (Re)collect a segment's sprites: the limb and everything under it (clothing, the held item).
        private void Sync(ref Segment seg, Limb limb, Vector2 a, Vector2 b)
        {
            if (seg == null || seg.Limb != limb) { Release(seg); seg = new Segment { Limb = limb }; }
            seg.A = a;
            seg.B = b;
            seg.Own = limb.GetComponent<SpriteRenderer>();
            var srcs = limb.GetComponentsInChildren<SpriteRenderer>();
            bool same = srcs.Length == seg.Parts.Count;
            for (int i = 0; same && i < srcs.Length; i++) same = seg.Parts[i].src == srcs[i];
            if (same) return;
            foreach (var (_, cp) in seg.Parts) Return(cp);
            seg.Parts.Clear();
            foreach (var sr in srcs) seg.Parts.Add((sr, Take()));
        }

        private Block Take()
        {
            if (pool.Count > 0) { var p = pool.Pop(); p.enabled = true; return p; }
            var go = new GameObject("arm part") { layer = VoxelTerrain.Layer, hideFlags = HideFlags.DontSave };
            go.transform.SetParent(root.transform, false);
            var b = new Block { Go = go, Filter = go.AddComponent<MeshFilter>(), Renderer = go.AddComponent<MeshRenderer>() };
            b.Renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            b.Renderer.receiveShadows = false;
            b.Renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            b.Renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            return b;
        }

        private void Return(Block cp)
        {
            if (cp == null || cp.Go == null) return;
            cp.Built = null;
            cp.Filter.sharedMesh = null;
            cp.Renderer.sharedMaterial = null;   // do not keep the game's per-limb materials alive
            cp.enabled = false;
            pool.Push(cp);
        }

        // A sprite's own outline as a flat mesh (z = 0), for sprites the extruder cannot read.
        private static Mesh FlatMesh(Sprite sp)
        {
            var m = new Mesh { name = "CasualtyVR.FlatArm " + sp.name };
            var v = sp.vertices;
            var verts = new Vector3[v.Length];
            for (int i = 0; i < v.Length; i++) verts[i] = v[i];
            m.vertices = verts;
            m.uv = sp.uv;
            var t = sp.triangles;
            var tris = new int[t.Length * 2];
            for (int i = 0; i < t.Length; i += 3)
            {
                tris[i] = t[i]; tris[i + 1] = t[i + 1]; tris[i + 2] = t[i + 2];
                tris[t.Length + i] = t[i]; tris[t.Length + i + 1] = t[i + 2]; tris[t.Length + i + 2] = t[i + 1];
            }
            m.triangles = tris;
            var cols = new Color[v.Length];
            for (int i = 0; i < cols.Length; i++) cols[i] = Color.white;
            m.colors = cols;
            m.RecalculateBounds();
            return m;
        }

        private void Release(Segment seg)
        {
            if (seg == null) return;
            foreach (var (_, cp) in seg.Parts) Return(cp);
            seg.Parts.Clear();
        }

        public void Clear()
        {
            foreach (var arm in new[] { front, back })
                if (arm != null) foreach (var s in arm.Segs) Release(s);
            front = back = null;
            built = null;
        }

        public void Destroy()
        {
            Clear();
            pool.Clear();
            Object.Destroy(root);
        }
    }
}
