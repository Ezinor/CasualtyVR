// CasualtyVR native OpenXR bridge (D3D11). C ABI consumed by CasualtyVR.Core via P/Invoke.
//
// Threading contract (playbook XR-005 "wait once, cache, submit after every camera"):
//   main thread : cvr_xr_poll, cvr_xr_wait_frame (xrWaitFrame + xrLocateViews, cached), shutdown
//   render path : the event returned by cvr_xr_get_submit_event, issued with GL.IssuePluginEvent
//                 after both eye cameras rendered: xrBeginFrame, copy eye RTs into swapchains,
//                 xrEndFrame with exactly the views that were cached at wait time.
#pragma once
#include <stdint.h>

#define CVR_API extern "C" __declspec(dllexport)

struct CvrVec3 { float x, y, z; };
struct CvrQuat { float x, y, z, w; };
struct CvrFov  { float left, right, up, down; };   // radians, OpenXR convention (left/down negative)

struct CvrView
{
    CvrQuat orientation;   // OpenXR space (right-handed, -Z forward)
    CvrVec3 position;      // metres, in the LOCAL reference space
    CvrFov  fov;
};

struct CvrFrameInfo
{
    int64_t predictedDisplayTime;
    int64_t predictedDisplayPeriod;
    int32_t shouldRender;
    int32_t viewsValid;      // both orientation and position valid
    uint64_t frameIndex;     // our counter, increments per successful xrWaitFrame
    CvrView views[2];        // 0 = left, 1 = right
};

struct CvrInfo
{
    int32_t recommendedWidth;
    int32_t recommendedHeight;
    int32_t swapchainFormat;     // DXGI_FORMAT value
    int32_t sessionState;        // XrSessionState
    int32_t sessionRunning;
    uint64_t framesSubmitted;
    uint64_t framesDropped;
    int32_t lastSubmitResult;    // XrResult of the last xrEndFrame
    char runtimeName[128];
    char systemName[128];
    uint32_t apiMajor, apiMinor;
};

// Creates instance (version negotiated newest-first), system, session on the device that owns
// `anyUnityTexture` (an ID3D11Texture2D*), LOCAL space and one swapchain per eye.
// Returns 1 on success, 0 on failure (message in errorBuf).
CVR_API int32_t cvr_xr_init(void* anyUnityTexture, char* errorBuf, int32_t errorLen);
CVR_API void    cvr_xr_get_info(CvrInfo* out);
// Pumps runtime events and drives session begin/end. Returns the current XrSessionState.
CVR_API int32_t cvr_xr_poll();
// xrWaitFrame + xrLocateViews when the session is running. Returns 1 if a frame is pending submit.
CVR_API int32_t cvr_xr_wait_frame(CvrFrameInfo* out);
// ID3D11Texture2D* of the left/right eye RenderTextures; must match swapchain size, compatible format.
CVR_API void    cvr_xr_set_eye_textures(void* left, void* right);
// Render-thread callback for GL.IssuePluginEvent(fn, 1).
CVR_API void*   cvr_xr_get_submit_event();
CVR_API void    cvr_xr_shutdown();
CVR_API void    cvr_xr_get_log(char* buf, int32_t len);   // drains the bridge's log buffer

// Controller state (playbook INPUT-001: physical transport only; the game mapping lives in C#).
// Refreshed by cvr_xr_wait_frame (xrSyncActions + aim poses located at the frame's display time).
enum CvrButtons : uint32_t { CVR_BTN_A = 1, CVR_BTN_B = 2, CVR_BTN_X = 4, CVR_BTN_Y = 8, CVR_BTN_MENU = 16 };
struct CvrHand
{
    CvrQuat aimOrientation;   // aim pose (HAND-001: pointing contract), LOCAL space, OpenXR axes
    CvrVec3 aimPosition;
    int32_t poseValid;
    int32_t active;           // the hand's actions are bound and the controller is tracked/connected
    float trigger, squeeze, stickX, stickY;
    int32_t stickClick;
};
struct CvrInput
{
    CvrHand hands[2];         // 0 = left, 1 = right
    uint32_t buttons;         // CvrButtons
    int32_t focused;          // session FOCUSED: input is only meaningful (XR-002) when 1
    char profile[128];        // current interaction profile of the right hand (diagnostic)
};
CVR_API void    cvr_xr_get_input(CvrInput* out);
CVR_API void    cvr_xr_haptic(int32_t hand, float amplitude, float seconds);   // controller vibration
