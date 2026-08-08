using Bato.Water;
using UnityEngine;

namespace Features.Player
{
    /// <summary>
    /// Applies boat-like acceleration, steering, air control, and variable jumping to a Rigidbody.
    /// Input is supplied by a separate PlayerInputSource so this component remains network-agnostic.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class BoatMovementController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PlayerInputSource _inputSource;
        [SerializeField] private BoatBuoyancy _buoyancy;

        [Header("Movement")]
        [SerializeField, Min(0f)] private float _forwardAcceleration = 12f;
        [SerializeField, Min(0f)] private float _reverseAcceleration = 6f;
        [SerializeField, Min(0f)] private float _maxForwardSpeed = 8f;
        [SerializeField, Min(0f)] private float _maxReverseSpeed = 3f;
        [SerializeField, Min(0f)] private float _linearDamping = 1.5f;

        [Header("Steering")]
        [SerializeField, Min(0f)] private float _steeringTorque = 8f;
        [SerializeField, Min(0f)] private float _angularDamping = 3f;

        [Header("Jump")]
        [SerializeField, Min(0f)] private float _jumpImpulse = 8f;
        [SerializeField, Min(0f)] private float _jumpHoldAcceleration = 30f;
        [SerializeField, Min(0f)] private float _maxJumpHoldTime = 0.35f;
        [SerializeField, Range(0f, 1f)] private float _jumpReleaseMultiplier = 0.35f;
        [SerializeField, Min(0f)] private float _airControl = 0.35f;
        [SerializeField, Min(0f)] private float _jumpCooldown = 0.4f;

        private Rigidbody _rigidbody;
        private float _lastJumpTime = -Mathf.Infinity;
        private float _jumpHoldTime;
        private bool _jumpInProgress;

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();

            if (_inputSource == null)
            {
                _inputSource = GetComponent<PlayerInputSource>();
            }

            if (_buoyancy == null)
            {
                _buoyancy = GetComponent<BoatBuoyancy>();
            }

            _rigidbody.useGravity = true;
            _rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            _rigidbody.linearDamping = _linearDamping;
            _rigidbody.angularDamping = _angularDamping;

            // With buoyancy, the hull owns its own vertical motion and its roll and pitch: it has
            // to be free on every axis. Freezing Y here used to leave the boat pinned in mid-air —
            // unable to float or to jump — whenever buoyancy found no water surface to sit on.
            // Without buoyancy we fall back to a flat sea: upright, gravity does the rest.
            _rigidbody.constraints = _buoyancy != null
                ? RigidbodyConstraints.None
                : RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        }

        private void FixedUpdate()
        {
            if (_inputSource == null)
            {
                return;
            }

            Vector2 input = _inputSource.MoveInput;
            bool canJump = _buoyancy == null || _buoyancy.IsInWater;
            bool isInWater = _buoyancy != null && _buoyancy.IsInWater;
            float controlMultiplier = isInWater ? 1f : _airControl;

            HandleJump(canJump);
            ApplyForwardForce(input.y * controlMultiplier);
            ApplySteering(input.x * controlMultiplier);
            ClampPlanarVelocity();
        }

        private void HandleJump(bool canJump)
        {
            if (canJump &&
                _inputSource.ConsumeJumpPressed() &&
                Time.time >= _lastJumpTime + _jumpCooldown)
            {
                _rigidbody.AddForce(Vector3.up * _jumpImpulse, ForceMode.VelocityChange);
                _lastJumpTime = Time.time;
                _jumpHoldTime = 0f;
                _jumpInProgress = true;

                // Let the water stop dragging the hull down while it climbs out, otherwise the
                // buoyancy damping eats most of the impulse before the boat clears the surface.
                if (_buoyancy != null)
                {
                    _buoyancy.NotifyJump();
                }
            }

            if (!_jumpInProgress)
            {
                return;
            }

            if (_inputSource.JumpHeld && _jumpHoldTime < _maxJumpHoldTime)
            {
                _rigidbody.AddForce(Vector3.up * _jumpHoldAcceleration, ForceMode.Acceleration);
                _jumpHoldTime += Time.fixedDeltaTime;
                return;
            }

            if (!_inputSource.JumpHeld && _rigidbody.linearVelocity.y > 0f)
            {
                Vector3 velocity = _rigidbody.linearVelocity;
                velocity.y *= _jumpReleaseMultiplier;
                _rigidbody.linearVelocity = velocity;
            }

            _jumpInProgress = false;
        }

        private void ApplyForwardForce(float throttle)
        {
            if (Mathf.Approximately(throttle, 0f))
            {
                return;
            }

            float acceleration = throttle >= 0f ? _forwardAcceleration : _reverseAcceleration;
            _rigidbody.AddForce(transform.forward * (throttle * acceleration), ForceMode.Acceleration);
        }

        private void ApplySteering(float steering)
        {
            if (Mathf.Approximately(steering, 0f))
            {
                return;
            }

            float forwardFactor = Mathf.Clamp01(Mathf.Abs(Vector3.Dot(_rigidbody.linearVelocity.normalized, transform.forward)));
            _rigidbody.AddTorque(Vector3.up * (steering * _steeringTorque * Mathf.Max(forwardFactor, 0.25f)), ForceMode.Acceleration);
        }

        private void ClampPlanarVelocity()
        {
            float verticalSpeed = _rigidbody.linearVelocity.y;
            Vector3 planarVelocity = Vector3.ProjectOnPlane(_rigidbody.linearVelocity, Vector3.up);
            float forwardSpeed = Vector3.Dot(planarVelocity, transform.forward);
            float maximumSpeed = forwardSpeed >= 0f ? _maxForwardSpeed : _maxReverseSpeed;

            if (planarVelocity.sqrMagnitude > maximumSpeed * maximumSpeed)
            {
                planarVelocity = planarVelocity.normalized * maximumSpeed;
            }

            _rigidbody.linearVelocity = new Vector3(planarVelocity.x, verticalSpeed, planarVelocity.z);
        }
    }
}
