using TMPro;
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

        [Tooltip("Facultatif. Non renseigné, le joueur garde son pseudo précédent ou en reçoit un " +
                 "au hasard : le multijoueur reste utilisable sans ce champ.")]
        [SerializeField] TMP_InputField m_NameField;

        [Header("Menu principal")]
        [Tooltip("Contrôleur appelé automatiquement quand l'hébergement ou la connexion est réussi.")]
        [SerializeField] MainMenuController m_MainMenuController;


        SessionRunner Runner => SessionRunner.Instance;

        void Start()
        {
            // Le champ part rempli du pseudo retenu, et le mémorise à chaque frappe : la valeur
            // est donc déjà à jour quand on clique sur Héberger ou Rejoindre.
            if (m_NameField)
            {
                m_NameField.characterLimit = PlayerIdentity.MaxBytes;
                m_NameField.text = PlayerIdentity.Name;
                m_NameField.onEndEdit.AddListener(value => PlayerIdentity.Name = value);
            }

            if (m_HostButton) m_HostButton.onClick.AddListener(() => _ = Runner.HostAsync());
            if (m_JoinButton) m_JoinButton.onClick.AddListener(() => _ = Runner.JoinAsync(m_CodeField ? m_CodeField.text : ""));
            if (m_DirectHostButton) m_DirectHostButton.onClick.AddListener(() => Runner.HostDirect());
            if (m_DirectJoinButton) m_DirectJoinButton.onClick.AddListener(() => Runner.JoinDirect(m_CodeField ? m_CodeField.text : ""));

            if (Runner != null)
            {
                Runner.StatusChanged += OnStatus;
                Runner.Started += OnNetworkStarted;
            }

            SetStatus("Héberge une partie, ou entre un code pour rejoindre.");
        }

        void OnDestroy()
        {
            if (Runner == null) return;
            Runner.StatusChanged -= OnStatus;
            Runner.Started -= OnNetworkStarted;
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

        void OnNetworkStarted()
        {
            Hide();
            if (m_MainMenuController != null) m_MainMenuController.PlayGame();
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
