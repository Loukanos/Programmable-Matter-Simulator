namespace ParticleSim.SimulationEngine
{
    public static class SetStateAction
    {
        public static bool TryParse(string action, out string key, out object value)
        {
            key = null;
            value = null;

            if (string.IsNullOrEmpty(action))
                return false;

            string a = action.Trim();
            if (!a.StartsWith("setState", System.StringComparison.OrdinalIgnoreCase))
                return false;

            int open = a.IndexOf('(');
            int close = a.LastIndexOf(')');
            if (open < 0 || close < 0 || close <= open)
                return false;

            string inner = a.Substring(open + 1, close - open - 1).Trim();
            
            // Split by comma, but handle quoted strings
            string[] parts = SplitArguments(inner);
            if (parts.Length < 2)
                return false;

            key = parts[0].Trim().Trim('\'', '\"');
            string valStr = parts[1].Trim().Trim('\'', '\"');
            
            value = ConditionEvaluator.ParseValue(valStr);
            return true;
        }

        private static string[] SplitArguments(string inner)
        {
            System.Collections.Generic.List<string> args = new System.Collections.Generic.List<string>();
            bool inQuotes = false;
            char quoteChar = '\0';
            int start = 0;

            for (int i = 0; i < inner.Length; i++)
            {
                char c = inner[i];
                if ((c == '\'' || c == '\"') && !inQuotes)
                {
                    inQuotes = true;
                    quoteChar = c;
                }
                else if (c == quoteChar && inQuotes)
                {
                    inQuotes = false;
                    quoteChar = '\0';
                }
                else if (c == ',' && !inQuotes)
                {
                    args.Add(inner.Substring(start, i - start).Trim());
                    start = i + 1;
                }
            }
            args.Add(inner.Substring(start).Trim());
            return args.ToArray();
        }
    }
}
