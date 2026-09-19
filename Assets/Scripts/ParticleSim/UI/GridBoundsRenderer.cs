using UnityEngine;

namespace ParticleSim.UI
{
    /// <summary>
    /// Draws only the outline (the 12 edges) of the grid's bounding box using a
    /// single LineRenderer. Shows the simulation limits without filling the
    /// interior, so it stays subtle and out of the way.
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public class GridBoundsRenderer : MonoBehaviour
    {
        private LineRenderer _line;

        /// <summary>
        /// Builds the wireframe box that encloses every grid cell.
        /// Cells span integer coords 0..gridSize-1; the box is padded by half a
        /// cell on each side so the cubes sit neatly inside.
        /// <paramref name="shader"/> comes from <see cref="SimulationController.gridBoundsShader"/>
        /// rather than being resolved here: this component is created at runtime with
        /// AddComponent, so it has no Inspector slot of its own to hold a serialized shader
        /// reference — and a serialized reference is the only thing that keeps the shader from
        /// being stripped out of a build (see <see cref="CreateLineMaterial"/>).
        /// </summary>
        public void Build(Vector3Int gridSize, float cellSize, Color color, float lineWidth, Shader shader = null)
        {
            if (_line == null)
                _line = GetComponent<LineRenderer>();

            Vector3 min = new Vector3(-0.5f, -0.5f, -0.5f) * cellSize;
            Vector3 max = new Vector3(
                (gridSize.x - 0.5f),
                (gridSize.y - 0.5f),
                (gridSize.z - 0.5f)) * cellSize;

            // 8 corners of the box.
            Vector3 a = new Vector3(min.x, min.y, min.z);
            Vector3 b = new Vector3(max.x, min.y, min.z);
            Vector3 c = new Vector3(max.x, min.y, max.z);
            Vector3 d = new Vector3(min.x, min.y, max.z);
            Vector3 e = new Vector3(min.x, max.y, min.z);
            Vector3 f = new Vector3(max.x, max.y, min.z);
            Vector3 g = new Vector3(max.x, max.y, max.z);
            Vector3 h = new Vector3(min.x, max.y, max.z);

            // Single continuous path covering all 12 edges (a few are retraced).
            Vector3[] points =
            {
                a, b, c, d, a,   // bottom face
                e,               // up to top-front-left
                f, b, f,         // top edge + vertical BF (retrace)
                g, c, g,         // top edge + vertical CG (retrace)
                h, d, h,         // top edge + vertical DH (retrace)
                e                // close top face
            };

            _line.useWorldSpace = false;
            _line.loop = false;
            _line.widthMultiplier = lineWidth;
            _line.numCornerVertices = 0;
            _line.numCapVertices = 0;
            _line.alignment = LineAlignment.View;
            _line.textureMode = LineTextureMode.Stretch;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.receiveShadows = false;

            _line.material = CreateLineMaterial(color, shader);
            _line.startColor = color;
            _line.endColor = color;

            _line.positionCount = points.Length;
            _line.SetPositions(points);
        }

        /// <summary>
        /// Prefers the shader handed in from the scene, because only a serialized reference
        /// guarantees the shader is in the build: `Shader.Find` finds nothing that was
        /// stripped, and a shader no asset references gets stripped. The `Shader.Find` chain
        /// below is a fallback, and a misleading one — it "worked" in builds purely because
        /// `Sprites/Default` happens to sit in Graphics settings' always-included list, so the
        /// wireframe silently rendered through a sprite shader in players while the Editor
        /// used URP/Unlit. That never went magenta, so unlike the particles it produced no
        /// obvious symptom, just a quiet difference between Editor and build.
        /// </summary>
        private static Material CreateLineMaterial(Color color, Shader preferredShader)
        {
            Shader shader = preferredShader;
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");

            if (preferredShader == null)
                Debug.LogWarning($"[{nameof(GridBoundsRenderer)}] Sem shader serializado — atribua '{nameof(SimulationController.gridBoundsShader)}' no Inspector do {nameof(SimulationController)}. Em builds o contorno cai num shader de reserva e pode não ficar igual ao Editor.");

            var mat = new Material(shader);
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", color);
            return mat;
        }
    }
}
