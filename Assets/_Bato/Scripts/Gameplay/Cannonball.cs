using Bato.Water;
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
        [SerializeField] float m_Lifetime = 4f;
        [SerializeField] float m_SplashScale = 1.3f;

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

            if (Time.time >= m_DespawnTime)
            {
                Consume(transform.position, hitHull: false);
                return;
            }

            // Amerrissage. Le serveur fait foi, mais comme la houle est une fonction pure du temps
            // serveur, chaque client aurait trouvé le même point d'impact.
            var field = WaveField.Instance;
            if (field != null && transform.position.y < field.SampleHeight(transform.position))
            {
                Consume(transform.position, hitHull: false);
            }
        }

        void OnTriggerEnter(Collider other)
        {
            if (!IsServer || m_Consumed) return;

            bool hitHull = false;
            var health = other.GetComponentInParent<BoatHealth>();
            if (health != null)
            {
                // Pas de tir ami sur soi-même.
                if (health.OwnerClientId == m_ShooterClientId) return;
                if (!health.IsAlive) return;

                health.ApplyDamage(m_Damage, m_ShooterClientId);
                hitHull = true;
            }

            Consume(transform.position, hitHull);
        }

        void Consume(Vector3 position, bool hitHull)
        {
            m_Consumed = true;
            ImpactRpc(position, m_Rigidbody.linearVelocity.normalized, hitHull);
            NetworkObject.Despawn();
        }

        /// <summary>
        /// L'impact est décidé par le serveur, mais l'effet est joué localement par chacun :
        /// on ne diffuse qu'un point et un drapeau, jamais de particules.
        /// </summary>
        [Rpc(SendTo.Everyone)]
        void ImpactRpc(Vector3 position, Vector3 direction, bool hitHull)
        {
            if (hitHull)
            {
                WaterEffects.HullImpact(position, -direction);
            }
            else
            {
                WaterEffects.Splash(position, m_SplashScale);
            }
        }
    }
}
