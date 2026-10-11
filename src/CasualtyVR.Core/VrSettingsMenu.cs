using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CasualtyVR
{
    /// <summary>
    /// In-game VR settings (user request 2026-10-04): view, blocks, controller mapping and passthrough,
    /// all backed by the BepInEx config (saved as they change). Opened from a "VR" button added to the
    /// game's pause menu, or the SettingsKey (F12). It is an ordinary overlay canvas, so VrUi moves it to
    /// the UI camera and shows it on the dashboard like the game's own menus; the controller laser clicks it.
    /// Every row is "label  &lt;  value  &gt;" so it works with a pointer and needs no text entry.
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

        private sealed class Row
        {
            public string Label;
            public Func<string> Value;
            public Action<int> Step;
        }

        private static readonly string[] Tabs = { "View", "Blocks", "1st person", "Hands", "Controls", "Passthrough" };
        private static readonly (string name, Color c)[] KeyPresets =
        {
            ("Green", new Color(0f, 1f, 0f)), ("Blue", new Color(0f, 0f, 1f)), ("Magenta", new Color(1f, 0f, 1f)),
            ("Black", Color.black), ("White", Color.white),
        };

        private GameObject canvasGo, root;
        private RectTransform body;
        private TMP_FontAsset font;
        private string tab = "View";
        private readonly List<(TextMeshProUGUI text, Func<string> get)> live = new List<(TextMeshProUGUI, Func<string>)>();
        private readonly List<(Image image, string name)> tabButtons = new List<(Image, string)>();
        private PauseHandler injectedFor;
        private float nextRefresh;
        // "Reset page" (user, 2026-10-06): the settings shown on the current tab, back to their defaults. The first
        // press arms it ("Press again"), a second within 3 s resets, so a stray laser click cannot.
        private readonly List<BepInEx.Configuration.ConfigEntryBase> pageEntries = new List<BepInEx.Configuration.ConfigEntryBase>();
        private TextMeshProUGUI resetLabel;
        private float resetArmedUntil = -1f;

        private void Awake() => Instance = this;

        // Headless test: --cvr-dbg-settings <tab> opens the menu on that tab once gameplay is up.
        private static readonly string DbgTab = ArgAfter("--cvr-dbg-settings");
        private bool dbgOpened;
        private float dbgResetAt = -1f;
        private float dbgNextTab;

        private static string ArgAfter(string flag)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, flag);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        private void Update()
        {
            if (Input.GetKeyDown(Plugin.SettingsKey.Value)) Toggle();
            if (DbgTab != null && !dbgOpened && PlayerCamera.main != null && VrRig.Instance != null && VrRig.Instance.Active && Time.timeSinceLevelLoad > 8f)
            {
                dbgOpened = true;
                Open();
                ShowTab(Array.Find(Tabs, t => t.StartsWith(DbgTab, StringComparison.OrdinalIgnoreCase)) ?? Tabs[0]);
                dbgNextTab = Time.unscaledTime + 3f;
                dbgResetAt = Array.IndexOf(Environment.GetCommandLineArgs(), "--cvr-dbg-reset") >= 0 ? Time.unscaledTime + 4f : -1f;
            }
            // Headless test: --cvr-dbg-settings all shows each tab in turn, 3 s each (layout captures).
            if (dbgOpened && DbgTab == "all" && IsOpen && Time.unscaledTime >= dbgNextTab)
            {
                dbgNextTab = Time.unscaledTime + 3f;
                ShowTab(Tabs[(Array.IndexOf(Tabs, tab) + 1) % Tabs.Length]);
                Plugin.Log.LogInfo($"VR settings: tab '{tab}' ({body.childCount / 4} rows)");
            }
            // Headless test: --cvr-dbg-reset presses "Reset page" twice (arm, then confirm) on the --cvr-dbg-settings tab.
            if (dbgResetAt > 0f && Time.unscaledTime >= dbgResetAt)
            {
                ResetPage();
                dbgResetAt = resetArmedUntil > 0f ? Time.unscaledTime + 1f : -1f;
            }
            InjectPauseButton();
            if (resetArmedUntil > 0f && Time.unscaledTime > resetArmedUntil) DisarmReset();
            if (IsOpen && Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + 0.15f;
                foreach (var (text, get) in live) if (text != null) text.text = get();
            }
        }

        public void Toggle()
        {
            if (IsOpen) Close(); else Open();
        }

        public void Open()
        {
            if (root == null) Build();
            root.SetActive(true);
            ShowTab(tab);
            Plugin.Log.LogInfo("VR settings: opened");
        }

        public void Close()
        {
            if (root != null) root.SetActive(false);
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
            rt.localPosition = src.localPosition + new Vector3(0f, -src.rect.height * 1.25f, 0f);
            rt.localScale = Vector3.one;
            var button = clone.GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(Open);
            // Text only: the cloned round icon was cut off at the menu's edge (user, 2026-10-04). The image
            // stays as the (invisible) click target.
            if (clone.TryGetComponent<Image>(out var img)) img.color = new Color(1f, 1f, 1f, 0f);
            for (int i = clone.transform.childCount - 1; i >= 0; i--) Destroy(clone.transform.GetChild(i).gameObject);
            if (clone.TryGetComponent<UITooltip>(out var tip))
            {
                tip.skipLocale = true;
                tip.tipName = "VR settings";
                tip.tipDesc = "Zoom, blocks, controller mapping, passthrough";
            }
            if (font == null && ph.pauseText != null) font = ph.pauseText.font;
            var label = NewText(rt, "VR", 44, TextAlignmentOptions.Center);
            label.color = new Color(0.55f, 0.9f, 1f);
            Stretch(label.rectTransform);
            Plugin.Log.LogInfo("VR settings: button added to the pause menu");
        }

        // ---- construction ----------------------------------------------------------------------------

        private void Build()
        {
            font = PauseHandler.main != null && PauseHandler.main.pauseText != null ? PauseHandler.main.pauseText.font : TMP_Settings.defaultFontAsset;
            canvasGo = new GameObject("CasualtyVR.SettingsCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            DontDestroyOnLoad(canvasGo);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            root = NewPanel(canvasGo.transform, "Panel", new Color(0.05f, 0.06f, 0.08f, 1f));   // opaque: HUD banners showed through
            var rrt = (RectTransform)root.transform;
            rrt.anchorMin = rrt.anchorMax = new Vector2(0.5f, 0.5f);
            rrt.sizeDelta = new Vector2(1240f, 950f);

            var title = NewText(rrt, "VR settings", 46, TextAlignmentOptions.Left);
            Place(title.rectTransform, 40f, 30f, 600f, 60f);

            float x = 40f;
            foreach (var t in Tabs)
            {
                var name = t;
                var b = NewButton(rrt, t, 26, () => ShowTab(name));
                FitText(b.GetComponentInChildren<TextMeshProUGUI>(), 16f);
                Place((RectTransform)b.transform, x, 105f, 186f, 60f);
                tabButtons.Add((b.GetComponent<Image>(), t));
                x += 194f;
            }
            var done = NewButton(rrt, "Done", 32, Close);
            Place((RectTransform)done.transform, 1240f - 40f - 200f, 30f, 200f, 60f);
            var reset = NewButton(rrt, "Reset page", 28, ResetPage);
            Place((RectTransform)reset.transform, 1240f - 40f - 200f - 20f - 240f, 30f, 240f, 60f);
            resetLabel = reset.GetComponentInChildren<TextMeshProUGUI>();
            // Recenter is an action, not a setting: one button for every page (it was a row on two pages).
            var recenter = NewButton(rrt, "Recenter", 28, () => VrRig.Instance?.Recenter());
            Place((RectTransform)recenter.transform, 1240f - 40f - 200f - 20f - 240f - 20f - 200f, 30f, 200f, 60f);
            var version = NewText(rrt, "CasualtyVR " + Plugin.Version, 20, TextAlignmentOptions.Right);
            version.color = new Color(0.6f, 0.65f, 0.7f);
            Place(version.rectTransform, 1240f - 40f - 400f, 905f, 400f, 30f);

            var bodyGo = new GameObject("Body", typeof(RectTransform));
            body = (RectTransform)bodyGo.transform;
            body.SetParent(rrt, false);
            Place(body, 40f, 190f, 1160f, 700f);
            root.SetActive(false);
        }

        private void ShowTab(string name)
        {
            tab = name;
            foreach (var (image, t) in tabButtons) image.color = t == name ? new Color(0.25f, 0.55f, 0.7f) : new Color(0.16f, 0.18f, 0.22f);
            for (int i = body.childCount - 1; i >= 0; i--) Destroy(body.GetChild(i).gameObject);
            live.Clear();
            pageEntries.Clear();
            DisarmReset();
            var rows = RowsFor(name);
            // One column while it fits (labels need the width); two only for the long controls list.
            bool twoCols = rows.Count > 18;
            int perCol = twoCols ? (rows.Count + 1) / 2 : rows.Count;
            float colW = twoCols ? 570f : 1160f;
            float rowH = Mathf.Min(twoCols ? 64f : 56f, 700f / Mathf.Max(1, perCol));
            for (int i = 0; i < rows.Count; i++)
            {
                int col = i / perCol, r = i % perCol;
                AddRow(rows[i], col * (colW + 20f), r * rowH, colW, rowH - 8f, twoCols ? 0.42f : 0.5f);
            }
        }

        private void AddRow(Row row, float x, float y, float w, float h, float labelFrac)
        {
            float font = Mathf.Min(28f, h * 0.8f);
            var label = NewText(body, row.Label, font, TextAlignmentOptions.Left);
            FitText(label, 14f);
            Place(label.rectTransform, x, y, w * labelFrac, h);
            float bx = x + w * labelFrac;
            var minus = NewButton(body, "<", 30, () => { row.Step(-1); Refresh(); });
            Place((RectTransform)minus.transform, bx, y, h, h);
            var value = NewText(body, row.Value(), font, TextAlignmentOptions.Center);
            FitText(value, 14f);
            Place(value.rectTransform, bx + h + 6f, y, w * (1f - labelFrac) - 2f * h - 12f, h);
            var plus = NewButton(body, ">", 30, () => { row.Step(+1); Refresh(); });
            Place((RectTransform)plus.transform, x + w - h, y, h, h);
            live.Add((value, row.Value));
        }

        private void Refresh() => nextRefresh = 0f;

        private void ResetPage()
        {
            if (resetArmedUntil < 0f)
            {
                resetArmedUntil = Time.unscaledTime + 3f;
                if (resetLabel != null) { resetLabel.text = "Press again"; resetLabel.color = new Color(1f, 0.75f, 0.35f); }
                return;
            }
            DisarmReset();
            foreach (var e in pageEntries) e.BoxedValue = e.DefaultValue;
            PopupDepth.Scale = Plugin.PopupScale.Value;
            Plugin.Log.LogInfo($"VR settings: reset {pageEntries.Count} settings on '{tab}' to defaults");
            Refresh();
        }

        private void DisarmReset()
        {
            resetArmedUntil = -1f;
            if (resetLabel != null) { resetLabel.text = "Reset page"; resetLabel.color = Color.white; }
        }

        /// <summary>A setting on this page that "Reset page" resets (Num and Toggle add theirs).</summary>
        private void Track(params BepInEx.Configuration.ConfigEntryBase[] entries) => pageEntries.AddRange(entries);

        // ---- the settings ----------------------------------------------------------------------------

        private List<Row> RowsFor(string name)
        {
            var rows = new List<Row>();
            switch (name)
            {
                case "View":
                    // The mode switches (first person on/off, passthrough on/off) are not reset with their pages.
                    Track(Plugin.UnitsPerMetre, Plugin.FogMode, Plugin.FogMist, Plugin.FogHaze, Plugin.CursorStyle, Plugin.HandCursorSize, Plugin.CursorSize);
                    rows.Add(new Row { Label = "Zoom", Value = () => $"{45f / Plugin.UnitsPerMetre.Value:F2} m tall", Step = d => Plugin.UnitsPerMetre.Value = Mathf.Clamp(Plugin.UnitsPerMetre.Value * (d > 0 ? 1f / 1.1f : 1.1f), 8f, 80f) });
                    rows.Add(Num("Diorama distance (m)", Plugin.DioramaDistance, 0.05f, 0.4f, 3f, "F2"));
                    rows.Add(Num("Window size", Plugin.FrameMargin, 0.05f, 0f, 2f, "F2"));
                    rows.Add(Num("Window edge fade", Plugin.FrameFade, 0.1f, 0f, 2f, "F1"));
                    rows.Add(Num("HUD size", Plugin.HudScale, 0.1f, 0.5f, 2f, "F1"));
                    rows.Add(Toggle("Hover highlight (diorama)", Plugin.HoverHighlight));
                    rows.Add(new Row
                    {
                        Label = "Fog (foggy layers)",
                        Value = () => Plugin.FogMode.Value,
                        Step = d =>
                        {
                            var modes = new[] { "Mist", "Haze", "Off" };
                            int i = Array.IndexOf(modes, Plugin.FogMode.Value);
                            Plugin.FogMode.Value = modes[((i < 0 ? 0 : i) + d + modes.Length) % modes.Length];
                        },
                    });
                    rows.Add(new Row
                    {
                        Label = "Fog strength",
                        Value = () => (Plugin.FogMode.Value == "Haze" ? Plugin.FogHaze.Value : Plugin.FogMist.Value).ToString("F2"),
                        Step = d =>
                        {
                            var e = Plugin.FogMode.Value == "Haze" ? Plugin.FogHaze : Plugin.FogMist;
                            e.Value = Mathf.Clamp01((float)Math.Round(e.Value + d * 0.05f, 2));
                        },
                    });
                    rows.Add(Num("Dashboard distance (m)", Plugin.DashboardDistance, 0.05f, 0.3f, 2f, "F2"));
                    rows.Add(Num("Dashboard height (m)", Plugin.DashboardHeight, 0.05f, -1.5f, 0.5f, "F2"));
                    rows.Add(Num("Dashboard width (m)", Plugin.DashboardWidth, 0.1f, 0.4f, 3f, "F1"));
                    rows.Add(new Row
                    {
                        Label = "Cursor",
                        Value = () => Plugin.CursorStyle.Value,
                        Step = d =>
                        {
                            var styles = new[] { "Hand", "Cursor", "Off" };
                            int i = Array.IndexOf(styles, Plugin.CursorStyle.Value);
                            Plugin.CursorStyle.Value = styles[((i < 0 ? 0 : i) + d + styles.Length) % styles.Length];
                        },
                    });
                    // One size row for whichever cursor is shown (was two: hand and arrow).
                    rows.Add(new Row
                    {
                        Label = "Cursor size (m)",
                        Value = () => Plugin.CursorStyle.Value == "Hand" ? Plugin.HandCursorSize.Value.ToString("F3") : Plugin.CursorStyle.Value == "Cursor" ? Plugin.CursorSize.Value.ToString("F3") : "-",
                        Step = d =>
                        {
                            if (Plugin.CursorStyle.Value == "Hand") Plugin.HandCursorSize.Value = Mathf.Clamp((float)Math.Round(Plugin.HandCursorSize.Value + d * 0.005f, 4), 0.01f, 0.2f);
                            else if (Plugin.CursorStyle.Value == "Cursor") Plugin.CursorSize.Value = Mathf.Clamp((float)Math.Round(Plugin.CursorSize.Value + d * 0.005f, 4), 0.005f, 0.1f);
                        },
                    });
                    rows.Add(OptNum("Controller hands (size, m)", Plugin.ShowHands, Plugin.HandSize, 0.01f, 0.03f, 0.3f, "F2"));
                    break;
                case "1st person":
                    Track(Plugin.FpHighlight, Plugin.FpSides, Plugin.FpVoidDarkness, Plugin.FpTurning, Plugin.FpTurnSpeed, Plugin.FpRagdollView, Plugin.FpUnitsPerMetre);
                    rows.Add(new Row { Label = $"First person ({Plugin.FpKey.Value})", Value = () => Plugin.FpWanted ? "On" : "Off", Step = d => { bool now = !Plugin.FpWanted; Plugin.FpForced = false; Plugin.FpOn.Value = now; } });
                    // Sides and the void's distance darkness in one row (the darkness only applies to the void).
                    rows.Add(new Row
                    {
                        Label = "Tunnel sides",
                        Value = () => Plugin.FpSides.Value != "Void" ? "Cave walls" : Plugin.FpVoidDarkness.Value <= 0.001f ? "Void" : $"Void, {Plugin.FpVoidDarkness.Value * 100f:F0}% dark far off",
                        Step = d =>
                        {
                            int i = Plugin.FpSides.Value != "Void" ? 0 : 1 + NearestIndex(VoidDarkness, Plugin.FpVoidDarkness.Value);
                            i = (i + d + VoidDarkness.Length + 1) % (VoidDarkness.Length + 1);
                            Plugin.FpSides.Value = i == 0 ? "CaveWalls" : "Void";
                            if (i > 0) Plugin.FpVoidDarkness.Value = VoidDarkness[i - 1];
                        },
                    });
                    rows.Add(Toggle("Characters face you", Plugin.FpBillboards));
                    rows.Add(Toggle("Objects face you", Plugin.FpFaceObjects));
                    rows.Add(new Row
                    {
                        Label = "Highlight under your hand",
                        Value = () => Plugin.FpHighlight.Value == "All" ? "Items and blocks" : Plugin.FpHighlight.Value == "Off" ? "Off" : "Blocks only",
                        Step = d =>
                        {
                            var modes = new[] { "Off", "Blocks", "All" };
                            int i = Array.IndexOf(modes, Plugin.FpHighlight.Value);
                            Plugin.FpHighlight.Value = modes[((i < 0 ? 0 : i) + d + modes.Length) % modes.Length];
                        },
                    });
                    // Snap, or smooth at a speed, in one row.
                    rows.Add(new Row
                    {
                        Label = "Turning",
                        Value = () => Plugin.FpTurning.Value == "Smooth" ? $"Smooth {Plugin.FpTurnSpeed.Value:F0}\u00b0/s" : "Snap",
                        Step = d =>
                        {
                            int i = Plugin.FpTurning.Value != "Smooth" ? 0 : 1 + NearestIndex(TurnSpeeds, Plugin.FpTurnSpeed.Value);
                            i = Mathf.Clamp(i + d, 0, TurnSpeeds.Length);
                            Plugin.FpTurning.Value = i == 0 ? "Snap" : "Smooth";
                            if (i > 0) Plugin.FpTurnSpeed.Value = TurnSpeeds[i - 1];
                        },
                    });
                    rows.Add(Num("Movement speed", Plugin.FpMoveSpeed, 0.05f, 0.5f, 1f, "F2"));
                    rows.Add(new Row { Label = "When knocked down", Value = () => Plugin.FpRagdollView.Value == "Stay" ? "Stay" : "Diorama", Step = d => Plugin.FpRagdollView.Value = Plugin.FpRagdollView.Value == "Stay" ? "Diorama" : "Stay" });
                    rows.Add(Num("Fall vignette", Plugin.FpVignette, 0.1f, 0f, 1f, "F1"));
                    rows.Add(Toggle("Blackout when unconscious", Plugin.FpBlackout));
                    rows.Add(Num("Camera smoothing", Plugin.FpSmoothing, 0.1f, 0f, 1f, "F1"));
                    rows.Add(Num("Tunnel width (blocks)", Plugin.FpCorridor, 0.5f, 1f, 12f, "F1"));
                    rows.Add(Num("View distance (blocks)", Plugin.FpViewDistance, 10f, 20f, 200f, "F0"));
                    rows.Add(new Row
                    {
                        Label = "World scale (blocks/m)",
                        Value = () => Plugin.FpUnitsPerMetre.Value <= 0f ? "auto" : Plugin.FpUnitsPerMetre.Value.ToString("F2"),
                        // auto <-> 0.8 .. 4 in 0.1 steps; stepping below 0.8 returns to auto.
                        Step = d =>
                        {
                            float v = Plugin.FpUnitsPerMetre.Value;
                            if (v <= 0f) v = d > 0 ? 0.8f : 0f;
                            else { v = (float)Math.Round(v + d * 0.1f, 2); if (v < 0.8f) v = 0f; }
                            Plugin.FpUnitsPerMetre.Value = Mathf.Min(v, 4f);
                        },
                    });
                    break;
                case "Hands":
                    Track(Plugin.FpAimWith, Plugin.FpGrabMode);
                    rows.Add(Toggle("Character looks where you look", Plugin.FpLookWithHead));
                    rows.Add(new Row { Label = "Aim with", Value = () => Plugin.FpAimWith.Value, Step = d => Plugin.FpAimWith.Value = Plugin.FpAimWith.Value == "Hand" ? "Head" : "Hand" });
                    rows.Add(OptNum("Your arms (thickness)", Plugin.FpArms, Plugin.FpArmThickness, 0.1f, 0.1f, 2f, "F1"));
                    rows.Add(OptNum("Your body (thickness)", Plugin.FpBody, Plugin.FpBodyDepth, 0.1f, 0.2f, 2f, "F1"));
                    rows.Add(Num("Body behind eyes (m)", Plugin.FpBodyBack, 0.05f, -0.2f, 1.0f, "F2"));
                    rows.Add(new Row { Label = "Grab", Value = () => Plugin.FpGrabMode.Value == "Hold" ? "Hold grip" : "Toggle", Step = d => Plugin.FpGrabMode.Value = Plugin.FpGrabMode.Value == "Hold" ? "Toggle" : "Hold" });
                    rows.Add(Toggle("Swing to attack", Plugin.FpSwingAttack));
                    rows.Add(Toggle("Touch minigames", Plugin.FpMinigameTouch));
                    rows.Add(OptNum("Vibration (strength)", Plugin.Haptics, Plugin.HapticStrength, 0.1f, 0.1f, 1f, "F1"));
                    break;
                case "Blocks":
                    Track(Plugin.PopupScale);
                    rows.Add(Toggle("3D blocks", Plugin.VoxelsOn));
                    rows.Add(Num("Block front depth", Plugin.VoxelFront, 0.25f, 0f, 4f, "F2"));
                    rows.Add(Num("Block back depth", Plugin.VoxelBack, 0.25f, 0f, 6f, "F2"));
                    rows.Add(new Row { Label = "Pop-up depth", Value = () => $"{Plugin.PopupScale.Value:F1}", Step = d => { Plugin.PopupScale.Value = Mathf.Clamp(Plugin.PopupScale.Value + d * 0.1f, 0f, 3f); PopupDepth.Scale = Plugin.PopupScale.Value; } });
                    // Thickness layers (Voxels.ThicknessLayers) is config-only now: a quality setting.
                    rows.Add(OptNum("Object depth (blocks)", Plugin.ObjectDepthOn, Plugin.ObjectDepth, 0.25f, 0.25f, 4f, "F2"));
                    rows.Add(OptNum("Character thickness (blocks)", Plugin.SlabsOn, Plugin.SlabThickness, 0.05f, 0.05f, 1.5f, "F2"));
                    rows.Add(OptNum("Liquid depth (surface opacity)", Plugin.LiquidDepthOn, Plugin.LiquidSurfaceAlpha, 0.05f, 0f, 1f, "F2"));
                    break;
                case "Controls":
                    foreach (var kv in Plugin.ControlBinds)
                    {
                        var entry = kv.Value;
                        Track(entry);
                        rows.Add(new Row
                        {
                            Label = ControlLabel(kv.Key),
                            Value = () => ActionName(entry.Value),
                            Step = d =>
                            {
                                int i = Array.IndexOf(VrInput.Actions, entry.Value);
                                i = ((i < 0 ? 0 : i) + d + VrInput.Actions.Length) % VrInput.Actions.Length;
                                entry.Value = VrInput.Actions[i];
                            },
                        });
                    }
                    rows.Add(Num("Minigame hand X (px)", Plugin.MinigameHandX, 10f, -400f, 400f, "F0"));
                    rows.Add(Num("Minigame hand Y (px)", Plugin.MinigameHandY, 10f, -400f, 400f, "F0"));
                    break;
                case "Passthrough":
                    Track(Plugin.GreenscreenColor);
                    rows.Add(Toggle("Passthrough greenscreen", Plugin.Greenscreen));
                    rows.Add(new Row
                    {
                        Label = "Key colour",
                        Value = () => PresetName(Plugin.GreenscreenColor.Value),
                        Step = d =>
                        {
                            int i = Array.FindIndex(KeyPresets, p => p.c == Plugin.GreenscreenColor.Value);
                            i = ((i < 0 ? 0 : i) + d + KeyPresets.Length) % KeyPresets.Length;
                            Plugin.GreenscreenColor.Value = KeyPresets[i].c;
                        },
                    });
                    rows.Add(Channel("Red", 0));
                    rows.Add(Channel("Green", 1));
                    rows.Add(Channel("Blue", 2));
                    break;
            }
            return rows;
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

        private static string PresetName(Color c)
        {
            foreach (var (name, pc) in KeyPresets) if (pc == c) return name;
            return $"#{ColorUtility.ToHtmlStringRGB(c)}";
        }

        private static Row Channel(string label, int ch)
        {
            return new Row
            {
                Label = label,
                Value = () => $"{Plugin.GreenscreenColor.Value[ch]:F2}",
                Step = d =>
                {
                    var c = Plugin.GreenscreenColor.Value;
                    c[ch] = Mathf.Clamp01(Mathf.Round((c[ch] + d * 0.05f) * 20f) / 20f);
                    c.a = 1f;
                    Plugin.GreenscreenColor.Value = c;
                },
            };
        }

        private Row Num(string label, BepInEx.Configuration.ConfigEntry<float> e, float step, float min, float max, string fmt)
        {
            Track(e);
            return new Row { Label = label, Value = () => e.Value.ToString(fmt), Step = d => e.Value = Mathf.Clamp((float)Math.Round(e.Value + d * step, 4), min, max) };
        }

        /// <summary>
        /// A switch and its amount in one row: "Off", or the amount. &lt; at the lowest amount switches it off,
        /// &gt; from Off switches it back on at the amount it had.
        /// </summary>
        private Row OptNum(string label, BepInEx.Configuration.ConfigEntry<bool> on, BepInEx.Configuration.ConfigEntry<float> e, float step, float min, float max, string fmt)
        {
            Track(on, e);
            return new Row
            {
                Label = label,
                Value = () => on.Value ? e.Value.ToString(fmt) : "Off",
                Step = d =>
                {
                    if (!on.Value) { if (d > 0) on.Value = true; return; }
                    if (d < 0 && e.Value <= min + step * 0.01f) { on.Value = false; return; }
                    e.Value = Mathf.Clamp((float)Math.Round(e.Value + d * step, 4), min, max);
                },
            };
        }

        private static readonly float[] VoidDarkness = { 0f, 0.3f, 0.6f, 1f };
        private static readonly float[] TurnSpeeds = { 45f, 60f, 90f, 120f, 180f, 240f, 300f, 360f };

        private static int NearestIndex(float[] values, float v)
        {
            int best = 0;
            for (int i = 1; i < values.Length; i++) if (Mathf.Abs(values[i] - v) < Mathf.Abs(values[best] - v)) best = i;
            return best;
        }

        private Row Toggle(string label, BepInEx.Configuration.ConfigEntry<bool> e)
        {
            if (e != Plugin.Greenscreen) Track(e);
            return new Row { Label = label, Value = () => e.Value ? "On" : "Off", Step = d => e.Value = !e.Value };
        }

        // ---- uGUI helpers ----------------------------------------------------------------------------

        private GameObject NewPanel(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color;
            return go;
        }

        private TextMeshProUGUI NewText(Transform parent, string text, float size, TextAlignmentOptions align)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.text = text;
            t.fontSize = size;
            t.alignment = align;
            t.color = Color.white;
            t.raycastTarget = false;
            t.enableWordWrapping = false;
            return t;
        }

        private Button NewButton(Transform parent, string text, float size, UnityEngine.Events.UnityAction onClick)
        {
            var go = NewPanel(parent, "Button " + text, new Color(0.16f, 0.18f, 0.22f));
            var b = go.AddComponent<Button>();
            var colors = b.colors;
            colors.highlightedColor = new Color(1.4f, 1.4f, 1.4f);
            colors.pressedColor = new Color(0.7f, 0.7f, 0.7f);
            b.colors = colors;
            b.onClick.AddListener(onClick);
            var t = NewText(go.transform, text, size, TextAlignmentOptions.Center);
            Stretch(t.rectTransform);
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
    }
}
