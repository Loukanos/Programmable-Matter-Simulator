using System.Collections.Generic;
using System.Text;

namespace ParticleSim.InputValidation
{
    /// <summary>
    /// Accumulates every problem found while validating one input file.
    /// Validation deliberately collects all errors instead of stopping at the
    /// first one, so a single failed load tells the user everything that is
    /// wrong with the file rather than one problem per attempt. A file is
    /// only loaded when the result is still empty (RF04/RNF08: any lexical,
    /// syntactic or semantic error blocks the load).
    /// </summary>
    public class ValidationResult
    {
        private readonly List<string> _errors = new List<string>();

        public IReadOnlyList<string> Errors => _errors;
        public bool IsValid => _errors.Count == 0;

        public void AddError(string message) => _errors.Add(message);

        /// <summary>
        /// Formats the collected errors as one human-readable block, capped at
        /// <paramref name="maxLines"/> entries so a badly broken file cannot
        /// flood the on-screen panel.
        /// </summary>
        public string ToMessage(int maxLines = 10)
        {
            if (IsValid) return string.Empty;

            var builder = new StringBuilder();
            int shown = _errors.Count < maxLines ? _errors.Count : maxLines;

            for (int i = 0; i < shown; i++)
            {
                if (i > 0) builder.Append('\n');
                builder.Append("• ").Append(_errors[i]);
            }

            int remaining = _errors.Count - shown;
            if (remaining > 0)
                builder.Append($"\n• ... e mais {remaining} erro(s).");

            return builder.ToString();
        }
    }
}
