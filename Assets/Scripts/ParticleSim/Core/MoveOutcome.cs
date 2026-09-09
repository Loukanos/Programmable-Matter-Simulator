namespace ParticleSim.Core
{
    /// <summary>
    /// Result of a particle's move() attempt in one cycle, exposed to the
    /// declarative language via moveResult() (always reading the *previous*
    /// cycle's outcome, same one-cycle lag as message delivery). Set once per
    /// particle per Step() by SimulationEngine, stored on Particle.LastMoveResult.
    /// </summary>
    public enum MoveOutcome
    {
        /// <summary>No move() action was executed by the particle last cycle.</summary>
        NotAttempted,
        /// <summary>A move() was attempted and the particle actually changed position.</summary>
        Moved,
        /// <summary>A move() was attempted but the target cell was occupied or contested by another particle (SimulationEngine.OnCollision).</summary>
        BlockedByCollision,
        /// <summary>
        /// A move() was attempted but could not be applied for a reason other
        /// than collision — the direction led outside the grid, or (with
        /// direction-as-variable) a dynamic direction expression did not
        /// resolve to one of the six valid directions. Both share this single
        /// outcome and SimulationEngine.OnInvalidMovement, since the
        /// declarative language treats an unresolvable direction the same
        /// way it treats an out-of-grid one.
        /// </summary>
        Invalid,
    }
}
