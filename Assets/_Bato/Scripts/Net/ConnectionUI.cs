using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace Bato
{
    /// <summary>
    /// Panneau de connexion : héberger / rejoindre par code, plus un mode direct de secours.
    /// Se masque automatiquement une fois la partie lancée.
    /// </summary>
    public class ConnectionUI : MonoBehaviour
    {
        [SerializeField] GameObject m_Panel;
        [SerializeField] Button m_HostButton;
        [SerializeField] Button m_JoinButton;
        [SerializeField] InputField m_CodeField;
        [SerializeField] Button m_DirectHostButton;
        [SerializeField] Button m_DirectJoinButton;
        [SerializeField] Text m_StatusLabel;

        SessionRunner Runner => SessionRunner.Instance;

        void Start()
        {
            if (m_HostButton) m_HostButton.onClick.AddListener(() => _ = Runner.HostAsync());
            if (m_JoinButton) m_JoinButton.onClick.AddListener(() => _ = Runner.JoinAsync(m_CodeField ? m_CodeField.text : ""));
            if (m_DirectHostButton) m_DirectHostButton.onClick.AddListener(() => Runner.HostDirect());
            if (m_DirectJoinButton) m_DirectJoinButton.onClick.AddListener(() => Runner.JoinDirect(m_CodeField ? m_CodeField.text : ""));

            if (Runner != null)
            {
                Runner.StatusChanged += OnStatus;
                Runner.Started += Hide;
            }

            SetStatus("Héberge une partie, ou entre un code pour rejoindre.");
        }

        void OnDestroy()
        {
            if (Runner == null) return;
            Runner.StatusChanged -= OnStatus;
            Runner.Started -= Hide;
        }

        void Update()
        {
            // Le panneau réapparaît si on se fait déconnecter.
            if (m_Panel != null && !m_Panel.activeSelf &&
                NetworkManager.Singleton != null && !NetworkManager.Singleton.IsListening)
            {
                m_Panel.SetActive(true);
            }
        }

        void OnStatus(string message) => SetStatus(message);

        void SetStatus(string message)
        {
            if (m_StatusLabel) m_StatusLabel.text = message;
        }

        void Hide()
        {
            if (m_Panel) m_Panel.SetActive(false);
        }
    }
}
