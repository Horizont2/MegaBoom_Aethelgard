using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System;

public enum ResourceType { Wood, Stone, Food }

[System.Serializable]
public class BuildingLevel
{
    public int costWood;
    public int costStone;
    public int costFood;
    public int productionValue;
    public string productionDescription;
    public float buildTime = 5f;
}

public class CampBuilding : MonoBehaviour
{
    [Header("Logistics (NEW)")]
    public ResourceType productionType;
    public Transform pickupPoint;
    public int pendingResourcesCount = 0;
    public bool hasStorageInCamp = false;

    [Header("Unique ID")]
    public string buildingID = "Building_01";

    [Header("Building Objects")]
    public GameObject ghostModel;
    public GameObject realModel;
    public Sprite buildingIconSprite;

    [Header("Visual Production Piles")]
    public GameObject[] resourceVisuals;
    private int currentVisualIndex = 0;

    [Header("Building Info")]
    public string buildingName = "BUILDING";
    [TextArea] public string description = "Building description.";
    public bool isStorageVault = false;

    [Header("Levels & Upgrades")]
    public int currentLevel = 0;
    public BuildingLevel[] levels;

    [Header("UI & Interaction (NEW)")]
    public GameObject aaaPanel;
    public Image holdFillImage;
    public float holdTimeRequired = 1.5f;
    private float currentHoldTime = 0f;
    // Only-if-changed cache for the [E] hold percentage TMP write.
    private int lastHoldPercentDisplayed = -1;
    private bool isPanelOpen = false;

    [Header("UI Text References")]
    public TextMeshProUGUI titleTMP;
    public TextMeshProUGUI lvlTMP;
    public TextMeshProUGUI descTMP;
    public TextMeshProUGUI infoTMP;
    public TextMeshProUGUI progressTMP;
    public Image buildingIconImage;

    [Header("Resource Cost Texts")]
    public TextMeshProUGUI costWoodTMP;
    public TextMeshProUGUI costStoneTMP;
    public TextMeshProUGUI costFoodTMP;
    public TextMeshProUGUI buildHintTMP;

    [Header("3D Effects & Seasons")]
    public GameObject upgradeGlimmer;
    public GameObject snowClumps;

    [Header("Cinematic Effects")]
    public ParticleSystem buildDustVFX;
    public float spawnDepth = 12f;
    public float upgradeBounceAmount = 1.15f;

    private bool playerInRange = false;
    private bool isAnimating = false;
    private float glimmerCheckTimer = 0f;
    private SmartSeasonManager seasonManager;
    private Coroutine productionCoroutine;
    private Transform playerTransform;

    private Vector3 originalModelPos;
    private Vector3 originalModelScale;

    private static CampBuilding storageBuildingCached;

    // Handle for the looping 3D build SFX. -1 = nothing playing. Kept so
    // we can Stop it exactly when the animation ends instead of letting
    // FMOD's PlayOneShot finish a too-long clip after construction.
    private int buildSfxHandle = -1;

    // Cached PlayerPrefs keys — the `"Prefix_" + buildingID` concat used
    // to run every frame in Update on every building (GC + string-table
    // lookup). Built once in Awake.
    private string ppKey_Save;
    private string ppKey_UpgradeFinished;
    private string ppKey_IsUpgrading;
    private string ppKey_UpgradeStart;

    private void Awake()
    {
        // Cache PlayerPrefs keys once so the Update path stops allocating
        // a fresh string every frame per building.
        ppKey_Save = "SaveBld_" + buildingID;
        ppKey_UpgradeFinished = "UpgradeFinished_" + buildingID;
        ppKey_IsUpgrading = "IsUpgrading_" + buildingID;
        ppKey_UpgradeStart = "UpgradeStart_" + buildingID;

        if (realModel != null)
        {
            originalModelPos = realModel.transform.localPosition;
            originalModelScale = realModel.transform.localScale;
        }

        // Force the info panel closed before the first frame renders. Prefabs
        // often ship with the panel active so designers can edit it, and if
        // Start() runs late (or another script re-enables it via a lingering
        // coroutine between scene loads) the raw "New Text" placeholder can
        // flash on screen the moment the camp scene finishes loading.
        if (aaaPanel != null && aaaPanel.activeSelf) aaaPanel.SetActive(false);

        // These labels are set by code (BUILD vs UPGRADE by level, live info /
        // costs). AutoLocalize was re-capturing the baked "HOLD [E] TO BUILD"
        // and re-applying it whenever the panel re-enabled, which fought the
        // 1-second UpdateUIData refresh and made the hint flip BUILD<->UPGRADE.
        MarkNoAutoLocalize(buildHintTMP);
        MarkNoAutoLocalize(infoTMP);
    }

    private static void MarkNoAutoLocalize(TMPro.TextMeshProUGUI t)
    {
        if (t != null && t.GetComponent<NoAutoLocalize>() == null)
            t.gameObject.AddComponent<NoAutoLocalize>();
    }

    private void OnEnable()
    {
        // Extra guard on scene reload — never come back with the panel visible
        // unless the player explicitly opened it via [F].
        if (aaaPanel != null && aaaPanel.activeSelf && !isPanelOpen) aaaPanel.SetActive(false);
    }

    private void OnDestroy()
    {
        // Kill the looping build SFX if the building is torn down mid-hammer
        // (scene unload, prefab replaced, etc.) — otherwise the FMOD event
        // instance keeps playing until natural end.
        if (buildSfxHandle != -1 && AudioManager.Instance != null)
        {
            AudioManager.Instance.StopLoopingSFX(buildSfxHandle, 0f);
            buildSfxHandle = -1;
        }
    }

    private void Start()
    {
        seasonManager = FindFirstObjectByType<SmartSeasonManager>();

        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null) playerTransform = playerObj.transform;

        if (aaaPanel != null) aaaPanel.SetActive(false);
        if (holdFillImage != null) holdFillImage.fillAmount = 0f;
        if (progressTMP != null) progressTMP.text = "0%";

        HideAllVisualResources();

        currentLevel = PlayerPrefs.GetInt("SaveBld_" + buildingID, 0);

        if (PlayerPrefs.GetInt("UpgradeFinished_" + buildingID, 0) == 1)
        {
            PlayerPrefs.SetInt("UpgradeFinished_" + buildingID, 0);
            currentLevel++;
            PlayerPrefs.SetInt("SaveBld_" + buildingID, currentLevel);
            PlayerPrefs.SetInt("IsUpgrading_" + buildingID, 0);
            PlayerPrefs.Save();
        }

        SetupVisualsForCurrentLevel();
        if (currentLevel > 0) ApplyBuildingEffects();

        if (PlayerPrefs.GetInt("IsUpgrading_" + buildingID, 0) == 1) StartDustEffect();
        else StopDustEffect();

        UpdateGlimmerState();
    }

    private void SetupVisualsForCurrentLevel()
    {
        if (currentLevel == 0)
        {
            if (ghostModel != null) ghostModel.SetActive(true);
            if (realModel != null) realModel.SetActive(false);
        }
        else
        {
            if (ghostModel != null) ghostModel.SetActive(false);
            if (realModel != null)
            {
                realModel.SetActive(true);
                realModel.transform.localPosition = originalModelPos;
            }
        }
    }

    private void Update()
    {

        if (aaaPanel != null)
        {
            // Якщо стороння система примусово увімкнула панель, а гравець її не відкривав:
            if (aaaPanel.activeSelf && !isPanelOpen)
            {
                aaaPanel.SetActive(false);
            }
            // Якщо GlobalHUD примусово вимкнув активну панель під час входу в паузу:
            else if (!aaaPanel.activeSelf && isPanelOpen)
            {
                ClosePanel(); // Коректно скидаємо всі внутрішні змінні будівлі, щоб вона не зависла
            }
        }

        // ПРІОРИТЕТ: Читання F завжди нагорі — навіть якщо код нижче
        // зламається, панель відкриється. Раніше цей блок був
        // задубльований: перший виклик відкривав панель, другий у
        // тому ж кадрі (Input.GetKeyDown лишається true впродовж
        // усього кадру) миттєво її закривав, тому клік по F виглядав
        // ніби ігнорується.
        if (playerInRange && Input.GetKeyDown(KeyCode.F))
        {
            if (isPanelOpen) ClosePanel();
            else OpenPanel();
        }

        // Загортаємо решту логіки в try-catch, щоб вона не спамила помилками 
        // і не ламала інші будівлі на рівні
        try
        {
            // 2. Сніг
            if (snowClumps != null && seasonManager != null)
            {
                bool isWinter = (seasonManager.currentSeason == Season.Winter);
                bool isBuilt = (currentLevel > 0);
                snowClumps.SetActive(isBuilt && isWinter && !isAnimating);
            }

            if (isAnimating) return;

            // 3. Перевірка завершення апгрейду — cached key path avoids
            //    "UpgradeFinished_" + buildingID string concat every frame.
            if (PlayerPrefs.GetInt(ppKey_UpgradeFinished, 0) == 1)
            {
                PlayerPrefs.SetInt(ppKey_UpgradeFinished, 0);
                StartCoroutine(CompleteUpgradeSequence(currentLevel + 1));
                return;
            }

            // 4. Активний процес апгрейду (анімація побудови)
            if (PlayerPrefs.GetInt(ppKey_IsUpgrading, 0) == 1)
            {
                if (isPanelOpen) ClosePanel();

                if (buildDustVFX != null && !buildDustVFX.isPlaying) StartDustEffect();
                if (upgradeGlimmer != null && upgradeGlimmer.activeSelf) upgradeGlimmer.SetActive(false);

                string startTimeStr = PlayerPrefs.GetString(ppKey_UpgradeStart, "");
                if (long.TryParse(startTimeStr, out long startTimeBin))
                {
                    DateTime startTime = DateTime.FromBinary(startTimeBin);
                    float elapsed = (float)(DateTime.UtcNow - startTime).TotalSeconds;

                    if (levels != null && currentLevel < levels.Length)
                    {
                        float totalTime = levels[currentLevel].buildTime;
                        if (totalTime > 0)
                        {
                            float progress = Mathf.Clamp01(elapsed / totalTime);

                            if (ghostModel != null && !ghostModel.activeSelf) ghostModel.SetActive(true);
                            if (realModel != null)
                            {
                                if (!realModel.activeSelf) realModel.SetActive(true);
                                Vector3 startPos = originalModelPos - new Vector3(0, spawnDepth, 0);
                                realModel.transform.localPosition = Vector3.Lerp(startPos, originalModelPos, progress);
                            }
                        }
                    }
                }
                return;
            }

            // 5. Оновлення світіння
            glimmerCheckTimer += Time.deltaTime;
            if (glimmerCheckTimer >= 1f)
            {
                glimmerCheckTimer = 0f;
                UpdateGlimmerState();
                if (isPanelOpen) UpdateUIData();
            }

            // Якщо туторіал активний - блокуємо взаємодію
            try { if (TutorialPanelUI.IsTutorialActive) return; } catch { }

            // 6. Автозакриття панелі при віддаленні
            if (isPanelOpen && playerTransform != null)
            {
                if (Vector3.Distance(transform.position, playerTransform.position) > 12f)
                {
                    playerInRange = false;
                    ClosePanel();
                    if (GlobalHUD.Instance != null) GlobalHUD.Instance.HidePrompt();
                    return;
                }
            }

            if (!playerInRange) return;

            // 7. Показ підказки [F]
            if (!isPanelOpen && PlayerPrefs.GetInt(ppKey_IsUpgrading, 0) == 0)
            {
                if (GlobalHUD.Instance != null)
                {
                    GlobalHUD.Instance.ShowPrompt(LocalizationManager.Tr("PROMPT_INSPECT_BUILDING", LocalizationManager.Tr(buildingName)));
                }
            }

            // 8. Логіка утримання клавіші [E] всередині панелі
            if (isPanelOpen && levels != null && currentLevel < levels.Length)
            {
                BuildingLevel nextLevelData = levels[currentLevel];

                bool canAfford = false;
                if (ResourceManager.Instance != null && nextLevelData != null)
                {
                    canAfford = ResourceManager.Instance.CanAffordStash(nextLevelData.costWood, nextLevelData.costStone, nextLevelData.costFood);
                }

                if (Input.GetKey(KeyCode.E) && canAfford)
                {
                    currentHoldTime += Time.deltaTime;
                    float fillRatio = holdTimeRequired > 0 ? currentHoldTime / holdTimeRequired : 1f;

                    if (holdFillImage != null) holdFillImage.fillAmount = fillRatio;
                    if (progressTMP != null)
                    {
                        // Only rewrite when the displayed integer percent
                        // changes — a per-frame string alloc + TMP relayout
                        // was one of the top HUD GC spikes.
                        int pct = (int)(fillRatio * 100);
                        if (pct != lastHoldPercentDisplayed)
                        {
                            lastHoldPercentDisplayed = pct;
                            progressTMP.text = pct + "%";
                        }
                    }

                    if (currentHoldTime >= holdTimeRequired)
                    {
                        currentHoldTime = 0f;
                        if (progressTMP != null) progressTMP.text = "100%";

                        if (ResourceManager.Instance != null)
                        {
                            ResourceManager.Instance.SpendStashResources(nextLevelData.costWood, nextLevelData.costStone, nextLevelData.costFood);
                        }

                        long startTimeBinary = DateTime.UtcNow.ToBinary();
                        PlayerPrefs.SetString("UpgradeStart_" + buildingID, startTimeBinary.ToString());
                        PlayerPrefs.SetInt("IsUpgrading_" + buildingID, 1);
                        PlayerPrefs.Save();

                        StartDustEffect();
                        // 3D-positioned looping SFX so the sound sits at the
                        // building in space (not glued to the player's ears)
                        // and can be cleanly stopped when construction ends —
                        // one-shot fires the whole clip regardless of build
                        // length, which is why hammering kept ringing after
                        // the building was already up.
                        if (AudioManager.Instance != null)
                        {
                            if (buildSfxHandle != -1) AudioManager.Instance.StopLoopingSFX(buildSfxHandle, 0f);
                            buildSfxHandle = AudioManager.Instance.PlayLoopingSFX3D(AudioID.Camp_BuildStart, transform);
                        }

                        if (GlobalHUD.Instance != null && nextLevelData != null)
                            GlobalHUD.Instance.StartTrackingUpgrade(buildingID, buildingName, buildingIconSprite, nextLevelData.buildTime, startTimeBinary);

                        ClosePanel();
                    }
                }
                else
                {
                    if (currentHoldTime > 0)
                    {
                        currentHoldTime -= Time.deltaTime * 3f;
                        currentHoldTime = Mathf.Max(0, currentHoldTime);
                        float fillRatio = holdTimeRequired > 0 ? currentHoldTime / holdTimeRequired : 0f;

                        if (holdFillImage != null) holdFillImage.fillAmount = fillRatio;
                        if (progressTMP != null) progressTMP.text = $"{(int)(fillRatio * 100)}%";
                    }
                }
            }
        }
        catch (System.Exception ex)
        {
            // Якщо щось зламалося в ефектах, панель все одно працюватиме!
            Debug.LogWarning($"[CampBuilding] Фонова помилка у {buildingName}, але будівля продовжує працювати: {ex.Message}");
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInRange = true;
            if (GlobalHUD.Instance != null && !isPanelOpen)
            {
                GlobalHUD.Instance.ShowPrompt(LocalizationManager.Tr("PROMPT_INSPECT_BUILDING", LocalizationManager.Tr(buildingName)));
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInRange = false;
            ClosePanel();
            if (GlobalHUD.Instance != null) GlobalHUD.Instance.HidePrompt();
        }
    }

    // Only ONE building panel may be open at a time. Several open panels render
    // at the same screen position and their 1-second UpdateUIData refreshes
    // overlapped, which made the hint flicker BUILD<->UPGRADE between buildings.
    private static CampBuilding s_openBuilding;

    private void OpenPanel()
    {
        // Close any other building's panel first so two never overlap.
        if (s_openBuilding != null && s_openBuilding != this)
            s_openBuilding.ClosePanel();
        s_openBuilding = this;

        // Per-building first-open hints — teach what each building does
        // the first time the player inspects it. Keyed on buildingID so
        // each building gets its own one-shot.
        if (TutorialHints.Instance != null && !string.IsNullOrEmpty(buildingID))
        {
            string bid = buildingID.ToLowerInvariant();
            if (bid.Contains("storage") || bid.Contains("vault"))
            {
                TutorialHints.Instance.ShowIfNew("StorageVaultInspect",
                    "The Storage Vault raises your max Wood / Stone / Food capacity. Upgrade it BEFORE big builds so nothing overflows.", 6f);
            }
            else if (bid.Contains("hunter") || bid.Contains("cabin"))
            {
                TutorialHints.Instance.ShowIfNew("HunterCabinInspect",
                    "The Hunter's Cabin produces FOOD per minute — the rarest basic resource. Prioritise it before high-tier builds.", 6f);
            }
            else if (bid.Contains("lumberjack") || bid.Contains("lumber"))
            {
                TutorialHints.Instance.ShowIfNew("LumberjackInspect",
                    "The Lumberjack's Hut generates LOGS per minute. Cheapest resource but every build needs some.", 5f);
            }
            else if (bid.Contains("forge") || bid.Contains("blacksmith"))
            {
                TutorialHints.Instance.ShowIfNew("ForgeInspect",
                    "The Forge boosts your in-mission weapon damage by up to +15% at max level. Stacks with weapon tier.", 6f);
            }
        }

        // A sibling script on this GO can hijack the F-panel — used by the
        // barracks to open its custom Hire/Upgrade UI instead of the generic
        // building sheet. Interface check keeps CampBuilding independent
        // of the mercenary module.
        var custom = GetComponent<ICustomBuildingPanel>();
        if (custom != null)
        {
            if (AudioManager.Instance != null) AudioManager.Instance.PlayUI(AudioID.UI_Click);
            if (GlobalHUD.Instance != null) GlobalHUD.Instance.HidePrompt();
            isPanelOpen = true;
            custom.OpenCustomPanel();
            return;
        }

        isPanelOpen = true;
        if (AudioManager.Instance != null) AudioManager.Instance.PlayUI(AudioID.UI_Click);

        UpdateUIData();

        if (aaaPanel != null)
        {
            // Force every ancestor up to the canvas active. If the
            // player HUD's ToggleGameplayUIForPause turn (or a scene
            // controller) left the panel's parent container disabled,
            // SetActive on the leaf would silently sit with
            // activeInHierarchy=false and nothing would show up.
            Transform t = aaaPanel.transform;
            while (t != null)
            {
                if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
                t = t.parent;
            }
            aaaPanel.SetActive(true);

            // Some canvases carry a CanvasGroup that a previous close
            // routine faded to 0; make sure the panel is actually
            // interactable + visible when we reopen it.
            CanvasGroup cg = aaaPanel.GetComponent<CanvasGroup>();
            if (cg != null)
            {
                cg.alpha = 1f;
                cg.interactable = true;
                cg.blocksRaycasts = true;
            }

            StartCoroutine(PopUpUI(aaaPanel.transform));
        }
        else
        {
            Debug.LogWarning($"[CampBuilding] У будівлі {buildingName} не призначена aaaPanel в інспекторі!");
        }

        if (GlobalHUD.Instance != null) GlobalHUD.Instance.HidePrompt();
    }

    private void ClosePanel()
    {
        isPanelOpen = false;
        if (s_openBuilding == this) s_openBuilding = null;
        currentHoldTime = 0f;

        if (holdFillImage != null) holdFillImage.fillAmount = 0f;
        if (progressTMP != null) progressTMP.text = "0%";
        if (aaaPanel != null) aaaPanel.SetActive(false);

        // If a sibling script hijacked OpenPanel via ICustomBuildingPanel,
        // also route the close through so pressing F a second time actually
        // hides the custom UI (e.g. BarracksUpgradePanel).
        var custom = GetComponent<ICustomBuildingPanel>();
        if (custom != null) custom.CloseCustomPanel();
    }

    private IEnumerator PopUpUI(Transform targetTransform)
    {
        targetTransform.localScale = new Vector3(0.8f, 0.8f, 0.8f);
        float t = 0;
        while (t < 1)
        {
            t += Time.deltaTime * 8f;
            targetTransform.localScale = Vector3.Lerp(new Vector3(0.8f, 0.8f, 0.8f), Vector3.one, Mathf.SmoothStep(0, 1, t));
            yield return null;
        }
    }

    private float GetForgeMultiplier(int level)
    {
        switch (level)
        {
            case 1: return 1.02f;
            case 2: return 1.05f;
            case 3: return 1.08f;
            case 4: return 1.11f;
            case 5: return 1.15f;
            default: return 1.00f;
        }
    }

    private void UpdateUIData()
    {
        // Read the AUTHORITATIVE saved level every refresh. The hint (BUILD vs
        // UPGRADE) is chosen from currentLevel; if that field was ever stale
        // (e.g. not reloaded after an upgrade, or a second code path holding an
        // old value), the 1-second refresh flipped the hint back and forth.
        // PlayerPrefs is the single source of truth (written on build/upgrade).
        if (!string.IsNullOrEmpty(ppKey_Save))
            currentLevel = PlayerPrefs.GetInt(ppKey_Save, currentLevel);

        // 1. Оновлюємо базові тексти ЗАВЖДИ (навіть якщо максимальний рівень).
        // Building name + description live on the SO/prefab in English;
        // wrap through Tr so the localisation table can override.
        if (titleTMP != null) titleTMP.text = LocalizationManager.Tr(buildingName).ToUpper();
        if (lvlTMP != null) lvlTMP.text = currentLevel == 0 ? LocalizationManager.Tr("CB_UNBUILT_LABEL") : LocalizationManager.Tr("CB_LEVEL_LABEL", currentLevel);
        if (descTMP != null) descTMP.text = LocalizationManager.Tr(description);

        if (buildingIconImage != null && buildingIconSprite != null)
            buildingIconImage.sprite = buildingIconSprite;

        // 2. Якщо масив рівнів пустий - просто виходимо (базові тексти вже оновилися)
        if (levels == null || levels.Length == 0) return;

        string prodLabel = (buildingID == "ScoutsLodge") ? LocalizationManager.Tr("CB_FEATURE_LABEL") : LocalizationManager.Tr("CB_PRODUCTION_LABEL");

        // 3. БАГФІКС: Якщо будівля вже МАКСИМАЛЬНОГО рівня
        if (currentLevel >= levels.Length)
        {
            BuildingLevel maxLevelData = levels[levels.Length - 1];

            if (infoTMP != null)
                infoTMP.text = $"{prodLabel}: <b><color=#A8E6CF>{LocalizationManager.Tr(maxLevelData.productionDescription)}</color></b>\n<color=#F1C40F>{LocalizationManager.Tr("CB_MAX_LEVEL")}</color>";

            if (buildHintTMP != null) buildHintTMP.text = LocalizationManager.Tr("MAX LEVEL");

            // Ховаємо вартість, бо купувати більше нічого
            if (costWoodTMP != null) costWoodTMP.text = "-";
            if (costStoneTMP != null) costStoneTMP.text = "-";
            if (costFoodTMP != null) costFoodTMP.text = "-";

            return; // Виходимо, бо далі йде логіка апгрейду
        }

        // 4. Логіка для нормального апгрейду
        BuildingLevel nextLevelData = levels[currentLevel];
        string infoText = "";

        if (currentLevel == 0)
        {
            infoText += $"{prodLabel}: <b><color=#FFFFFF>{LocalizationManager.Tr(nextLevelData.productionDescription)}</color></b>\n";
            infoText += LocalizationManager.Tr("CB_BUILD_TIME", $"<b><color=#FFFFFF>{nextLevelData.buildTime}</color></b>");
            if (buildHintTMP != null) buildHintTMP.text = GamepadGlyphs.Apply(LocalizationManager.Tr("HOLD [E] TO BUILD"));
        }
        else
        {
            if (currentLevel > 0 && currentLevel - 1 < levels.Length)
                infoText += $"{prodLabel}: <b><color=#FFFFFF>{LocalizationManager.Tr(levels[currentLevel - 1].productionDescription)}</color></b>";

            infoText += $" ➔ <b><color=#A8E6CF>{LocalizationManager.Tr(nextLevelData.productionDescription)}</color></b>\n";
            infoText += LocalizationManager.Tr("CB_UPGRADE_TIME", $"<b><color=#FFFFFF>{nextLevelData.buildTime}</color></b>");

            if (buildHintTMP != null) buildHintTMP.text = GamepadGlyphs.Apply(LocalizationManager.Tr("HOLD [E] TO UPGRADE"));
        }

        if (infoTMP != null) infoTMP.text = infoText;

        // Оновлюємо колір ресурсів
        if (ResourceManager.Instance != null)
        {
            if (costWoodTMP != null)
            {
                costWoodTMP.text = nextLevelData.costWood.ToString();
                costWoodTMP.color = ResourceManager.Instance.stashWood >= nextLevelData.costWood ? Color.white : Color.red;
            }
            if (costStoneTMP != null)
            {
                costStoneTMP.text = nextLevelData.costStone.ToString();
                costStoneTMP.color = ResourceManager.Instance.stashStone >= nextLevelData.costStone ? Color.white : Color.red;
            }
            if (costFoodTMP != null)
            {
                costFoodTMP.text = nextLevelData.costFood.ToString();
                costFoodTMP.color = ResourceManager.Instance.stashFood >= nextLevelData.costFood ? Color.white : Color.red;
            }
        }
    }

    private IEnumerator CompleteUpgradeSequence(int targetLevel)
    {
        isAnimating = true;
        if (GlobalHUD.Instance != null) GlobalHUD.Instance.HidePrompt();
        if (upgradeGlimmer != null) upgradeGlimmer.SetActive(false);

        if (productionCoroutine != null)
        {
            StopCoroutine(productionCoroutine);
            productionCoroutine = null;
        }

        if (ghostModel != null) ghostModel.SetActive(false);
        if (realModel != null)
        {
            realModel.SetActive(true);
            realModel.transform.localPosition = originalModelPos;
        }

        float popTimer = 0f;
        float popDuration = 0.5f;
        Vector3 bounceScale = originalModelScale * upgradeBounceAmount;

        while (popTimer < popDuration && realModel != null)
        {
            popTimer += Time.deltaTime;
            float progress = popTimer / popDuration;
            float scaleCurve = Mathf.PingPong(progress * 2f, 1f);
            realModel.transform.localScale = Vector3.Lerp(originalModelScale, bounceScale, Mathf.SmoothStep(0f, 1f, scaleCurve));
            yield return null;
        }
        if (realModel != null) realModel.transform.localScale = originalModelScale;

        StopDustEffect();
        // Kill the looping hammering with a short fade first, then fire the
        // completion sting in 3D so it comes from the building.
        if (AudioManager.Instance != null)
        {
            if (buildSfxHandle != -1)
            {
                AudioManager.Instance.StopLoopingSFX(buildSfxHandle, 0.3f);
                buildSfxHandle = -1;
            }
            AudioManager.Instance.PlaySFX3D(AudioID.Camp_BuildDone, transform.position);
        }

        currentLevel = targetLevel;
        PlayerPrefs.SetInt("SaveBld_" + buildingID, currentLevel);
        PlayerPrefs.Save();

        // Achievement hooks per specific building milestone.
        if (!string.IsNullOrEmpty(buildingID))
        {
            string bid = buildingID.ToLowerInvariant();
            if ((bid.Contains("scoutslodge") || bid.Contains("lodge")) && currentLevel >= 2)
                AchievementSystem.Unlock("SCOUTS_MAP");
            if ((bid.Contains("storage") || bid.Contains("vault")) && currentLevel >= 1)
                AchievementSystem.Unlock("SUPPLY_LINES");
        }

        ApplyBuildingEffects();
        UpdateGlimmerState();

        isAnimating = false;

        if (playerInRange && levels != null && currentLevel < levels.Length)
        {
            OpenPanel();
        }
    }

    public int CollectResourcesByStorageNPC()
    {
        int amount = pendingResourcesCount;
        pendingResourcesCount = 0;
        HideAllVisualResources();
        return amount;
    }

    public void ShowNextVisualResource()
    {
        if (resourceVisuals == null || resourceVisuals.Length == 0) return;
        if (currentVisualIndex < resourceVisuals.Length)
        {
            if (resourceVisuals[currentVisualIndex] != null) resourceVisuals[currentVisualIndex].SetActive(true);
            currentVisualIndex++;
        }
    }

    public bool IsVisualsFull()
    {
        if (resourceVisuals == null || resourceVisuals.Length == 0) return false;
        return currentVisualIndex >= resourceVisuals.Length;
    }

    private void HideAllVisualResources()
    {
        currentVisualIndex = 0;
        if (resourceVisuals == null) return;
        foreach (var vis in resourceVisuals) if (vis != null) vis.SetActive(false);
    }

    private void ApplyBuildingEffects()
    {
        if (levels == null || levels.Length == 0 || currentLevel == 0) return;

        BuildingLevel currentData = levels[currentLevel - 1];
        if (isStorageVault && ResourceManager.Instance != null)
        {
            ResourceManager.Instance.SetExtraCapacity(currentData.productionValue);
        }
        else if (!isStorageVault)
        {
            if (productionCoroutine != null) StopCoroutine(productionCoroutine);
            productionCoroutine = StartCoroutine(ProductionRoutine(currentData.productionValue));
        }
    }

    private IEnumerator ProductionRoutine(int amountPerMinute)
    {
        if (amountPerMinute <= 0) yield break;
        while (true)
        {
            yield return new WaitForSeconds(60f);

            CampBuilding storage = FindStorageBuilding();
            hasStorageInCamp = (storage != null && storage.currentLevel > 0);

            if (hasStorageInCamp)
            {
                pendingResourcesCount += amountPerMinute;
            }
            else if (ResourceManager.Instance != null) // Безпечне додавання
            {
                if (productionType == ResourceType.Wood) ResourceManager.Instance.AddStashResources(amountPerMinute, 0, 0);
                else if (productionType == ResourceType.Food) ResourceManager.Instance.AddStashResources(0, 0, amountPerMinute);
                else if (productionType == ResourceType.Stone) ResourceManager.Instance.AddStashResources(0, amountPerMinute, 0);

                if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Camp_CollectItem);

                HideAllVisualResources();
            }
        }
    }

    private CampBuilding FindStorageBuilding()
    {
        if (storageBuildingCached != null) return storageBuildingCached;

        CampBuilding[] all = FindObjectsByType<CampBuilding>(FindObjectsSortMode.None);
        foreach (var b in all)
        {
            if (b.isStorageVault)
            {
                storageBuildingCached = b;
                return b;
            }
        }
        return null;
    }

    private void UpdateGlimmerState()
    {
        if (upgradeGlimmer != null)
        {
            if (levels == null || levels.Length == 0) return;

            bool canBeUpgraded = (currentLevel < levels.Length && !isAnimating);
            bool hasResources = false;

            if (PlayerPrefs.GetInt("IsUpgrading_" + buildingID, 0) == 1) canBeUpgraded = false;

            if (canBeUpgraded && ResourceManager.Instance != null)
            {
                BuildingLevel nextLevelData = levels[currentLevel];
                hasResources = ResourceManager.Instance.CanAffordStash(nextLevelData.costWood, nextLevelData.costStone, nextLevelData.costFood);
            }

            upgradeGlimmer.SetActive(canBeUpgraded && hasResources);
        }
    }

    // Exposed so companion panels (e.g. BarracksUpgradePanel) can drive
    // the same VFX beat as the generic hold-E upgrade flow instead of
    // rolling their own instant no-VFX path.
    public void StartDustEffect()
    {
        if (buildDustVFX != null)
        {
            buildDustVFX.gameObject.SetActive(true);
            var main = buildDustVFX.main;
            main.loop = true;
            if (!buildDustVFX.isPlaying) buildDustVFX.Play();
        }
    }

    private void StopDustEffect()
    {
        if (buildDustVFX != null)
        {
            buildDustVFX.Stop();
            buildDustVFX.gameObject.SetActive(false);
        }
    }
}