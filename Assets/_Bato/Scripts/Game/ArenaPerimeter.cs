using UnityEngine;

namespace Bato
{
    /// <summary>
    /// Construit 4 murs visibles autour de l'arène d'eau (collision + mesh).
    /// À poser sur l'objet Walls de la scène Arena.
    /// </summary>
    [DisallowMultipleComponent]
    public class ArenaPerimeter : MonoBehaviour
    {
        [Tooltip("Demi-taille de l'arène (océan 190 → 95).")]
        [SerializeField] float m_HalfExtent = 95f;
        [SerializeField] float m_Height = 22f;
        [SerializeField] float m_Thickness = 5f;
        [SerializeField] Color m_Color = new Color(0.42f, 0.36f, 0.30f, 1f);
        [SerializeField] bool m_RebuildOnStart = true;

        Material m_RuntimeMaterial;

        void Start()
        {
            if (m_RebuildOnStart) Rebuild();
        }

        [ContextMenu("Rebuild Walls")]
        public void Rebuild()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(child);
                else DestroyImmediate(child);
            }

            float length = m_HalfExtent * 2f + m_Thickness;
            float y = m_Height * 0.5f;

            CreateWall("WallNorth", new Vector3(0f, y, m_HalfExtent), new Vector3(length, m_Height, m_Thickness));
            CreateWall("WallSouth", new Vector3(0f, y, -m_HalfExtent), new Vector3(length, m_Height, m_Thickness));
            CreateWall("WallEast", new Vector3(m_HalfExtent, y, 0f), new Vector3(m_Thickness, m_Height, length));
            CreateWall("WallWest", new Vector3(-m_HalfExtent, y, 0f), new Vector3(m_Thickness, m_Height, length));
        }

        void CreateWall(string name, Vector3 localPosition, Vector3 scale)
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = name;
            wall.transform.SetParent(transform, false);
            wall.transform.localPosition = localPosition;
            wall.transform.localRotation = Quaternion.identity;
            wall.transform.localScale = scale;

            var renderer = wall.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = GetMaterial();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.receiveShadows = true;

            // Cube primitive a déjà un BoxCollider à l'échelle du transform.
        }

        Material GetMaterial()
        {
            if (m_RuntimeMaterial != null) return m_RuntimeMaterial;

            var shader = Shader.Find("Universal Render Pipeline/Lit")
                         ?? Shader.Find("HDRP/Lit")
                         ?? Shader.Find("Standard");
            m_RuntimeMaterial = new Material(shader) { name = "ArenaWall (runtime)", color = m_Color };
            if (m_RuntimeMaterial.HasProperty("_BaseColor"))
                m_RuntimeMaterial.SetColor("_BaseColor", m_Color);
            if (m_RuntimeMaterial.HasProperty("_Color"))
                m_RuntimeMaterial.SetColor("_Color", m_Color);
            return m_RuntimeMaterial;
        }

        void OnDestroy()
        {
            if (m_RuntimeMaterial != null)
            {
                if (Application.isPlaying) Destroy(m_RuntimeMaterial);
                else DestroyImmediate(m_RuntimeMaterial);
            }
        }
    }
}
