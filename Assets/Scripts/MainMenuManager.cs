using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

public class MainMenuManager : MonoBehaviour
{
    [Header("UI References")]
    public TextMeshProUGUI crystalsText;
    public Button continueButton;

    [Header("Extended Menu (all optional)")]
    // NEW GAME button — hard-restart the save. Fires a confirmation
    // dialog first when a save already exists so an accidental click
    // doesn't erase 40 hours of progress.
    public Button newGameButton;
    // CREDITS button — shows the CreditsUI panel.
    public Button creditsButton;
    // QUIT button — closes the application.  Wired to a Yes/No confirm
    // so a stray click on the front page doesn't just kill the game.
    public Button quitButton;
    // Optional confirmation dialog (reused by New Game + Quit).
    public GameObject confirmDialog;
    public TextMeshProUGUI confirmDialogText;
    public Button confirmYesButton;
    public Button confirmNoButton;
    // Credits panel — CanvasGroup so it can fade cleanly.
    public CreditsUI creditsUI;

    [Header("Scene Settings")]
    public string gameSceneName = "GameScene";
    public string shopSceneName = "ShopScene";
    public string campSceneName = "CampScene";

    [Header("Hero Spawning")]
    public GameObject heroPrefab;
    public GameObject[] weaponPrefabs; // ����'������ ����� ���� ����� � ���������!
    public Transform heroSpawnPoint;
    public RuntimeAnimatorController menuAnimatorController;

    private void Start()
    {
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        StartCoroutine(AnimateCrystals());
        CheckContinueStatus();
        SpawnSelectedHero();
        WireExtendedMenu();
    }

    // Wire the optional New Game / Credits / Quit buttons + confirm
    // dialog. Everything is null-safe so an old-style menu prefab with
    // only the Continue button still works unchanged.
    private void WireExtendedMenu()
    {
        if (confirmDialog != null) confirmDialog.SetActive(false);

        if (newGameButton != null)
        {
            newGameButton.onClick.RemoveAllListeners();
            newGameButton.onClick.AddListener(OnNewGameClicked);
        }
        if (creditsButton != null)
        {
            creditsButton.onClick.RemoveAllListeners();
            creditsButton.onClick.AddListener(OnCreditsClicked);
        }
        if (quitButton != null)
        {
            quitButton.onClick.RemoveAllListeners();
            quitButton.onClick.AddListener(OnQuitClicked);
        }
    }

    private void OnNewGameClicked()
    {
        // If nothing to lose, straight through — no confirm popup.
        bool hasProgress = PlayerPrefs.GetInt("TutorialCompleted", 0) == 1
                        || PlayerPrefs.GetInt("HasCampSave", 0) == 1
                        || PlayerPrefs.GetInt("TotalConqueredRegions", 0) > 0;
        if (!hasProgress) { WipeAndStartFresh(); return; }

        ShowConfirmDialog(
            LocalizationManager.Tr("MENU_CONFIRM_NEW_GAME"),
            onYes: WipeAndStartFresh);
    }

    private void OnQuitClicked()
    {
        ShowConfirmDialog(
            LocalizationManager.Tr("MENU_CONFIRM_QUIT"),
            onYes: QuitGame);
    }

    private void OnCreditsClicked()
    {
        if (creditsUI != null) creditsUI.Open();
    }

    // Universal confirm popup — text + Yes/No callbacks. No is always
    // "close dialog"; Yes is caller-supplied. Falls back to the runtime
    // ConfirmDialog utility when the scene doesn't wire an authored
    // panel, so a pristine menu prefab still gets the confirm step.
    private void ShowConfirmDialog(string message, System.Action onYes)
    {
        if (confirmDialog == null)
        {
            ConfirmDialog.Show(message, onYes);
            return;
        }
        confirmDialog.SetActive(true);
        if (confirmDialogText != null) confirmDialogText.text = message;
        if (confirmYesButton != null)
        {
            confirmYesButton.onClick.RemoveAllListeners();
            confirmYesButton.onClick.AddListener(() =>
            {
                confirmDialog.SetActive(false);
                onYes?.Invoke();
            });
        }
        if (confirmNoButton != null)
        {
            confirmNoButton.onClick.RemoveAllListeners();
            confirmNoButton.onClick.AddListener(() => confirmDialog.SetActive(false));
        }
    }

    // Wipes both the JSON save + every PlayerPrefs key we know the
    // game uses, then jumps to the tutorial scene as if it were the
    // very first launch. Kept in one place so future save-key
    // additions have a single place to opt into the wipe.
    private void WipeAndStartFresh()
    {
        // JSON save — the authoritative one going forward.
        SaveSystem.Reset();

        // Legacy PlayerPrefs — nuke the whole set. Settings (Settings_*
        // prefix + audio volumes) survive: PlayerPrefs has no
        // enumeration API, so we snapshot the ones we want to keep,
        // DeleteAll, then rewrite them.
        var settingsInts = new Dictionary<string, int>();
        var settingsFloats = new Dictionary<string, float>();
        string[] intKeys = {
            "Settings_QualityLevel", "Settings_AntiAliasing", "Settings_TextureQuality",
            "Settings_ShadowQuality", "Settings_Bloom", "Settings_AO", "Settings_Volumetrics",
            "Settings_MotionBlur", "Settings_DepthOfField", "Settings_VSync", "Settings_FPSLimit",
            "Settings_FpsCapIndex", "Settings_WindowMode", "Settings_ResolutionIndex",
            "Settings_RefreshRateIndex", "Settings_Monitor", "Settings_MuteWhenUnfocused",
            "Settings_TutorialHints", "Settings_Colorblind", "Settings_AutoConfigured",
            "Settings_AutoTier", "Settings_Language", "Settings_SubtitlesEnabled",
            "Settings_SubtitleSize",
        };
        string[] floatKeys = {
            "Settings_MasterVol", "Settings_MusicVol", "Settings_SFXVol", "Settings_VoiceVol",
            "Settings_UIVol", "Settings_AmbientVol", "Settings_ShadowDistance",
            "Settings_RenderScale", "Settings_FOV", "Settings_Brightness", "Settings_Gamma",
            "Settings_UIScale", "Settings_Sensitivity",
        };
        foreach (var k in intKeys) if (PlayerPrefs.HasKey(k)) settingsInts[k] = PlayerPrefs.GetInt(k);
        foreach (var k in floatKeys) if (PlayerPrefs.HasKey(k)) settingsFloats[k] = PlayerPrefs.GetFloat(k);

        PlayerPrefs.DeleteAll();

        foreach (var kv in settingsInts) PlayerPrefs.SetInt(kv.Key, kv.Value);
        foreach (var kv in settingsFloats) PlayerPrefs.SetFloat(kv.Key, kv.Value);
        PlayerPrefs.Save();

        // Go to the tutorial scene — the very first-launch entry point.
        SceneLoader.LoadScene("Lvl_1");
    }

    private System.Collections.IEnumerator AnimateCrystals()
    {
        if (crystalsText == null) yield break;

        int targetCrystals = PlayerPrefs.GetInt("PlayerDiamonds", 0);
        int currentCount = 0;
        float duration = 1.2f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            currentCount = (int)Mathf.Lerp(0, targetCrystals, elapsed / duration);
            crystalsText.text = currentCount.ToString("N0");
            yield return null;
        }
        crystalsText.text = targetCrystals.ToString("N0");
    }

    private void SpawnSelectedHero()
    {
        if (heroPrefab != null && heroSpawnPoint != null)
        {
            GameObject currentVisual = Instantiate(heroPrefab, heroSpawnPoint.position, heroSpawnPoint.rotation);
            currentVisual.transform.localScale = new Vector3(1f, 1f, 1f); // ����� ������ �����, ���� �������

            // Բ��: ������� �����, ��� ������� �� ��������������!
            PlayerController pc = currentVisual.GetComponent<PlayerController>();
            if (pc != null) Destroy(pc);

            CharacterController cc = currentVisual.GetComponent<CharacterController>();
            if (cc != null) Destroy(cc);

            Animator anim = currentVisual.GetComponentInChildren<Animator>();
            if (anim != null)
            {
                if (menuAnimatorController != null) anim.runtimeAnimatorController = menuAnimatorController;
                else { anim.SetBoolSafe("IsGrounded", true); anim.SetFloatSafe("Speed", 0f); }
            }

            // ������� ��������� �����
            ModularArmorManager mam = currentVisual.GetComponent<ModularArmorManager>();
            if (mam != null) mam.LoadEquippedArmor();

            // ������� ��������� �����
            Transform socket = FindDeepChild(currentVisual.transform, "handslot.r");
            if (socket == null) socket = FindDeepChild(currentVisual.transform, "RightHand");

            if (socket != null && weaponPrefabs != null && weaponPrefabs.Length > 0)
            {
                int savedWepID = PlayerPrefs.GetInt("SelectedWeaponID", 0);
                if (savedWepID < weaponPrefabs.Length && weaponPrefabs[savedWepID] != null)
                {
                    GameObject wep = Instantiate(weaponPrefabs[savedWepID], socket.position, socket.rotation, socket);
                    wep.transform.SetParent(socket);
                    foreach (var s in wep.GetComponents<MonoBehaviour>()) { if (s != null) s.enabled = false; }
                }
            }
        }
    }

    private Transform FindDeepChild(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name.ToLower() == name.ToLower()) return child;
            Transform r = FindDeepChild(child, name);
            if (r != null) return r;
        }
        return null;
    }

    private void CheckContinueStatus()
    {
        if (continueButton != null)
        {
            bool hasSave = PlayerPrefs.GetInt("HasCampSave", 0) == 1;
            bool tutorialDone = PlayerPrefs.GetInt("TutorialCompleted", 0) == 1;
            // Continue is only meaningful once the tutorial is behind us —
            // otherwise the button loads Lvl_1 anyway (see ContinueGame),
            // so pretend there's no save and show Start Adventure so the
            // label matches what actually happens on click.
            bool showContinue = hasSave && tutorialDone;

            TextMeshProUGUI btnText = continueButton.GetComponentInChildren<TextMeshProUGUI>();
            if (btnText != null) btnText.text = LocalizationManager.Tr(showContinue ? "Continue" : "Start Adventure!");

            continueButton.interactable = true;
            CanvasGroup cg = continueButton.GetComponent<CanvasGroup>();
            if (cg != null) cg.alpha = 1f;

            continueButton.onClick.RemoveAllListeners();
            if (showContinue) continueButton.onClick.AddListener(ContinueGame);
            else continueButton.onClick.AddListener(StartNewRun);
        }
    }

    private void HideMenuBeforeLoad()
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas != null) canvas.enabled = false;
        else gameObject.SetActive(false);
    }

    private void PlayClickSound() { AudioManager.Instance?.PlayUI(AudioID.UI_Click); }

    public void StartNewRun()
    {
        PlayClickSound();
        PlayerPrefs.DeleteKey("HasCampSave");
        PlayerPrefs.SetInt("IsRunActive", 0);
        PlayerPrefs.SetInt("IsContinuing", 0);
        PlayerPrefs.Save();

        ResourceManager.Instance?.ClearRunInventory();
        HideMenuBeforeLoad();

        if (PlayerPrefs.GetInt("TutorialCompleted", 0) == 0)
        {
            if (GlobalHUD.Instance != null) GlobalHUD.Instance.FadeAndLoadScene("Lvl_1");
            else SceneLoader.LoadScene("Lvl_1");
        }
        else
        {
            if (GlobalHUD.Instance != null) GlobalHUD.Instance.FadeAndLoadScene(campSceneName);
            else SceneLoader.LoadScene(campSceneName);
        }
    }

    public void ContinueGame()
    {
        PlayClickSound();
        HideMenuBeforeLoad();

        if (PlayerPrefs.GetInt("TutorialCompleted", 0) == 0)
        {
            if (GlobalHUD.Instance != null) GlobalHUD.Instance.FadeAndLoadScene("Lvl_1");
            else SceneLoader.LoadScene("Lvl_1");
            return;
        }

        PlayerPrefs.SetInt("IsContinuing", 1);
        PlayerPrefs.Save();

        if (GlobalHUD.Instance != null) GlobalHUD.Instance.FadeAndLoadScene(campSceneName);
        else SceneLoader.LoadScene(campSceneName);
    }

    public void OpenShop()
    {
        PlayClickSound();
        HideMenuBeforeLoad();

        if (GlobalHUD.Instance != null) GlobalHUD.Instance.FadeAndLoadScene(shopSceneName);
        else SceneLoader.LoadScene(shopSceneName);
    }

    public void OpenOptions()
    {
        PlayClickSound();
        SettingsUI.Instance?.OpenSettings();
    }

    public void QuitGame()
    {
        PlayClickSound();
        // Route through the confirm dialog even when the scene button
        // was wired directly to this public method (not through the
        // extended-menu `quitButton` field). Users kept clicking Quit
        // and hard-exiting without a prompt because the button in the
        // scene was bound to QuitGame() via UnityEvent, bypassing the
        // OnQuitClicked → ShowConfirmDialog path.
        ShowConfirmDialog(
            LocalizationManager.Tr("MENU_CONFIRM_QUIT"),
            onYes: QuitGameConfirmed);
    }

    private void QuitGameConfirmed()
    {
        Application.Quit();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }
}