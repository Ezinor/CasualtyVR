using UnityEngine;
using UnityEngine.Rendering;

namespace CasualtyVR
{
    /// <summary>
    /// Rain in first person (Rainy layers and the fungal biome; layer-modifier pass, 2026-10-06). The game's rain is one
    /// 1024-unit sprite (WorldGeneration.rainSprite, "Shader Graphs/RainShader") whose alpha the game sets every frame
    /// from its rain map at the player. The diorama draws it correctly (the dark veil on Rainy layers there is the VR
    /// mist: rain also sets fog 0.8), but in first person it is a flat sheet seen edge-on: nothing. There it is hidden
    /// in the eyes and replaced by falling streak particles on the VR-only layer around the player's head, as strong
    /// as the game's own rain.
    /// </summary>
    internal sealed class VrRain
    {
        private readonly GameObject go;
        private readonly ParticleSystem ps;
        private readonly Material mat;
        private SpriteRenderer hidden;

        public int Particles => ps != null ? ps.particleCount : 0;
        public bool Playing => ps != null && ps.isPlaying;

        public VrRain()
        {
            go = new GameObject("CasualtyVR.Rain") { layer = VoxelTerrain.Layer, hideFlags = HideFlags.DontSave };
            Object.DontDestroyOnLoad(go);
            ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSpeed = 0f;
            main.startSize = 0.06f;
            main.startLifetime = 1.4f;
            main.maxParticles = 4000;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;   // empty bounds would pause it before it ever emits
            main.startColor = new Color(0.75f, 0.8f, 0.9f, 0.45f);
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);
            vel.y = new ParticleSystem.MinMaxCurve(-30f, -24f);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.045f;
            r.lengthScale = 1f;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sortingLayerName = "Top";
            r.sortingOrder = -10;   // where the game's rain draws
            mat = new Material(Shader.Find("Sprites/Default")) { name = "CasualtyVR.Rain" };
            r.sharedMaterial = mat;
            RenderPipelineManager.beginCameraRendering += OnBegin;
            RenderPipelineManager.endCameraRendering += OnEnd;
        }

        public void Update()
        {
            var world = WorldGeneration.world;
            var rain = world != null ? world.rainSprite : null;
            // The diorama shows the game's own rain fine; only first person (flat sheet seen edge-on) needs streaks.
            float amount = VrSpace.FirstPerson && rain != null && rain.enabled && rain.gameObject.activeInHierarchy && !world.generatingWorld && Plugin.VrRain.Value ? rain.color.a : 0f;
            var emission = ps.emission;
            if (amount < 0.02f)
            {
                emission.rateOverTime = 0f;
                if (ps.isPlaying && ps.particleCount == 0) ps.Stop();
                return;
            }
            if (!ps.isPlaying) ps.Play();
            var shape = ps.shape;
            var main = ps.main;
            Vector3 center;
            if (VrSpace.FirstPerson)
            {
                // A box over the head spanning the tunnel: the streaks fall past you and down the tunnel ahead.
                Vector3 h = VrSpace.HeadPosition;
                center = new Vector3(h.x, h.y + 14f, 0f);
                shape.scale = new Vector3(40f, 1f, Mathf.Max(1f, VoxelTerrain.BackZ - VoxelTerrain.FrontZ));
                main.startSize = 0.025f;
                main.startLifetime = 1.3f;
                main.startColor = new Color(0.7f, 0.75f, 0.82f, 0.28f);
                emission.rateOverTime = 900f * amount;
            }
            else
            {
                // Across the game window, just in front of the play plane.
                var cam = PlayerCamera.main != null ? PlayerCamera.main.GetComponent<Camera>() : null;
                float halfH = cam != null && cam.orthographic ? cam.orthographicSize : 22.5f;
                float halfW = cam != null ? halfH * cam.aspect : 40f;
                Vector3 c = cam != null ? cam.transform.position : Vector3.zero;
                center = new Vector3(c.x, c.y + halfH + 6f, VoxelTerrain.FrontZ - 0.3f * PopupDepth.Scale);
                shape.scale = new Vector3(halfW * 2f, 1f, 0.5f);
                main.startSize = 0.1f;
                main.startLifetime = (2f * halfH + 8f) / 27f;
                emission.rateOverTime = 2200f * amount;
            }
            go.transform.position = center;
        }

        private void OnBegin(ScriptableRenderContext ctx, Camera cam)
        {
            if (VrRig.Instance == null || !VrRig.Instance.IsEyeCamera(cam) || !VrSpace.FirstPerson) return;
            var rain = WorldGeneration.world != null ? WorldGeneration.world.rainSprite : null;
            if (rain != null && !rain.forceRenderingOff) { rain.forceRenderingOff = true; hidden = rain; }
        }

        private void OnEnd(ScriptableRenderContext ctx, Camera cam)
        {
            if (hidden != null) { hidden.forceRenderingOff = false; hidden = null; }
        }

        public void Destroy()
        {
            RenderPipelineManager.beginCameraRendering -= OnBegin;
            RenderPipelineManager.endCameraRendering -= OnEnd;
            OnEnd(default, null);
            Object.Destroy(go);
            Object.Destroy(mat);
        }
    }
}
