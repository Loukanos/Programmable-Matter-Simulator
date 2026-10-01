using NUnit.Framework;

namespace Tests
{
    public class LastMovementCycleTests
    {
        private static TestSimulation Load(string behavior, params (int id, string type, int x, int y, int z)[] particles) =>
            TestSimulation.Load(Json.Scenario(3, particles), Json.Of(behavior));

        [Test]
        public void IsEmpty_BeforeAnyMovement()
        {
            TestSimulation sim = Load("{ `M`: { `onStep`: [ { `actions`: [ `wait` ] } ] } }", (1, "M", 1, 1, 1));
            sim.Step(3);
            Assert.That(sim.Metrics.LastMovementCycle, Is.Null);
        }

        [Test]
        public void TracksTheCycleOfTheLatestAppliedMove()
        {
            TestSimulation sim = Load(
                "{ `M`: { `onInit`: [ { `actions`: [ `setState('n', 0)` ] } ], " +
                "         `onStep`: [ { `condition`: `checkState('n', '<', 2)`, `actions`: [ `setState('n', +1)`, `move(X+)` ] } ] } }",
                (1, "M", 0, 1, 1));

            sim.Step(5);

            Assert.That(sim.Metrics.CurrentCycle, Is.EqualTo(5));
            Assert.That(sim.Metrics.LastMovementCycle, Is.EqualTo(2));
        }

        [Test]
        public void MatchesTheCyclesIndicator_WhenTheMoveHappensInTheLatestCycle()
        {
            TestSimulation sim = Load("{ `M`: { `onStep`: [ { `actions`: [ `move(X+)` ] } ] } }", (1, "M", 0, 1, 1));

            sim.Step();

            Assert.That(sim.Engine.CurrentCycle, Is.EqualTo(1));
            Assert.That(sim.Metrics.CurrentCycle, Is.EqualTo(1));
            Assert.That(sim.Metrics.LastMovementCycle, Is.EqualTo(1));
        }

        [Test]
        public void IgnoresMovesCancelledByASharedDestination()
        {
            TestSimulation sim = Load(
                "{ `A`: { `onStep`: [ { `actions`: [ `move(X+)` ] } ] }, `B`: { `onStep`: [ { `actions`: [ `move(X-)` ] } ] } }",
                (1, "A", 0, 1, 1), (2, "B", 2, 1, 1));

            sim.Step(3);

            Assert.That(sim.Metrics.TotalCollisions, Is.GreaterThan(0));
            Assert.That(sim.Metrics.LastMovementCycle, Is.Null);
        }

        [Test]
        public void IgnoresMovesBlockedByAStationaryParticle()
        {
            TestSimulation sim = Load(
                "{ `A`: { `onStep`: [ { `actions`: [ `move(X+)` ] } ] }, `W`: { `onStep`: [ { `actions`: [ `wait` ] } ] } }",
                (1, "A", 0, 1, 1), (2, "W", 1, 1, 1));

            sim.Step(3);

            Assert.That(sim.Metrics.TotalCollisions, Is.GreaterThan(0));
            Assert.That(sim.Metrics.LastMovementCycle, Is.Null);
        }

        [Test]
        public void IgnoresInvalidMovesOutOfTheGrid()
        {
            TestSimulation sim = Load("{ `M`: { `onStep`: [ { `actions`: [ `move(X+)` ] } ] } }", (1, "M", 2, 1, 1));

            sim.Step(3);

            Assert.That(sim.Metrics.TotalInvalidMovements, Is.EqualTo(3));
            Assert.That(sim.Metrics.LastMovementCycle, Is.Null);
        }

        [Test]
        public void IsClearedByReset()
        {
            TestSimulation sim = Load("{ `M`: { `onStep`: [ { `actions`: [ `move(X+)` ] } ] } }", (1, "M", 0, 1, 1));
            sim.Step();
            Assert.That(sim.Metrics.LastMovementCycle, Is.EqualTo(1));

            sim.Metrics.Reset();

            Assert.That(sim.Metrics.LastMovementCycle, Is.Null);
        }
    }
}
