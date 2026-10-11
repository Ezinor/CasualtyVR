using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace CasualtyVR.Sandbox
{
    /// <summary>
    /// File-backed stand-in for UnityEngine.PlayerPrefs. The sandbox patcher rewrites every
    /// PlayerPrefs call in Assembly-CSharp to land here, so the dev copy never touches the
    /// registry key it shares with the user's real install (HKCU\Software\Orsoniks\CasualtiesUnknown).
    /// Signatures mirror PlayerPrefs exactly; the patcher matches by name and parameter types.
    /// </summary>
    public static class SandboxPrefs
    {
        private static readonly Dictionary<string, object> values = new Dictionary<string, object>();
        private static bool loaded;

        // Set by the patcher-injected path; falls back next to the game executable.
        public static string FilePath => Path.Combine(SandboxPaths.DataPath, "playerprefs.txt");

        public static int GetInt(string key) => GetInt(key, 0);
        public static int GetInt(string key, int defaultValue) => Get(key, out var v) && v is int i ? i : defaultValue;
        public static float GetFloat(string key) => GetFloat(key, 0f);
        public static float GetFloat(string key, float defaultValue) => Get(key, out var v) && v is float f ? f : defaultValue;
        public static string GetString(string key) => GetString(key, "");
        public static string GetString(string key, string defaultValue) => Get(key, out var v) && v is string s ? s : defaultValue;
        public static bool HasKey(string key) { Load(); return values.ContainsKey(key); }

        public static void SetInt(string key, int value) => Set(key, value);
        public static void SetFloat(string key, float value) => Set(key, value);
        public static void SetString(string key, string value) => Set(key, value);
        public static void DeleteKey(string key) { Load(); if (values.Remove(key)) Save(); }
        public static void DeleteAll() { Load(); values.Clear(); Save(); }

        public static void Save()
        {
            Load();
            var sb = new StringBuilder();
            foreach (var kv in values)
            {
                string type = kv.Value is int ? "i" : kv.Value is float ? "f" : "s";
                string val = kv.Value is float f ? f.ToString("R", CultureInfo.InvariantCulture)
                           : kv.Value is int i ? i.ToString(CultureInfo.InvariantCulture)
                           : Escape((string)kv.Value);
                sb.Append(type).Append('\t').Append(Escape(kv.Key)).Append('\t').Append(val).Append('\n');
            }
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllText(FilePath, sb.ToString());
        }

        private static bool Get(string key, out object value) { Load(); return values.TryGetValue(key, out value); }

        private static void Set(string key, object value) { Load(); values[key] = value; Save(); }

        private static void Load()
        {
            if (loaded) return;
            loaded = true;
            if (!File.Exists(FilePath)) return;
            foreach (var line in File.ReadAllLines(FilePath))
            {
                var parts = line.Split('\t');
                if (parts.Length != 3) continue;
                string key = Unescape(parts[1]);
                switch (parts[0])
                {
                    case "i": values[key] = int.Parse(parts[2], CultureInfo.InvariantCulture); break;
                    case "f": values[key] = float.Parse(parts[2], CultureInfo.InvariantCulture); break;
                    default: values[key] = Unescape(parts[2]); break;
                }
            }
        }

        private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\n", "\\n");

        private static string Unescape(string s)
        {
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    char c = s[++i];
                    sb.Append(c == 't' ? '\t' : c == 'n' ? '\n' : c);
                }
                else sb.Append(s[i]);
            }
            return sb.ToString();
        }
    }

    /// <summary>Replacement for Application.persistentDataPath inside Assembly-CSharp.</summary>
    public static class SandboxPaths
    {
        private static string dataPath;

        public static string DataPath
        {
            get
            {
                if (dataPath == null)
                {
                    // <game>\CasualtiesUnknown_Data\Managed\this.dll -> <game>\CasualtyVR_UserData
                    string managed = Path.GetDirectoryName(typeof(SandboxPaths).Assembly.Location);
                    string gameRoot = Path.GetFullPath(Path.Combine(managed, "..", ".."));
                    dataPath = Path.Combine(gameRoot, "CasualtyVR_UserData");
                    Directory.CreateDirectory(dataPath);
                }
                return dataPath;
            }
        }
    }
}
