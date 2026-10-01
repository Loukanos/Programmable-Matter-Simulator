using System.Linq;
using ParticleSim.Core;

namespace ParticleSim.InputValidation
{
    public static class FunctionUsage
    {
        public static bool Calls(ConditionNode node, string functionName)
        {
            switch (node)
            {
                case OrNode or:
                    return or.Operands.Any(operand => Calls(operand, functionName));
                case AndNode and:
                    return and.Operands.Any(operand => Calls(operand, functionName));
                case NotNode not:
                    return Calls(not.Operand, functionName);
                case FunctionCallNode call:
                    return call.FunctionName == functionName || call.Arguments.Any(argument => Calls(argument, functionName));
                default:
                    return false;
            }
        }

        public static bool Calls(ActionNode node, string functionName) =>
            node != null && node.Arguments.Any(argument => Calls(argument, functionName));

        public static bool Calls(ValueNode node, string functionName)
        {
            switch (node)
            {
                case ValueCallNode call:
                    return call.FunctionName == functionName || call.Arguments.Any(argument => Calls(argument, functionName));
                case SignedCallNode signed:
                    return Calls(signed.Call, functionName);
                default:
                    return false;
            }
        }
    }
}
