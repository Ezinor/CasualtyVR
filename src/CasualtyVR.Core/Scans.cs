using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;

namespace CasualtyVR
{
    /// <summary>
    /// Periodic scans (finding renderers, lights, walls, bodies), staggered (docs/BUILD_PLAN.md Phase 2, IP 7). Phase 0
    /// found slow frames every 60 and 120 frames: every scan counted its period from the frame it first ran, so the
    /// 30-, 60- and 120-frame ones landed on the same frames. Each now runs on a frame of its own within its period
    /// (its phase), so at most one scan runs in any frame. Phases in use, by period:
    ///   30: Billboards 2, BodyView 6, ObjectExtruder 10, VrArms 14, FP front wall 18, FP back wall 22, SpriteSlabs 26
    ///   60: FP body refresh 28, FP lights 58
    ///  120: PopupDepth 0, FP tile copies 4, ShaderFog 8, Greenscreen 12
    /// Verbose runs also time each scan; the perf line reports the slowest run of each since the last line.
    /// </summary>
    internal static class Scans
    {
        /// <summary>
        /// Due this frame: on its phase of the period, or overdue (never run, or forced by setting last to -1000); not
        /// twice in one frame (some scans run from per-eye camera callbacks).
        /// </summary>
        public static bool Due(int lastFrame, int period, int phase)
            => lastFrame != Time.frameCount && (Time.frameCount - lastFrame >= 2 * period || (Time.frameCount + phase) % period == 0);

        private static readonly Dictionary<string, double> worst = new Dictionary<string, double>();

        public static long Start() => Plugin.Verbose ? Stopwatch.GetTimestamp() : 0L;

        public static void Stop(string name, long start)
        {
            if (!Plugin.Verbose || start == 0L) return;
            double ms = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
            if (!worst.TryGetValue(name, out double w) || ms > w) worst[name] = ms;
        }

        /// <summary>The slowest run of each scan since the last call ("name ms, ..."), or null.</summary>
        public static string Report()
        {
            if (worst.Count == 0) return null;
            string r = string.Join(", ", worst.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value:F1}"));
            worst.Clear();
            return r;
        }
    }
}
