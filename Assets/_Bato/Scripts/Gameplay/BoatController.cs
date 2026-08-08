using Unity.Netcode;
using UnityEngine;

namespace Bato
{
    /// <summary>
    /// Déplacement du bateau, en autorité propriétaire : seul le client qui possède ce bateau
    /// simule sa physique. Les autres ne voient que le NetworkTransform interpolé, avec un
    /// Rigidbody kinematic pour qu'aucune deuxième simulation ne vienne se battre avec lui.
    ///
    /// Le NetworkTransform de ce prefab DOIT être en AuthorityMode = Owner.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class BoatController : NetworkBehaviour
    {
        [Header("Conduite")]
        [SerializeField] float m_ThrustForce = 900f;
        [SerializeField] float m_ReverseMultiplier = 0.45f;
        [SerializeField] float m_TurnTorque = 220f;
        [SerializeField] float m_MaxSpeed = 14f;

        [Header("Ressenti")]
        [Tooltip("Freine la dérive latérale pour que le bateau ne glisse pas comme un palet.")]
        [SerializeField] float m_LateralGrip = 6f;

        Rigidbody m_Rigidbody;
        BoatInput m_Input;
        BoatHealth m_Health;

        void Awake()
        {
            m_Rigidbody = GetComponent<Rigidbody>();
            m_Input = GetComponent<BoatInput>();
            m_Health = GetComponent<BoatHealth>();

            // Désactivé par défaut : réactivé pour le propriétaire dans OnNetworkSpawn.
            enabled = false;
            if (m_Input) m_Input.enabled = false;
        }

        public override void OnNetworkSpawn()
        {
            bool owned = IsOwner;

            m_Rigidbody.isKinematic = !owned;
            enabled = owned;
            if (m_Input) m_Input.enabled = owned;
        }

        void FixedUpdate()
        {
            if (!IsOwner || m_Input == null) return;
            if (m_Health != null && !m_Health.IsAlive)
            {
                m_Rigidbody.linearVelocity = Vector3.zero;
                m_Rigidbody.angularVelocity = Vector3.zero;
                return;
            }

            float throttle = m_Input.Throttle;
            if (throttle < 0f) throttle *= m_ReverseMultiplier;

            m_Rigidbody.AddForce(transform.forward * (throttle * m_ThrustForce), ForceMode.Force);

            // On ne tourne qu'en ayant de la vitesse : un bateau à l'arrêt ne pivote pas sur place.
            float speedFactor = Mathf.Clamp01(m_Rigidbody.linearVelocity.magnitude / 3f);
            m_Rigidbody.AddTorque(Vector3.up * (m_Input.Steer * m_TurnTorque * speedFactor), ForceMode.Force);

            KillLateralDrift();
            ClampSpeed();
        }

        void KillLateralDrift()
        {
            var velocity = m_Rigidbody.linearVelocity;
            float sideways = Vector3.Dot(velocity, transform.right);
            m_Rigidbody.AddForce(transform.right * (-sideways * m_LateralGrip), ForceMode.Force);
        }

        void ClampSpeed()
        {
            var velocity = m_Rigidbody.linearVelocity;
            if (velocity.magnitude > m_MaxSpeed)
            {
                m_Rigidbody.linearVelocity = velocity.normalized * m_MaxSpeed;
            }
        }

        /// <summary>Téléportation propre (respawn) : côté propriétaire uniquement.</summary>
        public void TeleportTo(Vector3 position, Quaternion rotation)
        {
            m_Rigidbody.linearVelocity = Vector3.zero;
            m_Rigidbody.angularVelocity = Vector3.zero;
            m_Rigidbody.position = position;
            m_Rigidbody.rotation = rotation;
            transform.SetPositionAndRotation(position, rotation);
        }
    }
}
