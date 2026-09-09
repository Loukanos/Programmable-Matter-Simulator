using ParticleSim.Core;
using UnityEngine;

namespace ParticleSim.SimulationEngine
{
    /// <summary>
    /// Resolves a ValueNode (a literal token, or a nested read-function call)
    /// against an EvaluationContext into an actual runtime value. Mirrors the
    /// recursive shape ExpressionValidator already validated at load time —
    /// this is where a well-formed but not-statically-checkable expression
    /// (state('key') reading a key that doesn't exist, or holds the wrong
    /// type) turns into the documented "behaves as if absent/invalid"
    /// fallback, never an exception.
    /// </summary>
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

        /// <summary>
        /// Resolves node and checks the result is one of the six directions.
        /// Used everywhere a direction argument is accepted (move,
        /// hasNeighbor, sendMessage, canMove) — uniformly for a plain
        /// literal direction token as well as a nested
        /// state(...)/opposite(...) call, since both flow through Resolve the
        /// same way. Returns null when unresolvable — a missing/wrong-type
        /// state key, or a computed value that isn't a direction — which
        /// callers treat as "no neighbor there" / "invalid movement"
        /// depending on context, per the extension's documented
        /// direction-as-variable fallback.
        /// </summary>
        public static string ResolveDirection(ValueNode node, EvaluationContext ctx)
        {
            object value = Resolve(node, ctx);
            string text = value as string ?? value?.ToString();
            return LanguageVocabulary.IsDirection(text) ? LanguageVocabulary.NormalizeDirection(text) : null;
        }

        private static object ResolveCall(ValueCallNode call, EvaluationContext ctx)
        {
            // hasNeighbor/checkMessage are boolean-producing and dual-role
            // (Condition | Value, see Grammar.FunctionRole) — their canonical
            // implementation lives on ConditionEvaluator, called back into
            // here for the nested-value case so the two roles can never
            // disagree about the result.
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

            Debug.LogWarning($"[ValueResolver] Função de valor desconhecida: {call.FunctionName}");
            return null;
        }

        /// <summary>state's argument is always ArgumentKind.StateKey — validation never allows a nested call there, so this is always a LiteralValueNode.</summary>
        private static string LiteralArgumentText(ValueCallNode call, int index) =>
            call.Arguments.Length > index && call.Arguments[index] is LiteralValueNode literal ? literal.Token.Value : null;

        private static int CountNeighbors(EvaluationContext ctx)
        {
            int count = 0;
            foreach (Vector3Int delta in LanguageVocabulary.DirectionDeltas)
            {
                Vector3Int target = ctx.Particle.GridPosition + delta;
                if (ctx.Grid.IsInBounds(target) && ctx.Grid.TryGetParticleAt(target, out _))
                    count++;
            }
            return count;
        }

        /// <summary>
        /// canMove is a query, never a reservation — no state is touched and
        /// no event fires. True only when the direction resolves and the
        /// target cell is both in bounds and unoccupied; false for every
        /// other reason (occupied, out of bounds, unresolvable direction),
        /// without distinguishing which. Public and taking the raw argument
        /// array (rather than a ValueCallNode) because this is also the
        /// canonical implementation when canMove is used as a direct
        /// condition — SimulationEngine.ConditionEvaluator.EvaluateFunctionCall
        /// calls back into this for that case, mirroring how
        /// EvaluateHasNeighbor/EvaluateCheckMessage above are called from here
        /// for the opposite direction.
        /// </summary>
        public static bool ResolveCanMove(ValueNode[] args, EvaluationContext ctx)
        {
            string direction = ResolveDirection(args[0], ctx);
            if (direction == null || !LanguageVocabulary.TryGetDirectionDelta(direction, out Vector3Int delta))
                return false;

            Vector3Int target = ctx.Particle.GridPosition + delta;
            return ctx.Grid.IsInBounds(target) && !ctx.Grid.TryGetParticleAt(target, out _);
        }
    }
}
