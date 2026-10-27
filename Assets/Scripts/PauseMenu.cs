using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Pause / play button + the pause menu (Resume, Restart, Level Select, Quit).
// Also shows the end-of-run screens:
//   EXTRACTED!          - you reached the portal (you keep your loot)
//   DUNGEON COLLAPSED   - the timer ran out (loot lost)
//   YOU DIED            - your health hit zero (loot lost)
public class PauseMenu : MonoBehaviour
{
    public static PauseMenu Instance { get; private set; }
    public static bool IsPaused { get; private set; }

    // All the salvage you've ever extracted with (saved on this computer). Use it for upgrades later.
    public static int BankedSalvage
    {
        get => PlayerPrefs.GetInt("BankedSalvage", 0);
        set { PlayerPrefs.SetInt("BankedSalvage", value); PlayerPrefs.Save(); }
    }

    [Header("Scenes")]
    public string levelSelectScene = "LevelSelect";

    [Header("UI")]
    public GameObject pauseOverlay;          // the dark screen with the menu
    public TextMeshProUGUI titleText;        // "PAUSED"
    public GameObject resumeButton;
    public UnityEngine.UI.Image pauseButtonIcon;
    public Sprite pauseIcon;                 // shown while playing  ( II )
    public Sprite playIcon;                  // shown while paused   ( > )

    [Header("End of run")]
    public TextMeshProUGUI bodyText;         // the line under the title on end screens
    public PlayerController player;
    public Inventory inventory;
    [Tooltip("Seconds to let the death animation play before showing YOU DIED.")]
    public float deathScreenDelay = 1.5f;

    [Header("Optional")]
    public InventoryUI inventoryUI;          // so pausing closes the bag

    bool gameOver;
    float deadFor;
    Color titleColor;
    float titleSize;

    public bool IsGameOver => gameOver;

    void Awake()
    {
        Instance = this;
        IsPaused = false;
        Time.timeScale = 1f;
    }

    void Start()
    {
        if (pauseOverlay != null) pauseOverlay.SetActive(false);
        if (bodyText != null) bodyText.gameObject.SetActive(false);
        if (titleText != null) { titleColor = titleText.color; titleSize = titleText.fontSize; }
        if (player == null) player = FindFirstObjectByType<PlayerController>();
        if (inventory == null && player != null) inventory = player.GetComponent<Inventory>();
        UpdateIcon();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        IsPaused = false;
        Time.timeScale = 1f;
    }

    void Update()
    {
        if (gameOver) return;

        // Died? Wait for the death animation, then show the screen
        if (player != null && player.IsDead)
        {
            deadFor += Time.deltaTime;
            if (deadFor >= deathScreenDelay) ShowDied();
            return;
        }

        bool esc = EscPressed() && InventoryUI.EscUsedFrame != Time.frameCount
                   && (inventoryUI == null || !inventoryUI.IsOpen);
        if (esc || PPressed()) TogglePause();
    }

    // ---------------------------------------------------------------- buttons call these
    public void TogglePause()
    {
        if (gameOver) return;
        if (IsPaused) Resume(); else Pause();
    }

    public void Pause()
    {
        if (inventoryUI != null && inventoryUI.IsOpen) inventoryUI.CloseBag();
        IsPaused = true;
        Time.timeScale = 0f;
        if (titleText != null) { titleText.text = "PAUSED"; titleText.color = titleColor; titleText.fontSize = titleSize; }
        if (bodyText != null) bodyText.gameObject.SetActive(false);
        if (resumeButton != null) resumeButton.SetActive(true);
        if (pauseOverlay != null) pauseOverlay.SetActive(true);
        UpdateIcon();
    }

    public void Resume()
    {
        if (gameOver) return;
        IsPaused = false;
        Time.timeScale = 1f;
        if (pauseOverlay != null) pauseOverlay.SetActive(false);
        UpdateIcon();
    }

    public void Restart()
    {
        IsPaused = false;
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    public void GoToLevelSelect()
    {
        IsPaused = false;
        Time.timeScale = 1f;
        SceneManager.LoadScene(levelSelectScene);
    }

    public void QuitGame()
    {
        Application.Quit();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    // ---------------------------------------------------------------- end of run
    // Called by the ExtractionPortal
    public void ShowExtracted()
    {
        if (gameOver) return;
        int value = LootValue(), count = inventory != null ? inventory.Count : 0;
        BankedSalvage += value;
        EndScreen("EXTRACTED!", new Color(0.55f, 0.9f, 0.6f),
            $"You escaped with {count} item{(count == 1 ? "" : "s")} worth <color=#F2C94C>{value}</color>.\nTotal salvage banked: {BankedSalvage}");
    }

    // Hooked to the timer's "On Time Up"
    public void ShowTimeUp()
    {
        if (gameOver) return;
        EndScreen("DUNGEON\nCOLLAPSED", new Color(1f, 0.45f, 0.35f),
            $"You were buried with your loot.\n<color=#E05A5A>{LootValue()}</color> worth of salvage lost.");
    }

    public void ShowDied()
    {
        if (gameOver) return;
        EndScreen("YOU DIED", new Color(1f, 0.45f, 0.35f),
            $"Your loot stays in the dungeon.\n<color=#E05A5A>{LootValue()}</color> worth of salvage lost.");
    }

    void EndScreen(string title, Color color, string body)
    {
        Pause();
        gameOver = true;
        if (titleText != null)
        {
            titleText.text = title;
            titleText.color = color;
            if (title.Contains("\n")) titleText.fontSize = titleSize * 0.8f;
        }
        if (bodyText != null) { bodyText.text = body; bodyText.gameObject.SetActive(true); }
        if (resumeButton != null) resumeButton.SetActive(false);
        if (player != null) player.enabled = false;   // no moving behind the end screen
    }

    int LootValue()
    {
        if (inventory == null) return 0;
        int v = 0;
        foreach (var item in inventory.items) if (item != null) v += item.value;
        return v;
    }

    // ---------------------------------------------------------------- helpers
    void UpdateIcon()
    {
        if (pauseButtonIcon != null) pauseButtonIcon.sprite = IsPaused ? playIcon : pauseIcon;
    }

    static bool EscPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Escape);
#endif
    }

    static bool PPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.pKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.P);
#endif
    }
}
