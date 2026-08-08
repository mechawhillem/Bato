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
    /// Le profil est calqué sur la mer de Sea of Thieves, mais à l'échelle de NOTRE bateau et pas
    /// du leur. C'est le point qui décide de tout : chez Rare le sloop fait 10 m et la houle
    /// dominante tourne autour de 3-4 longueurs de coque. Notre coque fait ~4,4 m, donc la houle
    /// dominante doit tenir autour de 20 m — pas 60. Une mer réglée en mètres absolus « comme
    /// SoT » donnerait un plan qui bascule, pas des vagues.
    ///
    /// Deux choses font l'irrégularité caractéristique :
    ///  - un vrai écart d'échelle entre les trains (23 m jusqu'à 3 m, chacun ~1,6× plus court) ;
    ///  - une houle croisée, à 60-80° de la houle principale, qui casse l'effet « velours côtelé ».
    /// </summary>
    [CreateAssetMenu(fileName = "WaveSettings", menuName = "Bato/Wave Settings")]
    public class WaveSettings : ScriptableObject
    {
        /// <summary>Doit rester synchronisé avec la taille des tableaux dans Ocean.shader.</summary>
        public const int MaxWaves = 5;

        /// <summary>Accélération utilisée pour la vitesse de propagation en eau profonde.</summary>
        public const float Gravity = 9.81f;

        // Le profil de référence. Le tirage aléatoire brode autour sans en changer le caractère.
        //
        // La plus courte est à 3,3 m : en dessous, une vague passe sous la maille du mesh
        // (voir OceanSurface) et ne fait plus que du bruit de sommets.
        static readonly float[] k_BaseWavelengths = { 23f, 14f, 8.5f, 5.2f, 3.3f };
        static readonly float[] k_BaseAmplitudes = { 0.85f, 0.50f, 0.26f, 0.13f, 0.06f };
        static readonly float[] k_BaseSteepness = { 1.00f, 0.92f, 0.85f, 0.75f, 0.65f };

        // Écart angulaire imposé à chaque train, en degrés, par rapport au vent. C'est la houle
        // croisée : sans elle toutes les vagues avancent de front et la mer fait des sillons.
        static readonly float[] k_BaseAngles = { 0f, 64f, -38f, 22f, -78f };

        [Header("Aléatoire")]
        [Tooltip("Change ce nombre pour obtenir une mer différente. Deux seeds identiques donnent " +
                 "exactement la même mer, sur toutes les machines.")]
        [SerializeField] int m_Seed = 1337;

        [Tooltip("Direction dominante du vent, en degrés.")]
        [Range(0f, 360f)]
        [SerializeField] float m_WindDirection = 20f;

        [Tooltip("Désordre ajouté aux écarts angulaires du profil. 0 = mer parfaitement rangée.")]
        [Range(0f, 60f)]
        [SerializeField] float m_DirectionalSpread = 18f;

        [Tooltip("Variation aléatoire des longueurs d'onde et des amplitudes autour du profil de " +
                 "référence. 0 = toujours la même mer, seules les directions changent.")]
        [Range(0f, 0.6f)]
        [SerializeField] float m_SizeVariation = 0.25f;

        [Header("Forme")]
        [Tooltip("Somme des amplitudes, en mètres. La mer monte et descend d'environ le double " +
                 "de cette valeur entre creux et crête.")]
        [Min(0f)]
        [SerializeField] float m_TotalAmplitude = 1.4f;

        [Tooltip("Multiplie le pincement des crêtes. À 1 la crête la plus raide est à la limite du " +
                 "repli sur elle-même ; au-delà la surface se croise et la flottaison décroche.")]
        [Range(0f, 1.1f)]
        [SerializeField] float m_SteepnessScale = 0.85f;

        [Header("Bruit")]
        [Tooltip("Hauteur du bruit ajouté par-dessus la houle, en mètres. 0 = vagues parfaitement " +
                 "régulières. C'est ce qui empêche de voir le motif se répéter.")]
        [Range(0f, 0.6f)]
        [SerializeField] float m_NoiseAmplitude = 0.12f;

        [Tooltip("Finesse du bruit, en cycles par mètre. 0,18 donne des bosses d'environ 5 m.")]
        [Range(0.02f, 1f)]
        [SerializeField] float m_NoiseScale = 0.18f;

        [Tooltip("Vitesse de déformation du bruit. Trop haut, l'eau grésille.")]
        [Range(0f, 2f)]
        [SerializeField] float m_NoiseSpeed = 0.35f;

        [Header("Vagues générées")]
        [SerializeField] GerstnerWave[] m_Waves = Array.Empty<GerstnerWave>();

        [Tooltip("Multiplie l'amplitude de toutes les vagues. L'état de mer réseau vient s'y ajouter.")]
        [Range(0f, 3f)]
        [SerializeField] float m_GlobalAmplitude = 1f;

        public float GlobalAmplitude => m_GlobalAmplitude;

        public float NoiseAmplitude => m_NoiseAmplitude;
        public float NoiseScale => m_NoiseScale;
        public float NoiseSpeed => m_NoiseSpeed;

        public GerstnerWave[] Waves => m_Waves;

        public int WaveCount => m_Waves == null ? 0 : Mathf.Min(m_Waves.Length, MaxWaves);

        /// <summary>
        /// Somme des amplitudes. Sert à deux choses : normaliser les dégradés du shader, et
        /// répartir le budget de pincement entre les vagues (voir <see cref="WaveField"/>).
        /// </summary>
        public float TotalAmplitude
        {
            get
            {
                float total = 0f;
                for (int i = 0; i < WaveCount; i++) total += m_Waves[i].Amplitude;
                return total;
            }
        }

        void OnEnable()
        {
            // Couvre aussi le cas d'un asset laissé avec l'ancien spectre : dès que le nombre de
            // vagues ne correspond plus au profil courant, on regénère.
            //
            // Sans marquer l'asset non plus : OnEnable tourne pendant l'import, et écrire à ce
            // moment-là relance l'import. Le tirage étant déterministe (même seed = même mer), le
            // recalculer au chargement ne coûte rien et ne change rien pour les autres joueurs.
            if (m_Waves == null || m_Waves.Length != MaxWaves) Regenerate(markDirty: false);
        }

        // Les réglages ci-dessus ne servent qu'à produire m_Waves : les toucher sans régénérer
        // ne changerait rien à l'écran. On régénère donc à chaque modification de l'inspecteur.
        //
        // ⚠ SANS marquer l'asset modifié. EditorUtility.SetDirty() appelé depuis OnValidate fait
        // re-sérialiser l'asset par Unity, ce qui rappelle OnValidate, en boucle : l'éditeur se
        // fige. L'inspecteur marque déjà l'asset modifié de lui-même.
        void OnValidate() => Regenerate(markDirty: false);

        /// <summary>
        /// Brode autour du profil de référence : mêmes ordres de grandeur, directions et tailles
        /// tirées au sort. Le caractère de la mer ne change pas, seul son motif.
        /// </summary>
        [ContextMenu("Régénérer les vagues")]
        public void Regenerate() => Regenerate(markDirty: true);

        void Regenerate(bool markDirty)
        {
            var random = new System.Random(m_Seed);
            m_Waves = new GerstnerWave[MaxWaves];
            float amplitudeSum = 0f;

            for (int i = 0; i < MaxWaves; i++)
            {
                float wavelength = k_BaseWavelengths[i] * (1f + NextSigned(random) * m_SizeVariation);
                float amplitude = k_BaseAmplitudes[i] * (1f + NextSigned(random) * m_SizeVariation * 0.6f);
                amplitudeSum += amplitude;

                float angle = (m_WindDirection
                               + k_BaseAngles[i]
                               + NextSigned(random) * m_DirectionalSpread) * Mathf.Deg2Rad;

                m_Waves[i] = new GerstnerWave
                {
                    Direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)),
                    Wavelength = wavelength,
                    Amplitude = amplitude,
                    Steepness = Mathf.Clamp01(k_BaseSteepness[i] * m_SteepnessScale),
                    SpeedMultiplier = 1f + NextSigned(random) * 0.08f,
                };
            }

            // Normalisation : la hauteur de la mer ne doit pas dépendre du tirage.
            float scale = amplitudeSum > 0.0001f ? m_TotalAmplitude / amplitudeSum : 0f;
            for (int i = 0; i < MaxWaves; i++)
            {
                m_Waves[i].Amplitude *= scale;
            }

            if (markDirty) MarkDirty();
        }

        /// <summary>Tire une mer différente.</summary>
        [ContextMenu("Nouvelle mer (seed aléatoire)")]
        public void RegenerateWithNewSeed()
        {
            m_Seed = UnityEngine.Random.Range(1, int.MaxValue);
            Regenerate();
        }

        /// <summary>Valeur dans [-1, 1].</summary>
        static float NextSigned(System.Random random) => (float)(random.NextDouble() * 2.0 - 1.0);

        void MarkDirty()
        {
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }
    }
}
