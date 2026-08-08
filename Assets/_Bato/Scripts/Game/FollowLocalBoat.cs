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

        [Tooltip("Lissage vertical, volontairement plus mou : la caméra ne doit pas copier le " +
                 "ballant du bateau sur la houle, sinon c'est le mal de mer garanti.")]
        [SerializeField] float m_VerticalSmoothTime = 0.9f;

        [SerializeField] float m_RotationLerp = 6f;
        [Tooltip("Suit le cap du bateau au lieu de rester orientée vers le nord.")]
        [SerializeField] bool m_FollowHeading = true;

        Transform m_Target;
        Vector3 m_Velocity;
        float m_VerticalVelocity;

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

            // Horizontal réactif, vertical mou : la caméra suit le bateau sans épouser la houle.
            var horizontal = Vector3.SmoothDamp(
                new Vector3(transform.position.x, 0f, transform.position.z),
                new Vector3(desired.x, 0f, desired.z),
                ref m_Velocity, m_PositionSmoothTime);

            float vertical = Mathf.SmoothDamp(
                transform.position.y, desired.y, ref m_VerticalVelocity, m_VerticalSmoothTime);

            transform.position = new Vector3(horizontal.x, vertical, horizontal.z);

            // On vise la hauteur lissée de la caméra plutôt que le bateau lui-même, sinon
            // l'horizon tangue à chaque vague.
            var lookTarget = new Vector3(m_Target.position.x, vertical - m_Offset.y + 1.5f, m_Target.position.z);
            var lookRotation = Quaternion.LookRotation(lookTarget - transform.position);
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
