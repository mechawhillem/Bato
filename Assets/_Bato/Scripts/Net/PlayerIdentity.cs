using System.Text;
using Unity.Netcode;
using UnityEngine;

namespace Bato
{
    /// <summary>
    /// Le pseudo local.
    ///
    /// Il part dans la charge utile d'approbation de connexion plutôt que par un RPC : le serveur
    /// le reçoit donc AVANT que le joueur n'existe côté réseau, ce qui évite la fenêtre où un
    /// joueur s'affiche sans nom en attendant que son message arrive.
    ///
    /// Il est conservé d'une session à l'autre dans les PlayerPrefs, pour ne pas avoir à le
    /// retaper à chaque lancement.
    /// </summary>
    public static class PlayerIdentity
    {
        /// <summary>
        /// Limite en octets UTF-8, pas en caractères : le nom voyage dans un FixedString32Bytes,
        /// qui tient 29 octets utiles. Un accent en coûte deux.
        /// </summary>
        public const int MaxBytes = 28;

        const string k_PrefsKey = "Bato.PlayerName";

        static string s_Name;

        public static string Name
        {
            get
            {
                if (string.IsNullOrEmpty(s_Name))
                {
                    s_Name = PlayerPrefs.GetString(k_PrefsKey, string.Empty);
                    if (string.IsNullOrWhiteSpace(s_Name)) s_Name = $"Marin {Random.Range(100, 1000)}";
                }
                return s_Name;
            }
            set
            {
                // Un champ vidé ne doit pas effacer l'identité : on retombe sur un nom tiré au sort.
                var cleaned = Sanitize(value);
                s_Name = string.IsNullOrEmpty(cleaned) ? $"Marin {Random.Range(100, 1000)}" : cleaned;

                PlayerPrefs.SetString(k_PrefsKey, s_Name);
                PlayerPrefs.Save();
            }
        }

        /// <summary>Nettoie et tronque proprement, sans jamais couper un caractère en deux.</summary>
        public static string Sanitize(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

            raw = raw.Trim();
            while (Encoding.UTF8.GetByteCount(raw) > MaxBytes)
            {
                raw = raw.Substring(0, raw.Length - 1);
            }
            return raw;
        }

        /// <summary>
        /// À appeler juste avant StartHost/StartClient : NGO recopie ConnectionData dans la charge
        /// utile transmise au serveur, que ce soit via Relay ou en direct.
        /// </summary>
        public static void ApplyToConnectionData()
        {
            var manager = NetworkManager.Singleton;
            if (manager == null) return;

            manager.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(Name);
        }

        /// <summary>Côté serveur : relit le pseudo depuis la charge utile. Vide si absent.</summary>
        public static string FromPayload(byte[] payload)
        {
            if (payload == null || payload.Length == 0) return string.Empty;
            return Sanitize(Encoding.UTF8.GetString(payload));
        }
    }
}
