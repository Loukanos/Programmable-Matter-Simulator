using UnityEngine;
using ParticleSim.Core;

namespace ParticleSim.UI
{
    /// <summary>
    /// Visual representation of a particle (color and movement animation).
    /// </summary>
    public class ParticleView : MonoBehaviour
    {
        public Particle Particle { get; private set; }

        private float _cellSize = 1f;
        private float _moveDuration = 0.25f;

        private Renderer _renderer;
        private Color _originalColor = Color.white;

        /// <summary>
        /// True once this particle's behavior ran `setColor()`, i.e. the color
        /// on screen was chosen by the `behavior.json` rather than inherited
        /// from <see cref="SimulationController.particleColor"/>. It's what
        /// lets <see cref="SetBaseColor"/> change the default color live
        /// without wiping a color the simulation deliberately assigned.
        /// Cleared by <see cref="ResetVisual"/>, since a reset puts every
        /// particle back on the default.
        /// </summary>
        private bool _hasBehaviorColor;

        private Vector3 _animStart;
        private Vector3 _animTarget;
        private float _animTimer;
        private bool _animating;

        public void Bind(Particle particle, float cellSize, float moveDuration)
        {
            Particle = particle;
            _cellSize = cellSize;
            _moveDuration = Mathf.Max(0f, moveDuration);
            _renderer = GetComponent<Renderer>();

            if (_renderer != null && _renderer.sharedMaterial != null)
            {
                _originalColor = _renderer.sharedMaterial.HasProperty("_BaseColor")
                    ? _renderer.sharedMaterial.GetColor("_BaseColor")
                    : (_renderer.sharedMaterial.HasProperty("_Color")
                        ? _renderer.sharedMaterial.GetColor("_Color")
                        : Color.white);
            }

            if (Particle != null)
            {
                name = $"Particle_{Particle.Id}_{Particle.Type}";
                transform.position = GridToWorld(Particle.GridPosition);
            }

            _animating = false;
            _hasBehaviorColor = false;
        }

        /// <summary>
        /// Applies a color chosen by the particle's own behavior (the
        /// `setColor()` action, via SimulationEngine's OnColorChanged). Marks
        /// this particle as behavior-colored so a later change to the default
        /// particle color leaves it alone.
        /// </summary>
        public void SetColor(Color color)
        {
            _hasBehaviorColor = true;
            ApplyColor(color);
        }

        /// <summary>
        /// Changes the *default* color — what this particle shows while its
        /// behavior hasn't assigned one. Called when the default is edited
        /// live from the UI. A particle already colored by `setColor()` keeps
        /// that color; only its fallback is updated, so it still turns the new
        /// default on the next <see cref="ResetVisual"/>.
        /// </summary>
        public void SetBaseColor(Color color)
        {
            _originalColor = color;
            if (!_hasBehaviorColor)
                ApplyColor(color);
        }

        private void ApplyColor(Color color)
        {
            if (_renderer == null || _renderer.sharedMaterial == null)
                return;

            MaterialPropertyBlock mpb = new MaterialPropertyBlock();
            _renderer.GetPropertyBlock(mpb);

            if (_renderer.sharedMaterial.HasProperty("_BaseColor"))
                mpb.SetColor("_BaseColor", color);
            else if (_renderer.sharedMaterial.HasProperty("_Color"))
                mpb.SetColor("_Color", color);

            _renderer.SetPropertyBlock(mpb);
        }

        /// <summary>
        /// Changes how long a grid-to-grid move takes to animate. Safe to call
        /// mid-animation: only the duration the in-flight interpolation is
        /// measured against changes, the animation itself is not restarted or
        /// cut short (which is why this exists instead of re-calling
        /// <see cref="Bind"/>, whose position snap would be visible).
        /// </summary>
        public void SetMoveDuration(float moveDuration)
        {
            _moveDuration = Mathf.Max(0f, moveDuration);
        }

        public void AnimateToCurrentGridPosition()
        {
            if (Particle == null)
                return;

            _animStart = transform.position;
            _animTarget = GridToWorld(Particle.GridPosition);
            _animTimer = 0f;

            if (_moveDuration <= 0f || (_animTarget - _animStart).sqrMagnitude < 1e-6f)
            {
                transform.position = _animTarget;
                _animating = false;
                return;
            }

            _animating = true;
        }

        public void ResetVisual()
        {
            if (Particle == null)
                return;

            transform.position = GridToWorld(Particle.InitialGridPosition);
            _animating = false;
            _hasBehaviorColor = false;
            ApplyColor(_originalColor);
        }

        private void Update()
        {
            if (!_animating)
                return;

            _animTimer += Time.deltaTime;
            float t = Mathf.Clamp01(_animTimer / _moveDuration);
            float eased = t * t * (3f - 2f * t);
            transform.position = Vector3.LerpUnclamped(_animStart, _animTarget, eased);

            if (t >= 1f)
            {
                transform.position = _animTarget;
                _animating = false;
            }
        }

        private Vector3 GridToWorld(Vector3Int gridPosition)
        {
            return (Vector3)gridPosition * _cellSize;
        }
    }
}
