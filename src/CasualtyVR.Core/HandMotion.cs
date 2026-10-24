using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace CasualtyVR
{
    /// <summary>
    /// The swing and throw recipes for one hand (docs/BUILD_PLAN.md Phase 4a, FIRSTPERSON_REVIEW FP 9d/9e), kept free of
    /// the game so live play (VrGrab) and recorded motion takes (MotionTakes, TEST-027) run the same code. Fed the hand's
    /// grip pose in room space with the caller's own clock (playbook ch. 02: never the runtime's predicted display time).
    ///
    /// Swing (FEAR VR's gesture rules, adapted): fast (2.6 m/s over the last ~3 frames), not a retraction (moving back
    /// towards the shoulder), re-armed only once the hand has slowed below 0.8 m/s (one swing, one attack, however long),
    /// samples more than 100 ms apart start over (a hitch is a teleport, not a swing). FEAR thresholds the forward axis
    /// because its melee is a thrust; this game's attack is a swing towards the mouse, so the swing's own direction is
    /// what counts. The game's attack cooldown and the held item's gate are the caller's (VrGrab).
    ///
    /// Throw (Quake VR's throwing review): release when the analog grip falls 30 % below its peak since the grab (the
    /// boolean squeeze fires late, near full release); the velocity is the peak over the last 150 ms (a running average
    /// decays exactly at release, so throws dropped short), frozen at the release edge with the hand's position.
    /// </summary>
    internal sealed class HandMotion
    {
        public const float SwingSpeed = 2.6f, RearmSpeed = 0.8f, MaxGap = 0.1f, PeakWindow = 0.15f, ReleaseDrop = 0.3f;

        private readonly Vector3[] pos = new Vector3[48];
        private readonly float[] time = new float[48];
        private int count;
        private bool armed = true;
        private float gripPeak;
        private bool gripHeld;

        // Diagnostics since the last Report (playbook: evaluated samples beside the peak speed separate "never sees
        // motion" from "threshold too high").
        public int Evaluated, Swings, Rejected;
        public float PeakSpeed;

        public int Count => count;
        /// <summary>The last frame's movement (metres, room space).</summary>
        public Vector3 LastStep => count >= 2 ? pos[0] - pos[1] : Vector3.zero;

        public void Reset() { count = 0; armed = true; }

        public void Add(Vector3 p, float t)
        {
            if (count > 0 && (t - time[0] > MaxGap || t <= time[0])) count = 0;   // a gap or a stalled clock: start over
            for (int i = Mathf.Min(count, pos.Length - 1); i > 0; i--) { pos[i] = pos[i - 1]; time[i] = time[i - 1]; }
            pos[0] = p;
            time[0] = t;
            if (count < pos.Length) count++;
        }

        /// <summary>Velocity (m/s) over the last ~3 samples.</summary>
        public Vector3 Velocity()
        {
            int last = Mathf.Min(count - 1, 3);
            if (last < 1) return Vector3.zero;
            float dt = time[0] - time[last];
            return dt > 1e-4f ? (pos[0] - pos[last]) / dt : Vector3.zero;
        }

        /// <summary>The fastest two-sample-span velocity within the last 150 ms (the throw's).</summary>
        public Vector3 PeakVelocity()
        {
            Vector3 best = Vector3.zero;
            for (int i = 0; i + 2 < count && time[0] - time[i + 2] <= PeakWindow + 1e-4f; i++)
            {
                float dt = time[i] - time[i + 2];
                if (dt <= 1e-4f) continue;
                Vector3 v = (pos[i] - pos[i + 2]) / dt;
                if (v.sqrMagnitude > best.sqrMagnitude) best = v;
            }
            return best.sqrMagnitude > 0f ? best : Velocity();
        }

        /// <summary>
        /// One swing per motion: true on the frame the hand first passes the swing speed while armed and accept(v) holds.
        /// shoulder: room-space shoulder, for the retraction test.
        /// </summary>
        public bool Swing(Vector3 shoulder, Func<Vector3, bool> accept, out Vector3 velocity)
        {
            velocity = Velocity();
            if (count < 3) return false;
            float speed = velocity.magnitude;
            Evaluated++;
            PeakSpeed = Mathf.Max(PeakSpeed, speed);
            if (!armed)
            {
                if (speed < RearmSpeed) armed = true;
                return false;
            }
            if (speed < SwingSpeed) return false;
            Vector3 out_ = pos[0] - shoulder;
            bool retraction = out_.sqrMagnitude > 1e-4f && Vector3.Dot(velocity, out_.normalized) < -0.5f * speed;
            if (retraction || (accept != null && !accept(velocity))) { Rejected++; armed = false; return false; }
            armed = false;
            Swings++;
            return true;
        }

        /// <summary>
        /// The grip of a hand holding something: true on the frame it lets go (30 % below its peak since the grab).
        /// holding: the hand holds an item now; grip: the analog grip (0..1).
        /// </summary>
        public bool Release(bool holding, float grip)
        {
            if (!holding) { gripHeld = false; gripPeak = 0f; return false; }
            if (!gripHeld)
            {
                if (grip < 0.6f) return false;
                gripHeld = true;
                gripPeak = grip;
                return false;
            }
            gripPeak = Mathf.Max(gripPeak, grip);
            if (grip >= gripPeak * (1f - ReleaseDrop)) return false;
            gripHeld = false;
            gripPeak = 0f;
            return true;
        }

        // A swing with something in the hand waits until the hand has passed its fastest (at most MaxHold): a throw lets
        // go at about the peak (Quake VR), a swing keeps holding through it. A release meanwhile cancels the swing.
        public const float MaxHold = 0.15f;
        private float pendingAt = -1f, pendingPeak;
        private Vector3 pendingVel;
        public bool Pending => pendingAt >= 0f;

        public void Hold(Vector3 v, float t) { pendingAt = t; pendingVel = v; pendingPeak = v.magnitude; }

        /// <summary>The held swing is due: the hand is slowing (under 90 % of its peak) or MaxHold has passed.</summary>
        public bool Due(float t, out Vector3 v)
        {
            v = pendingVel;
            if (pendingAt < 0f) return false;
            float s = Velocity().magnitude;
            pendingPeak = Mathf.Max(pendingPeak, s);
            if (s >= 0.9f * pendingPeak && t - pendingAt < MaxHold) return false;
            pendingAt = -1f;
            return true;
        }

        /// <summary>Drop a held swing (the grip let go: it was a throw); true if one was held.</summary>
        public bool Cancel()
        {
            bool was = pendingAt >= 0f;
            pendingAt = -1f;
            return was;
        }

        public string Report()
        {
            string r = $"{Evaluated} samples, peak {PeakSpeed:F1} m/s, {Swings} swings, {Rejected} rejected";
            Evaluated = Swings = Rejected = 0;
            PeakSpeed = 0f;
            return r;
        }
    }

    /// <summary>
    /// Motion takes (FP 9f, playbook TEST-027): record a few seconds of real hand motion in the headset, labelled by what
    /// it was meant to be, then replay the recipes over them headless. VR settings › Hands &amp; body › More settings ›
    /// "Record a motion take" starts one: 5 s to close the menu (a tick each second), then 20 s of recording (a long
    /// tick at the start and the end). Files: BepInEx/CasualtyVR/takes/&lt;label&gt;-&lt;time&gt;.csv, one line per hand per frame.
    /// --cvr-dbg-replay &lt;folder|synthetic&gt; replays every take (or generated ones) and logs what the recipes find.
    /// </summary>
    internal static class MotionTakes
    {
        public static readonly string[] Labels = { "Swings", "Throws", "Other" };
        public static string Recording { get; private set; }
        private static float startAt = -1f, endAt = -1f, nextTick;
        private static StringBuilder buf;
        private static string label;

        public static string Folder => Path.Combine(BepInEx.Paths.BepInExRootPath, "CasualtyVR", "takes");

        public static void Start(string what)
        {
            label = what;
            startAt = Time.unscaledTime + 5f;
            endAt = startAt + 20f;
            nextTick = Time.unscaledTime;
            buf = new StringBuilder();
            buf.AppendLine($"# CasualtyVR motion take '{what}', {DateTime.Now:yyyy-MM-dd HH:mm:ss}, version {Plugin.Version}");
            buf.AppendLine("# t,hand,px,py,pz,qx,qy,qz,qw,grip,trigger,holding,headx,heady,headz (room space, Unity axes, metres)");
            Recording = what + " (starting)";
            Plugin.Log.LogInfo($"Motion take: '{what}' starts in 5 s");
        }

        /// <summary>Once per frame (VrGrab.Update): countdown, samples, and the end.</summary>
        public static void Update(Body body)
        {
            if (startAt < 0f) return;
            float now = Time.unscaledTime;
            if (now < startAt)
            {
                if (now >= nextTick) { nextTick += 1f; VrHaptics.Pulse(0, 0.3f, 0.03f); VrHaptics.Pulse(1, 0.3f, 0.03f); }
                return;
            }
            if (Recording != label) { Recording = label; VrHaptics.Pulse(0, 0.8f, 0.2f); VrHaptics.Pulse(1, 0.8f, 0.2f); }
            Vector3 head = VrSpace.HeadTrackingPos;
            for (int h = 0; h < 2; h++)
            {
                if (!VrInput.GripTracking(h, out Vector3 p, out Quaternion q, out float grip, out float trigger)) continue;
                bool holding = body != null && VrGrab.HoldingInHand(body, h);
                buf.AppendLine(string.Join(",", new[] { now - startAt, h, p.x, p.y, p.z, q.x, q.y, q.z, q.w, grip, trigger, holding ? 1 : 0, head.x, head.y, head.z }
                    .Select(v => Convert.ToString(v, CultureInfo.InvariantCulture))));
            }
            if (now < endAt) return;
            Directory.CreateDirectory(Folder);
            string file = Path.Combine(Folder, $"{label.ToLowerInvariant()}-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
            File.WriteAllText(file, buf.ToString());
            Plugin.Log.LogInfo($"Motion take: '{label}' saved, {file}");
            VrHaptics.Pulse(0, 0.8f, 0.2f);
            VrHaptics.Pulse(1, 0.8f, 0.2f);
            startAt = endAt = -1f;
            Recording = null;
            buf = null;
        }

        /// <summary>Note a live event in the take being recorded (to compare with the replay).</summary>
        public static void Note(string what)
        {
            if (buf != null && startAt > 0f && Time.unscaledTime >= startAt) buf.AppendLine($"# event {Time.unscaledTime - startAt:F3} {what}");
        }

        // ---- replay ----------------------------------------------------------------------------------------

        private struct Sample { public float t; public int h; public Vector3 p, head; public float grip; public bool holding; }

        /// <summary>--cvr-dbg-replay: run the recipes over every take in a folder (or over generated ones) and log the counts.</summary>
        public static void Replay(string what)
        {
            var takes = new List<(string name, string label, List<Sample> samples)>();
            if (what == "synthetic") takes.AddRange(Synthetic());
            else
            {
                string dir = Directory.Exists(what) ? what : Folder;
                if (!Directory.Exists(dir)) { Plugin.Log.LogWarning($"Motion replay: no takes in {dir}"); return; }
                foreach (var f in Directory.GetFiles(dir, "*.csv").OrderBy(f => f))
                {
                    var list = new List<Sample>();
                    foreach (var line in File.ReadAllLines(f))
                    {
                        if (line.StartsWith("#") || line.Trim().Length == 0) continue;
                        var c = line.Split(',').Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                        list.Add(new Sample { t = c[0], h = (int)c[1], p = new Vector3(c[2], c[3], c[4]), grip = c[9], holding = c[11] > 0.5f, head = new Vector3(c[12], c[13], c[14]) });
                    }
                    string name = Path.GetFileNameWithoutExtension(f);
                    takes.Add((name, name.Split('-')[0], list));
                }
            }
            foreach (var (name, lab, samples) in takes)
            {
                int swings = 0, throws = 0, drops = 0, cancelled = 0;
                float peakThrow = 0f;
                var motion = new[] { new HandMotion(), new HandMotion() };
                bool[] holdingWas = { false, false };
                foreach (var s in samples)
                {
                    var m = motion[s.h];
                    m.Add(s.p, s.t);
                    // A release while a swing waits cancels it (that flick was a throw), as live (VrGrab).
                    if (m.Release(s.holding || holdingWas[s.h], s.grip) && (s.holding || holdingWas[s.h]))
                    {
                        float speed = m.PeakVelocity().magnitude;
                        if (speed >= VrGrab.ThrowSpeedM) { throws++; peakThrow = Mathf.Max(peakThrow, speed); } else drops++;
                        if (m.Cancel()) cancelled++;
                        holdingWas[s.h] = false;
                    }
                    else holdingWas[s.h] = s.holding;
                    if (s.h == 1)
                    {
                        if (m.Due(s.t, out _)) swings++;
                        Vector3 shoulder = s.head + new Vector3(0f, -0.25f, 0f);
                        if (m.Swing(shoulder, null, out Vector3 v))
                        {
                            if (s.holding) m.Hold(v, s.t); else swings++;
                        }
                    }
                }
                if (motion[1].Pending) swings++;
                Plugin.Log.LogInfo($"Motion replay: '{name}' ({lab}, {samples.Count} samples): {swings} swings, {throws} throws (peak {peakThrow:F1} m/s), {drops} drops, {cancelled} swings cancelled by a throw");
            }
            Plugin.Log.LogInfo($"Motion replay: done, {takes.Count} takes");
        }

        /// <summary>
        /// Generated takes for the headless test: 5 swings (an arc at about 4 m/s, back slowly), 5 slow reaches (1 m/s), 5
        /// fast retractions (back towards the shoulder), 4 throws (grab, flick at 3.5 m/s, let the grip go to 60 %
        /// mid-flick) and 3 drops (let go at rest), at 90 Hz with small noise.
        /// </summary>
        private static IEnumerable<(string, string, List<Sample>)> Synthetic()
        {
            var rng = new System.Random(1234);
            float Noise() => (float)(rng.NextDouble() - 0.5) * 0.002f;
            Vector3 head = new Vector3(0f, 1.6f, 0f), rest = new Vector3(0.25f, 1.15f, 0.3f);
            List<Sample> Build(Func<float, (Vector3 p, float grip, bool holding)> f, float seconds)
            {
                var l = new List<Sample>();
                for (float t = 0f; t < seconds; t += 1f / 90f)
                {
                    var (p, g, hold) = f(t);
                    l.Add(new Sample { t = t, h = 1, p = p + new Vector3(Noise(), Noise(), Noise()), grip = g, holding = hold, head = head });
                }
                return l;
            }
            // Smoothstep travel between two points over d seconds, starting at t0.
            Vector3 Move(float t, float t0, float d, Vector3 a, Vector3 b) => Vector3.Lerp(a, b, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - t0) / d)));
            yield return ("synthetic-swings", "Swings", Build(t =>
            {
                float k = t % 2f;   // every 2 s: out to the side and up, a 0.6 m arc in 0.22 s (about 4 m/s at its fastest), back in 1 s
                Vector3 top = rest + new Vector3(0.1f, 0.45f, 0.25f), end = rest + new Vector3(0.25f, -0.15f, 0.45f);
                Vector3 p = k < 0.6f ? Move(k, 0f, 0.6f, rest, top) : k < 0.82f ? Move(k, 0.6f, 0.22f, top, end) : Move(k, 0.82f, 1f, end, rest);
                return (p, 0f, false);
            }, 10f));
            yield return ("synthetic-other", "Other", Build(t =>
            {
                float k = t % 3f;   // a slow reach out and back (1 m/s at most), then a fast pull back towards the shoulder
                Vector3 far = rest + new Vector3(0.05f, 0.1f, 0.4f);
                Vector3 p = k < 1f ? Move(k, 0f, 1f, rest, far) : k < 1.15f ? Move(k, 1f, 0.15f, far, rest + new Vector3(-0.05f, 0.05f, -0.1f)) : Move(k, 1.15f, 1.5f, rest + new Vector3(-0.05f, 0.05f, -0.1f), rest);
                return (p, 0f, false);
            }, 15f));
            yield return ("synthetic-throws", "Throws", Build(t =>
            {
                float k = t % 2.5f;   // grab at rest, wind up, flick 0.55 m in 0.16 s and let go to 60 % halfway, recover
                Vector3 back = rest + new Vector3(-0.05f, 0.25f, -0.15f), fore = rest + new Vector3(0.1f, 0.2f, 0.45f);
                Vector3 p = k < 0.7f ? Move(k, 0f, 0.7f, rest, back) : k < 0.86f ? Move(k, 0.7f, 0.16f, back, fore) : Move(k, 0.86f, 1f, fore, rest);
                bool holding = k < 0.78f;
                float grip = k < 0.05f ? 0f : k < 0.78f ? 0.95f : 0.55f;
                return (p, grip, holding);
            }, 10f));
            yield return ("synthetic-drops", "Other", Build(t =>
            {
                float k = t % 2f;   // hold still, open the hand slowly
                bool holding = k < 1.2f;
                float grip = k < 0.05f ? 0f : k < 1.2f ? 0.9f : 0.3f;
                return (rest, grip, holding);
            }, 6f));
        }
    }
}
