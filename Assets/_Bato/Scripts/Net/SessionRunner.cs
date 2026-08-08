using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace Bato
{
    /// <summary>
    /// Point d'entrée réseau : héberger ou rejoindre une partie.
    ///
    /// Deux chemins possibles :
    ///  - Sessions (Relay) : passe par UGS, donne un code de partie court, marche sur internet.
    ///  - Direct : NGO + UnityTransport en IP directe, aucun service cloud. Filet de sécurité
    ///    si UGS est down ou si le projet n'est pas lié au dashboard.
    ///
    /// Ne jamais appeler NetworkManager.StartHost()/StartClient() en plus des méthodes Session* :
    /// le SDK Sessions démarre le NetworkManager lui-même.
    /// </summary>
    public class SessionRunner : MonoBehaviour
    {
        public static SessionRunner Instance { get; private set; }

        [Header("Session")]
        [SerializeField] int m_MaxPlayers = 4;

        [Header("Fallback direct (sans UGS)")]
        [SerializeField] string m_DirectAddress = "127.0.0.1";
        [SerializeField] ushort m_DirectPort = 7777;

        public ISession Session { get; private set; }
        public string JoinCode => Session != null ? Session.Code : string.Empty;
        public bool IsBusy { get; private set; }

        /// <summary>Message lisible pour l'UI (statut ou erreur).</summary>
        public event Action<string> StatusChanged;
        /// <summary>Le NetworkManager tourne, la partie est lancée.</summary>
        public event Action Started;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Status(string message)
        {
            Debug.Log($"[SessionRunner] {message}");
            StatusChanged?.Invoke(message);
        }

        // ---------------------------------------------------------------- UGS

        async Task EnsureSignedInAsync()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                Status("Initialisation des services...");
                await UnityServices.InitializeAsync();
            }

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                Status("Connexion anonyme...");
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }
        }

        // ------------------------------------------------------- Relay (UGS)

        public async Task HostAsync()
        {
            if (IsBusy) return;
            IsBusy = true;
            try
            {
                await EnsureSignedInAsync();

                Status("Création de la partie...");
                var options = new SessionOptions { MaxPlayers = m_MaxPlayers }.WithRelayNetwork();
                Session = await MultiplayerService.Instance.CreateSessionAsync(options);

                Status($"Partie créée — code : {Session.Code}");
                Started?.Invoke();
            }
            catch (Exception e)
            {
                Status($"Échec de l'hébergement : {e.Message}");
                Debug.LogException(e);
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task JoinAsync(string code)
        {
            if (IsBusy) return;
            if (string.IsNullOrWhiteSpace(code))
            {
                Status("Entre un code de partie.");
                return;
            }

            IsBusy = true;
            try
            {
                await EnsureSignedInAsync();

                Status("Connexion à la partie...");
                Session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code.Trim().ToUpperInvariant());

                Status($"Connecté — code : {Session.Code}");
                Started?.Invoke();
            }
            catch (Exception e)
            {
                Status($"Impossible de rejoindre : {e.Message}");
                Debug.LogException(e);
            }
            finally
            {
                IsBusy = false;
            }
        }

        // ------------------------------------------------- Direct (sans UGS)

        /// <summary>
        /// Héberge sans passer par UGS. Écoute sur toutes les interfaces pour que le LAN puisse
        /// se connecter ; les clients utilisent l'IP locale du host (ipconfig).
        /// </summary>
        public void HostDirect()
        {
            var transport = ConfigureDirectTransport("0.0.0.0", m_DirectPort);
            if (transport == null) return;

            if (NetworkManager.Singleton.StartHost())
            {
                Status($"Hébergement direct sur le port {m_DirectPort}");
                Started?.Invoke();
            }
            else
            {
                Status("Échec du démarrage du host direct.");
            }
        }

        public void JoinDirect(string address)
        {
            if (!string.IsNullOrWhiteSpace(address)) m_DirectAddress = address.Trim();

            var transport = ConfigureDirectTransport(m_DirectAddress, m_DirectPort);
            if (transport == null) return;

            if (NetworkManager.Singleton.StartClient())
            {
                Status($"Connexion directe à {m_DirectAddress}:{m_DirectPort}...");
                Started?.Invoke();
            }
            else
            {
                Status("Échec du démarrage du client direct.");
            }
        }

        UnityTransport ConfigureDirectTransport(string listenOrConnectAddress, ushort port)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null)
            {
                Status("Pas de NetworkManager dans la scène.");
                return null;
            }

            var transport = nm.GetComponent<UnityTransport>();
            if (transport == null)
            {
                Status("Pas de UnityTransport sur le NetworkManager.");
                return null;
            }

            transport.SetConnectionData(listenOrConnectAddress, port);
            return transport;
        }

        // ------------------------------------------------------------ Sortie

        public async Task LeaveAsync()
        {
            try
            {
                if (Session != null)
                {
                    await Session.LeaveAsync();
                    Session = null;
                }
                else if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
                {
                    NetworkManager.Singleton.Shutdown();
                }
                Status("Partie quittée.");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }
}
