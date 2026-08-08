using Bato.Water;
using Features.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Bato
{
    /// <summary>
    /// Colle réseau autour du bateau de Features.Player, qui est volontairement network-agnostic.
    /// Ce composant est le seul à savoir qui possède quoi :
    ///  - le propriétaire simule sa physique et lit ses inputs ;
    ///  - les autres n'ont qu'un Rigidbody kinematic piloté par le NetworkTransform interpolé,
    ///    sinon deux simulations se battent et le bateau vibre.
    ///
    /// Le NetworkTransform de ce prefab doit être en AuthorityMode = Owner.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class BoatNetworkAuthority : NetworkBehaviour
    {
        Rigidbody m_Rigidbody;
        PlayerInput m_PlayerInput;
        PlayerInputSource m_InputSource;
        BoatMovementController m_Movement;
        BoatBuoyancy m_Buoyancy;

        /// <summary>Tir canon droit (P). Null tant que le bateau n'est pas possédé.</summary>
        public InputAction FireRightAction { get; private set; }

        /// <summary>Tir canon gauche (O). Null tant que le bateau n'est pas possédé.</summary>
        public InputAction FireLeftAction { get; private set; }

        void Awake()
        {
            m_Rigidbody = GetComponent<Rigidbody>();
            m_PlayerInput = GetComponent<PlayerInput>();
            m_InputSource = GetComponent<PlayerInputSource>();
            m_Movement = GetComponent<BoatMovementController>();
            m_Buoyancy = GetComponent<BoatBuoyancy>();

            // Tout est coupé tant qu'on ne sait pas si ce bateau nous appartient. Awake tourne
            // même sur un composant désactivé, donc BoatMovementController configure quand même
            // le Rigidbody (gravité, damping, contraintes) sur tous les clients.
            SetControlEnabled(false);
            if (m_Buoyancy) m_Buoyancy.enabled = false;
        }

        public override void OnNetworkSpawn()
        {
            bool owned = IsOwner;

            m_Rigidbody.isKinematic = !owned;
            SetControlEnabled(owned);

            // La flottaison n'est simulée que chez le propriétaire ; les autres reçoivent le
            // tangage tout cuit via le NetworkTransform. Volontairement hors de
            // SetControlEnabled : une épave continue de flotter après la mort du joueur.
            if (m_Buoyancy) m_Buoyancy.enabled = owned;

            if (owned && m_PlayerInput != null && m_PlayerInput.actions != null)
            {
                FireRightAction = m_PlayerInput.actions.FindAction("Attack", throwIfNotFound: false);
                FireLeftAction = m_PlayerInput.actions.FindAction("AttackLeft", throwIfNotFound: false);
            }
        }

        /// <summary>Coupe ou rend le contrôle (mort / respawn).</summary>
        public void SetControlEnabled(bool value)
        {
            if (m_PlayerInput) m_PlayerInput.enabled = value;
            if (m_InputSource) m_InputSource.enabled = value;
            if (m_Movement) m_Movement.enabled = value;

            if (!value && m_Rigidbody != null && !m_Rigidbody.isKinematic)
            {
                m_Rigidbody.linearVelocity = Vector3.zero;
                m_Rigidbody.angularVelocity = Vector3.zero;
            }
        }

        /// <summary>Téléportation propre (respawn). Côté propriétaire uniquement.</summary>
        public void TeleportTo(Vector3 position, Quaternion rotation)
        {
            if (!m_Rigidbody.isKinematic)
            {
                m_Rigidbody.linearVelocity = Vector3.zero;
                m_Rigidbody.angularVelocity = Vector3.zero;
            }

            m_Rigidbody.position = position;
            m_Rigidbody.rotation = rotation;
            transform.SetPositionAndRotation(position, rotation);
        }
    }
}
