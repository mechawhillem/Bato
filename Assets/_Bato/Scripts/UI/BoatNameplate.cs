using TMPro;
using Unity.Netcode;
using UnityEngine;

namespace Bato
{
    /// <summary>
    /// Étiquette flottante au-dessus du bateau, avec le pseudo de son propriétaire.
    ///
    /// Rien n'est répliqué ici : le pseudo vit déjà dans la liste des joueurs d'ArenaBootstrap,
    /// partagée par le réseau. On se contente de la relire quand elle bouge, en surveillant
    /// LobbyRevision — donc zéro octet de trafic en plus par étiquette.
    ///
    /// Tout est construit par code, comme les particules d'écume : pas de prefab d'UI à maintenir
    /// en parallèle du bateau, et la hauteur se déduit de la coque au lieu d'être saisie à la main.
    ///
    /// À poser sur la racine du bateau, à côté de BoatNetworkAuthority.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class BoatNameplate : MonoBehaviour
    {
        [Tooltip("Hauteur de l'étiquette au-dessus du sommet de la coque, en mètres.")]
        [SerializeField, Min(0f)] float m_HeightAboveHull = 0.8f;

        [Tooltip("Hauteur des caractères, en mètres.")]
        [SerializeField, Min(0.05f)] float m_FontSize = 0.35f;

        [Tooltip("Masquer sa propre étiquette : elle gêne la vue sans rien apprendre au joueur.")]
        [SerializeField] bool m_HideForLocalPlayer = true;

        [Tooltip("Distance au-delà de laquelle l'étiquette disparaît. 0 = toujours visible.")]
        [SerializeField, Min(0f)] float m_MaxDistance = 80f;

        // Le canvas est dessiné en grand puis réduit : TMP rastérise mal des tailles de police
        // inférieures à 1, et on veut des caractères de 35 cm de haut.
        const float k_CanvasScale = 0.01f;

        NetworkObject m_NetworkObject;
        Transform m_Root;
        TextMeshProUGUI m_Label;
        Camera m_Camera;
        int m_KnownRevision = -1;

        void Awake()
        {
            m_NetworkObject = GetComponent<NetworkObject>();
            Build();
        }

        void Build()
        {
            var go = new GameObject("Nameplate", typeof(RectTransform), typeof(Canvas));
            m_Root = go.transform;
            m_Root.SetParent(transform, false);
            m_Root.localPosition = Vector3.up * (LocalHullTop() + m_HeightAboveHull);

            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            var rect = (RectTransform)m_Root;
            rect.sizeDelta = new Vector2(4f / k_CanvasScale, 1f / k_CanvasScale);
            rect.localScale = Vector3.one * k_CanvasScale;

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(m_Root, false);

            var labelRect = (RectTransform)labelGo.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.sizeDelta = Vector2.zero;

            m_Label = labelGo.AddComponent<TextMeshProUGUI>();
            m_Label.alignment = TextAlignmentOptions.Center;
            m_Label.fontSize = m_FontSize / k_CanvasScale;
            m_Label.textWrappingMode = TextWrappingModes.NoWrap;
            m_Label.raycastTarget = false;
            m_Label.text = string.Empty;

            // Contour sombre : sans lui, un pseudo clair devient illisible sur une crête écumeuse.
            m_Label.outlineWidth = 0.22f;
            m_Label.outlineColor = new Color32(0, 20, 30, 255);
        }

        /// <summary>Sommet de la coque en local, pour ne pas coller l'étiquette dans le mât.</summary>
        float LocalHullTop()
        {
            var hull = GetComponent<Collider>();
            if (hull == null) return 1f;

            // bounds est en monde : on repasse en local pour rester juste si le bateau est incliné
            // au moment du spawn.
            float scale = Mathf.Max(0.0001f, Mathf.Abs(transform.lossyScale.y));
            return hull.bounds.extents.y / scale;
        }

        void LateUpdate()
        {
            if (m_Label == null) return;

            if (m_HideForLocalPlayer && m_NetworkObject.IsOwner)
            {
                SetVisible(false);
                return;
            }

            if (m_Camera == null)
            {
                m_Camera = Camera.main;
                if (m_Camera == null)
                {
                    SetVisible(false);
                    return;
                }
            }

            var toCamera = m_Root.position - m_Camera.transform.position;

            if (m_MaxDistance > 0f && toCamera.sqrMagnitude > m_MaxDistance * m_MaxDistance)
            {
                SetVisible(false);
                return;
            }

            RefreshName();
            SetVisible(true);

            // Face à la caméra, mais toujours d'aplomb : une étiquette qui roule avec le bateau
            // devient illisible dès qu'il gîte.
            m_Root.rotation = Quaternion.LookRotation(toCamera, Vector3.up);
        }

        /// <summary>
        /// Ne relit le pseudo que lorsque le salon a changé. Sans ce garde-fou on reconstruirait
        /// le maillage de texte de TMP à chaque frame et pour chaque bateau.
        /// </summary>
        void RefreshName()
        {
            var arena = ArenaBootstrap.Instance;
            if (arena == null || arena.LobbyRevision == m_KnownRevision) return;

            m_KnownRevision = arena.LobbyRevision;
            m_Label.text = arena.GetName(m_NetworkObject.OwnerClientId);
        }

        void SetVisible(bool visible)
        {
            if (m_Root != null && m_Root.gameObject.activeSelf != visible)
            {
                m_Root.gameObject.SetActive(visible);
            }
        }
    }
}
