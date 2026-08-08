using UnityEngine;

namespace Bato.Water
{
    /// <summary>
    /// Tout ce que le gameplay a besoin de savoir de la mer, indépendamment de qui la dessine.
    ///
    /// Deux implémentations coexistent : <see cref="WaveField"/> (notre houle de Gerstner, dessinée
    /// par Ocean.shader) et <see cref="BitgemWaterSurface"/> (le volume d'eau stylisé importé).
    /// Le gameplay ne connaît ni l'une ni l'autre : il passe par <see cref="WaterSurface"/>.
    /// </summary>
    public interface IWaterSurface
    {
        /// <summary>
        /// Hauteur de l'eau à l'aplomb d'un point du monde.
        ///
        /// Renvoie faux là où il n'y a pas d'eau du tout. Ce n'est pas un cas d'erreur : une mer
        /// bornée (le volume Bitgem l'est) a des endroits secs, et une coque qui les survole ne
        /// doit surtout pas recevoir de poussée. « Pas d'eau » et « eau à la hauteur zéro » sont
        /// deux choses différentes.
        /// </summary>
        bool TrySampleHeight(Vector3 worldPosition, out float height);

        /// <summary>Normale de la surface. Vector3.up là où l'eau est plate.</summary>
        Vector3 SampleNormal(Vector3 worldPosition);
    }

    /// <summary>
    /// Point d'entrée unique vers la mer active.
    ///
    /// Une seule surface est active à la fois, celle qui s'est enregistrée en dernier. Passer de
    /// notre océan à celui de Bitgem ne demande donc aucune modification de code ni de prefab :
    /// on désactive l'objet Ocean, on active celui qui porte <see cref="BitgemWaterSurface"/>, et
    /// la flottaison, le sillage, les gerbes et l'amerrissage des boulets suivent.
    /// </summary>
    public static class WaterSurface
    {
        /// <summary>Surface active, ou null si la scène n'en contient aucune.</summary>
        public static IWaterSurface Active { get; private set; }

        public static bool Exists => Active != null;

        // Les statiques survivent au rechargement de scène quand le rechargement de domaine est
        // désactivé (Enter Play Mode Options). Sans cette remise à zéro, la deuxième entrée en
        // Play garderait une référence sur une surface détruite.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Active = null;

        public static void Register(IWaterSurface surface)
        {
            if (surface == null) return;

            if (Active != null && !ReferenceEquals(Active, surface))
            {
                Debug.LogWarning(
                    $"[Bato] Deux surfaces d'eau actives en même temps : {Active.GetType().Name} " +
                    $"est remplacée par {surface.GetType().Name}. Désactive l'objet de l'ancienne " +
                    "mer, sinon la surface qui gagne dépend de l'ordre d'initialisation.");
            }

            Active = surface;
        }

        public static void Unregister(IWaterSurface surface)
        {
            if (ReferenceEquals(Active, surface)) Active = null;
        }

        /// <summary>
        /// Hauteur de l'eau, ou faux s'il n'y a pas de mer ici — soit parce qu'aucune surface
        /// n'est enregistrée, soit parce que le point est en dehors de celle qui l'est.
        /// </summary>
        public static bool TrySampleHeight(Vector3 worldPosition, out float height)
        {
            if (Active == null)
            {
                height = 0f;
                return false;
            }

            return Active.TrySampleHeight(worldPosition, out height);
        }

        public static Vector3 SampleNormal(Vector3 worldPosition)
            => Active?.SampleNormal(worldPosition) ?? Vector3.up;
    }
}
