using System.Collections.Generic;
using ParticleSim.Core;

namespace ParticleSim.InputValidation
{
    /// <summary>
    /// Resolves ConditionReferenceNode placeholders (behavior.json's
    /// "definitions" block) into the referenced definition's own tree,
    /// spliced in place — pure tree substitution, no textual grouping
    /// needed, since the replacement just becomes whatever node sits in that
    /// slot of the parent Or/And/Not structure. Used by BehaviorValidator
    /// (to reject a cyclic "definitions" block as a load error) and
    /// BehaviorLoader (to build the final tree SimulationEngine evaluates).
    ///
    /// Every ConditionReferenceNode reaching this class is already known to
    /// name a real "definitions" entry — ExpressionValidator only ever builds
    /// one for an identifier it found in the definition-names set it was
    /// given, so an undefined reference is already a "função desconhecida"
    /// parse error before it ever gets here.
    /// </summary>
    public static class DefinitionResolver
    {
        /// <summary>
        /// Detects a cycle in the definitions' reference graph — a
        /// definition that, directly or transitively, references itself.
        /// Returns the name of a definition involved in a cycle, or null if
        /// there is none. Standard 3-color DFS (0 unvisited / 1 visiting / 2
        /// done); a back-edge to a "visiting" node is the cycle.
        /// </summary>
        public static string FindCycle(Dictionary<string, ConditionNode> definitions)
        {
            var state = new Dictionary<string, int>();
            foreach (string name in definitions.Keys)
            {
                string cycleName = Visit(name, definitions, state);
                if (cycleName != null) return cycleName;
            }
            return null;
        }

        private static string Visit(string name, Dictionary<string, ConditionNode> definitions, Dictionary<string, int> state)
        {
            if (state.TryGetValue(name, out int visitState))
                return visitState == 1 ? name : null;

            state[name] = 1;
            if (definitions.TryGetValue(name, out ConditionNode tree))
            {
                foreach (string referenced in FindReferences(tree))
                {
                    string cycleName = Visit(referenced, definitions, state);
                    if (cycleName != null) return cycleName;
                }
            }
            state[name] = 2;
            return null;
        }

        /// <summary>Every definition name referenced anywhere in the tree (a bare condition term only — a definition can never be referenced as a function argument).</summary>
        public static IEnumerable<string> FindReferences(ConditionNode node)
        {
            switch (node)
            {
                case ConditionReferenceNode reference:
                    yield return reference.Name;
                    break;

                case OrNode or:
                    foreach (ConditionNode operand in or.Operands)
                        foreach (string name in FindReferences(operand))
                            yield return name;
                    break;

                case AndNode and:
                    foreach (ConditionNode operand in and.Operands)
                        foreach (string name in FindReferences(operand))
                            yield return name;
                    break;

                case NotNode not:
                    foreach (string name in FindReferences(not.Operand))
                        yield return name;
                    break;

                // BoolLiteralNode / FunctionCallNode: leaves, nothing to recurse into.
            }
        }

        /// <summary>
        /// Replaces every ConditionReferenceNode in <paramref name="tree"/>
        /// with the fully-resolved tree of the definition it names, using —
        /// and populating — <paramref name="resolvedCache"/> so a definition
        /// referenced from several places is only resolved once. Assumes no
        /// cycle exists (checked beforehand by FindCycle) and every reference
        /// names a real definition (see class summary) — safe to call
        /// unconditionally once those hold.
        /// </summary>
        public static ConditionNode Resolve(ConditionNode tree, Dictionary<string, ConditionNode> rawDefinitions, Dictionary<string, ConditionNode> resolvedCache)
        {
            switch (tree)
            {
                case null:
                    return null;

                case ConditionReferenceNode reference:
                    return ResolveDefinition(reference.Name, rawDefinitions, resolvedCache);

                case OrNode or:
                    var resolvedOr = new OrNode();
                    foreach (ConditionNode operand in or.Operands)
                        resolvedOr.Operands.Add(Resolve(operand, rawDefinitions, resolvedCache));
                    return resolvedOr;

                case AndNode and:
                    var resolvedAnd = new AndNode();
                    foreach (ConditionNode operand in and.Operands)
                        resolvedAnd.Operands.Add(Resolve(operand, rawDefinitions, resolvedCache));
                    return resolvedAnd;

                case NotNode not:
                    return new NotNode { Operand = Resolve(not.Operand, rawDefinitions, resolvedCache) };

                default:
                    return tree; // BoolLiteralNode / FunctionCallNode: no children to resolve.
            }
        }

        private static ConditionNode ResolveDefinition(string name, Dictionary<string, ConditionNode> rawDefinitions, Dictionary<string, ConditionNode> resolvedCache)
        {
            if (resolvedCache.TryGetValue(name, out ConditionNode cached)) return cached;
            if (!rawDefinitions.TryGetValue(name, out ConditionNode raw)) return null;

            ConditionNode resolved = Resolve(raw, rawDefinitions, resolvedCache);
            resolvedCache[name] = resolved;
            return resolved;
        }
    }
}
