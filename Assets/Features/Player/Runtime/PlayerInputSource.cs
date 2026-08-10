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
        private Vector2 _lastLoggedMoveInput;

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

        private void OnEnable()
        {
            Debug.Log($"[PlayerInputSource] Activé sur '{name}' | PlayerInput={(_playerInput != null ? _playerInput.enabled : false)} | ActionMap={(_playerInput != null ? _playerInput.currentActionMap?.name ?? "null" : "null")}", this);
        }

        private void Update()
        {
            float throttle = Mathf.Clamp(_throttleAction.ReadValue<float>(), -1f, 1f);
            float steering = Mathf.Clamp(_steerAction.ReadValue<float>(), -1f, 1f);

            if (Keyboard.current != null)
            {
                if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) throttle = 1f;
                else if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) throttle = -1f;

                if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) steering = 1f;
                else if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) steering = -1f;
            }

            MoveInput = new Vector2(steering, throttle);
            if (MoveInput != _lastLoggedMoveInput)
            {
                Debug.Log($"[PlayerInputSource] MoveInput={MoveInput} sur '{name}'", this);
                _lastLoggedMoveInput = MoveInput;
            }

            if (_jumpAction.WasPressedThisFrame() || (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame))
            {
                Debug.Log($"[PlayerInputSource] Jump détecté sur '{name}'", this);
                _jumpPressed = true;
            }

            JumpHeld = _jumpAction.IsPressed();
        }

        private void OnDisable()
        {
            Debug.Log($"[PlayerInputSource] Désactivé sur '{name}'", this);
            MoveInput = Vector2.zero;
            _jumpPressed = false;
            JumpHeld = false;
        }
    }
}
