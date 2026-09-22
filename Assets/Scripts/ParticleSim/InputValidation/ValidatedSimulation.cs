using System.Collections.Generic;
using ParticleSim.Core;

namespace ParticleSim.InputValidation
{
    /// <summary>
    /// Par cenário + comportamento que **comprovadamente** passou por toda a
    /// validação: sintática/estrutural/semântica de cada arquivo e a checagem
    /// cruzada entre os dois (<see cref="CrossValidator"/>).
    ///
    /// A garantia vem do tipo, não da ordem de chamada. O construtor é privado
    /// e a única forma de obter uma instância é <see cref="Load"/>, que executa
    /// a validação — então quem recebe um <c>ValidatedSimulation</c> sabe que
    /// ele foi validado sem precisar confiar em quem chamou. Antes disto o
    /// Gerenciador de Partículas aceitava um <c>ScenarioData</c> qualquer:
    /// correto enquanto o único caminho de entrada validasse na ordem certa,
    /// mas um segundo caminho que pulasse a validação compilaria sem aviso.
    ///
    /// O padrão (construtor privado + fábrica estática no próprio tipo) é o que
    /// funciona com os módulos compilando num assembly só — um construtor
    /// <c>internal</c> não separaria nada sem asmdefs.
    ///
    /// Limites, para não prometer mais do que entrega:
    /// <list type="bullet">
    /// <item>Garante que o objeto <em>saiu</em> da validação, não que continua
    /// válido: <see cref="ScenarioData"/> tem campos públicos mutáveis, então
    /// quem já tem uma instância ainda pode alterá-la depois. Isso bloqueia um
    /// caminho alternativo de entrada, não uma corrupção deliberada.</item>
    /// <item>Cobre o lado cenário → Gerenciador de Partículas. O Motor recebe
    /// os comportamentos como dicionário comum, porque não pode depender de
    /// InputValidation; fechar esse lado também exigiria asmdefs, com o tipo
    /// validado morando em Core.</item>
    /// </list>
    /// </summary>
    public sealed class ValidatedSimulation
    {
        public ScenarioData Scenario { get; }
        public Dictionary<string, TypeBehavior> Behaviors { get; }

        private ValidatedSimulation(ScenarioData scenario, Dictionary<string, TypeBehavior> behaviors)
        {
            Scenario = scenario;
            Behaviors = behaviors;
        }

        /// <summary>
        /// Carrega e valida os dois arquivos e só então faz a checagem cruzada.
        /// Devolve null se qualquer etapa falhar, com os erros separados por
        /// arquivo para a interface mostrar cada um no seu próprio card
        /// (erros da checagem cruzada vão para o cenário, que é quem referencia
        /// o tipo inexistente).
        ///
        /// Os dois arquivos são sempre validados, mesmo quando o primeiro já
        /// falhou, para o usuário ver todos os problemas de uma vez. A checagem
        /// cruzada, porém, só roda quando ambos passaram — sem os dois modelos
        /// não há o que comparar.
        /// </summary>
        public static ValidatedSimulation Load(
            string scenarioPath,
            string behaviorPath,
            out ValidationResult scenarioValidation,
            out ValidationResult behaviorValidation)
        {
            ScenarioData scenario = ScenarioLoader.LoadFromFile(scenarioPath, out scenarioValidation);
            Dictionary<string, TypeBehavior> behaviors = BehaviorLoader.LoadFromFile(behaviorPath, out behaviorValidation);

            if (scenario == null || behaviors == null)
                return null;

            CrossValidator.Validate(scenario, behaviors, scenarioValidation);
            if (!scenarioValidation.IsValid)
                return null;

            return new ValidatedSimulation(scenario, behaviors);
        }
    }
}
