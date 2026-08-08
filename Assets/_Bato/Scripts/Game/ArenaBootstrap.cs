using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
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

    /// <summary>Place d'un joueur dans le salon, avant que la partie ne démarre.</summary>
    public struct LobbyPlayer : INetworkSerializable, IEquatable<LobbyPlayer>
    {
        public ulong ClientId;
        public bool Ready;
        public int Team;

        /// <summary>Pseudo choisi par le joueur. Taille fixe : une NetworkList n'accepte pas string.</summary>
        public FixedString32Bytes Name;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ClientId);
            serializer.SerializeValue(ref Ready);
            serializer.SerializeValue(ref Team);
            serializer.SerializeValue(ref Name);
        }

        public bool Equals(LobbyPlayer other) =>
            ClientId == other.ClientId && Ready == other.Ready && Team == other.Team &&
            Name.Equals(other.Name);
    }

    /// <summary>
    /// Chef d'orchestre de l'arène, côté serveur : salon d'avant-partie (équipes, prêt, mode de
    /// jeu), approbation des connexions, points de réapparition et tableau des scores répliqué.
    ///
    /// Les bateaux n'apparaissent PAS à la connexion : les joueurs restent dans le salon, et
    /// c'est <see cref="StartMatchServerRpc"/> qui les fait tous apparaître d'un coup après
    /// <see cref="m_StartDelay"/>. Une fois la partie lancée, les nouvelles connexions sont
    /// refusées.
    /// </summary>
    public class ArenaBootstrap : NetworkBehaviour
    {
        public static ArenaBootstrap Instance { get; private set; }

        /// <summary>Servé quand la partie démarre (serveur). Les systèmes d'objets s'y branchent.</summary>
        public static event Action MatchStarted;

        [SerializeField] Transform[] m_SpawnPoints;

        [Tooltip("Coché : chacun reçoit son bateau dès la connexion, sans passer par le salon. " +
                 "Décoche-le seulement une fois que la scène contient un LobbyUI (Canvas.prefab), " +
                 "sinon plus rien ne peut lancer la partie.")]
        [SerializeField] bool m_AutoStart = true;

        [Tooltip("Salon uniquement : délai entre le lancement de la partie et l'apparition des " +
                 "bateaux, en secondes.")]
        [SerializeField] float m_StartDelay = 3f;

        public readonly NetworkList<PlayerScore> Scores = new NetworkList<PlayerScore>();
        public readonly NetworkList<LobbyPlayer> Players = new NetworkList<LobbyPlayer>();

        public bool IsMatchStarted => m_MatchStarted.Value;
        public double StartTime => m_StartTime.Value;
        public int GameMode => m_GameMode.Value;
        public bool IsTeamMode => m_GameMode.Value == 1;

        /// <summary>
        /// Compteur incrémenté à chaque changement du salon. Les UI comparent leur copie à
        /// celle-ci pour ne se reconstruire que quand quelque chose a réellement bougé.
        /// </summary>
        public int LobbyRevision { get; private set; }

        readonly NetworkVariable<bool> m_MatchStarted = new NetworkVariable<bool>(false);
        readonly NetworkVariable<double> m_StartTime = new NetworkVariable<double>(0d);
        readonly NetworkVariable<int> m_GameMode = new NetworkVariable<int>(0);

        int m_NextSpawnIndex;

        // Pseudos reçus à l'approbation, en attente : ApproveConnection est appelé avant
        // OnClientConnected, et c'est le seul endroit où la charge utile du client est lisible.
        readonly Dictionary<ulong, string> m_PendingNames = new Dictionary<ulong, string>();

        void Awake()
        {
            Instance = this;

            if (GetComponent<ArenaPickupSystem>() == null)
                gameObject.AddComponent<ArenaPickupSystem>();

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
            m_MatchStarted.OnValueChanged += OnMatchStartedChanged;
            m_GameMode.OnValueChanged += OnGameModeChanged;
            Players.OnListChanged += OnPlayersChanged;

            if (!IsServer) return;

            NetworkManager.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;

            // Le host se connecte à lui-même avant que cet objet ne soit spawné : on rattrape.
            foreach (var clientId in NetworkManager.ConnectedClientsIds)
            {
                OnClientConnected(clientId);
            }

            // Sans salon, la partie est considérée lancée d'entrée : c'est ce drapeau qui décide
            // aussi de l'affichage du HUD.
            if (m_AutoStart)
            {
                m_MatchStarted.Value = true;
                m_StartTime.Value = NetworkManager.ServerTime.Time;
                MatchStarted?.Invoke();
            }
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

        // NetworkBehaviour.OnDestroy fait son propre ménage (désinscription auprès du
        // NetworkManager) : il faut l'appeler, pas le masquer.
        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }

        // ------------------------------------------------------ Connexions

        void ApproveConnection(NetworkManager.ConnectionApprovalRequest request,
                               NetworkManager.ConnectionApprovalResponse response)
        {
            // Le pseudo voyage dans la charge utile : c'est ici, et seulement ici, qu'on peut la
            // lire. On la met de côté pour OnClientConnected, qui suivra.
            m_PendingNames[request.ClientNetworkId] = PlayerIdentity.FromPayload(request.Payload);

            // Sans salon : NGO fait apparaître le bateau tout de suite, à la place qu'on lui
            // indique ici. On n'interdit jamais l'entrée, sinon plus personne ne peut rejoindre.
            if (m_AutoStart)
            {
                var (spawnPosition, spawnRotation) = GetNextSpawn();

                response.Approved = true;
                response.CreatePlayerObject = true;
                response.Position = spawnPosition;
                response.Rotation = spawnRotation;
                return;
            }

            if (m_MatchStarted.Value)
            {
                response.Approved = false;
                response.Reason = "La partie a déjà commencé.";
                return;
            }

            // Pas de bateau à la connexion : le joueur atterrit dans le salon. C'est
            // SpawnPlayersAfterDelay qui instancie tout le monde au lancement.
            response.Approved = true;
            response.CreatePlayerObject = false;
        }

        void OnClientConnected(ulong clientId)
        {
            if (IndexOf(clientId) < 0)
            {
                Players.Add(new LobbyPlayer
                {
                    ClientId = clientId,
                    Ready = false,
                    Team = 1,
                    Name = ResolveName(clientId),
                });
            }

            if (ScoreIndexOf(clientId) < 0)
            {
                Scores.Add(new PlayerScore { ClientId = clientId, Kills = 0, Deaths = 0 });
            }

            LobbyRevision++;
        }

        /// <summary>
        /// Pseudo mis de côté à l'approbation. Repli sur un nom générique : un client qui se
        /// connecte sans charge utile ne doit pas apparaître sans nom dans la liste.
        /// </summary>
        FixedString32Bytes ResolveName(ulong clientId)
        {
            if (m_PendingNames.TryGetValue(clientId, out var pending))
            {
                m_PendingNames.Remove(clientId);
                if (!string.IsNullOrEmpty(pending)) return new FixedString32Bytes(pending);
            }

            return new FixedString32Bytes($"Joueur {clientId}");
        }

        void OnClientDisconnected(ulong clientId)
        {
            int playerIndex = IndexOf(clientId);
            if (playerIndex >= 0) Players.RemoveAt(playerIndex);

            int scoreIndex = ScoreIndexOf(clientId);
            if (scoreIndex >= 0) Scores.RemoveAt(scoreIndex);

            m_PendingNames.Remove(clientId);
            LobbyRevision++;
        }

        /// <summary>Pseudo d'un joueur, utilisable par toutes les UI. Jamais vide.</summary>
        public string GetName(ulong clientId)
        {
            int index = IndexOf(clientId);
            return index >= 0 ? Players[index].Name.ToString() : $"Joueur {clientId}";
        }

        void OnPlayersChanged(NetworkListEvent<LobbyPlayer> _) => LobbyRevision++;
        void OnMatchStartedChanged(bool _, bool started) => LobbyRevision++;
        void OnGameModeChanged(int _, int mode) => LobbyRevision++;

        // ----------------------------------------------------------- Salon

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

        /// <summary>Hôte uniquement. Changer de mode remet tout le monde « pas prêt ».</summary>
        [Rpc(SendTo.Server)]
        public void SetGameModeServerRpc(int mode, RpcParams rpcParams = default)
        {
            if (!IsServer || m_MatchStarted.Value) return;
            if (rpcParams.Receive.SenderClientId != NetworkManager.ServerClientId) return;

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

            int index = IndexOf(rpcParams.Receive.SenderClientId);
            if (index < 0) return;

            var player = Players[index];
            player.Team = team == 2 ? 2 : 1;
            player.Ready = false;      // changer d'équipe invalide le « prêt »
            Players[index] = player;
        }

        [Rpc(SendTo.Server)]
        public void SetReadyServerRpc(bool ready, RpcParams rpcParams = default)
        {
            if (m_MatchStarted.Value) return;

            int index = IndexOf(rpcParams.Receive.SenderClientId);
            if (index < 0) return;

            var player = Players[index];
            player.Ready = ready;
            Players[index] = player;
        }

        /// <summary>Hôte uniquement, et seulement si tout le monde est prêt.</summary>
        [Rpc(SendTo.Server)]
        public void StartMatchServerRpc(RpcParams rpcParams = default)
        {
            if (!IsServer || m_MatchStarted.Value || Players.Count == 0) return;
            if (rpcParams.Receive.SenderClientId != NetworkManager.ServerClientId) return;
            if (IsTeamMode && (Players.Count < 2 || !HasPlayersInBothTeams())) return;

            for (int i = 0; i < Players.Count; i++)
            {
                if (!Players[i].Ready) return;
            }

            m_MatchStarted.Value = true;
            m_StartTime.Value = NetworkManager.ServerTime.Time + m_StartDelay;
            MatchStarted?.Invoke();
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
                if (!NetworkManager.ConnectedClients.TryGetValue(clientId, out var client)) continue;
                if (client.PlayerObject != null) continue;

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
            for (int i = 0; i < Players.Count; i++)
            {
                if (Players[i].ClientId == clientId) return i;
            }
            return -1;
        }

        int ScoreIndexOf(ulong clientId)
        {
            for (int i = 0; i < Scores.Count; i++)
            {
                if (Scores[i].ClientId == clientId) return i;
            }
            return -1;
        }
    }
}
