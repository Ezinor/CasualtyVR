using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CasualtyVR
{
    /// <summary>
    /// A short message on the monitor (polish, 2026-10-07): when VR cannot start, the reason was only in the
    /// BepInEx log, so pressing F10 with no runtime running seemed to do nothing. Top of the screen, a few seconds.
    /// </summary>
    internal sealed class ScreenNotice : MonoBehaviour
    {
        private static ScreenNotice instance;
        private TextMeshProUGUI text;
        private Image back;
        private float hideAt;

        public static void Show(string message, float seconds = 10f)
        {
            if (instance == null)
            {
                var go = new GameObject("CasualtyVR.Notice", typeof(Canvas), typeof(CanvasScaler));
                DontDestroyOnLoad(go);
                var canvas = go.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 32001;
                var scaler = go.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;
                instance = go.AddComponent<ScreenNotice>();

                var panel = new GameObject("Back", typeof(RectTransform), typeof(Image));
                panel.transform.SetParent(go.transform, false);
                instance.back = panel.GetComponent<Image>();
                instance.back.color = new Color(0.05f, 0.06f, 0.08f, 0.92f);
                instance.back.raycastTarget = false;
                var prt = (RectTransform)panel.transform;
                prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 1f);
                prt.pivot = new Vector2(0.5f, 1f);
                prt.anchoredPosition = new Vector2(0f, -40f);
                prt.sizeDelta = new Vector2(1400f, 120f);

                var label = new GameObject("Text", typeof(RectTransform));
                label.transform.SetParent(panel.transform, false);
                var t = label.AddComponent<TextMeshProUGUI>();
                if (PauseHandler.main != null && PauseHandler.main.pauseText != null) t.font = PauseHandler.main.pauseText.font;
                t.fontSize = 30f;
                t.enableAutoSizing = true;
                t.fontSizeMin = 16f;
                t.fontSizeMax = 30f;
                t.alignment = TextAlignmentOptions.Center;
                t.color = new Color(1f, 0.85f, 0.5f);
                t.raycastTarget = false;
                var lrt = (RectTransform)label.transform;
                lrt.anchorMin = Vector2.zero;
                lrt.anchorMax = Vector2.one;
                lrt.offsetMin = new Vector2(24f, 12f);
                lrt.offsetMax = new Vector2(-24f, -12f);
                instance.text = t;
            }
            Plugin.Log.LogInfo("Notice: " + message.Replace('\n', ' '));
            instance.text.text = message;
            instance.hideAt = Time.unscaledTime + seconds;
            instance.gameObject.SetActive(true);
        }

        private void Update()
        {
            if (Time.unscaledTime >= hideAt) gameObject.SetActive(false);
        }
    }
}
