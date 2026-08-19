using UnityEngine;

namespace ParticleSim.SimulationEngine
{
    /// <summary>
    /// Minimal parser for the supported action strings of this version.
    /// Only move('DIR') is recognized, where DIR is one of:
    /// X+, X-, Y+, Y-, Z+, Z-.
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
            if (!a.StartsWith("move", System.StringComparison.OrdinalIgnoreCase))
                return false;

            int open = a.IndexOf('(');
            int close = a.LastIndexOf(')');
            if (open < 0 || close < 0 || close <= open)
                return false;

            // Extract the argument and strip quotes / whitespace.
            string arg = a.Substring(open + 1, close - open - 1)
                          .Trim()
                          .Trim('\'', '"')
                          .Trim()
                          .ToUpperInvariant();

            switch (arg)
            {
                case "X+": delta = new Vector3Int(1, 0, 0); return true;
                case "X-": delta = new Vector3Int(-1, 0, 0); return true;
                case "Y+": delta = new Vector3Int(0, 1, 0); return true;
                case "Y-": delta = new Vector3Int(0, -1, 0); return true;
                case "Z+": delta = new Vector3Int(0, 0, 1); return true;
                case "Z-": delta = new Vector3Int(0, 0, -1); return true;
                default:
                    Debug.LogWarning($"[MoveAction] Unknown move direction: '{arg}'");
                    return false;
            }
        }
    }
}
