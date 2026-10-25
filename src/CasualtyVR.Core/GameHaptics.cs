using HarmonyLib;
using UnityEngine;

namespace CasualtyVR
{
    /// <summary>
    /// Vibration from the game's own events (docs/BUILD_PLAN.md Phase 5, FIRSTPERSON_REVIEW FP 10b), driven by signals
    /// the game already scales (playbook HAND-003) rather than a parallel state machine:
    /// <list type="bullet">
    /// <item><b>Damage:</b> the game's screen shake (Shaker.Shake: impacts scaled by force, broken bones, dislocations,
    /// traps, explosions, earthquakes; VR drops the shake itself) on both hands, stronger for a bigger shake; and a drop
    /// in the character's limb health or a rise in its pain that came without a shake (bites, cuts, shots).</item>
    /// <item><b>Hits that landed:</b> Body.Attack returns true only when the swing hit a block, an animal or a building;
    /// a pulse on the right hand (the active hand), which a swing that hit nothing does not get.</item>
    /// <item><b>Heartbeat</b> (optional, off by default): when the character is in danger (low blood pressure, oxygen or
    /// consciousness, a racing heart), a lub-dub on the left hand at the game's own simulated heart rate. A heart that
    /// stops beating stops it too.</item>
    /// </list>
    /// All of it goes through VrHaptics.Pulse, so the Vibration switch and strength apply.
    /// </summary>
    internal static class GameHaptics
    {
        private static float pendingShake, lastHealth = -1f, lastPain = -1f, nextPulse, nextBeat, dubAt = -1f, dubAmp;
        private static Body watched;

        public static void Update()
        {
            if (VrRig.Instance == null || !VrRig.Instance.Active) { pendingShake = 0f; return; }
            var body = PlayerCamera.main != null ? PlayerCamera.main.body : null;
            if (body == null || body.limbs == null) return;
            if (body != watched) { watched = body; lastHealth = lastPain = -1f; }
            float now = Time.unscaledTime;

            // Damage: the shake, else a health drop or a pain rise (one pulse per 80 ms at most).
            float health = 0f, pain = 0f;
            foreach (var l in body.limbs) if (l != null) { health += l.skinHealth + l.muscleHealth; pain += l.pain; }
            float lost = lastHealth >= 0f ? Mathf.Max(0f, lastHealth - health) + 0.5f * Mathf.Max(0f, pain - lastPain) : 0f;
            lastHealth = health;
            lastPain = pain;
            if (Plugin.HapticDamage.Value && now >= nextPulse && (pendingShake > 0f || lost > 1f))
            {
                // Shake 50 (a dislocation, a trap) -> 0.4; 100 -> 0.6; 200 or more -> full. Lost health: 10 points -> 0.5.
                float amp = pendingShake > 0f ? Mathf.Clamp(0.2f + pendingShake / 250f, 0.2f, 1f) : Mathf.Clamp(0.25f + lost / 40f, 0.25f, 1f);
                float secs = pendingShake > 0f ? 0.04f + Mathf.Clamp(pendingShake / 1000f, 0f, 0.2f) : 0.05f + Mathf.Clamp(lost / 200f, 0f, 0.15f);
                VrHaptics.Pulse(0, amp, secs);
                VrHaptics.Pulse(1, amp, secs);
                if (Plugin.Verbose) Plugin.Log.LogInfo($"Haptics: damage ({(pendingShake > 0f ? $"shake {pendingShake:F0}" : $"lost {lost:F1}")}) -> {amp:F2} for {secs:F2} s");
                nextPulse = now + 0.08f;
                pendingShake = 0f;
            }
            else if (now >= nextPulse) pendingShake = 0f;

            Heartbeat(body, now);
        }

        private static void Heartbeat(Body body, float now)
        {
            if (dubAt > 0f && now >= dubAt) { VrHaptics.Pulse(0, dubAmp, 0.03f); dubAt = -1f; }
            if ((!Plugin.HapticHeartbeat.Value && !DbgDanger) || !body.alive || body.heartRate < 20f) return;
            // In danger: the worst of the game's own vital signs, 0 (fine) to 1.
            float danger = Mathf.Max(Mathf.Max(Mathf.InverseLerp(85f, 40f, body.consciousness), Mathf.InverseLerp(95f, 70f, body.bloodPressure)),
                                     Mathf.Max(Mathf.InverseLerp(90f, 60f, body.bloodOxygen), Mathf.InverseLerp(140f, 200f, body.heartRate)));
            if (DbgDanger) danger = 1f;
            if (danger < 0.15f || now < nextBeat) return;
            float amp = Mathf.Lerp(0.25f, 0.7f, danger);
            VrHaptics.Pulse(0, amp, 0.04f);   // the heart side
            dubAt = now + 0.14f;
            dubAmp = amp * 0.6f;
            nextBeat = now + 60f / Mathf.Clamp(body.heartRate, 30f, 220f);
            if (Plugin.Verbose && now >= nextBeatLog) { nextBeatLog = now + 5f; Plugin.Log.LogInfo($"Haptics: heartbeat at {body.heartRate:F0} bpm (danger {danger:F2}: consciousness {body.consciousness:F0}, pressure {body.bloodPressure:F0}, oxygen {body.bloodOxygen:F0})"); }
        }

        private static float nextBeatLog;
        // --cvr-dbg-danger (tools/fp-comfort.ps1): beat as if in danger, at the character's own heart rate.
        private static readonly bool DbgDanger = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "--cvr-dbg-danger") >= 0;

        [HarmonyPatch(typeof(Shaker), nameof(Shaker.Shake))]
        private static class ShakePatch
        {
            // The amount before the game's screen-shake setting scales it: turning the shake off must not mute this.
            private static void Prefix(float amount) { if (amount > pendingShake) pendingShake = amount; }
        }

        [HarmonyPatch(typeof(Body), nameof(Body.Attack))]
        private static class AttackPatch
        {
            private static void Postfix(Body __instance, AttackInfo atk, bool __result)
            {
                if (!__result || !Plugin.HapticHits.Value || PlayerCamera.main == null || __instance != PlayerCamera.main.body) return;
                if (VrRig.Instance == null || !VrRig.Instance.Active) return;
                float amp = Mathf.Clamp(0.45f + (atk != null ? atk.damage : 0f) / 60f, 0.45f, 1f);
                VrHaptics.Pulse(1, amp, 0.06f);
                if (Plugin.Verbose) Plugin.Log.LogInfo($"Haptics: hit landed ({(atk != null ? atk.damage : 0f):F0} damage) -> {amp:F2}");
            }
        }
    }
}
