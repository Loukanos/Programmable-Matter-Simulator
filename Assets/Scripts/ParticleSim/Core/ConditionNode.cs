using System.Collections.Generic;

namespace ParticleSim.Core
{
    public abstract class ConditionNode { }

    public class OrNode : ConditionNode
    {
        public List<ConditionNode> Operands = new();
    }

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

    public class FunctionCallNode : ConditionNode
    {
        public string FunctionName;
        public ValueNode[] Arguments = System.Array.Empty<ValueNode>();
    }

    public class ConditionReferenceNode : ConditionNode
    {
        public string Name;
    }
}
