using UnityEngine;
using UnityEngine.InputSystem;

namespace Features.Player
{
    /// <summary>
    /// Converts the shared Player/Move input action into a reusable movement intent.
    /// </summary>
    [RequireComponent(typeof(PlayerInput))]
    public sealed class PlayerInputSource : MonoBehaviour
    {
        private PlayerInput _playerInput;
        private InputAction _moveAction;

        public Vector2 MoveInput { get; private set; }

        private void Awake()
        {
            _playerInput = GetComponent<PlayerInput>();
            _moveAction = _playerInput.actions.FindAction("Move", true);
        }

        private void Update()
        {
            MoveInput = Vector2.ClampMagnitude(_moveAction.ReadValue<Vector2>(), 1f);
        }

        private void OnDisable()
        {
            MoveInput = Vector2.zero;
        }
    }
}
