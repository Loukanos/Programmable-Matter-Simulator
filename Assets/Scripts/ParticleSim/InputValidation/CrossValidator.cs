using System.Collections.Generic;
using ParticleSim.Core;

namespace ParticleSim.InputValidation
{
    /// <summary>
    /// Validação que depende dos **dois** arquivos ao mesmo tempo, e por isso
    /// não cabe em <see cref="ScenarioValidator"/> nem em
    /// <see cref="BehaviorValidator"/> — cada um deles enxerga apenas o seu
    /// próprio arquivo.
    ///
    /// Cobre a categoria de erro "referência a tipo de partícula inexistente
    /// no arquivo de comportamento" exigida pelo TCC. Sem esta checagem, uma
    /// partícula cujo tipo não existe no comportamento simplesmente não é
    /// encontrada no dicionário durante o Step() e fica inerte para sempre,
    /// sem erro nem aviso — exatamente o "degradar em tempo de execução" que
    /// a especificação proíbe ao exigir que o carregamento seja bloqueado.
    /// </summary>
    public static class CrossValidator
    {
        /// <summary>
        /// Acusa todo tipo referenciado pelo cenário que não tenha
        /// comportamento definido. O contrário não é erro: um comportamento
        /// pode declarar tipos que nenhuma partícula usa.
        ///
        /// A checagem é feita com <c>ContainsKey</c> sobre o próprio
        /// dicionário que o motor vai consultar, de propósito: assim a
        /// comparação de chaves aqui é necessariamente a mesma que
        /// <c>SimulationEngine.Step</c> faz em runtime. Reimplementar a
        /// comparação abriria espaço para validar de um jeito e executar de
        /// outro — hoje o dicionário é ordinal e diferencia maiúsculas, mas
        /// esta checagem continua correta mesmo que isso mude.
        /// </summary>
        public static void Validate(
            ScenarioData scenario,
            IReadOnlyDictionary<string, TypeBehavior> behaviors,
            ValidationResult validation)
        {
            if (scenario?.particles == null || behaviors == null) return;

            var alreadyReported = new HashSet<string>();

            foreach (ParticleData particle in scenario.particles)
            {
                // Tipo ausente/vazio já é erro reportado pelo ScenarioValidator;
                // ignorar aqui evita uma segunda mensagem sobre o mesmo problema
                // (e ContainsKey(null) lançaria exceção no meio da validação).
                if (string.IsNullOrEmpty(particle?.type)) continue;

                if (behaviors.ContainsKey(particle.type)) continue;
                if (!alreadyReported.Add(particle.type)) continue;

                string nearMatch = FindCaseInsensitiveMatch(behaviors, particle.type);
                validation.AddError(nearMatch == null
                    ? $"Cenário: tipo de partícula '{particle.type}' (partícula id {particle.id}) não existe no arquivo de comportamento."
                    : $"Cenário: tipo de partícula '{particle.type}' (partícula id {particle.id}) não existe no arquivo de comportamento — o comportamento define '{nearMatch}', e a comparação diferencia maiúsculas de minúsculas.");
            }
        }

        /// <summary>
        /// Procura um tipo que difira apenas por maiúsculas/minúsculas. Como o
        /// dicionário é case-sensitive, essa é de longe a causa mais provável
        /// de um tipo "faltando", e apontá-la transforma um erro confuso em um
        /// conserto óbvio.
        /// </summary>
        private static string FindCaseInsensitiveMatch(IReadOnlyDictionary<string, TypeBehavior> behaviors, string type)
        {
            foreach (string declared in behaviors.Keys)
            {
                if (string.Equals(declared, type, System.StringComparison.OrdinalIgnoreCase))
                    return declared;
            }

            return null;
        }
    }
}
