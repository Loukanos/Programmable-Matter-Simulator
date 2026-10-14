using NUnit.Framework;
using UnityEngine;

namespace Tests
{
    public class FieldSpellingTests
    {
        private static readonly string Behavior = Json.Of("{ `M`: { `onStep`: [ { `actions`: [ `wait` ] } ] } }");

        private static string Scenario(string fields) => Json.Of("{ " + fields + " }");

        private static string ValidParticles => "`particles`: [ { `id`: 1, `type`: `M`, `position`: [0, 0, 0] } ]";

        [TestCase("GridSize", "gridSize")]
        [TestCase("MaxCycles", "maxCycles")]
        [TestCase("IdleCyclesLimit", "idleCyclesLimit")]
        public void ScenarioField_WithOtherCasing_IsRejected_NamingTheCorrectSpelling(string written, string correct)
        {
            string value = correct == "gridSize" ? "[3, 3, 3]" : "10";
            string gridSize = correct == "gridSize" ? "" : "`gridSize`: [3, 3, 3], ";
            string errors = TestSimulation.ScenarioErrors(Scenario($"{gridSize}`{written}`: {value}, {ValidParticles}"), Behavior);

            Assert.That(errors, Does.Contain($"Cenário: campo \"{written}\" não reconhecido. Use \"{correct}\"."));
            Assert.That(errors, Does.Not.Contain("é obrigatório"));
        }

        [Test]
        public void MisspelledParticles_IsRejected_InsteadOfSkippingParticleValidation()
        {
            string scenario = Scenario("`gridSize`: [3, 3, 3], `Particles`: [ { `id`: 1, `type`: `M`, `position`: [9, 9, 9] }, { `id`: 2, `type`: `M`, `position`: [9, 9, 9] } ]");

            string errors = TestSimulation.ScenarioErrors(scenario, Behavior);

            Assert.That(errors, Does.Contain("Cenário: campo \"Particles\" não reconhecido. Use \"particles\"."));
        }

        [Test]
        public void MisspelledMaxCyclesHoldingText_GetsThePortugueseMessage_NotTheParserOne()
        {
            string errors = TestSimulation.ScenarioErrors(Scenario($"`gridSize`: [3, 3, 3], `MaxCycles`: `abc`, {ValidParticles}"), Behavior);

            Assert.That(errors, Does.Contain("Cenário: campo \"MaxCycles\" não reconhecido. Use \"maxCycles\"."));
            Assert.That(errors, Does.Not.Contain("Could not convert"));
            Assert.That(errors, Does.Not.Contain("erro ao interpretar"));
        }

        [TestCase("Id", "id", "Cenário: partícula #1")]
        [TestCase("Type", "type", "Cenário: partícula id 1")]
        [TestCase("Position", "position", "Cenário: partícula id 1")]
        public void ParticleField_WithOtherCasing_IsRejected_NamingTheCorrectSpelling(string written, string correct, string where)
        {
            string id = written == "Id" ? "`Id`: 1" : "`id`: 1";
            string type = written == "Type" ? "`Type`: `M`" : "`type`: `M`";
            string position = written == "Position" ? "`Position`: [0, 0, 0]" : "`position`: [0, 0, 0]";
            string scenario = Scenario($"`gridSize`: [3, 3, 3], `particles`: [ {{ {id}, {type}, {position} }} ]");

            string errors = TestSimulation.ScenarioErrors(scenario, Behavior);

            Assert.That(errors, Does.Contain($"{where}: campo \"{written}\" não reconhecido. Use \"{correct}\"."));
            Assert.That(errors, Does.Not.Contain("é obrigatório"));
        }

        [TestCase("hasNeighbor('X+')")]
        [TestCase("hasNeighbr('X+')")]
        public void MisspelledCondition_IsRejected_WhetherOrNotTheConditionIsValid(string condition)
        {
            string behavior = Json.Of($"{{ `M`: {{ `onStep`: [ {{ `Condition`: `{condition}`, `actions`: [ `move(Y+)` ] }} ] }} }}");

            string errors = TestSimulation.BehaviorErrors(Json.Scenario(3, (1, "M", 0, 0, 0)), behavior);

            Assert.That(errors, Does.Contain("Comportamento: tipo 'M', evento 'onStep', bloco 1: campo \"Condition\" não reconhecido. Use \"condition\"."));
        }

        [Test]
        public void MisspelledActions_IsRejected_WithOneMessage()
        {
            string behavior = Json.Of("{ `M`: { `onStep`: [ { `Actions`: [ `wait` ] } ] } }");

            string errors = TestSimulation.BehaviorErrors(Json.Scenario(3, (1, "M", 0, 0, 0)), behavior);

            Assert.That(errors, Does.Contain("Comportamento: tipo 'M', evento 'onStep', bloco 1: campo \"Actions\" não reconhecido. Use \"actions\"."));
            Assert.That(errors, Does.Not.Contain("é obrigatório"));
        }

        [Test]
        public void MisspelledDefinitions_IsRejected_WithoutReportingItsNamesAsUnknownFunctions()
        {
            string behavior = Json.Of("{ `Definitions`: { `livre`: `canMove('Y+')` }, `M`: { `onStep`: [ { `condition`: `livre`, `actions`: [ `move(Y+)` ] } ] } }");

            string errors = TestSimulation.BehaviorErrors(Json.Scenario(3, (1, "M", 0, 0, 0)), behavior);

            Assert.That(errors, Does.Contain("Comportamento: campo \"Definitions\" não reconhecido. Use \"definitions\"."));
            Assert.That(errors, Does.Not.Contain("função desconhecida"));
        }

        [Test]
        public void CorrectSpelling_StillLoadsAndRuns()
        {
            string scenario = Json.Of("{ `gridSize`: [3, 3, 3], `maxCycles`: 2, `idleCyclesLimit`: 0, `particles`: [ { `id`: 1, `type`: `M`, `position`: [0, 0, 0] } ] }");
            string behavior = Json.Of("{ `definitions`: { `livre`: `canMove('Y+')` }, `M`: { `onStep`: [ { `condition`: `livre`, `actions`: [ `move(Y+)` ] } ] } }");

            TestSimulation sim = TestSimulation.Load(scenario, behavior);
            sim.Step(2);

            Assert.That(sim.Particle(1).GridPosition, Is.EqualTo(new Vector3Int(0, 2, 0)));
            Assert.That(sim.Engine.IsFinished, Is.True);
        }
    }
}
