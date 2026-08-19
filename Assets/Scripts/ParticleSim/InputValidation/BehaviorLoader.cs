using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
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
                var data = JsonConvert.DeserializeObject<Dictionary<string, TypeBehavior>>(json);

                if (data == null)
                {
                    Debug.LogError("[BehaviorLoader] Failed to parse behavior JSON (result was null).");
                    return null;
                }

                return data;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[BehaviorLoader] Error reading behavior file: {e.Message}");
                return null;
            }
        }
    }
}
