using System;
using System.Collections;
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

        static readonly ushort[] k_DirectPortCandidates = { 9787, 9788, 9789, 7777, 7778 };

        [Header("Session")]
        [SerializeField] int m_MaxPlayers = 4;

        [Header("Fallback direct (sans UGS)")]
        [SerializeField] string m_DirectAddress = "127.0.0.1";
        [SerializeField] ushort m_DirectPort = 9787;

        public ISession Session { get; private set; }
        public string JoinCode => Session != null ? Session.Code : string.Empty;
        public bool IsBusy { get; private set; }

        /// <summary>Message lisible pour l'UI (statut ou erreur).</summary>
        public event Action<string> StatusChanged;
        /// <summary>Le NetworkManager tourne, la partie est lancée.</summary>
        public event Action Started;

        string m_LastTransportError;

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

        /// <summary>Héberge en local sans UGS (127.0.0.1). Essaie plusieurs ports si besoin.</summary>
        public void HostDirect()
        {
            if (IsBusy) return;
            StartCoroutine(HostDirectCoroutine());
        }

        IEnumerator HostDirectCoroutine()
        {
            IsBusy = true;
            try
            {
                var nm = NetworkManager.Singleton;
                if (nm == null)
                {
                    Status("Pas de NetworkManager dans la scène.");
                    yield break;
                }

                if (nm.NetworkConfig.PlayerPrefab == null)
                {
                    Status("Player Prefab manquant sur le NetworkManager.");
                    yield break;
                }

                yield return EnsureNetworkStopped(nm);

                // Écoute uniquement en local (évite les conflits de bind sur 0.0.0.0 / port 7777).
                ushort[] ports = BuildPortList(m_DirectPort);
                bool started = false;

                foreach (ushort port in ports)
                {
                    m_LastTransportError = null;
                    Application.logMessageReceived += CaptureLog;

                    var transport = ConfigureDirectTransport(m_DirectAddress, port, m_DirectAddress);
                    if (transport == null)
                    {
                        Application.logMessageReceived -= CaptureLog;
                        yield break;
                    }

                    bool ok = false;
                    Exception caught = null;
                    try
                    {
                        ok = nm.StartHost();
                    }
                    catch (Exception e)
                    {
                        caught = e;
                    }

                    Application.logMessageReceived -= CaptureLog;

                    if (caught != null)
                    {
                        Status($"Échec host : {caught.Message}");
                        Debug.LogException(caught);
                        yield return EnsureNetworkStopped(nm);
                        continue;
                    }

                    if (ok)
                    {
                        m_DirectPort = port;
                        Status($"Hébergement local OK — {m_DirectAddress}:{port}");
                        Started?.Invoke();
                        started = true;
                        break;
                    }

                    Status($"Port {port} indisponible, essai suivant...");
                    yield return EnsureNetworkStopped(nm);
                }

                if (!started)
                {
                    string detail = string.IsNullOrEmpty(m_LastTransportError)
                        ? "aucun port libre (ferme les autres instances Play / VPN)."
                        : m_LastTransportError;
                    Status($"Échec du host local : {detail}");
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        public void JoinDirect(string address)
        {
            if (!string.IsNullOrWhiteSpace(address)) m_DirectAddress = address.Trim();
            if (IsBusy) return;
            StartCoroutine(JoinDirectCoroutine());
        }

        IEnumerator JoinDirectCoroutine()
        {
            IsBusy = true;
            try
            {
                var nm = NetworkManager.Singleton;
                if (nm == null)
                {
                    Status("Pas de NetworkManager dans la scène.");
                    yield break;
                }

                yield return EnsureNetworkStopped(nm);

                var transport = ConfigureDirectTransport(m_DirectAddress, m_DirectPort);
                if (transport == null) yield break;

                try
                {
                    if (nm.StartClient())
                    {
                        Status($"Connexion directe à {m_DirectAddress}:{m_DirectPort}...");
                        Started?.Invoke();
                    }
                    else
                    {
                        Status("Échec du démarrage du client direct.");
                    }
                }
                catch (Exception e)
                {
                    Status($"Échec client direct : {e.Message}");
                    Debug.LogException(e);
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        IEnumerator EnsureNetworkStopped(NetworkManager nm)
        {
            if (nm == null) yield break;

            if (nm.IsListening || nm.IsServer || nm.IsClient || nm.ShutdownInProgress)
            {
                if (!nm.ShutdownInProgress)
                    nm.Shutdown();

                float timeout = Time.realtimeSinceStartup + 2f;
                while (nm != null &&
                       (nm.IsListening || nm.IsServer || nm.IsClient || nm.ShutdownInProgress) &&
                       Time.realtimeSinceStartup < timeout)
                {
                    yield return null;
                }

                // Laisse une frame de plus pour que UTP libère le socket.
                yield return null;
            }
        }

        void CaptureLog(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Warning) return;
            if (condition.IndexOf("bind", StringComparison.OrdinalIgnoreCase) >= 0 ||
                condition.IndexOf("listen", StringComparison.OrdinalIgnoreCase) >= 0 ||
                condition.IndexOf("transport", StringComparison.OrdinalIgnoreCase) >= 0 ||
                condition.IndexOf("NetworkManager", StringComparison.OrdinalIgnoreCase) >= 0 ||
                condition.IndexOf("prefab", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                m_LastTransportError = condition;
            }
        }

        static ushort[] BuildPortList(ushort preferred)
        {
            var list = new System.Collections.Generic.List<ushort> { preferred };
            foreach (ushort p in k_DirectPortCandidates)
            {
                if (!list.Contains(p)) list.Add(p);
            }
            return list.ToArray();
        }

        UnityTransport ConfigureDirectTransport(string address, ushort port, string listenAddress = null)
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

            // Force le protocole direct (pas Relay) + IP/port.
            transport.SetConnectionData(address, port, listenAddress ?? address);
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
