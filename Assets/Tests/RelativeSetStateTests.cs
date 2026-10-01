using NUnit.Framework;

namespace Tests
{
    public class RelativeSetStateTests
    {
        private const string TwoVariables = "`setState('a', 10)`, `setState('b', 3)`";

        [Test]
        public void Plus_AddsAnotherVariable()
        {
            TestSimulation sim = TestSimulation.RunSingleParticle(TwoVariables, "`setState('a', +state('b'))`");
            Assert.That(sim.State(1, "a"), Is.EqualTo(13.0));
        }

        [Test]
        public void Minus_SubtractsAnotherVariable()
        {
            TestSimulation sim = TestSimulation.RunSingleParticle(TwoVariables, "`setState('a', -state('b'))`");
            Assert.That(sim.State(1, "a"), Is.EqualTo(7.0));
        }

        [Test]
        public void Result_CanBeNegative()
        {
            TestSimulation sim = TestSimulation.RunSingleParticle("`setState('a', 2)`, `setState('b', 5)`", "`setState('a', -state('b'))`");
            Assert.That(sim.State(1, "a"), Is.EqualTo(-3.0));
        }

        [Test]
        public void MissingTarget_StartsFromZero()
        {
            TestSimulation sim = TestSimulation.RunSingleParticle(TwoVariables, "`setState('z', -state('b'))`");
            Assert.That(sim.State(1, "z"), Is.EqualTo(-3.0));
        }

        [Test]
        public void MissingOperand_IsIgnored_AndDoesNotCreateTheTarget()
        {
            TestSimulation sim = TestSimulation.RunSingleParticle(TwoVariables,
                "`setState('a', +state('missing'))`, `setState('n', +state('missing'))`");
            Assert.That(sim.State(1, "a"), Is.EqualTo(10.0));
            Assert.That(sim.State(1, "n"), Is.Null);
        }

        [Test]
        public void NonNumericOperand_IsIgnored()
        {
            TestSimulation sim = TestSimulation.RunSingleParticle("`setState('a', 10)`, `setState('flag', true)`, `setState('word', 'abc')`",
                "`setState('a', +state('flag'))`, `setState('a', -state('word'))`");
            Assert.That(sim.State(1, "a"), Is.EqualTo(10.0));
        }

        [Test]
        public void NonNumericTarget_IsIgnored_LikeWithALiteral()
        {
            TestSimulation sim = TestSimulation.RunSingleParticle("`setState('a', 'abc')`, `setState('b', 3)`", "`setState('a', +state('b'))`");
            Assert.That(sim.State(1, "a"), Is.EqualTo("abc"));
        }

        [Test]
        public void ChainedOperations_SeeEarlierResultsOfTheSameBlock()
        {
            TestSimulation sim = TestSimulation.RunSingleParticle(TwoVariables,
                "`setState('b', +1)`, `setState('a', -state('b'))`, `setState('a', +state('a'))`");
            Assert.That(sim.State(1, "b"), Is.EqualTo(4.0));
            Assert.That(sim.State(1, "a"), Is.EqualTo(12.0));
        }

        [Test]
        public void MyIdAndCountNeighbors_CanBeOperands()
        {
            string scenario = Json.Scenario(3, (5, "M", 1, 1, 1), (6, "N", 2, 1, 1));
            string behavior = Json.Of(
                "{ `M`: { `onStep`: [ { `actions`: [ `setState('s', +myId())`, `setState('s', +countNeighbors())` ] } ] }, `N`: { } }");

            TestSimulation sim = TestSimulation.Load(scenario, behavior);
            sim.Step();

            Assert.That(sim.State(5, "s"), Is.EqualTo(6.0));
        }

        [Test]
        public void MessageValue_InsideOnMessage_AddsThePayload()
        {
            TestSimulation sim = MessageScenario("`condition`: `checkMessage('N')`, ");
            Assert.That(sim.State(2, "d"), Is.EqualTo(5.0));
        }

        [Test]
        public void MessageValue_WithoutAMatchingCheckMessage_IsIgnored()
        {
            TestSimulation sim = MessageScenario("");
            Assert.That(sim.State(2, "d"), Is.EqualTo(1.0));
        }

        private static TestSimulation MessageScenario(string receiverCondition)
        {
            string scenario = Json.Scenario(3, (1, "S", 1, 1, 1), (2, "R", 2, 1, 1));
            string behavior = Json.Of(
                "{ `S`: { `onInit`: [ { `actions`: [ `sendMessage('X+', 'N', 4)` ] } ] }, " +
                "`R`: { `onInit`: [ { `actions`: [ `setState('d', 1)` ] } ], " +
                $"`onMessage`: [ {{ {receiverCondition}`actions`: [ `setState('d', +messageValue())` ] }} ] }} }}");

            TestSimulation sim = TestSimulation.Load(scenario, behavior);
            sim.Step();
            return sim;
        }

        [Test]
        public void IgnoredOperation_DoesNotCountAsActivity()
        {
            string ignored = Json.Of("{ `M`: { `onStep`: [ { `actions`: [ `setState('a', +state('missing'))` ] } ] } }");
            TestSimulation idle = TestSimulation.Load(Json.ScenarioWithIdleLimit(3, 2, (1, "M", 1, 1, 1)), ignored);
            idle.Step(2);
            Assert.That(idle.Engine.IsFinished, Is.True);

            string applied = Json.Of("{ `M`: { `onInit`: [ { `actions`: [ `setState('b', 0)` ] } ], `onStep`: [ { `actions`: [ `setState('a', +state('b'))` ] } ] } }");
            TestSimulation active = TestSimulation.Load(Json.ScenarioWithIdleLimit(3, 2, (1, "M", 1, 1, 1)), applied);
            active.Step(3);
            Assert.That(active.Engine.IsFinished, Is.False);
        }

        [TestCase("`actions`: [ `setState('a', +opposite(X+))` ]", "não produz um número")]
        [TestCase("`actions`: [ `setState('a', -canMove(X+))` ]", "não produz um número")]
        [TestCase("`actions`: [ `setState('a', +moveResult())` ]", "não produz um número")]
        [TestCase("`actions`: [ `sendMessage('T', +state('b'))` ]", "só é permitido no valor do setState")]
        [TestCase("`actions`: [ `setState('a', state(+state('b')))` ]", "só é permitido no valor do setState")]
        [TestCase("`condition`: `checkState('a', '==', +state('b'))`, `actions`: [ `wait` ]", "só é permitido no valor do setState")]
        [TestCase("`actions`: [ `setState('a', +'5')` ]", "só pode preceder uma função de leitura")]
        [TestCase("`actions`: [ `setState('a', +b)` ]", "só pode preceder uma função de leitura")]
        [TestCase("`actions`: [ `setState('a', +-state('b'))` ]", "só pode preceder uma função de leitura")]
        public void InvalidUseOfTheSign_IsRejectedAtLoad(string block, string expectedError)
        {
            string behavior = Json.Of($"{{ `M`: {{ `onStep`: [ {{ {block} }} ] }} }}");
            string errors = TestSimulation.BehaviorErrors(Json.Scenario(3, (1, "M", 1, 1, 1)), behavior);
            StringAssert.Contains(expectedError, errors);
        }
    }
}
