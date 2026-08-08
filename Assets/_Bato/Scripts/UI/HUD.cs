using System.Text;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace Bato
{
    /// <summary>
    /// HUD en jeu : PV, scores, code de partie, et jauge de charge des canons.
    /// </summary>
    public class HUD : MonoBehaviour
    {
        [SerializeField] GameObject m_Root;
        [SerializeField] Text m_HealthLabel;
        [SerializeField] Image m_HealthFill;
        [SerializeField] Text m_ScoreboardLabel;
        [SerializeField] Text m_JoinCodeLabel;
        [SerializeField] GameObject m_PowerRoot;
        [SerializeField] Image m_PowerFill;
        [SerializeField] Text m_PowerLabel;

        BoatHealth m_LocalHealth;
        BoatCannon m_LocalCannon;
        readonly StringBuilder m_Builder = new StringBuilder();

        void Awake() => EnsurePowerGauge();

        void Update()
        {
            var nm = NetworkManager.Singleton;
            var arena = ArenaBootstrap.Instance;
            bool inGame = nm != null && nm.IsListening && arena != null && arena.IsMatchStarted;

            if (m_Root && m_Root.activeSelf != inGame) m_Root.SetActive(inGame);
            if (!inGame) return;

            RefreshHealth(nm);
            RefreshPower(nm);
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

        void RefreshPower(NetworkManager nm)
        {
            if (m_LocalCannon == null)
            {
                var playerObject = nm.LocalClient?.PlayerObject;
                if (playerObject != null) m_LocalCannon = playerObject.GetComponent<BoatCannon>();
            }

            float power = m_LocalCannon != null ? m_LocalCannon.ChargePower : 0f;
            bool charging = power > 0.001f;

            if (m_PowerRoot && m_PowerRoot.activeSelf != charging)
                m_PowerRoot.SetActive(charging);

            if (m_PowerFill) m_PowerFill.fillAmount = power;
            if (m_PowerLabel) m_PowerLabel.text = charging ? $"PUISSANCE  {Mathf.RoundToInt(power * 100f)}%" : string.Empty;
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

        /// <summary>Crée la jauge si la scène n'a pas encore les refs (Arena existante).</summary>
        void EnsurePowerGauge()
        {
            if (m_PowerFill != null || m_Root == null) return;

            var powerRoot = new GameObject("PowerGauge", typeof(RectTransform));
            powerRoot.transform.SetParent(m_Root.transform, false);
            var rootRect = powerRoot.GetComponent<RectTransform>();
            rootRect.anchorMin = rootRect.anchorMax = new Vector2(0.5f, 0f);
            rootRect.sizeDelta = new Vector2(360f, 70f);
            rootRect.anchoredPosition = new Vector2(0f, 110f);

            var labelGo = new GameObject("PowerLabel", typeof(RectTransform));
            labelGo.transform.SetParent(powerRoot.transform, false);
            var labelRect = labelGo.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0f, 0.55f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            var label = labelGo.AddComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (label.font == null) label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            label.fontSize = 22;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
            label.raycastTarget = false;

            var barBack = new GameObject("PowerBarBack", typeof(RectTransform));
            barBack.transform.SetParent(powerRoot.transform, false);
            var backRect = barBack.GetComponent<RectTransform>();
            backRect.anchorMin = new Vector2(0f, 0f);
            backRect.anchorMax = new Vector2(1f, 0.45f);
            backRect.offsetMin = Vector2.zero;
            backRect.offsetMax = Vector2.zero;
            barBack.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

            var barFill = new GameObject("PowerBarFill", typeof(RectTransform));
            barFill.transform.SetParent(barBack.transform, false);
            var fillRect = barFill.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            var fill = barFill.AddComponent<Image>();
            fill.color = new Color(0.95f, 0.7f, 0.15f);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillAmount = 0f;
            fill.raycastTarget = false;

            m_PowerRoot = powerRoot;
            m_PowerFill = fill;
            m_PowerLabel = label;
            m_PowerRoot.SetActive(false);
        }
    }
}
