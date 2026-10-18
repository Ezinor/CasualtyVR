using UnityEngine;

namespace CasualtyVR.Probe
{
    /// <summary>
    /// --cvr-dbg-imgui: a mod-style OnGUI window in the middle of the screen (GUI.Window + GUILayout), so the IMGUI
    /// capture (CasualtyVR.ImguiCapture) can be tested without another mod installed. Logs when the pointer is
    /// inside it and when its button is clicked.
    /// </summary>
    internal sealed class ImguiTestWindow : MonoBehaviour
    {
        private Rect window;
        private int clicks;
        private bool hoverLogged;
        private bool targetLogged;

        private void OnGUI()
        {
            if (Time.timeSinceLevelLoad < 3f) return;
            if (Event.current.type == EventType.Repaint && !targetLogged && Time.timeSinceLevelLoad > 20f)
            {
                targetLogged = true;
                ProbePlugin.Log.LogInfo($"IMGUI test: repaints into '{(RenderTexture.active != null ? RenderTexture.active.name : "the screen")}'");
            }
            float w = 700f, h = 400f;
            window = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            window = GUI.Window(0x5C71, window, DrawWindow, "CasualtyVR IMGUI test");
            GUI.Label(new Rect(20f, 20f, 400f, 30f), "IMGUI test label (top-left)");
            if (Event.current.type == EventType.Repaint && window.Contains(Event.current.mousePosition) && !hoverLogged)
            {
                hoverLogged = true;
                ProbePlugin.Log.LogInfo($"IMGUI test: pointer inside the window at {Event.current.mousePosition}");
            }
        }

        private void DrawWindow(int id)
        {
            if (GUILayout.Button($"Test button ({clicks} clicks)", GUILayout.ExpandHeight(true), GUILayout.ExpandWidth(true)))
            {
                clicks++;
                ProbePlugin.Log.LogInfo($"IMGUI test: button clicked ({clicks})");
            }
        }
    }
}
