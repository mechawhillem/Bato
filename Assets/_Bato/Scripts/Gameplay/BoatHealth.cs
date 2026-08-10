using System.Collections;
using Bato.Water;
using Bitgem.VFX.StylisedWater;
using Unity.Netcode;
using UnityEngine;

namespace Bato
{
    /// <summary>
    /// PV, mort et respawn. Tout est décidé par le serveur (le host) — c'est la partie
    /// « autoritaire » du modèle hybride : le mouvement appartient au client, mais les dégâts,
    /// la mort et le score ne sont jamais décidés côté client.
    ///
    /// Le respawn passe par un RPC vers le propriétaire, parce que le NetworkTransform est en
    /// autorité propriétaire : le serveur n'a pas le droit de bouger ce transform lui-même.
    /// </summary>
    public class BoatHealth : NetworkBehaviour
    {
        [SerializeField] int m_MaxHealth = 100;
        [SerializeField] float m_RespawnDelay = 3f;
        [SerializeField] GameObject[] m_VisualsToHideOnDeath;
        [SerializeField] Collider[] m_CollidersToDisableOnDeath;
        [SerializeField, Min(0.1f)] float m_OutOfWaterGracePeriod = 0.8f;
        [SerializeField, Min(0f)] float m_OutOfWaterDeathHeight = -10f;

        readonly NetworkVariable<int> m_Health = new NetworkVariable<int>(
            100, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        readonly NetworkVariable<bool> m_IsAlive = new NetworkVariable<bool>(
            true, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        float m_OutOfWaterSince = -1f;
        bool m_OutOfWaterDeathTriggered;
        float m_NextOutOfWaterLogTime;

        public int Health => m_Health.Value;
        public int MaxHealth => m_MaxHealth;
        public bool IsAlive => m_IsAlive.Value;

        /// <summary>(pv actuels, pv max) — pour le HUD et les barres de vie.</summary>
        public event System.Action<int, int> HealthChanged;

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                m_Health.Value = m_MaxHealth;
                m_IsAlive.Value = true;
            }

            m_Health.OnValueChanged += OnHealthChanged;
            m_IsAlive.OnValueChanged += OnAliveChanged;

            m_OutOfWaterSince = -1f;
            m_OutOfWaterDeathTriggered = false;
            ApplyAliveVisuals(m_IsAlive.Value);
            HealthChanged?.Invoke(m_Health.Value, m_MaxHealth);
        }

        void Update()
        {
            if (!IsServer || !IsSpawned || !m_IsAlive.Value || m_OutOfWaterDeathTriggered) return;

            bool insideVolume = IsInsideWaterVolume(transform.position);
            if (Time.time >= m_NextOutOfWaterLogTime)
            {
                var surface = WaterSurface.Active;
                Debug.Log($"[BoatHealth] Détection '{name}' | pos={transform.position} | surface={(surface == null ? "null" : surface.GetType().Name)} | insideVolume={insideVolume} | buoyancy={GetComponent<BoatBuoyancy>()?.IsInWater}", this);
                m_NextOutOfWaterLogTime = Time.time + 0.5f;
            }

            if (insideVolume)
            {
                m_OutOfWaterSince = -1f;
                return;
            }

            if (m_OutOfWaterSince < 0f)
            {
                m_OutOfWaterSince = Time.time;
                Debug.Log($"[BoatHealth] '{name}' a quitté le WaterVolume : chute avant destruction.", this);
            }

            bool graceExpired = Time.time - m_OutOfWaterSince >= m_OutOfWaterGracePeriod;
            bool belowDeathHeight = transform.position.y <= m_OutOfWaterDeathHeight;
            if (!graceExpired && !belowDeathHeight) return;

            m_OutOfWaterDeathTriggered = true;
            Debug.Log($"[BoatHealth] '{name}' détruit hors WaterVolume | y={transform.position.y:0.00} | grace={graceExpired}", this);
            ApplyDamage(MaxHealth, OwnerClientId);
        }

        static bool IsInsideWaterVolume(Vector3 position)
        {
            if (WaterSurface.Active is WaveField waveField)
                return waveField.IsInsideWaterBounds(position);
            if (WaterSurface.Active is BitgemWaterSurface bitgemSurface)
                return bitgemSurface.IsInsideVolume(position);

            return true;
        }

        public override void OnNetworkDespawn()
        {
            m_Health.OnValueChanged -= OnHealthChanged;
            m_IsAlive.OnValueChanged -= OnAliveChanged;
        }

        void OnHealthChanged(int _, int current) => HealthChanged?.Invoke(current, m_MaxHealth);

        void OnAliveChanged(bool _, bool alive)
        {
            ApplyAliveVisuals(alive);

            // Un bateau coulé ne se pilote plus. Seul le propriétaire a du contrôle à couper.
            if (IsOwner)
            {
                var authority = GetComponent<BoatNetworkAuthority>();
                if (authority != null) authority.SetControlEnabled(alive);
            }
        }

        void ApplyAliveVisuals(bool alive)
        {
            foreach (var visual in m_VisualsToHideOnDeath)
            {
                if (visual) visual.SetActive(alive);
            }
            foreach (var col in m_CollidersToDisableOnDeath)
            {
                if (col) col.enabled = alive;
            }
        }

        /// <summary>Serveur uniquement. Retourne vrai si ce coup a tué le bateau.</summary>
        public bool ApplyDamage(int amount, ulong attackerClientId)
        {
            if (!IsServer || !m_IsAlive.Value || amount <= 0) return false;

            var loadout = GetComponent<BoatLoadout>();
            if (loadout != null && loadout.IsShielded) return false;

            m_Health.Value = Mathf.Max(0, m_Health.Value - amount);
            if (m_Health.Value > 0) return false;

            m_IsAlive.Value = false;

            var status = GetComponent<BoatStatusEffects>();
            status?.ClearAll();
            loadout?.ClearItem();
            loadout?.ClearShield();

            ArenaBootstrap.Instance?.ReportKill(attackerClientId, OwnerClientId);
            StartCoroutine(RespawnAfterDelay());
            return true;
        }

        IEnumerator RespawnAfterDelay()
        {
            yield return new WaitForSeconds(m_RespawnDelay);
            if (!IsSpawned) yield break;

            var (position, rotation) = ArenaBootstrap.Instance != null
                ? ArenaBootstrap.Instance.GetRandomSpawn()
                : (Vector3.zero, Quaternion.identity);

            // Le transform appartient au propriétaire : on lui demande de se téléporter.
            TeleportRpc(position, rotation);

            m_Health.Value = m_MaxHealth;
            m_OutOfWaterSince = -1f;
            m_OutOfWaterDeathTriggered = false;
            m_IsAlive.Value = true;
        }

        [Rpc(SendTo.Owner)]
        void TeleportRpc(Vector3 position, Quaternion rotation)
        {
            var authority = GetComponent<BoatNetworkAuthority>();
            if (authority != null) authority.TeleportTo(position, rotation);
            else transform.SetPositionAndRotation(position, rotation);
        }
    }
}
