using UnityEngine;
using UnityEngine.UI;
using WebSocketSharp;

public class MainMenuController : MonoBehaviour
{
    [SerializeField] private GameObject playerVisual;

    [Header("Panels")]
    [SerializeField] private GameObject mainMenuPanel;
    [SerializeField] private GameObject profilePanel;
    [SerializeField] private GameObject settingsPanel;
    [SerializeField] private GameObject playPanel;

    [Header("Main Menu Buttons")]
    [SerializeField] private Button playButton;
    [SerializeField] private Button profileButton;
    [SerializeField] private Button settingsButton;
    [SerializeField] private Button exitButton;

    private string _lastMenu;

    private void Start()
    {
        playButton.onClick.AddListener(() => EnabledMenu("Play", true));
        profileButton.onClick.AddListener(() => EnabledMenu("Profile", true));
        settingsButton.onClick.AddListener(() => EnabledMenu("Settings", true));
        exitButton.onClick.AddListener(ExitGame);

        EnabledMenu("MainMenu", true);
        _lastMenu = "MainMenu";
    }

    public void PlayGame()
    {
        ShowPlayerVisual(false);
        EnabledMenu("", false);
    }

    public void ShowPlayerVisual(bool state)
    {
        playerVisual.SetActive(state);
    }

    public void BackToLastMenu(string menuName)
    {
        EnabledMenu(menuName.IsNullOrEmpty() ? _lastMenu : menuName, true);
    }

    private void EnabledMenu(string menuName, bool state)
    {
        profilePanel.SetActive(false);
        settingsPanel.SetActive(false);
        playPanel.SetActive(false);
        mainMenuPanel.SetActive(false);

        if (state == false) return;

        switch (menuName)
        {
            case "MainMenu":
                mainMenuPanel.SetActive(true);
                break;
            case "Profile":
                profilePanel.SetActive(true);
                _lastMenu = "MainMenu";
                break;
            case "Settings":
                settingsPanel.SetActive(true);
                _lastMenu = "MainMenu";
                break;
            case "Play":
                playPanel.SetActive(true);
                _lastMenu = "MainMenu";
                break;
        }
    }

    private void ExitGame()
    {
        Application.Quit();
    }
}