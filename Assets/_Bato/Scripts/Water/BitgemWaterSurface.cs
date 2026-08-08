using Bitgem.VFX.StylisedWater;
using UnityEngine;

namespace Bato.Water
{
    /// <summary>
    /// Adaptateur : fait passer l'eau stylisée de Bitgem pour une <see cref="IWaterSurface"/>, afin
    /// que la flottaison, le sillage et les gerbes marchent dessus sans rien savoir d'elle.
    ///
    /// À poser sur le même GameObject que le WaterVolume (Box ou Transforms), à la place du
    /// WaterVolumeHelper de l'asset — ce composant fait le même travail, en mieux pour nos besoins :
    ///
    ///  • il met en cache le renderer et les paramètres de vague du matériau, au lieu de faire un
    ///    GetComponent et trois GetFloat par échantillon (la flottaison en demande quatre par
    ///    bateau et par pas de physique) ;
    ///  • il tient compte de la position ET de l'échelle du volume, alors que GetHeight() d'origine
    ///    ne gère que la position — or il faut agrandir le volume pour couvrir l'arène, voir plus bas ;
    ///  • il donne la normale de la surface, dont le redressement du bateau a besoin ;
    ///  • il distingue « pas d'eau ici » de « eau à la hauteur zéro ».
    ///
    /// ⚠ Taille du volume. WaterVolumeBox borne ses Dimensions à 100 unités de côté, alors que
    /// l'arène en fait 190 (voir ArenaPerimeter). Deux issues : réduire l'arène sous 100, ou
    /// agrandir l'objet d'eau par son Transform — c'est pour ça que cet adaptateur gère l'échelle.
    /// À défaut, coche <see cref="m_ExtendBeyondVolume"/> : au-delà du volume, la mer est
    /// prolongée à plat au niveau du volume, et les bateaux ne coulent pas en atteignant le bord.
    /// </summary>
    [DisallowMultipleComponent]
    public class BitgemWaterSurface : MonoBehaviour, IWaterSurface
    {
        [Tooltip("Volume d'eau Bitgem. Vide = celui posé sur cet objet.")]
        [SerializeField] WaterVolumeBase m_Volume;

        [Tooltip("Renderer qui porte le matériau d'eau, d'où sont lus les paramètres de vague. " +
                 "Vide = celui du volume.")]
        [SerializeField] MeshRenderer m_Renderer;

        [Tooltip("Hors du volume, prolonger la mer à plat au niveau de celui-ci plutôt que de " +
                 "considérer qu'il n'y a pas d'eau. À cocher tant que le volume ne couvre pas " +
                 "toute l'arène : sinon un bateau qui sort du volume perd sa poussée et coule.")]
        [SerializeField] bool m_ExtendBeyondVolume = true;

        // Réglages de vague du matériau, lus une fois. Le shader les applique en espace monde,
        // on fait pareil : la surface calculée ici est celle qui est affichée.
        float m_WaveFrequency;
        float m_WaveScale;
        float m_WaveSpeed;

        // Niveau de repli, mesuré sur la colonne d'origine du volume au démarrage.
        float m_ExtendedHeight;
        bool m_HasExtendedHeight;

        static readonly int s_WaveFrequencyId = Shader.PropertyToID("_WaveFrequency");
        static readonly int s_WaveScaleId = Shader.PropertyToID("_WaveScale");
        static readonly int s_WaveSpeedId = Shader.PropertyToID("_WaveSpeed");

        void Awake()
        {
            ResolveReferences();

            if (m_Volume == null)
            {
                Debug.LogError("[Bato] BitgemWaterSurface n'a pas de WaterVolume : pas de mer.", this);
                enabled = false;
                return;
            }

            // WaterVolumeBase ne construit sa grille de tuiles que dans son Update. La flottaison
            // tourne en FixedUpdate, qui peut passer avant : sans ce Rebuild, le tout premier
            // échantillon déréférence un tableau nul.
            m_Volume.Rebuild();

            CacheMaterialWaves();
            CacheExtendedHeight();
        }

        void OnEnable() => WaterSurface.Register(this);
        void OnDisable() => WaterSurface.Unregister(this);

        void ResolveReferences()
        {
            if (m_Volume == null) m_Volume = GetComponent<WaterVolumeBase>();
            if (m_Renderer == null && m_Volume != null) m_Renderer = m_Volume.GetComponent<MeshRenderer>();
        }

        /// <summary>
        /// Recopie les paramètres de vague du matériau. C'est la seule chose qui garde le CPU et le
        /// GPU d'accord : le shader dessine cette formule, on la rejoue à l'identique.
        /// </summary>
        [ContextMenu("Recharger les réglages du matériau")]
        void CacheMaterialWaves()
        {
            var material = m_Renderer != null ? m_Renderer.sharedMaterial : null;
            if (material == null)
            {
                Debug.LogWarning("[Bato] BitgemWaterSurface ne trouve pas le matériau d'eau : " +
                                 "la surface sera plate (les bateaux flotteront quand même).", this);
                m_WaveFrequency = m_WaveScale = m_WaveSpeed = 0f;
                return;
            }

            m_WaveFrequency = material.HasFloat(s_WaveFrequencyId) ? material.GetFloat(s_WaveFrequencyId) : 0f;
            m_WaveScale = material.HasFloat(s_WaveScaleId) ? material.GetFloat(s_WaveScaleId) : 0f;
            m_WaveSpeed = material.HasFloat(s_WaveSpeedId) ? material.GetFloat(s_WaveSpeedId) : 0f;
        }

        void CacheExtendedHeight()
        {
            // La colonne à l'origine du volume est presque toujours pleine : c'est le niveau de la
            // mer, sans le clapot.
            m_HasExtendedHeight = TryGetSurfaceLevel(m_Volume.transform.position, out m_ExtendedHeight);
        }

#if UNITY_EDITOR
        // Confort d'édition seulement : régler _WaveScale ou _WaveSpeed sur le matériau doit
        // déplacer les bateaux tout de suite, sans repasser par Play.
        void Update() => CacheMaterialWaves();
#endif

        // ------------------------------------------------------- IWaterSurface

        public bool TrySampleHeight(Vector3 worldPosition, out float height)
        {
            if (m_Volume == null)
            {
                height = 0f;
                return false;
            }

            if (!TryGetSurfaceLevel(worldPosition, out height))
            {
                if (!m_ExtendBeyondVolume || !m_HasExtendedHeight) return false;
                height = m_ExtendedHeight;
            }

            height += WaveOffset(worldPosition);
            return true;
        }

        public Vector3 SampleNormal(Vector3 worldPosition)
        {
            if (m_WaveScale == 0f || m_WaveFrequency == 0f) return Vector3.up;

            // Dérivées analytiques de WaveOffset : la surface est une somme de deux sinusoïdes
            // séparables, pas besoin de différences finies.
            float time = Time.time * m_WaveSpeed;
            float amplitude = m_WaveFrequency * m_WaveScale;

            float slopeX = amplitude * Mathf.Cos(worldPosition.x * m_WaveFrequency + time);
            float slopeZ = -amplitude * Mathf.Sin(worldPosition.z * m_WaveFrequency + time);

            return new Vector3(-slopeX, 1f, -slopeZ).normalized;
        }

        // ------------------------------------------------------------ Interne

        /// <summary>Ondulation du shader, rejouée telle quelle. Le temps est local, comme le sien.</summary>
        float WaveOffset(Vector3 worldPosition)
        {
            if (m_WaveScale == 0f) return 0f;

            float time = Time.time * m_WaveSpeed;
            return (Mathf.Sin(worldPosition.x * m_WaveFrequency + time)
                    + Mathf.Cos(worldPosition.z * m_WaveFrequency + time)) * m_WaveScale;
        }

        /// <summary>
        /// Sommet de la colonne de tuiles sous un point du monde, clapot exclu.
        ///
        /// WaterVolumeBase.GetHeight() raisonne en tuiles brutes : il soustrait la position du
        /// volume mais ignore sa rotation et son échelle, et il rend une hauteur du même acabit.
        /// On lui passe donc le point ramené dans le repère local du volume puis ré-ancré sur son
        /// origine monde, et on repasse sa réponse par le Transform. Avec un volume à l'échelle 1
        /// et sans rotation, ça se réduit exactement à l'appel d'origine.
        /// </summary>
        bool TryGetSurfaceLevel(Vector3 worldPosition, out float level)
        {
            var volumeTransform = m_Volume.transform;
            var origin = volumeTransform.position;

            var local = volumeTransform.InverseTransformPoint(worldPosition);
            float? tileTop = m_Volume.GetHeight(origin + local);

            if (!tileTop.HasValue)
            {
                level = 0f;
                return false;
            }

            level = volumeTransform.TransformPoint(new Vector3(local.x, tileTop.Value - origin.y, local.z)).y;
            return true;
        }
    }
}
