using UnityEngine;

namespace ParticleSim.UI
{
    [RequireComponent(typeof(LineRenderer))]
    public class GridBoundsRenderer : MonoBehaviour
    {
        private LineRenderer _line;

        public void Build(Vector3Int gridSize, float cellSize, Color color, float lineWidth, Shader shader = null)
        {
            if (_line == null)
                _line = GetComponent<LineRenderer>();

            Vector3 min = new Vector3(-0.5f, -0.5f, -0.5f) * cellSize;
            Vector3 max = new Vector3(
                (gridSize.x - 0.5f),
                (gridSize.y - 0.5f),
                (gridSize.z - 0.5f)) * cellSize;

            Vector3 a = new Vector3(min.x, min.y, min.z);
            Vector3 b = new Vector3(max.x, min.y, min.z);
            Vector3 c = new Vector3(max.x, min.y, max.z);
            Vector3 d = new Vector3(min.x, min.y, max.z);
            Vector3 e = new Vector3(min.x, max.y, min.z);
            Vector3 f = new Vector3(max.x, max.y, min.z);
            Vector3 g = new Vector3(max.x, max.y, max.z);
            Vector3 h = new Vector3(min.x, max.y, max.z);

            // One continuous line strip over all 12 edges; some edges are retraced.
            Vector3[] points =
            {
                a, b, c, d, a,
                e,
                f, b, f,
                g, c, g,
                h, d, h,
                e
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

        // Prefer the serialized shader: Shader.Find only finds shaders that made it into the build.
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
                Debug.LogWarning($"[{nameof(GridBoundsRenderer)}] No serialized shader: assign '{nameof(SimulationController.gridBoundsShader)}' in the {nameof(SimulationController)} Inspector. In builds the outline falls back to another shader and may not match the Editor.");

            var mat = new Material(shader);
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", color);
            return mat;
        }
    }
}
