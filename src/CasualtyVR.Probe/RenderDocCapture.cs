using System;
using System.Runtime.InteropServices;
using System.Text;

namespace CasualtyVR.Probe
{
    /// <summary>
    /// Minimal binding to RenderDoc's in-application API. Only works when the game was launched
    /// through RenderDoc (renderdoc.dll already injected); otherwise <see cref="TryInit"/> returns false.
    /// Indices follow the RENDERDOC_API_1_1_2 function table in renderdoc_app.h (unions count once).
    /// </summary>
    internal static class RenderDocCapture
    {
        private const int ApiVersion_1_1_2 = 10102;
        private const int IdxSetCaptureFilePathTemplate = 11;
        private const int IdxGetNumCaptures = 13;
        private const int IdxGetCapture = 14;
        private const int IdxTriggerCapture = 15;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetApiFn(int version, out IntPtr api);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SetPathTemplateFn([MarshalAs(UnmanagedType.LPStr)] string template);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint GetNumCapturesFn();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint GetCaptureFn(uint idx, StringBuilder filename, ref uint pathLength, ref ulong timestamp);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void TriggerCaptureFn();

        [DllImport("kernel32", CharSet = CharSet.Ansi)] private static extern IntPtr GetModuleHandle(string name);
        [DllImport("kernel32", CharSet = CharSet.Ansi)] private static extern IntPtr GetProcAddress(IntPtr module, string name);

        private static IntPtr api;

        public static bool TryInit(string captureTemplate)
        {
            IntPtr module = GetModuleHandle("renderdoc.dll");
            if (module == IntPtr.Zero) return false;
            IntPtr getApi = GetProcAddress(module, "RENDERDOC_GetAPI");
            if (getApi == IntPtr.Zero) return false;
            if (Marshal.GetDelegateForFunctionPointer<GetApiFn>(getApi)(ApiVersion_1_1_2, out api) != 1 || api == IntPtr.Zero)
                return false;
            Fn<SetPathTemplateFn>(IdxSetCaptureFilePathTemplate)(captureTemplate);
            return true;
        }

        public static void Trigger() => Fn<TriggerCaptureFn>(IdxTriggerCapture)();

        public static string LatestCapturePath()
        {
            uint count = Fn<GetNumCapturesFn>(IdxGetNumCaptures)();
            if (count == 0) return null;
            uint len = 0; ulong ts = 0;
            var get = Fn<GetCaptureFn>(IdxGetCapture);
            get(count - 1, null, ref len, ref ts);
            var sb = new StringBuilder((int)len + 1);
            get(count - 1, sb, ref len, ref ts);
            return sb.ToString();
        }

        private static T Fn<T>(int index) where T : Delegate =>
            Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(api, index * IntPtr.Size));
    }
}
