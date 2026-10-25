using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CasualtyVR
{
    /// <summary>
    /// In-game VR settings (user request 2026-10-04), all backed by the BepInEx config (saved as they change). Opened
    /// from a "VR" button added to the game's pause menu, or the SettingsKey (F12). It is an ordinary overlay canvas, so
    /// VrUi moves it to the UI camera; in the headset it gets its own panel, cropped to the menu and opened in front of
    /// you (docs/VRMENU_REVIEW.md M1a, M5b). The controller laser clicks it.
    ///
    /// Layout (VRMENU_REVIEW M1b, M2, M4): tabs by task; up to eight big rows a page, scrolled with a stick, the wheel
    /// or the arrows; each row is a switch (click anywhere on the row), segmented choices, a slider (drag, with fine
    /// &lt; &gt; at the ends) or a stepper. Tuning rows are behind "More settings". Pointing at a row shows its
    /// description (the config text) under the list, and a row that differs from its default has a reset button.
    /// Every control is at least 2.5 degrees across at the default panel size (checked headless, LogCheck).
    /// </summary>
    internal sealed class VrSettingsMenu : MonoBehaviour
    {
        /// <summary>
        /// While the menu is open, no game tooltips: the game shows the tooltip of anything under the pointer on
        /// any canvas (UIUtil.GetUITooltip walks every raycast result), so HUD tooltips popped up over the rows.
        /// </summary>
        [HarmonyLib.HarmonyPatch(typeof(UIUtil), nameof(UIUtil.GetUITooltip))]
        private static class NoTooltips
        {
            private static void Postfix(ref (string, string) __result)
            {
                if (IsOpen) __result = ("", "");
            }
        }

        /// <summary>...and no world hover labels (the block, item or liquid under the pointer, PlayerCamera).</summary>
        [HarmonyLib.HarmonyPatch(typeof(GlobalDark), nameof(GlobalDark.SetTooltip))]
        private static class NoWorldLabels
        {
            private static bool Prefix() => !IsOpen;
        }

        public static VrSettingsMenu Instance;
        public static bool IsOpen => Instance != null && Instance.root != null && Instance.root.activeSelf;

        // ---- layout, in the canvas's reference pixels (1920x1080 reference, so the menu is these pixels on a 1080p
        // window). At the default panel (0.95 m wide, 0.8 m away) a pixel is 0.74 mm. Every hit area is at least 72 px
        // (53 mm): 3.8 degrees in the middle, still 2.7 at the panel's corners (LogCheck).
        private const float MenuW = 1280f, MenuH = 1016f, Side = 40f;
        private const float HeadY = 16f, HeadH = 72f, TabY = 100f, TabH = 72f;   // two rows of four tabs
        private const float BodyY = 264f, RowPitch = 80f, RowH = 72f;
        private const int RowsShown = 7;
        private const float DescY = 832f, DescH = 88f, BarY = 928f, BarH = 72f;
        private const float BodyW = MenuW - 2f * Side;            // 1200
        private const float ScrollW = 72f, RowW = BodyW - ScrollW - 8f;
        private const float LabelX = 16f, LabelW = 380f, CtrlX = 404f, Btn = 72f;
        private const float CtrlW = RowW - CtrlX - Btn - 20f;     // control area, left of the reset button

        // The game's look (Phase 3, VRMENU_REVIEW M3, V1): boxes and buttons are the game's own white outline sprites,
        // tinted (MenuArt); the panel has the game's big frame and a soft glow; VrUi draws it additively over a dark
        // see-through backing, as the game's screen is composited (black adds nothing, lines and text glow). Colours are
        // its palette (§2): periwinkle lines and labels, white values, green for on, yellow for warnings.
        private static readonly Color FrameCol = new Color(0.72f, 0.78f, 1f);              // the outer frame
        private static readonly Color GlowCol = new Color(0.38f, 0.48f, 1f, 0.5f);
        private static readonly Color RowCol = new Color(0.28f, 0.32f, 0.52f), RowHover = new Color(0.62f, 0.70f, 1f);   // row frames
        private static readonly Color HeaderRowCol = new Color(0.09f, 0.12f, 0.28f);       // filled
        private static readonly Color LabelCol = new Color(0.76f, 0.81f, 1f);              // periwinkle labels
        private static readonly Color OnCol = new Color(0.30f, 0.78f, 0.42f);              // green: on (filled)
        private static readonly Color AccentCol = new Color(0.30f, 0.38f, 0.86f);          // selected tab / segment (filled)
        private static readonly Color ButtonCol = new Color(0.50f, 0.57f, 0.88f);          // button frames
        private static readonly Color TrackCol = new Color(0.15f, 0.18f, 0.36f);           // slider bars, the scroll track
        private static readonly Color DimText = new Color(0.58f, 0.61f, 0.72f);
        private static readonly Color WarnCol = new Color(1f, 0.78f, 0.35f);
        /// <summary>Room around the frame for the glow; the cropped panel includes it.</summary>
        private const float Margin = 28f;

        // Tabs by task (M4a), in two rows of four: eight across left "Passthrough" at 9 dp.
        private static readonly string[] Tabs = { "Comfort", "View", "First person", "Hands & body", "Controls", "Menus & HUD", "Passthrough", "About" };
        private static readonly (string name, Color c)[] KeyPresets =
        {
            ("Green", new Color(0f, 1f, 0f)), ("Blue", new Color(0f, 0f, 1f)), ("Magenta", new Color(1f, 0f, 1f)),
            ("Black", Color.black), ("White", Color.white),
        };

        private enum Kind { Switch, Choice, Slider, OptSlider, Stepper, Info, Header }

        private sealed class Row
        {
            public Kind Kind;
            public string Label, Desc;
            public bool More;       // only with "More settings" (M4b)
            public bool NoReset;    // mode switches: not reset with their page
            public ConfigEntryBase[] Entries = Array.Empty<ConfigEntryBase>();
            public Func<bool> Active;                                   // null = always; else dimmed when false
            public Func<bool> GetOn; public Action<bool> SetOn;          // Switch, OptSlider
            public string[] Options; public Func<int> GetIndex; public Action<int> SetIndex;   // Choice
            public float Min, Max, Step; public Func<float> Get; public Action<float> Set; public Func<float, string> Format;   // Slider
            public Func<string> Value; public Action<int> StepBy;       // Stepper, Info
            public string Control;  // a controller binding: shown on the controller picture, not as a row (M4c)
        }

        private GameObject canvasGo, root;
        private RectTransform body, scrollTrack, scrollThumb;
        private GameObject scrollUp, scrollDown;
        private TextMeshProUGUI desc, pictureDesc, moreLabel, resetLabel;
        private GameObject descBg;
        private string picking;   // Controls: the control whose action is being chosen
        private Button moreButton;
        private TMP_FontAsset font;
        private Sprite resetIcon, arrowIcon;
        private string tab = "Comfort";
        private bool showMore;
        private int scroll;
        private List<Row> rows = new List<Row>();
        private List<Row> shown = new List<Row>();
        private readonly List<Action> refreshers = new List<Action>();
        private readonly List<(Image image, TextMeshProUGUI text, string name)> tabButtons = new List<(Image, TextMeshProUGUI, string)>();
        private PauseHandler injectedFor;
        private float nextRefresh, nextStickScroll, lastTick;
        private bool stickHeld;
        // "Reset page" (user, 2026-10-06): the first press arms it ("Press again"), a second within 3 s resets.
        private float resetArmedUntil = -1f;
        private bool loggingChanges;

        private void Awake() => Instance = this;

        // ---- headless tests -----------------------------------------------------------------------------------
        // --cvr-dbg-settings <tab>: opens on that tab once gameplay is up; "all" shows each tab in turn, 3 s each.
        // --cvr-dbg-settings-more: with "More settings" shown. --cvr-dbg-reset: presses "Reset page" twice.
        // --cvr-dbg-menu-aim: logs the xr-sim "hand r point" angles for a few controls (tools/vr-menu.ps1).
        private static readonly string DbgTab = ArgAfter("--cvr-dbg-settings");
        private static readonly bool DbgMore = Array.IndexOf(Environment.GetCommandLineArgs(), "--cvr-dbg-settings-more") >= 0;
        private static readonly bool DbgAim = Array.IndexOf(Environment.GetCommandLineArgs(), "--cvr-dbg-menu-aim") >= 0;
        private bool dbgOpened;
        private float dbgResetAt = -1f, dbgNextTab, dbgCheckAt = -1f, dbgAimAt = -1f;

        private static string ArgAfter(string flag)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, flag);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        private void Update()
        {
            float now = Time.unscaledTime;
            if (Input.GetKeyDown(Plugin.SettingsKey.Value)) Toggle();
            DebugUpdate(now);
            InjectPauseButton();
            if (resetArmedUntil > 0f && now > resetArmedUntil) DisarmReset();
            if (!IsOpen) return;
            // Scrolling (M1b): either stick up/down (VrInput keeps it from the game meanwhile), the mouse wheel.
            float stick = VrInput.MenuStickY;
            if (Mathf.Abs(stick) > 0.6f)
            {
                if (now >= nextStickScroll)
                {
                    ScrollBy(stick > 0f ? -1 : 1);
                    nextStickScroll = now + (stickHeld ? 0.16f : 0.4f);
                    stickHeld = true;
                }
            }
            else if (Mathf.Abs(stick) < 0.4f) { stickHeld = false; nextStickScroll = 0f; }
            float wheel = Input.mouseScrollDelta.y;
            if (wheel != 0f) ScrollBy(wheel > 0f ? -1 : 1);
            if (now >= nextRefresh) RefreshAll();
        }

        private void DebugUpdate(float now)
        {
            if (DbgTab != null && !dbgOpened && PlayerCamera.main != null && VrRig.Instance != null && VrRig.Instance.Active && Time.timeSinceLevelLoad > 8f)
            {
                dbgOpened = true;
                Open();
                showMore = DbgMore;
                ShowTab(Array.Find(Tabs, t => t.StartsWith(DbgTab, StringComparison.OrdinalIgnoreCase)) ?? Tabs[0]);
                dbgNextTab = now + 3f;
                dbgResetAt = Array.IndexOf(Environment.GetCommandLineArgs(), "--cvr-dbg-reset") >= 0 ? now + 4f : -1f;
                if (DbgAim) dbgAimAt = now + 2f;
            }
            if (dbgOpened && DbgTab == "all" && IsOpen && now >= dbgNextTab)
            {
                dbgNextTab = now + 3f;
                ShowTab(Tabs[(Array.IndexOf(Tabs, tab) + 1) % Tabs.Length]);
            }
            if (dbgResetAt > 0f && now >= dbgResetAt)
            {
                ResetPage();
                dbgResetAt = resetArmedUntil > 0f ? now + 1f : -1f;
            }
            if (dbgCheckAt > 0f && now >= dbgCheckAt) { dbgCheckAt = -1f; LogCheck(); }
            if (dbgAimAt > 0f && now >= dbgAimAt) { dbgAimAt = -1f; LogAims(); }
        }

        public void Toggle()
        {
            if (IsOpen) Close(); else Open();
        }

        public void Open()
        {
            // Built in the main menu with stand-in sprites: rebuild with the game's own once a level has loaded them.
            if (root != null && MenuArt.Source != "game")
            {
                MenuArt.Load();
                if (MenuArt.Source == "game") { Destroy(canvasGo); tabButtons.Clear(); root = null; }
            }
            if (root == null) Build();
            if (!loggingChanges && Plugin.Verbose)
            {
                // Headless tests read what a click changed from here.
                loggingChanges = true;
                Plugin.FpOn.ConfigFile.SettingChanged += (s, e) =>
                {
                    if (IsOpen) Plugin.Log.LogInfo($"VR settings: {e.ChangedSetting.Definition.Section}.{e.ChangedSetting.Definition.Key} = {e.ChangedSetting.BoxedValue}");
                };
            }
            root.SetActive(true);
            ShowTab(tab, keepScroll: true);
            VrUi.Current?.PlaceSettingsAgain();
            Plugin.Log.LogInfo("VR settings: opened");
        }

        public void Close()
        {
            if (root != null) root.SetActive(false);
        }

        /// <summary>The part of the cropped panel (each side, as a fraction) outside the frame: VrUi's backing stops there.</summary>
        public static Vector2 BackingInset => new Vector2(Margin / (MenuW + 2f * Margin), Margin / (MenuH + 2f * Margin));

        /// <summary>The settings panel's width in UI-texture pixels at this window size (built or not): game menus zoomed
        /// on the dashboard (V7) get the same metres per pixel.</summary>
        public static float PanelPixelWidth()
        {
            // CanvasScaler: 1920x1080 reference, match 0.5 = the geometric mean of the width and height ratios.
            float s = Mathf.Sqrt(Screen.width / 1920f * (Screen.height / 1080f));
            return (MenuW + 2f * Margin) * s;
        }

        /// <summary>The menu's rectangle in UI-texture pixels (VrUi crops the panel to it).</summary>
        public Rect ScreenRect()
        {
            if (!IsOpen) return Rect.zero;
            var c = new Vector3[4];
            ((RectTransform)root.transform).GetWorldCorners(c);
            return Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y);
        }

        // ---- pause-menu entry ------------------------------------------------------------------------

        /// <summary>Clone the pause menu's Settings button into a "VR" button next to it.</summary>
        private void InjectPauseButton()
        {
            var ph = PauseHandler.main;
            if (ph == null || ph == injectedFor || ph.pauseContainer == null) return;
            injectedFor = ph;
            Button settings = null;
            foreach (var b in ph.pauseContainer.GetComponentsInChildren<Button>(true))
                for (int i = 0; i < b.onClick.GetPersistentEventCount(); i++)
                    if (b.onClick.GetPersistentMethodName(i) == "Settings") settings = b;
            if (settings == null) { Plugin.Log.LogWarning("VR settings: pause menu Settings button not found"); return; }

            var clone = Instantiate(settings.gameObject, settings.transform.parent);
            clone.name = "CasualtyVR.VrSettingsButton";
            var rt = (RectTransform)clone.transform;
            var src = (RectTransform)settings.transform;
            rt.localPosition = ArcPosition(ph, settings, src);
            rt.localScale = Vector3.one;
            var button = clone.GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(Open);
            // A round button like the game's three (M3d): their ring, with "VR" inside. Below the gear it was cut off at
            // the screen's edge (2026-10-04), so it continues their arc instead.
            MenuArt.Load();
            if (clone.TryGetComponent<Image>(out var img)) { img.sprite = MenuArt.PauseRing; img.color = Color.white; }
            for (int i = clone.transform.childCount - 1; i >= 0; i--) Destroy(clone.transform.GetChild(i).gameObject);
            if (clone.TryGetComponent<UITooltip>(out var tip))
            {
                tip.skipLocale = true;
                tip.tipName = "VR settings";
                tip.tipDesc = "Comfort, view, first person, hands, controls, menus, passthrough";
            }
            if (font == null && ph.pauseText != null) font = ph.pauseText.font;
            var label = NewText(rt, "VR", 44, TextAlignmentOptions.Center, Color.white);
            Stretch(label.rectTransform);
            Plugin.Log.LogInfo($"VR settings: round button added to the pause menu at {rt.localPosition} (gear at {src.localPosition})");
        }

        /// <summary>
        /// The next place on the arc of the pause menu's round buttons (return, quit, settings: equal steps around the big
        /// circle's centre), after the settings button.
        /// </summary>
        private static Vector3 ArcPosition(PauseHandler ph, Button settings, RectTransform src)
        {
            RectTransform circle = null, quit = null;
            foreach (var img in ph.pauseContainer.GetComponentsInChildren<Image>(true))
            {
                if (img.sprite == null || img.transform.parent != src.parent) continue;
                if (img.sprite.name == "pauseMenuCircle") circle = img.rectTransform;
                if (img.sprite.name == "pauseMenuDeath") quit = img.rectTransform;
            }
            if (circle == null || quit == null) return src.localPosition + new Vector3(src.rect.width * 1.4f, src.rect.height * 0.6f, 0f);
            Vector2 c = circle.localPosition, s = (Vector2)src.localPosition - c, q = (Vector2)quit.localPosition - c;
            float step = Vector2.SignedAngle(q, s);
            Vector2 next = Quaternion.Euler(0f, 0f, step) * s;
            return new Vector3(c.x + next.x, c.y + next.y, src.localPosition.z);
        }

        // ---- construction ----------------------------------------------------------------------------

        private void Build()
        {
            MenuArt.Load();
            font = PauseHandler.main != null && PauseHandler.main.pauseText != null ? PauseHandler.main.pauseText.font : TMP_Settings.defaultFontAsset;
            resetIcon = MakeResetIcon();
            arrowIcon = MakeArrowIcon();
            canvasGo = new GameObject("CasualtyVR.SettingsCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            DontDestroyOnLoad(canvasGo);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            // Root: black (adds nothing) and catches the pointer; the cropped panel. Inside it the glow, the game's big frame,
            // and the content panel that everything else is placed on.
            var rootImg = NewImage(canvasGo.transform, "Root", Color.black);
            root = rootImg.gameObject;
            var root_ = rootImg.rectTransform;
            root_.anchorMin = root_.anchorMax = new Vector2(0.5f, 0.5f);
            root_.sizeDelta = new Vector2(MenuW + 2f * Margin, MenuH + 2f * Margin);
            var glow = NewImage(root_, "Glow", GlowCol);
            glow.sprite = MenuArt.Glow;
            glow.type = Image.Type.Sliced;
            glow.pixelsPerUnitMultiplier = 1.2f;
            glow.raycastTarget = false;
            Stretch(glow.rectTransform);
            glow.rectTransform.offsetMin = new Vector2(Margin - 26f, Margin - 26f);
            glow.rectTransform.offsetMax = new Vector2(26f - Margin, 26f - Margin);
            var rrt = new GameObject("Panel", typeof(RectTransform)).GetComponent<RectTransform>();
            rrt.SetParent(root_, false);
            rrt.anchorMin = rrt.anchorMax = new Vector2(0.5f, 0.5f);
            rrt.sizeDelta = new Vector2(MenuW, MenuH);
            var frame = NewImage(rrt, "Frame", FrameCol);
            frame.sprite = MenuArt.Frame;
            frame.type = Image.Type.Sliced;
            frame.raycastTarget = false;
            Stretch(frame.rectTransform);

            var title = NewText(rrt, "VR settings", 46, TextAlignmentOptions.Left, Color.white);
            Place(title.rectTransform, Side, HeadY, 500f, HeadH);
            var done = NewButton(rrt, "Done", 34, Close);
            Place((RectTransform)done.transform, MenuW - Side - 200f, HeadY, 200f, HeadH);
            // Recenter is an action, not a setting: one button for every page. The panel moves in front of you again.
            var recenter = NewButton(rrt, "Recenter", 32, () => { VrRig.Instance?.Recenter(); VrUi.Current?.PlaceSettingsAgain(); });
            Place((RectTransform)recenter.transform, MenuW - Side - 200f - 16f - 240f, HeadY, 240f, HeadH);

            const int perRow = 4;
            float tw = (BodyW - (perRow - 1) * 8f) / perRow;
            for (int i = 0; i < Tabs.Length; i++)
            {
                var name = Tabs[i];
                var b = NewButton(rrt, name, 36, () => { if (tab != name) { showMore = false; picking = null; ShowTab(name); } });
                var t = b.GetComponentInChildren<TextMeshProUGUI>();
                // Long names ("Hands & body", "Passthrough") shrink to fit between the icon and the right edge; at a
                // 30-px floor they ran over both (user's headset screenshot, 2026-10-23).
                FitText(t, 24f);
                Place((RectTransform)b.transform, Side + (i % perRow) * (tw + 8f), TabY + (i / perRow) * (TabH + 8f), tw, TabH);
                // A pixel icon at 4 canvas pixels a texel, in the game's category colours (M3c).
                var icon = NewImage(b.transform, "Icon", MenuArt.IconColour(name));
                icon.sprite = MenuArt.Icon(name);
                icon.raycastTarget = false;
                Place(icon.rectTransform, 16f, (TabH - 32f) * 0.5f, 32f, 32f);
                t.rectTransform.offsetMin = new Vector2(56f, 0f);
                t.rectTransform.offsetMax = new Vector2(-12f, 0f);
                tabButtons.Add((b.GetComponent<Image>(), t, name));
            }
            // One size for all eight: the one the longest name fits at (mixed sizes looked uneven).
            float tabSize = 36f;
            foreach (var (_, t, _) in tabButtons) { t.ForceMeshUpdate(); tabSize = Mathf.Min(tabSize, t.fontSize); }
            foreach (var (_, t, _) in tabButtons) { t.enableAutoSizing = false; t.fontSize = tabSize; }

            body = new GameObject("Body", typeof(RectTransform)).GetComponent<RectTransform>();
            body.SetParent(rrt, false);
            Place(body, Side, BodyY, BodyW, RowsShown * RowPitch);

            // Scroll column: arrows and a thumb, only when the page has more rows than fit.
            float sx = Side + RowW + 8f;
            scrollUp = NewIconButton(rrt, arrowIcon, 0f, () => ScrollBy(-3)).gameObject;
            Place((RectTransform)scrollUp.transform, sx, BodyY, ScrollW, ScrollW);
            scrollDown = NewIconButton(rrt, arrowIcon, 180f, () => ScrollBy(3)).gameObject;
            Place((RectTransform)scrollDown.transform, sx, BodyY + RowsShown * RowPitch - 8f - ScrollW, ScrollW, ScrollW);
            var track = NewImage(rrt, "ScrollTrack", TrackCol);
            scrollTrack = track.rectTransform;
            Place(scrollTrack, sx + ScrollW * 0.5f - 6f, BodyY + ScrollW + 8f, 12f, RowsShown * RowPitch - 2f * ScrollW - 24f);
            var thumb = NewImage(scrollTrack, "Thumb", LabelCol);
            scrollThumb = thumb.rectTransform;

            // Description strip (M1c): the pointed-at setting's own config text.
            var descImg = NewImage(rrt, "Description", RowCol);
            Frame(descImg, RowCol);
            descBg = descImg.gameObject;
            Place(descImg.rectTransform, Side, DescY, BodyW, DescH);
            desc = NewText(descImg.rectTransform, "", 32, TextAlignmentOptions.MidlineLeft, DimText);
            desc.enableWordWrapping = true;
            FitText(desc, 26f);
            Place(desc.rectTransform, 16f, 4f, BodyW - 32f, DescH - 8f);

            moreButton = NewButton(rrt, "More settings", 32, () => { showMore = !showMore; ShowTab(tab, keepScroll: true); });
            Place((RectTransform)moreButton.transform, Side, BarY, 380f, BarH);
            moreLabel = moreButton.GetComponentInChildren<TextMeshProUGUI>();
            var reset = NewButton(rrt, "Reset page", 32, ResetPage);
            Place((RectTransform)reset.transform, Side + 396f, BarY, 280f, BarH);
            resetLabel = reset.GetComponentInChildren<TextMeshProUGUI>();
            var version = NewText(rrt, "CasualtyVR " + Plugin.Version, 32, TextAlignmentOptions.Right, DimText);
            Place(version.rectTransform, MenuW - Side - 400f, BarY, 400f, BarH);
            root.SetActive(false);
        }

        private void ShowTab(string name, bool keepScroll = false)
        {
            if (tab != name || !keepScroll) scroll = 0;
            tab = name;
            rows = RowsFor(name);
            DisarmReset();
            Layout();
            Plugin.Log.LogInfo($"VR settings: tab '{tab}'{(showMore ? " (more)" : "")}, {shown.Count} rows");
            if (DbgTab != null) dbgCheckAt = Time.unscaledTime + 0.5f;
        }

        private void Layout()
        {
            for (int i = body.childCount - 1; i >= 0; i--) Destroy(body.GetChild(i).gameObject);
            refreshers.Clear();
            pictureDesc = null;
            // Controls (M4c): a picture of the controllers with each control's action, instead of the rows.
            bool picture = tab == "Controls" && !showMore;
            Place(body, Side, BodyY, BodyW, picture ? DescY + DescH - BodyY : RowsShown * RowPitch);
            descBg.SetActive(!picture);
            if (picture) { shown = new List<Row>(); BuildControls(); }
            if (DbgAim && dbgOpened) dbgAimAt = Time.unscaledTime + 1f;
            shown = picture ? shown : rows.Where(r => !r.More && r.Control == null).ToList();
            int moreCount = rows.Count(r => r.More);
            if (showMore && moreCount > 0)
            {
                shown.Add(new Row { Kind = Kind.Header, Label = "More settings", Desc = "Fine tuning. Every row resets on its own, or with Reset page." });
                shown.AddRange(rows.Where(r => r.More));
            }
            scroll = Mathf.Clamp(scroll, 0, Mathf.Max(0, shown.Count - RowsShown));
            for (int i = scroll; i < Mathf.Min(shown.Count, scroll + RowsShown); i++) BuildRow(shown[i], (i - scroll) * RowPitch);

            foreach (var (image, text, t) in tabButtons)
            {
                SetSelected(image, t == tab);
                text.color = t == tab ? Color.white : LabelCol;
            }
            moreButton.interactable = moreCount > 0;
            moreLabel.text = moreCount == 0 ? "More settings" : showMore ? "Fewer settings" : $"More settings ({moreCount})";
            moreLabel.color = moreCount == 0 ? DimText * 0.8f : Color.white;
            bool scrolls = shown.Count > RowsShown;
            scrollUp.SetActive(scrolls);
            scrollDown.SetActive(scrolls);
            scrollTrack.gameObject.SetActive(scrolls);
            if (scrolls)
            {
                float h = scrollTrack.rect.height, frac = (float)RowsShown / shown.Count;
                float top = h * scroll / shown.Count;
                Place(scrollThumb, 0f, top, 12f, Mathf.Max(24f, h * frac));
            }
            ShowDesc(null);
            RefreshAll();
        }

        private void ScrollBy(int d)
        {
            int max = Mathf.Max(0, shown.Count - RowsShown);
            int s = Mathf.Clamp(scroll + d, 0, max);
            if (s == scroll) return;
            scroll = s;
            Layout();
            Tick(0.12f, 0.012f);
            if (Plugin.Verbose) Plugin.Log.LogInfo($"VR settings: scrolled to row {scroll} of {shown.Count}");
        }

        private void ShowDesc(Row r)
        {
            if (pictureDesc != null)
            {
                pictureDesc.text = r == null ? "Point at a control to see its action. Click it to choose another." : r.Desc;
                pictureDesc.color = r == null ? DimText : Color.white;
                return;
            }
            if (desc == null) return;
            if (r == null)
            {
                desc.text = tab == "About" ? "CasualtyVR: the game in VR. Keys work on the keyboard while VR runs." : "Point at a setting to see what it does.";
                desc.color = DimText;
                return;
            }
            desc.text = string.IsNullOrEmpty(r.Desc) ? r.Label : r.Desc;
            desc.color = Color.white;
        }

        private void RefreshAll()
        {
            nextRefresh = Time.unscaledTime + 0.2f;
            foreach (var r in refreshers) r();
        }

        /// <summary>A value changed from the menu: show it at once, and keep side effects in step.</summary>
        private void Changed()
        {
            PopupDepth.Scale = Plugin.PopupScale.Value;
            RefreshAll();
        }

        // ---- the Controls page: a picture of the controllers (VRMENU_REVIEW M4c, decision V5) ---------------------

        private static readonly string[] LeftControls = { "L.trigger", "L.grip", "X", "Y", "Menu", "L.stickUp", "L.stickDown", "L.stickLeft", "L.stickRight", "L.stickClick" };
        private static readonly string[] RightControls = { "R.trigger", "R.grip", "A", "B", "R.stickUp", "R.stickDown", "R.stickClick", "BothGrips" };
        private const float PadScale = 4.4f;   // canvas pixels per texel of the controller drawing

        /// <summary>
        /// Both controllers drawn in the game's line style, each control's action in a box beside them with a leader line
        /// to where the control is. Pointing at a box lights its line and says what it does; clicking it opens the picker.
        /// The boxes are 64 px on a 65 px pitch (2.5 degrees at the bottom-left corner), using the description strip's room.
        /// </summary>
        private void BuildControls()
        {
            if (picking != null) { BuildPicker(); return; }
            float h = DescY + DescH - BodyY;
            const float colW = 396f, pitch = 65f, boxH = 64f;
            float padW = MenuArt.PadW * PadScale, padH = MenuArt.PadH * PadScale;
            float leftPadX = colW + 4f, rightPadX = BodyW - colW - 4f - padW, padY = 48f;
            for (int side = 0; side < 2; side++)
            {
                bool left = side == 0;
                float px = left ? leftPadX : rightPadX;
                var title = NewText(body, left ? "Left" : "Right", 32, TextAlignmentOptions.Center, LabelCol);
                Place(title.rectTransform, px, 0f, padW, 44f);
                var pad = NewImage(body, left ? "Left controller" : "Right controller", LabelCol);
                pad.sprite = MenuArt.Controller();
                pad.raycastTarget = false;
                // The right hand's is mirrored: flipped about its left edge, so it is placed at its right edge.
                Place(pad.rectTransform, left ? px : px + padW, padY, padW, padH);
                if (!left) pad.rectTransform.localScale = new Vector3(-1f, 1f, 1f);
                var controls = left ? LeftControls : RightControls;
                float top = (h - controls.Length * pitch) * 0.5f;
                for (int i = 0; i < controls.Length; i++)
                {
                    string key = controls[i];
                    if (!Plugin.ControlBinds.TryGetValue(key, out var entry)) continue;
                    var row = rows.FirstOrDefault(r => r.Control == key);
                    float y = top + i * pitch;
                    float bx = left ? 0f : BodyW - colW;
                    var b = NewButton(body, "", 30, () => { picking = key; Plugin.Log.LogInfo($"VR settings: choosing an action for {key}"); Clicked(mini: true); Layout(); });
                    b.name = "Control " + key;
                    Place((RectTransform)b.transform, bx, y, colW, boxH);
                    // Two lines, so they never overlap (user, 2026-10-23: side by side, long names ran into each other): the
                    // control's name small at the top, its action (the game's own name, up to 23 letters) below at full width.
                    var name = NewText(b.transform, ShortControl(key), 24, TextAlignmentOptions.TopLeft, LabelCol);
                    Place(name.rectTransform, 10f, 3f, colW - 20f, 26f);
                    var action = NewText(b.transform, "", 30, TextAlignmentOptions.BottomRight, Color.white);
                    Place(action.rectTransform, 10f, 22f, colW - 20f, boxH - 25f);
                    FitText(action, 24f);
                    refreshers.Add(() => action.text = ActionName(entry.Value));
                    // Leader line from the box to the control on the drawing.
                    Vector2 a = MenuArt.Anchor(key);
                    float ax = left ? px + (a.x + 0.5f) * PadScale : px + padW - (a.x + 0.5f) * PadScale;
                    float ay = padY + (MenuArt.PadH - a.y - 0.5f) * PadScale;
                    var from = new Vector2(left ? bx + colW : bx, y + boxH * 0.5f);
                    // Hidden until you point at the box (user, 2026-10-23: all the lines at once looked messy).
                    var line = Line(from, new Vector2(ax, ay), Color.clear);
                    var dot = NewImage(body, "Anchor", Color.clear);
                    dot.raycastTarget = false;
                    Place(dot.rectTransform, ax - 5f, ay - 5f, 10f, 10f);
                    var fx = b.GetComponent<Fx>();
                    fx.Enter = () => { line.color = dot.color = FrameCol; ShowDesc(row); };
                    fx.Exit = () => { line.color = dot.color = Color.clear; ShowDesc(null); };
                    if (row != null) row.Desc = $"{ControlLabel(key)}: {ActionName(entry.Value)}. Click to choose another action.";
                }
            }
            pictureDesc = NewText(body, "", 30, TextAlignmentOptions.Top, DimText);
            pictureDesc.enableWordWrapping = true;
            FitText(pictureDesc, 26f);
            Place(pictureDesc.rectTransform, leftPadX, padY + padH + 16f, rightPadX + padW - leftPadX, h - padY - padH - 16f);
            ShowDesc(null);
        }

        /// <summary>The action picker for one control: every game action as a button; the current one lit.</summary>
        private void BuildPicker()
        {
            string key = picking;
            if (!Plugin.ControlBinds.TryGetValue(key, out var entry)) { picking = null; BuildControls(); return; }
            string def = Array.Find(VrInput.Defaults, d => d.control == key).action;
            var title = NewText(body, $"{ControlLabel(key)}: choose an action", 36, TextAlignmentOptions.MidlineLeft, Color.white);
            Place(title.rectTransform, 8f, 0f, BodyW - 16f, 56f);
            const int cols = 4;
            float w = (BodyW - (cols - 1) * 10f) / cols;
            var actions = VrInput.Actions;
            for (int i = 0; i < actions.Length; i++)
            {
                string act = actions[i];
                var b = NewButton(body, ActionName(act) + (act == def ? " (default)" : ""), 30, () =>
                {
                    entry.Value = act;
                    picking = null;
                    Clicked();
                    Layout();
                });
                b.name = "Action " + act;
                Place((RectTransform)b.transform, (i % cols) * (w + 10f), 64f + (i / cols) * 80f, w, 72f);
                var label = b.GetComponentInChildren<TextMeshProUGUI>();
                label.enableWordWrapping = true;   // "Expand item description" takes two lines
                label.rectTransform.offsetMin = new Vector2(8f, 0f);
                label.rectTransform.offsetMax = new Vector2(-8f, 0f);
                FitText(label, 26f);
                var img = b.GetComponent<Image>();
                bool chosen = entry.Value == act;
                SetSelected(img, chosen);
            }
            int last = (actions.Length + cols - 1) / cols;
            var back = NewButton(body, "Back", 32, () => { picking = null; Clicked(mini: true); Layout(); });
            Place((RectTransform)back.transform, 0f, 64f + last * 80f, w, 72f);
        }

        /// <summary>A thin line on the body from a to b (top-left coordinates).</summary>
        private Image Line(Vector2 a, Vector2 b, Color c)
        {
            var img = NewImage(body, "Line", c);
            img.raycastTarget = false;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(a.x, -a.y);
            Vector2 d = new Vector2(b.x - a.x, -(b.y - a.y));
            rt.sizeDelta = new Vector2(d.magnitude, 3f);
            rt.localEulerAngles = new Vector3(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            return img;
        }

        private static string ShortControl(string control)
        {
            switch (control)
            {
                case "R.trigger": case "L.trigger": return "Trigger";
                case "R.grip": case "L.grip": return "Grip";
                case "BothGrips": return "Both grips";
                case "R.stickUp": case "L.stickUp": return "Stick up";
                case "R.stickDown": case "L.stickDown": return "Stick down";
                case "L.stickLeft": return "Stick left";
                case "L.stickRight": return "Stick right";
                case "R.stickClick": case "L.stickClick": return "Stick click";
                case "Menu": return "Menu";
                default: return control;
            }
        }

        // ---- rows ------------------------------------------------------------------------------------

        private void BuildRow(Row r, float y)
        {
            bool header = r.Kind == Kind.Header;
            var bg = NewImage(body, "Row " + r.Label, header ? HeaderRowCol : RowCol);
            if (!header) Frame(bg, RowCol);   // the game's box outline: the rows read as the crafting list's (M3b)
            Place(bg.rectTransform, 0f, y, RowW, RowH);
            var go = bg.gameObject;
            var group = go.AddComponent<CanvasGroup>();
            var fx = go.AddComponent<Fx>();
            fx.Enter = () => { if (!header) bg.color = RowHover; ShowDesc(r); };
            fx.Exit = () => { bg.color = header ? HeaderRowCol : RowCol; ShowDesc(null); };

            var label = NewText(bg.rectTransform, r.Label, 36, TextAlignmentOptions.MidlineLeft, header ? Color.white : LabelCol);
            label.enableWordWrapping = true;
            FitText(label, 28f);
            Place(label.rectTransform, LabelX, 0f, header ? RowW - 32f : LabelW, RowH);
            if (header) return;

            switch (r.Kind)
            {
                case Kind.Switch:
                {
                    // The whole row is the switch (M2a).
                    var b = go.AddComponent<Button>();
                    b.transition = Selectable.Transition.None;
                    b.navigation = new Navigation { mode = Navigation.Mode.None };
                    b.onClick.AddListener(() => { r.SetOn(!r.GetOn()); Clicked(); });
                    fx.Ticks = true;
                    var update = BuildSwitch(bg.rectTransform, CtrlX + CtrlW - 140f, 140f, r, interactive: false);
                    refreshers.Add(update);
                    break;
                }
                case Kind.Choice:
                {
                    int n = r.Options.Length;
                    float gw = (CtrlW - (n - 1) * 6f) / n;
                    var segs = new List<(Image img, TextMeshProUGUI txt)>();
                    for (int i = 0; i < n; i++)
                    {
                        int index = i;
                        var b = NewButton(bg.rectTransform, r.Options[i], 30, () => { r.SetIndex(index); Clicked(); });
                        Place((RectTransform)b.transform, CtrlX + i * (gw + 6f), 0f, gw, RowH);
                        var t = b.GetComponentInChildren<TextMeshProUGUI>();
                        FitText(t, 26f);
                        segs.Add((b.GetComponent<Image>(), t));
                    }
                    refreshers.Add(() =>
                    {
                        int sel = r.GetIndex();
                        for (int i = 0; i < segs.Count; i++)
                        {
                            SetSelected(segs[i].img, i == sel);
                            segs[i].txt.color = i == sel ? Color.white : LabelCol;
                        }
                    });
                    break;
                }
                case Kind.Slider:
                    refreshers.Add(BuildSlider(bg.rectTransform, CtrlX, CtrlW, r, 160f));
                    break;
                case Kind.OptSlider:
                {
                    // A switch and its amount in one row: the amount dims while it is off.
                    var sw = BuildSwitch(bg.rectTransform, CtrlX, 120f, r, interactive: true);
                    var slider = BuildSlider(bg.rectTransform, CtrlX + 132f, CtrlW - 132f, r, 130f);
                    refreshers.Add(sw);
                    refreshers.Add(slider);
                    break;
                }
                case Kind.Stepper:
                {
                    var minus = NewButton(bg.rectTransform, "<", 34, () => { r.StepBy(-1); Clicked(); });
                    Place((RectTransform)minus.transform, CtrlX, (RowH - Btn) * 0.5f, Btn, Btn);
                    var plus = NewButton(bg.rectTransform, ">", 34, () => { r.StepBy(+1); Clicked(); });
                    Place((RectTransform)plus.transform, CtrlX + CtrlW - Btn, (RowH - Btn) * 0.5f, Btn, Btn);
                    var value = NewText(bg.rectTransform, "", 34, TextAlignmentOptions.Center, Color.white);
                    FitText(value, 28f);
                    Place(value.rectTransform, CtrlX + Btn + 8f, 0f, CtrlW - 2f * Btn - 16f, RowH);
                    refreshers.Add(() => value.text = r.Value());
                    break;
                }
                case Kind.Info:
                {
                    var value = NewText(bg.rectTransform, "", 34, TextAlignmentOptions.MidlineLeft, Color.white);
                    value.enableWordWrapping = true;
                    FitText(value, 26f);
                    Place(value.rectTransform, CtrlX, 0f, RowW - CtrlX - 16f, RowH);
                    refreshers.Add(() => value.text = r.Value());
                    break;
                }
            }

            // Row reset (M2e): only while the row differs from its default.
            if (!r.NoReset && r.Entries.Length > 0 && r.Kind != Kind.Info)
            {
                var reset = NewIconButton(bg.rectTransform, resetIcon, 0f, () =>
                {
                    foreach (var e in r.Entries) e.BoxedValue = e.DefaultValue;
                    Plugin.Log.LogInfo($"VR settings: '{r.Label}' reset to default");
                    Clicked();
                });
                Place((RectTransform)reset.transform, RowW - 12f - Btn, (RowH - Btn) * 0.5f, Btn, Btn);
                var rgo = reset.gameObject;
                refreshers.Add(() => rgo.SetActive(r.Entries.Any(e => !Equals(e.BoxedValue, e.DefaultValue))));
            }
            if (r.Active != null)
                refreshers.Add(() =>
                {
                    bool on = r.Active();
                    group.alpha = on ? 1f : 0.4f;
                    group.interactable = on;
                });
        }

        /// <summary>A switch pill (M2a). interactive: its own button; otherwise the row is the button.</summary>
        private Action BuildSwitch(RectTransform parent, float x, float w, Row r, bool interactive)
        {
            const float h = RowH;
            Image pill;
            if (interactive)
            {
                var b = NewButton(parent, "", 26, () => { r.SetOn(!r.GetOn()); Clicked(); });
                pill = b.GetComponent<Image>();
            }
            else
            {
                pill = NewImage(parent, "Switch", ButtonCol);
                pill.raycastTarget = false;
            }
            Place(pill.rectTransform, x, (RowH - h) * 0.5f, w, h);
            var knob = NewImage(pill.rectTransform, "Knob", Color.white);
            knob.raycastTarget = false;
            var text = NewText(pill.rectTransform, "", 32, TextAlignmentOptions.Center, Color.white);
            return () =>
            {
                bool on = r.GetOn();
                if (on) Solid(pill, OnCol); else Frame(pill, ButtonCol);
                Place(knob.rectTransform, on ? w - h + 10f : 10f, 10f, h - 20f, h - 20f);
                Place(text.rectTransform, on ? 0f : h - 10f, 0f, w - h + 10f, h);
                text.text = on ? "On" : "Off";
                text.color = on ? Color.white : DimText;
            };
        }

        /// <summary>A slider (M2c): drag on the track, or the fine &lt; &gt; buttons; the value in real units.</summary>
        private Action BuildSlider(RectTransform parent, float x, float w, Row r, float valueW)
        {
            var minus = NewButton(parent, "<", 34, () => Nudge(r, -1));
            Place((RectTransform)minus.transform, x, (RowH - Btn) * 0.5f, Btn, Btn);
            float plusX = x + w - valueW - 8f - Btn;
            var plus = NewButton(parent, ">", 34, () => Nudge(r, +1));
            Place((RectTransform)plus.transform, plusX, (RowH - Btn) * 0.5f, Btn, Btn);
            var value = NewText(parent, "", 34, TextAlignmentOptions.MidlineRight, Color.white);
            FitText(value, 26f);
            Place(value.rectTransform, x + w - valueW, 0f, valueW, RowH);

            // The hit area is the full row height; the bar inside is thin.
            float tx = x + Btn + 14f, tw = plusX - 14f - tx;
            var trackImg = NewImage(parent, "Slider", new Color(0f, 0f, 0f, 0f));
            Place(trackImg.rectTransform, tx, 0f, tw, RowH);
            var bar = NewImage(trackImg.rectTransform, "Bar", TrackCol);
            bar.raycastTarget = false;
            Place(bar.rectTransform, 0f, RowH * 0.5f - 6f, tw, 12f);
            var fill = NewImage(trackImg.rectTransform, "Fill", AccentCol);
            fill.raycastTarget = false;
            var knob = NewImage(trackImg.rectTransform, "Knob", Color.white);
            knob.raycastTarget = false;
            var slider = trackImg.gameObject.AddComponent<MenuSlider>();
            slider.OnValue = t =>
            {
                float v = Snap(r, Mathf.Lerp(r.Min, r.Max, t));
                if (Mathf.Abs(v - r.Get()) < r.Step * 0.01f) return;
                r.Set(v);
                Tick(0.15f, 0.01f);   // a detent per step
                Changed();
            };
            slider.OnRelease = () => PlaySound(PlayerCamera.UISoundType.MiniClick);
            trackImg.gameObject.AddComponent<Fx>().Ticks = true;
            return () =>
            {
                float t = Mathf.InverseLerp(r.Min, r.Max, r.Get());
                Place(fill.rectTransform, 0f, RowH * 0.5f - 6f, tw * t, 12f);
                Place(knob.rectTransform, tw * t - 11f, RowH * 0.5f - 23f, 22f, 46f);
                value.text = r.Format(r.Get());
                if (r.Kind == Kind.OptSlider)
                {
                    bool on = r.GetOn();
                    value.color = on ? Color.white : DimText;
                    fill.color = on ? AccentCol : DimText * 0.6f;
                }
            };
        }

        private static float Snap(Row r, float v)
        {
            v = Mathf.Round((v - r.Min) / r.Step) * r.Step + r.Min;
            return (float)Math.Round(Mathf.Clamp(v, r.Min, r.Max), 4);
        }

        private void Nudge(Row r, int d)
        {
            if (r.Kind == Kind.OptSlider && !r.GetOn()) r.SetOn(true);
            r.Set(Snap(r, r.Get() + d * r.Step));
            Clicked(mini: true);
        }

        private void Clicked(bool mini = false)
        {
            Tick(0.35f, 0.02f);
            PlaySound(mini ? PlayerCamera.UISoundType.MiniClick : PlayerCamera.UISoundType.Click);
            Changed();
        }

        // Feedback (M2d): a light tick on hover and slider steps, a stronger one on a click; the game's own click sound.
        private void Tick(float amplitude, float seconds)
        {
            float now = Time.unscaledTime;
            if (now - lastTick < 0.05f) return;
            lastTick = now;
            VrHaptics.Pulse(Plugin.LaserHand.Value == "Left" ? 0 : 1, amplitude, seconds);
        }

        private static void PlaySound(PlayerCamera.UISoundType sound)
        {
            try { if (PlayerCamera.main != null) PlayerCamera.main.PlayUISound(sound); }
            catch (Exception) { }
        }

        private void ResetPage()
        {
            if (resetArmedUntil < 0f)
            {
                resetArmedUntil = Time.unscaledTime + 3f;
                if (resetLabel != null) { resetLabel.text = "Press again"; resetLabel.color = WarnCol; }
                return;
            }
            DisarmReset();
            var entries = rows.Where(r => !r.NoReset).SelectMany(r => r.Entries).Distinct().ToList();
            foreach (var e in entries) e.BoxedValue = e.DefaultValue;
            Plugin.Log.LogInfo($"VR settings: reset {entries.Count} settings on '{tab}' to defaults");
            Clicked();
        }

        private void DisarmReset()
        {
            resetArmedUntil = -1f;
            if (resetLabel != null) { resetLabel.text = "Reset page"; resetLabel.color = Color.white; }
        }

        // ---- the settings ----------------------------------------------------------------------------

        private List<Row> RowsFor(string name)
        {
            var list = new List<Row>();
            switch (name)
            {
                case "Comfort":
                    // First-person comfort (docs/FIRSTPERSON_REVIEW.md FP-8): the preset sets the rows below it and those
                    // under More settings; changing any of them makes it your own mix (no preset lit).
                    list.Add(new Row
                    {
                        Kind = Kind.Choice, Label = "Comfort preset", NoReset = true,
                        Desc = "Sets the comfort options at once. Intense: the game moves you as it moves the character. Moderate: snap blink, steady eye height, a light fall vignette. Gentle: also the diorama while knocked down, running at about 4 m/s and snap turning.",
                        Options = ComfortPresets.Names, GetIndex = ComfortPresets.Current, SetIndex = i => ComfortPresets.Apply(ComfortPresets.Names[i]),
                    });
                    list.Add(Choice("Turning", Plugin.FpTurning, ("Snap", "Snap"), ("Smooth", "Smooth")));
                    list.Add(new Row
                    {
                        Kind = Kind.Slider, Label = "Running speed", Entries = new ConfigEntryBase[] { Plugin.FpMoveSpeed },
                        Desc = "How fast you run in first person, with the stick pushed all the way (a real run is about 3 m/s). The flat game and the diorama keep the game's own speed.",
                        Min = 0.5f, Max = 1f, Step = 0.05f, Get = () => Plugin.FpMoveSpeed.Value, Set = v => Plugin.FpMoveSpeed.Value = v, Format = RunSpeedText,
                    });
                    list.Add(OptSlider("Walk on a half push", Plugin.FpAnalogWalk, Plugin.FpWalkSpeed, 0.8f, 3f, 0.1f, v => $"{v:F1} m/s"));
                    list.Add(Slider("Fall vignette", Plugin.FpVignette, 0f, 1f, 0.1f, v => v <= 0f ? "Off" : $"{v * 100f:F0}%"));
                    list.Add(Choice("When knocked down", Plugin.FpRagdollView, ("Stay", "Stay in 1st person"), ("Diorama", "Diorama")));
                    list.Add(Switch("Blackout when unconscious", Plugin.FpBlackout));
                    list.Add(Switch("Steady eye height", Plugin.FpSteadyHeight, more: true));
                    list.Add(Switch("Snap blink", Plugin.FpSnapBlink, more: true));
                    list.Add(Switch("Fade inside walls", Plugin.FpWallFade, more: true));
                    list.Add(Slider("Smooth turn speed", Plugin.FpTurnSpeed, 30f, 360f, 15f, v => $"{v:F0}°/s", more: true, active: () => Plugin.FpTurning.Value == "Smooth"));
                    list.Add(Slider("Camera smoothing", Plugin.FpSmoothing, 0f, 1f, 0.1f, v => $"{v * 100f:F0}%", more: true));
                    break;
                case "View":
                    list.Add(new Row
                    {
                        Kind = Kind.Slider, Label = "Zoom (view height)", Entries = new ConfigEntryBase[] { Plugin.UnitsPerMetre },
                        Desc = "How tall the diorama's view is in the room. Bigger = zoomed in. Also PageUp / PageDown.",
                        Min = 0.6f, Max = 5.6f, Step = 0.05f, Get = () => 45f / Plugin.UnitsPerMetre.Value,
                        Set = v => Plugin.UnitsPerMetre.Value = Mathf.Clamp(45f / v, 8f, 80f), Format = v => $"{v:F2} m",
                    });
                    list.Add(Slider("Diorama distance", Plugin.DioramaDistance, 0.4f, 3f, 0.05f, v => $"{v:F2} m"));
                    list.Add(Slider("Window size", Plugin.FrameMargin, 0f, 2f, 0.05f, v => $"{v * 100f:F0}%"));
                    list.Add(Switch("3D blocks", Plugin.VoxelsOn));
                    list.Add(Choice("Fog (foggy layers)", Plugin.FogMode, ("Mist", "Mist"), ("Haze", "Haze"), ("Off", "Off")));
                    list.Add(new Row
                    {
                        Kind = Kind.Slider, Label = "Fog strength", Entries = new ConfigEntryBase[] { Plugin.FogMist, Plugin.FogHaze },
                        Desc = "How thick the mist (or haze) is at its thickest.", Active = () => Plugin.FogMode.Value != "Off",
                        Min = 0f, Max = 1f, Step = 0.05f, Get = () => FogEntry().Value, Set = v => FogEntry().Value = v, Format = v => $"{v * 100f:F0}%",
                    });
                    list.Add(Switch("Hover highlight (diorama)", Plugin.HoverHighlight));
                    list.Add(Switch("Rain", Plugin.VrRain));
                    list.Add(Slider("Window edge fade", Plugin.FrameFade, 0f, 2f, 0.1f, v => $"{v:F1}", more: true));
                    list.Add(Slider("Block front depth", Plugin.VoxelFront, 0f, 4f, 0.25f, v => $"{v:F2} blocks", more: true));
                    list.Add(Slider("Block back depth", Plugin.VoxelBack, 0f, 6f, 0.25f, v => $"{v:F2} blocks", more: true));
                    list.Add(Slider("Pop-up depth", Plugin.PopupScale, 0f, 3f, 0.1f, v => $"{v:F1}×", more: true));
                    list.Add(OptSlider("Object depth", Plugin.ObjectDepthOn, Plugin.ObjectDepth, 0.25f, 4f, 0.25f, v => $"{v:F2} blocks", more: true));
                    list.Add(OptSlider("Character thickness", Plugin.SlabsOn, Plugin.SlabThickness, 0.05f, 1.5f, 0.05f, v => $"{v:F2} blocks", more: true));
                    list.Add(OptSlider("Liquid depth (surface)", Plugin.LiquidDepthOn, Plugin.LiquidSurfaceAlpha, 0f, 1f, 0.05f, v => $"{v * 100f:F0}%", more: true));
                    break;
                case "First person":
                    // The mode switch is not reset with its page.
                    list.Add(new Row
                    {
                        Kind = Kind.Switch, Label = $"First person ({Plugin.FpKey.Value})", NoReset = true, Entries = new ConfigEntryBase[] { Plugin.FpOn },
                        Desc = Plugin.FpOn.Description.Description, GetOn = () => Plugin.FpWanted, SetOn = on => { Plugin.FpForced = false; Plugin.FpOn.Value = on; },
                    });
                    list.Add(Choice("Tunnel sides", Plugin.FpSides, ("CaveWalls", "Cave walls"), ("Void", "Void")));
                    list.Add(Slider("Void darkness far off", Plugin.FpVoidDarkness, 0f, 1f, 0.1f, v => $"{v * 100f:F0}%", active: () => Plugin.FpSides.Value == "Void"));
                    list.Add(Switch("Characters face you", Plugin.FpBillboards));
                    list.Add(Switch("Objects face you", Plugin.FpFaceObjects));
                    list.Add(Choice("Highlight under your hand", Plugin.FpHighlight, ("Off", "Off"), ("Blocks", "Blocks"), ("All", "All")));
                    list.Add(Slider("View distance", Plugin.FpViewDistance, 20f, 200f, 10f, v => $"{v:F0} blocks"));
                    list.Add(Switch("Sound from your head", Plugin.FpSoundAtHead));
                    list.Add(Slider("Tunnel width", Plugin.FpCorridor, 1f, 12f, 0.5f, v => $"{v:F1} blocks", more: true));
                    list.Add(new Row
                    {
                        Kind = Kind.Choice, Label = "World scale", Entries = new ConfigEntryBase[] { Plugin.FpUnitsPerMetre }, More = true,
                        Desc = "Auto: the character's eye height matches a 1.6 m standing eye height. Set: choose blocks per metre below.",
                        Options = new[] { "Auto", "Set" }, GetIndex = () => Plugin.FpUnitsPerMetre.Value <= 0f ? 0 : 1,
                        SetIndex = i => Plugin.FpUnitsPerMetre.Value = i == 0 ? 0f : Plugin.FpUnitsPerMetre.Value > 0f ? Plugin.FpUnitsPerMetre.Value : Mathf.Clamp((float)Math.Round(FirstPerson.CurrentScale > 0f ? FirstPerson.CurrentScale : 3f, 1), 0.8f, 4f),
                    });
                    list.Add(new Row
                    {
                        Kind = Kind.Slider, Label = "Blocks per metre", Entries = new ConfigEntryBase[] { Plugin.FpUnitsPerMetre }, More = true, NoReset = true,
                        Desc = "World scale when set: fewer blocks per metre makes the world bigger around you.", Active = () => Plugin.FpUnitsPerMetre.Value > 0f,
                        Min = 0.8f, Max = 4f, Step = 0.1f, Get = () => Plugin.FpUnitsPerMetre.Value > 0f ? Plugin.FpUnitsPerMetre.Value : (FirstPerson.CurrentScale > 0f ? FirstPerson.CurrentScale : 3f),
                        Set = v => Plugin.FpUnitsPerMetre.Value = v, Format = v => $"{v:F1}",
                    });
                    break;
                case "Hands & body":
                    list.Add(Switch("Character looks where you look", Plugin.FpLookWithHead));
                    list.Add(Choice("Aim with", Plugin.FpAimWith, ("Hand", "Hand"), ("Head", "Head")));
                    list.Add(Choice("Grab", Plugin.FpGrabMode, ("Hold", "Hold grip"), ("Toggle", "Toggle")));
                    list.Add(Switch("Swing to attack", Plugin.FpSwingAttack));
                    list.Add(Switch("Distance pull", Plugin.FpDistancePull));
                    list.Add(Switch("Hand on what you can grab", Plugin.FpGrabGlow));
                    list.Add(OptSlider("Crouch for real", Plugin.FpRealCrouch, Plugin.FpCrouchDepth, 0.15f, 0.6f, 0.05f, v => $"{v * 100f:F0} cm down"));
                    list.Add(OptSlider("Vibration", Plugin.Haptics, Plugin.HapticStrength, 0.1f, 1f, 0.1f, v => $"{v * 100f:F0}%"));
                    list.Add(Switch("Heartbeat when in danger", Plugin.HapticHeartbeat));
                    list.Add(Switch("Vibrate when hurt", Plugin.HapticDamage, more: true));
                    list.Add(Switch("Vibrate when a hit lands", Plugin.HapticHits, more: true));
                    list.Add(Switch("Hand poses", Plugin.FpHandPoses, more: true));
                    list.Add(Switch("Touch minigames", Plugin.FpMinigameTouch, more: true));
                    list.Add(OptSlider("Controller hands (diorama)", Plugin.ShowHands, Plugin.HandSize, 0.03f, 0.3f, 0.01f, v => $"{v * 100f:F0} cm", more: true));
                    list.Add(OptSlider("Your arms (thickness)", Plugin.FpArms, Plugin.FpArmThickness, 0.1f, 2f, 0.1f, v => $"{v:F1}×", more: true));
                    list.Add(OptSlider("Your body (thickness)", Plugin.FpBody, Plugin.FpBodyDepth, 0.2f, 2f, 0.1f, v => $"{v:F1}×", more: true));
                    list.Add(Slider("Body behind your eyes", Plugin.FpBodyBack, -0.2f, 1f, 0.05f, v => $"{v * 100f:F0} cm", more: true));
                    // Motion takes (Phase 4a, FP 9f): real swings and throws recorded here, replayed headless against the recipes.
                    list.Add(new Row
                    {
                        Kind = Kind.Choice, Label = "Record a motion take", More = true, NoReset = true,
                        Desc = "Records 20 s of your hand motion for testing the swing and throw detection. Pick one: the menu closes, 5 ticks, then record. Swings: 5 sword swings. Throws: 5 throws (pick something up each time). Other: ordinary moves that should not attack (reaching, eating, looking around). A long buzz starts and ends it.",
                        Options = MotionTakes.Labels,
                        GetIndex = () => MotionTakes.Recording == null ? -1 : Array.FindIndex(MotionTakes.Labels, l => MotionTakes.Recording.StartsWith(l)),
                        SetIndex = i => { MotionTakes.Start(MotionTakes.Labels[i]); Close(); },
                    });
                    break;
                case "Controls":
                    foreach (var kv in Plugin.ControlBinds)
                    {
                        var entry = kv.Value;
                        list.Add(new Row
                        {
                            Kind = Kind.Stepper, Label = ControlLabel(kv.Key), Entries = new ConfigEntryBase[] { entry }, Control = kv.Key,
                            Desc = $"The game action for the {ControlLabel(kv.Key).ToLowerInvariant()}. In first person the sticks walk, climb and turn.",
                            Value = () => ActionName(entry.Value),
                            StepBy = d =>
                            {
                                int i = Array.IndexOf(VrInput.Actions, entry.Value);
                                i = ((i < 0 ? 0 : i) + d + VrInput.Actions.Length) % VrInput.Actions.Length;
                                entry.Value = VrInput.Actions[i];
                            },
                        });
                    }
                    list.Add(Choice("Laser hand", Plugin.LaserHand, ("Right", "Right"), ("Left", "Left"), more: true));
                    list.Add(Switch("Show the laser beam", Plugin.ShowLaser, more: true));
                    list.Add(Slider("Minigame hand X", Plugin.MinigameHandX, -400f, 400f, 10f, v => $"{v:F0} px", more: true));
                    list.Add(Slider("Minigame hand Y", Plugin.MinigameHandY, -400f, 400f, 10f, v => $"{v:F0} px", more: true));
                    break;
                case "Menus & HUD":
                    list.Add(Slider("HUD size", Plugin.HudScale, 0.5f, 2f, 0.1f, v => $"{v * 100f:F0}%"));
                    list.Add(Switch("Zoom the game's menus", Plugin.ZoomGameMenus));
                    list.Add(Choice("Cursor", Plugin.CursorStyle, ("Hand", "Hand"), ("Cursor", "Arrow"), ("Off", "Off")));
                    list.Add(new Row
                    {
                        Kind = Kind.Slider, Label = "Cursor size", Entries = new ConfigEntryBase[] { Plugin.HandCursorSize, Plugin.CursorSize },
                        Desc = "Size of the cursor drawn on the menu panels.", Active = () => Plugin.CursorStyle.Value != "Off",
                        Min = 0.5f, Max = 20f, Step = 0.5f,
                        Get = () => (Plugin.CursorStyle.Value == "Hand" ? Plugin.HandCursorSize.Value : Plugin.CursorSize.Value) * 100f,
                        Set = v => { if (Plugin.CursorStyle.Value == "Hand") Plugin.HandCursorSize.Value = Mathf.Clamp(v / 100f, 0.01f, 0.2f); else Plugin.CursorSize.Value = Mathf.Clamp(v / 100f, 0.005f, 0.1f); },
                        Format = v => $"{v:F1} cm",
                    });
                    list.Add(Slider("Settings panel distance", Plugin.SettingsDistance, 0.4f, 1.5f, 0.05f, v => $"{v:F2} m", more: true));
                    list.Add(Slider("Settings panel height", Plugin.SettingsHeight, -0.6f, 0.3f, 0.02f, v => $"{v * 100f:F0} cm", more: true));
                    list.Add(Slider("Settings panel width", Plugin.SettingsWidth, 0.5f, 1.6f, 0.05f, v => $"{v:F2} m", more: true));
                    list.Add(Slider("Dashboard distance", Plugin.DashboardDistance, 0.3f, 2f, 0.05f, v => $"{v:F2} m", more: true));
                    list.Add(Slider("Dashboard height", Plugin.DashboardHeight, -1.5f, 0.5f, 0.05f, v => $"{v * 100f:F0} cm", more: true));
                    list.Add(Slider("Dashboard width", Plugin.DashboardWidth, 0.4f, 3f, 0.1f, v => $"{v:F1} m", more: true));
                    list.Add(Slider("Main menu width", Plugin.MenuWidth, 0.6f, 3f, 0.1f, v => $"{v:F1} m", more: true));
                    break;
                case "Passthrough":
                    list.Add(new Row
                    {
                        Kind = Kind.Switch, Label = "Passthrough greenscreen", NoReset = true, Entries = new ConfigEntryBase[] { Plugin.Greenscreen },
                        Desc = Plugin.Greenscreen.Description.Description, GetOn = () => Plugin.Greenscreen.Value, SetOn = on => Plugin.Greenscreen.Value = on,
                    });
                    list.Add(new Row
                    {
                        Kind = Kind.Choice, Label = "Key colour", Entries = new ConfigEntryBase[] { Plugin.GreenscreenColor },
                        Desc = "The colour your passthrough app keys out (Virtual Desktop: match its setting). Exact values under More settings.",
                        Options = KeyPresets.Select(p => p.name).ToArray(),
                        GetIndex = () => Array.FindIndex(KeyPresets, p => p.c == Plugin.GreenscreenColor.Value),
                        SetIndex = i => Plugin.GreenscreenColor.Value = KeyPresets[i].c,
                    });
                    list.Add(Channel("Red", 0));
                    list.Add(Channel("Green", 1));
                    list.Add(Channel("Blue", 2));
                    break;
                case "About":
                    list.Add(Info("CasualtyVR", () => $"{Plugin.Version}, tested with the game's {Plugin.TestedGameVersion}"));
                    list.Add(Info("Game version", () => Application.version + (Plugin.UntestedGameVersion ? " (not tested)" : "")));
                    list.Add(Info($"{Plugin.ToggleKey.Value}", () => "VR on / off"));
                    list.Add(Info($"{Plugin.RecenterKey.Value}", () => "Recenter"));
                    list.Add(Info($"{Plugin.FpKey.Value}", () => "First person / diorama"));
                    list.Add(Info($"{Plugin.VoxelKey.Value}", () => "3D blocks / flat"));
                    list.Add(Info($"{Plugin.DashboardKey.Value}", () => "Show the dashboard"));
                    list.Add(Info($"{Plugin.SettingsKey.Value}", () => "This menu"));
                    list.Add(Info($"{Plugin.ZoomInKey.Value} / {Plugin.ZoomOutKey.Value}", () => "Zoom the diorama"));
                    list.Add(Info("Logs", () => "BepInEx\\LogOutput.log, BepInEx\\CasualtyVR-bridge.log"));
                    list.Add(Info("Settings file", () => "BepInEx\\config\\casualtyvr.core.cfg"));
                    break;
            }
            return list;
        }

        private static ConfigEntry<float> FogEntry() => Plugin.FogMode.Value == "Haze" ? Plugin.FogHaze : Plugin.FogMist;

        /// <summary>Running speed in metres per second (FP 8b), once the first-person scale is known; else the fraction.</summary>
        private static string RunSpeedText(float fraction)
        {
            var body = PlayerCamera.main != null ? PlayerCamera.main.body : null;
            float scale = FirstPerson.ScaleEstimate;
            if (body == null || scale <= 0f || body.maxSpeed <= 0f) return $"{fraction * 100f:F0}%";
            return $"{body.maxSpeed * fraction / scale:F1} m/s";
        }

        private static string ControlLabel(string control)
        {
            switch (control)
            {
                case "R.trigger": return "Right trigger";
                case "R.grip": return "Right grip";
                case "L.trigger": return "Left trigger";
                case "L.grip": return "Left grip";
                case "BothGrips": return "Both grips";
                case "R.stickUp": return "Right stick up";
                case "R.stickDown": return "Right stick down";
                case "R.stickClick": return "Right stick click";
                case "L.stickUp": return "Left stick up";
                case "L.stickDown": return "Left stick down";
                case "L.stickLeft": return "Left stick left";
                case "L.stickRight": return "Left stick right";
                case "L.stickClick": return "Left stick click";
                case "Menu": return "Menu button";
                default: return control + " button";
            }
        }

        /// <summary>The game's own name for an action, as its keybind settings show it (in the game's language).</summary>
        private static string ActionName(string action)
        {
            if (string.IsNullOrEmpty(action) || action == "none") return "None";
            string key = "gameset" + action;
            string name = Locale.GetOther(key);
            return string.IsNullOrEmpty(name) || name == key ? action : name;
        }

        private static Row Channel(string label, int ch)
        {
            return new Row
            {
                Kind = Kind.Slider, Label = label + " (key colour)", More = true, Entries = new ConfigEntryBase[] { Plugin.GreenscreenColor },
                Desc = "One channel of the key colour, for a custom colour.",
                Min = 0f, Max = 1f, Step = 0.05f, Get = () => Plugin.GreenscreenColor.Value[ch],
                Set = v =>
                {
                    var c = Plugin.GreenscreenColor.Value;
                    c[ch] = v;
                    c.a = 1f;
                    Plugin.GreenscreenColor.Value = c;
                },
                Format = v => $"{v:F2}",
            };
        }

        private static Row Switch(string label, ConfigEntry<bool> e, bool more = false) => new Row
        {
            Kind = Kind.Switch, Label = label, More = more, Entries = new ConfigEntryBase[] { e }, Desc = e.Description.Description,
            GetOn = () => e.Value, SetOn = on => e.Value = on,
        };

        private static Row Choice(string label, ConfigEntry<string> e, params (string value, string text)[] options) => Choice(label, e, false, options);

        private static Row Choice(string label, ConfigEntry<string> e, bool more, params (string value, string text)[] options) => new Row
        {
            Kind = Kind.Choice, Label = label, More = more, Entries = new ConfigEntryBase[] { e }, Desc = e.Description.Description,
            Options = options.Select(o => o.text).ToArray(),
            GetIndex = () => Array.FindIndex(options, o => o.value == e.Value),
            SetIndex = i => e.Value = options[i].value,
        };

        private static Row Choice(string label, ConfigEntry<string> e, (string value, string text) a, (string value, string text) b, bool more) => Choice(label, e, more, a, b);

        private static Row Slider(string label, ConfigEntry<float> e, float min, float max, float step, Func<float, string> format, bool more = false, Func<bool> active = null) => new Row
        {
            Kind = Kind.Slider, Label = label, More = more, Active = active, Entries = new ConfigEntryBase[] { e }, Desc = e.Description.Description,
            Min = min, Max = max, Step = step, Get = () => e.Value, Set = v => e.Value = v, Format = format,
        };

        private static Row OptSlider(string label, ConfigEntry<bool> on, ConfigEntry<float> e, float min, float max, float step, Func<float, string> format, bool more = false) => new Row
        {
            Kind = Kind.OptSlider, Label = label, More = more, Entries = new ConfigEntryBase[] { on, e },
            Desc = on.Description.Description + " " + e.Description.Description,
            GetOn = () => on.Value, SetOn = v => on.Value = v,
            Min = min, Max = max, Step = step, Get = () => e.Value, Set = v => e.Value = v, Format = format,
        };

        private static Row Info(string label, Func<string> value) => new Row { Kind = Kind.Info, Label = label, Value = value, Desc = "" };

        // ---- headless checks -------------------------------------------------------------------------

        /// <summary>
        /// Every control's angular size at the default placement (VRMENU_REVIEW §6): its rectangle on the panel, seen from
        /// the eye with the panel facing the eye. Fails anything under 2.5 degrees (Meta: ray targets 2.5-3 degrees).
        /// Also the smallest text, in dp (14 minimum, 18 comfortable).
        /// </summary>
        private void LogCheck()
        {
            if (!IsOpen) return;
            var rrt = (RectTransform)root.transform;
            float mPerPx = Plugin.SettingsWidth.Value / (MenuW + 2f * Margin);
            float d = new Vector2(Plugin.SettingsHeight.Value, Plugin.SettingsDistance.Value).magnitude;
            float minAng = float.MaxValue, minDp = float.MaxValue;
            string minName = "-", minText = "-";
            int n = 0;
            var c = new Vector3[4];
            void Measure(RectTransform t, out float w, out float h, out float dist)
            {
                t.GetWorldCorners(c);
                Vector3 a = rrt.InverseTransformPoint(c[0]), b = rrt.InverseTransformPoint(c[2]);
                w = Mathf.Abs(b.x - a.x) * mPerPx;
                h = Mathf.Abs(b.y - a.y) * mPerPx;
                Vector3 mid = (a + b) * 0.5f * mPerPx;
                dist = Mathf.Sqrt(d * d + mid.x * mid.x + mid.y * mid.y);
            }
            var targets = root.GetComponentsInChildren<Selectable>(false).Where(s => s.interactable && s.isActiveAndEnabled).Select(s => (RectTransform)s.transform)
                .Concat(root.GetComponentsInChildren<MenuSlider>(false).Select(s => (RectTransform)s.transform));
            foreach (var t in targets)
            {
                Measure(t, out float w, out float h, out float dist);
                float ang = Mathf.Rad2Deg * Mathf.Min(w, h) * (d / dist) / dist;
                n++;
                if (ang < minAng) { minAng = ang; minName = t.name; }
            }
            foreach (var t in root.GetComponentsInChildren<TextMeshProUGUI>(false))
            {
                if (string.IsNullOrEmpty(t.text) || !t.isActiveAndEnabled) continue;
                t.ForceMeshUpdate();
                Measure(t.rectTransform, out _, out _, out float dist);
                float dp = t.fontSize * mPerPx * (d / dist) / dist / 0.002f;
                if (dp < minDp) { minDp = dp; minText = $"'{t.text}'"; }
            }
            Plugin.Log.LogInfo($"VR settings check: tab '{tab}'{(showMore ? " (more)" : "")}: {n} targets, smallest {minAng:F1}° ({minName}) {(minAng >= 2.5f ? "OK" : "FAIL")}; smallest text {minDp:F0} dp ({minText}) {(minDp >= 14f ? "OK" : "LOW")}");
        }

        /// <summary>
        /// --cvr-dbg-menu-aim: the xr-sim "hand r point &lt;yaw&gt; &lt;pitch&gt;" that points the right hand's laser at a few
        /// controls (tools/vr-menu.ps1 clicks them). xr-sim aims relative to the head: yaw left-positive, pitch up.
        /// </summary>
        private void LogAims()
        {
            var ui = VrUi.Current;
            if (ui == null || !VrInput.HandTracking(1, out Vector3 hand, out _)) { Plugin.Log.LogWarning("VR settings aim: no panel or right hand"); return; }
            var targets = new List<(string name, RectTransform t, float fx)>();
            void Add(string name, string path, float fx = 0.5f)
            {
                var t = root.transform.Find("Panel/" + path) as RectTransform;
                if (t != null && t.gameObject.activeInHierarchy) targets.Add((name, t, fx));
            }
            Add("control R.trigger", "Body/Control R.trigger");
            Add("action jump", "Body/Action jump");
            Add("action back", "Body/Button Back");
            Add("tab View", "Button View");
            Add("switch Blackout", "Body/Row Blackout when unconscious");
            Add("segment Snap", "Body/Row Turning/Button Snap");
            Add("slider Fall vignette 0.2", "Body/Row Fall vignette/Slider", 0.2f);
            Add("slider Fall vignette 0.8", "Body/Row Fall vignette/Slider", 0.8f);
            Add("segment Moderate", "Body/Row Comfort preset/Button Moderate");
            Add("more", "Button More settings");
            var c = new Vector3[4];
            foreach (var (name, t, fx) in targets)
            {
                t.GetWorldCorners(c);
                var px = new Vector2(Mathf.Lerp(c[0].x, c[2].x, fx), (c[0].y + c[2].y) * 0.5f);
                if (!ui.SettingsPixelToWorld(px, out Vector3 world)) continue;
                Vector3 dir = Quaternion.Inverse(VrSpace.HeadTrackingRot) * (VrSpace.ToTracking(world) - hand);
                float yaw = Mathf.Atan2(-dir.x, dir.z) * Mathf.Rad2Deg, pitch = Mathf.Asin(dir.y / dir.magnitude) * Mathf.Rad2Deg;
                Plugin.Log.LogInfo($"VR settings aim '{name}': point {yaw:F2} {pitch:F2}");
            }
        }

        // ---- uGUI helpers ----------------------------------------------------------------------------

        private Image NewImage(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            return img;
        }

        private TextMeshProUGUI NewText(Transform parent, string text, float size, TextAlignmentOptions align, Color color)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.text = text;
            t.fontSize = size;
            t.alignment = align;
            t.color = color;
            t.raycastTarget = false;
            t.enableWordWrapping = false;
            return t;
        }

        /// <summary>The game's box outline (uiBlockNano), tinted: black inside, so the backing shows through.</summary>
        private static void Frame(Image img, Color tint)
        {
            img.sprite = MenuArt.Nano;
            img.type = Image.Type.Sliced;
            img.color = tint;
        }

        private static void Solid(Image img, Color c)
        {
            img.sprite = null;
            img.type = Image.Type.Simple;
            img.color = c;
        }

        /// <summary>Chosen tabs and segments are filled with the accent colour; the others are outlines.</summary>
        private static void SetSelected(Image img, bool selected)
        {
            if (selected) Solid(img, AccentCol); else Frame(img, ButtonCol);
        }

        private Button NewButton(Transform parent, string text, float size, UnityEngine.Events.UnityAction onClick)
        {
            var img = NewImage(parent, "Button " + text, ButtonCol);
            Frame(img, ButtonCol);
            var b = img.gameObject.AddComponent<Button>();
            var colors = b.colors;
            colors.highlightedColor = new Color(1.5f, 1.5f, 1.6f);
            colors.pressedColor = new Color(0.75f, 0.75f, 0.8f);
            colors.selectedColor = Color.white;   // no sticky "selected" tint after a laser click
            colors.disabledColor = new Color(0.6f, 0.6f, 0.6f, 0.6f);
            colors.fadeDuration = 0.05f;
            b.colors = colors;
            b.navigation = new Navigation { mode = Navigation.Mode.None };
            b.onClick.AddListener(onClick);
            img.gameObject.AddComponent<Fx>().Ticks = true;
            if (!string.IsNullOrEmpty(text))
            {
                var t = NewText(img.transform, text, size, TextAlignmentOptions.Center, Color.white);
                Stretch(t.rectTransform);
            }
            return b;
        }

        private Button NewIconButton(Transform parent, Sprite icon, float rotation, UnityEngine.Events.UnityAction onClick)
        {
            var b = NewButton(parent, "", 0f, onClick);
            b.name = "Button icon";
            var img = NewImage(b.transform, "Icon", LabelCol);
            img.sprite = icon;
            img.raycastTarget = false;
            img.preserveAspect = true;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(40f, 40f);
            rt.localEulerAngles = new Vector3(0f, 0f, rotation);
            return b;
        }

        // Top-left based placement inside the parent rect.
        private static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }

        private static void FitText(TextMeshProUGUI t, float min)
        {
            if (t == null) return;
            t.enableAutoSizing = true;
            t.fontSizeMax = t.fontSize;
            t.fontSizeMin = min;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        // Pixel icons in the game's style (16 px, point filtered): an anticlockwise arrow (reset) and a triangle (scroll).
        private static Sprite MakeResetIcon()
        {
            var tex = NewIconTexture();
            Vector2 tip = Polar(95f, 5.3f), b1 = Polar(55f, 2.7f), b2 = Polar(55f, 7.9f);
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                {
                    var p = new Vector2(x + 0.5f - 8f, y + 0.5f - 8f);
                    float r = p.magnitude, a = Mathf.Atan2(p.y, p.x) * Mathf.Rad2Deg;
                    if (a < 0f) a += 360f;
                    bool ring = r >= 4.1f && r <= 6.5f && !(a > 55f && a < 125f);
                    if (ring || InTriangle(p, tip, b1, b2)) tex.SetPixel(x, y, Color.white);
                }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 16, 16), new Vector2(0.5f, 0.5f), 16f);
        }

        private static Sprite MakeArrowIcon()
        {
            var tex = NewIconTexture();
            for (int y = 3; y < 13; y++)
            {
                int half = (12 - y) * 7 / 10 + 1;   // wide at the bottom, a point at the top
                for (int x = 8 - half; x < 8 + half; x++) tex.SetPixel(x, y, Color.white);
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 16, 16), new Vector2(0.5f, 0.5f), 16f);
        }

        private static Texture2D NewIconTexture()
        {
            var tex = new Texture2D(16, 16, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "CasualtyVR.MenuIcon" };
            tex.SetPixels(Enumerable.Repeat(new Color(1f, 1f, 1f, 0f), 256).ToArray());
            return tex;
        }

        private static Vector2 Polar(float deg, float r) => new Vector2(Mathf.Cos(deg * Mathf.Deg2Rad), Mathf.Sin(deg * Mathf.Deg2Rad)) * r;

        private static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float Cross(Vector2 u, Vector2 v, Vector2 w) => (v.x - u.x) * (w.y - u.y) - (v.y - u.y) * (w.x - u.x);
            float d1 = Cross(p, a, b), d2 = Cross(p, b, c), d3 = Cross(p, c, a);
            bool neg = d1 < 0f || d2 < 0f || d3 < 0f, pos = d1 > 0f || d2 > 0f || d3 > 0f;
            return !(neg && pos);
        }

        /// <summary>Hover and click feedback on a menu element (M2d): callbacks, a light haptic tick, the "lift" scale.</summary>
        private sealed class Fx : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            public Action Enter, Exit;
            public bool Ticks;

            public void OnPointerEnter(PointerEventData e)
            {
                Enter?.Invoke();
                if (!Ticks) return;
                var sel = GetComponent<Selectable>();
                if (sel != null && !sel.IsInteractable()) return;
                Instance?.Tick(0.1f, 0.01f);
                if (sel != null && sel.transition == Selectable.Transition.ColorTint) transform.localScale = new Vector3(1.04f, 1.04f, 1f);
            }

            public void OnPointerExit(PointerEventData e)
            {
                Exit?.Invoke();
                transform.localScale = Vector3.one;
            }

            private void OnDisable() => transform.localScale = Vector3.one;
        }
    }

    /// <summary>A slider track for the laser (VRMENU_REVIEW M2c): press on it and drag; the value follows the pointer.</summary>
    internal sealed class MenuSlider : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler, IInitializePotentialDragHandler
    {
        public Action<float> OnValue;
        public Action OnRelease;

        public void OnInitializePotentialDrag(PointerEventData e) => e.useDragThreshold = false;
        public void OnPointerDown(PointerEventData e) => Apply(e);
        public void OnDrag(PointerEventData e) => Apply(e);
        public void OnPointerUp(PointerEventData e) => OnRelease?.Invoke();

        private void Apply(PointerEventData e)
        {
            var group = GetComponentInParent<CanvasGroup>();
            if (group != null && !group.interactable) return;
            var rt = (RectTransform)transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, e.position, e.pressEventCamera, out Vector2 local)) return;
            float t = Mathf.Clamp01((local.x - rt.rect.xMin) / rt.rect.width);
            OnValue?.Invoke(t);
        }
    }
}
