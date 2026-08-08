using UnityEngine;
using UnityEngine.InputSystem;

namespace Features.Player
{
    /// <summary>
    /// Converts the shared throttle and steering actions into reusable movement intent.
    /// </summary>
    [RequireComponent(typeof(PlayerInput))]
    public sealed class PlayerInputSource : MonoBehaviour
    {
        private PlayerInput _playerInput;
        private InputAction _throttleAction;
        private InputAction _steerAction;

        public Vector2 MoveInput { get; private set; }

        private void Awake()
        {
            _playerInput = GetComponent<PlayerInput>();
            _throttleAction = _playerInput.actions.FindAction("Throttle", true);
            _steerAction = _playerInput.actions.FindAction("Steer", true);
        }

        private void Update()
        {
            float throttle = Mathf.Clamp(_throttleAction.ReadValue<float>(), -1f, 1f);
            float steering = Mathf.Clamp(_steerAction.ReadValue<float>(), -1f, 1f);
            MoveInput = new Vector2(steering, throttle);
        }

        private void OnDisable()
        {
            MoveInput = Vector2.zero;
        }
    }
}
