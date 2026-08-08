using UnityEngine;

namespace Features.Player
{
    /// <summary>
    /// Applies boat-like acceleration and steering to a Rigidbody.
    /// Input is supplied by a separate PlayerInputSource so this component remains network-agnostic.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class BoatMovementController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PlayerInputSource _inputSource;

        [Header("Movement")]
        [SerializeField, Min(0f)] private float _forwardAcceleration = 12f;
        [SerializeField, Min(0f)] private float _reverseAcceleration = 6f;
        [SerializeField, Min(0f)] private float _maxForwardSpeed = 8f;
        [SerializeField, Min(0f)] private float _maxReverseSpeed = 3f;
        [SerializeField, Min(0f)] private float _linearDamping = 1.5f;

        [Header("Steering")]
        [SerializeField, Min(0f)] private float _steeringTorque = 8f;
        [SerializeField, Min(0f)] private float _angularDamping = 3f;

        private Rigidbody _rigidbody;

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();

            if (_inputSource == null)
            {
                _inputSource = GetComponent<PlayerInputSource>();
            }

            _rigidbody.useGravity = false;
            _rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            _rigidbody.linearDamping = _linearDamping;
            _rigidbody.angularDamping = _angularDamping;
            _rigidbody.constraints = RigidbodyConstraints.FreezePositionY | RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        }

        private void FixedUpdate()
        {
            if (_inputSource == null)
            {
                return;
            }

            Vector2 input = _inputSource.MoveInput;
            ApplyForwardForce(input.y);
            ApplySteering(input.x);
            ClampPlanarVelocity();
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
            // La composante verticale appartient à la flottaison (BoatBuoyancy), pas au pilotage :
            // l'écraser à 0 ici empêchait tout ballant sur la houle. On ne borne que le plan.
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
