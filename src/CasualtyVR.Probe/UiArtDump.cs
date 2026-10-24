using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace CasualtyVR.Probe
{
    /// <summary>
    /// --cvr-dbg-uidump: lists the Image components of the game's crafting panel and pause menu (sprite, slicing border,
    /// colour, material, size) and saves each distinct sprite as a PNG in BepInEx/CasualtyVR/uidump, so the VR settings
    /// menu can reuse the game's own frames and buttons (docs/BUILD_PLAN.md Phase 3, VRMENU_REVIEW M3a).
    /// </summary>
    internal sealed class UiArtDump : MonoBehaviour
    {
        private bool done;

        private void Update()
        {
            if (done || PlayerCamera.main == null || PauseHandler.main == null || Time.timeSinceLevelLoad < 12f) return;
            done = true;
            string dir = Path.Combine(BepInEx.Paths.BepInExRootPath, "CasualtyVR", "uidump");
            Directory.CreateDirectory(dir);
            var saved = new HashSet<Sprite>();
            Dump("crafting", PlayerCamera.main.craftingPanel, dir, saved);
            Dump("pause", PauseHandler.main.pauseContainer, dir, saved);
            Dump("woundview", PlayerCamera.main.woundView, dir, saved);
            ProbePlugin.Log.LogInfo($"UI dump: {saved.Count} sprites saved to {dir}");
        }

        private static void Dump(string label, GameObject root, string dir, HashSet<Sprite> saved)
        {
            if (root == null) { ProbePlugin.Log.LogInfo($"UI dump: {label}: none"); return; }
            foreach (var img in root.GetComponentsInChildren<Image>(true))
            {
                var s = img.sprite;
                string path = img.name;
                for (var t = img.transform.parent; t != null && t != root.transform.parent; t = t.parent) path = t.name + "/" + path;
                var r = img.rectTransform.rect;
                ProbePlugin.Log.LogInfo($"UI dump: {label}: {path} sprite '{(s != null ? s.name : "-")}' tex '{(s != null ? s.texture.name : "-")}' rect {(s != null ? s.textureRect.ToString() : "-")} border {(s != null ? s.border.ToString() : "-")} ppu {(s != null ? s.pixelsPerUnit : 0f)} type {img.type} ppuMult {img.pixelsPerUnitMultiplier} color {img.color} mat '{(img.material != null ? img.material.name : "-")}' shader '{(img.material != null ? img.material.shader.name : "-")}' size {r.width:F0}x{r.height:F0} active {img.gameObject.activeInHierarchy}");
                if (s != null && saved.Add(s)) Save(s, Path.Combine(dir, Safe(s.name) + ".png"));
            }
        }

        private static string Safe(string n)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) n = n.Replace(c, '_');
            return n;
        }

        private static void Save(Sprite s, string file)
        {
            var tex = s.texture;
            var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prev = RenderTexture.active;
            Graphics.Blit(tex, rt);
            RenderTexture.active = rt;
            var rect = s.textureRect;
            var outTex = new Texture2D(Mathf.Max(1, (int)rect.width), Mathf.Max(1, (int)rect.height), TextureFormat.RGBA32, false);
            outTex.ReadPixels(new Rect(rect.x, rect.y, outTex.width, outTex.height), 0, 0);
            outTex.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            File.WriteAllBytes(file, outTex.EncodeToPNG());
            Object.Destroy(outTex);
        }
    }
}
