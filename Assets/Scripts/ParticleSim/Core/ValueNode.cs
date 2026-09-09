namespace ParticleSim.Core
{
    /// <summary>
    /// Parsed form of a single function argument. Before this extension every
    /// argument was exactly one token; now an argument can also be a nested
    /// read-function call (state/opposite/messageValue/myId/countNeighbors/
    /// canMove/moveResult), recursively — e.g. opposite(state('dir')).
    /// Built once at load time alongside
    /// ConditionNode/ActionNode, walked by SimulationEngine.ValueResolver at
    /// evaluation time.
    /// </summary>
    public abstract class ValueNode { }

    /// <summary>A single token used as-is — the only shape arguments had before this extension.</summary>
    public class LiteralValueNode : ValueNode
    {
        public Token Token;
    }

    /// <summary>A nested read-function call, e.g. state('dir') or opposite(state('dir')).</summary>
    public class ValueCallNode : ValueNode
    {
        public string FunctionName;
        public ValueNode[] Arguments = System.Array.Empty<ValueNode>();
    }
}
