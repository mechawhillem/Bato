using Bato.Water;
using UnityEngine;

namespace Features.Player
{
    /// <summary>
    /// Pilotage arcade très vif : accélération forte, braquage immédiat même à l'arrêt.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class BoatMovementController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PlayerInputSource _inputSource;
        [SerializeField] private BoatBuoyancy _buoyancy;

        [Header("Movement")]
        [SerializeField, Min(0f)] private float _forwardAcceleration = 38f;
        [SerializeField, Min(0f)] private float _reverseAcceleration = 24f;
        [SerializeField, Min(0f)] private float _maxForwardSpeed = 18f;
        [SerializeField, Min(0f)] private float _maxReverseSpeed = 10f;
        [SerializeField, Min(0f)] private float _linearDamping = 0.7f;

        [Header("Steering")]
        [SerializeField, Min(0f)] private float _steeringTorque = 52f;
        [SerializeField, Min(0f)] private float _angularDamping = 6f;
        [Tooltip("1 = braquage full même à l'arrêt.")]
        [SerializeField, Range(0.25f, 1f)] private float _minSteerFactor = 1f;

        [Header("Jump")]
        [SerializeField, Min(0f)] private float _jumpImpulse = 14f;
        [SerializeField, Min(0f)] private float _airControl = 0.85f;
        [SerializeField, Min(0f)] private float _jumpCooldown = 0.2f;
        [Tooltip("Gravité en l'air pendant la montée (1 = normale). Un peu >1 = apex plus net.")]
        [SerializeField, Min(1f)] private float _ascentGravityMultiplier = 1.35f;
        [Tooltip("Gravité en l'air pendant la descente — plus haut = retombée nette.")]
        [SerializeField, Min(1f)] private float _fallGravityMultiplier = 3.8f;
        [Tooltip("Damping en l'air (évite le flottement lunaire sur Y).")]
        [SerializeField, Min(0f)] private float _airLinearDamping = 0.02f;

        private Rigidbody _rigidbody;
        private float _lastJumpTime = -Mathf.Infinity;

        /// <summary>1 = normal, &lt;1 = ralenti (ex. boulet chaîne).</summary>
        public float SpeedMultiplier { get; set; } = 1f;

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();

            if (_inputSource == null)
                _inputSource = GetComponent<PlayerInputSource>();

            if (_buoyancy == null)
                _buoyancy = GetComponent<BoatBuoyancy>();

            // La flottaison réactive gravité + contraintes dans son FixedUpdate.
            _rigidbody.useGravity = true;
            _rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            _rigidbody.linearDamping = _linearDamping;
            _rigidbody.angularDamping = _angularDamping;
            if (_buoyancy == null)
            {
                _rigidbody.constraints =
                    RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            }
        }

        private void FixedUpdate()
        {
            if (_inputSource == null) return;

            Vector2 input = _inputSource.MoveInput;
            bool canJump = _buoyancy == null || _buoyancy.IsInWater;
            bool isInWater = _buoyancy != null && _buoyancy.IsInWater;
            float controlMultiplier = isInWater ? 1f : _airControl;

            // En l'air : presque pas de damping (sinon sensation lunaire sur Y).
            _rigidbody.linearDamping = isInWater ? _linearDamping : _airLinearDamping;

            HandleJump(canJump);
            ApplyAirGravity(isInWater);
            ApplyForwardForce(input.y * controlMultiplier);
            ApplySteering(input.x * controlMultiplier);
            ClampPlanarVelocity();
        }

        private void HandleJump(bool canJump)
        {
            if (!canJump ||
                !_inputSource.ConsumeJumpPressed() ||
                Time.time < _lastJumpTime + _jumpCooldown)
            {
                return;
            }

            Vector3 v = _rigidbody.linearVelocity;
            if (v.y < 0f) v.y = 0f;
            _rigidbody.linearVelocity = v;

            _rigidbody.AddForce(Vector3.up * _jumpImpulse, ForceMode.VelocityChange);
            _lastJumpTime = Time.time;
        }

        private void ApplyAirGravity(bool isInWater)
        {
            if (isInWater) return;

            float multiplier = _rigidbody.linearVelocity.y > 0f
                ? _ascentGravityMultiplier
                : _fallGravityMultiplier;

            if (multiplier <= 1f) return;

            float extra = Physics.gravity.y * (multiplier - 1f);
            _rigidbody.AddForce(Vector3.up * extra, ForceMode.Acceleration);
        }

        private void ApplyForwardForce(float throttle)
        {
            if (Mathf.Approximately(throttle, 0f)) return;

            float acceleration = throttle >= 0f ? _forwardAcceleration : _reverseAcceleration;
            float mul = Mathf.Max(0.05f, SpeedMultiplier);
            _rigidbody.AddForce(transform.forward * (throttle * acceleration * mul), ForceMode.Acceleration);
        }

        private void ApplySteering(float steering)
        {
            if (Mathf.Approximately(steering, 0f)) return;

            // Arcade : braquage quasi indépendant de la vitesse.
            float speed = Vector3.ProjectOnPlane(_rigidbody.linearVelocity, Vector3.up).magnitude;
            float forwardFactor = speed > 0.05f
                ? Mathf.Clamp01(Mathf.Abs(Vector3.Dot(_rigidbody.linearVelocity.normalized, transform.forward)))
                : 1f;

            float steerPower = Mathf.Max(forwardFactor, _minSteerFactor);
            float mul = Mathf.Max(0.05f, SpeedMultiplier);
            _rigidbody.AddTorque(Vector3.up * (steering * _steeringTorque * steerPower * mul), ForceMode.Acceleration);
        }

        private void ClampPlanarVelocity()
        {
            float verticalSpeed = _rigidbody.linearVelocity.y;
            Vector3 planarVelocity = Vector3.ProjectOnPlane(_rigidbody.linearVelocity, Vector3.up);
            float forwardSpeed = Vector3.Dot(planarVelocity, transform.forward);
            float mul = Mathf.Max(0.05f, SpeedMultiplier);
            float maximumSpeed = (forwardSpeed >= 0f ? _maxForwardSpeed : _maxReverseSpeed) * mul;

            if (planarVelocity.sqrMagnitude > maximumSpeed * maximumSpeed)
                planarVelocity = planarVelocity.normalized * maximumSpeed;

            _rigidbody.linearVelocity = new Vector3(planarVelocity.x, verticalSpeed, planarVelocity.z);
        }
    }
}
