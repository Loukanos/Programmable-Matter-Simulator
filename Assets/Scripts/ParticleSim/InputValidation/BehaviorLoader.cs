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
    /// never again per cycle per particle.
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
                var behaviors = new Dictionary<string, TypeBehavior>();
                foreach (JProperty typeProperty in root.Properties())
                    behaviors[typeProperty.Name] = ParseTypeBehavior((JObject)typeProperty.Value);

                return behaviors;
            }
            catch (System.Exception e)
            {
                validation.AddError($"Comportamento: erro ao interpretar o arquivo — {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Converts an already-validated type object into a TypeBehavior. Event
        /// keys are recognized through <see cref="Grammar.ParseEventKey"/> — the
        /// same routine the validator uses — so the key shapes are defined in
        /// exactly one place.
        /// </summary>
        private static TypeBehavior ParseTypeBehavior(JObject typeObject)
        {
            var behavior = new TypeBehavior();
            if (typeObject == null) return behavior;

            foreach (JProperty property in typeObject.Properties())
            {
                EventKey key = Grammar.ParseEventKey(property.Name);
                List<ActionBlock> blocks = property.Value.ToObject<List<ActionBlock>>() ?? new List<ActionBlock>();
                bool isOnInit = key.Kind == EventKeyKind.OnInit;
                BuildAst(blocks, isOnInit);

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
        /// Populates ParsedCondition/ParsedActions on every block. A discarded
        /// ValidationResult is passed to the (already-proven-successful)
        /// parse calls only because the shared parsing API always reports
        /// through one — this call path can't actually fail post-validation.
        /// </summary>
        private static void BuildAst(List<ActionBlock> blocks, bool isOnInit)
        {
            var discarded = new ValidationResult();

            foreach (ActionBlock block in blocks)
            {
                if (block == null) continue;

                ExpressionValidator.ValidateCondition(block.condition, "reparse", discarded, out block.ParsedCondition);

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
