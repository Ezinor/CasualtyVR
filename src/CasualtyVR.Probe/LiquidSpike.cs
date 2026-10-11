using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace CasualtyVR.Probe
{
    /// <summary>
    /// --cvr-spike-liquids: is any liquid/atmosphere shader anchored to the screen instead of the world?
    /// (User report 2026-10-03: "water puddles/lakes look kind of weird" in VR.) For every renderer whose
    /// shader name mentions a liquid or weather effect, an orthographic camera renders the renderer's
    /// bounds twice, the second time moved +2 world units in x. World-anchored content moves by exactly
    /// 2 units of pixels between the two images; a screen-space pattern stays where it was.
    /// The PNG pairs are compared offline (tools/liquid_shift.py).
    /// </summary>
    internal static class LiquidSpike
    {
        private static readonly string[] Keywords = { "Water", "Liquid", "Fog", "Rain", "Magma", "Algae", "Sap", "Oil" };
        public const float Shift = 2f;
        public const int Size = 512;

        public static IEnumerator Run(string outDir)
        {
            var main = Camera.main;
            var targets = Object.FindObjectsOfType<Renderer>()
                .Where(r => r.isVisible || r.enabled)
                .Where(r => r.sharedMaterial && r.sharedMaterial.shader && Keywords.Any(k => r.sharedMaterial.shader.name.Contains(k)))
                .GroupBy(r => r.sharedMaterial.shader.name).Select(g => g.First()).ToList();
            ProbePlugin.Log.LogInfo($"Liquid spike: {targets.Count} shaders");
            foreach (var r in targets)
            {
                var b = r.bounds;
                // Frame 24 units around the renderer's centre (or the camera's view centre if huge).
                Vector3 centre = b.size.x > 200f ? new Vector3(main.transform.position.x, main.transform.position.y, 0f) : b.center;
                string tag = r.sharedMaterial.shader.name.Replace("/", "_").Replace(" ", "");
                ProbePlugin.Log.LogInfo($"Liquid spike: {Path(r.transform)} shader={r.sharedMaterial.shader.name} type={r.GetType().Name} layer={r.sortingLayerName}/{r.sortingOrder} z={r.transform.position.z:F2} bounds={b.center}/{b.size} keywords=[{string.Join(",", r.sharedMaterial.shaderKeywords)}]");
                for (int k = 0; k < 2; k++)
                {
                    var go = new GameObject("CasualtyVR.LiquidCam");
                    var cam = go.AddComponent<Camera>();
                    cam.CopyFrom(main);
                    cam.ResetWorldToCameraMatrix();
                    cam.ResetProjectionMatrix();
                    cam.orthographic = true;
                    cam.orthographicSize = 12f;
                    cam.aspect = 1f;
                    cam.transform.position = new Vector3(centre.x + k * Shift, centre.y, main.transform.position.z);
                    var rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32);
                    cam.targetTexture = rt;
                    cam.GetUniversalAdditionalCameraData().renderPostProcessing = false;
                    for (int f = 0; f < 3; f++) yield return new WaitForEndOfFrame();
                    var prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    var tex = new Texture2D(Size, Size, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                    tex.Apply();
                    RenderTexture.active = prev;
                    File.WriteAllBytes(System.IO.Path.Combine(outDir, $"liquid-{tag}-{k}.png"), tex.EncodeToPNG());
                    Object.Destroy(tex);
                    cam.targetTexture = null;
                    Object.Destroy(go);
                    rt.Release();
                    Object.Destroy(rt);
                }
            }
        }

        private static string Path(Transform t) => t.parent ? Path(t.parent) + "/" + t.name : t.name;
    }
}
