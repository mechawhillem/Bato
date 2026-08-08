using UnityEngine;
using UnityEngine.InputSystem;

namespace Features.Input
{
    /// <summary>
    /// Selects one control scheme for all local player input components and prevents automatic switching.
    /// </summary>
    public sealed class InputDeviceModeToggle : MonoBehaviour
    {
        [SerializeField] private bool _useGamepad = true;
        [SerializeField] private PlayerInput[] _playerInputs;

        public bool UseGamepad => _useGamepad;

        private void Awake()
        {
            ApplyMode();
        }

        public void ApplyMode()
        {
            if (_playerInputs == null)
            {
                return;
            }

            for (int index = 0; index < _playerInputs.Length; index++)
            {
                PlayerInput playerInput = _playerInputs[index];
                if (playerInput == null)
                {
                    continue;
                }

                playerInput.neverAutoSwitchControlSchemes = true;
                if (_useGamepad)
                {
                    playerInput.SwitchCurrentControlScheme("Gamepad", Gamepad.current);
                }
                else
                {
                    playerInput.SwitchCurrentControlScheme("Keyboard&Mouse", Keyboard.current, Mouse.current);
                }
            }
        }

        public void SetUseGamepad(bool useGamepad)
        {
            _useGamepad = useGamepad;
            ApplyMode();
        }
    }
}
