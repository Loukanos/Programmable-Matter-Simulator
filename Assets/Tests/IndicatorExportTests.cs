using System;
using System.IO;
using NUnit.Framework;
using ParticleSim.MetricsCalculator;
using UnityEngine;

namespace Tests
{
    public class IndicatorExportTests
    {
        private static readonly DateTime ClickTime = new DateTime(2026, 10, 1, 9, 30, 0);

        private static IndicatorSnapshot Capture(TestSimulation sim) =>
            sim.Metrics.Capture(ClickTime, "cenario.json", "comportamento.json", sim.Manager.Particles.Count);

        private static string[] Columns(IndicatorSnapshot snapshot) => IndicatorsCsv.FormatRow(snapshot).Split(';');

        [Test]
        public void Capture_ReflectsRunningPausedAndFinished_WithTheFinishReason()
        {
            TestSimulation sim = TestSimulation.Load(
                Json.ScenarioWithIdleLimit(3, 2, (1, "M", 1, 1, 1)),
                Json.Of("{ `M`: { `onStep`: [ { `actions`: [ `wait` ] } ] } }"));

            Assert.That(Columns(Capture(sim))[3], Is.EqualTo("rodando"));

            sim.Engine.Pause();
            Assert.That(Columns(Capture(sim))[3], Is.EqualTo("pausada"));
            Assert.That(Columns(Capture(sim))[4], Is.EqualTo(""));

            sim.Engine.Run();
            sim.Step(2);
            string[] finished = Columns(Capture(sim));
            Assert.That(finished[3], Is.EqualTo("encerrada"));
            Assert.That(finished[4], Is.EqualTo("sem atividade por 2 ciclo(s)"));

            sim.Metrics.Reset();
            Assert.That(sim.Metrics.FinishReason, Is.Null);
        }

        [Test]
        public void Snapshot_DescribesTheMomentOfTheClick_NotOfTheWrite()
        {
            TestSimulation sim = TestSimulation.Load(
                Json.Scenario(5, (1, "M", 0, 2, 2), (2, "N", 4, 4, 4)),
                Json.Of("{ `M`: { `onStep`: [ { `actions`: [ `move(X+)` ] } ] }, `N`: { } }"));
            sim.Step();

            IndicatorSnapshot atClick = Capture(sim);
            sim.Step(2);
            string[] columns = Columns(atClick);

            Assert.That(sim.Engine.CurrentCycle, Is.EqualTo(3));
            Assert.That(columns[0], Is.EqualTo("01/10/2026 09:30:00"));
            Assert.That(columns[1], Is.EqualTo("cenario.json"));
            Assert.That(columns[2], Is.EqualTo("comportamento.json"));
            Assert.That(columns[5], Is.EqualTo("2"));
            Assert.That(columns[6], Is.EqualTo("1"));
            Assert.That(columns[7], Is.EqualTo("1"));
            Assert.That(columns[8], Is.EqualTo("1"));
        }

        [Test]
        public void Capture_CarriesTheAverageFrameRate_InTheLastColumn()
        {
            TestSimulation sim = TestSimulation.Load(
                Json.Scenario(3, (1, "M", 1, 1, 1)),
                Json.Of("{ `M`: { `onStep`: [ { `actions`: [ `wait` ] } ] } }"));

            Assert.That(Columns(Capture(sim))[16], Is.EqualTo(""));

            for (int frame = 0; frame < 6; frame++)
                sim.Metrics.Tick(0.25f);

            Assert.That(Columns(Capture(sim))[13], Is.EqualTo("1,500"));
            Assert.That(Columns(Capture(sim))[16], Is.EqualTo("4,0"));
        }

        [Test]
        public void FailedWrite_DoesNotAffectTheSimulation()
        {
            TestSimulation sim = TestSimulation.Load(
                Json.Scenario(5, (1, "M", 0, 2, 2)),
                Json.Of("{ `M`: { `onStep`: [ { `actions`: [ `move(X+)` ] } ] } }"));
            sim.Step();

            string path = Path.Combine(Path.GetTempPath(), $"particlesim-locked-{Guid.NewGuid():N}.csv");
            File.WriteAllText(path, IndicatorsCsv.Header + "\r\n");
            try
            {
                using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    Assert.That(IndicatorsCsv.TryAppend(path, Capture(sim), out string error), Is.False);
                    Assert.That(error, Is.Not.Empty);
                }
            }
            finally
            {
                File.Delete(path);
            }

            sim.Step();

            Assert.That(sim.Engine.IsRunning, Is.True);
            Assert.That(sim.Engine.CurrentCycle, Is.EqualTo(2));
            Assert.That(sim.Particle(1).GridPosition, Is.EqualTo(new Vector3Int(2, 2, 2)));
            Assert.That(sim.Metrics.LastMovementCycle, Is.EqualTo(2));
        }
    }
}
