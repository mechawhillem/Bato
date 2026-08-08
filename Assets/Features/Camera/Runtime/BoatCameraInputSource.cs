using UnityEngine;
using UnityEngine.InputSystem;

namespace Features.Camera
{
    /// <summary>
    /// Reads camera-only input and exposes it independently from boat movement.
    /// </summary>
    [RequireComponent(typeof(PlayerInput))]
    public sealed class BoatCameraInputSource : MonoBehaviour
    {
        private PlayerInput _playerInput;
        private InputAction _lookAction;
        private InputAction _flankLeftAction;
        private InputAction _flankRightAction;

        public Vector2 LookInput { get; private set; }
        public bool FlankLeftPressed => _flankLeftAction.IsPressed();
        public bool FlankRightPressed => _flankRightAction.IsPressed();

        public event System.Action FlankLeftStarted;
        public event System.Action FlankLeftCanceled;
        public event System.Action FlankRightStarted;
        public event System.Action FlankRightCanceled;

        private void Awake()
        {
            _playerInput = GetComponent<PlayerInput>();
            _lookAction = _playerInput.actions.FindAction("Look", true);
            _flankLeftAction = _playerInput.actions.FindAction("FlankLeft", true);
            _flankRightAction = _playerInput.actions.FindAction("FlankRight", true);

            _flankLeftAction.started += OnFlankLeftStarted;
            _flankLeftAction.canceled += OnFlankLeftCanceled;
            _flankRightAction.started += OnFlankRightStarted;
            _flankRightAction.canceled += OnFlankRightCanceled;
        }

        private void Update()
        {
            LookInput = Vector2.ClampMagnitude(_lookAction.ReadValue<Vector2>(), 1f);
        }

        private void OnFlankLeftStarted(InputAction.CallbackContext context) => FlankLeftStarted?.Invoke();
        private void OnFlankLeftCanceled(InputAction.CallbackContext context) => FlankLeftCanceled?.Invoke();
        private void OnFlankRightStarted(InputAction.CallbackContext context) => FlankRightStarted?.Invoke();
        private void OnFlankRightCanceled(InputAction.CallbackContext context) => FlankRightCanceled?.Invoke();

        private void OnDisable()
        {
            LookInput = Vector2.zero;
        }

        private void OnDestroy()
        {
            if (_flankLeftAction == null)
            {
                return;
            }

            _flankLeftAction.started -= OnFlankLeftStarted;
            _flankLeftAction.canceled -= OnFlankLeftCanceled;
            _flankRightAction.started -= OnFlankRightStarted;
            _flankRightAction.canceled -= OnFlankRightCanceled;
        }
    }
}
