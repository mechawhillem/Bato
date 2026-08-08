using UnityEngine;
using UnityEngine.InputSystem;

namespace Features.Camera
{
    /// <summary>
    /// Local follow camera for a boat. It follows velocity for framing while preserving the boat's forward direction in the look target.
    /// </summary>
    [RequireComponent(typeof(UnityEngine.Camera))]
    [RequireComponent(typeof(PlayerInput))]
    public sealed class BoatCameraController : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] private Transform _target;
        [SerializeField] private Rigidbody _targetRigidbody;
        [SerializeField] private BoatCameraInputSource _inputSource;

        [Header("Normal View")]
        [SerializeField, Min(0.01f)] private float _distance = 8f;
        [SerializeField, Min(0f)] private float _height = 5f;
        [SerializeField, Min(0f)] private float _lookHeight = 1f;
        [SerializeField, Min(0f)] private float _lookAhead = 2f;
        [SerializeField, Min(0.001f)] private float _minimumVelocity = 0.15f;
        [SerializeField, Min(0f)] private float _positionSharpness = 10f;
        [SerializeField, Min(0f)] private float _rotationSharpness = 8f;

        [Header("Stick Offset")]
        [SerializeField, Min(0f)] private float _lookYawSpeed = 120f;
        [SerializeField, Min(0f)] private float _lookPitchSpeed = 75f;
        [SerializeField, Range(0f, 89f)] private float _maximumPitch = 55f;
        [SerializeField, Min(0f)] private float _offsetRecenterSpeed = 4f;

        [Header("Flank Views")]
        [SerializeField, Min(0.01f)] private float _flankBlendDuration = 0.18f;
        [SerializeField, Min(0f)] private float _flankSideOffset = 5f;
        [SerializeField, Min(0f)] private float _flankForwardOffset = 1.5f;
        [SerializeField, Min(0f)] private float _flankHeight = 3.5f;

        private readonly BoatCameraStateMachine _stateMachine = new();
        private Vector3 _lastValidVelocityDirection = Vector3.forward;
        private Vector3 _currentDesiredPosition;
        private Quaternion _currentDesiredRotation;
        private BoatCameraState _previousState = BoatCameraState.Normal;
        private float _yawOffset;
        private float _pitchOffset;

        public BoatCameraState CurrentState => _stateMachine.State;
        public BoatCameraLockMode CurrentLockMode => _stateMachine.LockMode;

        public void SetTarget(Transform target, Rigidbody targetRigidbody)
        {
            _target = target;
            _targetRigidbody = targetRigidbody != null ? targetRigidbody : target != null ? target.GetComponent<Rigidbody>() : null;

            if (_targetRigidbody != null)
            {
                Vector3 planarVelocity = Vector3.ProjectOnPlane(_targetRigidbody.linearVelocity, Vector3.up);
                if (planarVelocity.sqrMagnitude >= _minimumVelocity * _minimumVelocity)
                {
                    _lastValidVelocityDirection = planarVelocity.normalized;
                }
            }
        }


        private void Awake()
        {
            _inputSource ??= GetComponent<BoatCameraInputSource>();
            _currentDesiredPosition = transform.position;
            _currentDesiredRotation = transform.rotation;

            if (_target != null && _targetRigidbody == null)
            {
                _targetRigidbody = _target.GetComponent<Rigidbody>();
            }
        }

        private void OnEnable()
        {
            if (_inputSource == null)
            {
                return;
            }

            _inputSource.FlankLeftStarted += OnFlankLeftStarted;
            _inputSource.FlankLeftCanceled += OnFlankLeftCanceled;
            _inputSource.FlankRightStarted += OnFlankRightStarted;
            _inputSource.FlankRightCanceled += OnFlankRightCanceled;
        }

        private void OnDisable()
        {
            if (_inputSource == null)
            {
                return;
            }

            _inputSource.FlankLeftStarted -= OnFlankLeftStarted;
            _inputSource.FlankLeftCanceled -= OnFlankLeftCanceled;
            _inputSource.FlankRightStarted -= OnFlankRightStarted;
            _inputSource.FlankRightCanceled -= OnFlankRightCanceled;
        }

        private void LateUpdate()
        {
            if (_target == null || _targetRigidbody == null || _inputSource == null)
            {
                return;
            }

            _stateMachine.Update(Time.unscaledTime);
            Vector3 planarVelocity = Vector3.ProjectOnPlane(_targetRigidbody.linearVelocity, Vector3.up);
            bool boatIsMoving = planarVelocity.sqrMagnitude >= _minimumVelocity * _minimumVelocity;
            UpdateStickOffset(_inputSource.LookInput, Time.unscaledDeltaTime, boatIsMoving);

            CalculateView(out Vector3 desiredPosition, out Quaternion desiredRotation);

            bool stateChanged = _previousState != _stateMachine.State;
            float positionSpeed = stateChanged ? 1f / _flankBlendDuration : _positionSharpness;
            float rotationSpeed = stateChanged ? 1f / _flankBlendDuration : _rotationSharpness;
            float positionBlend = 1f - Mathf.Exp(-positionSpeed * Time.unscaledDeltaTime);
            float rotationBlend = 1f - Mathf.Exp(-rotationSpeed * Time.unscaledDeltaTime);

            _currentDesiredPosition = Vector3.Lerp(_currentDesiredPosition, desiredPosition, positionBlend);
            _currentDesiredRotation = Quaternion.Slerp(_currentDesiredRotation, desiredRotation, rotationBlend);
            transform.SetPositionAndRotation(_currentDesiredPosition, _currentDesiredRotation);
            _previousState = _stateMachine.State;
        }

        private void UpdateStickOffset(Vector2 lookInput, float deltaTime, bool boatIsMoving)
        {
            if (lookInput.sqrMagnitude > 0.001f)
            {
                _yawOffset += lookInput.x * _lookYawSpeed * deltaTime;
                _yawOffset = Mathf.Repeat(_yawOffset + 180f, 360f) - 180f;
                _pitchOffset = Mathf.Clamp(_pitchOffset - lookInput.y * _lookPitchSpeed * deltaTime, -_maximumPitch, _maximumPitch);
                return;
            }

            if (!boatIsMoving)
            {
                return;
            }

            _yawOffset = Mathf.MoveTowardsAngle(_yawOffset, 0f, _offsetRecenterSpeed * _lookYawSpeed * deltaTime);
            _pitchOffset = Mathf.MoveTowards(_pitchOffset, 0f, _offsetRecenterSpeed * _lookPitchSpeed * deltaTime);
        }

        private void CalculateView(out Vector3 desiredPosition, out Quaternion desiredRotation)
        {
            Vector3 velocity = Vector3.ProjectOnPlane(_targetRigidbody.linearVelocity, Vector3.up);
            if (velocity.sqrMagnitude >= _minimumVelocity * _minimumVelocity)
            {
                _lastValidVelocityDirection = velocity.normalized;
            }

            Vector3 heading = _lastValidVelocityDirection;
            Vector3 lookDirection = _target.forward;
            Vector3 positionOffset;

            if (_stateMachine.State == BoatCameraState.Normal)
            {
                Quaternion headingRotation = Quaternion.LookRotation(heading, Vector3.up);
                Quaternion stickOffset = Quaternion.Euler(_pitchOffset, _yawOffset, 0f);
                positionOffset = headingRotation * (stickOffset * (Vector3.back * _distance + Vector3.up * _height));
            }
            else
            {
                float sideSign = _stateMachine.State == BoatCameraState.FlankLeft ? -1f : 1f;
                Vector3 side = _target.right * sideSign;
                positionOffset = side * _flankSideOffset - _target.forward * _flankForwardOffset + Vector3.up * _flankHeight;
            }

            desiredPosition = _target.position + positionOffset;
            Vector3 lookTarget = _target.position + lookDirection * _lookAhead + Vector3.up * _lookHeight;
            Vector3 toLookTarget = lookTarget - desiredPosition;
            desiredRotation = toLookTarget.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(toLookTarget.normalized, Vector3.up) : transform.rotation;
        }

        private void OnFlankLeftStarted() => _stateMachine.BeginLeft(Time.unscaledTime);
        private void OnFlankLeftCanceled() => _stateMachine.EndLeft(Time.unscaledTime);
        private void OnFlankRightStarted() => _stateMachine.BeginRight(Time.unscaledTime);
        private void OnFlankRightCanceled() => _stateMachine.EndRight(Time.unscaledTime);
    }
}
