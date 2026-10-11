using System;
using System.Collections;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CasualtyVR.Probe
{
    /// <summary>
    /// Phase 0 read-only probe. Records what the VR work depends on (graphics API, render pipeline,
    /// cameras, canvases, sorting layers, lights, world grid) without changing rendering or gameplay.
    ///
    /// Automation, all opt-in through command-line arguments:
    ///   --cvr-autostart        skip the menu and start a run (marks the sandboxed tutorial flag done)
    ///   --cvr-seed N           seed UnityEngine.Random before world generation (deterministic world)
    ///   --cvr-probe-delay S    seconds after generation finishes before the gameplay report (default 5)
    ///   --cvr-screenshot       save a screenshot next to the gameplay report
    ///   --cvr-quit             quit after the gameplay report
    ///   --cvr-spike            Phase 1 stereo spike: save ortho/perspective/eye PNGs (see StereoSpike)
    ///   --cvr-eye-sep U        spike eye separation in world units (default 1)
    ///   --cvr-renderdoc        trigger a RenderDoc frame capture after the report (launch via renderdoccmd)
    /// F8 writes a report at any time.
    /// </summary>
    [BepInPlugin(Guid, "CasualtyVR Probe", "0.1.0")]
    public class ProbePlugin : BaseUnityPlugin
    {
        public const string Guid = "casualtyvr.probe";

        internal static ManualLogSource Log;
        internal static ProbeOptions Options;
        internal static string ReportDir;
        private static int reportCounter;

        private void Awake()
        {
            Log = Logger;
            Options = ProbeOptions.Parse(Environment.GetCommandLineArgs());
            ReportDir = Path.Combine(Paths.BepInExRootPath, "CasualtyVR", "run-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(ReportDir);
            Log.LogInfo("Options: " + Options);

            new Harmony(Guid).PatchAll(typeof(ProbePlugin).Assembly);
            WriteReport("boot");

            // The game destroys BepInEx's manager GameObject when the first scene loads, which kills
            // this component's Update and coroutines. Run on our own persistent object instead.
            var host = new GameObject("CasualtyVR.Probe");
            host.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(host);
            host.AddComponent<ProbeRunner>();
        }

        internal static string WriteReport(string label, FrameStats frameStats = null)
        {
            string path = Path.Combine(ReportDir, $"probe-{++reportCounter:D2}-{label}.txt");
            try
            {
                string text = ProbeReport.Build(label, frameStats);
                File.WriteAllText(path, text);
                Log.LogInfo($"Report written: {path}");
            }
            catch (Exception ex)
            {
                Log.LogError($"Report '{label}' failed: {ex}");
            }
            return path;
        }
    }

    internal sealed class ProbeRunner : MonoBehaviour
    {
        private bool gameplayReportDone;

        private void Awake() => SceneManager.sceneLoaded += OnSceneLoaded;

        private void OnDestroy() => SceneManager.sceneLoaded -= OnSceneLoaded;

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F8))
                ProbePlugin.WriteReport("manual");
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            ProbePlugin.Log.LogInfo($"Scene loaded: {scene.name} (build index {scene.buildIndex}, mode {mode})");
            StartCoroutine(AfterSceneLoaded(scene.name));
        }

        private IEnumerator AfterSceneLoaded(string sceneName)
        {
            yield return null;
            yield return null;
            ProbePlugin.WriteReport("scene-" + sceneName);

            if (ProbePlugin.Options.AutoStart && PreRunScript.instance != null)
            {
                yield return new WaitForSeconds(1f);
                ProbePlugin.Log.LogInfo("Autostart: starting a run");
                PreRunScript.instance.MarkTutorialCompleted();
                PreRunScript.instance.StartRun();
            }

            if (WorldGeneration.world != null && !gameplayReportDone)
                yield return GameplayReport();
        }

        /// <summary>--cvr-pool: water in the two air rows above the floor around the player (liquid test scene).</summary>
        private static void FillPool()
        {
            var world = WorldGeneration.world;
            var fm = FluidManager.main;
            if (world == null || fm == null) { ProbePlugin.Log.LogWarning("Pool: no world/fluid manager"); return; }
            var p = world.WorldToBlockPos(PlayerCamera.main.body.transform.position);
            int floor = p.y;
            while (floor > p.y - 10 && world.GetBlock(new Vector2Int(p.x, floor)) == 0) floor--;
            int n = 0;
            for (int x = p.x - 8; x <= p.x + 8; x++)
                for (int y = floor + 1; y <= floor + 2; y++)
                    if (world.GetBlock(new Vector2Int(x, y)) == 0 && !fm.HasLiquid(new Vector2Int(x, y)))
                    {
                        fm.SetLiquid(x, y, 1);
                        n++;
                    }
            ProbePlugin.Log.LogInfo($"Pool: {n} water cells above floor y={floor} around x={p.x}");
        }

        private IEnumerator GameplayReport()
        {
            float started = Time.realtimeSinceStartup;
            while (WorldGeneration.world != null &&
                   (WorldGeneration.world.generatingWorld || PlayerCamera.main == null || PlayerCamera.main.body == null))
            {
                if (Time.realtimeSinceStartup - started > 300f) { ProbePlugin.Log.LogWarning("World never became ready"); yield break; }
                yield return new WaitForSeconds(0.5f);
            }
            ProbePlugin.Log.LogInfo($"World ready after {Time.realtimeSinceStartup - started:F1}s");
            if (ProbePlugin.Options.Pool) FillPool();
            yield return new WaitForSecondsRealtime(ProbePlugin.Options.ProbeDelay);

            if (!string.IsNullOrEmpty(ProbePlugin.Options.Command))
            {
                // Runs a game console command (e.g. "kill") through ConsoleScript's own dispatcher.
                var method = typeof(ConsoleScript).GetMethod("TryExecuteCommand", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                ProbePlugin.Log.LogInfo($"Console command '{ProbePlugin.Options.Command}' (dispatcher {(method != null ? "found" : "MISSING")})");
                if (ProbePlugin.Options.Command == "!selfdestruct")
                    PlayerCamera.main.StartSelfDestruct();   // what the pause menu's 4th "Quit" press does
                else if (ProbePlugin.Options.Command == "!endsequence")
                    PlayerCamera.main.StartCoroutine(PlayerCamera.main.EndSequence(0));   // the game's own death/end screen
                else
                    method?.Invoke(ConsoleScript.instance, new object[] { ProbePlugin.Options.Command.Split(' '), true });
                yield return new WaitForSecondsRealtime(ProbePlugin.Options.CommandWait);
            }
            var frameStats = new FrameStats();
            yield return frameStats.Measure(120);
            string path = ProbePlugin.WriteReport("gameplay", frameStats);
            gameplayReportDone = true;

            if (ProbePlugin.Options.Screenshot)
            {
                string shot = Path.ChangeExtension(path, ".png");
                ScreenCapture.CaptureScreenshot(shot);
                yield return new WaitForSecondsRealtime(1f);
                ProbePlugin.Log.LogInfo("Screenshot: " + shot);
            }
            if (ProbePlugin.Options.LiquidSpike)
                yield return LiquidSpike.Run(ProbePlugin.ReportDir);
            if (ProbePlugin.Options.Spike)
                yield return StereoSpike.Run(ProbePlugin.ReportDir, ProbePlugin.Options.EyeSeparation);
            if (ProbePlugin.Options.RenderDoc)
            {
                string template = Path.Combine(ProbePlugin.ReportDir, "gameplay");
                if (RenderDocCapture.TryInit(template))
                {
                    RenderDocCapture.Trigger();
                    for (int i = 0; i < 10; i++) yield return null;
                    yield return new WaitForSecondsRealtime(2f);
                    ProbePlugin.Log.LogInfo("RenderDoc capture: " + (RenderDocCapture.LatestCapturePath() ?? "none recorded"));
                }
                else ProbePlugin.Log.LogWarning("--cvr-renderdoc given but renderdoc.dll is not loaded (launch through renderdoccmd)");
            }
            if (ProbePlugin.Options.Quit)
            {
                ProbePlugin.Log.LogInfo("Quitting after gameplay report");
                Application.Quit();
            }
        }
    }

    [HarmonyPatch(typeof(WorldGeneration), "Awake")]
    internal static class SeedPatch
    {
        private static void Prefix()
        {
            if (ProbePlugin.Options.Seed is int seed)
            {
                UnityEngine.Random.InitState(seed);
                ProbePlugin.Log.LogInfo($"Seeded UnityEngine.Random with {seed} before WorldGeneration.Awake");
            }
        }
    }

    internal sealed class ProbeOptions
    {
        public bool AutoStart;
        public int? Seed;
        public float ProbeDelay = 5f;
        public bool Screenshot;
        public bool Quit;
        public bool RenderDoc;
        public bool Spike;
        public bool LiquidSpike;
        public bool Pool;
        public string Command;
        public float CommandWait = 12f;
        public float EyeSeparation = 1f;

        public static ProbeOptions Parse(string[] args)
        {
            var o = new ProbeOptions();
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--cvr-autostart": o.AutoStart = true; break;
                    case "--cvr-screenshot": o.Screenshot = true; break;
                    case "--cvr-quit": o.Quit = true; break;
                    case "--cvr-renderdoc": o.RenderDoc = true; break;
                    case "--cvr-spike": o.Spike = true; break;
                    case "--cvr-spike-liquids": o.LiquidSpike = true; break;
                    case "--cvr-pool": o.Pool = true; break;
                    case "--cvr-command" when i + 1 < args.Length: o.Command = args[++i]; break;
                    case "--cvr-command-wait" when i + 1 < args.Length:
                        o.CommandWait = float.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture); break;
                    case "--cvr-eye-sep" when i + 1 < args.Length:
                        o.EyeSeparation = float.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture); break;
                    case "--cvr-seed" when i + 1 < args.Length: o.Seed = int.Parse(args[++i]); break;
                    case "--cvr-probe-delay" when i + 1 < args.Length:
                        o.ProbeDelay = float.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture); break;
                }
            }
            return o;
        }

        public override string ToString() =>
            $"autostart={AutoStart} seed={(Seed.HasValue ? Seed.ToString() : "none")} delay={ProbeDelay} screenshot={Screenshot} renderdoc={RenderDoc} spike={Spike} eyeSep={EyeSeparation} quit={Quit}";
    }

    internal sealed class FrameStats
    {
        public int Frames;
        public float Seconds;
        public float WorstMs;

        public IEnumerator Measure(int frames)
        {
            float start = Time.realtimeSinceStartup, last = start;
            for (int i = 0; i < frames; i++)
            {
                yield return null;
                float now = Time.realtimeSinceStartup;
                WorstMs = Mathf.Max(WorstMs, (now - last) * 1000f);
                last = now;
            }
            Frames = frames;
            Seconds = last - start;
        }

        public override string ToString() =>
            $"{Frames} frames in {Seconds:F2}s = {Frames / Mathf.Max(Seconds, 1e-4f):F1} fps avg, worst frame {WorstMs:F1} ms";
    }
}
