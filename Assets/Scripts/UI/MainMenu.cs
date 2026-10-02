using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Title screen navigation. Shows one panel at a time:
///   Title panel: Play (loads the level) and Audio (opens the audio settings).
///   Audio panel: the AudioMenu rows, plus Back to return to the title.
/// The audio panel is only hidden, not destroyed, so AudioMenu keeps its state while closed.
/// </summary>
public class MainMenu : MonoBehaviour
{
    [Header("Panels")]
    public GameObject titlePanel;
    public GameObject audioPanel;

    [Header("Buttons")]
    public Button playButton;
    public Button audioButton;
    public Button backButton;

    [Tooltip("Scene loaded by Play. It must be listed in File > Build Profiles > Scene List.")]
    public string gameScene = "Pandemonium_Level";

    private void Start()
    {
        playButton.onClick.AddListener(Play);
        audioButton.onClick.AddListener(() => Show(audioPanel));
        backButton.onClick.AddListener(() => Show(titlePanel));
        Show(titlePanel); // audio settings stay hidden until the Audio button is pressed
    }

    private void Show(GameObject panel)
    {
        titlePanel.SetActive(panel == titlePanel);
        audioPanel.SetActive(panel == audioPanel);
    }

    // AudioOutputManager and Wwise's AkInitializer survive the scene change (DontDestroyOnLoad),
    // so the devices chosen here stay in effect in the level.
    private void Play() => SceneManager.LoadScene(gameScene);
}
