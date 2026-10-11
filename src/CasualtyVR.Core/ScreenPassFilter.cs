using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CasualtyVR
{
    /// <summary>
    /// The game's single Renderer2D carries four FullScreenPassRendererFeatures (probe, 2026-10-03):
    /// BonusScreenPass, DamagePass, EffectsPass (MainPassScreen: blood/oxygen/consciousness) and AberPass
    /// (chromatic aberration). Features run on every camera using that renderer, so they also painted
    /// over the UI camera's texture: while dying, the whole UI (end-of-run screen, receipt) became a pink
    /// wash in the headset (user report: "can't see the end-of-run notes"). This switches chosen features
    /// off for chosen cameras, only while that camera renders.
    /// </summary>
    internal static class ScreenPassFilter
    {
        private static readonly Dictionary<Camera, HashSet<string>> rules = new Dictionary<Camera, HashSet<string>>();
        private static readonly List<ScriptableRendererFeature> disabled = new List<ScriptableRendererFeature>();
        private static List<ScriptableRendererFeature> features;
        private static bool hooked;

        /// <summary>Disable the named features (null = all of them) while <paramref name="cam"/> renders.</summary>
        public static void Exclude(Camera cam, params string[] featureNames)
        {
            rules[cam] = featureNames == null || featureNames.Length == 0 ? null : new HashSet<string>(featureNames);
            if (hooked) return;
            RenderPipelineManager.beginCameraRendering += OnBegin;
            RenderPipelineManager.endCameraRendering += OnEnd;
            hooked = true;
        }

        public static void Forget(Camera cam) => rules.Remove(cam);

        private static void OnBegin(ScriptableRenderContext ctx, Camera cam)
        {
            if (!rules.TryGetValue(cam, out var names)) return;
            foreach (var f in Features())
            {
                if (f == null || !f.isActive || (names != null && !names.Contains(f.name))) continue;
                f.SetActive(false);
                disabled.Add(f);
            }
        }

        private static void OnEnd(ScriptableRenderContext ctx, Camera cam)
        {
            if (disabled.Count == 0) return;
            foreach (var f in disabled) if (f != null) f.SetActive(true);
            disabled.Clear();
        }

        private static List<ScriptableRendererFeature> Features()
        {
            if (features != null) return features;
            features = new List<ScriptableRendererFeature>();
            var asset = GraphicsSettings.currentRenderPipeline;
            var field = asset?.GetType().GetField("m_RendererDataList", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field?.GetValue(asset) is IEnumerable list)
                foreach (var data in list)
                    if (data is ScriptableRendererData rd)
                        features.AddRange(rd.rendererFeatures);
            Plugin.Log.LogInfo($"Screen passes: {string.Join(", ", features.ConvertAll(f => f != null ? f.name : "null"))}");
            return features;
        }
    }
}
