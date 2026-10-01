using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ParticleSim.Core;
using ParticleSim.InputValidation;

namespace Tests
{
    internal sealed class TestSimulation
    {
        public ParticleSim.ParticleManager.ParticleManager Manager { get; } = new ParticleSim.ParticleManager.ParticleManager();
        public ParticleSim.SimulationEngine.SimulationEngine Engine { get; }
        public List<Message> Sent { get; } = new List<Message>();

        private TestSimulation()
        {
            Engine = new ParticleSim.SimulationEngine.SimulationEngine(Manager);
            Engine.OnMessageSent += Sent.Add;
        }

        public static TestSimulation Load(string scenarioJson, string behaviorJson)
        {
            ValidatedSimulation loaded = Validate(scenarioJson, behaviorJson, out ValidationResult scenario, out ValidationResult behavior);
            if (loaded == null)
                Assert.Fail($"Load failed.\nScenario: {scenario.ToMessage(50)}\nBehavior: {behavior.ToMessage(50)}");

            var simulation = new TestSimulation();
            simulation.Manager.BuildFromScenario(loaded);
            simulation.Engine.SetBehaviors(loaded.Behaviors);
            simulation.Engine.MaxCycles = loaded.Scenario.maxCycles;
            simulation.Engine.IdleCyclesLimit = loaded.Scenario.idleCyclesLimit;
            simulation.Engine.Reset();
            simulation.Engine.ExecuteOnInit();
            simulation.Engine.Run();
            return simulation;
        }

        public static TestSimulation RunSingleParticle(string onInitActions, string onStepActions, int cycles = 1)
        {
            string onInit = onInitActions == null ? "" : $"`onInit`: [ {{ `actions`: [ {onInitActions} ] }} ], ";
            string behavior = Json.Of($"{{ `M`: {{ {onInit}`onStep`: [ {{ `actions`: [ {onStepActions} ] }} ] }} }}");
            TestSimulation simulation = Load(Json.Scenario(3, (1, "M", 1, 1, 1)), behavior);
            simulation.Step(cycles);
            return simulation;
        }

        public static string BehaviorErrors(string scenarioJson, string behaviorJson)
        {
            ValidatedSimulation loaded = Validate(scenarioJson, behaviorJson, out _, out ValidationResult behavior);
            Assert.IsNull(loaded, "Expected the load to be rejected.");
            return behavior.ToMessage(50);
        }

        public void Step(int cycles = 1)
        {
            for (int i = 0; i < cycles; i++)
                Engine.Step();
        }

        public Particle Particle(int id) => Manager.Particles.Single(p => p.Id == id);

        public object State(int id, string key) => Particle(id).GetInternalState(key);

        private static ValidatedSimulation Validate(string scenarioJson, string behaviorJson,
            out ValidationResult scenario, out ValidationResult behavior)
        {
            string scenarioPath = Path.Combine(Path.GetTempPath(), $"particlesim-test-{Guid.NewGuid():N}-scenario.json");
            string behaviorPath = Path.Combine(Path.GetTempPath(), $"particlesim-test-{Guid.NewGuid():N}-behavior.json");
            try
            {
                File.WriteAllText(scenarioPath, scenarioJson);
                File.WriteAllText(behaviorPath, behaviorJson);
                return ValidatedSimulation.Load(scenarioPath, behaviorPath, out scenario, out behavior);
            }
            finally
            {
                File.Delete(scenarioPath);
                File.Delete(behaviorPath);
            }
        }
    }

    internal static class Json
    {
        // Backticks stand for JSON double quotes, so behavior expressions keep their own single quotes readable.
        public static string Of(string text) => text.Replace('`', '"');

        public static string Scenario(int size, params (int id, string type, int x, int y, int z)[] particles) =>
            ScenarioWithIdleLimit(size, 0, particles);

        public static string ScenarioWithIdleLimit(int size, int idleCyclesLimit, params (int id, string type, int x, int y, int z)[] particles)
        {
            IEnumerable<string> entries = particles.Select(p =>
                $"{{\"id\": {p.id}, \"type\": \"{p.type}\", \"position\": [{p.x}, {p.y}, {p.z}]}}");
            return $"{{\"gridSize\": [{size}, {size}, {size}], \"maxCycles\": 0, \"idleCyclesLimit\": {idleCyclesLimit}, \"particles\": [{string.Join(", ", entries)}]}}";
        }
    }
}
