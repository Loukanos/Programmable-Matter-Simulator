using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace ParticleSim.InputValidation
{
    public static class ScenarioValidator
    {
        private static readonly string[] AxisNames = { "x", "y", "z" };

        public static void Validate(JObject root, ValidationResult result)
        {
            if (root == null)
            {
                result.AddError("Cenário: arquivo vazio ou não é um objeto JSON.");
                return;
            }

            int[] gridSize = ValidateGridSize(root, result);
            ValidateCycleLimit(root, "maxCycles", result);
            ValidateCycleLimit(root, "idleCyclesLimit", result);
            ValidateParticles(root, gridSize, result);
        }

        private static int[] ValidateGridSize(JObject root, ValidationResult result)
        {
            JToken token = root["gridSize"];
            if (token == null || token.Type == JTokenType.Null)
            {
                result.AddError("Cenário: 'gridSize' é obrigatório e deve ser uma lista [x, y, z].");
                return null;
            }

            if (token.Type != JTokenType.Array)
            {
                result.AddError("Cenário: 'gridSize' deve ser uma lista [x, y, z].");
                return null;
            }

            var array = (JArray)token;
            if (array.Count != 3)
            {
                result.AddError($"Cenário: 'gridSize' deve ter exatamente 3 valores [x, y, z], mas tem {array.Count}.");
                return null;
            }

            var size = new int[3];
            bool valid = true;

            for (int i = 0; i < 3; i++)
            {
                if (array[i].Type != JTokenType.Integer)
                {
                    result.AddError($"Cenário: 'gridSize' eixo {AxisNames[i]} deve ser um número inteiro, mas é '{array[i]}'.");
                    valid = false;
                    continue;
                }

                size[i] = array[i].Value<int>();
                if (size[i] <= 0)
                {
                    result.AddError($"Cenário: 'gridSize' eixo {AxisNames[i]} deve ser maior que zero, mas é {size[i]}.");
                    valid = false;
                }
            }

            return valid ? size : null;
        }

        // Zero or negative is valid (no cap / check disabled), so only the type is checked.
        private static void ValidateCycleLimit(JObject root, string field, ValidationResult result)
        {
            JToken token = root[field];
            if (token == null || token.Type == JTokenType.Null) return;

            if (token.Type != JTokenType.Integer)
                result.AddError($"Cenário: '{field}', quando presente, deve ser um número inteiro, mas é '{token}'.");
        }

        private static void ValidateParticles(JObject root, int[] gridSize, ValidationResult result)
        {
            JToken token = root["particles"];
            if (token == null || token.Type == JTokenType.Null) return;

            if (token.Type != JTokenType.Array)
            {
                result.AddError("Cenário: 'particles' deve ser uma lista de partículas.");
                return;
            }

            var seenIds = new HashSet<int>();
            var seenPositions = new HashSet<string>();
            var particles = (JArray)token;

            for (int i = 0; i < particles.Count; i++)
            {
                string where = $"Cenário: partícula #{i + 1}";

                if (particles[i].Type != JTokenType.Object)
                {
                    result.AddError($"{where} deve ser um objeto com 'id', 'type' e 'position'.");
                    continue;
                }

                var particle = (JObject)particles[i];

                if (TryValidateId(particle, where, seenIds, result, out int id))
                    where = $"Cenário: partícula id {id}";

                ValidateType(particle, where, result);
                ValidatePosition(particle, where, gridSize, seenPositions, result);
            }
        }

        private static bool TryValidateId(JObject particle, string where, HashSet<int> seenIds, ValidationResult result, out int id)
        {
            id = 0;

            JToken token = particle["id"];
            if (token == null || token.Type == JTokenType.Null)
            {
                result.AddError($"{where}: 'id' é obrigatório.");
                return false;
            }

            if (token.Type != JTokenType.Integer)
            {
                result.AddError($"{where}: 'id' deve ser um número inteiro, mas é '{token}'.");
                return false;
            }

            id = token.Value<int>();
            if (!seenIds.Add(id))
                result.AddError($"Cenário: id de partícula duplicado: {id}.");

            return true;
        }

        private static void ValidateType(JObject particle, string where, ValidationResult result)
        {
            JToken token = particle["type"];
            if (token == null || token.Type == JTokenType.Null)
            {
                result.AddError($"{where}: 'type' é obrigatório.");
                return;
            }

            if (token.Type != JTokenType.String || string.IsNullOrWhiteSpace(token.Value<string>()))
                result.AddError($"{where}: 'type' deve ser um texto não vazio.");
        }

        private static void ValidatePosition(JObject particle, string where, int[] gridSize,
            HashSet<string> seenPositions, ValidationResult result)
        {
            JToken token = particle["position"];
            if (token == null || token.Type == JTokenType.Null)
            {
                result.AddError($"{where}: 'position' é obrigatório e deve ser uma lista [x, y, z].");
                return;
            }

            if (token.Type != JTokenType.Array)
            {
                result.AddError($"{where}: 'position' deve ser uma lista [x, y, z].");
                return;
            }

            var array = (JArray)token;
            if (array.Count != 3)
            {
                result.AddError($"{where}: 'position' deve ter exatamente 3 valores [x, y, z], mas tem {array.Count}.");
                return;
            }

            var coordinates = new int[3];
            for (int i = 0; i < 3; i++)
            {
                if (array[i].Type != JTokenType.Integer)
                {
                    result.AddError($"{where}: 'position' eixo {AxisNames[i]} deve ser um número inteiro, mas é '{array[i]}'.");
                    return;
                }
                coordinates[i] = array[i].Value<int>();
            }

            if (gridSize != null)
            {
                for (int i = 0; i < 3; i++)
                {
                    if (coordinates[i] < 0 || coordinates[i] >= gridSize[i])
                    {
                        result.AddError($"{where}: 'position' eixo {AxisNames[i]} = {coordinates[i]} está fora da grade (válido: 0 a {gridSize[i] - 1}).");
                        return;
                    }
                }
            }

            string key = $"{coordinates[0]}, {coordinates[1]}, {coordinates[2]}";
            if (!seenPositions.Add(key))
                result.AddError($"{where}: mais de uma partícula começa na posição [{key}].");
        }
    }
}
