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
                    result[kvp.Key] = ParseTypeBehavior(kvp.Value);

                return result;
            }
            catch (Exception e)
            {
                Debug.LogError($"[BehaviorLoader] Error reading behavior file: {e.Message}");
                return null;
            }
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
