using System.Linq;
using NUnit.Framework;
using ParticleSim.Core;
using UnityEngine;

namespace Tests
{
    public class MessageSideTests
    {
        private const int Center = 1;

        private static Vector3Int CenterOffset(string direction)
        {
            LanguageVocabulary.TryGetDirectionDelta(direction, out Vector3Int delta);
            return new Vector3Int(Center, Center, Center) + delta;
        }

        private static string ReceiverRecordingItsArrivalSide() =>
            string.Join(", ", LanguageVocabulary.Directions.Select(d =>
                $"`onMessage({d})`: [ {{ `actions`: [ `setState('side', '{d}')` ] }} ]"));

        [TestCase("X+", "X-")]
        [TestCase("X-", "X+")]
        [TestCase("Y+", "Y-")]
        [TestCase("Y-", "Y+")]
        [TestCase("Z+", "Z-")]
        [TestCase("Z-", "Z+")]
        public void DirectedSend_ArrivesOnTheOppositeSide(string sendDirection, string expectedSide)
        {
            Vector3Int receiver = CenterOffset(sendDirection);
            string scenario = Json.Scenario(3, (1, "S", Center, Center, Center), (2, "R", receiver.x, receiver.y, receiver.z));
            string behavior = Json.Of(
                $"{{ `S`: {{ `onInit`: [ {{ `actions`: [ `sendMessage('{sendDirection}', 'PING')` ] }} ] }}, " +
                $"`R`: {{ {ReceiverRecordingItsArrivalSide()} }} }}");

            TestSimulation sim = TestSimulation.Load(scenario, behavior);
            sim.Step();

            Assert.That(sim.State(2, "side"), Is.EqualTo(expectedSide));
        }

        [Test]
        public void Broadcast_EachNeighborReceivesItsOwnSide()
        {
            Vector3Int east = CenterOffset("X+"), below = CenterOffset("Y-"), front = CenterOffset("Z+");
            string scenario = Json.Scenario(3,
                (1, "S", Center, Center, Center),
                (2, "R", east.x, east.y, east.z),
                (3, "R", below.x, below.y, below.z),
                (4, "R", front.x, front.y, front.z));
            string behavior = Json.Of(
                "{ `S`: { `onInit`: [ { `actions`: [ `sendMessage('PING')` ] } ] }, " +
                $"`R`: {{ {ReceiverRecordingItsArrivalSide()} }} }}");

            TestSimulation sim = TestSimulation.Load(scenario, behavior);
            sim.Step();

            Assert.That(sim.State(2, "side"), Is.EqualTo("X-"));
            Assert.That(sim.State(3, "side"), Is.EqualTo("Y+"));
            Assert.That(sim.State(4, "side"), Is.EqualTo("Z-"));
        }
    }
}
