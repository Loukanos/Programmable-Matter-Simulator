using UnityEngine;

namespace ParticleSim.Core
{
    /// <summary>
    /// Contrato de consulta à grade (lattice) usado pelo **interpretador** da
    /// linguagem declarativa — ConditionEvaluator e ValueResolver, através de
    /// EvaluationContext.Grid. É a superfície inteira de dados que uma regra
    /// de behavior.json consegue alcançar sobre o mundo fora da própria
    /// partícula, e é por isso que ela é estreita de propósito.
    ///
    /// **Só responde sobre células, nunca entrega uma Partícula.** Essa é a
    /// forma estrutural da RN09 ("cada partícula acessa apenas seu próprio
    /// estado interno, sem nenhum tipo de conhecimento global"): não existindo
    /// aqui um jeito de obter o objeto vizinho, não há por onde uma função de
    /// leitura futura ler o estado, o id ou o tipo dele — a regra deixa de
    /// depender de todo autor lembrar de descartar o resultado.
    ///
    /// O Motor de Simulação **não** consome esta interface: ele referencia o
    /// Gerenciador de Partículas concretamente, porque precisa enumerar a
    /// população e reconstruir o índice espacial a cada ciclo (ver o comentário
    /// de SimulationEngine). Quem precisa da Partícula em si — entrega de
    /// mensagem e resolução de colisão — usa ParticleManager.TryGetParticleAt
    /// diretamente, fora deste contrato.
    /// </summary>
    public interface IGridQuery
    {
        /// <summary>Bloqueia uma posição de destino na borda da grade: cada eixo é fixado no último índice válido, cancelando o deslocamento além da borda.</summary>
        Vector3Int ApplyBoundary(Vector3Int position);

        /// <summary>Informa apenas se há alguma partícula na posição — nunca qual.</summary>
        bool IsOccupied(Vector3Int position);

        /// <summary>Verifica se a posição está dentro dos limites da grade (0 &lt;= coord &lt; GridSize em cada eixo).</summary>
        bool IsInBounds(Vector3Int position);
    }
}