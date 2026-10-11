using System.Collections.Generic;
using UnityEngine;

namespace CasualtyVR
{
    /// <summary>
    /// While VR runs, camera-facing billboard particles are switched to world alignment. For the game's
    /// own orthographic camera (which never rotates) the two are pixel-identical; for the eye cameras a
    /// View-aligned billboard turns with the player's head, so the water's particle quads rotated with
    /// head tilt (user report 2026-10-03). Originals are restored when VR stops (XR-004).
    /// </summary>
    internal sealed class ParticleAlignment
    {
        private readonly Dictionary<ParticleSystemRenderer, ParticleSystemRenderSpace> changed = new Dictionary<ParticleSystemRenderer, ParticleSystemRenderSpace>();
        private float nextScan;

        private bool lastFirstPerson;

        /// <summary>
        /// firstPerson: the particles face the eye's position (Facing) instead. World-aligned quads lie flat in the
        /// play plane, which first person sees from inside: the lifepod's crash dust became a tinted pane through
        /// the middle of the view (user: "brightness seam", 2026-10-05). Facing does not roll with head tilt.
        /// </summary>
        public void Update(bool firstPerson)
        {
            var want = firstPerson ? ParticleSystemRenderSpace.Facing : ParticleSystemRenderSpace.World;
            if (firstPerson != lastFirstPerson)
            {
                lastFirstPerson = firstPerson;
                foreach (var r in changed.Keys) if (r) r.alignment = want;
            }
            if (Time.unscaledTime < nextScan) return;
            nextScan = Time.unscaledTime + 1f;
            foreach (var r in Object.FindObjectsOfType<ParticleSystemRenderer>())
            {
                if (changed.ContainsKey(r) || r.renderMode != ParticleSystemRenderMode.Billboard) continue;
                if (r.alignment != ParticleSystemRenderSpace.View && r.alignment != ParticleSystemRenderSpace.Facing) continue;
                changed[r] = r.alignment;
                r.alignment = want;
            }
        }

        public void Restore()
        {
            foreach (var kv in changed)
                if (kv.Key) kv.Key.alignment = kv.Value;
            changed.Clear();
        }
    }
}
