using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

namespace CasualtyVR.Probe
{
    /// <summary>
    /// Builds the plain-text probe report. Render-pipeline types (URP asset, renderer data,
    /// Light2D, Volume) are read by reflection so the probe survives package changes.
    /// </summary>
    internal static class ProbeReport
    {
        private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        public static string Build(string label, FrameStats frameStats)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# CasualtyVR probe report: {label}");
            sb.AppendLine($"utc={DateTime.UtcNow:O} scene={SceneManager.GetActiveScene().name} frame={Time.frameCount} t={Time.realtimeSinceStartup:F1}s");
            Section(sb, "System", () => SystemSection(sb, frameStats));
            Section(sb, "Render pipeline", () => PipelineSection(sb));
            Section(sb, "XR", () => XrSection(sb));
            Section(sb, "Layers", () => LayerSection(sb));
            Section(sb, "Cameras", () => CameraSection(sb));
            Section(sb, "Canvases", () => CanvasSection(sb));
            Section(sb, "Renderers", () => RendererSection(sb));
            Section(sb, "Lights (2D)", () => LightSection(sb));
            Section(sb, "Volumes", () => VolumeSection(sb));
            Section(sb, "World", () => WorldSection(sb));
            Section(sb, "Player", () => PlayerSection(sb));
            return sb.ToString();
        }

        private static void Section(StringBuilder sb, string title, Action body)
        {
            sb.AppendLine().AppendLine($"## {title}");
            try { body(); }
            catch (Exception ex) { sb.AppendLine($"!! section failed: {ex.GetType().Name}: {ex.Message}"); }
        }

        private static void SystemSection(StringBuilder sb, FrameStats frameStats)
        {
            sb.AppendLine($"unity={Application.unityVersion} game={Application.version} product={Application.companyName}/{Application.productName}");
            sb.AppendLine($"graphicsDeviceType={SystemInfo.graphicsDeviceType} version=\"{SystemInfo.graphicsDeviceVersion}\" gpu=\"{SystemInfo.graphicsDeviceName}\"");
            sb.AppendLine($"shaderLevel={SystemInfo.graphicsShaderLevel} multithreaded={SystemInfo.graphicsMultiThreaded} renderingThreadingMode={SystemInfo.renderingThreadingMode}");
            sb.AppendLine($"screen={Screen.width}x{Screen.height} fullScreenMode={Screen.fullScreenMode} dpi={Screen.dpi} refresh={Screen.currentResolution.refreshRateRatio}");
            sb.AppendLine($"vSyncCount={QualitySettings.vSyncCount} targetFrameRate={Application.targetFrameRate} quality={QualitySettings.names[QualitySettings.GetQualityLevel()]}");
            sb.AppendLine($"timeScale={Time.timeScale} fixedDeltaTime={Time.fixedDeltaTime} colorSpace={QualitySettings.activeColorSpace}");
            sb.AppendLine($"persistentDataPath(engine)={Application.persistentDataPath}");
            sb.AppendLine($"sandboxDataPath={CasualtyVR.Sandbox.SandboxPaths.DataPath}");
            if (frameStats != null) sb.AppendLine($"frameStats: {frameStats}");
        }

        private static void PipelineSection(StringBuilder sb)
        {
            var asset = GraphicsSettings.currentRenderPipeline;
            if (asset == null) { sb.AppendLine("Built-in render pipeline (no SRP asset)"); return; }
            sb.AppendLine($"asset={asset.name} type={asset.GetType().FullName}");
            if (GetMember(asset, "m_RendererDataList") is IEnumerable list)
            {
                int i = 0;
                foreach (var data in list)
                    sb.AppendLine($"  rendererData[{i++}]={(data as UnityEngine.Object)?.name} type={data?.GetType().FullName}");
            }
            sb.AppendLine($"  defaultRendererIndex={GetMember(asset, "m_DefaultRendererIndex")}");
            if (GetMember(asset, "m_RendererDataList") is IEnumerable datas)
                foreach (var data in datas)
                    if (GetMember(data, "rendererFeatures") is IEnumerable features)
                        foreach (var f in features)
                            sb.AppendLine($"  feature: {(f as UnityEngine.Object)?.name} type={f?.GetType().FullName} active={GetMember(f, "isActive")} material={(GetMember(GetMember(f, "settings") ?? f, "material") ?? GetMember(f, "passMaterial") as UnityEngine.Object)}");
            foreach (var name in new[] { "renderScale", "msaaSampleCount", "supportsHDR", "upscalingFilter", "supportsCameraDepthTexture", "supportsCameraOpaqueTexture" })
                sb.AppendLine($"  {name}={GetMember(asset, name)}");
        }

        private static void XrSection(StringBuilder sb)
        {
            sb.AppendLine($"XRSettings.enabled={UnityEngine.XR.XRSettings.enabled} loadedDevice=\"{UnityEngine.XR.XRSettings.loadedDeviceName}\"");
            sb.AppendLine($"supportedDevices=[{string.Join(", ", UnityEngine.XR.XRSettings.supportedDevices)}]");
            var displays = new List<UnityEngine.XR.XRDisplaySubsystem>();
            SubsystemManager.GetInstances(displays);
            sb.AppendLine($"XRDisplaySubsystem instances={displays.Count}");
        }

        private static void LayerSection(StringBuilder sb)
        {
            sb.AppendLine("physics/render layers: " + string.Join(", ",
                Enumerable.Range(0, 32).Select(i => (i, n: LayerMask.LayerToName(i))).Where(x => x.n.Length > 0).Select(x => $"{x.i}:{x.n}")));
            sb.AppendLine("sorting layers (back->front): " + string.Join(", ",
                SortingLayer.layers.Select(l => $"{l.name}({l.value})")));
        }

        private static void CameraSection(StringBuilder sb)
        {
            var main = Camera.main;
            foreach (var cam in Resources.FindObjectsOfTypeAll<Camera>().Where(IsSceneObject))
            {
                sb.AppendLine($"- {Path(cam.transform)}{(cam == main ? "  [Camera.main]" : "")}");
                sb.AppendLine($"    enabled={cam.enabled && cam.gameObject.activeInHierarchy} depth={cam.depth} ortho={cam.orthographic} orthoSize={cam.orthographicSize:F3} fov={cam.fieldOfView:F1} aspect={cam.aspect:F3}");
                sb.AppendLine($"    pos={cam.transform.position} rot={cam.transform.eulerAngles} near={cam.nearClipPlane} far={cam.farClipPlane}");
                sb.AppendLine($"    clear={cam.clearFlags} bg={cam.backgroundColor} target={(cam.targetTexture ? $"{cam.targetTexture.name} {cam.targetTexture.width}x{cam.targetTexture.height}" : "screen")} stereoTarget={cam.stereoTargetEye}");
                sb.AppendLine($"    cullingMask={MaskNames(cam.cullingMask)}");
                var extra = cam.GetComponents<Component>().Where(c => c != null && !(c is Transform) && !(c is Camera)).Select(c => c.GetType().Name);
                sb.AppendLine($"    components=[{string.Join(", ", extra)}]");
                var urp = cam.GetComponents<Component>().FirstOrDefault(c => c != null && c.GetType().Name == "UniversalAdditionalCameraData");
                if (urp != null)
                {
                    sb.AppendLine($"    urp: renderType={GetMember(urp, "renderType")} rendererIndex={GetMember(urp, "m_RendererIndex")} postProcessing={GetMember(urp, "renderPostProcessing")} volumeMask={GetMember(urp, "volumeLayerMask")}");
                    if (GetMember(urp, "cameraStack") is IEnumerable stack)
                        sb.AppendLine($"    urp stack=[{string.Join(", ", stack.Cast<Camera>().Select(c => c ? c.name : "null"))}]");
                }
            }
        }

        private static void CanvasSection(StringBuilder sb)
        {
            foreach (var canvas in Resources.FindObjectsOfTypeAll<Canvas>().Where(IsSceneObject).Where(c => c.isRootCanvas))
            {
                var scaler = canvas.GetComponent<UnityEngine.UI.CanvasScaler>();
                sb.AppendLine($"- {Path(canvas.transform)} active={canvas.gameObject.activeInHierarchy} mode={canvas.renderMode} camera={(canvas.worldCamera ? canvas.worldCamera.name : "-")} planeDist={canvas.planeDistance}");
                sb.AppendLine($"    sortingLayer={canvas.sortingLayerName} order={canvas.sortingOrder} scale={canvas.transform.localScale} pixelRect={canvas.pixelRect}");
                if (scaler) sb.AppendLine($"    scaler={scaler.uiScaleMode} ref={scaler.referenceResolution} match={scaler.matchWidthOrHeight} scaleFactor={scaler.scaleFactor}");
            }
        }

        private static void RendererSection(StringBuilder sb)
        {
            var renderers = UnityEngine.Object.FindObjectsOfType<Renderer>();
            sb.AppendLine($"active renderers={renderers.Length}");
            foreach (var g in renderers.GroupBy(r => r.GetType().Name).OrderByDescending(g => g.Count()))
                sb.AppendLine($"  {g.Key}: {g.Count()}");
            sb.AppendLine("by sorting layer (count, order range, z range):");
            foreach (var g in renderers.GroupBy(r => r.sortingLayerName).OrderBy(g => SortingLayer.GetLayerValueFromName(g.Key)))
                sb.AppendLine($"  {g.Key}: {g.Count()}  order {g.Min(r => r.sortingOrder)}..{g.Max(r => r.sortingOrder)}  z {g.Min(r => r.transform.position.z):F2}..{g.Max(r => r.transform.position.z):F2}  types=[{string.Join(",", g.Select(r => r.GetType().Name).Distinct())}]");
            sb.AppendLine("draw-order keys (layer/order: count, types, sample names):");
            foreach (var g in renderers.GroupBy(r => (layer: SortingLayer.GetLayerValueFromName(r.sortingLayerName), name: r.sortingLayerName, order: r.sortingOrder))
                                       .OrderBy(g => g.Key.layer).ThenBy(g => g.Key.order))
                sb.AppendLine($"  {g.Key.name}/{g.Key.order}: {g.Count()}  [{string.Join(",", g.Select(r => r.GetType().Name).Distinct())}]  e.g. {string.Join(" | ", g.Select(r => Path(r.transform)).Distinct().Take(3))}");
            sb.AppendLine("shaders (renderer count):");
            foreach (var g in renderers.SelectMany(r => r.sharedMaterials).Where(m => m && m.shader).GroupBy(m => m.shader.name).OrderByDescending(g => g.Count()).Take(30))
                sb.AppendLine($"  {g.Key}: {g.Count()}");
        }

        private static void LightSection(StringBuilder sb)
        {
            var lightType = FindType("UnityEngine.Rendering.Universal.Light2D");
            if (lightType == null) { sb.AppendLine("Light2D type not found"); return; }
            var lights = UnityEngine.Object.FindObjectsOfType(lightType);
            sb.AppendLine($"active Light2D={lights.Length}");
            foreach (var g in lights.GroupBy(l => $"{GetMember(l, "lightType")} blendStyle={GetMember(l, "blendStyleIndex")} shadows={GetMember(l, "shadowsEnabled")} volume={GetMember(l, "volumeIntensityEnabled")}"))
                sb.AppendLine($"  {g.Count()} x {g.Key}");
            foreach (var l in lights.Cast<Component>().Where(l => Equals(GetMember(l, "lightType")?.ToString(), "Global")))
                sb.AppendLine($"  global: {Path(l.transform)} intensity={GetMember(l, "intensity")} color={GetMember(l, "color")} targetSortingLayers={SortingLayersOf(l)}");
        }

        private static void VolumeSection(StringBuilder sb)
        {
            var volumeType = FindType("UnityEngine.Rendering.Volume");
            if (volumeType == null) { sb.AppendLine("Volume type not found"); return; }
            foreach (Component v in UnityEngine.Object.FindObjectsOfType(volumeType))
            {
                var profile = GetMember(v, "sharedProfile") as ScriptableObject;
                sb.AppendLine($"- {Path(v.transform)} global={GetMember(v, "isGlobal")} weight={GetMember(v, "weight")} priority={GetMember(v, "priority")} profile={(profile ? profile.name : "-")}");
                if (profile != null && GetMember(profile, "components") is IEnumerable comps)
                    foreach (var c in comps)
                        sb.AppendLine($"    {c.GetType().Name} active={GetMember(c, "active")}");
            }
        }

        private static void WorldSection(StringBuilder sb)
        {
            var world = WorldGeneration.world;
            if (world == null) { sb.AppendLine("WorldGeneration.world is null"); return; }
            sb.AppendLine($"size={world.width}x{world.height} chunk={world.chunkWidth}x{world.chunkHeight} CHUNKSIZE={WorldGeneration.CHUNKSIZE} generating={world.generatingWorld} biomeDepth={GetMember(world, "biomeDepth")}");
            sb.AppendLine($"tiles={world.tiles?.Length} tileColors={world.tileColors?.Length} transform={world.transform.position} scale={world.transform.lossyScale}");
            if (GetMember(world, "worldBlocks") is ushort[,] blocks)
            {
                int w = blocks.GetLength(0), h = blocks.GetLength(1), solid = 0;
                ulong hash = 14695981039346656037UL;
                var counts = new Dictionary<ushort, int>();
                for (int x = 0; x < w; x++)
                    for (int y = 0; y < h; y++)
                    {
                        ushort b = blocks[x, y];
                        hash = (hash ^ b) * 1099511628211UL;
                        counts[b] = counts.TryGetValue(b, out int c) ? c + 1 : 1;
                        if (b != 0) solid++;
                    }
                sb.AppendLine($"worldBlocks={w}x{h} nonzero={solid} distinctIds={counts.Count} fnv1a64={hash:X16}");
                sb.AppendLine("  top ids: " + string.Join(", ", counts.OrderByDescending(kv => kv.Value).Take(12).Select(kv => $"{kv.Key}:{kv.Value}")));
            }
            if (GetMember(world, "renderChunks") is TilemapRenderer[,] chunks)
            {
                var flat = chunks.Cast<TilemapRenderer>().Where(c => c).ToList();
                var first = flat.FirstOrDefault();
                sb.AppendLine($"renderChunks={chunks.GetLength(0)}x{chunks.GetLength(1)} enabled={flat.Count(c => c.enabled)}");
                if (first)
                {
                    var map = first.GetComponent<Tilemap>();
                    sb.AppendLine($"  sample chunk {Path(first.transform)} mode={first.mode} sortingLayer={first.sortingLayerName} order={first.sortingOrder} material={first.sharedMaterial?.name}/{first.sharedMaterial?.shader?.name}");
                    sb.AppendLine($"  grid cellSize={map.layoutGrid.cellSize} tileAnchor={map.tileAnchor} chunkPos={first.transform.position}");
                }
            }
        }

        private static void PlayerSection(StringBuilder sb)
        {
            var pc = PlayerCamera.main;
            if (pc == null) { sb.AppendLine("PlayerCamera.main is null"); return; }
            sb.AppendLine($"PlayerCamera at {Path(pc.transform)} pos={pc.transform.position} uiScale={GetStatic(typeof(PlayerCamera), "uiScale")}");
            if (pc.body != null)
            {
                var body = pc.body;
                var limbs = body.GetComponentsInChildren<Limb>(true);
                var sprites = body.GetComponentsInChildren<SpriteRenderer>(true);
                sb.AppendLine($"body={Path(body.transform)} pos={body.transform.position} limbs={limbs.Length} spriteRenderers={sprites.Length}");
                foreach (var g in sprites.GroupBy(s => s.sortingLayerName))
                    sb.AppendLine($"  sprites layer {g.Key}: {g.Count()} order {g.Min(s => s.sortingOrder)}..{g.Max(s => s.sortingOrder)}");
                var sample = sprites.FirstOrDefault(s => s.sprite);
                if (sample) sb.AppendLine($"  sample sprite {sample.sprite.name} ppu={sample.sprite.pixelsPerUnit} rect={sample.sprite.rect} tex={sample.sprite.texture.width}x{sample.sprite.texture.height} filter={sample.sprite.texture.filterMode} shader={sample.sharedMaterial?.shader?.name}");
            }
        }

        // ---- helpers ----

        private static bool IsSceneObject(Component c) => c != null && c.gameObject.scene.IsValid();

        private static string Path(Transform t)
        {
            var parts = new List<string>();
            for (; t != null; t = t.parent) parts.Add(t.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        private static string MaskNames(int mask)
        {
            if (mask == -1) return "Everything";
            var names = Enumerable.Range(0, 32).Where(i => (mask & (1 << i)) != 0).Select(i => LayerMask.LayerToName(i)).Select(n => n.Length > 0 ? n : "?");
            return string.Join("|", names);
        }

        private static string SortingLayersOf(object light) =>
            GetMember(light, "m_ApplyToSortingLayers") is int[] ids ? string.Join(",", ids.Select(SortingLayer.IDToName)) : "?";

        private static Type FindType(string fullName) =>
            AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(fullName, false)).FirstOrDefault(t => t != null);

        internal static object GetMember(object obj, string name)
        {
            for (var type = obj?.GetType(); type != null; type = type.BaseType)
            {
                var prop = type.GetProperty(name, AnyInstance);
                if (prop != null && prop.GetIndexParameters().Length == 0) return prop.GetValue(obj);
                var field = type.GetField(name, AnyInstance);
                if (field != null) return field.GetValue(obj);
            }
            return null;
        }

        private static object GetStatic(Type type, string name) =>
            (object)type.GetProperty(name, AnyStatic)?.GetValue(null) ?? type.GetField(name, AnyStatic)?.GetValue(null);
    }
}
