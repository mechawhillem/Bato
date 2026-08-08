using System.Collections.Generic;
using UnityEngine;

namespace Bato.Water
{
    /// <summary>
    /// Découpe la mer à l'intérieur de la coque.
    ///
    /// Le problème : la mer est un immense mesh qui traverse le bateau. La flottaison enfonce la
    /// coque, donc la surface passe visuellement au travers. Ce n'est pas un bug de physique — la
    /// physique est juste — c'est un problème de rendu.
    ///
    /// La solution : marquer la silhouette de la coque dans le stencil et faire jeter à
    /// Ocean.shader tous ses fragments qui tombent dedans. Différence booléenne, mais résolue par
    /// pixel au moment du rendu : gratuite, et suit une coque qui roule et tangue sans recalcul.
    ///
    /// Aucun volume à modéliser : le composant réutilise les meshes déjà présents sur le bateau et
    /// les redessine avec le matériau de masque, sans créer d'objets ni toucher aux renderers
    /// existants. Pose-le sur la racine du bateau et c'est fini.
    /// </summary>
    public class HullWaterMask : MonoBehaviour
    {
        [Tooltip("Vide = tous les MeshFilter sous cet objet. À remplir seulement si un élément " +
                 "gêne : un mât ou une voile masquent de l'eau qu'on aurait voulu voir derrière.")]
        [SerializeField] MeshFilter[] m_MaskMeshes;

        [Tooltip("Ignore les meshes dont le renderer est éteint (bateau coulé, pièces cachées).")]
        [SerializeField] bool m_SkipHiddenMeshes = true;

        static Material s_MaskMaterial;

        readonly List<MeshFilter> m_Resolved = new List<MeshFilter>();
        Material m_Material;

        void Awake()
        {
            m_Material = GetMaskMaterial();
            if (m_Material == null)
            {
                enabled = false;
                return;
            }

            ResolveMeshes();
        }

        void ResolveMeshes()
        {
            m_Resolved.Clear();

            if (m_MaskMeshes != null && m_MaskMeshes.Length > 0)
            {
                foreach (var filter in m_MaskMeshes)
                {
                    if (filter != null) m_Resolved.Add(filter);
                }
            }
            else
            {
                GetComponentsInChildren(true, m_Resolved);
            }

            if (m_Resolved.Count == 0)
            {
                Debug.LogWarning($"[Bato] HullWaterMask sur '{name}' ne trouve aucun mesh : " +
                                 "la mer ne sera pas découpée.", this);
            }
        }

        /// <summary>
        /// Redessine les meshes dans le stencil. En LateUpdate : la coque a fini de bouger pour
        /// cette frame, donc le masque colle exactement à ce que le rendu va afficher.
        /// </summary>
        void LateUpdate()
        {
            foreach (var filter in m_Resolved)
            {
                if (filter == null) continue;

                var mesh = filter.sharedMesh;
                if (mesh == null) continue;

                var renderer = filter.GetComponent<MeshRenderer>();
                if (m_SkipHiddenMeshes && renderer != null && !renderer.enabled) continue;
                if (m_SkipHiddenMeshes && !filter.gameObject.activeInHierarchy) continue;

                var parameters = new RenderParams(m_Material)
                {
                    worldBounds = renderer != null
                        ? renderer.bounds
                        : TransformBounds(filter.transform.localToWorldMatrix, mesh.bounds),
                    shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off,
                    receiveShadows = false,
                    layer = filter.gameObject.layer,
                };

                var matrix = filter.transform.localToWorldMatrix;
                for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                {
                    Graphics.RenderMesh(parameters, mesh, submesh, matrix);
                }
            }
        }

        static Bounds TransformBounds(Matrix4x4 matrix, Bounds local)
        {
            var center = matrix.MultiplyPoint3x4(local.center);
            var extents = local.extents;

            // Extents d'une AABB après rotation : somme des contributions absolues de chaque axe.
            var axisX = matrix.MultiplyVector(new Vector3(extents.x, 0f, 0f));
            var axisY = matrix.MultiplyVector(new Vector3(0f, extents.y, 0f));
            var axisZ = matrix.MultiplyVector(new Vector3(0f, 0f, extents.z));

            var worldExtents = new Vector3(
                Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x),
                Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y),
                Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z));

            return new Bounds(center, worldExtents * 2f);
        }

        static Material GetMaskMaterial()
        {
            if (s_MaskMaterial != null) return s_MaskMaterial;

            var shader = Shader.Find("Bato/WaterMask");
            if (shader == null)
            {
                Debug.LogError("[Bato] Shader 'Bato/WaterMask' introuvable : la mer ne sera pas découpée.");
                return null;
            }

            s_MaskMaterial = new Material(shader)
            {
                name = "WaterMask (généré)",
                hideFlags = HideFlags.DontSave,
            };
            return s_MaskMaterial;
        }
    }
}
