using ParticleSim.Core;

namespace ParticleSim.SimulationEngine
{
    /// <summary>
    /// Parses sendMessage(dir?, content). With two arguments, the first is
    /// the target neighbor's direction (X+/X-/Y+/Y-/Z+/Z-); with a single
    /// argument, it's a broadcast (SimulationEngine decides the recipients).
    /// Content accepts the same types as setState (numeric, boolean or
    /// string), reusing ConditionEvaluator.ParseValue.
    /// </summary>
    public static class SendMessageAction
    {
        public static bool TryParse(string action, out string direction, out object content)
        {
            direction = null;
            content = null;

            if (string.IsNullOrEmpty(action))
                return false;

            string a = action.Trim();
            if (!a.StartsWith(LanguageVocabulary.SendMessage, System.StringComparison.OrdinalIgnoreCase))
                return false;

            int open = a.IndexOf('(');
            int close = a.LastIndexOf(')');
            if (open < 0 || close < 0 || close <= open)
                return false;
            if (a.Substring(open + 1, close - open - 1).Trim().Length == 0)
                return false;

            string[] args = ConditionEvaluator.ExtractArguments(a);
            switch (args.Length)
            {
                case 1:
                    content = ConditionEvaluator.ParseValue(args[0]);
                    return true;
                case 2:
                    direction = args[0];
                    content = ConditionEvaluator.ParseValue(args[1]);
                    return true;
                default:
                    return false;
            }
        }
    }
}
