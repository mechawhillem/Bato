using Unity.Netcode;
using UnityEngine;

namespace Bato
{
    /// <summary>
    /// Caméra unique de la scène (pas dans le prefab : sinon N caméras et N AudioListeners).
    /// Elle s'accroche au bateau que ce client possède dès qu'il est spawné.
    /// </summary>
    public class FollowLocalBoat : MonoBehaviour
    {
        [SerializeField] Vector3 m_Offset = new Vector3(0f, 11f, -14f);
        [SerializeField] float m_PositionSmoothTime = 0.18f;
        [SerializeField] float m_RotationLerp = 6f;
        [Tooltip("Suit le cap du bateau au lieu de rester orientée vers le nord.")]
        [SerializeField] bool m_FollowHeading = true;

        Transform m_Target;
        Vector3 m_Velocity;

        void LateUpdate()
        {
            if (m_Target == null)
            {
                TryAcquireTarget();
                if (m_Target == null) return;
            }

            var basis = m_FollowHeading
                ? Quaternion.Euler(0f, m_Target.eulerAngles.y, 0f)
                : Quaternion.identity;

            var desired = m_Target.position + basis * m_Offset;
            transform.position = Vector3.SmoothDamp(transform.position, desired, ref m_Velocity, m_PositionSmoothTime);

            var lookRotation = Quaternion.LookRotation(m_Target.position + Vector3.up * 1.5f - transform.position);
            transform.rotation = Quaternion.Slerp(transform.rotation, lookRotation, m_RotationLerp * Time.deltaTime);
        }

        void TryAcquireTarget()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsClient) return;

            var playerObject = nm.LocalClient?.PlayerObject;
            if (playerObject != null) m_Target = playerObject.transform;
        }
    }
}
