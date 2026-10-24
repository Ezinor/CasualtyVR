using System.Collections.Generic;
using UnityEngine;

namespace CasualtyVR
{
    /// <summary>
    /// Art for the VR settings menu in the game's own style (docs/BUILD_PLAN.md Phase 3, VRMENU_REVIEW M3, decision V1:
    /// "full, if practical"). The game's UI is white pixel outlines on black, sliced sprites (found with
    /// --cvr-dbg-uidump: uiBlock for big panels, uiBlockNano for boxes and buttons, darkenedUiBlockNano for the selected
    /// crafting tab, the round pause buttons). Its glow and see-through panels are how the screen is composited: black adds
    /// nothing, the lines glow. These sprites are taken from the game by name; until the game has loaded them (the main
    /// menu) identical generated copies stand in. Our own pieces (glow, tab icons, the controller drawing) are drawn in
    /// the same 1-texel line style.
    /// </summary>
    internal static class MenuArt
    {
        /// <summary>Canvas pixels per sprite texel for the game's sprites (ppu 33.3 under the canvas' 100).</summary>
        public const float Texel = 3f;

        public static Sprite Frame, Nano, Solid, Glow, PauseRing;
        /// <summary>"game" when the game's own sprites were found, else "generated".</summary>
        public static string Source = "-";
        private static bool loaded;

        public static void Load()
        {
            if (loaded && Source == "game") return;
            var found = new Dictionary<string, Sprite>();
            foreach (var s in Resources.FindObjectsOfTypeAll<Sprite>())
                if (s != null && (s.name == "uiBlock" || s.name == "uiBlockNano" || s.name == "pauseMenuSettings") && !found.ContainsKey(s.name)) found[s.name] = s;
            bool game = found.ContainsKey("uiBlock") && found.ContainsKey("uiBlockNano");
            Frame = game ? found["uiBlock"] : Outline(64, new Vector4(3, 3, 3, 3), "uiBlock (generated)");
            Nano = game ? found["uiBlockNano"] : Outline(16, new Vector4(1, 2, 1, 3), "uiBlockNano (generated)");
            if (Solid == null) Solid = Make(Fill(8, 8, (x, y) => 1f), new Vector4(2, 2, 2, 2), "solid");
            if (Glow == null) Glow = MakeGlow();
            if (PauseRing == null || game) PauseRing = found.TryGetValue("pauseMenuSettings", out var gear) ? Ring(gear) : GeneratedRing();
            Source = game ? "game" : "generated";
            if (!loaded || game) Plugin.Log.LogInfo($"VR settings: frames from the {(game ? "game's own sprites (uiBlock, uiBlockNano)" : "generated copies (the game's are not loaded yet)")}");
            loaded = true;
        }

        // ---- the game's frame style ------------------------------------------------------------------------

        /// <summary>A white 1-texel outline with rounded corners on black, sliced at the border (as the game's uiBlock*).</summary>
        private static Sprite Outline(int n, Vector4 border, string name)
        {
            var px = Fill(n, n, (x, y) =>
            {
                bool edge = x == 0 || y == 0 || x == n - 1 || y == n - 1;
                bool corner = (x == 0 || x == n - 1) && (y == 0 || y == n - 1);
                return edge && !corner ? 1f : -1f;   // -1 = black, opaque
            });
            return Make(px, border, name);
        }

        /// <summary>A soft halo for behind a frame: transparent in the middle, glowing at the edge band.</summary>
        private static Sprite MakeGlow()
        {
            const int n = 64, pad = 16;
            var px = Fill(n, n, (x, y) =>
            {
                float dx = Mathf.Max(pad - x, x - (n - 1 - pad), 0f), dy = Mathf.Max(pad - y, y - (n - 1 - pad), 0f);
                float outside = Mathf.Sqrt(dx * dx + dy * dy);   // distance outside the inner box
                float inside = Mathf.Min(Mathf.Min(x - pad, n - 1 - pad - x), Mathf.Min(y - pad, n - 1 - pad - y));
                float d = outside > 0f ? outside : -inside;
                return Mathf.Exp(-(d * d) / (2f * 5f * 5f)) * (d < 0f ? 0.6f : 1f);
            }, alphaOnly: true);
            var s = Make(px, new Vector4(pad + 4, pad + 4, pad + 4, pad + 4), "glow");
            s.texture.filterMode = FilterMode.Bilinear;
            return s;
        }

        /// <summary>The round outline of the game's pause buttons, without the gear inside.</summary>
        private static Sprite Ring(Sprite gear)
        {
            var src = Readable(gear);
            int w = src.width, h = src.height;
            float c = (w - 1) * 0.5f;
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float r = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    var col = src.GetPixel(x, y);
                    px[y * w + x] = r >= c - 2.5f ? col : r < c - 2.5f && col.a > 0.5f ? Color.black : col;
                }
            Object.Destroy(src);
            var t = NewTex(w, h);
            t.SetPixels(px);
            t.Apply();
            return Sprite.Create(t, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), gear.pixelsPerUnit);
        }

        private static Sprite GeneratedRing()
        {
            const int n = 40;
            float c = (n - 1) * 0.5f;
            var px = Fill(n, n, (x, y) =>
            {
                float r = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                return r > c + 0.5f ? 0f : r >= c - 0.9f ? 1f : -1f;
            });
            return Make(px, Vector4.zero, "ring", 33.333f);
        }

        // ---- tab icons: 8x8, shown at 3 canvas pixels a texel like the crafting categories ---------------------

        private static readonly Dictionary<string, string[]> IconMaps = new Dictionary<string, string[]>
        {
            ["Comfort"] = new[] { "........", ".##.##..", "#######.", "#######.", ".#####..", "..###...", "...#....", "........" },
            ["View"] = new[] { "........", "..####..", ".#....#.", "#..##..#", "#..##..#", ".#....#.", "..####..", "........" },
            ["First person"] = new[] { "...##...", "..####..", "..####..", "...##...", ".######.", "#.####.#", "..#..#..", "..#..#.." },
            ["Hands & body"] = new[] { "..#.#.#.", "..#.#.#.", "#.#####.", "#.#####.", ".######.", "..#####.", "..####..", "..####.." },
            ["Controls"] = new[] { "........", ".######.", "#......#", "#.#..#.#", "###..#.#", "#.#....#", ".######.", "........" },
            ["Menus & HUD"] = new[] { "........", "#.######", "........", "#.######", "........", "#.######", "........", "........" },
            ["Passthrough"] = new[] { "########", "#......#", "#.#....#", "#..#...#", "#....#.#", "#......#", "########", "........" },
            ["About"] = new[] { "...##...", "...##...", "........", "..###...", "...##...", "...##...", "..####..", "........" },
        };

        /// <summary>The game's category colours, one per tab (crafting: medicine red, utilities green, food yellow...).</summary>
        public static Color IconColour(string tab)
        {
            switch (tab)
            {
                case "Comfort": return new Color(1f, 0.41f, 0.41f);
                case "First person": return new Color(0.70f, 0.78f, 1f);
                case "Hands & body": return new Color(0.85f, 0.65f, 0.52f);
                case "Controls": return new Color(0.42f, 1f, 0.37f);
                case "Menus & HUD": return new Color(1f, 0.98f, 0.38f);
                case "Passthrough": return new Color(0.4f, 0.9f, 1f);
                default: return Color.white;
            }
        }

        private static readonly Dictionary<string, Sprite> icons = new Dictionary<string, Sprite>();

        public static Sprite Icon(string tab)
        {
            if (icons.TryGetValue(tab, out var s)) return s;
            if (!IconMaps.TryGetValue(tab, out var map)) return null;
            s = Make(Fill(8, 8, (x, y) => map[7 - y][x] == '#' ? 1f : 0f), Vector4.zero, "icon " + tab, 33.333f);
            icons[tab] = s;
            return s;
        }

        // ---- the controller drawing (Controls page, VRMENU_REVIEW M4c) -----------------------------------------

        public const int PadW = 44, PadH = 96;

        /// <summary>Where each control is on the left controller's drawing, in texels from the bottom-left (mirrored for the right).</summary>
        public static Vector2 Anchor(string control)
        {
            switch (control.Substring(control.IndexOf('.') + 1))
            {
                case "trigger": return new Vector2(36, 57);
                case "grip": case "BothGrips": return new Vector2(12, 34);
                case "stickUp": return new Vector2(19, 79);
                case "stickDown": return new Vector2(19, 65);
                case "stickLeft": return new Vector2(12, 72);
                case "stickRight": return new Vector2(26, 72);
                case "stickClick": return new Vector2(19, 72);
                case "X": case "A": return new Vector2(31, 66);
                case "Y": case "B": return new Vector2(30, 78);
                case "Menu": return new Vector2(12, 59);
                default: return new Vector2(22, 70);
            }
        }

        private static Sprite pad;

        /// <summary>A Touch-style controller in the game's line style: face with stick and two buttons, ring, trigger, handle and grip.</summary>
        public static Sprite Controller()
        {
            if (pad != null) return pad;
            var lines = new bool[PadW, PadH];
            void Dot(int x, int y) { if (x >= 0 && y >= 0 && x < PadW && y < PadH) lines[x, y] = true; }
            void Ellipse(float cx, float cy, float rx, float ry, float from = 0f, float to = 360f)
            {
                for (float a = from; a <= to; a += 0.5f)
                    Dot(Mathf.RoundToInt(cx + Mathf.Cos(a * Mathf.Deg2Rad) * rx), Mathf.RoundToInt(cy + Mathf.Sin(a * Mathf.Deg2Rad) * ry));
            }
            void Line(float x0, float y0, float x1, float y1)
            {
                int n = Mathf.CeilToInt(Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0))) * 2 + 1;
                for (int i = 0; i <= n; i++) Dot(Mathf.RoundToInt(Mathf.Lerp(x0, x1, i / (float)n)), Mathf.RoundToInt(Mathf.Lerp(y0, y1, i / (float)n)));
            }
            Ellipse(22, 70, 17, 15);                 // face
            Ellipse(21, 72, 21, 20, 100f, 260f);     // tracking ring, outer side
            Ellipse(19, 72, 6, 6);                   // stick
            Ellipse(19, 72, 2, 2);
            Ellipse(31, 66, 3, 3);                   // lower button (X / A)
            Ellipse(30, 78, 3, 3);                   // upper button (Y / B)
            Ellipse(12, 59, 1.5f, 1.5f);             // menu (left only; the right's system button is not ours)
            Line(31, 56, 38, 60); Line(38, 60, 38, 54); Line(38, 54, 32, 52);   // trigger
            Line(15, 56, 13, 10); Line(30, 56, 28, 10); Line(13, 10, 16, 6); Line(28, 10, 25, 6); Line(16, 6, 25, 6);   // handle
            Line(10, 46, 9, 22); Line(9, 22, 13, 20); Line(10, 46, 14, 48);    // grip
            var px = Fill(PadW, PadH, (x, y) => lines[x, y] ? 1f : 0f);
            pad = Make(px, PadW, PadH, Vector4.zero, "controller", 33.333f);
            return pad;
        }

        // ---- helpers ---------------------------------------------------------------------------------------

        /// <summary>value: 1 white, -1 black (opaque), 0 transparent; alphaOnly: white with that alpha.</summary>
        private static Color[] Fill(int w, int h, System.Func<int, int, float> f, bool alphaOnly = false)
        {
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float v = f(x, y);
                    px[y * w + x] = alphaOnly ? new Color(1f, 1f, 1f, Mathf.Clamp01(v)) : v > 0f ? Color.white : v < 0f ? Color.black : new Color(0f, 0f, 0f, 0f);
                }
            return px;
        }

        private static Sprite Make(Color[] px, Vector4 border, string name, float ppu = 33.3f) => Make(px, (int)Mathf.Sqrt(px.Length), (int)Mathf.Sqrt(px.Length), border, name, ppu);

        private static Sprite Make(Color[] px, int w, int h, Vector4 border, string name, float ppu)
        {
            var t = NewTex(w, h);
            t.SetPixels(px);
            t.Apply();
            var s = Sprite.Create(t, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), ppu, 0, SpriteMeshType.FullRect, border);
            s.name = name;
            return s;
        }

        private static Texture2D NewTex(int w, int h) =>
            new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "CasualtyVR.MenuArt" };

        /// <summary>A readable copy of a sprite's pixels (the game's textures are not readable).</summary>
        private static Texture2D Readable(Sprite s)
        {
            var tex = s.texture;
            var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prev = RenderTexture.active;
            Graphics.Blit(tex, rt);
            RenderTexture.active = rt;
            var r = s.textureRect;
            var t = new Texture2D((int)r.width, (int)r.height, TextureFormat.RGBA32, false);
            t.ReadPixels(new Rect(r.x, r.y, r.width, r.height), 0, 0);
            t.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return t;
        }
    }
}
