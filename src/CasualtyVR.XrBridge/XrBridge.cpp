// CasualtyVR native OpenXR bridge. See XrBridge.h for the contract.
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <d3d11.h>
#include <dxgi.h>
#include <mutex>
#include <cmath>
#include <string>
#include <vector>
#include <cstdio>
#include <cstring>
#include <cstdarg>
#include <share.h>

#define XR_USE_PLATFORM_WIN32
#define XR_USE_GRAPHICS_API_D3D11
#include <openxr/openxr.h>
#include <openxr/openxr_platform.h>

#include "XrBridge.h"

namespace
{
    std::mutex g_logMutex;
    std::string g_log;
    // Optional copy of the log on disk, flushed per line, so a crash or a runtime hang keeps the lines that explain
    // it (FAIL-TEST-039: the C# side only drains the buffer every 2 s). Routine per-second lines stay out of it.
    FILE* g_logFile = nullptr;
    size_t g_logFileBytes = 0;
    const size_t LogFileLimit = 1024 * 1024;

    void Log(const char* fmt, ...)
    {
        char line[1024];
        va_list args;
        va_start(args, fmt);
        vsnprintf(line, sizeof(line), fmt, args);
        va_end(args);
        std::lock_guard<std::mutex> lock(g_logMutex);
        g_log += line;
        g_log += '\n';
        if (g_log.size() > 64 * 1024) g_log.erase(0, g_log.size() - 32 * 1024);
        bool routine = strstr(line, "timing/frame") != nullptr || strstr(line, "input: hand") != nullptr;
        if (g_logFile && !routine && g_logFileBytes < LogFileLimit)
        {
            SYSTEMTIME t;
            GetLocalTime(&t);
            int n = fprintf(g_logFile, "%02d:%02d:%02d.%03d %s\n", t.wHour, t.wMinute, t.wSecond, t.wMilliseconds, line);
            if (n > 0) g_logFileBytes += (size_t)n;
            if (g_logFileBytes >= LogFileLimit) fputs("(log file limit reached)\n", g_logFile);
            fflush(g_logFile);
        }
    }

    // Static names for instance-creation failures: xrResultToString needs an instance (XR-008).
    const char* ResultName(XrResult r)
    {
        switch (r)
        {
        case XR_SUCCESS: return "XR_SUCCESS";
        case XR_ERROR_RUNTIME_UNAVAILABLE: return "XR_ERROR_RUNTIME_UNAVAILABLE";
        case XR_ERROR_RUNTIME_FAILURE: return "XR_ERROR_RUNTIME_FAILURE";
        case XR_ERROR_API_VERSION_UNSUPPORTED: return "XR_ERROR_API_VERSION_UNSUPPORTED";
        case XR_ERROR_EXTENSION_NOT_PRESENT: return "XR_ERROR_EXTENSION_NOT_PRESENT";
        case XR_ERROR_FORM_FACTOR_UNAVAILABLE: return "XR_ERROR_FORM_FACTOR_UNAVAILABLE";
        case XR_ERROR_GRAPHICS_DEVICE_INVALID: return "XR_ERROR_GRAPHICS_DEVICE_INVALID";
        case XR_ERROR_GRAPHICS_REQUIREMENTS_CALL_MISSING: return "XR_ERROR_GRAPHICS_REQUIREMENTS_CALL_MISSING";
        case XR_ERROR_SESSION_NOT_RUNNING: return "XR_ERROR_SESSION_NOT_RUNNING";
        case XR_ERROR_SESSION_LOST: return "XR_ERROR_SESSION_LOST";
        case XR_ERROR_CALL_ORDER_INVALID: return "XR_ERROR_CALL_ORDER_INVALID";
        case XR_ERROR_VALIDATION_FAILURE: return "XR_ERROR_VALIDATION_FAILURE";
        case XR_ERROR_INSTANCE_LOST: return "XR_ERROR_INSTANCE_LOST";
        case XR_ERROR_INITIALIZATION_FAILED: return "XR_ERROR_INITIALIZATION_FAILED";
        case XR_SESSION_LOSS_PENDING: return "XR_SESSION_LOSS_PENDING";
        case XR_FRAME_DISCARDED: return "XR_FRAME_DISCARDED";
        default: return "XrResult(?)";
        }
    }

    struct EyeSwapchain
    {
        XrSwapchain handle = XR_NULL_HANDLE;
        std::vector<XrSwapchainImageD3D11KHR> images;
    };

    struct State
    {
        XrInstance instance = XR_NULL_HANDLE;
        XrSystemId systemId = XR_NULL_SYSTEM_ID;
        XrSession session = XR_NULL_HANDLE;
        XrSpace localSpace = XR_NULL_HANDLE;
        // Input (INPUT-001).
        XrActionSet actionSet = XR_NULL_HANDLE;
        // One action per hand and control (no subaction paths): valid everywhere, and xr-sim keeps a
        // single binding per action, so shared left/right actions read the wrong hand there.
        XrAction aAim[2] = {}, aTrigger[2] = {}, aSqueeze[2] = {}, aStick[2] = {}, aStickClick[2] = {}, aHaptic[2] = {};
        XrAction aA = XR_NULL_HANDLE, aB = XR_NULL_HANDLE, aX = XR_NULL_HANDLE, aY = XR_NULL_HANDLE, aMenu = XR_NULL_HANDLE;
        XrPath handPath[2] = {XR_NULL_PATH, XR_NULL_PATH};
        XrSpace aimSpace[2] = {XR_NULL_HANDLE, XR_NULL_HANDLE};
        CvrInput input{};
        XrSessionState sessionState = XR_SESSION_STATE_UNKNOWN;
        bool sessionRunning = false;
        EyeSwapchain eyes[2];
        int32_t width = 0, height = 0;
        int64_t format = 0;
        ID3D11Device* device = nullptr;
        ID3D11DeviceContext* context = nullptr;
        ID3D11Texture2D* eyeTextures[2] = {nullptr, nullptr};
        CvrInfo info{};
        bool hpControllerExt = false;   // XR_EXT_hp_mixed_reality_controller enabled (Reverb G2 bindings)
        // The runtime ended the session (LOSS_PENDING, EXITING, instance loss, or a frame call reporting it lost).
        // Nothing waits or submits after this; the C# side stops VR (FAIL-XR-029).
        bool sessionLost = false;
        bool referenceSpaceChanged = false;   // LOCAL recentred by the runtime; consumed by cvr_xr_get_info
        uint64_t waitsSkipped = 0;
        // Last tracked head (centre of the eyes) and each eye's offset in its own frame: when position tracking
        // drops, the views keep turning with the IMU around the last tracked position instead of going blank
        // (FAIL-XR-013; the poses are absolute every frame, so nothing persists once tracking returns).
        bool hasHead = false;
        XrVector3f lastHead{};
        XrVector3f eyeLocal[2]{};
        // Last tracked aim pose per hand, held for a short dropout (FAIL-HAND-003).
        bool hasHand[2] = {false, false};
        XrPosef lastHand[2]{};
        double lastHandMs[2] = {0, 0};
        bool adapterMismatch = false;   // the runtime's adapter LUID differs from the game device's

        // Frame handed from wait (main thread) to submit (render path).
        std::mutex frameMutex;
        bool framePending = false;
        XrFrameState frameState{XR_TYPE_FRAME_STATE};
        XrView views[2]{{XR_TYPE_VIEW}, {XR_TYPE_VIEW}};
        bool viewsValid = false;
        uint64_t frameIndex = 0;
    };

    State* g = nullptr;

    // Coarse timing so frame-rate problems can be attributed to the runtime or to us.
    struct Timing { double waitMs = 0, locateMs = 0, beginMs = 0, swapWaitMs = 0, copyMs = 0, endMs = 0; int frames = 0; };
    Timing g_timing;
    double NowMs()
    {
        static LARGE_INTEGER freq = [] { LARGE_INTEGER f; QueryPerformanceFrequency(&f); return f; }();
        LARGE_INTEGER t; QueryPerformanceCounter(&t);
        return (double)t.QuadPart * 1000.0 / (double)freq.QuadPart;
    }

    // The same failure every frame (a lost session fails each xrWaitFrame) is logged once, then every 100th time.
    std::mutex g_repeatMutex;
    const char* g_repeatWhat = nullptr;
    XrResult g_repeatResult = XR_SUCCESS;
    uint64_t g_repeatCount = 0;

    bool Check(XrResult r, const char* what)
    {
        if (XR_SUCCEEDED(r)) return true;
        uint64_t repeat;
        {
            std::lock_guard<std::mutex> lock(g_repeatMutex);
            if (what == g_repeatWhat && r == g_repeatResult) repeat = ++g_repeatCount;
            else { g_repeatWhat = what; g_repeatResult = r; g_repeatCount = repeat = 1; }
        }
        if (repeat > 1 && repeat % 100 != 0) return false;
        char name[XR_MAX_RESULT_STRING_SIZE] = "";
        if (g && g->instance) xrResultToString(g->instance, r, name);
        if (repeat > 1) Log("%s failed: %d %s (repeated %llu times)", what, (int)r, name[0] ? name : ResultName(r), (unsigned long long)repeat);
        else Log("%s failed: %d %s", what, (int)r, name[0] ? name : ResultName(r));
        return false;
    }

    // Quaternion helpers for held poses (OpenXR axes; unit quaternions).
    XrVector3f Rotate(const XrQuaternionf& q, const XrVector3f& v)
    {
        // v' = v + 2 * cross(q.xyz, cross(q.xyz, v) + w * v)
        XrVector3f u{q.x, q.y, q.z};
        XrVector3f c1{u.y * v.z - u.z * v.y + q.w * v.x, u.z * v.x - u.x * v.z + q.w * v.y, u.x * v.y - u.y * v.x + q.w * v.z};
        XrVector3f c2{u.y * c1.z - u.z * c1.y, u.z * c1.x - u.x * c1.z, u.x * c1.y - u.y * c1.x};
        return {v.x + 2.f * c2.x, v.y + 2.f * c2.y, v.z + 2.f * c2.z};
    }
    XrQuaternionf Conjugate(const XrQuaternionf& q) { return {-q.x, -q.y, -q.z, q.w}; }
    XrVector3f Add(const XrVector3f& a, const XrVector3f& b) { return {a.x + b.x, a.y + b.y, a.z + b.z}; }
    XrVector3f Sub(const XrVector3f& a, const XrVector3f& b) { return {a.x - b.x, a.y - b.y, a.z - b.z}; }

    CvrView ToCvr(const XrView& v)
    {
        CvrView c;
        c.orientation = {v.pose.orientation.x, v.pose.orientation.y, v.pose.orientation.z, v.pose.orientation.w};
        c.position = {v.pose.position.x, v.pose.position.y, v.pose.position.z};
        c.fov = {v.fov.angleLeft, v.fov.angleRight, v.fov.angleUp, v.fov.angleDown};
        return c;
    }

    bool HasExtension(const std::vector<XrExtensionProperties>& available, const char* name)
    {
        for (auto& e : available) if (strcmp(e.extensionName, name) == 0) return true;
        return false;
    }

    bool CreateInstance()
    {
        // D3D11 is required; controller-profile extensions are enabled when the runtime has them (more headsets).
        uint32_t extCount = 0;
        xrEnumerateInstanceExtensionProperties(nullptr, 0, &extCount, nullptr);
        std::vector<XrExtensionProperties> available(extCount, {XR_TYPE_EXTENSION_PROPERTIES});
        if (extCount) xrEnumerateInstanceExtensionProperties(nullptr, extCount, &extCount, available.data());
        std::vector<const char*> extensions = {XR_KHR_D3D11_ENABLE_EXTENSION_NAME};
        if (HasExtension(available, XR_EXT_HP_MIXED_REALITY_CONTROLLER_EXTENSION_NAME))
        {
            extensions.push_back(XR_EXT_HP_MIXED_REALITY_CONTROLLER_EXTENSION_NAME);
            g->hpControllerExt = true;
        }
        Log("Runtime extensions: %u available; enabling %zu", extCount, extensions.size());
        XrInstanceCreateInfo ci{XR_TYPE_INSTANCE_CREATE_INFO};
        strcpy_s(ci.applicationInfo.applicationName, "CasualtyVR");
        ci.applicationInfo.applicationVersion = 1;
        strcpy_s(ci.applicationInfo.engineName, "Unity2022-CasualtyVR");
        ci.enabledExtensionCount = (uint32_t)extensions.size();
        ci.enabledExtensionNames = extensions.data();

        // XR-008: negotiate newest-first, never assert one version.
        const XrVersion versions[] = {XR_MAKE_VERSION(1, 1, 0), XR_MAKE_VERSION(1, 0, 0)};
        for (XrVersion v : versions)
        {
            ci.applicationInfo.apiVersion = v;
            XrResult r = xrCreateInstance(&ci, &g->instance);
            Log("xrCreateInstance(api %u.%u) -> %d %s", XR_VERSION_MAJOR(v), XR_VERSION_MINOR(v), (int)r, ResultName(r));
            if (XR_SUCCEEDED(r))
            {
                g->info.apiMajor = XR_VERSION_MAJOR(v);
                g->info.apiMinor = XR_VERSION_MINOR(v);
                return true;
            }
            if (r != XR_ERROR_API_VERSION_UNSUPPORTED) return false;
        }
        return false;
    }

    bool CheckAdapter(PFN_xrGetD3D11GraphicsRequirementsKHR getReqs)
    {
        XrGraphicsRequirementsD3D11KHR reqs{XR_TYPE_GRAPHICS_REQUIREMENTS_D3D11_KHR};
        if (!Check(getReqs(g->instance, g->systemId, &reqs), "xrGetD3D11GraphicsRequirementsKHR")) return false;

        // XR-006: the game's device must be on the adapter the runtime asks for.
        IDXGIDevice* dxgiDevice = nullptr;
        IDXGIAdapter* adapter = nullptr;
        DXGI_ADAPTER_DESC desc{};
        bool ok = SUCCEEDED(g->device->QueryInterface(__uuidof(IDXGIDevice), (void**)&dxgiDevice)) &&
                  SUCCEEDED(dxgiDevice->GetAdapter(&adapter)) && SUCCEEDED(adapter->GetDesc(&desc));
        if (adapter) adapter->Release();
        if (dxgiDevice) dxgiDevice->Release();
        if (!ok) { Log("Could not query the Unity device's adapter"); return false; }

        bool same = desc.AdapterLuid.LowPart == reqs.adapterLuid.LowPart && desc.AdapterLuid.HighPart == reqs.adapterLuid.HighPart;
        char gpu[128];
        WideCharToMultiByte(CP_UTF8, 0, desc.Description, -1, gpu, sizeof(gpu), nullptr, nullptr);
        Log("Adapter check: Unity device on '%s' LUID %08lx:%08lx, runtime wants %08lx:%08lx -> %s; min feature level 0x%x",
            gpu, desc.AdapterLuid.HighPart, desc.AdapterLuid.LowPart, reqs.adapterLuid.HighPart, reqs.adapterLuid.LowPart,
            same ? "MATCH" : "MISMATCH", (unsigned)reqs.minFeatureLevel);
        // A mismatch is a warning, not a failure: translation layers (Proton's wineopenxr over DXVK) can report
        // LUIDs that differ for the same GPU. xrCreateSession is the real test, and its failure message names
        // the likely cause (a laptop running the game on the integrated GPU).
        if (!same) Log("WARNING: the game's GPU is not the one the VR runtime asked for; trying anyway");
        g->adapterMismatch = !same;
        return true;
    }

    bool CreateSwapchains()
    {
        uint32_t viewCount = 0;
        if (!Check(xrEnumerateViewConfigurationViews(g->instance, g->systemId, XR_VIEW_CONFIGURATION_TYPE_PRIMARY_STEREO, 0, &viewCount, nullptr), "xrEnumerateViewConfigurationViews")) return false;
        std::vector<XrViewConfigurationView> configViews(viewCount, {XR_TYPE_VIEW_CONFIGURATION_VIEW});
        if (!Check(xrEnumerateViewConfigurationViews(g->instance, g->systemId, XR_VIEW_CONFIGURATION_TYPE_PRIMARY_STEREO, viewCount, &viewCount, configViews.data()), "xrEnumerateViewConfigurationViews")) return false;
        if (viewCount != 2) { Log("Expected 2 stereo views, runtime reports %u", viewCount); return false; }
        g->width = (int32_t)configViews[0].recommendedImageRectWidth;
        g->height = (int32_t)configViews[0].recommendedImageRectHeight;

        uint32_t formatCount = 0;
        xrEnumerateSwapchainFormats(g->session, 0, &formatCount, nullptr);
        std::vector<int64_t> formats(formatCount);
        xrEnumerateSwapchainFormats(g->session, formatCount, &formatCount, formats.data());
        // CopyResource needs the eye RT and the swapchain in the same typeless family: RGBA8 preferred, BGRA8 when
        // a runtime offers only that (the C# side then creates BGRA eye RTs, from CvrInfo.swapchainFormat).
        const int64_t wanted[] = {DXGI_FORMAT_R8G8B8A8_UNORM_SRGB, DXGI_FORMAT_B8G8R8A8_UNORM_SRGB, DXGI_FORMAT_R8G8B8A8_UNORM, DXGI_FORMAT_B8G8R8A8_UNORM};
        for (int64_t w : wanted)
            for (int64_t f : formats)
                if (f == w && g->format == 0) g->format = f;
        if (g->format == 0) { Log("Runtime offers no 8-bit RGBA or BGRA swapchain format (%u formats)", formatCount); return false; }

        for (int eye = 0; eye < 2; eye++)
        {
            XrSwapchainCreateInfo sci{XR_TYPE_SWAPCHAIN_CREATE_INFO};
            sci.usageFlags = XR_SWAPCHAIN_USAGE_COLOR_ATTACHMENT_BIT | XR_SWAPCHAIN_USAGE_TRANSFER_DST_BIT;
            sci.format = g->format;
            sci.sampleCount = 1;
            sci.width = (uint32_t)g->width;
            sci.height = (uint32_t)g->height;
            sci.faceCount = 1;
            sci.arraySize = 1;
            sci.mipCount = 1;
            if (!Check(xrCreateSwapchain(g->session, &sci, &g->eyes[eye].handle), "xrCreateSwapchain")) return false;
            uint32_t imageCount = 0;
            xrEnumerateSwapchainImages(g->eyes[eye].handle, 0, &imageCount, nullptr);
            g->eyes[eye].images.assign(imageCount, {XR_TYPE_SWAPCHAIN_IMAGE_D3D11_KHR});
            if (!Check(xrEnumerateSwapchainImages(g->eyes[eye].handle, imageCount, &imageCount,
                    reinterpret_cast<XrSwapchainImageBaseHeader*>(g->eyes[eye].images.data())), "xrEnumerateSwapchainImages")) return false;
        }
        Log("Swapchains: 2 x %dx%d format %lld, %zu images each", g->width, g->height, (long long)g->format, g->eyes[0].images.size());
        return true;
    }

    // XR-001: lifecycle flags change only after the runtime accepted the call.
    void HandleStateChange(XrSessionState state)
    {
        g->sessionState = state;
        Log("Session state -> %d", (int)state);
        if (state != XR_SESSION_STATE_FOCUSED)
        {
            // XR-002: input is neutral outside FOCUSED, also when no frame is waited any more (STOPPING, IDLE),
            // which is when UpdateInput stops refreshing it.
            std::lock_guard<std::mutex> lock(g->frameMutex);
            g->input = CvrInput{};
        }
        if (state == XR_SESSION_STATE_LOSS_PENDING || state == XR_SESSION_STATE_EXITING)
        {
            if (!g->sessionLost) Log("Session %s: the runtime ended the session", state == XR_SESSION_STATE_EXITING ? "EXITING" : "LOSS_PENDING");
            g->sessionLost = true;
        }
        if (state == XR_SESSION_STATE_READY && !g->sessionRunning)
        {
            XrSessionBeginInfo bi{XR_TYPE_SESSION_BEGIN_INFO};
            bi.primaryViewConfigurationType = XR_VIEW_CONFIGURATION_TYPE_PRIMARY_STEREO;
            if (Check(xrBeginSession(g->session, &bi), "xrBeginSession")) g->sessionRunning = true;
        }
        else if (state == XR_SESSION_STATE_STOPPING && g->sessionRunning)
        {
            if (Check(xrEndSession(g->session), "xrEndSession")) g->sessionRunning = false;
            std::lock_guard<std::mutex> lock(g->frameMutex);
            g->framePending = false;
        }
    }

    void SubmitFrame()
    {
        if (!g || !g->session) return;
        XrFrameState frameState;
        XrView views[2];
        bool viewsValid;
        {
            std::lock_guard<std::mutex> lock(g->frameMutex);
            if (!g->framePending) return;
            g->framePending = false;
            frameState = g->frameState;
            views[0] = g->views[0];
            views[1] = g->views[1];
            viewsValid = g->viewsValid;
        }

        XrFrameBeginInfo fbi{XR_TYPE_FRAME_BEGIN_INFO};
        double tb = NowMs();
        XrResult br = xrBeginFrame(g->session, &fbi);
        g_timing.beginMs += NowMs() - tb;
        if (!Check(br, "xrBeginFrame")) return;

        XrCompositionLayerProjectionView projViews[2]{{XR_TYPE_COMPOSITION_LAYER_PROJECTION_VIEW}, {XR_TYPE_COMPOSITION_LAYER_PROJECTION_VIEW}};
        bool canSubmit = frameState.shouldRender && viewsValid && g->eyeTextures[0] && g->eyeTextures[1];
        if (canSubmit)
        {
            for (int eye = 0; eye < 2; eye++)
            {
                uint32_t index = 0;
                XrSwapchainImageAcquireInfo ai{XR_TYPE_SWAPCHAIN_IMAGE_ACQUIRE_INFO};
                XrSwapchainImageWaitInfo wi{XR_TYPE_SWAPCHAIN_IMAGE_WAIT_INFO};
                wi.timeout = XR_INFINITE_DURATION;
                XrSwapchainImageReleaseInfo ri{XR_TYPE_SWAPCHAIN_IMAGE_RELEASE_INFO};
                double ts = NowMs();
                if (!Check(xrAcquireSwapchainImage(g->eyes[eye].handle, &ai, &index), "xrAcquireSwapchainImage") ||
                    !Check(xrWaitSwapchainImage(g->eyes[eye].handle, &wi), "xrWaitSwapchainImage"))
                {
                    canSubmit = false;
                    break;
                }
                double tc = NowMs();
                g_timing.swapWaitMs += tc - ts;
                g->context->CopyResource(g->eyes[eye].images[index].texture, g->eyeTextures[eye]);
                g_timing.copyMs += NowMs() - tc;
                Check(xrReleaseSwapchainImage(g->eyes[eye].handle, &ri), "xrReleaseSwapchainImage");

                // XR-005: submit exactly the pose and FOV the eye was rendered with.
                projViews[eye].pose = views[eye].pose;
                projViews[eye].fov = views[eye].fov;
                projViews[eye].subImage.swapchain = g->eyes[eye].handle;
                projViews[eye].subImage.imageRect = {{0, 0}, {g->width, g->height}};
                projViews[eye].subImage.imageArrayIndex = 0;
            }
        }

        XrCompositionLayerProjection layer{XR_TYPE_COMPOSITION_LAYER_PROJECTION};
        layer.space = g->localSpace;
        layer.viewCount = 2;
        layer.views = projViews;
        const XrCompositionLayerBaseHeader* layers[] = {reinterpret_cast<XrCompositionLayerBaseHeader*>(&layer)};

        XrFrameEndInfo fei{XR_TYPE_FRAME_END_INFO};
        fei.displayTime = frameState.predictedDisplayTime;
        fei.environmentBlendMode = XR_ENVIRONMENT_BLEND_MODE_OPAQUE;
        fei.layerCount = canSubmit ? 1 : 0;   // STR-010: an empty legal frame instead of a bad one
        fei.layers = canSubmit ? layers : nullptr;
        double te = NowMs();
        XrResult er = xrEndFrame(g->session, &fei);
        g_timing.endMs += NowMs() - te;
        if (++g_timing.frames == 90)
        {
            const double n = g_timing.frames;
            Log("timing/frame over %d frames: wait %.2f locate %.2f begin %.2f swapchainAcquire+Wait %.2f copy %.2f end %.2f ms (period %.2f ms)",
                g_timing.frames, g_timing.waitMs / n, g_timing.locateMs / n, g_timing.beginMs / n, g_timing.swapWaitMs / n,
                g_timing.copyMs / n, g_timing.endMs / n, frameState.predictedDisplayPeriod / 1e6);
            g_timing = Timing{};
        }
        g->info.lastSubmitResult = (int32_t)er;
        if (Check(er, "xrEndFrame") && canSubmit) g->info.framesSubmitted++;
        else g->info.framesDropped++;
    }

    XrPath Path(const char* s)
    {
        XrPath p = XR_NULL_PATH;
        xrStringToPath(g->instance, s, &p);
        return p;
    }

    XrAction MakeAction(const char* name, const char* localized, XrActionType type)
    {
        XrActionCreateInfo ci{XR_TYPE_ACTION_CREATE_INFO};
        strcpy_s(ci.actionName, name);
        strcpy_s(ci.localizedActionName, localized);
        ci.actionType = type;
        XrAction a = XR_NULL_HANDLE;
        Check(xrCreateAction(g->actionSet, &ci, &a), name);
        return a;
    }

    bool Suggest(const char* profile, const std::vector<std::pair<XrAction, const char*>>& binds)
    {
        std::vector<XrActionSuggestedBinding> list;
        for (auto& b : binds) list.push_back({b.first, Path(b.second)});
        XrInteractionProfileSuggestedBinding sb{XR_TYPE_INTERACTION_PROFILE_SUGGESTED_BINDING};
        sb.interactionProfile = Path(profile);
        sb.countSuggestedBindings = (uint32_t)list.size();
        sb.suggestedBindings = list.data();
        XrResult r = xrSuggestInteractionProfileBindings(g->instance, &sb);
        Log("input: suggest %s -> %d %s", profile, (int)r, ResultName(r));
        return XR_SUCCEEDED(r);
    }

    bool CreateActions()
    {
        XrActionSetCreateInfo asci{XR_TYPE_ACTION_SET_CREATE_INFO};
        strcpy_s(asci.actionSetName, "casualtyvr");
        strcpy_s(asci.localizedActionSetName, "CasualtyVR");
        if (!Check(xrCreateActionSet(g->instance, &asci, &g->actionSet), "xrCreateActionSet")) return false;
        g->handPath[0] = Path("/user/hand/left");
        g->handPath[1] = Path("/user/hand/right");

        const char* side[2] = {"left", "right"};
        for (int h = 0; h < 2; h++)
        {
            char n[64], l[64];
            sprintf_s(n, "aim_%s", side[h]); sprintf_s(l, "Aim %s", side[h]);
            g->aAim[h] = MakeAction(n, l, XR_ACTION_TYPE_POSE_INPUT);
            sprintf_s(n, "trigger_%s", side[h]); sprintf_s(l, "Trigger %s", side[h]);
            g->aTrigger[h] = MakeAction(n, l, XR_ACTION_TYPE_FLOAT_INPUT);
            sprintf_s(n, "squeeze_%s", side[h]); sprintf_s(l, "Grip %s", side[h]);
            g->aSqueeze[h] = MakeAction(n, l, XR_ACTION_TYPE_FLOAT_INPUT);
            sprintf_s(n, "thumbstick_%s", side[h]); sprintf_s(l, "Thumbstick %s", side[h]);
            g->aStick[h] = MakeAction(n, l, XR_ACTION_TYPE_VECTOR2F_INPUT);
            sprintf_s(n, "thumbstick_click_%s", side[h]); sprintf_s(l, "Thumbstick click %s", side[h]);
            g->aStickClick[h] = MakeAction(n, l, XR_ACTION_TYPE_BOOLEAN_INPUT);
            sprintf_s(n, "haptic_%s", side[h]); sprintf_s(l, "Vibration %s", side[h]);
            g->aHaptic[h] = MakeAction(n, l, XR_ACTION_TYPE_VIBRATION_OUTPUT);
        }
        g->aA = MakeAction("button_a", "A", XR_ACTION_TYPE_BOOLEAN_INPUT);
        g->aB = MakeAction("button_b", "B", XR_ACTION_TYPE_BOOLEAN_INPUT);
        g->aX = MakeAction("button_x", "X", XR_ACTION_TYPE_BOOLEAN_INPUT);
        g->aY = MakeAction("button_y", "Y", XR_ACTION_TYPE_BOOLEAN_INPUT);
        g->aMenu = MakeAction("menu", "Menu", XR_ACTION_TYPE_BOOLEAN_INPUT);

        bool any = false;
        // Fallback first, Touch last: xr-sim resolves an action to its last suggested binding. Every profile
        // below except HP's is in the core specification, so every runtime accepts its suggestions; the runtime
        // then uses the one matching the controllers. Buttons a controller lacks map to the nearest equivalent
        // (they can be re-assigned to other game actions in VR settings > Controls).
        any |= Suggest("/interaction_profiles/khr/simple_controller", {
            {g->aAim[0], "/user/hand/left/input/aim/pose"}, {g->aAim[1], "/user/hand/right/input/aim/pose"},
            {g->aTrigger[0], "/user/hand/left/input/select/click"}, {g->aTrigger[1], "/user/hand/right/input/select/click"},
            {g->aMenu, "/user/hand/left/input/menu/click"},
        });
        // HTC Vive wands: trackpads for sticks, no A/B/X/Y. Right pad click = A (jump), right menu = B, left menu =
        // the pause menu; the left pad click stays the left stick click.
        any |= Suggest("/interaction_profiles/htc/vive_controller", {
            {g->aAim[0], "/user/hand/left/input/aim/pose"}, {g->aAim[1], "/user/hand/right/input/aim/pose"},
            {g->aTrigger[0], "/user/hand/left/input/trigger/value"}, {g->aTrigger[1], "/user/hand/right/input/trigger/value"},
            {g->aSqueeze[0], "/user/hand/left/input/squeeze/click"}, {g->aSqueeze[1], "/user/hand/right/input/squeeze/click"},
            {g->aStick[0], "/user/hand/left/input/trackpad"}, {g->aStick[1], "/user/hand/right/input/trackpad"},
            {g->aStickClick[0], "/user/hand/left/input/trackpad/click"}, {g->aA, "/user/hand/right/input/trackpad/click"},
            {g->aB, "/user/hand/right/input/menu/click"}, {g->aMenu, "/user/hand/left/input/menu/click"},
            {g->aHaptic[0], "/user/hand/left/output/haptic"}, {g->aHaptic[1], "/user/hand/right/output/haptic"},
        });
        // Windows Mixed Reality motion controllers: sticks and trackpads. Right pad click = A, right menu = B, left
        // pad click = X, left menu = the pause menu.
        any |= Suggest("/interaction_profiles/microsoft/motion_controller", {
            {g->aAim[0], "/user/hand/left/input/aim/pose"}, {g->aAim[1], "/user/hand/right/input/aim/pose"},
            {g->aTrigger[0], "/user/hand/left/input/trigger/value"}, {g->aTrigger[1], "/user/hand/right/input/trigger/value"},
            {g->aSqueeze[0], "/user/hand/left/input/squeeze/click"}, {g->aSqueeze[1], "/user/hand/right/input/squeeze/click"},
            {g->aStick[0], "/user/hand/left/input/thumbstick"}, {g->aStick[1], "/user/hand/right/input/thumbstick"},
            {g->aStickClick[0], "/user/hand/left/input/thumbstick/click"}, {g->aStickClick[1], "/user/hand/right/input/thumbstick/click"},
            {g->aA, "/user/hand/right/input/trackpad/click"}, {g->aB, "/user/hand/right/input/menu/click"},
            {g->aX, "/user/hand/left/input/trackpad/click"}, {g->aMenu, "/user/hand/left/input/menu/click"},
            {g->aHaptic[0], "/user/hand/left/output/haptic"}, {g->aHaptic[1], "/user/hand/right/output/haptic"},
        });
        // Valve Index: A/B on both hands (left A/B = X/Y). No menu button (system is reserved): a firm squeeze of the
        // left trackpad is the pause menu.
        any |= Suggest("/interaction_profiles/valve/index_controller", {
            {g->aAim[0], "/user/hand/left/input/aim/pose"}, {g->aAim[1], "/user/hand/right/input/aim/pose"},
            {g->aTrigger[0], "/user/hand/left/input/trigger/value"}, {g->aTrigger[1], "/user/hand/right/input/trigger/value"},
            {g->aSqueeze[0], "/user/hand/left/input/squeeze/value"}, {g->aSqueeze[1], "/user/hand/right/input/squeeze/value"},
            {g->aStick[0], "/user/hand/left/input/thumbstick"}, {g->aStick[1], "/user/hand/right/input/thumbstick"},
            {g->aStickClick[0], "/user/hand/left/input/thumbstick/click"}, {g->aStickClick[1], "/user/hand/right/input/thumbstick/click"},
            {g->aA, "/user/hand/right/input/a/click"}, {g->aB, "/user/hand/right/input/b/click"},
            {g->aX, "/user/hand/left/input/a/click"}, {g->aY, "/user/hand/left/input/b/click"},
            {g->aMenu, "/user/hand/left/input/trackpad/force"},
            {g->aHaptic[0], "/user/hand/left/output/haptic"}, {g->aHaptic[1], "/user/hand/right/output/haptic"},
        });
        // HP Reverb G2 (extension): laid out like Touch.
        if (g->hpControllerExt)
            any |= Suggest("/interaction_profiles/hp/mixed_reality_controller", {
                {g->aAim[0], "/user/hand/left/input/aim/pose"}, {g->aAim[1], "/user/hand/right/input/aim/pose"},
                {g->aTrigger[0], "/user/hand/left/input/trigger/value"}, {g->aTrigger[1], "/user/hand/right/input/trigger/value"},
                {g->aSqueeze[0], "/user/hand/left/input/squeeze/value"}, {g->aSqueeze[1], "/user/hand/right/input/squeeze/value"},
                {g->aStick[0], "/user/hand/left/input/thumbstick"}, {g->aStick[1], "/user/hand/right/input/thumbstick"},
                {g->aStickClick[0], "/user/hand/left/input/thumbstick/click"}, {g->aStickClick[1], "/user/hand/right/input/thumbstick/click"},
                {g->aA, "/user/hand/right/input/a/click"}, {g->aB, "/user/hand/right/input/b/click"},
                {g->aX, "/user/hand/left/input/x/click"}, {g->aY, "/user/hand/left/input/y/click"},
                {g->aMenu, "/user/hand/left/input/menu/click"},
                {g->aHaptic[0], "/user/hand/left/output/haptic"}, {g->aHaptic[1], "/user/hand/right/output/haptic"},
            });
        any |= Suggest("/interaction_profiles/oculus/touch_controller", {
            {g->aAim[0], "/user/hand/left/input/aim/pose"}, {g->aAim[1], "/user/hand/right/input/aim/pose"},
            {g->aTrigger[0], "/user/hand/left/input/trigger/value"}, {g->aTrigger[1], "/user/hand/right/input/trigger/value"},
            {g->aSqueeze[0], "/user/hand/left/input/squeeze/value"}, {g->aSqueeze[1], "/user/hand/right/input/squeeze/value"},
            {g->aStick[0], "/user/hand/left/input/thumbstick"}, {g->aStick[1], "/user/hand/right/input/thumbstick"},
            {g->aStickClick[0], "/user/hand/left/input/thumbstick/click"}, {g->aStickClick[1], "/user/hand/right/input/thumbstick/click"},
            {g->aA, "/user/hand/right/input/a/click"}, {g->aB, "/user/hand/right/input/b/click"},
            {g->aX, "/user/hand/left/input/x/click"}, {g->aY, "/user/hand/left/input/y/click"},
            {g->aMenu, "/user/hand/left/input/menu/click"},
            {g->aHaptic[0], "/user/hand/left/output/haptic"}, {g->aHaptic[1], "/user/hand/right/output/haptic"},
        });
        if (!any) return false;

        for (int h = 0; h < 2; h++)
        {
            XrActionSpaceCreateInfo sci{XR_TYPE_ACTION_SPACE_CREATE_INFO};
            sci.action = g->aAim[h];
            sci.subactionPath = XR_NULL_PATH;
            sci.poseInActionSpace.orientation.w = 1.0f;
            Check(xrCreateActionSpace(g->session, &sci, &g->aimSpace[h]), "xrCreateActionSpace");
        }
        XrSessionActionSetsAttachInfo ai{XR_TYPE_SESSION_ACTION_SETS_ATTACH_INFO};
        ai.countActionSets = 1;
        ai.actionSets = &g->actionSet;
        return Check(xrAttachSessionActionSets(g->session, &ai), "xrAttachSessionActionSets");
    }

    float GetFloat(XrAction a, XrPath sub, bool* active)
    {
        XrActionStateGetInfo gi{XR_TYPE_ACTION_STATE_GET_INFO};
        gi.action = a; gi.subactionPath = sub;
        XrActionStateFloat st{XR_TYPE_ACTION_STATE_FLOAT};
        if (a && XR_SUCCEEDED(xrGetActionStateFloat(g->session, &gi, &st)) && st.isActive) { if (active) *active = true; return st.currentState; }
        return 0.0f;
    }

    bool GetBool(XrAction a, XrPath sub)
    {
        XrActionStateGetInfo gi{XR_TYPE_ACTION_STATE_GET_INFO};
        gi.action = a; gi.subactionPath = sub;
        XrActionStateBoolean st{XR_TYPE_ACTION_STATE_BOOLEAN};
        return a && XR_SUCCEEDED(xrGetActionStateBoolean(g->session, &gi, &st)) && st.isActive && st.currentState;
    }

    void UpdateInput(XrTime displayTime)
    {
        CvrInput in{};
        in.focused = g->sessionState == XR_SESSION_STATE_FOCUSED ? 1 : 0;
        if (g->actionSet && in.focused)
        {
            XrActiveActionSet active{g->actionSet, XR_NULL_PATH};
            XrActionsSyncInfo si{XR_TYPE_ACTIONS_SYNC_INFO};
            si.countActiveActionSets = 1;
            si.activeActionSets = &active;
            XrResult sr = xrSyncActions(g->session, &si);
            static XrResult lastSync = XR_SUCCESS;
            if (sr != lastSync) { Log("input: xrSyncActions -> %d %s", (int)sr, ResultName(sr)); lastSync = sr; }
            if (XR_SUCCEEDED(sr))
            {
                for (int h = 0; h < 2; h++)
                {
                    CvrHand& hand = in.hands[h];
                    bool act = false;
                    hand.trigger = GetFloat(g->aTrigger[h], XR_NULL_PATH, &act);
                    hand.squeeze = GetFloat(g->aSqueeze[h], XR_NULL_PATH, &act);
                    XrActionStateGetInfo gi{XR_TYPE_ACTION_STATE_GET_INFO};
                    gi.action = g->aStick[h]; gi.subactionPath = XR_NULL_PATH;
                    XrActionStateVector2f vs{XR_TYPE_ACTION_STATE_VECTOR2F};
                    if (XR_SUCCEEDED(xrGetActionStateVector2f(g->session, &gi, &vs)) && vs.isActive)
                    {
                        hand.stickX = vs.currentState.x; hand.stickY = vs.currentState.y; act = true;
                    }
                    hand.stickClick = GetBool(g->aStickClick[h], XR_NULL_PATH) ? 1 : 0;
                    XrSpaceLocation loc{XR_TYPE_SPACE_LOCATION};
                    bool located = g->aimSpace[h] && XR_SUCCEEDED(xrLocateSpace(g->aimSpace[h], g->localSpace, displayTime, &loc));
                    bool oriValid = located && (loc.locationFlags & XR_SPACE_LOCATION_ORIENTATION_VALID_BIT);
                    bool posValid = located && (loc.locationFlags & XR_SPACE_LOCATION_POSITION_VALID_BIT);
                    XrPosef pose = loc.pose;
                    double now = NowMs();
                    if (oriValid && posValid)
                    {
                        g->hasHand[h] = true;
                        g->lastHand[h] = pose;
                        g->lastHandMs[h] = now;
                        hand.poseValid = 1;
                        act = true;
                    }
                    else if (g->hasHand[h] && now - g->lastHandMs[h] < 500.0)
                    {
                        // A short dropout (occluded controller): the last tracked position, turning with the IMU if
                        // orientation is still valid. Marked held so nothing reads motion (throws, swings) from it.
                        if (!oriValid) pose.orientation = g->lastHand[h].orientation;
                        pose.position = g->lastHand[h].position;
                        hand.poseValid = 1;
                        hand.poseHeld = 1;
                        act = true;
                    }
                    static int lastTrack[2] = {-1, -1};   // 2 tracked, 1 held, 0 lost
                    int track = hand.poseHeld ? 1 : hand.poseValid ? 2 : 0;
                    if (track != lastTrack[h] && lastTrack[h] != -1)
                        Log("controller %s: %s", h == 0 ? "left" : "right", track == 2 ? "tracked again" : track == 1 ? "tracking lost, holding the last pose" : "pose lost");
                    lastTrack[h] = track;
                    if (hand.poseValid)
                    {
                        hand.aimOrientation = {pose.orientation.x, pose.orientation.y, pose.orientation.z, pose.orientation.w};
                        hand.aimPosition = {pose.position.x, pose.position.y, pose.position.z};
                    }
                    hand.active = act ? 1 : 0;
                    static float lastLogged[2] = {0, 0};
                    float mag = std::fabs(hand.stickX) + std::fabs(hand.stickY) + hand.trigger + hand.squeeze;
                    if ((mag > 0.1f) != (lastLogged[h] > 0.1f)) { Log("input: hand %d stick %.2f,%.2f trigger %.2f squeeze %.2f", h, hand.stickX, hand.stickY, hand.trigger, hand.squeeze); lastLogged[h] = mag; }
                }
                if (GetBool(g->aA, XR_NULL_PATH)) in.buttons |= CVR_BTN_A;
                if (GetBool(g->aB, XR_NULL_PATH)) in.buttons |= CVR_BTN_B;
                if (GetBool(g->aX, XR_NULL_PATH)) in.buttons |= CVR_BTN_X;
                if (GetBool(g->aY, XR_NULL_PATH)) in.buttons |= CVR_BTN_Y;
                if (GetBool(g->aMenu, XR_NULL_PATH)) in.buttons |= CVR_BTN_MENU;
            }
            XrInteractionProfileState ps{XR_TYPE_INTERACTION_PROFILE_STATE};
            if (XR_SUCCEEDED(xrGetCurrentInteractionProfile(g->session, g->handPath[1], &ps)) && ps.interactionProfile != XR_NULL_PATH)
            {
                uint32_t n = 0;
                xrPathToString(g->instance, ps.interactionProfile, sizeof(in.profile), &n, in.profile);
            }
        }
        // XR-002: when not focused every value stays zero/released (in was zero-initialised).
        std::lock_guard<std::mutex> lock(g->frameMutex);
        g->input = in;
    }

    void UNITY_INTERFACE_API_Submit(int /*eventId*/) { SubmitFrame(); }
}

CVR_API int32_t cvr_xr_init(void* anyUnityTexture, char* errorBuf, int32_t errorLen)
{
    auto fail = [&](const char* msg) {
        Log("init failed: %s", msg);
        if (errorBuf && errorLen > 0) strncpy_s(errorBuf, errorLen, msg, _TRUNCATE);
        cvr_xr_shutdown();
        return 0;
    };
    if (g) return 1;
    g = new State();

    auto* tex = static_cast<ID3D11Texture2D*>(anyUnityTexture);
    if (!tex) return fail("no Unity texture to find the D3D11 device");
    tex->GetDevice(&g->device);
    if (!g->device) return fail("texture has no D3D11 device");
    g->device->GetImmediateContext(&g->context);

    if (!CreateInstance()) return fail("xrCreateInstance failed (no runtime, or no D3D11 support)");

    XrInstanceProperties props{XR_TYPE_INSTANCE_PROPERTIES};
    if (Check(xrGetInstanceProperties(g->instance, &props), "xrGetInstanceProperties"))
    {
        strncpy_s(g->info.runtimeName, props.runtimeName, _TRUNCATE);
        Log("Runtime: %s %u.%u.%u", props.runtimeName, XR_VERSION_MAJOR(props.runtimeVersion), XR_VERSION_MINOR(props.runtimeVersion), XR_VERSION_PATCH(props.runtimeVersion));
    }

    XrSystemGetInfo sgi{XR_TYPE_SYSTEM_GET_INFO};
    sgi.formFactor = XR_FORM_FACTOR_HEAD_MOUNTED_DISPLAY;
    if (!Check(xrGetSystem(g->instance, &sgi, &g->systemId), "xrGetSystem")) return fail("no head-mounted display (is the headset connected and the runtime running?)");
    XrSystemProperties sp{XR_TYPE_SYSTEM_PROPERTIES};
    if (Check(xrGetSystemProperties(g->instance, g->systemId, &sp), "xrGetSystemProperties"))
        strncpy_s(g->info.systemName, sp.systemName, _TRUNCATE);

    PFN_xrGetD3D11GraphicsRequirementsKHR getReqs = nullptr;
    if (!Check(xrGetInstanceProcAddr(g->instance, "xrGetD3D11GraphicsRequirementsKHR", reinterpret_cast<PFN_xrVoidFunction*>(&getReqs)), "xrGetInstanceProcAddr")) return fail("runtime lacks xrGetD3D11GraphicsRequirementsKHR");
    if (!CheckAdapter(getReqs)) return fail("could not query the game's graphics adapter");

    XrGraphicsBindingD3D11KHR binding{XR_TYPE_GRAPHICS_BINDING_D3D11_KHR};
    binding.device = g->device;
    XrSessionCreateInfo sci{XR_TYPE_SESSION_CREATE_INFO};
    sci.next = &binding;
    sci.systemId = g->systemId;
    if (!Check(xrCreateSession(g->instance, &sci, &g->session), "xrCreateSession"))
        return fail(g->adapterMismatch
            ? "xrCreateSession failed: the game runs on a different graphics card than the headset (on a laptop, set CasualtiesUnknown.exe to High performance in Windows Settings > Display > Graphics)"
            : "xrCreateSession failed");

    XrReferenceSpaceCreateInfo rsci{XR_TYPE_REFERENCE_SPACE_CREATE_INFO};
    rsci.referenceSpaceType = XR_REFERENCE_SPACE_TYPE_LOCAL;
    rsci.poseInReferenceSpace.orientation.w = 1.0f;
    if (!Check(xrCreateReferenceSpace(g->session, &rsci, &g->localSpace), "xrCreateReferenceSpace")) return fail("LOCAL space unavailable");

    if (!CreateSwapchains()) return fail("swapchain creation failed");
    if (!CreateActions()) Log("input: action setup failed; continuing without controllers");
    g->info.recommendedWidth = g->width;
    g->info.recommendedHeight = g->height;
    g->info.swapchainFormat = (int32_t)g->format;
    Log("init ok: system '%s'", g->info.systemName);
    return 1;
}

CVR_API void cvr_xr_get_info(CvrInfo* out)
{
    if (!out) return;
    if (!g) { *out = CvrInfo{}; return; }
    g->info.sessionState = (int32_t)g->sessionState;
    g->info.sessionRunning = g->sessionRunning ? 1 : 0;
    g->info.waitsSkipped = g->waitsSkipped;
    g->info.sessionLost = g->sessionLost ? 1 : 0;
    g->info.referenceSpaceChanged = g->referenceSpaceChanged ? 1 : 0;
    g->referenceSpaceChanged = false;
    *out = g->info;
}

CVR_API int32_t cvr_xr_poll()
{
    if (!g || !g->instance) return 0;
    XrEventDataBuffer ev{XR_TYPE_EVENT_DATA_BUFFER};
    while (xrPollEvent(g->instance, &ev) == XR_SUCCESS)
    {
        switch (ev.type)
        {
        case XR_TYPE_EVENT_DATA_SESSION_STATE_CHANGED:
            HandleStateChange(reinterpret_cast<XrEventDataSessionStateChanged*>(&ev)->state);
            break;
        case XR_TYPE_EVENT_DATA_INSTANCE_LOSS_PENDING:
            Log("Instance loss pending: the runtime is going away");
            g->sessionLost = true;
            break;
        case XR_TYPE_EVENT_DATA_REFERENCE_SPACE_CHANGE_PENDING:
        {
            auto* rc = reinterpret_cast<XrEventDataReferenceSpaceChangePending*>(&ev);
            Log("Reference space change pending (runtime recenter), space type %d", (int)rc->referenceSpaceType);
            if (rc->referenceSpaceType == XR_REFERENCE_SPACE_TYPE_LOCAL)
            {
                g->referenceSpaceChanged = true;
                g->hasHead = false;   // the held head position belongs to the old LOCAL
                g->hasHand[0] = g->hasHand[1] = false;
            }
            break;
        }
        default:
            break;
        }
        ev = {XR_TYPE_EVENT_DATA_BUFFER};
    }
    return (int32_t)g->sessionState;
}

CVR_API int32_t cvr_xr_wait_frame(CvrFrameInfo* out)
{
    if (!g || !g->sessionRunning || g->sessionLost) return 0;
    {
        // A frame that was waited but never submitted must not be waited over (call order).
        std::lock_guard<std::mutex> lock(g->frameMutex);
        if (g->framePending) { g->waitsSkipped++; return 0; }
    }
    XrFrameWaitInfo fwi{XR_TYPE_FRAME_WAIT_INFO};
    XrFrameState fs{XR_TYPE_FRAME_STATE};
    double t0 = NowMs();
    XrResult wr = xrWaitFrame(g->session, &fwi, &fs);
    if (wr == XR_ERROR_SESSION_LOST || wr == XR_ERROR_INSTANCE_LOST)
    {
        if (!g->sessionLost) Log("xrWaitFrame: %s, stopping", ResultName(wr));
        g->sessionLost = true;
        return 0;
    }
    if (!Check(wr, "xrWaitFrame")) return 0;
    double t1 = NowMs();
    g_timing.waitMs += t1 - t0;

    XrViewLocateInfo vli{XR_TYPE_VIEW_LOCATE_INFO};
    vli.viewConfigurationType = XR_VIEW_CONFIGURATION_TYPE_PRIMARY_STEREO;
    vli.displayTime = fs.predictedDisplayTime;
    vli.space = g->localSpace;
    XrViewState vs{XR_TYPE_VIEW_STATE};
    XrView views[2]{{XR_TYPE_VIEW}, {XR_TYPE_VIEW}};
    uint32_t count = 0;
    bool located = Check(xrLocateViews(g->session, &vli, &vs, 2, &count, views), "xrLocateViews") && count == 2;
    g_timing.locateMs += NowMs() - t1;
    bool oriValid = located && (vs.viewStateFlags & XR_VIEW_STATE_ORIENTATION_VALID_BIT);
    bool posValid = located && (vs.viewStateFlags & XR_VIEW_STATE_POSITION_VALID_BIT);
    if (oriValid && posValid)
    {
        g->hasHead = true;
        g->lastHead = {(views[0].pose.position.x + views[1].pose.position.x) * 0.5f,
                       (views[0].pose.position.y + views[1].pose.position.y) * 0.5f,
                       (views[0].pose.position.z + views[1].pose.position.z) * 0.5f};
        for (int eye = 0; eye < 2; eye++)
            g->eyeLocal[eye] = Rotate(Conjugate(views[eye].pose.orientation), Sub(views[eye].pose.position, g->lastHead));
    }
    else if (oriValid && g->hasHead)
    {
        // Position dropped, orientation still tracked (IMU): turn about the last tracked head position. These are
        // the poses both rendered and submitted (XR-005).
        for (int eye = 0; eye < 2; eye++)
            views[eye].pose.position = Add(g->lastHead, Rotate(views[eye].pose.orientation, g->eyeLocal[eye]));
    }
    bool valid = oriValid && (posValid || g->hasHead);
    static bool lastPosValid = true;
    if (located && posValid != lastPosValid)
    {
        Log("Head position tracking %s%s", posValid ? "restored" : "lost", !posValid && oriValid && g->hasHead ? " (holding the last position)" : "");
        lastPosValid = posValid;
    }

    // Outside frameMutex: UpdateInput takes it itself (calling it under the lock deadlocked the first frame).
    UpdateInput(fs.predictedDisplayTime);

    std::lock_guard<std::mutex> lock(g->frameMutex);
    g->frameState = fs;
    g->views[0] = views[0];
    g->views[1] = views[1];
    g->viewsValid = valid;
    g->framePending = true;
    g->frameIndex++;
    if (out)
    {
        out->predictedDisplayTime = fs.predictedDisplayTime;
        out->predictedDisplayPeriod = fs.predictedDisplayPeriod;
        out->shouldRender = fs.shouldRender ? 1 : 0;
        out->viewsValid = valid ? 1 : 0;
        out->positionValid = posValid ? 1 : 0;
        out->frameIndex = g->frameIndex;
        out->views[0] = ToCvr(views[0]);
        out->views[1] = ToCvr(views[1]);
    }
    return 1;
}

CVR_API void cvr_xr_set_eye_textures(void* left, void* right)
{
    if (!g) return;
    g->eyeTextures[0] = static_cast<ID3D11Texture2D*>(left);
    g->eyeTextures[1] = static_cast<ID3D11Texture2D*>(right);
}

CVR_API void* cvr_xr_get_submit_event()
{
    return reinterpret_cast<void*>(&UNITY_INTERFACE_API_Submit);
}

CVR_API void cvr_xr_shutdown()
{
    if (!g) return;
    // Graceful exit (spec order) before destroying anything. Destroying a running session with a
    // waited-but-unsubmitted frame hung VirtualDesktopXR at application quit (WER AppHangB1, 2026-10-03).
    if (g->sessionLost) Log("shutdown: the runtime ended the session; skipping the exit handshake");
    if (g->session && g->sessionRunning && !g->sessionLost)
    {
        g->eyeTextures[0] = g->eyeTextures[1] = nullptr;
        SubmitFrame();   // closes a pending waited frame with a legal zero-layer xrEndFrame
        Log("shutdown: requesting session exit");
        if (Check(xrRequestExitSession(g->session), "xrRequestExitSession"))
        {
            const double deadline = NowMs() + 1500.0;
            while (g->sessionRunning && NowMs() < deadline)
            {
                cvr_xr_poll();   // STOPPING -> xrEndSession inside HandleStateChange
                if (g->sessionRunning) Sleep(5);
            }
        }
        if (g->sessionRunning)
        {
            Log("shutdown: session did not reach STOPPING in time; ending it directly");
            if (XR_SUCCEEDED(xrEndSession(g->session))) g->sessionRunning = false;
        }
    }
    // XR-004: stop returns the process to its pre-start state.
    for (auto& space : g->aimSpace)
        if (space) xrDestroySpace(space);
    if (g->actionSet) xrDestroyActionSet(g->actionSet);   // destroys its actions too
    for (auto& eye : g->eyes)
        if (eye.handle) xrDestroySwapchain(eye.handle);
    if (g->localSpace) xrDestroySpace(g->localSpace);
    if (g->session)
    {
        if (g->sessionRunning) xrEndSession(g->session);
        xrDestroySession(g->session);
    }
    if (g->instance) xrDestroyInstance(g->instance);
    if (g->context) g->context->Release();
    if (g->device) g->device->Release();
    Log("shutdown: %llu frames submitted, %llu dropped", (unsigned long long)g->info.framesSubmitted, (unsigned long long)g->info.framesDropped);
    delete g;
    g = nullptr;
}

// Controller vibration (FP-4): amplitude 0..1 for the given seconds, on hand 0 (left) or 1 (right).
CVR_API void cvr_xr_haptic(int32_t hand, float amplitude, float seconds)
{
    if (!g || g->session == XR_NULL_HANDLE || hand < 0 || hand > 1 || g->aHaptic[hand] == XR_NULL_HANDLE) return;
    XrHapticVibration v{XR_TYPE_HAPTIC_VIBRATION};
    v.amplitude = amplitude < 0.f ? 0.f : (amplitude > 1.f ? 1.f : amplitude);
    v.duration = (XrDuration)(seconds * 1e9);
    v.frequency = XR_FREQUENCY_UNSPECIFIED;
    XrHapticActionInfo hi{XR_TYPE_HAPTIC_ACTION_INFO};
    hi.action = g->aHaptic[hand];
    hi.subactionPath = XR_NULL_PATH;
    XrResult r = xrApplyHapticFeedback(g->session, &hi, (const XrHapticBaseHeader*)&v);
    static XrResult last = XR_SUCCESS;
    static bool first = true;
    if (first || r != last) { Log("haptic: xrApplyHapticFeedback(hand %d, %.2f, %.2fs) -> %d %s", hand, v.amplitude, seconds, (int)r, ResultName(r)); last = r; first = false; }
}

CVR_API void cvr_xr_get_input(CvrInput* out)
{
    if (!out) return;
    if (!g) { *out = CvrInput{}; return; }
    std::lock_guard<std::mutex> lock(g->frameMutex);
    *out = g->input;
}

CVR_API void cvr_xr_get_log(char* buf, int32_t len)
{
    if (!buf || len <= 0) return;
    std::lock_guard<std::mutex> lock(g_logMutex);
    strncpy_s(buf, len, g_log.c_str(), _TRUNCATE);
    g_log.erase(0, std::min<size_t>(g_log.size(), (size_t)len - 1));
}

CVR_API void cvr_xr_set_log_file(const wchar_t* path)
{
    std::lock_guard<std::mutex> lock(g_logMutex);
    if (g_logFile) { fclose(g_logFile); g_logFile = nullptr; }
    g_logFileBytes = 0;
    // Appended: each VR start in one game session adds to it (the C# side keeps the previous game session's copy).
    if (path && path[0]) g_logFile = _wfsopen(path, L"a", _SH_DENYWR);
    if (g_logFile) { fputs("---- CasualtyVR bridge log opened\n", g_logFile); fflush(g_logFile); }
}
