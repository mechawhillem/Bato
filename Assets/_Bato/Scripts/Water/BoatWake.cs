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
        [Header("Système de particules")]
        [Tooltip("Ton propre ParticleSystem, réglé dans l'inspecteur. Vide = celui généré par code. " +
                 "Mets-le en Simulation Space = World : l'écume doit rester dans le sillage, pas " +
                 "suivre la coque. Et décoche son module Emission, c'est le jeu qui émet.")]
        [SerializeField] ParticleSystem m_CustomSystem;

        [Tooltip("Coché, le jeu impose la taille et la couleur de chaque particule et écrase donc " +
                 "les tiennes. Décoche-le pour que Start Size, Start Color, Color/Size over " +
                 "Lifetime de TON système reprennent la main.")]
        [SerializeField] bool m_OverrideSizeAndColor = true;

        [Tooltip("Coché, le jeu impose la vitesse initiale (écartement en vague d'étrave). " +
                 "Décoche-le pour piloter le mouvement avec Start Speed, Shape, Velocity over " +
                 "Lifetime et Noise.")]
        [SerializeField] bool m_OverrideVelocity = true;

        [Tooltip("Points d'émission explicites : étrave, poupe, flancs. Vide = réparti " +
                 "automatiquement le long de la coque à partir de son collider.")]
        [SerializeField] Transform[] m_EmitPoints;

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

            if (m_CustomSystem != null)
            {
                m_System = m_CustomSystem;
                FoamParticles.PrepareForManualEmission(m_System, this);
                WarnIfWakeFollowsHull();
            }
            else
            {
                // Système sur un enfant dédié pour ne pas encombrer le bateau. Les particules sont
                // simulées en espace monde et émises à des positions absolues : le roulis de la
                // coque ne les entraîne pas.
                var host = new GameObject("Wake");
                host.transform.SetParent(transform, false);
                m_System = FoamParticles.Create(host, maxParticles: 600, lifetime: 1.6f, size: 0.5f);
            }

            m_PreviousPosition = transform.position;
        }

        /// <summary>
        /// Un sillage simulé dans le repère du bateau est traîné par la coque : l'écume avance avec
        /// le navire au lieu de rester là où elle est née. C'est l'erreur qui donne l'impression
        /// que « les particules ne marchent pas », et elle ne se voit qu'en mouvement.
        /// </summary>
        void WarnIfWakeFollowsHull()
        {
            if (m_System.main.simulationSpace != ParticleSystemSimulationSpace.Local) return;
            if (!m_System.transform.IsChildOf(transform)) return;

            Debug.LogWarning(
                $"[Bato] Le ParticleSystem de sillage '{m_System.name}' est en Simulation Space = " +
                "Local sous le bateau : l'écume va suivre la coque au lieu de rester derrière. " +
                "Passe-le en World.", this);
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
                Vector3 origin = NextEmitOrigin(heading, side, out float across);
                origin.y = waterHeight;

                // L'écume s'écarte sur les côtés et reste derrière le bateau.
                var spread = side * (across * m_SpreadSpeed * Random.Range(0.6f, 1.2f))
                             + Vector3.up * Random.Range(0.1f, 0.6f)
                             - heading * (speed * 0.15f);

                Emit(origin, spread * intensity, Random.Range(0.2f, 0.5f) * (0.6f + intensity));
            }
        }

        /// <summary>
        /// Point de naissance d'une particule, et de quel bord elle sort (pour l'écartement).
        /// Soit un des points câblés à la main, soit un tirage le long de la coque.
        /// </summary>
        Vector3 NextEmitOrigin(Vector3 heading, Vector3 side, out float across)
        {
            if (m_EmitPoints != null && m_EmitPoints.Length > 0)
            {
                var point = m_EmitPoints[Random.Range(0, m_EmitPoints.Length)];
                if (point != null)
                {
                    // Le bord se déduit de la position du point : une écume émise à bâbord doit
                    // s'écarter vers bâbord, sinon elle traverse la coque.
                    across = Vector3.Dot(point.position - transform.position, side) < 0f ? -1f : 1f;
                    return point.position;
                }
            }

            float along = Random.Range(-1f, 1f);
            across = Random.value < 0.5f ? -1f : 1f;

            return transform.position
                   + heading * (along * m_HullExtents.z)
                   + side * (across * m_HullExtents.x * Random.Range(0.7f, 1.1f));
        }

        /// <summary>
        /// Émet une particule en respectant ce que l'utilisateur a choisi de garder pour lui.
        /// Chaque champ posé sur EmitParams ÉCRASE le réglage correspondant du système, d'où les
        /// interrupteurs : ce qu'on ne renseigne pas reste piloté par l'inspecteur.
        /// </summary>
        void Emit(Vector3 worldPosition, Vector3 worldVelocity, float size)
        {
            var emitParams = new ParticleSystem.EmitParams
            {
                position = FoamParticles.ToSimulationSpace(m_System, worldPosition),
                applyShapeToPosition = false,
            };

            if (m_OverrideVelocity)
            {
                emitParams.velocity = FoamParticles.VelocityToSimulationSpace(m_System, worldVelocity);
            }

            if (m_OverrideSizeAndColor)
            {
                emitParams.startSize = size;
                emitParams.startColor = Color.white;
            }

            m_System.Emit(emitParams, 1);
        }
    }
}
