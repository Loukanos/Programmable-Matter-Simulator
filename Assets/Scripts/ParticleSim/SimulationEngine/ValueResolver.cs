using ParticleSim.Core;
using UnityEngine;

namespace ParticleSim.SimulationEngine
{
    public static class ValueResolver
    {
        public static object Resolve(ValueNode node, EvaluationContext ctx)
        {
            switch (node)
            {
                case null:
                    return null;
                case LiteralValueNode literal:
                    return ConditionEvaluator.ParseValue(literal.Token.Value);
                case ValueCallNode call:
                    return ResolveCall(call, ctx);
                default:
                    return null;
            }
        }

        public static string ResolveDirection(ValueNode node, EvaluationContext ctx)
        {
            object value = Resolve(node, ctx);
            string text = value as string ?? value?.ToString();
            return LanguageVocabulary.IsDirection(text) ? LanguageVocabulary.NormalizeDirection(text) : null;
        }

        private static object ResolveCall(ValueCallNode call, EvaluationContext ctx)
        {
            if (call.FunctionName == LanguageVocabulary.HasNeighbor)
                return ConditionEvaluator.EvaluateHasNeighbor(call.Arguments, ctx);

            if (call.FunctionName == LanguageVocabulary.CheckMessage)
                return ConditionEvaluator.EvaluateCheckMessage(call.Arguments, ctx);

            if (call.FunctionName == LanguageVocabulary.State)
                return ctx.Particle.GetInternalState(LiteralArgumentText(call, 0));

            if (call.FunctionName == LanguageVocabulary.Opposite)
            {
                string inner = ResolveDirection(call.Arguments[0], ctx);
                return inner == null ? null : LanguageVocabulary.OppositeDirection(inner);
            }

            if (call.FunctionName == LanguageVocabulary.MessageValue)
                return ctx.MatchedMessage?.Payload;

            if (call.FunctionName == LanguageVocabulary.MyId)
                return (double)ctx.Particle.Id;

            if (call.FunctionName == LanguageVocabulary.CountNeighbors)
                return (double)CountNeighbors(ctx);

            if (call.FunctionName == LanguageVocabulary.CanMove)
                return ResolveCanMove(call.Arguments, ctx);

            if (call.FunctionName == LanguageVocabulary.MoveResult)
                return LanguageVocabulary.DescribeMoveOutcome(ctx.Particle.LastMoveResult);

            Debug.LogWarning($"[ValueResolver] Unknown value function: {call.FunctionName}");
            return null;
        }

        private static string LiteralArgumentText(ValueCallNode call, int index) =>
            call.Arguments.Length > index && call.Arguments[index] is LiteralValueNode literal ? literal.Token.Value : null;

        private static int CountNeighbors(EvaluationContext ctx)
        {
            int count = 0;
            foreach (Vector3Int delta in LanguageVocabulary.DirectionDeltas)
            {
                Vector3Int target = ctx.Particle.GridPosition + delta;
                if (ctx.Grid.IsInBounds(target) && ctx.Grid.IsOccupied(target))
                    count++;
            }
            return count;
        }

        public static bool ResolveCanMove(ValueNode[] args, EvaluationContext ctx)
        {
            string direction = ResolveDirection(args[0], ctx);
            if (direction == null || !LanguageVocabulary.TryGetDirectionDelta(direction, out Vector3Int delta))
                return false;

            Vector3Int target = ctx.Particle.GridPosition + delta;
            return ctx.Grid.IsInBounds(target) && !ctx.Grid.IsOccupied(target);
        }
    }
}
