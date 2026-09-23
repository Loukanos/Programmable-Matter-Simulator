using UnityEngine;

namespace ParticleSim.Core
{
    //this is the interpreter's only view of the grid. Answer about cells; never expose a Particle.
    public interface IGridQuery
    {
        Vector3Int ApplyBoundary(Vector3Int position);
        bool IsOccupied(Vector3Int position);
        bool IsInBounds(Vector3Int position);
    }
}
