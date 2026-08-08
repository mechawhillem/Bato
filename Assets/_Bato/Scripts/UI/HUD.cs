using System.Text;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace Bato
{
    /// <summary>
    /// HUD en jeu : PV du bateau local, tableau des scores, et le code de partie affiché en
    /// permanence pour qu'on puisse inviter quelqu'un en cours de match.
    /// </summary>
    public class HUD : MonoBehaviour
    {
        [SerializeField] GameObject m_Root;
        [SerializeField] Text m_HealthLabel;
        [SerializeField] Image m_HealthFill;
        [SerializeField] Text m_ScoreboardLabel;
        [SerializeField] Text m_JoinCodeLabel;

        BoatHealth m_LocalHealth;
        readonly StringBuilder m_Builder = new StringBuilder();

        void Update()
        {
            var nm = NetworkManager.Singleton;
            bool inGame = nm != null && nm.IsListening;

            if (m_Root && m_Root.activeSelf != inGame) m_Root.SetActive(inGame);
            if (!inGame) return;

            RefreshHealth(nm);
            RefreshScoreboard();
            RefreshJoinCode();
        }

        void RefreshHealth(NetworkManager nm)
        {
            if (m_LocalHealth == null)
            {
                var playerObject = nm.LocalClient?.PlayerObject;
                if (playerObject != null) m_LocalHealth = playerObject.GetComponent<BoatHealth>();
                if (m_LocalHealth == null) return;
            }

            int current = m_LocalHealth.Health;
            int max = Mathf.Max(1, m_LocalHealth.MaxHealth);

            if (m_HealthLabel) m_HealthLabel.text = m_LocalHealth.IsAlive ? $"{current} / {max}" : "COULÉ";
            if (m_HealthFill) m_HealthFill.fillAmount = current / (float)max;
        }

        void RefreshScoreboard()
        {
            if (m_ScoreboardLabel == null) return;

            var arena = ArenaBootstrap.Instance;
            if (arena == null || !arena.IsSpawned) return;

            m_Builder.Clear();
            m_Builder.AppendLine("SCORES");

            ulong localId = NetworkManager.Singleton.LocalClientId;
            for (int i = 0; i < arena.Scores.Count; i++)
            {
                var entry = arena.Scores[i];
                string marker = entry.ClientId == localId ? "> " : "  ";
                m_Builder.AppendLine($"{marker}Joueur {entry.ClientId}   {entry.Kills} / {entry.Deaths}");
            }

            m_ScoreboardLabel.text = m_Builder.ToString();
        }

        void RefreshJoinCode()
        {
            if (m_JoinCodeLabel == null) return;

            string code = SessionRunner.Instance != null ? SessionRunner.Instance.JoinCode : string.Empty;
            m_JoinCodeLabel.text = string.IsNullOrEmpty(code) ? string.Empty : $"Code : {code}";
        }
    }
}
