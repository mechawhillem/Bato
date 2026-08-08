using UnityEngine;

namespace Bato.Water
{
    /// <summary>
    /// Sillage et écume de coque.
    ///
    /// Tourne chez TOUS les clients, pas seulement le propriétaire : chacun voit le sillage de
    /// tous les bateaux. C'est possible sans rien répliquer parce que la vitesse est mesurée sur
    /// le transform (déjà synchronisé) et la hauteur d'eau est une fonction pure du temps serveur.
    ///
    /// La vitesse ne peut pas venir du Rigidbody : chez les clients non-propriétaires il est
    /// kinematic et sa vélocité vaut zéro. On dérive donc le déplacement du transform.
    ///
    /// À poser sur le bateau, à côté de BoatBuoyancy.
    /// </summary>
    public class BoatWake : MonoBehaviour
    {
        [Header("Coque")]
        [Tooltip("Vide = le premier collider trouvé. Sert à dimensionner la zone d'émission.")]
        [SerializeField] Collider m_HullCollider;

        [Header("Sillage")]
        [Tooltip("En dessous de cette vitesse, pas d'écume.")]
        [SerializeField, Min(0f)] float m_MinSpeed = 1.2f;

        [Tooltip("Vitesse à laquelle le sillage est à son maximum.")]
        [SerializeField, Min(0.1f)] float m_FullSpeed = 7f;

        [Tooltip("Particules par seconde à pleine vitesse.")]
        [SerializeField, Min(0f)] float m_MaxEmissionRate = 70f;

        [Tooltip("Vitesse d'écartement latéral de l'écume, façon vague d'étrave.")]
        [SerializeField, Min(0f)] float m_SpreadSpeed = 1.3f;

        [Header("Claque de coque")]
        [Tooltip("Vitesse de chute à partir de laquelle la coque gifle l'eau et projette une gerbe.")]
        [SerializeField, Min(0.5f)] float m_SlamSpeed = 3.5f;

        [SerializeField, Min(0f)] float m_SlamCooldown = 0.35f;

        ParticleSystem m_System;
        Vector3 m_HullExtents;
        Vector3 m_PreviousPosition;
        float m_EmissionCarry;
        float m_NextSlamTime;
        bool m_WasInWater;

        void Awake()
        {
            if (m_HullCollider == null) m_HullCollider = GetComponent<Collider>();
            m_HullExtents = m_HullCollider is BoxCollider box
                ? box.size * 0.5f
                : new Vector3(0.8f, 0.6f, 2.2f);

            // Système sur un enfant dédié pour ne pas encombrer le bateau. Les particules sont
            // simulées en espace monde et émises à des positions absolues : le roulis de la coque
            // ne les entraîne pas.
            var host = new GameObject("Wake");
            host.transform.SetParent(transform, false);
            m_System = FoamParticles.Create(host, maxParticles: 600, lifetime: 1.6f, size: 0.5f);

            m_PreviousPosition = transform.position;
        }

        void LateUpdate()
        {
            float deltaTime = Time.deltaTime;
            if (deltaTime <= 0f) return;

            var velocity = (transform.position - m_PreviousPosition) / deltaTime;
            m_PreviousPosition = transform.position;

            // Pas d'eau ici : ni sillage ni claque, et on oublie l'état précédent — sinon un
            // bateau qui repasse au-dessus de la mer déclenche une gerbe qu'il n'a pas méritée.
            if (!WaterSurface.TrySampleHeight(transform.position, out float waterHeight))
            {
                m_WasInWater = false;
                m_EmissionCarry = 0f;
                return;
            }

            bool inWater = transform.position.y - m_HullExtents.y <= waterHeight;

            DetectSlam(velocity, waterHeight, inWater);
            m_WasInWater = inWater;

            if (!inWater)
            {
                m_EmissionCarry = 0f;
                return;
            }

            EmitWake(velocity, waterHeight, deltaTime);
        }

        /// <summary>Grosse gerbe quand la coque retombe dans l'eau après avoir décollé d'une vague.</summary>
        void DetectSlam(Vector3 velocity, float waterHeight, bool inWater)
        {
            if (m_WasInWater || !inWater) return;
            if (velocity.y > -m_SlamSpeed) return;
            if (Time.time < m_NextSlamTime) return;

            m_NextSlamTime = Time.time + m_SlamCooldown;

            float force = Mathf.Clamp(-velocity.y / m_SlamSpeed, 1f, 2.5f);
            var impact = new Vector3(transform.position.x, waterHeight, transform.position.z);
            WaterEffects.Splash(impact, force);
        }

        void EmitWake(Vector3 velocity, float waterHeight, float deltaTime)
        {
            var planarVelocity = new Vector3(velocity.x, 0f, velocity.z);
            float speed = planarVelocity.magnitude;
            if (speed < m_MinSpeed) return;

            float intensity = Mathf.Clamp01((speed - m_MinSpeed) / (m_FullSpeed - m_MinSpeed));

            // Report du reliquat : sinon à faible taux d'émission on n'émet jamais rien.
            m_EmissionCarry += m_MaxEmissionRate * intensity * deltaTime;
            int count = Mathf.FloorToInt(m_EmissionCarry);
            if (count <= 0) return;
            m_EmissionCarry -= count;

            // Cap à plat : l'écume naît sur la surface, pas sur une coque inclinée.
            var heading = planarVelocity / speed;
            var side = Vector3.Cross(Vector3.up, heading);

            for (int i = 0; i < count; i++)
            {
                // Réparti le long de la coque, de l'étrave à la poupe.
                float along = Random.Range(-1f, 1f);
                float across = Random.value < 0.5f ? -1f : 1f;

                var origin = transform.position
                             + heading * (along * m_HullExtents.z)
                             + side * (across * m_HullExtents.x * Random.Range(0.7f, 1.1f));
                origin.y = waterHeight;

                // L'écume s'écarte sur les côtés et reste derrière le bateau.
                var spread = side * (across * m_SpreadSpeed * Random.Range(0.6f, 1.2f))
                             + Vector3.up * Random.Range(0.1f, 0.6f)
                             - heading * (speed * 0.15f);

                m_System.Emit(new ParticleSystem.EmitParams
                {
                    position = origin,
                    velocity = spread * intensity,
                    startSize = Random.Range(0.2f, 0.5f) * (0.6f + intensity),
                    startColor = Color.white,
                    applyShapeToPosition = false,
                }, 1);
            }
        }
    }
}
