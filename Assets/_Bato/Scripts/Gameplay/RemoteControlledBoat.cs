using Features.Camera;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Bato
{
    /// <summary>
    /// Barque kamikaze pilotable : explose à la fin du timer OU au contact d'un ennemi.
    /// Chez le propriétaire, la caméra suit la barque jusqu'à l'explosion.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class RemoteControlledBoat : NetworkBehaviour
    {
        [SerializeField] float m_Acceleration = 40f;
        [SerializeField] float m_MaxSpeed = 16f;
        [SerializeField] float m_SteerTorque = 40f;
        [SerializeField] float m_ExplosionRadius = 8f;
        [SerializeField] float m_ContactRadius = 2.2f;
        [SerializeField] int m_ExplosionDamage = 35;
        [SerializeField] GameObject m_ExplosionVfxPrefab;

        Rigidbody m_Rigidbody;
        ulong m_OwnerClientId;
        float m_ExplodeAt;
        bool m_Armed;
        bool m_Exploded;
        bool m_CameraAttached;
        InputAction m_MoveAction;

        void Awake() => m_Rigidbody = GetComponent<Rigidbody>();

        public override void OnNetworkSpawn()
        {
            m_Rigidbody.isKinematic = !IsOwner;
            if (!IsOwner) return;

            m_Rigidbody.constraints =
                RigidbodyConstraints.FreezePositionY |
                RigidbodyConstraints.FreezeRotationX |
                RigidbodyConstraints.FreezeRotationZ;

            var player = NetworkManager.LocalClient?.PlayerObject;
            var pi = player != null ? player.GetComponent<PlayerInput>() : null;
            if (pi != null && pi.actions != null)
                m_MoveAction = pi.actions.FindAction("Move", throwIfNotFound: false);

            // Élan initial pour que le follow caméra parte bien derrière.
            m_Rigidbody.linearVelocity = transform.forward * (m_MaxSpeed * 0.55f);
            AttachCamera();
        }

        public override void OnNetworkDespawn()
        {
            if (m_CameraAttached)
                RestoreCamera();
        }

        /// <summary>Serveur : arme la barque et planifie l'explosion.</summary>
        public void Arm(ulong ownerClientId, float lifetime)
        {
            if (!IsServer) return;
            m_OwnerClientId = ownerClientId;
            m_ExplodeAt = Time.time + lifetime;
            m_Armed = true;
        }

        void AttachCamera()
        {
            var cam = FindLocalCamera();
            if (cam == null) return;
            cam.SetTarget(transform, m_Rigidbody, snapBehind: true);
            m_CameraAttached = true;
        }

        void RestoreCamera()
        {
            m_CameraAttached = false;
            var cam = FindLocalCamera();
            if (cam == null) return;

            var player = NetworkManager.Singleton != null
                ? NetworkManager.Singleton.LocalClient?.PlayerObject
                : null;
            if (player == null)
            {
                cam.SetTarget(null, null);
                return;
            }

            cam.SetTarget(player.transform, player.GetComponent<Rigidbody>(), snapBehind: true);
        }

        static BoatCameraController FindLocalCamera()
        {
#if UNITY_2023_1_OR_NEWER
            return Object.FindFirstObjectByType<BoatCameraController>();
#else
            return Object.FindObjectOfType<BoatCameraController>();
#endif
        }

        void FixedUpdate()
        {
            if (!IsOwner || m_Exploded) return;

            Vector2 input = Vector2.zero;
            if (m_MoveAction != null) input = m_MoveAction.ReadValue<Vector2>();
            else if (Keyboard.current != null)
            {
                if (Keyboard.current.wKey.isPressed) input.y += 1f;
                if (Keyboard.current.sKey.isPressed) input.y -= 1f;
                if (Keyboard.current.aKey.isPressed) input.x -= 1f;
                if (Keyboard.current.dKey.isPressed) input.x += 1f;
            }

            if (!Mathf.Approximately(input.y, 0f))
                m_Rigidbody.AddForce(transform.forward * (input.y * m_Acceleration), ForceMode.Acceleration);

            if (!Mathf.Approximately(input.x, 0f))
                m_Rigidbody.AddTorque(Vector3.up * (input.x * m_SteerTorque), ForceMode.Acceleration);

            Vector3 planar = Vector3.ProjectOnPlane(m_Rigidbody.linearVelocity, Vector3.up);
            if (planar.sqrMagnitude > m_MaxSpeed * m_MaxSpeed)
            {
                planar = planar.normalized * m_MaxSpeed;
                m_Rigidbody.linearVelocity = new Vector3(planar.x, m_Rigidbody.linearVelocity.y, planar.z);
            }
        }

        void Update()
        {
            if (!IsServer || !m_Armed || m_Exploded) return;

            if (Time.time >= m_ExplodeAt || TouchesEnemy())
                Explode();
        }

        bool TouchesEnemy()
        {
            var hits = Physics.OverlapSphere(transform.position, m_ContactRadius);
            foreach (var col in hits)
            {
                var health = col.GetComponentInParent<BoatHealth>();
                if (health == null || !health.IsAlive) continue;
                if (health.OwnerClientId == m_OwnerClientId) continue;
                return true;
            }

            return false;
        }

        void Explode()
        {
            if (m_Exploded) return;
            m_Exploded = true;

            Vector3 pos = transform.position;
            var hits = Physics.OverlapSphere(pos, m_ExplosionRadius);
            foreach (var col in hits)
            {
                var health = col.GetComponentInParent<BoatHealth>();
                if (health == null || !health.IsAlive) continue;
                if (health.OwnerClientId == m_OwnerClientId) continue;
                health.ApplyDamage(m_ExplosionDamage, m_OwnerClientId);
            }

            ExplosionRpc(pos);
            if (IsSpawned) NetworkObject.Despawn();
            else Destroy(gameObject);
        }

        [Rpc(SendTo.Everyone)]
        void ExplosionRpc(Vector3 position)
        {
            if (m_ExplosionVfxPrefab != null)
            {
                Instantiate(m_ExplosionVfxPrefab, position, Quaternion.identity);
                return;
            }

            var flash = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            flash.name = "KamikazeExplosion";
            flash.transform.position = position;
            flash.transform.localScale = Vector3.one * 2.5f;
            Object.Destroy(flash.GetComponent<Collider>());
            var renderer = flash.GetComponent<MeshRenderer>();
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
            if (shader != null)
            {
                var mat = new Material(shader) { color = new Color(1f, 0.45f, 0.1f, 1f) };
                renderer.sharedMaterial = mat;
            }

            Object.Destroy(flash, 0.45f);
        }
    }
}
