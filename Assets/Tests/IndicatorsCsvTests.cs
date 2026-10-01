using System;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using ParticleSim.MetricsCalculator;

namespace Tests
{
    public class IndicatorsCsvTests
    {
        private string _directory;
        private string _path;

        [SetUp]
        public void CreateDirectory()
        {
            _directory = Path.Combine(Path.GetTempPath(), $"particlesim-csv-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_directory);
            _path = Path.Combine(_directory, "indicadores.csv");
        }

        [TearDown]
        public void DeleteDirectory() => Directory.Delete(_directory, recursive: true);

        private static IndicatorSnapshot Snapshot(SimulationRunState state = SimulationRunState.Paused, string reason = null) => new IndicatorSnapshot
        {
            Timestamp = new DateTime(2026, 10, 1, 14, 5, 9),
            ScenarioFile = "cenario_fluxoMaximo.json",
            BehaviorFile = "comportamento_fluxoMaximo.json",
            State = state,
            FinishReason = reason,
            ParticleCount = 1000,
            Cycles = 24500,
            LastMovementCycle = 23871,
            Movements = 7934,
            Collisions = 12,
            InvalidMovements = 3,
            MessagesSent = 92248398,
            MessagesDelivered = 81144124,
            SimulationTimeSeconds = 1234.5678,
            AverageStepTimeMs = 0.1234,
            FrameRate = 59.94,
        };

        private string[] Lines() => File.ReadAllLines(_path, Encoding.UTF8);

        private static string[] Columns(string line) => line.Split(';');

        [Test]
        public void NewFile_GetsTheHeaderOnceAndTheRow_InUtf8WithBom()
        {
            Assert.That(IndicatorsCsv.TryAppend(_path, Snapshot(), out string error), Is.True, error);

            string[] lines = Lines();
            Assert.That(lines.Length, Is.EqualTo(2));
            Assert.That(lines[0], Is.EqualTo(IndicatorsCsv.Header));
            byte[] bytes = File.ReadAllBytes(_path);
            Assert.That(bytes.Take(3), Is.EqualTo(new byte[] { 0xEF, 0xBB, 0xBF }));
        }

        [Test]
        public void ExistingFile_GetsOneMoreRow_WithoutRepeatingHeaderOrBom()
        {
            IndicatorsCsv.TryAppend(_path, Snapshot(), out _);
            IndicatorsCsv.TryAppend(_path, Snapshot(SimulationRunState.Running), out _);
            IndicatorsCsv.TryAppend(_path, Snapshot(SimulationRunState.Finished, "atingiu o limite de 24500 ciclos"), out _);

            string[] lines = Lines();
            Assert.That(lines.Length, Is.EqualTo(4));
            Assert.That(lines.Count(l => l == IndicatorsCsv.Header), Is.EqualTo(1));
            byte[] bytes = File.ReadAllBytes(_path);
            int boms = Enumerable.Range(0, bytes.Length - 2).Count(i => bytes[i] == 0xEF && bytes[i + 1] == 0xBB && bytes[i + 2] == 0xBF);
            Assert.That(boms, Is.EqualTo(1));
        }

        [Test]
        public void ExistingFileWithoutFinalLineBreak_StillGetsTheRowOnItsOwnLine()
        {
            File.WriteAllText(_path, IndicatorsCsv.Header + "\r\n" + IndicatorsCsv.FormatRow(Snapshot()), new UTF8Encoding(false));

            Assert.That(IndicatorsCsv.TryAppend(_path, Snapshot(SimulationRunState.Running), out string error), Is.True, error);

            string[] lines = Lines();
            Assert.That(lines.Length, Is.EqualTo(3));
            Assert.That(Columns(lines[2])[3], Is.EqualTo("rodando"));
        }

        [Test]
        public void EmptyExistingFile_GetsTheHeader()
        {
            File.WriteAllText(_path, "");

            Assert.That(IndicatorsCsv.TryAppend(_path, Snapshot(), out string error), Is.True, error);

            Assert.That(Lines()[0], Is.EqualTo(IndicatorsCsv.Header));
        }

        [Test]
        public void FileWithAnotherHeader_IsRefusedAndLeftUntouched()
        {
            const string foreign = "a;b;c\r\n1;2;3\r\n";
            File.WriteAllText(_path, foreign);

            Assert.That(IndicatorsCsv.TryAppend(_path, Snapshot(), out string error), Is.False);

            Assert.That(error, Is.EqualTo(IndicatorsCsv.ForeignFileError));
            Assert.That(File.ReadAllText(_path), Is.EqualTo(foreign));
        }

        [Test]
        public void FileResavedByExcelInTheLegacyEncoding_StillAccepted_AndKeptInThatEncoding()
        {
            Encoding latin1 = Encoding.GetEncoding(28591);
            File.WriteAllText(_path, IndicatorsCsv.Header + "\r\n", latin1);

            Assert.That(IndicatorsCsv.TryAppend(_path, Snapshot(), out string error), Is.True, error);

            string[] lines = File.ReadAllLines(_path, latin1);
            Assert.That(lines.Length, Is.EqualTo(2));
            Assert.That(lines[0], Is.EqualTo(IndicatorsCsv.Header));
        }

        [Test]
        public void LockedFile_ReturnsAnErrorInsteadOfThrowing()
        {
            IndicatorsCsv.TryAppend(_path, Snapshot(), out _);

            using (new FileStream(_path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                bool ok = true;
                string error = null;
                Assert.DoesNotThrow(() => ok = IndicatorsCsv.TryAppend(_path, Snapshot(), out error));
                Assert.That(ok, Is.False);
                Assert.That(error, Is.Not.Empty);
            }

            Assert.That(Lines().Length, Is.EqualTo(2));
        }

        [Test]
        public void Row_UsesSemicolonsDecimalCommaAndNoThousandsSeparator()
        {
            string[] columns = Columns(IndicatorsCsv.FormatRow(Snapshot()));

            Assert.That(columns.Length, Is.EqualTo(Columns(IndicatorsCsv.Header).Length));
            Assert.That(columns[0], Is.EqualTo("01/10/2026 14:05:09"));
            Assert.That(columns[1], Is.EqualTo("cenario_fluxoMaximo.json"));
            Assert.That(columns[2], Is.EqualTo("comportamento_fluxoMaximo.json"));
            Assert.That(columns[5], Is.EqualTo("1000"));
            Assert.That(columns[6], Is.EqualTo("24500"));
            Assert.That(columns[7], Is.EqualTo("23871"));
            Assert.That(columns[11], Is.EqualTo("92248398"));
            Assert.That(columns[13], Is.EqualTo("1234,568"));
            Assert.That(columns[14], Is.EqualTo("0,123"));
            Assert.That(columns[15], Is.EqualTo("59,9"));
        }

        [TestCase(SimulationRunState.Running, "rodando", "")]
        [TestCase(SimulationRunState.Paused, "pausada", "")]
        [TestCase(SimulationRunState.Finished, "encerrada", "sem atividade por 40 ciclo(s)")]
        public void Row_ReflectsTheStateAndOnlyAFinishedRunHasAReason(SimulationRunState state, string label, string expectedReason)
        {
            string[] columns = Columns(IndicatorsCsv.FormatRow(Snapshot(state, "sem atividade por 40 ciclo(s)")));

            Assert.That(columns[3], Is.EqualTo(label));
            Assert.That(columns[4], Is.EqualTo(expectedReason));
        }

        [Test]
        public void Row_LeavesTheLastMovementEmpty_BeforeAnyMovement()
        {
            IndicatorSnapshot snapshot = Snapshot();
            snapshot.LastMovementCycle = null;

            Assert.That(Columns(IndicatorsCsv.FormatRow(snapshot))[7], Is.EqualTo(""));
        }

        [Test]
        public void Row_QuotesTextThatContainsTheSeparator()
        {
            IndicatorSnapshot snapshot = Snapshot();
            snapshot.ScenarioFile = "teste;1 \"final\".json";

            StringAssert.StartsWith("01/10/2026 14:05:09;\"teste;1 \"\"final\"\".json\";", IndicatorsCsv.FormatRow(snapshot));
        }

        [Test]
        public void SuggestedFileName_HasScenarioBehaviorAndTimestamp_WithoutJsonExtensions()
        {
            string name = IndicatorsCsv.SuggestedFileName("cenario_fluxoMaximo.json", "comportamento_fluxoMaximo.json", new DateTime(2026, 10, 1, 14, 5, 9));

            Assert.That(name, Is.EqualTo("indicadores_cenario_fluxoMaximo_comportamento_fluxoMaximo_2026-10-01_14-05-09.csv"));
        }
    }
}
