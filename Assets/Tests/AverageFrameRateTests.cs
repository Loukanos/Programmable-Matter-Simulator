using NUnit.Framework;

namespace Tests
{
    // Frame times are binary fractions (0.5, 0.25, 0.125...) so every sum is exact and averages compare with EqualTo.
    public class AverageFrameRateTests
    {
        private static TestSimulation Load(int idleCyclesLimit = 0) => TestSimulation.Load(
            Json.ScenarioWithIdleLimit(3, idleCyclesLimit, (1, "M", 1, 1, 1)),
            Json.Of("{ `M`: { `onStep`: [ { `actions`: [ `wait` ] } ] } }"));

        private static void Frames(TestSimulation sim, int count, float deltaTime)
        {
            for (int i = 0; i < count; i++)
                sim.Metrics.Tick(deltaTime);
        }

        [Test]
        public void IsTheFramesWhileRunning_DividedByTheElapsedTime()
        {
            TestSimulation sim = Load();

            Frames(sim, 8, 0.125f);
            Frames(sim, 2, 0.5f);

            Assert.That(sim.Metrics.SimulationTime, Is.EqualTo(2.0));
            Assert.That(sim.Metrics.AverageFrameRate, Is.EqualTo(5.0));
        }

        [Test]
        public void BeforeTheFirstRun_NothingCounts_AndThereIsNoValue()
        {
            TestSimulation sim = Load();
            sim.Engine.Reset();

            Frames(sim, 5, 0.125f);

            Assert.That(sim.Metrics.SimulationTime, Is.EqualTo(0.0));
            Assert.That(sim.Metrics.AverageFrameRate, Is.Null);

            sim.Engine.Run();
            Frames(sim, 4, 0.25f);

            Assert.That(sim.Metrics.AverageFrameRate, Is.EqualTo(4.0));
        }

        [Test]
        public void PausedFrames_AreExcluded()
        {
            TestSimulation sim = Load();

            Frames(sim, 4, 0.25f);
            sim.Engine.Pause();
            Frames(sim, 100, 0.0625f);
            sim.Engine.Run();
            Frames(sim, 4, 0.5f);

            Assert.That(sim.Metrics.SimulationTime, Is.EqualTo(3.0));
            Assert.That(sim.Metrics.AverageFrameRate, Is.EqualTo(8 / 3.0));
        }

        [Test]
        public void FreezesWhenTheSimulationFinishes()
        {
            TestSimulation sim = Load(idleCyclesLimit: 2);

            for (int frame = 0; frame < 2; frame++)
            {
                sim.Metrics.Tick(0.5f);
                sim.Engine.Step();
            }
            Assert.That(sim.Engine.IsFinished, Is.True);

            Frames(sim, 10, 0.125f);
            sim.Engine.Run();
            Frames(sim, 10, 0.125f);

            Assert.That(sim.Metrics.SimulationTime, Is.EqualTo(1.0));
            Assert.That(sim.Metrics.AverageFrameRate, Is.EqualTo(2.0));
        }

        [Test]
        public void Reset_ClearsIt_LikeTheOtherIndicators()
        {
            TestSimulation sim = Load();
            Frames(sim, 4, 0.25f);

            sim.Engine.Reset();
            sim.Metrics.Reset();

            Assert.That(sim.Metrics.SimulationTime, Is.EqualTo(0.0));
            Assert.That(sim.Metrics.AverageFrameRate, Is.Null);

            sim.Engine.Run();
            Frames(sim, 2, 0.125f);

            Assert.That(sim.Metrics.AverageFrameRate, Is.EqualTo(8.0));
        }

        [Test]
        public void TimesTheElapsedTime_GivesBackTheFrameCount()
        {
            TestSimulation sim = Load();
            float[] irregular = { 0.016f, 0.033f, 0.0071f, 0.3333f, 0.021f };

            for (int i = 0; i < 1000; i++)
                sim.Metrics.Tick(irregular[i % irregular.Length]);

            Assert.That(sim.Metrics.AverageFrameRate * sim.Metrics.SimulationTime, Is.EqualTo(1000).Within(1e-9));
        }

        [Test]
        public void ElapsedTime_StaysAccurateOverALongFastRun()
        {
            TestSimulation sim = Load();
            const int frames = 800 * 1800;
            const float deltaTime = 1f / 800;

            Frames(sim, frames, deltaTime);

            Assert.That(sim.Metrics.SimulationTime, Is.EqualTo(frames * (double)deltaTime).Within(1e-6));
            Assert.That(sim.Metrics.AverageFrameRate, Is.EqualTo(frames / (frames * (double)deltaTime)).Within(1e-6));
        }
    }
}
