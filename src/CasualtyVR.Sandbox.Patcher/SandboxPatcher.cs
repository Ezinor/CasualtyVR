using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace CasualtyVR.Sandbox
{
    /// <summary>
    /// BepInEx 5 preloader patcher. Before Assembly-CSharp loads, every call to
    /// Application.persistentDataPath and every PlayerPrefs.* call is redirected to
    /// CasualtyVR.Sandbox.Runtime (copied into the Managed folder), so the dev copy cannot read
    /// or write the user's real save, settings or PlayerPrefs. Fails closed: if the runtime
    /// assembly is missing, the patcher throws and BepInEx reports it, instead of silently
    /// running against the user's data.
    /// </summary>
    public static class SandboxPatcher
    {
        public static IEnumerable<string> TargetDLLs { get; } = new[] { "Assembly-CSharp.dll" };

        private static readonly ManualLogSource Log = Logger.CreateLogSource("CasualtyVR.Sandbox");

        public static void Patch(AssemblyDefinition assembly)
        {
            string runtimePath = Path.Combine(Paths.ManagedPath, "CasualtyVR.Sandbox.Runtime.dll");
            if (!File.Exists(runtimePath))
                throw new FileNotFoundException("CasualtyVR sandbox runtime missing; refusing to run unsandboxed", runtimePath);

            var runtime = AssemblyDefinition.ReadAssembly(runtimePath);
            var prefsType = runtime.MainModule.GetType("CasualtyVR.Sandbox.SandboxPrefs");
            var pathsType = runtime.MainModule.GetType("CasualtyVR.Sandbox.SandboxPaths");
            var module = assembly.MainModule;
            var dataPathGetter = module.ImportReference(pathsType.Properties.First(p => p.Name == "DataPath").GetMethod);

            int pathRewrites = 0, prefRewrites = 0;
            var unmatched = new HashSet<string>();
            foreach (var type in module.GetTypes())
            foreach (var method in type.Methods)
            {
                if (!method.HasBody) continue;
                foreach (var ins in method.Body.Instructions)
                {
                    if ((ins.OpCode != OpCodes.Call && ins.OpCode != OpCodes.Callvirt) || !(ins.Operand is MethodReference target))
                        continue;
                    string owner = target.DeclaringType.FullName;
                    if (owner == "UnityEngine.Application" && target.Name == "get_persistentDataPath")
                    {
                        ins.OpCode = OpCodes.Call;
                        ins.Operand = dataPathGetter;
                        pathRewrites++;
                    }
                    else if (owner == "UnityEngine.PlayerPrefs")
                    {
                        var replacement = prefsType.Methods.FirstOrDefault(m => m.Name == target.Name && SameParameters(m, target));
                        if (replacement == null) { unmatched.Add(target.FullName); continue; }
                        ins.OpCode = OpCodes.Call;
                        ins.Operand = module.ImportReference(replacement);
                        prefRewrites++;
                    }
                }
            }

            Log.LogMessage($"Sandboxed Assembly-CSharp: {pathRewrites} persistentDataPath and {prefRewrites} PlayerPrefs call sites redirected");
            if (unmatched.Count > 0)
                throw new InvalidOperationException("Unsandboxed PlayerPrefs overloads: " + string.Join(", ", unmatched));
        }

        private static bool SameParameters(MethodDefinition a, MethodReference b) =>
            a.Parameters.Count == b.Parameters.Count &&
            a.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(b.Parameters.Select(p => p.ParameterType.FullName));
    }
}
