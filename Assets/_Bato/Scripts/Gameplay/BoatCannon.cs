using Unity.Netcode;
using UnityEngine;

namespace Bato
{
    /// <summary>
    /// Tir séparé : O = canon gauche (spawnpointG), P = canon droit (spawnpointD).
    /// Le client demande, le serveur revalide le cooldown puis spawne le boulet.
    /// </summary>
    public class BoatCannon : NetworkBehaviour
    {
        const byte k_Left = 0;
        const byte k_Right = 1;

        [SerializeField] GameObject m_CannonballPrefab;
        [Tooltip("Canon bâbord / gauche — touche O (AttackLeft).")]
        [SerializeField] Transform m_MuzzleLeft;
        [Tooltip("Canon tribord / droit — touche P (Attack).")]
        [SerializeField] Transform m_MuzzleRight;
        [SerializeField] float m_Cooldown = 0.8f;
        [SerializeField] float m_MuzzleSpeed = 26f;

        BoatNetworkAuthority m_Authority;
        BoatHealth m_Health;
        float m_LocalNextFireLeft;
        float m_LocalNextFireRight;
        float m_ServerNextFireLeft;
        float m_ServerNextFireRight;

        void Awake()
        {
            m_Authority = GetComponent<BoatNetworkAuthority>();
            m_Health = GetComponent<BoatHealth>();
        }

        void Update()
        {
            if (!IsOwner || m_Authority == null) return;
            if (m_Health != null && !m_Health.IsAlive) return;

            TryFire(m_Authority.FireLeftAction, k_Left, ref m_LocalNextFireLeft);
            TryFire(m_Authority.FireRightAction, k_Right, ref m_LocalNextFireRight);
        }

        void TryFire(UnityEngine.InputSystem.InputAction action, byte side, ref float nextLocal)
        {
            if (action == null || !action.WasPressedThisFrame()) return;
            if (Time.time < nextLocal) return;

            nextLocal = Time.time + m_Cooldown;
            FireRpc(side);
        }
 
        [Rpc(SendTo.Server)]
        void FireRpc(byte side)
        {
            if (m_Health != null && !m_Health.IsAlive) return;

            if (side == k_Left)
            {
                if (Time.time < m_ServerNextFireLeft) return;
                m_ServerNextFireLeft = Time.time + m_Cooldown * 0.9f;
            }
            else
            {
                if (Time.time < m_ServerNextFireRight) return;
                m_ServerNextFireRight = Time.time + m_Cooldown * 0.9f;
            }

            if (m_CannonballPrefab == null)
            {
                Debug.LogError("[BoatCannon] Prefab de boulet non assigné.");
                return;
            }

            var muzzle = side == k_Left ? m_MuzzleLeft : m_MuzzleRight;
            if (muzzle == null)
            {
                Debug.LogWarning($"[BoatCannon] Muzzle {(side == k_Left ? "Left" : "Right")} non assigné.");
                return;
            }

            SpawnBall(muzzle.position, muzzle.forward);
        }

        void SpawnBall(Vector3 origin, Vector3 direction)
        {
            direction = direction.sqrMagnitude > 0.001f ? direction.normalized : transform.forward;

            var ball = Instantiate(m_CannonballPrefab, origin + direction * 0.6f, Quaternion.LookRotation(direction));
            ball.GetComponent<NetworkObject>().Spawn();
            ball.GetComponent<Cannonball>().Launch(direction * m_MuzzleSpeed, OwnerClientId);
        }
    }
}
