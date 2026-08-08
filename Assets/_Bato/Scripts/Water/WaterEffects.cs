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

        [Header("Gerbe d'eau")]
        [SerializeField, Min(1)] int m_SplashParticles = 26;
        [SerializeField, Min(0f)] float m_SplashSpeed = 5.5f;
        [SerializeField, Min(0f)] float m_SplashSpread = 0.55f;
        [SerializeField, Min(0f)] float m_SplashRadius = 0.35f;

        [Header("Impact sur coque")]
        [SerializeField, Min(1)] int m_HitParticles = 14;
        [SerializeField] Color m_HitColor = new Color(0.75f, 0.68f, 0.55f);

        ParticleSystem m_System;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;

            m_System = FoamParticles.Create(gameObject, maxParticles: 900, lifetime: 1.2f, size: 0.55f);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Gerbe blanche, recalée sur la surface réelle de l'eau.</summary>
        public static void Splash(Vector3 position, float scale = 1f)
        {
            if (Instance == null) return;

            if (WaterSurface.TrySampleHeight(position, out float height)) position.y = height;

            Instance.Burst(position, scale, Color.white, Instance.m_SplashParticles, upwardBias: 1f);
        }

        /// <summary>Éclats sur une coque : plus secs, plus sombres, sans recalage sur l'eau.</summary>
        public static void HullImpact(Vector3 position, Vector3 normal)
        {
            if (Instance == null) return;
            Instance.Burst(position, 0.7f, Instance.m_HitColor, Instance.m_HitParticles,
                upwardBias: 0.35f, biasDirection: normal);
        }

        void Burst(Vector3 position, float scale, Color color, int count,
                   float upwardBias, Vector3? biasDirection = null)
        {
            if (m_System == null) return;

            var bias = biasDirection ?? Vector3.up;

            for (int i = 0; i < count; i++)
            {
                // Cône autour de la direction de projection, plus une dispersion aléatoire.
                var direction = (bias * upwardBias + Random.insideUnitSphere * m_SplashSpread).normalized;
                if (direction.y < 0f) direction.y = -direction.y * 0.3f;   // rien ne part vers le fond

                var emitParams = new ParticleSystem.EmitParams
                {
                    position = position + Random.insideUnitSphere * (m_SplashRadius * scale),
                    velocity = direction * (m_SplashSpeed * scale * Random.Range(0.55f, 1.25f)),
                    startColor = color,
                    startSize = Random.Range(0.25f, 0.6f) * scale,
                    applyShapeToPosition = false,
                };

                m_System.Emit(emitParams, 1);
            }
        }
    }
}
