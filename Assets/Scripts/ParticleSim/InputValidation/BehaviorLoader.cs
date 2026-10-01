using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ParticleSim.Core;
using UnityEngine;

namespace ParticleSim.InputValidation
{
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
                    ExpressionValidator.ValidateCondition(property.Value.Value<string>(), "reparse", discarded, names, allowMessageDirection: true, out ConditionNode node);
                    rawDefinitions[property.Name] = node;
                }
            }

            var resolved = new Dictionary<string, ConditionNode>();
            foreach (string name in rawDefinitions.Keys)
                DefinitionResolver.Resolve(new ConditionReferenceNode { Name = name }, rawDefinitions, resolved);
            return resolved;
        }

        private static TypeBehavior ParseTypeBehavior(JObject typeObject, HashSet<string> definitionNames, Dictionary<string, ConditionNode> resolvedDefinitions)
        {
            var behavior = new TypeBehavior();
            if (typeObject == null) return behavior;

            foreach (JProperty property in typeObject.Properties())
            {
                EventKey key = Grammar.ParseEventKey(property.Name);
                List<ActionBlock> blocks = property.Value.ToObject<List<ActionBlock>>() ?? new List<ActionBlock>();
                BuildAst(blocks, key.Kind, definitionNames, resolvedDefinitions);

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

        private static void BuildAst(List<ActionBlock> blocks, EventKeyKind eventKind, HashSet<string> definitionNames, Dictionary<string, ConditionNode> resolvedDefinitions)
        {
            var discarded = new ValidationResult();

            foreach (ActionBlock block in blocks)
            {
                if (block == null) continue;

                ExpressionValidator.ValidateCondition(block.condition, "reparse", discarded, definitionNames,
                    allowMessageDirection: eventKind == EventKeyKind.OnMessage, out ConditionNode rawCondition);
                block.ParsedCondition = DefinitionResolver.Resolve(rawCondition, resolvedDefinitions, resolvedDefinitions);

                block.ParsedActions = new List<ActionNode>();
                foreach (string action in block.actions)
                {
                    ExpressionValidator.ValidateAction(action, "reparse", eventKind, discarded, out ActionNode node);
                    block.ParsedActions.Add(node);
                }
            }
        }
    }
}
