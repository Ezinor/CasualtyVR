using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace CasualtyVR.Probe
{
    /// <summary>
    /// Phase 1 spike (--cvr-spike). Renders the live scene through extra cameras, without touching the
    /// game's own camera, and saves each view as a PNG:
    ///   spike-ortho       orthographic clone without PixelPerfectCamera (reference)
    ///   spike-persp       perspective camera framed to show the same 45-unit-high slice of the z=0 plane
    ///   spike-eyeL/R      off-axis stereo pair whose zero-parallax plane is z=0 (the game plane)
    /// Questions answered: do URP 2D lights, post-processing and sorting survive a perspective camera
    /// and an off-axis projection (risk R7)? Does the pair contain real parallax?
    /// </summary>
    internal static class StereoSpike
    {
        public const float VerticalFovDeg = 40f;
        public const int Width = 1920, Height = 1080;

        public static IEnumerator Run(string outDir, float eyeSeparation)
        {
            var main = Camera.main;
            if (main == null) { ProbePlugin.Log.LogWarning("Spike: no Camera.main"); yield break; }

            float halfHeight = main.orthographicSize;                 // 22.5 world units at the game plane
            float aspect = (float)Width / Height;
            float distance = halfHeight / Mathf.Tan(VerticalFovDeg * 0.5f * Mathf.Deg2Rad);
            Vector3 focus = new Vector3(main.transform.position.x, main.transform.position.y, 0f);
            ProbePlugin.Log.LogInfo($"Spike: focus={focus} planeHalfHeight={halfHeight} fov={VerticalFovDeg} distance={distance:F2} eyeSep={eyeSeparation}");

            yield return Capture(outDir, "spike-ortho", main, cam =>
            {
                cam.orthographic = true;
                cam.orthographicSize = halfHeight;
                cam.transform.position = main.transform.position;
            });

            yield return Capture(outDir, "spike-persp", main, cam =>
            {
                cam.orthographic = false;
                cam.fieldOfView = VerticalFovDeg;
                cam.transform.position = focus + Vector3.back * distance;
                cam.nearClipPlane = 1f;
                cam.farClipPlane = distance + 100f;
            });

            var before = new System.Collections.Generic.Dictionary<Transform, Vector3>();
            foreach (var r in Object.FindObjectsOfType<Renderer>()) before[r.transform] = r.transform.position;

            foreach (var (name, sign, popup) in new[] { ("spike-eyeL", -1f, false), ("spike-eyeR", 1f, false), ("popup-eyeL", -1f, true), ("popup-eyeR", 1f, true) })
            {
                float eyeX = sign * eyeSeparation * 0.5f;
                yield return Capture(outDir, name, main, cam =>
                {
                    if (popup) PopupDepth.Register(cam);
                    cam.orthographic = false;
                    cam.transform.position = focus + new Vector3(eyeX, 0f, -distance);
                    cam.nearClipPlane = 1f;
                    cam.farClipPlane = distance + 100f;
                    // Off-axis frustum: both eyes frame the same rectangle on the z=0 plane, so the plane
                    // has zero parallax and anything off the plane gets horizontal disparity.
                    float n = cam.nearClipPlane, k = n / distance;
                    float halfW = halfHeight * aspect;
                    cam.projectionMatrix = Matrix4x4.Frustum((-halfW - eyeX) * k, (halfW - eyeX) * k, -halfHeight * k, halfHeight * k, n, cam.farClipPlane);
                });
            }
            // Leak check: the pop-up must leave no z offset behind. Physics bodies may legitimately move
            // in x/y between frames, so only z is compared.
            int leaked = 0;
            foreach (var kv in before)
                if (kv.Key && Mathf.Abs(kv.Key.position.z - kv.Value.z) > 1e-4f) leaked++;
            ProbePlugin.Log.LogInfo($"Spike: popup leak check: {leaked} of {before.Count} renderer transforms changed z");
            ProbePlugin.Log.LogInfo("Spike: done");
        }

        private static IEnumerator Capture(string outDir, string name, Camera main, System.Action<Camera> configure)
        {
            var go = new GameObject("CasualtyVR.SpikeCamera");
            var cam = go.AddComponent<Camera>();
            cam.CopyFrom(main);
            // CopyFrom also copies the main camera's *custom* view/projection matrices (the game's
            // camera carries an overridden worldToCameraMatrix), which stay frozen at z=-3 no matter
            // where this transform moves. Reset both so they follow the transform and lens again.
            ProbePlugin.Log.LogInfo($"Spike: main w2c.m23={main.worldToCameraMatrix.m23:F2} vs main z={main.transform.position.z:F2}");
            cam.ResetWorldToCameraMatrix();
            cam.ResetProjectionMatrix();
            cam.depth = main.depth - 10;
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { name = name };
            cam.targetTexture = rt;
            var data = cam.GetUniversalAdditionalCameraData();
            var mainData = main.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = mainData.renderPostProcessing;
            data.volumeLayerMask = mainData.volumeLayerMask;
            configure(cam);

            // Let the pipeline render the camera normally for a few frames (lights and volumes settle).
            for (int i = 0; i < 3; i++) yield return new WaitForEndOfFrame();

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            string path = Path.Combine(outDir, name + ".png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            ProbePlugin.Log.LogInfo($"Spike: {name} -> {path} (proj m00={cam.projectionMatrix.m00:F3} m02={cam.projectionMatrix.m02:F4}) popupApply={PopupDepth.LastApplyMs:F2}ms over {PopupDepth.LastCount} renderers");
            Vector3 f = new Vector3(main.transform.position.x, main.transform.position.y, 0f);
            ProbePlugin.Log.LogInfo($"Spike diag {name}: camPos={cam.transform.position} parent={(cam.transform.parent ? cam.transform.parent.name : "-")} rot={cam.transform.eulerAngles} " +
                $"vp(focus)={cam.WorldToViewportPoint(f)} vp(focus+10x)={cam.WorldToViewportPoint(f + Vector3.right * 10f)} vp(focus+10y)={cam.WorldToViewportPoint(f + Vector3.up * 10f)} " +
                $"w2c.m23={cam.worldToCameraMatrix.m23:F2} usePhysical={cam.usePhysicalProperties} sensor={cam.sensorSize} focal={cam.focalLength:F1} lensShift={cam.lensShift}");

            Object.Destroy(tex);
            cam.targetTexture = null;
            PopupDepth.Unregister(cam);
            Object.Destroy(go);
            rt.Release();
            Object.Destroy(rt);
        }
    }
}
