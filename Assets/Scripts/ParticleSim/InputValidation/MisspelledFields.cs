using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace ParticleSim.InputValidation
{
    // The typed model is read case-insensitively, so a field written with other casing would skip validation entirely.
    public sealed class MisspelledFields
    {
        private readonly List<(string Written, string Canonical)> _found = new List<(string, string)>();

        public static MisspelledFields In(JObject obj, IEnumerable<string> canonicalNames)
        {
            var misspelled = new MisspelledFields();
            foreach (JProperty property in obj.Properties())
                foreach (string canonical in canonicalNames)
                    if (property.Name != canonical && string.Equals(property.Name, canonical, StringComparison.OrdinalIgnoreCase))
                        misspelled._found.Add((property.Name, canonical));
            return misspelled;
        }

        public bool Has(string canonical) => _found.Exists(field => field.Canonical == canonical);

        public void Report(string where, ValidationResult result)
        {
            foreach ((string written, string canonical) in _found)
                result.AddError($"{where}: campo \"{written}\" não reconhecido. Use \"{canonical}\".");
        }
    }
}
