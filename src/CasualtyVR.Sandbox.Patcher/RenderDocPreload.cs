using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using BepInEx.Logging;
using Mono.Cecil;

namespace CasualtyVR.Sandbox
{
    /// <summary>
    /// Loads renderdoc.dll during BepInEx preload when the game is started with --cvr-renderdoc.
    /// Launching through renderdoccmd stops Doorstop from bootstrapping BepInEx, so the order is
    /// reversed: the preloader runs at Mono init, before Unity creates its D3D11 device, which is
    /// the point RenderDoc needs to be loaded by. The probe then triggers captures through the
    /// in-application API.
    /// </summary>
    public static class RenderDocPreload
    {
        public static IEnumerable<string> TargetDLLs { get; } = new string[0];

        private const string DefaultPath = @"C:\Program Files\RenderDoc\renderdoc.dll";

        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string path);

        public static void Initialize()
        {
            if (!Environment.GetCommandLineArgs().Contains("--cvr-renderdoc")) return;
            var log = Logger.CreateLogSource("CasualtyVR.RenderDoc");
            string path = Environment.GetEnvironmentVariable("CVR_RENDERDOC_DLL") ?? DefaultPath;
            IntPtr handle = LoadLibrary(path);
            if (handle == IntPtr.Zero)
                log.LogError($"LoadLibrary({path}) failed, Win32 error {Marshal.GetLastWin32Error()}");
            else
                log.LogMessage($"renderdoc.dll loaded before graphics device creation: {path}");
        }

        public static void Patch(AssemblyDefinition assembly) { }
    }
}
