using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

public class MainMenuManager : MonoBehaviour
{
    [Header("UI References")]
    public TextMeshProUGUI crystalsText;
    public Button continueButton;

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
            else SceneManager.LoadScene("Lvl_1");
        }
        else
        {
            if (GlobalHUD.Instance != null) GlobalHUD.Instance.FadeAndLoadScene(campSceneName);
            else SceneManager.LoadScene(campSceneName);
        }
    }

    public void ContinueGame()
    {
        PlayClickSound();
        HideMenuBeforeLoad();

        if (PlayerPrefs.GetInt("TutorialCompleted", 0) == 0)
        {
            if (GlobalHUD.Instance != null) GlobalHUD.Instance.FadeAndLoadScene("Lvl_1");
            else SceneManager.LoadScene("Lvl_1");
            return;
        }

        PlayerPrefs.SetInt("IsContinuing", 1);
        PlayerPrefs.Save();

        if (GlobalHUD.Instance != null) GlobalHUD.Instance.FadeAndLoadScene(campSceneName);
        else SceneManager.LoadScene(campSceneName);
    }

    public void OpenShop()
    {
        PlayClickSound();
        HideMenuBeforeLoad();

        if (GlobalHUD.Instance != null) GlobalHUD.Instance.FadeAndLoadScene(shopSceneName);
        else SceneManager.LoadScene(shopSceneName);
    }

    public void OpenOptions()
    {
        PlayClickSound();
        SettingsUI.Instance?.OpenSettings();
    }

    public void QuitGame()
    {
        PlayClickSound();
        Application.Quit();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }
}