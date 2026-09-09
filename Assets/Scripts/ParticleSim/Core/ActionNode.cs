namespace ParticleSim.Core
{
    /// <summary>
    /// Parsed form of one entry of a block's "actions" list — a single
    /// function call (move/setState/setColor/wait/sendMessage), built once at
    /// load time by <c>InputValidation.ExpressionValidator</c> and applied by
    /// <c>SimulationEngine</c> at every cycle. "wait" (bare, no parens) parses
    /// to the same shape as "wait()" — a zero-argument call — since the
    /// grammar already treats them identically.
    /// </summary>
    public class ActionNode
    {
        public string FunctionName;
        public ValueNode[] Arguments = System.Array.Empty<ValueNode>();
    }
}
