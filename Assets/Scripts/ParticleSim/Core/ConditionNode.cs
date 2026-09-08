using System.Collections.Generic;

namespace ParticleSim.Core
{
    /// <summary>
    /// Parsed form of a block's "condition" string, built once by
    /// <c>InputValidation.ExpressionValidator</c> during load-time validation
    /// and walked by <c>SimulationEngine.ConditionEvaluator</c> at every
    /// cycle — replaces re-lexing/re-parsing the same string per particle per
    /// cycle. Mirrors the grammar exactly as it already existed (Or/And/Not
    /// precedence, no parenthesized grouping): this migration changes how the
    /// grammar is represented, not the grammar itself.
    /// </summary>
    public abstract class ConditionNode { }

    /// <summary>Left-to-right chain of operands joined by Or (any true → true). N-ary, matching how the string grammar already flattens a run of "a Or b Or c".</summary>
    public class OrNode : ConditionNode
    {
        public List<ConditionNode> Operands = new();
    }

    /// <summary>Left-to-right chain of operands joined by And (all true → true).</summary>
    public class AndNode : ConditionNode
    {
        public List<ConditionNode> Operands = new();
    }

    public class NotNode : ConditionNode
    {
        public ConditionNode Operand;
    }

    public class BoolLiteralNode : ConditionNode
    {
        public bool Value;
    }

    /// <summary>A condition function call — hasNeighbor/checkState/checkMessage — with its already-validated arguments.</summary>
    public class FunctionCallNode : ConditionNode
    {
        public string FunctionName;
        public Token[] Arguments = System.Array.Empty<Token>();
    }
}
