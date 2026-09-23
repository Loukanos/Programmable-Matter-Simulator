namespace ParticleSim.Core
{
    public abstract class ValueNode { }

    public class LiteralValueNode : ValueNode
    {
        public Token Token;
    }

    public class ValueCallNode : ValueNode
    {
        public string FunctionName;
        public ValueNode[] Arguments = System.Array.Empty<ValueNode>();
    }
}
