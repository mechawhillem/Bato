using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Bato
{
    /// <summary>
    /// Inventaire d'un objet (style kart) + bouclier. Utilisation : touche F / UseItem.
    /// </summary>
    public class BoatLoadout : NetworkBehaviour
    {
        [SerializeField] GameObject m_RemoteBoatPrefab;
        [SerializeField] float m_ShieldDuration = 10f;
        [SerializeField] float m_SpecialBallSpeed = 55f;
        [SerializeField] float m_RemoteBoatLifetimeMin = 3f;
        [SerializeField] float m_RemoteBoatLifetimeMax = 5f;

        readonly NetworkVariable<byte> m_HeldItem = new NetworkVariable<byte>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        readonly NetworkVariable<float> m_ShieldEndTime = new NetworkVariable<float>(
            0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        BoatHealth m_Health;
        BoatCannon m_Cannon;
        BoatNetworkAuthority m_Authority;
        InputAction m_UseAction;
        bool m_UsingRemote;

        public PickupItemType HeldItem => (PickupItemType)m_HeldItem.Value;
        public bool HasItem => HeldItem != PickupItemType.None;
        public bool IsShielded =>
            IsSpawned && NetworkManager != null &&
            NetworkManager.ServerTime.Time < m_ShieldEndTime.Value;

        public float ShieldRemaining =>
            IsSpawned && NetworkManager != null
                ? Mathf.Max(0f, (float)(m_ShieldEndTime.Value - NetworkManager.ServerTime.Time))
                : 0f;
        public bool IsControllingRemote => m_UsingRemote;

        void Awake()
        {
            m_Health = GetComponent<BoatHealth>();
            m_Cannon = GetComponent<BoatCannon>();
            m_Authority = GetComponent<BoatNetworkAuthority>();

            if (m_RemoteBoatPrefab == null)
                m_RemoteBoatPrefab = FindKamikazePrefab();
        }

        static GameObject FindKamikazePrefab()
        {
            var fromResources = Resources.Load<GameObject>("kamikaze");
            if (fromResources != null) return fromResources;

            var nm = NetworkManager.Singleton;
            if (nm != null)
            {
                foreach (var entry in nm.NetworkConfig.Prefabs.Prefabs)
                {
                    if (entry.Prefab != null && entry.Prefab.name == "kamikaze")
                        return entry.Prefab;
                }
            }

            return Resources.Load<GameObject>("RemoteControlledBoat");
        }

        public override void OnNetworkSpawn()
        {
            if (!IsOwner) return;

            if (m_Authority != null)
            {
                var playerInput = GetComponent<PlayerInput>();
                if (playerInput != null && playerInput.actions != null)
                {
                    m_UseAction = playerInput.actions.FindAction("UseItem", throwIfNotFound: false);
                    if (m_UseAction == null)
                        m_UseAction = playerInput.actions.FindAction("Interact", throwIfNotFound: false);
                }
            }
        }

        void Update()
        {
            if (!IsOwner || m_Health == null || !m_Health.IsAlive || m_UsingRemote) return;

            bool pressed = Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame;
            if (!pressed && m_UseAction != null)
                pressed = m_UseAction.WasPressedThisFrame();

            if (pressed && HasItem)
            {
                Vector3 boatVelocity = Vector3.zero;
                var rb = GetComponent<Rigidbody>();
                if (rb != null) boatVelocity = rb.linearVelocity;
                UseItemServerRpc(boatVelocity);
            }
        }

        /// <summary>Serveur : donne un objet si l'inventaire est vide.</summary>
        public bool TryGrantItem(PickupItemType type)
        {
            if (!IsServer || type == PickupItemType.None) return false;
            if (HeldItem != PickupItemType.None) return false;
            if (m_Health != null && !m_Health.IsAlive) return false;

            m_HeldItem.Value = (byte)type;
            return true;
        }

        public void ClearItem()
        {
            if (!IsServer) return;
            m_HeldItem.Value = (byte)PickupItemType.None;
        }

        public void ClearShield()
        {
            if (!IsServer) return;
            m_ShieldEndTime.Value = 0f;
        }

        [Rpc(SendTo.Server)]
        void UseItemServerRpc(Vector3 boatVelocity, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId) return;
            if (m_Health == null || !m_Health.IsAlive) return;
            if (m_UsingRemote) return;

            var item = HeldItem;
            if (item == PickupItemType.None) return;

            m_HeldItem.Value = (byte)PickupItemType.None;

            switch (item)
            {
                case PickupItemType.Shield:
                    m_ShieldEndTime.Value = (float)NetworkManager.ServerTime.Time + m_ShieldDuration;
                    break;
                case PickupItemType.FireBall:
                    FireSpecialBall(CannonballEffect.Burn, boatVelocity);
                    break;
                case PickupItemType.ChainBall:
                    FireSpecialBall(CannonballEffect.Slow, boatVelocity);
                    break;
                case PickupItemType.RemoteBoat:
                    StartCoroutine(LaunchRemoteBoat());
                    break;
            }
        }

        void FireSpecialBall(CannonballEffect effect, Vector3 boatVelocity)
        {
            if (m_Cannon == null)
            {
                Debug.LogWarning("[BoatLoadout] BoatCannon manquant pour tir spécial.");
                return;
            }

            m_Cannon.ServerFireSpecial(effect, m_SpecialBallSpeed, boatVelocity);
        }

        IEnumerator LaunchRemoteBoat()
        {
            if (m_RemoteBoatPrefab == null)
            {
                Debug.LogError("[BoatLoadout] Prefab barque RC non assigné.");
                yield break;
            }

            m_UsingRemote = true;
            SetRemoteControlOwnerRpc(true);

            Vector3 spawnPos = transform.position + transform.forward * 3f + Vector3.up * 0.5f;
            var go = Instantiate(m_RemoteBoatPrefab, spawnPos, transform.rotation);
            var net = go.GetComponent<NetworkObject>();
            net.SpawnWithOwnership(OwnerClientId);

            var bomb = go.GetComponent<RemoteControlledBoat>();
            float life = Random.Range(m_RemoteBoatLifetimeMin, m_RemoteBoatLifetimeMax);
            bomb.Arm(OwnerClientId, life);

            yield return new WaitForSeconds(life + 0.15f);

            m_UsingRemote = false;
            if (IsSpawned) SetRemoteControlOwnerRpc(false);
        }

        [Rpc(SendTo.Owner)]
        void SetRemoteControlOwnerRpc(bool controllingRemote)
        {
            m_UsingRemote = controllingRemote;
            if (m_Authority == null) return;

            // Pendant la RC : on coupe le bateau principal, la barque lit les inputs.
            if (controllingRemote)
                m_Authority.SetBoatDriveEnabled(false);
            else if (m_Health != null && m_Health.IsAlive)
                m_Authority.SetBoatDriveEnabled(true);
        }
    }
}
