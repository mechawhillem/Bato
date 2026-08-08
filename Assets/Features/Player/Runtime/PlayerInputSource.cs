using UnityEngine;
using UnityEngine.InputSystem;

namespace Features.Player
{
    /// <summary>
    /// Converts the shared throttle, steering, and jump actions into reusable movement intent.
    /// </summary>
    [RequireComponent(typeof(PlayerInput))]
    public sealed class PlayerInputSource : MonoBehaviour
    {
        private PlayerInput _playerInput;
        private InputAction _throttleAction;
        private InputAction _steerAction;
        private InputAction _jumpAction;

        private bool _jumpPressed;

        public Vector2 MoveInput { get; private set; }
        public bool JumpHeld { get; private set; }

        public bool ConsumeJumpPressed()
        {
            bool wasPressed = _jumpPressed;
            _jumpPressed = false;
            return wasPressed;
        }

        private void Awake()
        {
            _playerInput = GetComponent<PlayerInput>();
            _throttleAction = _playerInput.actions.FindAction("Throttle", true);
            _steerAction = _playerInput.actions.FindAction("Steer", true);
            _jumpAction = _playerInput.actions.FindAction("Jump", true);
        }

        private void Update()
        {
            float throttle = Mathf.Clamp(_throttleAction.ReadValue<float>(), -1f, 1f);
            float steering = Mathf.Clamp(_steerAction.ReadValue<float>(), -1f, 1f);

            MoveInput = new Vector2(steering, throttle);
            if (_jumpAction.WasPressedThisFrame())
            {
                _jumpPressed = true;
            }

            JumpHeld = _jumpAction.IsPressed();
        }

        private void OnDisable()
        {
            MoveInput = Vector2.zero;
            _jumpPressed = false;
            JumpHeld = false;
        }
    }
}
