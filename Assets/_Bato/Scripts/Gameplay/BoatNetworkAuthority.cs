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
        BoatHealth m_Health;
        BoatLoadout m_Loadout;

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
            m_Health = GetComponent<BoatHealth>();
            m_Loadout = GetComponent<BoatLoadout>();

            Debug.Log($"[BoatNetworkAuthority] Awake sur '{name}' | PlayerInput={(m_PlayerInput != null)} | InputSource={(m_InputSource != null)} | Movement={(m_Movement != null)} | NetworkObject={(NetworkObject != null)}", this);

            // Avant Spawn : les NetworkBehaviour doivent déjà être présents.
            if (GetComponent<BoatStatusEffects>() == null) gameObject.AddComponent<BoatStatusEffects>();
            if (GetComponent<BoatLoadout>() == null) gameObject.AddComponent<BoatLoadout>();

            // Tout est coupé tant qu'on ne sait pas si ce bateau nous appartient. Awake tourne
            // même sur un composant désactivé, donc BoatMovementController configure quand même
            // le Rigidbody (gravité, damping, contraintes) sur tous les clients.
            SetControlEnabled(false);
            if (m_Buoyancy) m_Buoyancy.enabled = false;
        }

        public override void OnNetworkSpawn()
        {
            bool owned = IsOwner;
            Debug.Log($"[BoatNetworkAuthority] OnNetworkSpawn sur '{name}' | IsSpawned={IsSpawned} | IsOwner={IsOwner} | IsClient={IsClient} | IsServer={IsServer} | OwnerClientId={OwnerClientId} | LocalClientId={NetworkManager?.LocalClientId}", this);

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
                Debug.Log($"[BoatNetworkAuthority] Actions de tir | Attack={(FireRightAction != null)} | AttackLeft={(FireLeftAction != null)} | ActionMap={m_PlayerInput.currentActionMap?.name ?? "null"}", this);
            }
        }

        /// <summary>Coupe ou rend le contrôle (mort / respawn).</summary>
        public void SetControlEnabled(bool value)
        {
            if (m_PlayerInput) m_PlayerInput.enabled = value;
            if (m_InputSource) m_InputSource.enabled = value;
            if (m_Movement) m_Movement.enabled = value;

            Debug.Log($"[BoatNetworkAuthority] SetControlEnabled({value}) sur '{name}' | PlayerInput={(m_PlayerInput != null ? m_PlayerInput.enabled : false)} | InputSource={(m_InputSource != null ? m_InputSource.enabled : false)} | Movement={(m_Movement != null ? m_Movement.enabled : false)} | IsOwner={IsOwner} | IsSpawned={IsSpawned}", this);

            if (!value && m_Rigidbody != null && !m_Rigidbody.isKinematic)
            {
                m_Rigidbody.linearVelocity = Vector3.zero;
                m_Rigidbody.angularVelocity = Vector3.zero;
            }
        }

        /// <summary>Coupe seulement le bateau (garde PlayerInput pour piloter une barque RC).</summary>
        public void SetBoatDriveEnabled(bool value)
        {
            if (m_InputSource) m_InputSource.enabled = value;
            if (m_Movement) m_Movement.enabled = value;

            if (!value && m_Rigidbody != null && !m_Rigidbody.isKinematic)
            {
                m_Rigidbody.linearVelocity = Vector3.zero;
                m_Rigidbody.angularVelocity = Vector3.zero;
            }
        }

        private void LateUpdate()
        {
            if (!IsSpawned || !IsOwner) return;
            if (m_Health != null && !m_Health.IsAlive) return;
            if (m_Loadout != null && m_Loadout.IsControllingRemote) return;
            if (m_PlayerInput == null || m_InputSource == null || m_Movement == null) return;

            if (!m_PlayerInput.enabled || !m_InputSource.enabled || !m_Movement.enabled)
            {
                Debug.LogWarning($"[BoatNetworkAuthority] Contrôle local perdu sur '{name}', réactivation automatique.", this);
                SetControlEnabled(true);
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
