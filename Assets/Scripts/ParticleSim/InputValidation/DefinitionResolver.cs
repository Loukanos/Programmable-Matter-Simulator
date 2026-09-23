using System.Collections.Generic;
using ParticleSim.Core;

namespace ParticleSim.InputValidation
{
    public static class DefinitionResolver
    {
        // Three-color DFS: state 1 = visiting, 2 = done; reaching a node that is still visiting closes a cycle.
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
            }
        }

        // Precondition: FindCycle found no cycle. A cyclic graph would recurse forever.
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
                    return tree;
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
