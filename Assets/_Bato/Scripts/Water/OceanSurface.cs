using UnityEngine;

namespace Bato.Water
{
    /// <summary>
    /// Construit la grille de l'océan au démarrage.
    ///
    /// Toute la forme vient du shader ; ce mesh n'est qu'un support de sommets. Sa résolution est
    /// donc directement la limite de finesse de la mer : une vague plus courte qu'environ cinq
    /// mailles n'existe tout simplement pas à l'écran, quelles que soient les valeurs dans
    /// WaveSettings. C'est la raison la plus fréquente d'une mer qui « fait des grandes ondulations
    /// molles » au lieu de vraies vagues.
    ///
    /// Généré plutôt que stocké en asset : la résolution devient un réglage, pas un fichier à
    /// régénérer avec un outil.
    ///
    /// À poser sur l'objet Ocean, qui doit avoir un MeshFilter et un MeshRenderer.
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    [ExecuteAlways]
    public class OceanSurface : MonoBehaviour
    {
        [Tooltip("Côté de la grille, en mètres. Doit largement dépasser l'arène pour que " +
                 "l'horizon reste de l'eau.")]
        [SerializeField, Min(10f)] float m_Size = 190f;

        [Tooltip("Nombre de mailles par côté. 256 sur 190 m donne des mailles de 0,74 m, soit des " +
                 "vagues correctes jusqu'à 4 m de longueur d'onde environ.")]
        [SerializeField, Range(16, 400)] int m_Resolution = 256;

        [Tooltip("Marge verticale ajoutée aux bornes du mesh. Les sommets sont déplacés par le " +
                 "shader, ce qu'Unity ignore : sans marge, la mer disparaît quand on regarde " +
                 "vers l'horizon.")]
        [SerializeField, Min(1f)] float m_VerticalBoundsPadding = 20f;

        Mesh m_Mesh;
        float m_BuiltSize;
        int m_BuiltResolution;

        void OnEnable() => Rebuild();

        void OnValidate()
        {
            if (isActiveAndEnabled) Rebuild();
        }

        void OnDestroy()
        {
            if (m_Mesh != null) DestroyImmediate(m_Mesh);
        }

        void Rebuild()
        {
            if (m_Mesh != null && Mathf.Approximately(m_BuiltSize, m_Size) && m_BuiltResolution == m_Resolution)
            {
                return;
            }

            if (m_Mesh == null)
            {
                m_Mesh = new Mesh { name = "OceanGrid (généré)", hideFlags = HideFlags.DontSave };
            }

            BuildGrid(m_Mesh, m_Size, m_Resolution, m_VerticalBoundsPadding);
            GetComponent<MeshFilter>().sharedMesh = m_Mesh;

            m_BuiltSize = m_Size;
            m_BuiltResolution = m_Resolution;
        }

        static void BuildGrid(Mesh mesh, float size, int resolution, float verticalPadding)
        {
            int side = resolution + 1;
            int vertexCount = side * side;

            var vertices = new Vector3[vertexCount];
            var indices = new int[resolution * resolution * 6];

            float step = size / resolution;
            float origin = -size * 0.5f;

            for (int z = 0; z < side; z++)
            {
                for (int x = 0; x < side; x++)
                {
                    vertices[z * side + x] = new Vector3(origin + x * step, 0f, origin + z * step);
                }
            }

            int index = 0;
            for (int z = 0; z < resolution; z++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    int bottomLeft = z * side + x;
                    int topLeft = bottomLeft + side;

                    indices[index++] = bottomLeft;
                    indices[index++] = topLeft;
                    indices[index++] = topLeft + 1;

                    indices[index++] = bottomLeft;
                    indices[index++] = topLeft + 1;
                    indices[index++] = bottomLeft + 1;
                }
            }

            mesh.Clear();
            mesh.indexFormat = vertexCount > 65535
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.vertices = vertices;
            mesh.triangles = indices;
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(size, verticalPadding * 2f, size));
        }
    }
}
