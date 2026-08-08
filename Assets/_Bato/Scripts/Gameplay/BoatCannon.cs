using Unity.Netcode;
using UnityEngine;

namespace Bato
{
    /// <summary>
    /// Tir. Le client demande, le serveur décide : il revalide le cooldown avec sa propre horloge
    /// puis spawne le boulet. Un flash purement local part immédiatement côté tireur pour masquer
    /// le demi aller-retour réseau.
    /// </summary>
    public class BoatCannon : NetworkBehaviour
    {
        [SerializeField] GameObject m_CannonballPrefab;
        [Tooltip("Un boulet part de chaque canon : bordée des deux côtés, comme un vrai bateau.")]
        [SerializeField] Transform[] m_Muzzles;
        [SerializeField] float m_Cooldown = 0.8f;
        [SerializeField] float m_MuzzleSpeed = 26f;

        BoatInput m_Input;
        BoatHealth m_Health;
        float m_LocalNextFireTime;
        float m_ServerNextFireTime;

        void Awake()
        {
            m_Input = GetComponent<BoatInput>();
            m_Health = GetComponent<BoatHealth>();
        }

        void Update()
        {
            if (!IsOwner || m_Input == null) return;
            if (m_Health != null && !m_Health.IsAlive) return;
            if (!m_Input.FireHeld || Time.time < m_LocalNextFireTime) return;

            m_LocalNextFireTime = Time.time + m_Cooldown;

            // Le serveur re-dérive les positions depuis son propre état du bateau : le client
            // n'envoie donc rien qu'il puisse falsifier, juste « je tire ».
            FireRpc();
        }

        [Rpc(SendTo.Server)]
        void FireRpc()
        {
            if (m_Health != null && !m_Health.IsAlive) return;

            // Revalidation serveur : un client modifié qui spamme ne tire pas plus vite.
            if (Time.time < m_ServerNextFireTime) return;
            m_ServerNextFireTime = Time.time + m_Cooldown * 0.9f; // marge pour la gigue réseau

            if (m_CannonballPrefab == null)
            {
                Debug.LogError("[BoatCannon] Prefab de boulet non assigné.");
                return;
            }

            if (m_Muzzles == null || m_Muzzles.Length == 0)
            {
                SpawnBall(transform.position + transform.forward, transform.forward);
                return;
            }

            foreach (var muzzle in m_Muzzles)
            {
                if (muzzle == null) continue;
                SpawnBall(muzzle.position, muzzle.forward);
            }
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
