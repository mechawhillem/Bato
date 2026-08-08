using Unity.Netcode;
using UnityEngine;

namespace Bato
{
    /// <summary>
    /// Accroche la caméra locale au bateau que ce client possède.
    /// La caméra reste locale et le bateau reste le seul objet réseau concerné.
    /// </summary>
    public class LocalBoatCameraBinder : MonoBehaviour
    {
        [SerializeField] Features.Camera.BoatCameraController m_Camera;

        NetworkObject m_BoundPlayer;

        void Awake()
        {
            if (m_Camera == null) m_Camera = GetComponent<Features.Camera.BoatCameraController>();

            if (m_Camera == null)
            {
                Debug.LogError("[Bato] LocalBoatCameraBinder ne trouve pas de BoatCameraController.", this);
                enabled = false;
            }
        }

        void Update()
        {
            var manager = NetworkManager.Singleton;
            var playerObject = manager != null && manager.IsClient
                ? manager.LocalClient?.PlayerObject
                : null;

            if (playerObject == m_BoundPlayer) return;

            if (playerObject == null)
            {
                Unbind();
                return;
            }

            Bind(playerObject);
        }

        void Bind(NetworkObject playerObject)
        {
            var authority = playerObject.GetComponentInChildren<BoatNetworkAuthority>();
            if (authority == null)
            {
                Debug.LogError(
                    $"[Bato] Le prefab joueur '{playerObject.name}' n'a pas de BoatNetworkAuthority : " +
                    "la caméra n'a rien à suivre.", playerObject);
                return;
            }

            m_Camera.SetTarget(authority.transform, authority.GetComponent<Rigidbody>(), snapBehind: true);
            m_BoundPlayer = playerObject;
        }

        void Unbind()
        {
            if (m_BoundPlayer == null) return;

            m_Camera.SetTarget(null, null, snapBehind: false);
            m_BoundPlayer = null;
        }
    }
}
