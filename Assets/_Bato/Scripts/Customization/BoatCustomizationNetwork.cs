using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Bato
{
    /// <summary>Synchronise et applique la personnalisation visuelle du bateau joueur.</summary>
    public sealed class BoatCustomizationNetwork : NetworkBehaviour
    {
        [SerializeField] Transform m_VisualRoot;

        readonly NetworkVariable<byte> m_FlagColor = new NetworkVariable<byte>(0);
        readonly NetworkVariable<byte> m_SailColor = new NetworkVariable<byte>(0);
        readonly NetworkVariable<FixedString32Bytes> m_PlayerName =
            new NetworkVariable<FixedString32Bytes>(default);

        Renderer[] m_FlagRenderers;
        Renderer[] m_SailRenderers;
        MaterialPropertyBlock m_PropertyBlock;

        public string PlayerName => m_PlayerName.Value.ToString();
        public byte FlagColorIndex => m_FlagColor.Value;
        public byte SailColorIndex => m_SailColor.Value;

        public override void OnNetworkSpawn()
        {
            CacheRenderers();
            m_FlagColor.OnValueChanged += OnFlagColorChanged;
            m_SailColor.OnValueChanged += OnSailColorChanged;
            m_PlayerName.OnValueChanged += OnPlayerNameChanged;

            ApplyAll();

            if (IsOwner)
            {
                var preferences = BoatCustomizationPreferences.Load();
                ApplyPreferencesServerRpc(preferences.FlagColorIndex, preferences.SailColorIndex,
                    preferences.PlayerName);
            }
        }

        public override void OnNetworkDespawn()
        {
            m_FlagColor.OnValueChanged -= OnFlagColorChanged;
            m_SailColor.OnValueChanged -= OnSailColorChanged;
            m_PlayerName.OnValueChanged -= OnPlayerNameChanged;
        }

        public void ApplyLocalPreferences(byte flagColorIndex, byte sailColorIndex, string playerName)
        {
            if (!IsOwner) return;
            var cleanName = PlayerIdentity.Sanitize(playerName);
            BoatCustomizationPreferences.Save(flagColorIndex, sailColorIndex, cleanName);
            ApplyPreferencesServerRpc(flagColorIndex, sailColorIndex, cleanName);
        }

        [Rpc(SendTo.Server)]
        void ApplyPreferencesServerRpc(byte flagColorIndex, byte sailColorIndex, string playerName,
            RpcParams rpcParams = default)
        {
            if (!IsServer) return;

            m_FlagColor.Value = flagColorIndex;
            m_SailColor.Value = sailColorIndex;
            m_PlayerName.Value = new FixedString32Bytes(PlayerIdentity.Sanitize(playerName));
        }

        void CacheRenderers()
        {
            var root = m_VisualRoot != null ? m_VisualRoot : transform;
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            var flags = new System.Collections.Generic.List<Renderer>();
            var sails = new System.Collections.Generic.List<Renderer>();

            foreach (var renderer in renderers)
            {
                var objectName = renderer.name.ToLowerInvariant();
                if (objectName.StartsWith("flag-")) flags.Add(renderer);
                else if (objectName.StartsWith("sail-")) sails.Add(renderer);
            }

            m_FlagRenderers = flags.ToArray();
            m_SailRenderers = sails.ToArray();
            m_PropertyBlock = new MaterialPropertyBlock();
        }

        void ApplyAll()
        {
            ApplyColor(m_FlagRenderers, BoatCustomizationPalette.GetFlagColor(m_FlagColor.Value));
            ApplyColor(m_SailRenderers, BoatCustomizationPalette.GetSailColor(m_SailColor.Value));
        }

        void ApplyColor(Renderer[] renderers, Color color)
        {
            if (renderers == null) return;

            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                renderer.GetPropertyBlock(m_PropertyBlock);
                m_PropertyBlock.SetColor("_BaseColor", color);
                m_PropertyBlock.SetColor("_Color", color);
                renderer.SetPropertyBlock(m_PropertyBlock);
            }
        }

        void OnFlagColorChanged(byte _, byte value) =>
            ApplyColor(m_FlagRenderers, BoatCustomizationPalette.GetFlagColor(value));

        void OnSailColorChanged(byte _, byte value) =>
            ApplyColor(m_SailRenderers, BoatCustomizationPalette.GetSailColor(value));

        void OnPlayerNameChanged(FixedString32Bytes _, FixedString32Bytes value) { }
    }

    public readonly struct BoatCustomizationSelection
    {
        public readonly byte FlagColorIndex;
        public readonly byte SailColorIndex;
        public readonly string PlayerName;

        public BoatCustomizationSelection(byte flagColorIndex, byte sailColorIndex, string playerName)
        {
            FlagColorIndex = flagColorIndex;
            SailColorIndex = sailColorIndex;
            PlayerName = playerName;
        }
    }

    public static class BoatCustomizationPreferences
    {
        const string k_FlagKey = "Bato.Customization.FlagColor";
        const string k_SailKey = "Bato.Customization.SailColor";

        public static BoatCustomizationSelection Load() => new BoatCustomizationSelection(
            (byte)Mathf.Clamp(PlayerPrefs.GetInt(k_FlagKey, 0), 0, BoatCustomizationPalette.Count - 1),
            (byte)Mathf.Clamp(PlayerPrefs.GetInt(k_SailKey, 0), 0, BoatCustomizationPalette.Count - 1),
            PlayerIdentity.Name);

        public static void Save(byte flagColorIndex, byte sailColorIndex, string playerName)
        {
            PlayerPrefs.SetInt(k_FlagKey, Mathf.Clamp(flagColorIndex, 0, BoatCustomizationPalette.Count - 1));
            PlayerPrefs.SetInt(k_SailKey, Mathf.Clamp(sailColorIndex, 0, BoatCustomizationPalette.Count - 1));
            PlayerIdentity.Name = playerName;
            PlayerPrefs.Save();
        }
    }

    public static class BoatCustomizationPalette
    {
        static readonly Color[] s_Colors
        =
        {
            new Color(0.92f, 0.92f, 0.92f),
            new Color(0.12f, 0.35f, 0.85f),
            new Color(0.85f, 0.12f, 0.10f),
            new Color(0.10f, 0.65f, 0.25f),
            new Color(0.95f, 0.65f, 0.08f),
            new Color(0.55f, 0.15f, 0.70f),
            new Color(0.05f, 0.75f, 0.78f),
            new Color(0.95f, 0.28f, 0.55f),
            new Color(0.95f, 0.42f, 0.08f),
            new Color(0.35f, 0.18f, 0.08f),
            new Color(0.12f, 0.12f, 0.16f),
            new Color(0.48f, 0.52f, 0.58f)
        };

        static readonly string[] s_Names
        =
        {
            "Blanc perle",
            "Bleu océan",
            "Rouge corail",
            "Vert lagon",
            "Jaune soleil",
            "Violet améthyste",
            "Cyan turquoise",
            "Rose flamant",
            "Orange coucher de soleil",
            "Marron bois",
            "Noir pirate",
            "Gris acier"
        };

        public static int Count => s_Colors.Length;
        public static Color GetColor(byte index) => s_Colors[Mathf.Clamp(index, 0, s_Colors.Length - 1)];
        public static string GetName(byte index) => s_Names[Mathf.Clamp(index, 0, s_Names.Length - 1)];
        public static Color GetFlagColor(byte index) => GetColor(index);
        public static Color GetSailColor(byte index) => GetColor(index);
    }
}
