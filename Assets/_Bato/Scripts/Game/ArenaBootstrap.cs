using System;
using Unity.Netcode;
using UnityEngine;

namespace Bato
{
    /// <summary>Score d'un joueur, répliqué à tout le monde.</summary>
    public struct PlayerScore : INetworkSerializable, IEquatable<PlayerScore>
    {
        public ulong ClientId;
        public int Kills;
        public int Deaths;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ClientId);
            serializer.SerializeValue(ref Kills);
            serializer.SerializeValue(ref Deaths);
        }

        public bool Equals(PlayerScore other) =>
            ClientId == other.ClientId && Kills == other.Kills && Deaths == other.Deaths;
    }

    /// <summary>
    /// Chef d'orchestre de l'arène, côté serveur : approbation des connexions (qui décide aussi
    /// du point de spawn), points de réapparition, et tableau des scores répliqué.
    /// </summary>
    public class ArenaBootstrap : NetworkBehaviour
    {
        public static ArenaBootstrap Instance { get; private set; }

        [SerializeField] Transform[] m_SpawnPoints;

        public readonly NetworkList<PlayerScore> Scores = new NetworkList<PlayerScore>();

        int m_NextSpawnIndex;

        void Awake()
        {
            Instance = this;

            // Callback d'approbation AVANT tout StartHost (plus sûr que Start).
            var nm = NetworkManager.Singleton;
            if (nm != null)
            {
                nm.NetworkConfig.ConnectionApproval = true;
                nm.ConnectionApprovalCallback = ApproveConnection;
            }
        }

        void Start()
        {
            // Filet si le NetworkManager n'était pas encore prêt dans Awake.
            var nm = NetworkManager.Singleton;
            if (nm == null)
            {
                Debug.LogError("[ArenaBootstrap] Pas de NetworkManager dans la scène.");
                return;
            }

            nm.NetworkConfig.ConnectionApproval = true;
            nm.ConnectionApprovalCallback = ApproveConnection;
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;
            NetworkManager.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;

            // Le host se connecte à lui-même avant que cet objet ne soit spawné : on rattrape.
            foreach (var clientId in NetworkManager.ConnectedClientsIds)
            {
                OnClientConnected(clientId);
            }
        }

        public override void OnNetworkDespawn()
        {
            if (NetworkManager == null) return;
            NetworkManager.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ------------------------------------------------------ Connexions

        void ApproveConnection(NetworkManager.ConnectionApprovalRequest request,
                               NetworkManager.ConnectionApprovalResponse response)
        {
            var (position, rotation) = GetNextSpawn();

            response.Approved = true;
            response.CreatePlayerObject = true;
            response.Position = position;
            response.Rotation = rotation;
        }

        void OnClientConnected(ulong clientId)
        {
            if (IndexOf(clientId) < 0)
            {
                Scores.Add(new PlayerScore { ClientId = clientId, Kills = 0, Deaths = 0 });
            }
        }

        void OnClientDisconnected(ulong clientId)
        {
            int index = IndexOf(clientId);
            if (index >= 0) Scores.RemoveAt(index);
        }

        // ---------------------------------------------------------- Spawns

        public (Vector3, Quaternion) GetNextSpawn()
        {
            if (m_SpawnPoints == null || m_SpawnPoints.Length == 0)
                return (Vector3.zero, Quaternion.identity);

            var point = m_SpawnPoints[m_NextSpawnIndex % m_SpawnPoints.Length];
            m_NextSpawnIndex++;
            return (point.position, point.rotation);
        }

        public (Vector3, Quaternion) GetRandomSpawn()
        {
            if (m_SpawnPoints == null || m_SpawnPoints.Length == 0)
                return (Vector3.zero, Quaternion.identity);

            var point = m_SpawnPoints[UnityEngine.Random.Range(0, m_SpawnPoints.Length)];
            return (point.position, point.rotation);
        }

        // ----------------------------------------------------------- Score

        /// <summary>Serveur uniquement. Appelé par BoatHealth quand un bateau coule.</summary>
        public void ReportKill(ulong killerClientId, ulong victimClientId)
        {
            if (!IsServer) return;

            if (killerClientId != victimClientId)
            {
                int killerIndex = IndexOf(killerClientId);
                if (killerIndex >= 0)
                {
                    var entry = Scores[killerIndex];
                    entry.Kills++;
                    Scores[killerIndex] = entry;
                }
            }

            int victimIndex = IndexOf(victimClientId);
            if (victimIndex >= 0)
            {
                var entry = Scores[victimIndex];
                entry.Deaths++;
                Scores[victimIndex] = entry;
            }
        }

        int IndexOf(ulong clientId)
        {
            for (int i = 0; i < Scores.Count; i++)
            {
                if (Scores[i].ClientId == clientId) return i;
            }
            return -1;
        }
    }
}
