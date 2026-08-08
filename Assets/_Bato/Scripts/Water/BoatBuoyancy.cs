using UnityEngine;

namespace Bato.Water
{
    /// <summary>
    /// Flottaison par sondes : quelques points répartis sur la coque, chacun poussé vers le haut
    /// proportionnellement à sa profondeur sous l'eau. Comme les forces s'appliquent en des points
    /// décalés du centre de masse, le roulis et le tangage sortent gratuitement de la physique —
    /// on ne les anime pas.
    ///
    /// Ce composant ne tourne que chez le propriétaire du bateau. Les autres clients voient un
    /// Rigidbody kinematic piloté par le NetworkTransform : la houle qu'ils affichent est la même
    /// (fonction pure du temps serveur), donc leur bateau distant tangue en cohérence sans qu'on
    /// ait à répliquer quoi que ce soit.
    ///
    /// Activé par BoatNetworkAuthority au spawn, pas dans le prefab.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class BoatBuoyancy : MonoBehaviour
    {
        [Header("Sondes")]
        [Tooltip("Points de flottaison en espace local. 4 coins = roulis + tangage.")]
        [SerializeField] Vector3[] m_ProbeOffsets =
        {
            new Vector3(-0.7f, 0f,  1.7f),
            new Vector3( 0.7f, 0f,  1.7f),
            new Vector3(-0.7f, 0f, -1.7f),
            new Vector3( 0.7f, 0f, -1.7f),
        };

        [Header("Poussée")]
        [Tooltip("Hauteur sur laquelle la poussée passe de 0 à 100 %. Plus c'est petit, plus le bateau est raide.")]
        [SerializeField, Min(0.05f)] float m_ProbeDepth = 1f;

        [Tooltip("1 = le bateau flotte pile à la ligne de flottaison. Au-dessus il rebondit plus haut.")]
        [SerializeField, Min(0f)] float m_BuoyancyStrength = 1.6f;

        [Header("Amortissement")]
        [Tooltip("Freine le mouvement vertical dans l'eau. Sans lui le bateau oscille sans fin.")]
        [SerializeField, Min(0f)] float m_VerticalDrag = 2.2f;

        [Tooltip("Freine les rotations dans l'eau (roulis / tangage).")]
        [SerializeField, Min(0f)] float m_AngularDrag = 1.4f;

        [Header("Stabilité")]
        [Tooltip("Couple de redressement : empêche de finir la quille en l'air.")]
        [SerializeField, Min(0f)] float m_UprightTorque = 3.5f;

        [Tooltip("Aligne le bateau sur la pente de la vague. 0 = ignore la houle, 1 = colle à la surface.")]
        [SerializeField, Range(0f, 1f)] float m_WaveAlignment = 0.35f;

        Rigidbody m_Rigidbody;

        /// <summary>Vrai si au moins une sonde touche l'eau — utile pour couper la propulsion en l'air.</summary>
        public bool IsInWater { get; private set; }

        void Awake() => m_Rigidbody = GetComponent<Rigidbody>();

        void OnEnable()
        {
            // BoatMovementController.Awake fige Y et le roulis pour un bateau « posé » sur une mer
            // plate. Avec de la houle il faut justement ces degrés de liberté : on les rend ici,
            // c'est-à-dire après son Awake, et uniquement chez le propriétaire.
            m_Rigidbody.useGravity = true;
            m_Rigidbody.constraints = RigidbodyConstraints.None;
        }

        void FixedUpdate()
        {
            var field = WaveField.Instance;
            if (field == null || m_Rigidbody.isKinematic || m_ProbeOffsets.Length == 0) return;

            int probeCount = m_ProbeOffsets.Length;
            float perProbeMass = m_Rigidbody.mass / probeCount;
            float submergedProbes = 0f;

            foreach (var offset in m_ProbeOffsets)
            {
                var probeWorld = transform.TransformPoint(offset);
                float waterHeight = field.SampleHeight(probeWorld);
                float depth = waterHeight - probeWorld.y;

                if (depth <= 0f) continue;
                submergedProbes++;

                float submersion = Mathf.Clamp01(depth / m_ProbeDepth);

                // Poussée d'Archimède : de quoi compenser le poids de la part de coque immergée,
                // multipliée par la raideur voulue.
                var buoyantForce = Vector3.up *
                    (submersion * perProbeMass * -Physics.gravity.y * m_BuoyancyStrength);

                // Traînée verticale locale, sinon le bateau oscille indéfiniment.
                float pointVerticalSpeed = m_Rigidbody.GetPointVelocity(probeWorld).y;
                var dragForce = Vector3.up *
                    (-pointVerticalSpeed * submersion * perProbeMass * m_VerticalDrag);

                m_Rigidbody.AddForceAtPosition(buoyantForce + dragForce, probeWorld, ForceMode.Force);
            }

            IsInWater = submergedProbes > 0f;
            if (!IsInWater) return;

            float immersionRatio = submergedProbes / probeCount;
            ApplyAngularDamping(immersionRatio);
            ApplyRighting(field, immersionRatio);
        }

        void ApplyAngularDamping(float immersionRatio)
        {
            m_Rigidbody.AddTorque(
                -m_Rigidbody.angularVelocity * (m_AngularDrag * immersionRatio),
                ForceMode.Acceleration);
        }

        /// <summary>
        /// Redresse le bateau vers la verticale, ou vers la normale de la vague si on veut qu'il
        /// épouse la houle. Purement correctif : les sondes font déjà l'essentiel du travail.
        /// </summary>
        void ApplyRighting(WaveField field, float immersionRatio)
        {
            var waveNormal = field.SampleNormal(transform.position);
            var targetUp = Vector3.Slerp(Vector3.up, waveNormal, m_WaveAlignment).normalized;

            var correction = Vector3.Cross(transform.up, targetUp);
            m_Rigidbody.AddTorque(
                correction * (m_UprightTorque * immersionRatio),
                ForceMode.Acceleration);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.9f);
            foreach (var offset in m_ProbeOffsets)
            {
                Gizmos.DrawWireSphere(transform.TransformPoint(offset), 0.18f);
            }
        }
    }
}
