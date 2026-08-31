using ParticleSim.Core;
using UnityEngine;

namespace ParticleSim.SimulationEngine
{
    /// <summary>
    /// Minimal parser for the supported action strings of this version.
    /// Only move('DIR') is recognized, where DIR is one of the six directions
    /// defined by <see cref="LanguageVocabulary"/>.
    /// </summary>
    public static class MoveAction
    {
        /// <summary>
        /// Tries to interpret an action string as a move and returns the
        /// grid delta it represents. Returns Vector3Int.zero and false when
        /// the string is not a recognized move action.
        /// </summary>
        public static bool TryParse(string action, out Vector3Int delta)
        {
            delta = Vector3Int.zero;

            if (string.IsNullOrEmpty(action))
                return false;

            string a = action.Trim();
            if (!a.StartsWith(LanguageVocabulary.Move, System.StringComparison.OrdinalIgnoreCase))
                return false;

            int open = a.IndexOf('(');
            int close = a.LastIndexOf(')');
            if (open < 0 || close < 0 || close <= open)
                return false;

            string arg = a.Substring(open + 1, close - open - 1);
            if (LanguageVocabulary.TryGetDirectionDelta(arg, out delta))
                return true;

            Debug.LogWarning($"[MoveAction] Unknown move direction: '{arg.Trim()}'");
            return false;
        }
    }
}
