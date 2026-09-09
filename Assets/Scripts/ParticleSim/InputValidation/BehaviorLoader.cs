using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ParticleSim.Core;
using UnityEngine;

namespace ParticleSim.InputValidation
{
    /// <summary>
    /// Loads a behavior JSON file into a map of particleType -> TypeBehavior.
    /// The file is only converted after <see cref="BehaviorValidator"/> accepts
    /// it — any lexical, syntactic or semantic error blocks the load
    /// (RF04/RNF08) and is reported through the returned
    /// <see cref="ValidationResult"/>. That includes the rule that movement is
    /// not allowed inside onInit, which lives with the rest of the semantic
    /// checks rather than being re-implemented here.
    ///
    /// Once validation passes, every block's condition/actions strings are
    /// parsed a second time here to build the AST
    /// (<see cref="ActionBlock.ParsedCondition"/>/<see cref="ActionBlock.ParsedActions"/>)
    /// that SimulationEngine evaluates — this second pass is guaranteed to
    /// succeed (validation already proved every string well-formed), so
    /// parsing happens exactly twice total per string, at load time, and
    /// never again per cycle per particle. Named-condition references
    /// ("definitions") are resolved — spliced into the real tree — as part of
    /// this same pass, so SimulationEngine never has to know "definitions" exists.
    /// </summary>
    public static class BehaviorLoader
    {
        public static Dictionary<string, TypeBehavior> LoadFromFile(string absolutePath, out ValidationResult validation)
        {
            validation = new ValidationResult();

            Debug.Log($"[BehaviorLoader] Attempting to load behavior from: {absolutePath}");
            if (!File.Exists(absolutePath))
            {
                validation.AddError($"Comportamento: arquivo não encontrado em '{absolutePath}'.");
                return null;
            }

            JObject root;
            try
            {
                root = JsonConvert.DeserializeObject<JObject>(File.ReadAllText(absolutePath));
            }
            catch (System.Exception e)
            {
                validation.AddError($"Comportamento: JSON inválido — {e.Message}");
                return null;
            }

            BehaviorValidator.Validate(root, validation);
            if (!validation.IsValid)
                return null;

            try
            {
                Dictionary<string, ConditionNode> resolvedDefinitions = BuildResolvedDefinitions(root);
                var definitionNames = new HashSet<string>(resolvedDefinitions.Keys, System.StringComparer.Ordinal);

                var behaviors = new Dictionary<string, TypeBehavior>();
                foreach (JProperty typeProperty in root.Properties())
                {
                    if (string.Equals(typeProperty.Name, Grammar.DefinitionsKey, System.StringComparison.OrdinalIgnoreCase))
                        continue;

                    behaviors[typeProperty.Name] = ParseTypeBehavior((JObject)typeProperty.Value, definitionNames, resolvedDefinitions);
                }

                return behaviors;
            }
            catch (System.Exception e)
            {
                validation.AddError($"Comportamento: erro ao interpretar o arquivo — {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Re-parses "definitions" (guaranteed well-formed and acyclic — both
        /// already proven by BehaviorValidator) and fully resolves every
        /// definition's own tree, so later resolving a block's condition
        /// against this dictionary never needs to recurse any further.
        /// </summary>
        private static Dictionary<string, ConditionNode> BuildResolvedDefinitions(JObject root)
        {
            var rawDefinitions = new Dictionary<string, ConditionNode>();

            if (root[Grammar.DefinitionsKey] is JObject definitionsObject)
            {
                var discarded = new ValidationResult();
                var names = new HashSet<string>(System.StringComparer.Ordinal);
                foreach (JProperty property in definitionsObject.Properties())
                    if (!string.IsNullOrWhiteSpace(property.Name))
                        names.Add(property.Name);

                foreach (JProperty property in definitionsObject.Properties())
                {
                    if (string.IsNullOrWhiteSpace(property.Name)) continue;
                    ExpressionValidator.ValidateCondition(property.Value.Value<string>(), "reparse", discarded, names, out ConditionNode node);
                    rawDefinitions[property.Name] = node;
                }
            }

            var resolved = new Dictionary<string, ConditionNode>();
            foreach (string name in rawDefinitions.Keys)
                DefinitionResolver.Resolve(new ConditionReferenceNode { Name = name }, rawDefinitions, resolved);
            return resolved;
        }

        /// <summary>
        /// Converts an already-validated type object into a TypeBehavior. Event
        /// keys are recognized through <see cref="Grammar.ParseEventKey"/> — the
        /// same routine the validator uses — so the key shapes are defined in
        /// exactly one place.
        /// </summary>
        private static TypeBehavior ParseTypeBehavior(JObject typeObject, HashSet<string> definitionNames, Dictionary<string, ConditionNode> resolvedDefinitions)
        {
            var behavior = new TypeBehavior();
            if (typeObject == null) return behavior;

            foreach (JProperty property in typeObject.Properties())
            {
                EventKey key = Grammar.ParseEventKey(property.Name);
                List<ActionBlock> blocks = property.Value.ToObject<List<ActionBlock>>() ?? new List<ActionBlock>();
                bool isOnInit = key.Kind == EventKeyKind.OnInit;
                BuildAst(blocks, isOnInit, definitionNames, resolvedDefinitions);

                switch (key.Kind)
                {
                    case EventKeyKind.OnInit:
                        behavior.onInit = blocks;
                        break;
                    case EventKeyKind.OnStep:
                        behavior.onStep = blocks;
                        break;
                    case EventKeyKind.OnTouch:
                        behavior.onTouch.Add(new OnTouchEvent { direction = key.Direction, blocks = blocks });
                        break;
                    case EventKeyKind.OnMessage:
                        behavior.onMessage.Add(new OnMessageEvent { direction = key.Direction, blocks = blocks });
                        break;
                }
            }

            return behavior;
        }

        /// <summary>
        /// Populates ParsedCondition/ParsedActions on every block, splicing
        /// any named-condition reference into its resolved tree along the
        /// way. A discarded ValidationResult is passed to the
        /// (already-proven-successful) parse calls only because the shared
        /// parsing API always reports through one — this call path can't
        /// actually fail post-validation.
        /// </summary>
        private static void BuildAst(List<ActionBlock> blocks, bool isOnInit, HashSet<string> definitionNames, Dictionary<string, ConditionNode> resolvedDefinitions)
        {
            var discarded = new ValidationResult();

            foreach (ActionBlock block in blocks)
            {
                if (block == null) continue;

                ExpressionValidator.ValidateCondition(block.condition, "reparse", discarded, definitionNames, out ConditionNode rawCondition);
                block.ParsedCondition = DefinitionResolver.Resolve(rawCondition, resolvedDefinitions, resolvedDefinitions);

                block.ParsedActions = new List<ActionNode>();
                foreach (string action in block.actions)
                {
                    ExpressionValidator.ValidateAction(action, "reparse", isOnInit, discarded, out ActionNode node);
                    block.ParsedActions.Add(node);
                }
            }
        }
    }
}
