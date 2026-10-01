using System.Linq;
using NUnit.Framework;
using ParticleSim.Core;
using UnityEngine;

namespace Tests
{
    public class MessageDirectionTests
    {
        private const int Center = 1;

        private static Vector3Int CenterOffset(string direction)
        {
            LanguageVocabulary.TryGetDirectionDelta(direction, out Vector3Int delta);
            return new Vector3Int(Center, Center, Center) + delta;
        }

        private static (int, string, int, int, int) At(int id, string type, Vector3Int p) => (id, type, p.x, p.y, p.z);

        [TestCase("X+", "X-")]
        [TestCase("X-", "X+")]
        [TestCase("Y+", "Y-")]
        [TestCase("Y-", "Y+")]
        [TestCase("Z+", "Z-")]
        [TestCase("Z-", "Z+")]
        public void DirectedSend_ReturnsTheArrivalSide_AndAgreesWithTheDirectionalEvent(string sendDirection, string expectedSide)
        {
            string eventHandlers = string.Join(", ", LanguageVocabulary.Directions.Select(d =>
                $"`onMessage({d})`: [ {{ `actions`: [ `setState('eventSide', '{d}')`, `setState('functionSide', messageDirection())` ] }} ]"));
            string scenario = Json.Scenario(3, (1, "S", Center, Center, Center), At(2, "R", CenterOffset(sendDirection)));
            string behavior = Json.Of(
                $"{{ `S`: {{ `onInit`: [ {{ `actions`: [ `sendMessage('{sendDirection}', 'PING')` ] }} ] }}, `R`: {{ {eventHandlers} }} }}");

            TestSimulation sim = TestSimulation.Load(scenario, behavior);
            sim.Step();

            Assert.That(sim.State(2, "functionSide"), Is.EqualTo(expectedSide));
            Assert.That(sim.State(2, "eventSide"), Is.EqualTo(expectedSide));
        }

        [Test]
        public void Broadcast_EachNeighborGetsItsOwnSide()
        {
            string scenario = Json.Scenario(3,
                (1, "S", Center, Center, Center),
                At(2, "R", CenterOffset("X-")),
                At(3, "R", CenterOffset("Y+")),
                At(4, "R", CenterOffset("Z-")));
            string behavior = Json.Of(
                "{ `S`: { `onInit`: [ { `actions`: [ `sendMessage('PING')` ] } ] }, " +
                "`R`: { `onMessage`: [ { `actions`: [ `setState('side', messageDirection())` ] } ] } }");

            TestSimulation sim = TestSimulation.Load(scenario, behavior);
            sim.Step();

            Assert.That(sim.State(2, "side"), Is.EqualTo("X+"));
            Assert.That(sim.State(3, "side"), Is.EqualTo("Y-"));
            Assert.That(sim.State(4, "side"), Is.EqualTo("Z+"));
        }

        [Test]
        public void StoredDirection_RepliesToTheOriginalSenderOnly()
        {
            string scenario = Json.Scenario(3,
                (1, "S", Center, Center, Center),
                At(7, "R", CenterOffset("X+")),
                (9, "D", 2, 1, 0));
            string behavior = Json.Of(
                "{ `S`: { `onInit`: [ { `actions`: [ `sendMessage('X+', 'GROW')` ] } ], " +
                "         `onMessage`: [ { `condition`: `checkMessage('BACK')`, `actions`: [ `setState('reply', messageValue())`, `setState('replySide', messageDirection())` ] } ] }, " +
                "  `R`: { `onMessage`: [ { `condition`: `checkMessage('GROW')`, `actions`: [ `setState('pai', messageDirection())`, `sendMessage(state('pai'), 'BACK', myId())` ] } ] }, " +
                "  `D`: { `onMessage`: [ { `actions`: [ `setState('heard', true)` ] } ] } }");

            TestSimulation sim = TestSimulation.Load(scenario, behavior);
            sim.Step(2);

            Assert.That(sim.State(7, "pai"), Is.EqualTo("X-"));
            Assert.That(sim.State(1, "reply"), Is.EqualTo(7.0));
            Assert.That(sim.State(1, "replySide"), Is.EqualTo("X+"));
            Assert.That(sim.State(9, "heard"), Is.Null);
        }

        [Test]
        public void TwoArgumentSend_WithMessageDirection_IsDirected()
        {
            string scenario = Json.Scenario(3,
                (1, "S", Center, Center, Center),
                At(2, "R", CenterOffset("Y+")),
                (3, "S", 1, 2, 0));
            string behavior = Json.Of(
                "{ `S`: { `onInit`: [ { `actions`: [ `sendMessage('Y+', 'PING')` ] } ], " +
                "         `onMessage`: [ { `condition`: `checkMessage('ACK')`, `actions`: [ `setState('acked', true)` ] } ] }, " +
                "  `R`: { `onMessage`: [ { `condition`: `checkMessage('PING')`, `actions`: [ `sendMessage(messageDirection(), 'ACK')` ] } ] } }");

            TestSimulation sim = TestSimulation.Load(scenario, behavior);
            sim.Step(2);

            Assert.That(sim.State(1, "acked"), Is.EqualTo(true));
            Assert.That(sim.State(3, "acked"), Is.Null);
        }

        [Test]
        public void ComparedInCheckState_AgainstAStoredLiteral()
        {
            string scenario = Json.Scenario(3, (1, "S", Center, Center, Center), At(2, "R", CenterOffset("X+")));
            string behavior = Json.Of(
                "{ `S`: { `onInit`: [ { `actions`: [ `sendMessage('X+', 'PING')` ] } ] }, " +
                "  `R`: { `onInit`: [ { `actions`: [ `setState('expected', 'X-')`, `setState('wrong', 'Y+')` ] } ], " +
                "         `onMessage`: [ { `condition`: `checkState('expected', '==', messageDirection())`, `actions`: [ `setState('match', true)` ] }, " +
                "                        { `condition`: `checkState('wrong', '==', messageDirection())`, `actions`: [ `setState('falseMatch', true)` ] } ] } }");

            TestSimulation sim = TestSimulation.Load(scenario, behavior);
            sim.Step();

            Assert.That(sim.State(2, "match"), Is.EqualTo(true));
            Assert.That(sim.State(2, "falseMatch"), Is.Null);
        }

        [Test]
        public void WorksAsADirectionForOppositeMoveHasNeighborAndCanMove()
        {
            string scenario = Json.Scenario(5, (1, "S", 1, 2, 2), (2, "R", 2, 2, 2));
            string behavior = Json.Of(
                "{ `S`: { `onInit`: [ { `actions`: [ `sendMessage('X+', 'PING')` ] } ] }, " +
                "  `R`: { `onMessage`: [ { `actions`: [ " +
                "    `setState('senderThere', hasNeighbor(messageDirection()))`, " +
                "    `setState('canGoBack', canMove(messageDirection()))`, " +
                "    `setState('away', opposite(messageDirection()))`, " +
                "    `move(opposite(messageDirection()))` ] } ] } }");

            TestSimulation sim = TestSimulation.Load(scenario, behavior);
            sim.Step();

            Assert.That(sim.State(2, "senderThere"), Is.EqualTo(true));
            Assert.That(sim.State(2, "canGoBack"), Is.EqualTo(false));
            Assert.That(sim.State(2, "away"), Is.EqualTo("X+"));
            Assert.That(sim.Particle(2).GridPosition, Is.EqualTo(new Vector3Int(3, 2, 2)));
        }

        [Test]
        public void SideIsFixedAtSendTime_EvenIfTheSenderMovesBeforeDelivery()
        {
            string scenario = Json.Scenario(3, (1, "S", Center, Center, Center), At(2, "R", CenterOffset("X+")));
            string behavior = Json.Of(
                "{ `S`: { `onStep`: [ { `condition`: `checkState('sent', '!=', true)`, `actions`: [ `sendMessage('X+', 'PING')`, `setState('sent', true)`, `move(Y+)` ] } ] }, " +
                "  `R`: { `onMessage`: [ { `actions`: [ `setState('side', messageDirection())`, `setState('senderStillThere', hasNeighbor(messageDirection()))` ] } ] } }");

            TestSimulation sim = TestSimulation.Load(scenario, behavior);
            sim.Step(2);

            Assert.That(sim.Particle(1).GridPosition, Is.EqualTo(new Vector3Int(1, 2, 1)));
            Assert.That(sim.State(2, "side"), Is.EqualTo("X-"));
            Assert.That(sim.State(2, "senderStillThere"), Is.EqualTo(false));
        }

        [TestCase("`onStep`: [ { `actions`: [ `setState('x', messageDirection())` ] } ]")]
        [TestCase("`onInit`: [ { `actions`: [ `setState('x', messageDirection())` ] } ]")]
        [TestCase("`onTouch`: [ { `actions`: [ `sendMessage(messageDirection(), 'T')` ] } ]")]
        [TestCase("`onStep`: [ { `condition`: `checkState('x', '==', messageDirection())`, `actions`: [ `wait` ] } ]")]
        [TestCase("`onStep`: [ { `actions`: [ `move(opposite(messageDirection()))` ] } ]")]
        public void OutsideOnMessage_IsRejectedAtLoad(string eventJson)
        {
            string behavior = Json.Of($"{{ `M`: {{ {eventJson} }} }}");
            string errors = TestSimulation.BehaviorErrors(Json.Scenario(3, (1, "M", 1, 1, 1)), behavior);
            StringAssert.Contains("messageDirection() só pode ser usada em blocos onMessage", errors);
        }

        [Test]
        public void DefinitionUsingIt_IsAccepted_OnlyWhenReferencedFromOnMessage()
        {
            const string definitions = "`definitions`: { `fromParent`: `checkState('pai', '==', messageDirection())`, `wrapper`: `fromParent Or false` }";
            string scenario = Json.Scenario(3, (1, "M", 1, 1, 1));

            string inOnMessage = Json.Of($"{{ {definitions}, `M`: {{ `onMessage`: [ {{ `condition`: `wrapper`, `actions`: [ `wait` ] }} ] }} }}");
            TestSimulation.Load(scenario, inOnMessage);

            string inOnStep = Json.Of($"{{ {definitions}, `M`: {{ `onStep`: [ {{ `condition`: `wrapper`, `actions`: [ `wait` ] }} ] }} }}");
            string errors = TestSimulation.BehaviorErrors(scenario, inOnStep);
            StringAssert.Contains("'wrapper', que depende de messageDirection()", errors);
        }

        [Test]
        public void SignBeforeIt_IsRejected_BecauseItIsNotANumber()
        {
            string behavior = Json.Of("{ `M`: { `onMessage`: [ { `actions`: [ `setState('x', +messageDirection())` ] } ] } }");
            string errors = TestSimulation.BehaviorErrors(Json.Scenario(3, (1, "M", 1, 1, 1)), behavior);
            StringAssert.Contains("não produz um número", errors);
        }
    }
}
