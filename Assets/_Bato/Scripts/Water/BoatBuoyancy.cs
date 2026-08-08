using UnityEngine;

namespace Bato.Water
{
    /// <summary>
    /// Flottaison par sondes : quelques points répartis sur la coque, chacun poussé vers le haut
    /// proportionnellement à sa profondeur sous l'eau. Comme les forces s'appliquent en des points
    /// décalés du centre de masse, le roulis et le tangage sortent gratuitement de la physique.
    ///
    /// Les sondes sont déduites du collider de coque, pas saisies à la main : des sondes qui ne
    /// correspondent plus à la coque échantillonnent l'eau à des mètres du bateau et produisent
    /// des couples de roulis absurdes.
    ///
    /// La raideur et l'amortissement sont dérivés d'un modèle masse-ressort, pas réglés à
    /// l'oreille : on choisit une force de rappel et un taux d'amortissement, le reste suit.
    ///
    /// Ce composant ne tourne que chez le propriétaire du bateau. Les autres clients voient un
    /// Rigidbody kinematic piloté par le NetworkTransform ; comme la houle est une fonction pure
    /// du temps serveur, leur bateau distant tangue en cohérence sans rien répliquer.
    ///
    /// La hauteur de l'eau vient de <see cref="WaterSurface"/>, pas d'une mer en particulier : la
    /// flottaison marche pareil sur notre houle de Gerstner et sur le volume stylisé de Bitgem.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class BoatBuoyancy : MonoBehaviour
    {
        [Header("Sondes")]
        [Tooltip("Collider qui définit la coque. Vide = le premier collider trouvé sur l'objet.")]
        [SerializeField] Collider m_HullCollider;

        [Tooltip("À quel point les sondes rentrent vers l'intérieur de la coque. 1 = pile aux coins.")]
        [SerializeField, Range(0.2f, 1f)] float m_ProbeInset = 0.85f;

        [Header("Flottaison")]
        [Tooltip("Profondeur à laquelle la poussée atteint son maximum. À monter avec la taille " +
                 "des vagues : trop bas, la réponse sature et le bateau ne fait que du tout ou rien.")]
        [SerializeField, Min(0.1f)] float m_ProbeDepth = 1.5f;

        [Tooltip("Poussée maximale, en multiples du poids. Sous 2, un bateau enfoncé ne remonte " +
                 "pas assez vite et finit par couler.")]
        [SerializeField, Min(1.1f)] float m_BuoyancyStrength = 5f;

        [Tooltip("1 = amortissement critique (aucun rebond). En dessous le bateau oscille.")]
        [SerializeField, Range(0.1f, 1.5f)] float m_DampingRatio = 0.7f;

        [Tooltip("Hauteur de l'origine du bateau par rapport à la ligne de flottaison, à l'arrêt.")]
        [SerializeField] float m_WaterlineOffset = 0f;

        [Header("Stabilité")]
        [Tooltip("Freine le roulis et le tangage dans l'eau.")]
        [SerializeField, Min(0f)] float m_AngularDrag = 2.5f;

        [Tooltip("Couple de redressement : empêche de finir la quille en l'air.")]
        [SerializeField, Min(0f)] float m_UprightTorque = 6f;

        [Tooltip("Aligne le bateau sur la pente de la vague. 0 = reste à plat, 1 = colle à la surface.")]
        [SerializeField, Range(0f, 1f)] float m_WaveAlignment = 0.35f;

        [Tooltip("Part du redressement conservée en l'air, pendant un saut. À 0 le bateau part en " +
                 "vrille dès qu'il décolle et retombe sur le flanc.")]
        [SerializeField, Range(0f, 1f)] float m_AirStabilisation = 0.4f;

        [Header("Saut")]
        [Tooltip("Durée pendant laquelle la traînée verticale de l'eau est suspendue après un " +
                 "saut. Sans elle, l'amortissement freine l'impulsion tant que la coque n'a pas " +
                 "quitté la surface et mange près de la moitié de la hauteur du saut.")]
        [SerializeField, Min(0f)] float m_JumpDampingGrace = 0.3f;

        Rigidbody m_Rigidbody;
        Vector3[] m_Probes;
        float m_EquilibriumSubmersion;
        float m_VerticalDampingCoefficient;
        float m_DampingGraceUntil;
        bool m_WarnedAboutMissingWater;

        /// <summary>Vrai si au moins une sonde touche l'eau.</summary>
        public bool IsInWater { get; private set; }

        /// <summary>Part de la coque dans l'eau, 0 à 1. Utilisé par les effets d'écume.</summary>
        public float SubmergedRatio { get; private set; }

        void Awake()
        {
            m_Rigidbody = GetComponent<Rigidbody>();
            if (m_HullCollider == null) m_HullCollider = GetComponent<Collider>();

            RecalculateProbes();
            RecalculateResponse();
        }

        void OnEnable() => EnsureRigidbodyIsFree();

        /// <summary>
        /// Rend au Rigidbody sa gravité et sa liberté de mouvement.
        ///
        /// BoatMovementController prépare le bateau pour une mer plate quand il n'y a pas de
        /// flottaison. Dès qu'il y en a une, c'est ce composant qui décide — et il le réaffirme à
        /// chaque pas de physique plutôt qu'une fois pour toutes : ça survit à n'importe quel ordre
        /// d'exécution, à un respawn, et surtout à l'absence de mer. Un bateau sans eau doit
        /// tomber, pas rester figé en l'air, incapable de sauter.
        /// </summary>
        void EnsureRigidbodyIsFree()
        {
            if (m_Rigidbody == null || m_Rigidbody.isKinematic) return;
            if (m_Rigidbody.useGravity && m_Rigidbody.constraints == RigidbodyConstraints.None) return;

            m_Rigidbody.useGravity = true;
            m_Rigidbody.constraints = RigidbodyConstraints.None;
        }

        /// <summary>
        /// Prévient la flottaison qu'un saut vient de partir, pour qu'elle relâche sa traînée
        /// verticale le temps que la coque sorte de l'eau. Appelé par le pilotage.
        /// </summary>
        public void NotifyJump() => m_DampingGraceUntil = Time.time + m_JumpDampingGrace;

        void OnValidate()
        {
            if (!Application.isPlaying) return;
            RecalculateProbes();
            RecalculateResponse();
        }

        /// <summary>
        /// Place quatre sondes aux coins de la coque. La hauteur n'est pas libre : elle découle de
        /// la profondeur d'équilibre, pour que le bateau se stabilise pile à sa ligne de flottaison
        /// quelle que soit la raideur choisie.
        /// </summary>
        void RecalculateProbes()
        {
            m_EquilibriumSubmersion = 1f / m_BuoyancyStrength;
            float equilibriumDepth = m_ProbeDepth * m_EquilibriumSubmersion;
            float probeY = -equilibriumDepth - m_WaterlineOffset;

            Vector3 extents = GetLocalHullExtents();
            float x = extents.x * m_ProbeInset;
            float z = extents.z * m_ProbeInset;

            m_Probes = new[]
            {
                new Vector3(-x, probeY,  z),
                new Vector3( x, probeY,  z),
                new Vector3(-x, probeY, -z),
                new Vector3( x, probeY, -z),
            };
        }

        Vector3 GetLocalHullExtents()
        {
            if (m_HullCollider is BoxCollider box)
            {
                return box.size * 0.5f;
            }

            if (m_HullCollider != null)
            {
                // Approximation : bounds est en monde, on repasse en local via l'échelle.
                var scale = transform.lossyScale;
                var worldExtents = m_HullCollider.bounds.extents;
                return new Vector3(
                    worldExtents.x / Mathf.Max(0.0001f, Mathf.Abs(scale.x)),
                    worldExtents.y / Mathf.Max(0.0001f, Mathf.Abs(scale.y)),
                    worldExtents.z / Mathf.Max(0.0001f, Mathf.Abs(scale.z)));
            }

            Debug.LogWarning("[Bato] BoatBuoyancy n'a pas de collider de coque : sondes par défaut.", this);
            return new Vector3(0.8f, 0.6f, 2.2f);
        }

        /// <summary>
        /// Modèle masse-ressort. La poussée agit comme un ressort de pulsation
        /// ω = √(g × force / profondeur) ; on en déduit l'amortissement qui donne le taux voulu.
        /// Le coefficient est divisé par la submersion d'équilibre parce que la force
        /// d'amortissement est elle aussi pondérée par la submersion dans FixedUpdate.
        /// </summary>
        void RecalculateResponse()
        {
            float gravity = Mathf.Abs(Physics.gravity.y);
            float omega = Mathf.Sqrt(gravity * m_BuoyancyStrength / m_ProbeDepth);
            m_VerticalDampingCoefficient = 2f * m_DampingRatio * omega / m_EquilibriumSubmersion;
        }

        void FixedUpdate()
        {
            if (m_Rigidbody.isKinematic || m_Probes == null) return;

            // AVANT tout test sur l'eau : c'est ce qui rend le bateau jouable même quand la mer
            // manque à l'appel. Sortir d'ici sans l'avoir fait le laissait figé en Y et sans
            // gravité, donc incapable de flotter comme de sauter.
            EnsureRigidbodyIsFree();

            if (!WaterSurface.Exists)
            {
                WarnAboutMissingWaterOnce();
                IsInWater = false;
                SubmergedRatio = 0f;
                return;
            }

            int probeCount = m_Probes.Length;
            float perProbeMass = m_Rigidbody.mass / probeCount;
            float gravity = Mathf.Abs(Physics.gravity.y);
            float submersionSum = 0f;

            // Pendant un saut, l'eau ne doit pas retenir la coque qui monte. Elle continue en
            // revanche d'amortir la retombée : c'est elle qui évite le rebond au ré-amerrissage.
            bool jumping = Time.time < m_DampingGraceUntil;

            foreach (var offset in m_Probes)
            {
                var probeWorld = transform.TransformPoint(offset);
                if (!WaterSurface.TrySampleHeight(probeWorld, out float waterHeight)) continue;

                float depth = waterHeight - probeWorld.y;
                if (depth <= 0f) continue;

                float submersion = Mathf.Clamp01(depth / m_ProbeDepth);
                submersionSum += submersion;

                float buoyancy = submersion * perProbeMass * gravity * m_BuoyancyStrength;

                float verticalSpeed = m_Rigidbody.GetPointVelocity(probeWorld).y;
                float damping = jumping && verticalSpeed > 0f
                    ? 0f
                    : -verticalSpeed * submersion * perProbeMass * m_VerticalDampingCoefficient;

                m_Rigidbody.AddForceAtPosition(Vector3.up * (buoyancy + damping), probeWorld, ForceMode.Force);
            }

            SubmergedRatio = submersionSum / probeCount;
            IsInWater = submersionSum > 0f;

            // On continue de stabiliser hors de l'eau, sinon un saut laisse le bateau tourner
            // librement et il retombe sur le flanc — d'où il ne se relève plus.
            float stabilisation = IsInWater ? SubmergedRatio : m_AirStabilisation;
            if (stabilisation <= 0f) return;

            ApplyAngularDamping(stabilisation);
            ApplyRighting(stabilisation, IsInWater);
        }

        void WarnAboutMissingWaterOnce()
        {
            if (m_WarnedAboutMissingWater) return;
            m_WarnedAboutMissingWater = true;

            Debug.LogWarning(
                $"[Bato] Aucune surface d'eau dans la scène : '{name}' va couler. Il faut soit " +
                "l'objet Ocean (WaveField), soit un volume Bitgem portant BitgemWaterSurface.", this);
        }

        void ApplyAngularDamping(float immersion)
        {
            m_Rigidbody.AddTorque(
                -m_Rigidbody.angularVelocity * (m_AngularDrag * immersion),
                ForceMode.Acceleration);
        }

        /// <summary>
        /// Remet le bateau d'aplomb. Le couple est proportionnel à l'ANGLE d'inclinaison, pas au
        /// produit vectoriel brut : celui-ci a pour norme sin(angle), qui décroît passé 90° et
        /// s'annule exactement à 180°. Un bateau retourné s'y retrouvait donc en équilibre, sans
        /// rien pour le redresser — c'est ce qui le laissait la quille en l'air.
        /// </summary>
        void ApplyRighting(float strength, bool inWater)
        {
            // Hors de l'eau, s'aligner sur une vague qu'on ne touche pas n'a aucun sens.
            var targetUp = inWater
                ? Vector3.Slerp(Vector3.up, WaterSurface.SampleNormal(transform.position), m_WaveAlignment).normalized
                : Vector3.up;

            var cross = Vector3.Cross(transform.up, targetUp);
            float sin = cross.magnitude;
            float angle = Mathf.Atan2(sin, Vector3.Dot(transform.up, targetUp));   // 0 à π

            // Pile à l'envers, l'axe est indéterminé : on en choisit un pour faire basculer le
            // bateau d'un côté plutôt que de le laisser sur son point d'équilibre instable.
            var axis = sin > 1e-4f ? cross / sin : transform.forward;

            m_Rigidbody.AddTorque(axis * (angle * m_UprightTorque * strength), ForceMode.Acceleration);
        }

        void OnDrawGizmosSelected()
        {
            if (m_Probes == null)
            {
                if (m_HullCollider == null) m_HullCollider = GetComponent<Collider>();
                RecalculateProbes();
            }

            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.9f);
            foreach (var offset in m_Probes)
            {
                Gizmos.DrawWireSphere(transform.TransformPoint(offset), 0.18f);
            }
        }
    }
}
