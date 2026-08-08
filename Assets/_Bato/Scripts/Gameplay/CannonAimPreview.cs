using UnityEngine;

namespace Bato
{
    /// <summary>
    /// Prévisualisation locale de la trajectoire balistique (ligne pointillée en cloche + flèche).
    /// </summary>
    public class CannonAimPreview : MonoBehaviour
    {
        const int k_MaxPoints = 96;

        [SerializeField] float m_LineWidth = 0.12f;
        [SerializeField] Color m_Color = new Color(1f, 0.85f, 0.25f, 0.9f);
        [SerializeField] float m_SampleDt = 0.05f;
        [SerializeField] float m_MinY = -5f;
        [SerializeField] float m_MaxFlightTime = 6f;

        readonly Vector3[] m_Points = new Vector3[k_MaxPoints];
        LineRenderer m_Line;
        LineRenderer m_Arrow;
        Material m_DashedMaterial;
        Material m_SolidMaterial;

        void Awake()
        {
            m_DashedMaterial = CreateDashedMaterial(m_Color);
            m_SolidMaterial = CreateSolidMaterial(m_Color);

            m_Line = CreateLine("TrajectoryLine", m_DashedMaterial, m_LineWidth);
            m_Line.textureMode = LineTextureMode.Tile;
            m_Line.textureScale = new Vector2(1f / 1.1f, 1f);

            m_Arrow = CreateLine("TrajectoryArrow", m_SolidMaterial, m_LineWidth * 1.35f);
            Hide();
        }

        void OnDestroy()
        {
            if (m_DashedMaterial) Destroy(m_DashedMaterial);
            if (m_SolidMaterial) Destroy(m_SolidMaterial);
        }

        public void Hide()
        {
            if (m_Line) m_Line.enabled = false;
            if (m_Arrow) m_Arrow.enabled = false;
        }

        public void Show(Vector3 origin, Vector3 velocity)
        {
            int count = Sample(origin, velocity);
            if (count < 2)
            {
                Hide();
                return;
            }

            m_Line.enabled = true;
            m_Line.positionCount = count;
            m_Line.SetPositions(m_Points);

            // Flèche au bout, orientée selon la tangente locale.
            Vector3 tip = m_Points[count - 1];
            Vector3 tangent = (tip - m_Points[count - 2]).normalized;
            if (tangent.sqrMagnitude < 0.0001f) tangent = velocity.normalized;

            Vector3 right = Vector3.Cross(Vector3.up, tangent);
            if (right.sqrMagnitude < 0.0001f) right = Vector3.Cross(Vector3.forward, tangent);
            right.Normalize();

            float head = 0.55f;
            m_Arrow.enabled = true;
            m_Arrow.positionCount = 3;
            m_Arrow.SetPosition(0, tip - tangent * head + right * head * 0.45f);
            m_Arrow.SetPosition(1, tip);
            m_Arrow.SetPosition(2, tip - tangent * head - right * head * 0.45f);
        }

        int Sample(Vector3 origin, Vector3 velocity)
        {
            Vector3 pos = origin;
            Vector3 vel = velocity;
            m_Points[0] = pos;
            int count = 1;
            float time = 0f;

            for (int i = 1; i < k_MaxPoints; i++)
            {
                Vector3 next = pos + vel * m_SampleDt + 0.5f * Physics.gravity * (m_SampleDt * m_SampleDt);
                vel += Physics.gravity * m_SampleDt;
                time += m_SampleDt;

                if (Physics.Linecast(pos, next, out var hit, ~0, QueryTriggerInteraction.Ignore))
                {
                    m_Points[count++] = hit.point;
                    break;
                }

                pos = next;
                m_Points[count++] = pos;

                if (pos.y < m_MinY || time >= m_MaxFlightTime) break;
            }

            return count;
        }

        LineRenderer CreateLine(string name, Material material, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.widthMultiplier = width;
            line.numCapVertices = 2;
            line.numCornerVertices = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.useWorldSpace = true;
            line.alignment = LineAlignment.View;
            return line;
        }

        static Material CreateDashedMaterial(Color color)
        {
            var mat = new Material(FindLineShader()) { name = "TrajectoryDash", color = color };

            var tex = new Texture2D(16, 1, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Point,
                name = "TrajectoryDashTex"
            };

            for (int x = 0; x < 16; x++)
            {
                bool on = x < 9;
                tex.SetPixel(x, 0, on ? Color.white : new Color(1f, 1f, 1f, 0f));
            }

            tex.Apply(false, true);
            mat.mainTexture = tex;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            return mat;
        }

        static Material CreateSolidMaterial(Color color)
        {
            var mat = new Material(FindLineShader()) { name = "TrajectorySolid", color = color };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            return mat;
        }

        static Shader FindLineShader()
        {
            return Shader.Find("Sprites/Default")
                   ?? Shader.Find("Universal Render Pipeline/Unlit")
                   ?? Shader.Find("Unlit/Color")
                   ?? Shader.Find("Legacy Shaders/Particles/Alpha Blended");
        }
    }
}
