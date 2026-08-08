using System.Collections.Generic;
using UnityEngine;

namespace Features.Player
{
    /// <summary>
    /// Cosmetic lean: the hull rears up under throttle, noses down when braking, and banks into
    /// its turns, all on a slightly springy suspension.
    ///
    /// This never touches the Rigidbody. Two reasons, and both matter:
    ///
    ///  • BoatBuoyancy applies a righting torque proportional to the tilt angle. Any roll pushed
    ///    in through the physics would be actively fought, so it would need to out-muscle the
    ///    righting — at which point a hard turn could capsize the boat. Leaning the visuals costs
    ///    nothing and cannot destabilise the simulation.
    ///  • The motion is measured off the transform rather than the Rigidbody, so it runs on every
    ///    client for every boat. Remote hulls are kinematic and report zero velocity, but their
    ///    transform is synced — so everyone sees everyone else bank, without replicating a thing.
    ///
    /// The lean is applied to <see cref="_leanTargets"/>, which orbit a pivot on the boat's
    /// centreline instead of spinning around their own origins — otherwise deck-mounted pieces
    /// would rotate in place and tear away from the hull.
    ///
    /// Deliberately NOT auto-detected: BoatNameplate and BoatWake add their own children at
    /// runtime, and a nameplate that heels over with the boat looks broken. Fill the list from
    /// the context menu instead — in the prefab, where only the real visuals exist.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BoatJuice : MonoBehaviour
    {
        // A frame hitch must not launch the spring into orbit.
        private const float MaxTimeStep = 1f / 30f;

        [Header("What leans")]
        [Tooltip("Visual children to tilt: the hull, the cannons, anything bolted to the deck. " +
                 "Use the context menu \"Remplir avec les enfants visuels\" to fill this in.")]
        [SerializeField] private Transform[] _leanTargets;

        [Tooltip("Height of the pivot the visuals rotate around, in local units. 0 is the boat " +
                 "origin, which buoyancy already parks on the waterline — the natural place for a " +
                 "hull to heel about.")]
        [SerializeField] private float _pivotHeight;

        // Everything below is expressed in DEGREES AT FULL EFFECT, and "full" is defined by the
        // reference block further down. Set a value to 8 and you get 8° when the boat is pushing
        // as hard as it can — no unit conversion to do in your head.

        [Header("Cabrage — la proue se lève")]
        [Tooltip("Degrees of lean-back at full acceleration. The kick: it hits when the throttle " +
                 "opens, and reverses into a nose-dive under braking.")]
        [SerializeField] private float _leanBackOnThrottle = 7f;

        [Tooltip("Degrees of lean-back held at top speed. The part that STAYS while you cruise — " +
                 "raise this one if the boat only twitches when you start moving.")]
        [SerializeField] private float _leanBackAtTopSpeed = 5f;

        [SerializeField, Min(0f)] private float _maxPitch = 15f;

        [Header("Gîte — roulis en virage")]
        [Tooltip("Degrees of bank at the reference turn rate. Negative heels outwards instead, " +
                 "the way a heavy displacement hull would.")]
        [SerializeField] private float _bankInTurn = 16f;

        [Tooltip("How much speed is required to bank. 0 = full bank even turning on the spot, " +
                 "1 = no bank at all below top speed.")]
        [SerializeField, Range(0f, 1f)] private float _bankSpeedInfluence = 0.6f;

        [SerializeField, Min(0f)] private float _maxRoll = 22f;

        [Header("Références — ce que « à fond » veut dire")]
        [Tooltip("Acceleration counted as full throttle, in m/s². Match BoatMovementController's " +
                 "Forward Acceleration (20 on Boat 1).")]
        [SerializeField, Min(0.1f)] private float _referenceAcceleration = 20f;

        [Tooltip("Speed counted as top speed, in m/s. Match Max Forward Speed (15 on Boat 1).")]
        [SerializeField, Min(0.1f)] private float _referenceSpeed = 15f;

        [Tooltip("Turn rate counted as a hard turn, in °/s. Tick Log Motion below and steer hard " +
                 "to read the real figure for your boat.")]
        [SerializeField, Min(1f)] private float _referenceTurnRate = 50f;

        [Header("Elasticity")]
        [Tooltip("How quickly the hull chases its target attitude, in Hz. Higher is snappier.")]
        [SerializeField, Min(0.1f)] private float _frequency = 1.8f;

        [Tooltip("1 = no overshoot at all. Below that the hull rocks past and settles back — " +
                 "that little wobble is the whole point. Keep it high enough to stay subtle.")]
        [SerializeField, Range(0.15f, 1f)] private float _dampingRatio = 0.55f;

        [Tooltip("Smoothing applied to the measured motion, in seconds. Acceleration read from " +
                 "position differences is noisy, and interpolated remote boats are worse. Too " +
                 "high and the throttle kick is smoothed away before it reaches the spring.")]
        [SerializeField, Min(0f)] private float _motionSmoothing = 0.06f;

        [Header("Réglage")]
        [Tooltip("Logs the measured acceleration and turn rate twice a second, so you can set " +
                 "the reference values above from what your boat actually does.")]
        [SerializeField] private bool _logMotion;

        private Vector3[] _restPositions;
        private Quaternion[] _restRotations;

        private Vector3 _previousPosition;
        private Vector3 _previousHeading;
        private float _previousForwardSpeed;

        private float _acceleration;
        private float _turnRate;

        private float _pitch;
        private float _pitchVelocity;
        private float _roll;
        private float _rollVelocity;
        private float _nextLogTime;

        /// <summary>Smoothed longitudinal acceleration, m/s². Positive means speeding up.</summary>
        public float MeasuredAcceleration => _acceleration;

        /// <summary>Smoothed yaw rate, °/s. Positive means turning to starboard.</summary>
        public float MeasuredTurnRate => _turnRate;

        private void Awake()
        {
            if (_leanTargets == null || _leanTargets.Length == 0)
            {
                Debug.LogWarning(
                    $"[Bato] BoatJuice sur '{name}' n'a aucune cible : rien ne penchera. " +
                    "Renseigne « Lean Targets » (clic droit sur le composant → " +
                    "« Remplir avec les enfants visuels »).", this);
                enabled = false;
                return;
            }

            CacheRestPose();
            _previousPosition = transform.position;
            _previousHeading = FlatHeading(_previousHeading);
        }

        /// <summary>
        /// The pose the visuals sit in when the boat is level. Everything is expressed as an
        /// offset from it, so the component owns the lean and nothing else.
        /// </summary>
        private void CacheRestPose()
        {
            _restPositions = new Vector3[_leanTargets.Length];
            _restRotations = new Quaternion[_leanTargets.Length];

            for (int i = 0; i < _leanTargets.Length; i++)
            {
                Transform target = _leanTargets[i];
                if (target == null)
                {
                    _restRotations[i] = Quaternion.identity;
                    continue;
                }

                _restPositions[i] = target.localPosition;
                _restRotations[i] = target.localRotation;
            }
        }

        private void OnDisable()
        {
            // Put the hull back down straight, otherwise a boat frozen mid-heel keeps the tilt.
            _pitch = _roll = _pitchVelocity = _rollVelocity = 0f;
            if (_restPositions != null) ApplyLean(Quaternion.identity);
        }

        private void LateUpdate()
        {
            float deltaTime = Mathf.Min(Time.deltaTime, MaxTimeStep);
            if (deltaTime <= 0f) return;

            MeasureMotion(deltaTime, out float forwardSpeed);

            // Each measurement becomes a -1..1 fraction of "full", so the tuning fields below are
            // read directly as degrees.
            float throttleRatio = Mathf.Clamp(_acceleration / _referenceAcceleration, -1f, 1f);
            float speedRatio = Mathf.Clamp(forwardSpeed / _referenceSpeed, -1f, 1f);
            float turnRatio = Mathf.Clamp(_turnRate / _referenceTurnRate, -1f, 1f);

            // Banking fades in with speed, but only as far as the influence dial says: at 0 the
            // boat heels just as hard pivoting on the spot, which is wrong physically and often
            // exactly what an arcade boat wants.
            float bankFactor = Mathf.Lerp(1f, Mathf.Abs(speedRatio), _bankSpeedInfluence);

            // Unity's conventions run opposite to the intent on both axes: a positive pitch dips
            // the bow, and a positive roll lifts the starboard side. Hence the negations — the
            // boat rears up as it accelerates, and drops its inner rail into the turn.
            float targetPitch = -(throttleRatio * _leanBackOnThrottle + speedRatio * _leanBackAtTopSpeed);
            float targetRoll = -turnRatio * _bankInTurn * bankFactor;

            // Clamping the target, not the result, is what keeps a collision spike from throwing
            // the model on its side: the spring can overshoot the limit a little, but it is never
            // aimed past it.
            targetPitch = Mathf.Clamp(targetPitch, -_maxPitch, _maxPitch);
            targetRoll = Mathf.Clamp(targetRoll, -_maxRoll, _maxRoll);

            Spring(ref _pitch, ref _pitchVelocity, targetPitch, deltaTime);
            Spring(ref _roll, ref _rollVelocity, targetRoll, deltaTime);

            ApplyLean(Quaternion.Euler(_pitch, 0f, _roll));
            LogMotion(forwardSpeed);
        }

        /// <summary>
        /// Forward speed, acceleration and turn rate, read off the transform so this works just as
        /// well on a remote boat whose Rigidbody is kinematic and reports nothing.
        /// </summary>
        private void MeasureMotion(float deltaTime, out float forwardSpeed)
        {
            Vector3 position = transform.position;
            Vector3 velocity = (position - _previousPosition) / deltaTime;
            _previousPosition = position;

            forwardSpeed = Vector3.Dot(velocity, transform.forward);

            float rawAcceleration = (forwardSpeed - _previousForwardSpeed) / deltaTime;
            _previousForwardSpeed = forwardSpeed;

            // Yaw from the heading vector rather than eulerAngles.y, which flips around in ways
            // that would read as a violent turn the moment the hull rolls far enough.
            Vector3 heading = FlatHeading(_previousHeading);
            float rawTurnRate = Vector3.SignedAngle(_previousHeading, heading, Vector3.up) / deltaTime;
            _previousHeading = heading;

            float blend = _motionSmoothing > 0f
                ? 1f - Mathf.Exp(-deltaTime / _motionSmoothing)
                : 1f;

            _acceleration = Mathf.Lerp(_acceleration, rawAcceleration, blend);
            _turnRate = Mathf.Lerp(_turnRate, rawTurnRate, blend);
        }

        /// <summary>
        /// Prints what the boat is actually doing, so the reference values can be set from
        /// measurements instead of guesswork. Drive flat out, read Accel and Speed; steer hard,
        /// read Turn; copy the peaks into the reference block.
        /// </summary>
        private void LogMotion(float forwardSpeed)
        {
            if (!_logMotion || Time.time < _nextLogTime) return;
            _nextLogTime = Time.time + 0.5f;

            Debug.Log($"[Bato] {name} — accel {_acceleration,6:0.0} m/s²  |  vitesse {forwardSpeed,5:0.0} m/s  " +
                      $"|  virage {_turnRate,6:0.0} °/s  →  cabrage {_pitch,5:0.0}°  gîte {_roll,5:0.0}°", this);
        }

        private Vector3 FlatHeading(Vector3 fallback)
        {
            Vector3 heading = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (heading.sqrMagnitude > 1e-6f) return heading.normalized;
            return fallback.sqrMagnitude > 1e-6f ? fallback : Vector3.forward;
        }

        /// <summary>
        /// Damped harmonic oscillator, integrated semi-implicitly. A damping ratio under 1 lets
        /// the hull sail past its target and rock back — the elasticity, in one line of physics
        /// rather than a hand-tuned curve.
        /// </summary>
        private void Spring(ref float current, ref float velocity, float target, float deltaTime)
        {
            float omega = 2f * Mathf.PI * _frequency;
            float acceleration = (target - current) * omega * omega
                                 - velocity * (2f * _dampingRatio * omega);

            velocity += acceleration * deltaTime;
            current += velocity * deltaTime;
        }

        /// <summary>
        /// Rotates the visuals about a shared pivot on the centreline. Setting localRotation alone
        /// would spin each piece around its own origin, and the cannons would swing off the deck.
        /// </summary>
        private void ApplyLean(Quaternion lean)
        {
            var pivot = new Vector3(0f, _pivotHeight, 0f);

            for (int i = 0; i < _leanTargets.Length; i++)
            {
                Transform target = _leanTargets[i];
                if (target == null) continue;

                target.SetLocalPositionAndRotation(
                    pivot + lean * (_restPositions[i] - pivot),
                    lean * _restRotations[i]);
            }
        }

        [ContextMenu("Remplir avec les enfants visuels")]
        private void FillFromChildren()
        {
            var found = new List<Transform>();

            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);
                if (child.GetComponentInChildren<Renderer>(true) != null) found.Add(child);
            }

            _leanTargets = found.ToArray();

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
            Debug.Log($"[Bato] BoatJuice : {_leanTargets.Length} cible(s) trouvée(s) sur '{name}'.", this);
        }
    }
}
