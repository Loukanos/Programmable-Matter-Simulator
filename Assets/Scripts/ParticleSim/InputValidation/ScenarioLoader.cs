using System.IO;
using Newtonsoft.Json;
using ParticleSim.Core;
using UnityEngine;

namespace ParticleSim.InputValidation
{
    /// <summary>
    /// Responsible only for loading and deserializing a scenario JSON file.
    /// </summary>
    public static class ScenarioLoader
    {
        public static ScenarioData LoadFromFile(string absolutePath)
        {
            Debug.Log($"[ScenarioLoader] Attempting to load scenario from: {absolutePath}");
            if (!File.Exists(absolutePath))
            {
                Debug.LogError($"[ScenarioLoader] Scenario file NOT found at: {absolutePath}");
                return null;
            }

            try
            {
                string json = File.ReadAllText(absolutePath);
                ScenarioData data = JsonConvert.DeserializeObject<ScenarioData>(json);

                if (data == null)
                {
                    Debug.LogError("[ScenarioLoader] Failed to parse scenario JSON (result was null).");
                    return null;
                }

                if (data.gridSize == null || data.gridSize.Length < 3)
                {
                    Debug.LogError("[ScenarioLoader] Scenario must define a gridSize with 3 values [x,y,z].");
                    return null;
                }

                return data;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[ScenarioLoader] Error reading scenario file: {e.Message}");
                return null;
            }
        }
    }
}
