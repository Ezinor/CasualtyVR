using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace CasualtyVR
{
    /// <summary>P/Invoke surface of CasualtyVR.XrBridge.dll (see src/CasualtyVR.XrBridge/XrBridge.h).</summary>
    internal static class XrNative
    {
        private const string Dll = "CasualtyVR.XrBridge";

        [StructLayout(LayoutKind.Sequential)] public struct Vec3 { public float x, y, z; }
        [StructLayout(LayoutKind.Sequential)] public struct Quat { public float x, y, z, w; }
        [StructLayout(LayoutKind.Sequential)] public struct Fov { public float left, right, up, down; }

        [StructLayout(LayoutKind.Sequential)]
        public struct View
        {
            public Quat orientation;
            public Vec3 position;
            public Fov fov;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct FrameInfo
        {
            public long predictedDisplayTime;
            public long predictedDisplayPeriod;
            public int shouldRender;
            public int viewsValid;
            public ulong frameIndex;
            public View left;
            public View right;
            public int positionValid;   // 0: head position tracking dropped; the views hold the last tracked position
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct Info
        {
            public int recommendedWidth;
            public int recommendedHeight;
            public int swapchainFormat;
            public int sessionState;
            public int sessionRunning;
            public ulong framesSubmitted;
            public ulong framesDropped;
            public int lastSubmitResult;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string runtimeName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string systemName;
            public uint apiMajor, apiMinor;
            public ulong waitsSkipped;          // frames that skipped xrWaitFrame (previous frame not submitted yet)
            public int sessionLost;             // the runtime ended the session: stop VR
            public int referenceSpaceChanged;   // the runtime recentred LOCAL (consumed by this read)
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Hand
        {
            public Quat aimOrientation;
            public Vec3 aimPosition;
            public int poseValid;
            public int active;
            public float trigger, squeeze, stickX, stickY;
            public int stickClick;
            public int poseHeld;   // last tracked pose held through a short dropout: read no motion from it
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct Input
        {
            public Hand left;
            public Hand right;
            public uint buttons;   // A=1 B=2 X=4 Y=8 Menu=16
            public int focused;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string profile;
        }

        [DllImport(Dll)] public static extern void cvr_xr_get_input(out Input input);
        [DllImport(Dll)] public static extern void cvr_xr_haptic(int hand, float amplitude, float seconds);
        [DllImport(Dll)] public static extern int cvr_xr_init(IntPtr anyUnityTexture, StringBuilder errorBuf, int errorLen);
        [DllImport(Dll)] public static extern void cvr_xr_get_info(out Info info);
        [DllImport(Dll)] public static extern int cvr_xr_poll();
        [DllImport(Dll)] public static extern int cvr_xr_wait_frame(out FrameInfo frame);
        [DllImport(Dll)] public static extern void cvr_xr_set_eye_textures(IntPtr left, IntPtr right);
        [DllImport(Dll)] public static extern IntPtr cvr_xr_get_submit_event();
        [DllImport(Dll)] public static extern void cvr_xr_shutdown();
        [DllImport(Dll)] public static extern void cvr_xr_get_log(StringBuilder buf, int len);
        [DllImport(Dll, CharSet = CharSet.Unicode)] public static extern void cvr_xr_set_log_file(string path);

        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string path);

        /// <summary>
        /// Loads openxr_loader.dll and the bridge by full path from the plugin's native folder, so the
        /// DllImport by name resolves to them regardless of the process DLL search order.
        /// </summary>
        public static bool Preload(string nativeDir, out string error)
        {
            foreach (var name in new[] { "openxr_loader.dll", Dll + ".dll" })
            {
                string path = Path.Combine(nativeDir, name);
                if (!File.Exists(path)) { error = "missing " + path; return false; }
                if (LoadLibrary(path) == IntPtr.Zero) { error = $"LoadLibrary({path}) failed: Win32 {Marshal.GetLastWin32Error()}"; return false; }
            }
            error = null;
            return true;
        }

        public static string DrainLog()
        {
            var sb = new StringBuilder(16384);
            cvr_xr_get_log(sb, sb.Capacity);
            return sb.ToString();
        }
    }
}
