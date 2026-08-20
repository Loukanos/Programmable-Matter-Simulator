namespace ParticleSim.SimulationEngine
{
    /// <summary>
    /// wait() é um no-op explícito: indica que nenhuma ação deve ser
    /// executada no ciclo atual, tornando uma regra "não fazer nada" legível
    /// no behavior.json em vez de exigir uma actions list vazia.
    /// </summary>
    public static class WaitAction
    {
        public static bool IsWait(string action)
        {
            if (string.IsNullOrEmpty(action))
                return false;

            string a = action.Trim();

            if (a.Equals("wait", System.StringComparison.OrdinalIgnoreCase))
                return true;

            if (!a.StartsWith("wait", System.StringComparison.OrdinalIgnoreCase))
                return false;

            int open = a.IndexOf('(');
            int close = a.LastIndexOf(')');
            if (open < 0 || close < 0 || close <= open)
                return false;

            // Nada além de espaços entre "wait" e "(", e nenhum argumento dentro dos parênteses.
            return a.Substring(4, open - 4).Trim().Length == 0
                && a.Substring(open + 1, close - open - 1).Trim().Length == 0;
        }
    }
}
