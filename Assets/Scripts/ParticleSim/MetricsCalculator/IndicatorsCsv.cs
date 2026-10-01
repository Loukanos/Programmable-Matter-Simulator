using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace ParticleSim.MetricsCalculator
{
    public static class IndicatorsCsv
    {
        public const string Header =
            "Data e hora;Arquivo de cenário;Arquivo de comportamento;Estado;Motivo do encerramento;" +
            "Partículas;Ciclos;Ciclo do último movimento;Movimentos;Colisões;Movimentos inválidos;" +
            "Mensagens enviadas;Mensagens recebidas;Tempo decorrido (s);Tempo médio por ciclo (ms);FPS";

        public const string ForeignFileError =
            "o arquivo escolhido já existe e não é uma tabela de indicadores deste simulador (o cabeçalho é diferente)";

        private const string LineBreak = "\r\n";

        private static readonly NumberFormatInfo DecimalComma =
            new NumberFormatInfo { NumberDecimalSeparator = ",", NumberGroupSeparator = "" };

        private static readonly Encoding Utf8WithBom = new UTF8Encoding(true);
        private static readonly Encoding Utf8 = new UTF8Encoding(false);
        private static readonly Encoding Latin1 = Encoding.GetEncoding(28591);

        public static string SuggestedFileName(string scenarioFile, string behaviorFile, DateTime timestamp) =>
            $"indicadores_{Stem(scenarioFile)}_{Stem(behaviorFile)}_{timestamp.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture)}.csv";

        public static string FormatRow(IndicatorSnapshot snapshot) => string.Join(";",
            snapshot.Timestamp.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture),
            Text(snapshot.ScenarioFile),
            Text(snapshot.BehaviorFile),
            StateLabel(snapshot.State),
            snapshot.State == SimulationRunState.Finished ? Text(snapshot.FinishReason) : "",
            Integer(snapshot.ParticleCount),
            Integer(snapshot.Cycles),
            snapshot.LastMovementCycle.HasValue ? Integer(snapshot.LastMovementCycle.Value) : "",
            Integer(snapshot.Movements),
            Integer(snapshot.Collisions),
            Integer(snapshot.InvalidMovements),
            Integer(snapshot.MessagesSent),
            Integer(snapshot.MessagesDelivered),
            snapshot.SimulationTimeSeconds.ToString("F3", DecimalComma),
            snapshot.AverageStepTimeMs.ToString("F3", DecimalComma),
            snapshot.FrameRate.ToString("F1", DecimalComma));

        public static bool TryAppend(string path, IndicatorSnapshot snapshot, out string error)
        {
            error = null;
            try
            {
                string row = FormatRow(snapshot) + LineBreak;

                if (!File.Exists(path) || new FileInfo(path).Length == 0)
                {
                    File.WriteAllText(path, Header + LineBreak + row, Utf8WithBom);
                    return true;
                }

                Encoding encoding = EncodingOfOurHeader(path);
                if (encoding == null)
                {
                    error = ForeignFileError;
                    return false;
                }

                string separator = EndsWithLineBreak(path) ? "" : LineBreak;
                File.AppendAllText(path, separator + row, encoding);
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
        }

        // Excel re-saves CSV files in the legacy Windows encoding; accept our header there too and keep appending in it.
        private static Encoding EncodingOfOurHeader(string path)
        {
            if (FirstLine(path, Utf8) == Header) return Utf8;
            if (FirstLine(path, Latin1) == Header) return Latin1;
            return null;
        }

        private static string FirstLine(string path, Encoding encoding)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true))
                return reader.ReadLine()?.Trim();
        }

        private static bool EndsWithLineBreak(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                stream.Seek(-1, SeekOrigin.End);
                return stream.ReadByte() == '\n';
            }
        }

        private static string StateLabel(SimulationRunState state) => state switch
        {
            SimulationRunState.Running => "rodando",
            SimulationRunState.Finished => "encerrada",
            _ => "pausada",
        };

        private static string Integer(int value) => value.ToString(CultureInfo.InvariantCulture);

        private static string Text(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            bool needsQuotes = value.IndexOfAny(new[] { ';', '"', '\r', '\n' }) >= 0;
            return needsQuotes ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
        }

        private static string Stem(string file) =>
            string.IsNullOrEmpty(file) ? "sem-arquivo" : Path.GetFileNameWithoutExtension(file);
    }
}
