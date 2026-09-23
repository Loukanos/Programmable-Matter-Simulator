using System.Collections.Generic;
using ParticleSim.Core;

namespace ParticleSim.InputValidation
{
    // Only Load() can create one, so holding an instance proves validation ran. Keep the constructor private.
    public sealed class ValidatedSimulation
    {
        public ScenarioData Scenario { get; }
        public Dictionary<string, TypeBehavior> Behaviors { get; }

        private ValidatedSimulation(ScenarioData scenario, Dictionary<string, TypeBehavior> behaviors)
        {
            Scenario = scenario;
            Behaviors = behaviors;
        }

        public static ValidatedSimulation Load(
            string scenarioPath,
            string behaviorPath,
            out ValidationResult scenarioValidation,
            out ValidationResult behaviorValidation)
        {
            ScenarioData scenario = ScenarioLoader.LoadFromFile(scenarioPath, out scenarioValidation);
            Dictionary<string, TypeBehavior> behaviors = BehaviorLoader.LoadFromFile(behaviorPath, out behaviorValidation);

            if (scenario == null || behaviors == null)
                return null;

            CrossValidator.Validate(scenario, behaviors, scenarioValidation);
            if (!scenarioValidation.IsValid)
                return null;

            return new ValidatedSimulation(scenario, behaviors);
        }
    }
}
