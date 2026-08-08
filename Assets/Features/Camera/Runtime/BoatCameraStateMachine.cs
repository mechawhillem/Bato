namespace Features.Camera
{
    public enum BoatCameraState
    {
        Normal,
        FlankLeft,
        FlankRight
    }

    public enum BoatCameraLockMode
    {
        None,
        Hold,
        Toggle
    }

    /// <summary>
    /// Resolves tap, hold and toggle flank requests into one camera state.
    /// </summary>
    public sealed class BoatCameraStateMachine
    {
        private const float HoldThreshold = 0.18f;

        private BoatCameraState _toggleState = BoatCameraState.Normal;
        private float _leftPressedAt = -1f;
        private float _rightPressedAt = -1f;
        private bool _leftHold;
        private bool _rightHold;

        public BoatCameraState State { get; private set; } = BoatCameraState.Normal;
        public BoatCameraLockMode LockMode { get; private set; } = BoatCameraLockMode.None;

        public void BeginLeft(float time)
        {
            _leftPressedAt = time;
            _leftHold = false;
        }

        public void EndLeft(float time)
        {
            if (_leftPressedAt < 0f)
            {
                return;
            }

            if (time - _leftPressedAt < HoldThreshold && !_leftHold)
            {
                _toggleState = _toggleState == BoatCameraState.FlankLeft ? BoatCameraState.Normal : BoatCameraState.FlankLeft;
            }

            _leftPressedAt = -1f;
            _leftHold = false;
            RefreshState();
        }

        public void BeginRight(float time)
        {
            _rightPressedAt = time;
            _rightHold = false;
        }

        public void EndRight(float time)
        {
            if (_rightPressedAt < 0f)
            {
                return;
            }

            if (time - _rightPressedAt < HoldThreshold && !_rightHold)
            {
                _toggleState = _toggleState == BoatCameraState.FlankRight ? BoatCameraState.Normal : BoatCameraState.FlankRight;
            }

            _rightPressedAt = -1f;
            _rightHold = false;
            RefreshState();
        }

        public void Update(float time)
        {
            if (_leftPressedAt >= 0f && !_leftHold && time - _leftPressedAt >= HoldThreshold)
            {
                _leftHold = true;
            }

            if (_rightPressedAt >= 0f && !_rightHold && time - _rightPressedAt >= HoldThreshold)
            {
                _rightHold = true;
            }

            RefreshState();
        }

        private void RefreshState()
        {
            if (_leftHold || _rightHold)
            {
                State = _rightHold ? BoatCameraState.FlankRight : BoatCameraState.FlankLeft;
                LockMode = BoatCameraLockMode.Hold;
                return;
            }

            State = _toggleState;
            LockMode = State == BoatCameraState.Normal ? BoatCameraLockMode.None : BoatCameraLockMode.Toggle;
        }
    }
}
