using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace Bato
{
    public class LobbyUI : MonoBehaviour
    {
        [SerializeField] GameObject m_Panel;
        [SerializeField] Text m_PlayerListLabel;
        [SerializeField] Text m_StatusLabel;
        [SerializeField] Button m_ReadyButton;
        [SerializeField] Text m_ReadyButtonLabel;
        [SerializeField] Button m_StartButton;
        [SerializeField] Dropdown m_ModeDropdown;
        [SerializeField] Dropdown m_TeamDropdown;
        [SerializeField] Text m_ModeLabel;
        [SerializeField] Text m_TeamLabel;

        [SerializeField] Text m_JoinCodeLabel;
        
        readonly StringBuilder m_Builder = new StringBuilder();
        bool m_IsReady;
        bool m_WasMatchStarted;
        bool m_Refreshing;
        int m_LastLobbyRevision = -1;

        void Start()
        {
            ResolveReferences();
            if (m_ReadyButton) m_ReadyButton.onClick.AddListener(ToggleReady);
            if (m_StartButton) m_StartButton.onClick.AddListener(StartMatch);
            if (m_ModeDropdown) m_ModeDropdown.onValueChanged.AddListener(OnModeChanged);
            if (m_TeamDropdown) m_TeamDropdown.onValueChanged.AddListener(OnTeamChanged);
            ConfigureDropdowns();
            Refresh();
        }

        void OnDestroy()
        {
            if (m_ReadyButton) m_ReadyButton.onClick.RemoveListener(ToggleReady);
            if (m_StartButton) m_StartButton.onClick.RemoveListener(StartMatch);
            if (m_ModeDropdown) m_ModeDropdown.onValueChanged.RemoveListener(OnModeChanged);
            if (m_TeamDropdown) m_TeamDropdown.onValueChanged.RemoveListener(OnTeamChanged);
        }

        void Update()
        {
            var arena = ArenaBootstrap.Instance;
            if (arena == null || !arena.IsSpawned)
            {
                if (m_Panel) m_Panel.SetActive(false);
                return;
            }

            bool started = arena.IsMatchStarted;
            if (started != m_WasMatchStarted || arena.LobbyRevision != m_LastLobbyRevision)
            {
                m_WasMatchStarted = started;
                m_LastLobbyRevision = arena.LobbyRevision;
                Refresh();
            }

            if (started)
            {
                double remaining = arena.StartTime - NetworkManager.Singleton.ServerTime.Time;
                if (remaining > 0.0)
                {
                    if (m_StatusLabel) m_StatusLabel.text = $"La partie commence dans {Mathf.CeilToInt((float)remaining)}...";
                }
                else if (m_Panel) m_Panel.SetActive(false);
            }
            
            RefreshJoinCode();
        }

        void ResolveReferences()
        {
            if (!m_Panel) m_Panel = FindChild("LobbyPanel");
            if (!m_PlayerListLabel) m_PlayerListLabel = FindChild("PlayerList")?.GetComponent<Text>();
            if (!m_StatusLabel) m_StatusLabel = FindChild("LobbyStatus")?.GetComponent<Text>();
            if (!m_ReadyButton) m_ReadyButton = FindChild("ReadyButton")?.GetComponent<Button>();
            if (!m_ReadyButtonLabel) m_ReadyButtonLabel = FindChild("ReadyButtonLabel")?.GetComponent<Text>();
            if (!m_StartButton) m_StartButton = FindChild("StartButton")?.GetComponent<Button>();
            if (!m_ModeDropdown) m_ModeDropdown = FindChild("ModeDropdown")?.GetComponent<Dropdown>();
            if (!m_TeamDropdown) m_TeamDropdown = FindChild("TeamDropdown")?.GetComponent<Dropdown>();
            if (!m_ModeLabel) m_ModeLabel = FindChild("ModeLabel")?.GetComponent<Text>();
            if (!m_TeamLabel) m_TeamLabel = FindChild("TeamLabel")?.GetComponent<Text>();
        }

        GameObject FindChild(string childName)
        {
            var root = GetComponentInChildren<Transform>(true);
            if (!root) return null;
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
                if (child.name == childName) return child.gameObject;
            return null;
        }

        void ConfigureDropdowns()
        {
            if (m_ModeDropdown)
            {
                m_ModeDropdown.ClearOptions();
                m_ModeDropdown.AddOptions(new List<string> { "Chacun pour soi", "Équipes : 2 équipes" });
            }
            if (m_TeamDropdown)
            {
                m_TeamDropdown.ClearOptions();
                m_TeamDropdown.AddOptions(new List<string> { "Équipe 1", "Équipe 2" });
            }
        }

        void Refresh()
        {
            var arena = ArenaBootstrap.Instance;
            bool connected = arena != null && arena.IsSpawned;
            bool inLobby = connected && !arena.IsMatchStarted;
            if (m_Panel) m_Panel.SetActive(inLobby);
            if (!inLobby) return;

            bool isHost = NetworkManager.Singleton.IsHost;
            bool teamMode = arena.IsTeamMode;
            m_IsReady = arena.IsReady(NetworkManager.Singleton.LocalClientId);
            if (m_ReadyButtonLabel) m_ReadyButtonLabel.text = m_IsReady ? "Annuler" : "Prêt";
            if (m_StartButton) m_StartButton.gameObject.SetActive(isHost);
            if (m_ModeDropdown)
            {
                m_ModeDropdown.SetValueWithoutNotify(arena.GameMode);
                m_ModeDropdown.interactable = isHost;
            }
            if (m_ModeLabel) m_ModeLabel.text = isHost ? "Mode de partie" : "Mode choisi par l'host";

            if (m_TeamDropdown)
            {
                m_TeamDropdown.gameObject.SetActive(teamMode);
                m_TeamDropdown.interactable = teamMode;
                m_Refreshing = true;
                m_TeamDropdown.SetValueWithoutNotify(arena.GetTeam(NetworkManager.Singleton.LocalClientId) == 2 ? 1 : 0);
                m_Refreshing = false;
            }
            if (m_TeamLabel) m_TeamLabel.gameObject.SetActive(teamMode);

            bool allPlayersReady = arena.Players.Count > 0;
            for (int i = 0; i < arena.Players.Count; i++) allPlayersReady &= arena.Players[i].Ready;
            if (m_StatusLabel) m_StatusLabel.text = allPlayersReady ? "Tout le monde est prêt." : "Tout le monde n'est pas prêt.";

            if (m_PlayerListLabel)
            {
                m_Builder.Clear();
                m_Builder.AppendLine(teamMode ? "JOUEURS / ÉQUIPES" : "JOUEURS");
                for (int i = 0; i < arena.Players.Count; i++)
                {
                    var player = arena.Players[i];
                    string team = teamMode ? $" — Équipe {player.Team}" : string.Empty;
                    m_Builder.AppendLine($"{(player.Ready ? "[OK]" : "[ ]")} {player.Name}{team}");
                }
                m_PlayerListLabel.text = m_Builder.ToString();
            }
        }

        void ToggleReady()
        {
            var arena = ArenaBootstrap.Instance;
            if (arena != null && arena.IsSpawned) arena.SetReadyServerRpc(!m_IsReady);
        }

        void StartMatch()
        {
            var arena = ArenaBootstrap.Instance;
            if (arena != null && arena.IsSpawned) arena.StartMatchServerRpc();
        }

        void OnModeChanged(int mode)
        {
            if (m_Refreshing || !NetworkManager.Singleton.IsHost) return;
            ArenaBootstrap.Instance?.SetGameModeServerRpc(mode);
        }

        void OnTeamChanged(int teamIndex)
        {
            if (m_Refreshing || !NetworkManager.Singleton.IsClient || ArenaBootstrap.Instance == null || !ArenaBootstrap.Instance.IsTeamMode) return;
            ArenaBootstrap.Instance.SetTeamServerRpc(teamIndex + 1);
        }
        
        void RefreshJoinCode()
        {
            if (m_JoinCodeLabel == null) return;

            string code = SessionRunner.Instance != null ? SessionRunner.Instance.JoinCode : string.Empty;
            m_JoinCodeLabel.text = string.IsNullOrEmpty(code) ? string.Empty : $"Code : {code}";
        }
    }
}
