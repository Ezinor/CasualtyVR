using UnityEngine;
using UnityEngine.UI;

namespace CasualtyVR
{
    /// <summary>
    /// "HUD size" in the VR settings: scales the game's main canvas (HUD, inventory, menus) while VR runs.
    /// The game reads its UI scale back from that canvas (PlayerCamera.uiScale), so its own mouse maths stay
    /// consistent. Restored when VR stops.
    /// </summary>
    internal static class HudScaler
    {
        private static CanvasScaler scaler;
        private static Vector2 baseReference;
        private static float baseFactor;
        private static float applied = 1f;

        public static void Apply(bool vr)
        {
            var pc = PlayerCamera.main;
            var canvas = pc != null ? pc.mainCanvas : null;
            var s = canvas != null ? canvas.GetComponent<CanvasScaler>() : null;
            if (s != scaler)
            {
                scaler = s;
                applied = 1f;
                if (s != null) { baseReference = s.referenceResolution; baseFactor = s.scaleFactor; }
            }
            if (scaler == null) return;
            float want = vr ? Mathf.Clamp(Plugin.HudScale.Value, 0.5f, 2f) : 1f;
            if (Mathf.Approximately(want, applied)) return;
            applied = want;
            if (scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize) scaler.referenceResolution = baseReference / want;
            else scaler.scaleFactor = baseFactor * want;
        }
    }
}
