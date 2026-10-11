using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace CasualtyVR
{
    /// <summary>
    /// World-space text in first person (user, 2026-10-06: "dialogue text is rendered on the side of the wall and
    /// obscured"). The game's speech (Talker.text, a TextMeshPro placed every LateUpdate 4 units above the head and
    /// 2.6 units towards the flat camera), damage numbers and the tutorial's course text lie in the play plane: from
    /// inside the tunnel they are seen edge-on, and the speech sits inside the front cave wall (the corridor is
    /// +-2 units wide). While an eye renders:
    /// * Your own character's speech is a caption a metre ahead of you, a little below eye level, following where
    ///   you look (lazily, by yaw only), drawn over everything at a readable size.
    /// * Other texts stay where the game puts them, moved into the middle of the tunnel and turned to face you, and
    ///   scaled up if they would be too small to read from where you are.
    /// The monitor view is unchanged: everything is put back after each eye.
    /// </summary>
    internal sealed class WorldText
    {
        private const float CaptionMetres = 1.0f, CaptionDropMetres = 0.2f, CaptionLineMetres = 0.04f;
        private const float MinLineRadians = 0.03f;   // about 1.7 degrees per line of text

        private readonly List<TextMeshPro> texts = new List<TextMeshPro>();
        private readonly List<(Transform t, Vector3 pos, Quaternion rot, Vector3 scale, Renderer r, int layer, int order)> moved = new List<(Transform, Vector3, Quaternion, Vector3, Renderer, int, int)>();
        private readonly HashSet<TextMeshPro> onTop = new HashSet<TextMeshPro>();
        private float nextScan;
        private Vector3 captionDir;
        private bool hasDir;
        private Body talkerBody;
        private TextMeshPro ownText;
        private string lastLogged;

        public int Shown { get; private set; }

        // Damage numbers live half a second (WorldGeneration.CreateDamageNumber): each one triggers a rescan.
        private static bool scanSoon;

        [HarmonyLib.HarmonyPatch(typeof(WorldGeneration), nameof(WorldGeneration.CreateDamageNumber))]
        private static class DamageNumberPatch
        {
            private static void Postfix() => scanSoon = true;
        }

        public WorldText()
        {
            RenderPipelineManager.beginCameraRendering += OnBegin;
            RenderPipelineManager.endCameraRendering += OnEnd;
        }

        public void Update()
        {
            if (!FirstPerson.Active) { hasDir = false; return; }
            if (scanSoon || Time.unscaledTime >= nextScan)
            {
                scanSoon = false;
                nextScan = Time.unscaledTime + 1f;
                texts.Clear();
                texts.AddRange(Object.FindObjectsOfType<TextMeshPro>());
            }
            var body = PlayerCamera.main != null ? PlayerCamera.main.body : null;
            if (body != talkerBody)
            {
                talkerBody = body;
                var talker = body != null ? body.GetComponent<Talker>() : null;
                ownText = talker != null ? talker.text : null;
            }
            Vector3 f = VrSpace.HeadForward;
            f.y = 0f;
            if (f.sqrMagnitude < 0.01f) f = new Vector3(FirstPerson.FacingRightNow ? 1f : -1f, 0f, 0f);
            f.Normalize();
            captionDir = hasDir ? Vector3.Slerp(captionDir, f, 1f - Mathf.Exp(-2.5f * Time.unscaledDeltaTime)) : f;
            hasDir = true;
        }

        private void OnBegin(ScriptableRenderContext ctx, Camera cam)
        {
            if (!FirstPerson.Active || !hasDir || VrRig.Instance == null || !VrRig.Instance.IsEyeCamera(cam)) return;
            FirstPerson.RenderTime.Start();
            Vector3 head = VrSpace.HeadPosition;
            float s = VrSpace.Scale;
            int shown = 0;
            foreach (var text in texts)
            {
                if (text == null || !text.isActiveAndEnabled || string.IsNullOrEmpty(text.text)) continue;
                var t = text.transform;
                var r = text.renderer;
                moved.Add((t, t.position, t.rotation, t.localScale, r, r != null ? r.sortingLayerID : 0, r != null ? r.sortingOrder : 0));
                float line = LineHeight(text);
                Vector3 pos;
                float k = 1f;
                if (text == ownText)
                {
                    pos = head + captionDir * (CaptionMetres * s) + Vector3.down * (CaptionDropMetres * s);
                    if (line > 0f) k = Mathf.Clamp(CaptionLineMetres * s / line, 0.05f, 20f);
                    OnTop(text);
                    if (r != null) { r.sortingLayerID = TopLayer; r.sortingOrder = 32000; }
                }
                else
                {
                    pos = t.position;
                    pos.z = FirstPerson.EyeZ;
                    float d = Vector3.Distance(head, pos);
                    if (line > 0f && line < MinLineRadians * d) k = Mathf.Min(3f, MinLineRadians * d / line);
                }
                t.SetPositionAndRotation(pos, Quaternion.LookRotation(pos - head, Vector3.up));
                t.localScale *= k;
                shown++;
                // Logged once per line (the game types it out a letter at a time).
                bool fresh = lastLogged == null || !text.text.StartsWith(lastLogged);
                if (fresh || text.text.Length > lastLogged.Length) lastLogged = text.text;
                if (Plugin.Verbose && fresh)
                {
                    Plugin.Log.LogInfo($"WorldText: {(text == ownText ? "caption" : "facing")} '{text.text}' line {line:F2} units x{k:F2} at {pos}");
                }
            }
            Shown = shown;
            FirstPerson.RenderTime.Stop();
        }

        private void OnEnd(ScriptableRenderContext ctx, Camera cam)
        {
            for (int i = moved.Count - 1; i >= 0; i--)
            {
                var m = moved[i];
                if (m.t == null) continue;
                m.t.SetPositionAndRotation(m.pos, m.rot);
                m.t.localScale = m.scale;
                if (m.r != null) { m.r.sortingLayerID = m.layer; m.r.sortingOrder = m.order; }
            }
            moved.Clear();
        }

        /// <summary>World height of one line of the text (0 while TMP has not laid it out).</summary>
        private static float LineHeight(TextMeshPro text)
        {
            var b = text.textBounds;
            int lines = text.textInfo != null ? Mathf.Max(1, text.textInfo.lineCount) : 1;
            return b.size.y > 0f ? b.size.y / lines * Mathf.Abs(text.transform.lossyScale.y) : 0f;
        }

        // The caption is drawn through walls (a wall can be nearer than a metre): its own material instance with
        // TMP's depth test off. The flat game draws text without depth anyway.
        private void OnTop(TextMeshPro text)
        {
            if (!onTop.Add(text)) return;
            var mat = text.fontMaterial;   // a per-text instance
            if (mat != null) mat.SetFloat("unity_GUIZTestMode", (float)CompareFunction.Always);
        }

        private static int topLayer = int.MinValue;
        private static int TopLayer
        {
            get
            {
                if (topLayer == int.MinValue)
                {
                    var layers = SortingLayer.layers;
                    topLayer = layers.Length > 0 ? layers[layers.Length - 1].id : 0;
                }
                return topLayer;
            }
        }

        public void Destroy()
        {
            RenderPipelineManager.beginCameraRendering -= OnBegin;
            RenderPipelineManager.endCameraRendering -= OnEnd;
            OnEnd(default, null);
        }
    }
}
