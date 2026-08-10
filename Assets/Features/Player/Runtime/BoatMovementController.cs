using Bato.Water;
using UnityEngine;

namespace Features.Player
{
    /// <summary>
    /// Pilotage arcade inspiré Rocket League : accélération nerveuse, braquage snappy,
    /// saut + dodge (2e saut directionnel en l'air).
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class BoatMovementController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PlayerInputSource _inputSource;
        [SerializeField] private BoatBuoyancy _buoyancy;

        [Header("Throttle (RL feel)")]
        [SerializeField, Min(0f)] private float _forwardAcceleration = 48f;
        [SerializeField, Min(0f)] private float _reverseAcceleration = 36f;
        [SerializeField, Min(0f)] private float _brakeAcceleration = 55f;
        [SerializeField, Min(0f)] private float _maxForwardSpeed = 22f;
        [SerializeField, Min(0f)] private float _maxReverseSpeed = 12f;
        [SerializeField, Min(0f)] private float _linearDamping = 0.35f;
        [SerializeField, Min(0f)] private float _coastDamping = 1.4f;

        [Header("Steering")]
        [SerializeField, Min(0f)] private float _steeringTorque = 36f;
        [SerializeField, Min(0f)] private float _angularDamping = 7f;
        [SerializeField, Range(0.25f, 1f)] private float _minSteerFactor = 0.85f;
        [Tooltip("À haute vitesse le braquage baisse un peu (1 = pas de perte).")]
        [SerializeField, Range(0.3f, 1f)] private float _highSpeedSteerFactor = 0.7f;

        [Header("Jump")]
        [SerializeField, Min(0f)] private float _jumpImpulse = 12f;
        [SerializeField, Min(0f)] private float _doubleJumpImpulse = 10f;
        [SerializeField, Min(0f)] private float _airControl = 0.75f;
        [SerializeField, Min(0f)] private float _jumpCooldown = 0.15f;
        [SerializeField, Min(0.2f)] private float _dodgeWindow = 1.25f;
        [SerializeField, Min(1f)] private float _ascentGravityMultiplier = 1.6f;
        [SerializeField, Min(1f)] private float _fallGravityMultiplier = 4.2f;
        [SerializeField, Min(0f)] private float _airLinearDamping = 0.02f;

        [Header("Dodge / Flip")]
        [SerializeField, Min(0.1f)] private float _dodgeStickThreshold = 0.35f;
        [SerializeField, Min(0f)] private float _dodgeTorque = 14f;
        [SerializeField, Min(0f)] private float _dodgeBoost = 32f;
        [SerializeField, Min(0.2f)] private float _dodgeDuration = 0.45f;

        private Rigidbody _rigidbody;
        private float _lastJumpTime = -Mathf.Infinity;
        private float _firstJumpTime = -Mathf.Infinity;
        private bool _hasSecondJump = true;
        private bool _isDodging;
        private float _dodgeEndTime;
        private Vector3 _dodgeAxis;
        private float _nextInputLogTime;

        /// <summary>1 = normal, &lt;1 = ralenti (ex. boulet chaîne).</summary>
        public float SpeedMultiplier { get; set; } = 1f;

        public bool IsDodging => _isDodging;

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();

            if (_inputSource == null)
                _inputSource = GetComponent<PlayerInputSource>();

            if (_buoyancy == null)
                _buoyancy = GetComponent<BoatBuoyancy>();

            _rigidbody.useGravity = true;
            _rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            _rigidbody.linearDamping = _linearDamping;
            _rigidbody.angularDamping = _angularDamping;

            _rigidbody.constraints = _buoyancy != null
                ? RigidbodyConstraints.None
                : RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        }

        private void FixedUpdate()
        {
            if (_inputSource == null) return;

            Vector2 input = _inputSource.MoveInput;
            if (Time.time >= _nextInputLogTime && input.sqrMagnitude > 0.001f)
            {
                _nextInputLogTime = Time.time + 0.5f;
            }
            bool isInWater = _buoyancy != null && _buoyancy.IsInWater;
            bool canJump = _buoyancy == null || isInWater;
            float controlMultiplier = isInWater ? 1f : _airControl;

            if (isInWater)
            {
                _hasSecondJump = true;
                float throttle = input.y;
                bool coasting = Mathf.Abs(throttle) < 0.05f;
                _rigidbody.linearDamping = coasting ? _coastDamping : _linearDamping;
            }
            else
            {
                _rigidbody.linearDamping = _airLinearDamping;
            }

            TickDodge();
            HandleJumpAndDodge(canJump, isInWater, input);
            ApplyAirGravity(isInWater);
            ApplyForwardForce(input.y * controlMultiplier);
            ApplySteering(input.x * controlMultiplier);
            ClampPlanarVelocity();
        }

        private void HandleJumpAndDodge(bool canJump, bool isInWater, Vector2 input)
        {
            if (!_inputSource.ConsumeJumpPressed()) return;
            if (Time.time < _lastJumpTime + _jumpCooldown) return;

            // En l'air : 2e saut = dodge directionnel ou double jump vertical (fenêtre RL).
            if (!isInWater && _hasSecondJump && Time.time <= _firstJumpTime + _dodgeWindow)
            {
                _hasSecondJump = false;
                _lastJumpTime = Time.time;

                if (input.sqrMagnitude >= _dodgeStickThreshold * _dodgeStickThreshold)
                    StartDodge(input.normalized);
                else
                    DoDoubleJump();

                return;
            }

            // Au sol / sur l'eau : premier saut.
            if (!canJump) return;

            Vector3 v = _rigidbody.linearVelocity;
            if (v.y < 0f) v.y = 0f;
            _rigidbody.linearVelocity = v;

            _rigidbody.AddForce(Vector3.up * _jumpImpulse, ForceMode.VelocityChange);
            _lastJumpTime = Time.time;
            _firstJumpTime = Time.time;
            _hasSecondJump = true;

            if (_buoyancy != null)
                _buoyancy.NotifyJump();
        }

        private void DoDoubleJump()
        {
            Vector3 v = _rigidbody.linearVelocity;
            if (v.y < 0f) v.y = 0f;
            _rigidbody.linearVelocity = v;
            _rigidbody.AddForce(Vector3.up * _doubleJumpImpulse, ForceMode.VelocityChange);
        }

        private void StartDodge(Vector2 stick)
        {
            // stick.y = Z/S (avant/arrière), stick.x = Q/D (gauche/droite).
            // Pitch OK avec +right * y ; roll latéral inversé → -forward * x.
            _dodgeAxis = (transform.right * stick.y - transform.forward * stick.x).normalized;
            if (_dodgeAxis.sqrMagnitude < 0.01f)
                _dodgeAxis = transform.right;

            // Impulsion RL : dash dans la direction du stick (même sens que le salto).
            Vector3 boostDir = Vector3.ProjectOnPlane(
                transform.forward * stick.y + transform.right * stick.x,
                Vector3.up);
            if (boostDir.sqrMagnitude > 0.01f)
            {
                boostDir.Normalize();

                Vector3 v = _rigidbody.linearVelocity;
                float up = v.y;
                Vector3 planar = Vector3.ProjectOnPlane(v, Vector3.up);

                // Ajoute une vraie poussée par-dessus la vitesse actuelle.
                planar += boostDir * _dodgeBoost;

                _rigidbody.linearVelocity = new Vector3(planar.x, up, planar.z);
            }

            // Petit pop vertical pour dégager l'eau.
            _rigidbody.AddForce(Vector3.up * (_jumpImpulse * 0.15f), ForceMode.VelocityChange);

            _isDodging = true;
            _dodgeEndTime = Time.time + _dodgeDuration;
            _buoyancy?.NotifyFlip(_dodgeDuration + 0.15f);
        }

        private void TickDodge()
        {
            if (!_isDodging) return;

            if (Time.time >= _dodgeEndTime)
            {
                _isDodging = false;
                return;
            }

            // ~1 tour pendant la durée du dodge.
            float radPerSec = (Mathf.PI * 2f) / Mathf.Max(0.05f, _dodgeDuration);
            _rigidbody.AddTorque(_dodgeAxis * (radPerSec * _dodgeTorque * 0.12f), ForceMode.Acceleration);

            // Maintient une vitesse angulaire cible sur l'axe du flip.
            Vector3 ang = _rigidbody.angularVelocity;
            float along = Vector3.Dot(ang, _dodgeAxis);
            float target = radPerSec;
            _rigidbody.angularVelocity = ang + _dodgeAxis * (target - along);
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

            float mul = Mathf.Max(0.05f, SpeedMultiplier);
            float forwardSpeed = Vector3.Dot(
                Vector3.ProjectOnPlane(_rigidbody.linearVelocity, Vector3.up),
                transform.forward);

            // Frein RL si on pousse à l'opposé de la vitesse.
            bool braking = (throttle > 0.05f && forwardSpeed < -0.5f) ||
                           (throttle < -0.05f && forwardSpeed > 0.5f);

            float acceleration = braking
                ? _brakeAcceleration
                : (throttle >= 0f ? _forwardAcceleration : _reverseAcceleration);

            _rigidbody.AddForce(transform.forward * (throttle * acceleration * mul), ForceMode.Acceleration);
        }

        private void ApplySteering(float steering)
        {
            if (Mathf.Approximately(steering, 0f) || _isDodging) return;

            Vector3 planar = Vector3.ProjectOnPlane(_rigidbody.linearVelocity, Vector3.up);
            float speed = planar.magnitude;
            float forwardFactor = speed > 0.05f
                ? Mathf.Clamp01(Mathf.Abs(Vector3.Dot(planar.normalized, transform.forward)))
                : 1f;

            float speed01 = Mathf.Clamp01(speed / Mathf.Max(1f, _maxForwardSpeed));
            float speedSteer = Mathf.Lerp(1f, _highSpeedSteerFactor, speed01);

            float steerPower = Mathf.Max(forwardFactor, _minSteerFactor) * speedSteer;
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

            // Pendant un dodge : laisse le dash dépasser la vitesse max.
            if (_isDodging) maximumSpeed = Mathf.Max(maximumSpeed + _dodgeBoost, _dodgeBoost);

            if (planarVelocity.sqrMagnitude > maximumSpeed * maximumSpeed)
                planarVelocity = planarVelocity.normalized * maximumSpeed;

            _rigidbody.linearVelocity = new Vector3(planarVelocity.x, verticalSpeed, planarVelocity.z);
        }
    }
}
