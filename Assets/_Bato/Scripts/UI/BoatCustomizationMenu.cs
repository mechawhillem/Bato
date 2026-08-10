using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace Bato
{
    /// <summary>Relie le menu de personnalisation aux préférences et au visuel du bateau.</summary>
    public sealed class BoatCustomizationMenu : MonoBehaviour
    {
        [Header("Panneau")]
        [SerializeField] GameObject m_Panel;
        [SerializeField] TMP_InputField m_PlayerNameField;
        [SerializeField] TMP_Dropdown m_FlagColorDropdown;
        [SerializeField] TMP_Dropdown m_SailColorDropdown;
        [SerializeField] Button m_ApplyButton;
        [SerializeField] Button m_ResetButton;
        [SerializeField] TMP_Text m_StatusLabel;

        [Header("Aperçu visuel")]
        [Tooltip("Racine du bateau à recolorer dans le menu. Les enfants flag-* et sail-* sont détectés automatiquement.")]
        [SerializeField] Transform m_VisualRoot;

        Renderer[] m_FlagRenderers;
        Renderer[] m_SailRenderers;
        MaterialPropertyBlock m_PropertyBlock;

        void Awake()
        {
            CachePreviewRenderers();

            var preferences = BoatCustomizationPreferences.Load();
            if (m_PlayerNameField != null)
            {
                m_PlayerNameField.characterLimit = PlayerIdentity.MaxBytes;
                m_PlayerNameField.text = preferences.PlayerName;
            }

            if (m_FlagColorDropdown != null)
            {
                ConfigureColorDropdown(m_FlagColorDropdown);
                m_FlagColorDropdown.value = Mathf.Clamp(preferences.FlagColorIndex, 0, BoatCustomizationPalette.Count - 1);
                m_FlagColorDropdown.onValueChanged.AddListener(OnFlagColorSelected);
            }

            if (m_SailColorDropdown != null)
            {
                ConfigureColorDropdown(m_SailColorDropdown);
                m_SailColorDropdown.value = Mathf.Clamp(preferences.SailColorIndex, 0, BoatCustomizationPalette.Count - 1);
                m_SailColorDropdown.onValueChanged.AddListener(OnSailColorSelected);
            }

            if (m_ApplyButton != null) m_ApplyButton.onClick.AddListener(Apply);
            if (m_ResetButton != null) m_ResetButton.onClick.AddListener(Reset);

            ApplyPreview(preferences.FlagColorIndex, preferences.SailColorIndex);
        }

        void OnDestroy()
        {
            if (m_FlagColorDropdown != null) m_FlagColorDropdown.onValueChanged.RemoveListener(OnFlagColorSelected);
            if (m_SailColorDropdown != null) m_SailColorDropdown.onValueChanged.RemoveListener(OnSailColorSelected);
            if (m_ApplyButton != null) m_ApplyButton.onClick.RemoveListener(Apply);
            if (m_ResetButton != null) m_ResetButton.onClick.RemoveListener(Reset);
        }

        public void TogglePanel()
        {
            if (m_Panel != null) m_Panel.SetActive(!m_Panel.activeSelf);
        }

        /// <summary>Permet aussi de rafraîchir l'aperçu depuis un bouton UI ou un autre script.</summary>
        public void RefreshPreview()
        {
            var preferences = BoatCustomizationPreferences.Load();
            var flagIndex = (byte)(m_FlagColorDropdown != null ? m_FlagColorDropdown.value : preferences.FlagColorIndex);
            var sailIndex = (byte)(m_SailColorDropdown != null ? m_SailColorDropdown.value : preferences.SailColorIndex);
            ApplyPreview(flagIndex, sailIndex);
        }

        /// <summary>Réinitialise les couleurs et conserve le pseudo actuel.</summary>
        public void Reset()
        {
            const byte defaultColorIndex = 0;
            var currentName = PlayerIdentity.Name;

            if (m_PlayerNameField != null) m_PlayerNameField.text = currentName;
            if (m_FlagColorDropdown != null) m_FlagColorDropdown.SetValueWithoutNotify(defaultColorIndex);
            if (m_SailColorDropdown != null) m_SailColorDropdown.SetValueWithoutNotify(defaultColorIndex);

            Apply();
            SetStatus("Personnalisation réinitialisée.");
        }
        public void Apply()
        {
            var selection = BoatCustomizationPreferences.Load();
            var name = m_PlayerNameField != null ? m_PlayerNameField.text : selection.PlayerName;
            var flagIndex = (byte)(m_FlagColorDropdown != null ? m_FlagColorDropdown.value : selection.FlagColorIndex);
            var sailIndex = (byte)(m_SailColorDropdown != null ? m_SailColorDropdown.value : selection.SailColorIndex);

            BoatCustomizationPreferences.Save(flagIndex, sailIndex, name);
            ApplyPreview(flagIndex, sailIndex);

            var networkManager = NetworkManager.Singleton;
            var playerObject = networkManager != null && networkManager.IsListening
                ? networkManager.SpawnManager.GetLocalPlayerObject()
                : null;
            var customization = playerObject != null
                ? playerObject.GetComponent<BoatCustomizationNetwork>()
                : null;

            if (customization != null)
            {
                customization.ApplyLocalPreferences(flagIndex, sailIndex, name);
                SetStatus("Personnalisation appliquée à ton bateau.");
            }
            else
            {
                SetStatus("Préférences enregistrées. Elles seront appliquées à la création du bateau.");
            }
        }

        void OnFlagColorSelected(int value)
        {
            RefreshPreview();
        }

        void OnSailColorSelected(int value)
        {
            RefreshPreview();
        }

        void ConfigureColorDropdown(TMP_Dropdown dropdown)
        {
            dropdown.ClearOptions();
            var options = new System.Collections.Generic.List<TMP_Dropdown.OptionData>(BoatCustomizationPalette.Count);

            for (byte index = 0; index < BoatCustomizationPalette.Count; index++)
            {
                var color = BoatCustomizationPalette.GetColor(index);
                var hex = ColorUtility.ToHtmlStringRGB(color);
                var label = $"<color=#{hex}>■</color> {BoatCustomizationPalette.GetName(index)}";
                options.Add(new TMP_Dropdown.OptionData(label));
            }

            dropdown.AddOptions(options);
        }
        void CachePreviewRenderers()
        {
            if (m_VisualRoot == null) return;

            var renderers = m_VisualRoot.GetComponentsInChildren<Renderer>(true);
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

        void ApplyPreview(byte flagIndex, byte sailIndex)
        {
            ApplyColor(m_FlagRenderers, BoatCustomizationPalette.GetFlagColor(flagIndex));
            ApplyColor(m_SailRenderers, BoatCustomizationPalette.GetSailColor(sailIndex));
        }

        void ApplyColor(Renderer[] renderers, Color color)
        {
            if (renderers == null || m_PropertyBlock == null) return;

            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                renderer.GetPropertyBlock(m_PropertyBlock);
                m_PropertyBlock.SetColor("_BaseColor", color);
                m_PropertyBlock.SetColor("_Color", color);
                renderer.SetPropertyBlock(m_PropertyBlock);
            }
        }

        void SetStatus(string message)
        {
            if (m_StatusLabel != null) m_StatusLabel.text = message;
        }
    }
}
