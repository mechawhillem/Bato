using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Bato
{
    /// <summary>
    /// Caisses d'objets synchronisées via NetworkList (pas de NetworkObject par caisse).
    /// </summary>
    public class ArenaPickupSystem : NetworkBehaviour
    {
        [SerializeField] int m_PickupCount = 8;
        [SerializeField] float m_ArenaRadius = 55f;
        [SerializeField] float m_MinDistanceFromCenter = 12f;
        [SerializeField] float m_RespawnDelay = 12f;
        [SerializeField] float m_PickupRadius = 2.2f;
        [SerializeField] float m_BoxHeight = 1.2f;
        [SerializeField] Color m_BoxColor = new Color(1f, 0.85f, 0.2f);

        struct PickupSlot : INetworkSerializable, System.IEquatable<PickupSlot>
        {
            public int Id;
            public bool Active;
            public Vector3 Position;

            public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
            {
                serializer.SerializeValue(ref Id);
                serializer.SerializeValue(ref Active);
                serializer.SerializeValue(ref Position);
            }

            public bool Equals(PickupSlot other) =>
                Id == other.Id && Active == other.Active && Position == other.Position;
        }

        readonly NetworkList<PickupSlot> m_Pickups = new NetworkList<PickupSlot>();
        readonly Dictionary<int, GameObject> m_Visuals = new Dictionary<int, GameObject>();
        readonly Dictionary<int, float> m_RespawnAt = new Dictionary<int, float>();

        public override void OnNetworkSpawn()
        {
            m_Pickups.OnListChanged += OnPickupsChanged;
            RebuildAllVisuals();

            if (IsServer)
            {
                ArenaBootstrap.MatchStarted += OnMatchStartedServer;
                if (ArenaBootstrap.Instance != null && ArenaBootstrap.Instance.IsMatchStarted)
                    OnMatchStartedServer();
            }
        }

        public override void OnNetworkDespawn()
        {
            m_Pickups.OnListChanged -= OnPickupsChanged;
            if (IsServer)
                ArenaBootstrap.MatchStarted -= OnMatchStartedServer;
            ClearVisuals();
        }

        void OnMatchStartedServer()
        {
            if (!IsServer) return;
            m_Pickups.Clear();
            m_RespawnAt.Clear();

            for (int i = 0; i < m_PickupCount; i++)
            {
                m_Pickups.Add(new PickupSlot
                {
                    Id = i,
                    Active = true,
                    Position = RandomPickupPosition(),
                });
            }
        }

        Vector3 RandomPickupPosition()
        {
            for (int attempt = 0; attempt < 24; attempt++)
            {
                float angle = Random.Range(0f, Mathf.PI * 2f);
                float radius = Random.Range(m_MinDistanceFromCenter, m_ArenaRadius);
                var pos = new Vector3(Mathf.Cos(angle) * radius, m_BoxHeight, Mathf.Sin(angle) * radius);
                bool ok = true;
                for (int i = 0; i < m_Pickups.Count; i++)
                {
                    if (Vector3.Distance(pos, m_Pickups[i].Position) < 8f) { ok = false; break; }
                }
                if (ok) return pos;
            }

            return new Vector3(20f, m_BoxHeight, 0f);
        }

        void Update()
        {
            if (IsServer) TickServer();
            AnimateVisuals();
        }

        void TickServer()
        {
            // Respawn
            var toRespawn = new List<int>();
            foreach (var pair in m_RespawnAt)
            {
                if (Time.time >= pair.Value) toRespawn.Add(pair.Key);
            }
            foreach (int id in toRespawn)
            {
                m_RespawnAt.Remove(id);
                int index = IndexOf(id);
                if (index < 0) continue;
                var slot = m_Pickups[index];
                slot.Active = true;
                slot.Position = RandomPickupPosition();
                m_Pickups[index] = slot;
            }

            // Collecte
            if (NetworkManager == null) return;
            foreach (var client in NetworkManager.ConnectedClientsList)
            {
                var player = client.PlayerObject;
                if (player == null) continue;
                var loadout = player.GetComponent<BoatLoadout>();
                var health = player.GetComponent<BoatHealth>();
                if (loadout == null || health == null || !health.IsAlive) continue;
                if (loadout.HasItem) continue;

                for (int i = 0; i < m_Pickups.Count; i++)
                {
                    var slot = m_Pickups[i];
                    if (!slot.Active) continue;

                    Vector3 boatPos = player.transform.position;
                    boatPos.y = slot.Position.y;
                    if (Vector3.Distance(boatPos, slot.Position) > m_PickupRadius) continue;

                    var item = (PickupItemType)Random.Range(1, 5);
                    if (!loadout.TryGrantItem(item)) continue;

                    slot.Active = false;
                    m_Pickups[i] = slot;
                    m_RespawnAt[slot.Id] = Time.time + m_RespawnDelay;
                    break;
                }
            }
        }

        int IndexOf(int id)
        {
            for (int i = 0; i < m_Pickups.Count; i++)
                if (m_Pickups[i].Id == id) return i;
            return -1;
        }

        void OnPickupsChanged(NetworkListEvent<PickupSlot> _) => RebuildAllVisuals();

        void RebuildAllVisuals()
        {
            ClearVisuals();
            for (int i = 0; i < m_Pickups.Count; i++)
            {
                var slot = m_Pickups[i];
                if (!slot.Active) continue;
                m_Visuals[slot.Id] = CreateBoxVisual(slot.Position);
            }
        }

        void ClearVisuals()
        {
            foreach (var go in m_Visuals.Values)
                if (go != null) Destroy(go);
            m_Visuals.Clear();
        }

        GameObject CreateBoxVisual(Vector3 position)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "PickupBox";
            go.transform.position = position;
            go.transform.localScale = new Vector3(1.4f, 1.4f, 1.4f);
            Object.Destroy(go.GetComponent<Collider>());

            var renderer = go.GetComponent<MeshRenderer>();
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader != null)
            {
                var mat = new Material(shader) { color = m_BoxColor };
                renderer.sharedMaterial = mat;
            }

            return go;
        }

        void AnimateVisuals()
        {
            float bob = Mathf.Sin(Time.time * 2.5f) * 0.25f;
            float spin = Time.time * 90f;
            foreach (var pair in m_Visuals)
            {
                if (pair.Value == null) continue;
                int index = IndexOf(pair.Key);
                if (index < 0) continue;
                var slot = m_Pickups[index];
                pair.Value.transform.position = slot.Position + Vector3.up * bob;
                pair.Value.transform.rotation = Quaternion.Euler(0f, spin, 0f);
            }
        }
    }
}
