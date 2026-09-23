using System.Collections.Generic;
using System.Text;

namespace ParticleSim.InputValidation
{
    public class ValidationResult
    {
        private readonly List<string> _errors = new List<string>();

        public IReadOnlyList<string> Errors => _errors;
        public bool IsValid => _errors.Count == 0;

        public void AddError(string message) => _errors.Add(message);

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
