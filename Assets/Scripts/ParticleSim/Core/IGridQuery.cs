using UnityEngine;

namespace ParticleSim.Core
{
    /// <summary>
    /// Contrato mínimo que o Motor de Simulação e o interpretador da
    /// linguagem declarativa usam para consultar a grade (lattice), sem
    /// depender diretamente da implementação do Gerenciador de Partículas.
    /// Reforça a direção de dependência definida na Figura 7: o Motor de
    /// Simulação depende do Gerenciador de Partículas, nunca o contrário.
    /// </summary>
    public interface IGridQuery
    {
        /// <summary>Bloqueia uma posição de destino na borda da grade: cada eixo é fixado no último índice válido, cancelando o deslocamento além da borda.</summary>
        Vector3Int ApplyBoundary(Vector3Int position);

        /// <summary>Tenta obter a partícula que ocupa a posição informada.</summary>
        bool TryGetParticleAt(Vector3Int position, out Particle particle);

        /// <summary>Verifica se a posição está dentro dos limites da grade (0 &lt;= coord &lt; GridSize em cada eixo).</summary>
        bool IsInBounds(Vector3Int position);
    }
}