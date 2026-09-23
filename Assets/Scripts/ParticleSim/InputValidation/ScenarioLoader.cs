using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ParticleSim.Core;
using UnityEngine;

namespace ParticleSim.InputValidation
{
    public static class ScenarioLoader
    {
        public static ScenarioData LoadFromFile(string absolutePath, out ValidationResult validation)
        {
            validation = new ValidationResult();

            Debug.Log($"[ScenarioLoader] Attempting to load scenario from: {absolutePath}");
            if (!File.Exists(absolutePath))
            {
                validation.AddError($"Cenário: arquivo não encontrado em '{absolutePath}'.");
                return null;
            }

            JObject root;
            try
            {
                root = JsonConvert.DeserializeObject<JObject>(File.ReadAllText(absolutePath));
            }
            catch (System.Exception e)
            {
                validation.AddError($"Cenário: JSON inválido — {e.Message}");
                return null;
            }

            ScenarioValidator.Validate(root, validation);
            if (!validation.IsValid)
                return null;

            try
            {
                return root.ToObject<ScenarioData>();
            }
            catch (System.Exception e)
            {
                validation.AddError($"Cenário: erro ao interpretar o arquivo — {e.Message}");
                return null;
            }
        }
    }
}
