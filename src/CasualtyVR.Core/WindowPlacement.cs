using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CasualtyVR
{
    /// <summary>
    /// Keeps the game window on the user's chosen monitor (user request 2026-10-03: the game opened on
    /// whichever screen had focus). The display is matched by resolution ("1920x1080") or by name, not by
    /// Unity's monitor index, because `-monitor N` did not override the game's saved window placement.
    /// Applied at startup and after every scene load, since the game re-applies its resolution settings.
    /// </summary>
    internal sealed class WindowPlacement : MonoBehaviour
    {
        private void Awake()
        {
            SceneManager.sceneLoaded += (s, m) => StartCoroutine(PlaceSoon());
            StartCoroutine(PlaceSoon());
        }

        private IEnumerator PlaceSoon()
        {
            // Let the game's own Settings apply its resolution first, then move.
            for (int i = 0; i < 3; i++) yield return null;
            Place();
        }

        private static void Place()
        {
            string wanted = Plugin.PreferredDisplay.Value?.Trim();
            if (string.IsNullOrEmpty(wanted)) return;
            var displays = new List<DisplayInfo>();
            Screen.GetDisplayLayout(displays);
            DisplayInfo? match = null;
            foreach (var d in displays)
                if ($"{d.width}x{d.height}" == wanted || d.name == wanted) { match = d; break; }
            if (match == null)
            {
                Plugin.Log.LogWarning($"Window: no display matches '{wanted}' (have: {string.Join(", ", displays.ConvertAll(d => $"{d.name} {d.width}x{d.height}"))})");
                return;
            }
            var current = Screen.mainWindowDisplayInfo;
            if (current.name == match.Value.name && current.width == match.Value.width && current.height == match.Value.height) return;
            var op = Screen.MoveMainWindowTo(match.Value, Vector2Int.zero);
            Plugin.Log.LogInfo($"Window: moving from '{current.name}' {current.width}x{current.height} to '{match.Value.name}' {match.Value.width}x{match.Value.height}");
        }
    }
}
