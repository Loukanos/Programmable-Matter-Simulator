using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

using ParticleSim.Core;

namespace ParticleSim.InputValidation
{
    /// <summary>
    /// Structural validation of the behavior file (RF04/RNF08), plus the entry
    /// point into the lexical/syntactic/semantic validation of every condition
    /// and action string it contains (see <see cref="ExpressionValidator"/>).
    ///
    /// Runs against the raw JSON tree so shape errors are reported with the
    /// particle type, event and block that caused them, instead of surfacing
    /// as a generic deserialization exception.
    /// </summary>
    public static class BehaviorValidator
    {
        /// <summary>The only fields a rule block may declare — the schema is deliberately minimal.</summary>
        private static readonly HashSet<string> AllowedBlockFields =
            new HashSet<string>(new[] { "condition", "actions" }, StringComparer.OrdinalIgnoreCase);

        public static void Validate(JObject root, ValidationResult result)
        {
            if (root == null)
            {
                result.AddError("Comportamento: arquivo vazio ou não é um objeto JSON.");
                return;
            }

            foreach (JProperty typeProperty in root.Properties())
            {
                if (string.IsNullOrWhiteSpace(typeProperty.Name))
                {
                    result.AddError("Comportamento: existe um tipo de partícula com nome vazio.");
                    continue;
                }

                if (typeProperty.Value.Type != JTokenType.Object)
                {
                    result.AddError($"Comportamento: o tipo '{typeProperty.Name}' deve ser um objeto com chaves de evento ({Grammar.DescribeEventKeys()}).");
                    continue;
                }

                ValidateType(typeProperty.Name, (JObject)typeProperty.Value, result);
            }
        }

        private static void ValidateType(string typeName, JObject typeObject, ValidationResult result)
        {
            foreach (JProperty eventProperty in typeObject.Properties())
            {
                string where = $"Comportamento: tipo '{typeName}', evento '{eventProperty.Name}'";
                EventKey key = Grammar.ParseEventKey(eventProperty.Name);

                if (key.Kind == EventKeyKind.Unknown)
                {
                    result.AddError($"{where}: chave de evento desconhecida (esperado: {Grammar.DescribeEventKeys()}).");
                    continue;
                }

                if (key.Kind == EventKeyKind.OnInit || key.Kind == EventKeyKind.OnStep)
                {
                    if (key.HasDirectionPart)
                    {
                        result.AddError($"{where}: '{key.RawName}' não aceita parâmetro entre parênteses.");
                        continue;
                    }
                }
                else if (key.HasDirectionPart && !LanguageVocabulary.IsDirection(key.Direction))
                {
                    string shown = string.IsNullOrWhiteSpace(key.Direction) ? "(vazio)" : key.Direction;
                    result.AddError($"{where}: direção inválida '{shown}' (esperado: {LanguageVocabulary.DescribeDirections()}).");
                    continue;
                }

                ValidateBlocks(eventProperty.Value, where, key.Kind == EventKeyKind.OnInit, result);
            }
        }

        private static void ValidateBlocks(JToken blocksToken, string where, bool isOnInit, ValidationResult result)
        {
            if (blocksToken == null || blocksToken.Type == JTokenType.Null)
            {
                result.AddError($"{where}: deve conter uma lista de blocos de regra.");
                return;
            }

            if (blocksToken.Type != JTokenType.Array)
            {
                result.AddError($"{where}: deve ser uma lista de blocos de regra.");
                return;
            }

            var blocks = (JArray)blocksToken;
            for (int i = 0; i < blocks.Count; i++)
            {
                string blockWhere = $"{where}, bloco {i + 1}";

                if (blocks[i].Type != JTokenType.Object)
                {
                    result.AddError($"{blockWhere}: deve ser um objeto com 'actions' e, opcionalmente, 'condition'.");
                    continue;
                }

                var block = (JObject)blocks[i];
                ValidateBlockFields(block, blockWhere, result);
                ValidateCondition(block, blockWhere, result);
                ValidateActions(block, blockWhere, isOnInit, result);
            }
        }

        /// <summary>
        /// Rejects any field other than condition/actions, which is what turns
        /// a silent typo ("conditon") into a load error instead of a block that
        /// quietly always runs.
        /// </summary>
        private static void ValidateBlockFields(JObject block, string where, ValidationResult result)
        {
            foreach (JProperty field in block.Properties())
                if (!AllowedBlockFields.Contains(field.Name))
                    result.AddError($"{where}: campo desconhecido '{field.Name}' (um bloco aceita apenas 'condition' e 'actions').");
        }

        private static void ValidateCondition(JObject block, string where, ValidationResult result)
        {
            JToken token = block["condition"];
            if (token == null || token.Type == JTokenType.Null) return;

            if (token.Type != JTokenType.String)
            {
                result.AddError($"{where}: 'condition' deve ser um texto.");
                return;
            }

            ExpressionValidator.ValidateCondition(token.Value<string>(), where, result, out _);
        }

        private static void ValidateActions(JObject block, string where, bool isOnInit, ValidationResult result)
        {
            JToken token = block["actions"];
            if (token == null || token.Type == JTokenType.Null)
            {
                result.AddError($"{where}: 'actions' é obrigatório e deve ser uma lista de textos.");
                return;
            }

            if (token.Type != JTokenType.Array)
            {
                result.AddError($"{where}: 'actions' deve ser uma lista de textos.");
                return;
            }

            var actions = (JArray)token;
            if (actions.Count == 0)
            {
                result.AddError($"{where}: 'actions' não pode ser uma lista vazia.");
                return;
            }

            for (int i = 0; i < actions.Count; i++)
            {
                string actionWhere = $"{where}, ação {i + 1}";

                if (actions[i].Type != JTokenType.String)
                {
                    result.AddError($"{actionWhere}: deve ser um texto.");
                    continue;
                }

                ExpressionValidator.ValidateAction(actions[i].Value<string>(), actionWhere, isOnInit, result, out _);
            }
        }
    }
}
