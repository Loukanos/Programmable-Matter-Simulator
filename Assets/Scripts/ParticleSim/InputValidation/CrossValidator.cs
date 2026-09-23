using System.Collections.Generic;
using ParticleSim.Core;

namespace ParticleSim.InputValidation
{
    public static class CrossValidator
    {
        // Uses ContainsKey on the dictionary the engine will query, so this check can never disagree with Step()'s lookup.
        public static void Validate(
            ScenarioData scenario,
            IReadOnlyDictionary<string, TypeBehavior> behaviors,
            ValidationResult validation)
        {
            if (scenario?.particles == null || behaviors == null) return;

            var alreadyReported = new HashSet<string>();

            foreach (ParticleData particle in scenario.particles)
            {
                if (string.IsNullOrEmpty(particle?.type)) continue;

                if (behaviors.ContainsKey(particle.type)) continue;
                if (!alreadyReported.Add(particle.type)) continue;

                string nearMatch = FindCaseInsensitiveMatch(behaviors, particle.type);
                validation.AddError(nearMatch == null
                    ? $"Cenário: tipo de partícula '{particle.type}' (partícula id {particle.id}) não existe no arquivo de comportamento."
                    : $"Cenário: tipo de partícula '{particle.type}' (partícula id {particle.id}) não existe no arquivo de comportamento — o comportamento define '{nearMatch}', e a comparação diferencia maiúsculas de minúsculas.");
            }
        }

        private static string FindCaseInsensitiveMatch(IReadOnlyDictionary<string, TypeBehavior> behaviors, string type)
        {
            foreach (string declared in behaviors.Keys)
            {
                if (string.Equals(declared, type, System.StringComparison.OrdinalIgnoreCase))
                    return declared;
            }

            return null;
        }
    }
}
