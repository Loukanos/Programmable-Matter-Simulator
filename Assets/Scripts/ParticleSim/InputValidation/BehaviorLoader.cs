using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ParticleSim.Core;
using UnityEngine;

namespace ParticleSim.InputValidation
{
    /// <summary>
    /// Responsible only for loading and deserializing a behavior JSON file
    /// into a map of particleType -> TypeBehavior.
    /// </summary>
    public static class BehaviorLoader
    {
        // Matches "onTouch" or "onTouch(X+)" (whitespace and quotes around
        // the direction tolerated), case-insensitive.
        private static readonly Regex OnTouchKeyRegex =
            new(@"^onTouch\s*(?:\(\s*(.*?)\s*\))?$", RegexOptions.IgnoreCase);

        public static Dictionary<string, TypeBehavior> LoadFromFile(string absolutePath)
        {
            Debug.Log($"[BehaviorLoader] Attempting to load behavior from: {absolutePath}");
            if (!File.Exists(absolutePath))
            {
                Debug.LogError($"[BehaviorLoader] Behavior file NOT found at: {absolutePath}");
                return null;
            }

            try
            {
                string json = File.ReadAllText(absolutePath);
                var raw = JsonConvert.DeserializeObject<Dictionary<string, JObject>>(json);

                if (raw == null)
                {
                    Debug.LogError("[BehaviorLoader] Failed to parse behavior JSON (result was null).");
                    return null;
                }

                var result = new Dictionary<string, TypeBehavior>();
                foreach (KeyValuePair<string, JObject> kvp in raw)
                {
                    TypeBehavior behavior = ParseTypeBehavior(kvp.Value);

                    string invalidAction = FindMoveActionInOnInit(behavior.onInit);
                    if (invalidAction != null)
                    {
                        Debug.LogError($"[BehaviorLoader] Ação de movimento não é permitida em onInit: tipo '{kvp.Key}', ação '{invalidAction}'.");
                        return null;
                    }

                    result[kvp.Key] = behavior;
                }

                return result;
            }
            catch (Exception e)
            {
                Debug.LogError($"[BehaviorLoader] Error reading behavior file: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Movimento não é permitido em onInit (seção 2.2.3 do TCC). Retorna
        /// a primeira ação de movimento encontrada, ou null se nenhuma
        /// existir. Reconhece o mesmo prefixo que MoveAction.TryParse (a
        /// checagem não é feita chamando MoveAction diretamente para não
        /// criar uma dependência de InputValidation sobre SimulationEngine).
        /// </summary>
        private static string FindMoveActionInOnInit(List<ActionBlock> onInitBlocks)
        {
            if (onInitBlocks == null) return null;

            foreach (ActionBlock block in onInitBlocks)
            {
                if (block?.actions == null) continue;
                foreach (string action in block.actions)
                {
                    if (string.IsNullOrWhiteSpace(action)) continue;
                    if (action.Trim().StartsWith("move", StringComparison.OrdinalIgnoreCase))
                        return action;
                }
            }

            return null;
        }

        private static TypeBehavior ParseTypeBehavior(JObject obj)
        {
            var behavior = new TypeBehavior();
            if (obj == null) return behavior;

            foreach (JProperty prop in obj.Properties())
            {
                string name = prop.Name.Trim();

                if (name.Equals("onInit", StringComparison.OrdinalIgnoreCase))
                {
                    behavior.onInit = prop.Value.ToObject<List<ActionBlock>>() ?? new List<ActionBlock>();
                    continue;
                }

                if (name.Equals("onStep", StringComparison.OrdinalIgnoreCase))
                {
                    behavior.onStep = prop.Value.ToObject<List<ActionBlock>>() ?? new List<ActionBlock>();
                    continue;
                }

                Match match = OnTouchKeyRegex.Match(name);
                if (match.Success)
                {
                    string direction = match.Groups[1].Success
                        ? match.Groups[1].Value.Trim().Trim('\'', '\"')
                        : null;

                    behavior.onTouch.Add(new OnTouchEvent
                    {
                        direction = string.IsNullOrWhiteSpace(direction) ? null : direction,
                        blocks = prop.Value.ToObject<List<ActionBlock>>() ?? new List<ActionBlock>(),
                    });
                    continue;
                }

                Debug.LogWarning($"[BehaviorLoader] Chave de evento desconhecida em behavior.json: '{name}'");
            }

            return behavior;
        }
    }
}
