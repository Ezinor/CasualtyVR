using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace CasualtyVR
{
    /// <summary>
    /// Sound at the head (docs/BUILD_PLAN.md Phase 5, FIRSTPERSON_REVIEW FP 10a = IMPROVEMENT_PLAN 10; playbook AUDIO-001,
    /// FAIL-AUDIO-001, FAIL-AUDIO-004). The game hears through an AudioListener on its orthographic camera, which never
    /// turns and sits a fixed distance off the play plane: in first person a sound ahead of you was heard from wherever
    /// the flat camera put it, and turning your head changed nothing.
    ///
    /// In first person a listener of the mod's own follows the VR head (position and rotation) and the game's listeners
    /// are muted. Moving the ears into the play plane would make every 3D sound louder (FAIL-AUDIO-004): the game's mix
    /// was set with its camera Z blocks away, so with log rolloff a sound at your feet was at 1/Z gain. Each 3D source's
    /// rolloff is therefore rebuilt as a custom curve with the flat game's loudness at the same distance along the
    /// plane: gain(d) = flatGain(sqrt(d^2 + Z^2)). Direction comes from your head; loudness stays the game's. 2D sounds
    /// (music, UI, spatialBlend 0) are untouched (AUDIO-002). Everything is put back when first person or VR ends.
    /// </summary>
    internal sealed class Ears
    {
        private readonly GameObject go;
        private readonly AudioListener listener;
        private readonly List<AudioListener> muted = new List<AudioListener>();
        private float flatZ = -1f;   // the game listener's distance from the play plane, in blocks
        private int lastScan = -1000;
        private bool on;

        // Sources whose rolloff was rebuilt, with what to put back.
        private struct Saved { public AudioRolloffMode Mode; public AnimationCurve Curve; }
        private static readonly Dictionary<AudioSource, Saved> changed = new Dictionary<AudioSource, Saved>();
        private static readonly Dictionary<(AudioRolloffMode, float, float, float), AnimationCurve> curves = new Dictionary<(AudioRolloffMode, float, float, float), AnimationCurve>();
        private static Ears instance;

        /// <summary>Whether the ears are at the head now (first person, the setting on).</summary>
        public static bool AtHead => instance != null && instance.on;

        public Ears()
        {
            go = new GameObject("CasualtyVR.Ears") { hideFlags = HideFlags.DontSave };
            Object.DontDestroyOnLoad(go);
            listener = go.AddComponent<AudioListener>();
            listener.enabled = false;
            instance = this;
        }

        public void Update(bool firstPerson)
        {
            bool want = firstPerson && Plugin.FpSoundAtHead.Value;
            if (!want) { if (on) Release("first person off"); return; }
            if (!on || Scans.Due(lastScan, 120, 16))
            {
                long t0 = Scans.Start();
                lastScan = Time.frameCount;
                TakeOver();
                Prune();
                foreach (var src in Object.FindObjectsOfType<AudioSource>()) Adjust(src);
                Scans.Stop("ears", t0);
            }
            go.transform.SetPositionAndRotation(VrSpace.HeadPosition, VrSpace.Rot(VrSpace.HeadTrackingRot));
            if (DbgSound && Time.unscaledTime >= nextDbg) DebugSound();
        }

        // Mute the game's listeners (every enabled one but ours) and switch ours on; a scene change brings new ones.
        private void TakeOver()
        {
            foreach (var l in Object.FindObjectsOfType<AudioListener>())
            {
                if (l == listener || !l.enabled) continue;
                if (flatZ < 0f) flatZ = Mathf.Max(1f, Mathf.Abs(l.transform.position.z));
                l.enabled = false;
                muted.Add(l);
                Plugin.Log.LogInfo($"Ears: the game's listener on '{l.gameObject.name}' is off; the ears are at your head (the flat game heard from {flatZ:F1} blocks off the plane)");
            }
            if (flatZ < 0f) flatZ = 10f;
            if (!listener.enabled) listener.enabled = true;
            on = true;
        }

        public void Release(string why)
        {
            if (!on && muted.Count == 0) return;
            listener.enabled = false;
            foreach (var l in muted) if (l != null) l.enabled = true;
            muted.Clear();
            int n = 0;
            foreach (var kv in changed)
            {
                if (kv.Key == null) continue;
                kv.Key.rolloffMode = kv.Value.Mode;
                if (kv.Value.Mode == AudioRolloffMode.Custom && kv.Value.Curve != null) kv.Key.SetCustomCurve(AudioSourceCurveType.CustomRolloff, kv.Value.Curve);
                n++;
            }
            changed.Clear();
            on = false;
            Plugin.Log.LogInfo($"Ears: back at the game's listener ({why}); {n} sounds' falloff restored");
        }

        public void Destroy()
        {
            Release("VR stopped");
            if (instance == this) instance = null;
            Object.Destroy(go);
        }

        private static readonly List<AudioSource> dead = new List<AudioSource>();

        // The game's one-shot sounds destroy themselves once played.
        private static void Prune()
        {
            dead.Clear();
            foreach (var k in changed.Keys) if (k == null) dead.Add(k);
            foreach (var k in dead) changed.Remove(k);
        }

        /// <summary>A 3D source keeps the flat game's loudness with the ears in the plane (see the class summary).</summary>
        private static void Adjust(AudioSource src)
        {
            var self = instance;
            if (self == null || !self.on || src == null || src.spatialBlend < 0.5f || changed.ContainsKey(src)) return;
            var saved = new Saved { Mode = src.rolloffMode, Curve = src.rolloffMode == AudioRolloffMode.Custom ? src.GetCustomCurve(AudioSourceCurveType.CustomRolloff) : null };
            float min = src.minDistance, max = Mathf.Max(src.maxDistance, min + 0.01f), z = self.flatZ;
            AnimationCurve curve;
            if (saved.Mode == AudioRolloffMode.Custom)
                curve = Rebuild(d => saved.Curve.Evaluate(Mathf.Sqrt(d * d + z * z) / max), max);
            else
            {
                var key = (saved.Mode, min, max, z);
                if (!curves.TryGetValue(key, out curve))
                {
                    curve = Rebuild(d => FlatGain(saved.Mode, min, max, Mathf.Sqrt(d * d + z * z)), max);
                    curves[key] = curve;
                }
            }
            changed[src] = saved;
            src.rolloffMode = AudioRolloffMode.Custom;
            src.SetCustomCurve(AudioSourceCurveType.CustomRolloff, curve);
        }

        /// <summary>Unity's built-in rolloffs at a distance (blocks).</summary>
        private static float FlatGain(AudioRolloffMode mode, float min, float max, float d)
        {
            if (d <= min) return 1f;
            if (mode == AudioRolloffMode.Linear) return Mathf.Clamp01((max - d) / Mathf.Max(1e-3f, max - min));
            return min / Mathf.Min(d, max);   // logarithmic: no further fall beyond the max distance
        }

        // A custom rolloff curve over distance / maxDistance, sampled densely near the listener.
        private static AnimationCurve Rebuild(System.Func<float, float> gainAt, float max)
        {
            const int n = 40;
            var keys = new Keyframe[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / (n - 1);
                t *= t;
                keys[i] = new Keyframe(t, gainAt(t * max));
            }
            var c = new AnimationCurve(keys);
            for (int i = 0; i < n; i++) c.SmoothTangents(i, 0f);
            return c;
        }

        // New sounds from the game's own Sound.Play are adjusted as they start, not at the next scan.
        [HarmonyPatch(typeof(Sound), nameof(Sound.Play), new[] { typeof(AudioClip), typeof(Vector2), typeof(bool), typeof(bool), typeof(Transform), typeof(float), typeof(float), typeof(bool), typeof(bool) })]
        private static class SoundPlayPatch
        {
            private static void Postfix(AudioSource __result) { if (__result != null && AtHead) Adjust(__result); }
        }

        // --cvr-dbg-sound (tools/fp-comfort.ps1): every 2 s a quiet sound 3 blocks ahead of the character's facing at the
        // start, logged in the ears' frame (x right, z ahead) with its loudness now and in the flat game.
        private static readonly bool DbgSound = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "--cvr-dbg-sound") >= 0;
        private float nextDbg;
        private Vector2 dbgAt;

        private void DebugSound()
        {
            nextDbg = Time.unscaledTime + 2f;
            var body = PlayerCamera.main != null ? PlayerCamera.main.body : null;
            if (body == null) return;
            // Placed once near the character (again after a teleport, e.g. --cvr-dbg-cave's), then fixed in the world.
            if (dbgAt == Vector2.zero || Vector2.Distance(dbgAt, body.transform.position) > 8f)
                dbgAt = (Vector2)body.transform.position + new Vector2(body.isRight ? 3f : -3f, 1f);
            var src = Sound.Play("dogshake", dbgAt, volume: 0.02f);
            if (src == null) return;
            Vector3 local = go.transform.InverseTransformPoint(src.transform.position);
            float d = Vector3.Distance(go.transform.position, src.transform.position);
            float now = src.rolloffMode == AudioRolloffMode.Custom ? src.GetCustomCurve(AudioSourceCurveType.CustomRolloff).Evaluate(d / src.maxDistance) : FlatGain(src.rolloffMode, src.minDistance, src.maxDistance, d);
            float flat = changed.TryGetValue(src, out var s) ? FlatGain(s.Mode, src.minDistance, src.maxDistance, Mathf.Sqrt(d * d + flatZ * flatZ)) : now;
            Plugin.Log.LogInfo($"Ears test: sound at ears-local ({local.x:F2}, {local.y:F2}, {local.z:F2}) {d:F1} blocks; gain {now:F3} (flat game {flat:F3}); rolloff {src.rolloffMode}");
        }
    }
}
