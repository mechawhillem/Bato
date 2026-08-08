using System;
using UnityEngine;

namespace Bato.Water
{
    /// <summary>
    /// Une vague de Gerstner. La direction est dans le plan XZ.
    /// </summary>
    [Serializable]
    public struct GerstnerWave
    {
        [Tooltip("Direction de propagation dans le plan XZ (normalisée à l'usage).")]
        public Vector2 Direction;

        [Tooltip("Distance entre deux crêtes, en mètres.")]
        public float Wavelength;

        [Tooltip("Hauteur crête-à-creux / 2, en mètres.")]
        public float Amplitude;

        [Range(0f, 1f)]
        [Tooltip("0 = houle ronde, 1 = crêtes pincées. Au-delà la surface se replie sur elle-même.")]
        public float Steepness;

        [Tooltip("Multiplicateur sur la vitesse de propagation physique (eau profonde).")]
        public float SpeedMultiplier;
    }

    /// <summary>
    /// Paramètres de la mer, partagés par le CPU (flottaison) et le GPU (rendu).
    ///
    /// C'est LA source de vérité unique : cet asset est dans le projet, donc identique chez tous
    /// les joueurs. Rien de tout ça ne transite sur le réseau — voir <see cref="WaveField"/>.
    ///
    /// Le nombre de vagues est plafonné à <see cref="MaxWaves"/> parce que le shader déclare
    /// des tableaux de taille fixe.
    /// </summary>
    [CreateAssetMenu(fileName = "WaveSettings", menuName = "Bato/Wave Settings")]
    public class WaveSettings : ScriptableObject
    {
        public const int MaxWaves = 4;

        /// <summary>Accélération utilisée pour la vitesse de propagation en eau profonde.</summary>
        public const float Gravity = 9.81f;

        [SerializeField] GerstnerWave[] m_Waves = DefaultWaves();

        [Tooltip("Multiplie l'amplitude de toutes les vagues. L'état de mer réseau vient s'y ajouter.")]
        [Range(0f, 3f)]
        [SerializeField] float m_GlobalAmplitude = 1f;

        public float GlobalAmplitude => m_GlobalAmplitude;

        public GerstnerWave[] Waves => m_Waves;

        public int WaveCount => m_Waves == null ? 0 : Mathf.Min(m_Waves.Length, MaxWaves);

        void OnValidate()
        {
            if (m_Waves == null || m_Waves.Length == 0)
            {
                m_Waves = DefaultWaves();
                return;
            }

            if (m_Waves.Length > MaxWaves)
            {
                Debug.LogWarning($"[Bato] WaveSettings est limité à {MaxWaves} vagues (le shader a des tableaux fixes). Les suivantes sont ignorées.");
            }

            for (int i = 0; i < m_Waves.Length; i++)
            {
                if (m_Waves[i].Wavelength < 0.5f) m_Waves[i].Wavelength = 0.5f;
                if (m_Waves[i].SpeedMultiplier <= 0f) m_Waves[i].SpeedMultiplier = 1f;
                if (m_Waves[i].Direction.sqrMagnitude < 1e-4f) m_Waves[i].Direction = Vector2.right;
            }
        }

        /// <summary>Houle longue croisée par deux trains plus courts : lisible et pas trop répétitif.</summary>
        public static GerstnerWave[] DefaultWaves() => new[]
        {
            new GerstnerWave { Direction = new Vector2(1f, 0.15f),   Wavelength = 34f, Amplitude = 0.75f, Steepness = 0.55f, SpeedMultiplier = 1f },
            new GerstnerWave { Direction = new Vector2(0.6f, 0.8f),  Wavelength = 19f, Amplitude = 0.42f, Steepness = 0.7f,  SpeedMultiplier = 1f },
            new GerstnerWave { Direction = new Vector2(-0.4f, 0.9f), Wavelength = 11f, Amplitude = 0.22f, Steepness = 0.8f,  SpeedMultiplier = 1.1f },
            new GerstnerWave { Direction = new Vector2(-0.9f, 0.3f), Wavelength = 6f,  Amplitude = 0.09f, Steepness = 0.9f,  SpeedMultiplier = 1.2f },
        };
    }
}
