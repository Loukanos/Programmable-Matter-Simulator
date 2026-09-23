using UnityEngine;
using UnityEngine.InputSystem;

namespace ParticleSim.UI
{
    public class FlyCameraController : MonoBehaviour
    {
        [Header("Movement")]
        [Tooltip("Base movement speed in world units per second.")]
        public float moveSpeed = 8f;
        [Tooltip("Multiplier applied while Left Shift is held.")]
        public float boostMultiplier = 3f;
        [Tooltip("How quickly the mouse wheel changes the movement speed.")]
        public float scrollSpeedStep = 2f;
        public float minSpeed = 0.5f;
        public float maxSpeed = 100f;

        [Header("Look")]
        [Tooltip("Mouse look sensitivity (degrees per pixel of mouse delta).")]
        public float lookSensitivity = 0.12f;
        [Tooltip("Clamp for vertical look angle, in degrees.")]
        public float pitchLimit = 89f;
        [Tooltip("Require holding the right mouse button to look around.")]
        public bool requireRightMouseToLook = true;

        private float _yaw;
        private float _pitch;
        private bool _looking;

        private void OnEnable()
        {
            SyncRotationFromTransform();
        }

        private void SyncRotationFromTransform()
        {
            Vector3 euler = transform.eulerAngles;
            _yaw = euler.y;
            _pitch = NormalizePitch(euler.x);
        }

        private static float NormalizePitch(float angle)
        {
            if (angle > 180f)
                angle -= 360f;
            return angle;
        }

        private void Update()
        {
            HandleLook();
            HandleSpeedScroll();
            HandleMovement();
        }

        public void ConfigureForSceneSize(float diameter)
        {
            diameter = Mathf.Max(diameter, 0.01f);
            moveSpeed = Mathf.Clamp(diameter, 0.5f, 5000f);
            minSpeed = Mathf.Max(0.1f, diameter * 0.04f);
            maxSpeed = Mathf.Max(minSpeed * 5f, diameter * 6f);
        }

        private void HandleLook()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null)
            {
                _looking = false;
                return;
            }

            bool lookActive = !requireRightMouseToLook || mouse.rightButton.isPressed;

            // Re-sync from the transform: FrameCamera may have moved the camera since the last look.
            if (lookActive && !_looking)
                SyncRotationFromTransform();

            _looking = lookActive;

            if (!lookActive)
                return;

            Vector2 delta = mouse.delta.ReadValue();
            _yaw += delta.x * lookSensitivity;
            _pitch -= delta.y * lookSensitivity;
            _pitch = Mathf.Clamp(_pitch, -pitchLimit, pitchLimit);

            transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        }

        private void HandleSpeedScroll()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null)
                return;

            float scrollY = mouse.scroll.ReadValue().y;
            if (Mathf.Approximately(scrollY, 0f))
                return;

            // Mouse scroll reports about 120 per notch on most platforms.
            float notches = scrollY / 120f;
            if (Mathf.Approximately(notches, 0f))
                notches = Mathf.Sign(scrollY);

            moveSpeed = Mathf.Clamp(moveSpeed + notches * scrollSpeedStep, minSpeed, maxSpeed);
        }

        private void HandleMovement()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            Vector3 input = Vector3.zero;

            if (keyboard.wKey.isPressed) input += Vector3.forward;
            if (keyboard.sKey.isPressed) input += Vector3.back;
            if (keyboard.aKey.isPressed) input += Vector3.left;
            if (keyboard.dKey.isPressed) input += Vector3.right;
            if (keyboard.eKey.isPressed) input += Vector3.up;
            if (keyboard.qKey.isPressed) input += Vector3.down;

            if (input.sqrMagnitude < 0.0001f)
                return;

            input = Vector3.ClampMagnitude(input, 1f);

            float speed = moveSpeed;
            if (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed)
                speed *= boostMultiplier;

            Vector3 move =
                transform.right * input.x +
                transform.forward * input.z +
                Vector3.up * input.y;

            transform.position += move * speed * Time.deltaTime;
        }
    }
}
