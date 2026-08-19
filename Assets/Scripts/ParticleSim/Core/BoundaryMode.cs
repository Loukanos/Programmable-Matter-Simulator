namespace ParticleSim.Core
{
    /// <summary>
    /// Comportamento das partículas ao atingir o limite da grade (lattice).
    /// </summary>
    public enum BoundaryMode
    {
        /// <summary>Partículas podem sair livremente da grade.</summary>
        None,
        /// <summary>Partículas param na borda da grade.</summary>
        Clamp,
        /// <summary>Partículas dão a volta para o lado oposto da grade.</summary>
        Wrap
    }
}