using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CasualtyVR
{
    /// <summary>
    /// IMGUI windows in the headset (2026-10-17: the co-op mod's menus, player-interaction window and chat, and any
    /// other mod's OnGUI windows, never reached the headset). IMGUI draws straight to the window after every camera,
    /// so VrUi's UI camera cannot see it. While VR runs, every OnGUI repaint is redirected into a texture of the
    /// window's size:
    /// * ImguiFirst (GUI.depth +2^28) is the first behaviour to repaint: it sets RenderTexture.active to the capture
    ///   texture and clears it; ImguiLast (GUI.depth -2^28) is the last and restores the target. Both need useGUILayout
    ///   on: with it off they were not sorted by depth and both ran before a depth-0 window (measured 2026-10-17).
    ///   +-2^28 rather than int.MaxValue/MinValue keeps clear of overflow in a subtracting comparison. The
    ///   texture is shown on top of the UI on every VR panel and, on the monitor, on a quad in front of the game
    ///   camera (VrUi), so the monitor still shows the windows (one frame later).
    /// * Pointer and clicks: IMGUI reads the window's own mouse messages, not Input.mousePosition, and each behaviour
    ///   gets its own copy of an event (rewriting Event.current in one OnGUI reached no other window, measured
    ///   2026-10-17). So while the laser drives the mouse, its position and its mouse-button presses (VrInput) are
    ///   posted to the game window as WM_MOUSEMOVE / button messages at the laser's pixel: Unity turns them into real
    ///   IMGUI events for every mod. The OS cursor does not move. VrInput ignores the "real mouse moved" these
    ///   messages cause (IsOwnPosition).
    /// * Where IMGUI drew: a quarter-size copy of the texture's alpha is read back a few times a second. It tells
    ///   whether there is anything to show (HasContent) and, in first person, where (Covers): the HUD panel is in
    ///   front of you there, not on the world, so pointing at a window must take the laser off the world.
    /// </summary>
    internal static class ImguiCapture
    {
        public static RenderTexture Texture { get; private set; }
        internal const int FirstDepth = 1 << 28, LastDepth = -(1 << 28);
        public static bool Running => host != null;
        /// <summary>
        /// Something was drawn in a recent capture (the readback, a few times a second). Until then the panels skip
        /// the IMGUI layer: drawing an empty full-panel layer cost about 1.5 ms a frame in a GPU-bound view.
        /// </summary>
        public static bool HasContent { get; private set; }

        private static GameObject host;
        private static bool capturing;
        private static RenderTexture previousTarget;
        private static readonly bool[] buttonWas = new bool[2];
        private static Vector2 lastPosted = new Vector2(-1f, -1f);
        private static System.IntPtr window;

        // Coverage readback (first person).
        private const int ProbeDivisor = 4;
        private static RenderTexture probe;
        private static byte[] coverage;
        private static int coverageW, coverageH;
        private static bool readbackPending;
        private static int lastReadbackFrame = -1000;
        private static bool loggedFirstCapture;

        public static void Start()
        {
            if (host != null) return;
            host = new GameObject("CasualtyVR.ImguiCapture") { hideFlags = HideFlags.HideAndDontSave };
            Object.DontDestroyOnLoad(host);
            host.AddComponent<ImguiFirst>();
            host.AddComponent<ImguiLast>();
            buttonWas[0] = buttonWas[1] = false;
            lastPosted = new Vector2(-1f, -1f);
            loggedFirstCapture = false;
            EnsureTexture();
            Plugin.Log.LogInfo("IMGUI: capturing OnGUI windows for the headset");
        }

        public static void Stop()
        {
            EndOfFrame();
            ReleaseButtons();
            if (host != null) Object.Destroy(host);
            host = null;
            if (Texture != null) { Texture.Release(); Object.Destroy(Texture); Texture = null; }
            if (probe != null) { probe.Release(); Object.Destroy(probe); probe = null; }
            coverage = null;
            readbackPending = false;
            HasContent = false;
        }

        /// <summary>The capture texture follows the window's size.</summary>
        public static void EnsureTexture()
        {
            int w = Screen.width, h = Screen.height;
            if (Texture != null && Texture.width == w && Texture.height == h) return;
            if (Texture != null) { Texture.Release(); Object.Destroy(Texture); }
            Texture = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "CasualtyVR.ImguiTexture" };
            Texture.Create();
            if (probe != null) { probe.Release(); Object.Destroy(probe); probe = null; }
            coverage = null;
        }

        /// <summary>
        /// Once per frame after VrInput.Update: the laser's position and mouse buttons (whatever controls are bound to
        /// them) go to the game window as mouse messages, which Unity turns into IMGUI events.
        /// </summary>
        public static void QueueClicks()
        {
            if (host == null) return;
            if (!VrInput.Enabled || !VrInput.LaserMode) { ReleaseButtons(); return; }
            Vector2 mouse = VrInput.VirtualMouse;
            bool[] down = { VrInput.Held(KeyCode.Mouse0), VrInput.Held(KeyCode.Mouse1) };
            if ((mouse - lastPosted).sqrMagnitude > 0.25f) Post(WmMouseMove, mouse, down);
            for (int b = 0; b < 2; b++)
            {
                if (down[b] == buttonWas[b]) continue;
                Post(down[b] ? (b == 0 ? WmLButtonDown : WmRButtonDown) : (b == 0 ? WmLButtonUp : WmRButtonUp), mouse, down);
                if (Plugin.Verbose) Plugin.Log.LogInfo($"IMGUI: {(b == 0 ? "left" : "right")} button {(down[b] ? "down" : "up")} at {mouse}");
                buttonWas[b] = down[b];
            }
        }

        /// <summary>Whether a "real" mouse position is the one this class posted (not the player moving the mouse).</summary>
        public static bool IsOwnPosition(Vector2 screen) => Running && (screen - lastPosted).sqrMagnitude < 9f;

        private static void ReleaseButtons()
        {
            for (int b = 0; b < 2; b++)
                if (buttonWas[b])
                {
                    buttonWas[b] = false;
                    Post(b == 0 ? WmLButtonUp : WmRButtonUp, lastPosted, buttonWas);
                }
        }

        private const uint WmMouseMove = 0x0200, WmLButtonDown = 0x0201, WmLButtonUp = 0x0202, WmRButtonDown = 0x0204, WmRButtonUp = 0x0205;

        private static void Post(uint message, Vector2 screen, bool[] down)
        {
            if (window == System.IntPtr.Zero) window = FindGameWindow();
            if (window == System.IntPtr.Zero || !GetClientRect(window, out var rc)) return;
            int cw = rc.Right - rc.Left, ch = rc.Bottom - rc.Top;
            if (cw <= 0 || ch <= 0) return;
            // Unity screen pixels (origin bottom-left) -> client pixels (origin top-left).
            int x = Mathf.Clamp(Mathf.RoundToInt(screen.x / Screen.width * cw), 0, cw - 1);
            int y = Mathf.Clamp(Mathf.RoundToInt((1f - screen.y / Screen.height) * ch), 0, ch - 1);
            long keys = (down[0] ? 1 : 0) | (down[1] ? 2 : 0);   // MK_LBUTTON, MK_RBUTTON
            PostMessage(window, message, new System.IntPtr(keys), new System.IntPtr((y << 16) | (x & 0xFFFF)));
            lastPosted = screen;
        }

        // Unity's window belongs to the main thread, which calls this.
        private static System.IntPtr FindGameWindow()
        {
            System.IntPtr found = System.IntPtr.Zero;
            var name = new System.Text.StringBuilder(64);
            EnumThreadWindows(GetCurrentThreadId(), (h, unused) =>
            {
                name.Length = 0;
                GetClassName(h, name, name.Capacity);
                if (name.ToString() == "UnityWndClass") { found = h; return false; }
                return true;
            }, System.IntPtr.Zero);
            Plugin.Log.LogInfo(found != System.IntPtr.Zero ? "IMGUI: game window found for pointer messages" : "IMGUI: game window not found; mod windows will not take laser clicks");
            return found;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct Rect32 { public int Left, Top, Right, Bottom; }
        private delegate bool EnumWindowsProc(System.IntPtr hWnd, System.IntPtr lParam);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool PostMessage(System.IntPtr hWnd, uint msg, System.IntPtr wParam, System.IntPtr lParam);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool GetClientRect(System.IntPtr hWnd, out Rect32 rect);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool EnumThreadWindows(uint threadId, EnumWindowsProc callback, System.IntPtr lParam);
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern int GetClassName(System.IntPtr hWnd, System.Text.StringBuilder name, int max);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

        /// <summary>The first behaviour of every repaint: the capture starts.</summary>
        internal static void OnFirst()
        {
            var e = Event.current;
            if (e == null || e.type != EventType.Repaint || Texture == null) return;
            previousTarget = RenderTexture.active;
            RenderTexture.active = Texture;
            GL.Clear(false, true, Color.clear);
            capturing = true;
        }

        /// <summary>The last behaviour of every repaint: the capture ends.</summary>
        internal static void OnLast()
        {
            if (Event.current == null || Event.current.type != EventType.Repaint) return;
            Restore();
        }

        private static void Restore()
        {
            if (!capturing) return;
            RenderTexture.active = previousTarget;
            previousTarget = null;
            capturing = false;
            if (!loggedFirstCapture) { loggedFirstCapture = true; Plugin.Log.LogInfo("IMGUI: first frame captured"); }
            if (DbgDump && !dumped && Time.timeSinceLevelLoad > 25f) Dump();
        }

        // --cvr-dbg-imgui-dump: the captured texture as a PNG once, 25 s into a level (capture diagnostics).
        private static readonly bool DbgDump = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "--cvr-dbg-imgui-dump") >= 0;
        private static bool dumped;

        private static void Dump()
        {
            dumped = true;
            var prev = RenderTexture.active;
            RenderTexture.active = Texture;
            var tex = new Texture2D(Texture.width, Texture.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, Texture.width, Texture.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            int covered = 0;
            foreach (var c in tex.GetPixels32()) if (c.a > 24) covered++;
            string path = System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "CasualtyVR", "imgui-capture.png");
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Plugin.Log.LogInfo($"IMGUI: capture dumped to {path}: {covered} of {tex.width * tex.height} pixels drawn");
            Object.Destroy(tex);
        }

        /// <summary>End of frame (VrRig): never leave the capture target bound; refresh the first-person coverage map.</summary>
        public static void EndOfFrame()
        {
            Restore();
            if (Texture == null || readbackPending || Time.frameCount - lastReadbackFrame < 6) return;
            if (!SystemInfo.supportsAsyncGPUReadback) return;
            int w = Mathf.Max(1, Texture.width / ProbeDivisor), h = Mathf.Max(1, Texture.height / ProbeDivisor);
            if (probe == null || probe.width != w || probe.height != h)
            {
                if (probe != null) { probe.Release(); Object.Destroy(probe); }
                probe = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { name = "CasualtyVR.ImguiProbe" };
                probe.Create();
            }
            lastReadbackFrame = Time.frameCount;
            Graphics.Blit(Texture, probe);
            readbackPending = true;
            AsyncGPUReadback.Request(probe, 0, TextureFormat.RGBA32, r =>
            {
                readbackPending = false;
                if (r.hasError || probe == null) return;
                var data = r.GetData<byte>();
                if (coverage == null || coverage.Length != data.Length) coverage = new byte[data.Length];
                data.CopyTo(coverage);
                coverageW = r.width;
                coverageH = r.height;
                bool any = false;
                for (int i = 3; i < coverage.Length; i += 4) if (coverage[i] > 8) { any = true; break; }
                if (any != HasContent && Plugin.Verbose) Plugin.Log.LogInfo($"IMGUI: {(any ? "windows on screen" : "nothing drawn")}");
                HasContent = any;
            });
        }

        /// <summary>
        /// First person: whether IMGUI drew something at this screen pixel (bottom-left origin) in a recent frame,
        /// with a little slack around it (small buttons).
        /// </summary>
        public static bool Covers(Vector2 screen)
        {
            if (coverage == null || Texture == null) return false;
            int cx = Mathf.FloorToInt(screen.x / Texture.width * coverageW);
            int cy = Mathf.FloorToInt(screen.y / Texture.height * coverageH);
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int x = cx + dx, y = cy + dy;
                    if (x < 0 || y < 0 || x >= coverageW || y >= coverageH) continue;
                    if (coverage[(y * coverageW + x) * 4 + 3] > 24) return true;
                }
            return false;
        }
    }

    /// <summary>Drawn first (GUI.depth +2^28): starts the capture of each repaint.</summary>
    internal sealed class ImguiFirst : MonoBehaviour
    {

        private void OnGUI()
        {
            GUI.depth = ImguiCapture.FirstDepth;
            ImguiCapture.OnFirst();
        }
    }

    /// <summary>Drawn last (GUI.depth -2^28): ends the capture of each repaint.</summary>
    internal sealed class ImguiLast : MonoBehaviour
    {

        private void OnGUI()
        {
            GUI.depth = ImguiCapture.LastDepth;
            ImguiCapture.OnLast();
        }
    }
}
