using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Bato
{
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

        public bool Equals(PlayerScore other) => ClientId == other.ClientId && Kills == other.Kills && Deaths == other.Deaths;
    }

    public struct LobbyPlayer : INetworkSerializable, IEquatable<LobbyPlayer>
    {
        public ulong ClientId;
        public bool Ready;
        public int Team;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ClientId);
            serializer.SerializeValue(ref Ready);
            serializer.SerializeValue(ref Team);
        }

        public bool Equals(LobbyPlayer other) => ClientId == other.ClientId && Ready == other.Ready && Team == other.Team;
    }

    public class ArenaBootstrap : NetworkBehaviour
    {
        public static ArenaBootstrap Instance { get; private set; }

        [SerializeField] Transform[] m_SpawnPoints;
        [SerializeField] float m_StartDelay = 3f;

        public readonly NetworkList<PlayerScore> Scores = new NetworkList<PlayerScore>();
        public readonly NetworkList<LobbyPlayer> Players = new NetworkList<LobbyPlayer>();
        public bool IsMatchStarted => m_MatchStarted.Value;
        public double StartTime => m_StartTime.Value;
        public int LobbyRevision { get; private set; }
        public int GameMode => m_GameMode.Value;
        public bool IsTeamMode => m_GameMode.Value == 1;

        readonly NetworkVariable<bool> m_MatchStarted = new NetworkVariable<bool>(false);
        readonly NetworkVariable<double> m_StartTime = new NetworkVariable<double>(0d);
        readonly NetworkVariable<int> m_GameMode = new NetworkVariable<int>(0);
        int m_NextSpawnIndex;

        void Awake() => Instance = this;

        void Start()
        {
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
            m_MatchStarted.OnValueChanged += OnMatchStartedChanged;
            m_GameMode.OnValueChanged += OnGameModeChanged;
            Players.OnListChanged += OnPlayersChanged;
            if (!IsServer) return;
            NetworkManager.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
            foreach (var clientId in NetworkManager.ConnectedClientsIds) OnClientConnected(clientId);
        }

        public override void OnNetworkDespawn()
        {
            m_MatchStarted.OnValueChanged -= OnMatchStartedChanged;
            m_GameMode.OnValueChanged -= OnGameModeChanged;
            Players.OnListChanged -= OnPlayersChanged;
            if (NetworkManager == null) return;
            NetworkManager.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
        }

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }

        void ApproveConnection(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            if (m_MatchStarted.Value)
            {
                response.Approved = false;
                response.Reason = "La partie a déjà commencé.";
                return;
            }

            response.Approved = true;
            response.CreatePlayerObject = false;
        }

        void OnClientConnected(ulong clientId)
        {
            if (IndexOf(clientId) < 0) Players.Add(new LobbyPlayer { ClientId = clientId, Ready = false, Team = 1 });
            if (ScoreIndexOf(clientId) < 0) Scores.Add(new PlayerScore { ClientId = clientId, Kills = 0, Deaths = 0 });
            LobbyRevision++;
        }

        void OnClientDisconnected(ulong clientId)
        {
            int playerIndex = IndexOf(clientId);
            if (playerIndex >= 0) Players.RemoveAt(playerIndex);
            int scoreIndex = ScoreIndexOf(clientId);
            if (scoreIndex >= 0) Scores.RemoveAt(scoreIndex);
            LobbyRevision++;
        }

        void OnPlayersChanged(NetworkListEvent<LobbyPlayer> _) => LobbyRevision++;
        void OnMatchStartedChanged(bool _, bool started) => LobbyRevision++;
        void OnGameModeChanged(int _, int mode) => LobbyRevision++;

        public bool IsReady(ulong clientId)
        {
            int index = IndexOf(clientId);
            return index >= 0 && Players[index].Ready;
        }

        public int GetTeam(ulong clientId)
        {
            int index = IndexOf(clientId);
            return index >= 0 ? Players[index].Team : 1;
        }

        [Rpc(SendTo.Server)]
        public void SetGameModeServerRpc(int mode, RpcParams rpcParams = default)
        {
            if (!IsServer || m_MatchStarted.Value || rpcParams.Receive.SenderClientId != NetworkManager.ServerClientId) return;
            m_GameMode.Value = mode == 1 ? 1 : 0;
            for (int i = 0; i < Players.Count; i++)
            {
                var player = Players[i];
                player.Ready = false;
                if (m_GameMode.Value == 0) player.Team = 0;
                else if (player.Team < 1 || player.Team > 2) player.Team = i % 2 == 0 ? 1 : 2;
                Players[i] = player;
            }
        }

        [Rpc(SendTo.Server)]
        public void SetTeamServerRpc(int team, RpcParams rpcParams = default)
        {
            if (!IsServer || m_MatchStarted.Value || !IsTeamMode) return;
            ulong clientId = rpcParams.Receive.SenderClientId;
            int index = IndexOf(clientId);
            if (index < 0) return;
            var player = Players[index];
            player.Team = team == 2 ? 2 : 1;
            player.Ready = false;
            Players[index] = player;
        }

        [Rpc(SendTo.Server)]
        public void SetReadyServerRpc(bool ready, RpcParams rpcParams = default)
        {
            if (m_MatchStarted.Value) return;
            ulong clientId = rpcParams.Receive.SenderClientId;
            int index = IndexOf(clientId);
            if (index < 0) return;
            var player = Players[index];
            player.Ready = ready;
            Players[index] = player;
        }

        [Rpc(SendTo.Server)]
        public void StartMatchServerRpc(RpcParams rpcParams = default)
        {
            if (!IsServer || m_MatchStarted.Value || rpcParams.Receive.SenderClientId != NetworkManager.ServerClientId || Players.Count == 0) return;
            if (IsTeamMode && (Players.Count < 2 || !HasPlayersInBothTeams())) return;
            for (int i = 0; i < Players.Count; i++) if (!Players[i].Ready) return;

            m_MatchStarted.Value = true;
            m_StartTime.Value = NetworkManager.ServerTime.Time + m_StartDelay;
            StartCoroutine(SpawnPlayersAfterDelay());
        }

        bool HasPlayersInBothTeams()
        {
            bool teamOne = false;
            bool teamTwo = false;
            for (int i = 0; i < Players.Count; i++)
            {
                teamOne |= Players[i].Team == 1;
                teamTwo |= Players[i].Team == 2;
            }
            return teamOne && teamTwo;
        }

        IEnumerator SpawnPlayersAfterDelay()
        {
            double remaining = m_StartTime.Value - NetworkManager.ServerTime.Time;
            if (remaining > 0d) yield return new WaitForSeconds((float)remaining);
            if (!IsServer) yield break;

            m_NextSpawnIndex = 0;
            var prefab = NetworkManager.NetworkConfig.PlayerPrefab;
            if (prefab == null)
            {
                Debug.LogError("[ArenaBootstrap] Aucun PlayerPrefab configuré sur NetworkManager.");
                yield break;
            }

            for (int i = 0; i < Players.Count; i++)
            {
                ulong clientId = Players[i].ClientId;
                if (!NetworkManager.ConnectedClients.TryGetValue(clientId, out var client) || client.PlayerObject != null) continue;
                var (position, rotation) = GetNextSpawn();
                var player = Instantiate(prefab, position, rotation);
                var networkObject = player.GetComponent<NetworkObject>();
                if (networkObject == null)
                {
                    Destroy(player);
                    Debug.LogError("[ArenaBootstrap] Le PlayerPrefab n'a pas de NetworkObject.");
                    continue;
                }
                networkObject.SpawnAsPlayerObject(clientId);
            }
        }

        public (Vector3, Quaternion) GetNextSpawn()
        {
            if (m_SpawnPoints == null || m_SpawnPoints.Length == 0) return (Vector3.zero, Quaternion.identity);
            var point = m_SpawnPoints[m_NextSpawnIndex % m_SpawnPoints.Length];
            m_NextSpawnIndex++;
            return (point.position, point.rotation);
        }

        public (Vector3, Quaternion) GetRandomSpawn()
        {
            if (m_SpawnPoints == null || m_SpawnPoints.Length == 0) return (Vector3.zero, Quaternion.identity);
            var point = m_SpawnPoints[UnityEngine.Random.Range(0, m_SpawnPoints.Length)];
            return (point.position, point.rotation);
        }

        public void ReportKill(ulong killerClientId, ulong victimClientId)
        {
            if (!IsServer) return;
            if (killerClientId != victimClientId)
            {
                int killerIndex = ScoreIndexOf(killerClientId);
                if (killerIndex >= 0)
                {
                    var entry = Scores[killerIndex];
                    entry.Kills++;
                    Scores[killerIndex] = entry;
                }
            }
            int victimIndex = ScoreIndexOf(victimClientId);
            if (victimIndex >= 0)
            {
                var entry = Scores[victimIndex];
                entry.Deaths++;
                Scores[victimIndex] = entry;
            }
        }

        int IndexOf(ulong clientId)
        {
            for (int i = 0; i < Players.Count; i++) if (Players[i].ClientId == clientId) return i;
            return -1;
        }

        int ScoreIndexOf(ulong clientId)
        {
            for (int i = 0; i < Scores.Count; i++) if (Scores[i].ClientId == clientId) return i;
            return -1;
        }
    }
}
