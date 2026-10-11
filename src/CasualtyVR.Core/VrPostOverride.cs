using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CasualtyVR
{
    /// <summary>
    /// A global post-processing volume that only the eye cameras listen to (its layer is in their
    /// volume mask, not the game camera's). It switches off effects that are drawn in *screen space*
    /// and therefore look glued to the player's eyes in a headset (user report 2026-10-03: "faint
    /// visuals that are stuck/fixed to my vision as I turn my head"): film grain, and bloom's lens-dirt
    /// texture. Everything else (colour grading, bloom itself) is left to the game's own volumes.
    /// </summary>
    internal sealed class VrPostOverride
    {
        public const int Layer = 27;   // unused by the game (probe: layers 0-10 only)

        private readonly GameObject go, keyGo;
        private readonly VolumeProfile profile, keyProfile;

        public VrPostOverride()
        {
            go = new GameObject("CasualtyVR.PostOverride") { layer = Layer, hideFlags = HideFlags.DontSave };
            Object.DontDestroyOnLoad(go);
            profile = ScriptableObject.CreateInstance<VolumeProfile>();

            var grain = profile.Add<FilmGrain>(true);
            grain.intensity.Override(0f);
            var bloom = profile.Add<Bloom>(false);
            bloom.dirtIntensity.Override(0f);
            bloom.dirtTexture.Override(null);
            var vignette = profile.Add<Vignette>(false);
            vignette.intensity.Override(0f);
            var aberration = profile.Add<ChromaticAberration>(false);
            aberration.intensity.Override(0f);
            var distortion = profile.Add<LensDistortion>(false);
            distortion.intensity.Override(0f);

            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10000f;
            volume.weight = 1f;
            volume.sharedProfile = profile;

            // Passthrough greenscreen: neutral grading for the eyes, so the cleared key colour inside the
            // game window comes out exactly as chosen (the game's ColorAdjustments/LiftGammaGain/bloom made
            // it a paler green than the frame outside, 2026-10-04). Only active while the greenscreen is on.
            keyGo = new GameObject("CasualtyVR.PostOverrideKey") { layer = Layer, hideFlags = HideFlags.DontSave };
            Object.DontDestroyOnLoad(keyGo);
            keyProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            var ca = keyProfile.Add<ColorAdjustments>(false);
            ca.postExposure.Override(0f); ca.contrast.Override(0f); ca.saturation.Override(0f);
            ca.hueShift.Override(0f); ca.colorFilter.Override(Color.white);
            var lgg = keyProfile.Add<LiftGammaGain>(false);
            lgg.lift.Override(new Vector4(1f, 1f, 1f, 0f)); lgg.gamma.Override(new Vector4(1f, 1f, 1f, 0f)); lgg.gain.Override(new Vector4(1f, 1f, 1f, 0f));
            var smh = keyProfile.Add<ShadowsMidtonesHighlights>(false);
            smh.shadows.Override(new Vector4(1f, 1f, 1f, 0f)); smh.midtones.Override(new Vector4(1f, 1f, 1f, 0f)); smh.highlights.Override(new Vector4(1f, 1f, 1f, 0f));
            var wb = keyProfile.Add<WhiteBalance>(false);
            wb.temperature.Override(0f); wb.tint.Override(0f);
            var kb = keyProfile.Add<Bloom>(false);
            kb.intensity.Override(0f);
            var tm = keyProfile.Add<Tonemapping>(false);
            tm.mode.Override(TonemappingMode.None);
            var keyVolume = keyGo.AddComponent<Volume>();
            keyVolume.isGlobal = true;
            keyVolume.priority = 10001f;
            keyVolume.weight = 1f;
            keyVolume.sharedProfile = keyProfile;
            keyGo.SetActive(false);
        }

        public void SetGreenscreen(bool on)
        {
            if (keyGo.activeSelf != on) keyGo.SetActive(on);
        }

        public static int AddTo(int volumeMask) => volumeMask | (1 << Layer);

        public void Destroy()
        {
            Object.Destroy(go);
            Object.Destroy(profile);
            Object.Destroy(keyGo);
            Object.Destroy(keyProfile);
        }
    }
}
