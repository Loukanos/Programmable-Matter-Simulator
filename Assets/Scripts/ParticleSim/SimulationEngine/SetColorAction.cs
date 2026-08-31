using ParticleSim.Core;
using UnityEngine;

namespace ParticleSim.SimulationEngine
{
    public static class SetColorAction
    {
        public static bool TryParse(string action, out Color color)
        {
            color = Color.white;

            if (string.IsNullOrEmpty(action))
                return false;

            string a = action.Trim();
            if (!a.StartsWith(LanguageVocabulary.SetColor, System.StringComparison.OrdinalIgnoreCase))
                return false;

            int open = a.IndexOf('(');
            int close = a.LastIndexOf(')');
            if (open < 0 || close < 0 || close <= open)
                return false;

            string arg = a.Substring(open + 1, close - open - 1).Trim().Trim('\'', '\"');

            if (ColorUtility.TryParseHtmlString(arg, out color))
            {
                return true;
            }

            Debug.LogWarning($"[SetColorAction] Failed to parse hex color: {arg}");
            return false;
        }
    }
}