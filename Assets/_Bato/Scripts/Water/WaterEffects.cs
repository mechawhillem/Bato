using UnityEngine;

namespace Bato.Water
{
    /// <summary>
    /// Gerbes d'eau ponctuelles : impacts de boulets, claques de coque.
    ///
    /// Un seul système de particules pour toute la scène, en émission manuelle : pas
    /// d'Instantiate ni de Destroy par impact, donc pas de pic de GC pendant un échange de tirs.
    ///
    /// Purement visuel et purement local. Les impacts sont décidés par le serveur puis diffusés
    /// (voir Cannonball), mais l'effet lui-même ne traverse jamais le réseau.
    ///
    /// À poser sur un GameObject vide de la scène.
    /// </summary>
    public class WaterEffects : MonoBehaviour
    {
        public static WaterEffects Instance { get; private set; }

        [Header("Systèmes de particules")]
        [Tooltip("Ton ParticleSystem pour les gerbes d'eau. Vide = celui généré par code. " +
                 "Simulation Space = World, et décoche son module Emission : c'est le jeu qui émet.")]
        [SerializeField] ParticleSystem m_SplashSystem;

        [Tooltip("Ton ParticleSystem pour les éclats sur coque (bois, étincelles…). " +
                 "Vide = le même que les gerbes.")]
        [SerializeField] ParticleSystem m_HullImpactSystem;

        [Tooltip("Coché, le jeu impose taille et couleur et écrase les tiennes. Décoche-le pour " +
                 "que Start Size / Start Color / Color over Lifetime de tes systèmes décident.")]
        [SerializeField] bool m_OverrideSizeAndColor = true;

        [Header("Gerbe d'eau")]
        [SerializeField, Min(1)] int m_SplashParticles = 26;
        [SerializeField, Min(0f)] float m_SplashSpeed = 5.5f;
        [SerializeField, Min(0f)] float m_SplashSpread = 0.55f;
        [SerializeField, Min(0f)] float m_SplashRadius = 0.35f;

        [Header("Impact sur coque")]
        [SerializeField, Min(1)] int m_HitParticles = 14;
        [SerializeField] Color m_HitColor = new Color(0.75f, 0.68f, 0.55f);

        ParticleSystem m_GeneratedSystem;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;

            FoamParticles.PrepareForManualEmission(m_SplashSystem, this);
            FoamParticles.PrepareForManualEmission(m_HullImpactSystem, this);

            // Le système généré ne sert que de bouche-trou : inutile de le construire si les deux
            // effets ont déjà le leur.
            if (m_SplashSystem == null || m_HullImpactSystem == null)
            {
                m_GeneratedSystem = FoamParticles.Create(
                    gameObject, maxParticles: 900, lifetime: 1.2f, size: 0.55f);
            }
        }

        ParticleSystem SplashSystem => m_SplashSystem != null ? m_SplashSystem : m_GeneratedSystem;

        ParticleSystem HullImpactSystem => m_HullImpactSystem != null
            ? m_HullImpactSystem
            : SplashSystem;

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Gerbe blanche, recalée sur la surface réelle de l'eau.</summary>
        public static void Splash(Vector3 position, float scale = 1f)
        {
            if (Instance == null) return;

            if (WaterSurface.TrySampleHeight(position, out float height)) position.y = height;

            Instance.Burst(Instance.SplashSystem, position, scale, Color.white,
                Instance.m_SplashParticles, upwardBias: 1f);
        }

        /// <summary>Éclats sur une coque : plus secs, plus sombres, sans recalage sur l'eau.</summary>
        public static void HullImpact(Vector3 position, Vector3 normal)
        {
            if (Instance == null) return;
            Instance.Burst(Instance.HullImpactSystem, position, 0.7f, Instance.m_HitColor,
                Instance.m_HitParticles, upwardBias: 0.35f, biasDirection: normal);
        }

        void Burst(ParticleSystem system, Vector3 position, float scale, Color color, int count,
                   float upwardBias, Vector3? biasDirection = null)
        {
            if (system == null) return;

            var bias = biasDirection ?? Vector3.up;

            for (int i = 0; i < count; i++)
            {
                // Cône autour de la direction de projection, plus une dispersion aléatoire.
                var direction = (bias * upwardBias + Random.insideUnitSphere * m_SplashSpread).normalized;
                if (direction.y < 0f) direction.y = -direction.y * 0.3f;   // rien ne part vers le fond

                var origin = position + Random.insideUnitSphere * (m_SplashRadius * scale);
                var velocity = direction * (m_SplashSpeed * scale * Random.Range(0.55f, 1.25f));

                // Chaque champ renseigné sur EmitParams écrase le réglage correspondant du
                // système : ce qu'on laisse de côté reste piloté depuis l'inspecteur.
                var emitParams = new ParticleSystem.EmitParams
                {
                    position = FoamParticles.ToSimulationSpace(system, origin),
                    velocity = FoamParticles.VelocityToSimulationSpace(system, velocity),
                    applyShapeToPosition = false,
                };

                if (m_OverrideSizeAndColor)
                {
                    emitParams.startColor = color;
                    emitParams.startSize = Random.Range(0.25f, 0.6f) * scale;
                }

                system.Emit(emitParams, 1);
            }
        }
    }
}
