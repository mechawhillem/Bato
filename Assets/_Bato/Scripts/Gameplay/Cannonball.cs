using Unity.Netcode;
using UnityEngine;

namespace Bato
{
    /// <summary>
    /// Boulet de canon, entièrement simulé par le serveur. Les clients ne font que suivre le
    /// NetworkTransform (autorité serveur) : personne ne peut inventer un impact chez lui.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Cannonball : NetworkBehaviour
    {
        [SerializeField] int m_Damage = 20;
        [SerializeField] float m_Lifetime = 6f;
        [SerializeField] GameObject m_ImpactVfxPrefab;

        Rigidbody m_Rigidbody;
        ulong m_ShooterClientId;
        float m_DespawnTime;
        bool m_Consumed;

        void Awake() => m_Rigidbody = GetComponent<Rigidbody>();

        public override void OnNetworkSpawn()
        {
            // Seul le serveur simule ; chez les clients le Rigidbody ne doit rien faire.
            m_Rigidbody.isKinematic = !IsServer;
            if (IsServer) m_DespawnTime = Time.time + m_Lifetime;
        }

        /// <summary>Serveur uniquement, appelé juste après Spawn().</summary>
        public void Launch(Vector3 velocity, ulong shooterClientId)
        {
            if (!IsServer) return;
            m_ShooterClientId = shooterClientId;
            m_Rigidbody.linearVelocity = velocity;
        }

        void Update()
        {
            if (!IsServer || m_Consumed) return;
            if (Time.time >= m_DespawnTime) Consume(transform.position);
        }

        void OnTriggerEnter(Collider other)
        {
            if (!IsServer || m_Consumed) return;

            var health = other.GetComponentInParent<BoatHealth>();
            if (health != null)
            {
                // Pas de tir ami sur soi-même.
                if (health.OwnerClientId == m_ShooterClientId) return;
                if (!health.IsAlive) return;

                health.ApplyDamage(m_Damage, m_ShooterClientId);
            }

            Consume(transform.position);
        }

        void Consume(Vector3 position)
        {
            m_Consumed = true;
            if (m_ImpactVfxPrefab) ImpactRpc(position);
            NetworkObject.Despawn();
        }

        [Rpc(SendTo.Everyone)]
        void ImpactRpc(Vector3 position)
        {
            if (m_ImpactVfxPrefab) Instantiate(m_ImpactVfxPrefab, position, Quaternion.identity);
        }
    }
}
