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

    // The [F] prompt, built once per language rather than once per frame.
    private string _promptCache;
    private int _promptLang = -1;

    // ==== TWO PLAYERPREFS READS A FRAME, NINE BUILDINGS OVER ====
    //
    // PlayerPrefs.GetInt is a native call that hashes the key string on
    // every lookup, and Update read the finished/upgrading flags (plus the
    // start-time STRING while building) every single frame on each of the
    // nine camp buildings. None of those values can change more than once
    // per build, and a build takes minutes.
    //
    // Polled five times a second instead, and force-refreshed the moment
    // this component writes one of them, so nothing reacts late to its own
    // change.
    private bool _flagFinished;
    private bool _flagUpgrading;
    private long _flagStartBin;
    private bool _flagStartValid;
    private float _flagsPollAt = -1f;
    private const float FLAG_POLL = 0.2f;

    private void RefreshUpgradeFlags(bool force = false)
    {
        if (!force && Time.unscaledTime < _flagsPollAt) return;
        _flagsPollAt = Time.unscaledTime + FLAG_POLL;

        _flagFinished = PlayerPrefs.GetInt(ppKey_UpgradeFinished, 0) == 1;
        _flagUpgrading = PlayerPrefs.GetInt(ppKey_IsUpgrading, 0) == 1;
        _flagStartValid = _flagUpgrading &&
                          long.TryParse(PlayerPrefs.GetString(ppKey_UpgradeStart, ""), out _flagStartBin);
    }

    // ==== productionValue MEANS DIFFERENT THINGS TO DIFFERENT BUILDINGS ====
    //
    // This branch used to be "storage vault, or else produce" — so every
    // building that was not a vault trickled productionValue of productionType
    // into the stash every minute. For the Hunter and the Lumberjack that is
    // exactly right and is what the field is for.
    //
    // For the Forge, productionValue holds the DAMAGE PERCENTAGES (2, 5, 8, 11,
    // 15) and productionType was never set, so it defaulted to Wood: the Forge
    // quietly paid 2 to 15 wood a minute. The Barracks holds RECRUIT TIERS (1
    // to 5) and did the same. Neither panel mentions income, and the numbers
    // arriving were percentages and tiers.
    //
    // Somebody hit this once before: the Scout's Lodge instance in CampScene has
    // all five productionValues zeroed by hand, which makes ProductionRoutine
    // bail on its first line. That is a workaround at one call site; this is the
    // rule. Defaults to true so every existing producer keeps producing.
    [Tooltip("Does this building actually add resources to the stash every minute? OFF for buildings whose productionValue means something else — the Forge stores damage percentages in it, the Barracks stores recruit tiers.")]
    public bool producesResource = true;

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
        // The name + description are set in code via Tr(...) each refresh.
        // Exclude them from AutoLocalize so it can't re-capture the baked English
        // text and fight the translated value (descriptions showing in English).
        MarkNoAutoLocalize(titleTMP);
        MarkNoAutoLocalize(descTMP);
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

    // Also stop the loop if the building is merely DISABLED (not destroyed),
    // e.g. swapped out or the camp view toggled — otherwise the hammering can
    // outlive the object.
    private void OnDisable()
    {
        if (buildSfxHandle != -1 && AudioManager.Instance != null)
        {
            AudioManager.Instance.StopLoopingSFX(buildSfxHandle, 0f);
            buildSfxHandle = -1;
        }
    }

    // Best anchor for the building's 3D audio: the actual visible model, whose
    // pivot sits at the building — the ROOT transform is often centred on the
    // slot/plot, offset from the structure, so sound played there sat away from
    // where the building visually is.
    private Transform BuildingAudioAnchor()
    {
        if (realModel != null) return realModel.transform;
        if (pickupPoint != null) return pickupPoint;
        return transform;
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

            // Nothing in the project ever reported BuildStructures progress, so
            // any "build / expand the camp" mission could be completed in the
            // world and still never tick. This is the only place a build
            // actually finishes.
            if (MissionManager.Instance != null)
                MissionManager.Instance.AddProgress(MissionType.BuildStructures, 1);
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
            RefreshUpgradeFlags();
            if (_flagFinished)
            {
                PlayerPrefs.SetInt(ppKey_UpgradeFinished, 0);
                RefreshUpgradeFlags(true);
                StartCoroutine(CompleteUpgradeSequence(currentLevel + 1));
                return;
            }

            // 4. Активний процес апгрейду (анімація побудови)
            if (_flagUpgrading)
            {
                if (isPanelOpen) ClosePanel();

                if (buildDustVFX != null && !buildDustVFX.isPlaying) StartDustEffect();
                if (upgradeGlimmer != null && upgradeGlimmer.activeSelf) upgradeGlimmer.SetActive(false);

                if (_flagStartValid)
                {
                    DateTime startTime = DateTime.FromBinary(_flagStartBin);
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

                            // SELF-COMPLETE once the build time has elapsed. The
                            // completion used to depend solely on GlobalHUD's
                            // upgrade tracker firing UpgradeFinished — but that
                            // tracker is lost on a scene reload, which left the
                            // building frozen mid-build forever ("stays unbuilt").
                            // Now the building finishes itself from its own saved
                            // timestamp regardless of the HUD.
                            if (progress >= 1f)
                            {
                                PlayerPrefs.SetInt(ppKey_UpgradeFinished, 1);
                                PlayerPrefs.SetInt(ppKey_IsUpgrading, 0);
                                PlayerPrefs.Save();
                                RefreshUpgradeFlags(true);
                            }
                        }
                        else
                        {
                            // A zero/negative build time must not strand the build.
                            PlayerPrefs.SetInt(ppKey_UpgradeFinished, 1);
                            PlayerPrefs.SetInt(ppKey_IsUpgrading, 0);
                            PlayerPrefs.Save();
                            RefreshUpgradeFlags(true);
                        }
                    }
                }
                return;
            }

            // Catch-all: if we reach the idle steady-state (not animating, not
            // pending-finish, not upgrading) with the construction loop still
            // live, stop it — it must never outlive its build.
            if (buildSfxHandle != -1 && AudioManager.Instance != null)
            {
                AudioManager.Instance.StopLoopingSFX(buildSfxHandle, 0.2f);
                buildSfxHandle = -1;
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
            if (!isPanelOpen && !_flagUpgrading)
            {
                if (GlobalHUD.Instance != null)
                {
                    // ==== A FORMATTED STRING, EVERY FRAME, NINE TIMES OVER ====
                    //
                    // Tr(key, args) allocates a params object[] AND a
                    // string.Format result on every call, and ShowPrompt then
                    // runs the gamepad glyph pass - a chain of Regex.Replace on
                    // a connected controller - before its own early-out can
                    // discard any of it. All of that ran every frame the player
                    // stood near a building, on each of the nine CampBuilding
                    // instances in the camp.
                    //
                    // The text only changes when the building name or the
                    // language does, and neither changes per frame.
                    if (_promptCache == null || _promptLang != LocalizationManager.CurrentLanguage)
                    {
                        _promptLang = LocalizationManager.CurrentLanguage;
                        _promptCache = LocalizationManager.Tr("PROMPT_INSPECT_BUILDING",
                                                              LocalizationManager.Tr(buildingName));
                    }
                    GlobalHUD.Instance.ShowPrompt(_promptCache);
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
                        // Purchase confirmation ding at the moment resources are spent
                        // (the Camp_BuildStart loop that follows is the construction SFX).
                        if (AudioManager.Instance != null) AudioManager.Instance.PlayUI(AudioID.UI_Purchase);

                        long startTimeBinary = DateTime.UtcNow.ToBinary();
                        PlayerPrefs.SetString("UpgradeStart_" + buildingID, startTimeBinary.ToString());
                        PlayerPrefs.SetInt("IsUpgrading_" + buildingID, 1);
                        PlayerPrefs.Save();
                        RefreshUpgradeFlags(true);

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
                            buildSfxHandle = AudioManager.Instance.PlayLoopingSFX3D(AudioID.Camp_BuildStart, BuildingAudioAnchor());
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

    // ==== ONE OWNER FOR THE FORGE BONUS ====
    //
    // The Forge's +15% weapon damage was never applied to anything. Its
    // buildingID is "Forge_01", so its level saves to "SaveBld_Forge_01" — but
    // both readers, PlayerController and PowerSystemManager, asked for
    // "SaveBld_Forge". Nothing in the project has ever written that key, so the
    // level came back 0 and the bonus was always zero. The most expensive
    // building in camp — 900 wood, 900 stone, 375 food to max — did nothing,
    // while its panel, its first-open hint and seven localisations all promised
    // a damage increase.
    //
    // Two copies of the curve existed as well: this one, which nothing called,
    // and a switch inside PlayerController. Both are replaced by these, so the
    // key is spelled once and the numbers live in one place.
    public const string ForgeBuildingID = "Forge_01";

    public static string SaveKeyFor(string buildingID) => "SaveBld_" + buildingID;

    public static int LevelOf(string buildingID) =>
        string.IsNullOrEmpty(buildingID) ? 0 : PlayerPrefs.GetInt(SaveKeyFor(buildingID), 0);

    public static int ForgeLevel => LevelOf(ForgeBuildingID);

    // ==== HOW MUCH BUILDING IS ACTUALLY LEFT ====
    //
    // A "build N structures" mission is the only kind whose supply is finite.
    // The camp has six buildings of five levels each, so a save has exactly
    // thirty upgrades in it and not one more — and once they are spent, a board
    // that hands out another build mission has handed out a mission that can
    // never be completed. It then sits in one of the three active slots for the
    // rest of the save, and since the board refuses to restock while three are
    // active, it takes the whole mission system down with it.
    //
    // Counted from the live buildings rather than a hardcoded list, so adding a
    // building to the camp needs no second edit here. Both are camp-scene
    // objects, which is also where the notice board lives.
    public static int RemainingUpgrades
    {
        get
        {
            int left = 0;
            foreach (var b in UnityEngine.Object.FindObjectsByType<CampBuilding>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (b == null || b.levels == null) continue;
                left += Mathf.Max(0, b.levels.Length - LevelOf(b.buildingID));
            }
            return left;
        }
    }

    /// Upgrades the player has already finished across the whole camp, 0..30.
    /// Build costs climb steeply with level while the board's reward multiplier
    /// tracks conquered regions, so this is what a build mission's pay should
    /// actually follow.
    public static int UpgradesCompleted
    {
        get
        {
            int done = 0;
            foreach (var b in UnityEngine.Object.FindObjectsByType<CampBuilding>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (b == null || b.levels == null) continue;
                done += Mathf.Clamp(LevelOf(b.buildingID), 0, b.levels.Length);
            }
            return done;
        }
    }

    /// Additive damage bonus from the Forge: 0 at level 0, 0.15 at level 5.
    /// These are the same percentages the building's own panel advertises.
    public static float ForgeDamageBonus => ForgeLevel switch
    {
        1 => 0.02f,
        2 => 0.05f,
        3 => 0.08f,
        4 => 0.11f,
        5 => 0.15f,
        _ => 0f,
    };

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

            infoText += $" → <b><color=#A8E6CF>{LocalizationManager.Tr(nextLevelData.productionDescription)}</color></b>\n";
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
            AudioManager.Instance.PlaySFX3D(AudioID.Camp_BuildDone, BuildingAudioAnchor().position);
        }

        currentLevel = targetLevel;
        PlayerPrefs.SetInt("SaveBld_" + buildingID, currentLevel);
        PlayerPrefs.Save();

        // ==== THE BUILD MISSION WAS CREDITED IN THE WRONG PLACE ====
        //
        // BuildStructures progress was reported from the LOAD path, which reads
        // UpgradeFinished_<id> when the camp scene comes up and ticks the
        // mission if it finds the flag still set. But Update() consumes that
        // same flag the moment the build timer runs out — it clears it and
        // starts this sequence — so in the ordinary case, where the player is
        // standing in the camp watching their building go up, the flag is
        // already zero by the time anything reads it for missions. The load
        // path then found nothing on the next visit either, because it had been
        // cleared in the previous session.
        //
        // The result: "build 1 building" could never be completed by building a
        // building. This IS the completion, so the credit belongs here. The
        // load path stays as the fallback for a build that finished while the
        // camp scene was unloaded; the two are mutually exclusive, because
        // whichever one sees the flag clears it.
        if (MissionManager.Instance != null)
            MissionManager.Instance.AddProgress(MissionType.BuildStructures, 1);

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
        else if (!isStorageVault && producesResource)
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

            bool shouldShow = canBeUpgraded && hasResources;
            if (shouldShow && !upgradeGlimmer.activeSelf)
            {
                upgradeGlimmer.SetActive(true);
                RestartGlimmerVFX();   // actually PLAY it — see below

                // "You can afford this now." The glimmer appearing is the only
                // notice the player gets that a building became upgradable, and
                // it happens while they are usually looking somewhere else —
                // walking back into camp with a full stash. A quiet chime from
                // the building itself is what makes it noticeable without a
                // popup. 3D and positional, so it also says WHICH building.
                //
                // Only after the camp has settled: on load every affordable
                // building turns its glimmer on in the same frame, and a dozen
                // chimes at once is an alarm, not a notification.
                if (AudioManager.Instance != null && Time.timeSinceLevelLoad > 3f)
                    AudioManager.Instance.PlaySFX3D(AudioID.Camp_UpgradeReady, BuildingAudioAnchor().position);
            }
            else if (!shouldShow && upgradeGlimmer.activeSelf)
            {
                upgradeGlimmer.SetActive(false);
            }
            else if (shouldShow)
            {
                // Already visible. Something still has to keep it MOVING — see
                // EnsureGlimmerDriver.
                EnsureGlimmerDriver();
            }
        }
    }

    // The glimmer "froze" — it sat on a single frame instead of animating —
    // because a non-looping ParticleSystem (or an idle Animator) only plays
    // once when the object is first shown, and re-showing an already-active
    // object is a no-op. Force its VFX to loop and (re)start every time the
    // glimmer becomes visible so it always animates.
    private void RestartGlimmerVFX()
    {
        if (upgradeGlimmer == null) return;
        foreach (var ps in upgradeGlimmer.GetComponentsInChildren<ParticleSystem>(true))
        {
            var main = ps.main;
            main.loop = true;
            ps.Clear(true);
            ps.Play(true);
        }
        EnsureGlimmerDriver();
    }

    // ==== IT WARNS, IT NO LONGER TAKES OVER ====
    //
    // The six UpgradeGlimmer controllers all had their state motions set to a
    // clip inside Peasant Nolant(Free Version).fbx — a humanoid NPC animation
    // on an object that is a Light with no bones — while the six GlimmerSweep
    // .anim files that were actually authored for them, animating
    // m_LocalPosition and m_Intensity on a loop, were referenced by nothing.
    // That is why the light sat in one place.
    //
    // The first attempt at this disabled the Animator and drove the light from
    // code instead. That was the wrong call: it produced movement, but movement
    // along a path nobody had designed, silently replacing hand-authored
    // animation with a procedural guess. The controllers are repaired now and
    // the Animator runs the authored clips again.
    //
    // What stays is the DETECTION, as a warning only. An Animator whose every
    // clip is humanoid motion, on an object with no Avatar, provably cannot
    // animate it — and that is worth saying out loud once, because the failure
    // is completely silent otherwise. It no longer changes anything.
    private bool _warnedGlimmerAnimator;

    private void EnsureGlimmerDriver()
    {
        if (upgradeGlimmer == null || _warnedGlimmerAnimator) return;

        foreach (var an in upgradeGlimmer.GetComponentsInChildren<Animator>(true))
        {
            if (an == null || !CannotPossiblyAnimate(an)) continue;
            _warnedGlimmerAnimator = true;
            Debug.LogWarning($"[Camp] The glimmer on '{buildingID}' has an Animator whose clips are all humanoid " +
                             "motion, on an object with no Avatar — it cannot animate anything and the light will " +
                             "sit still. Point its controller states at the GlimmerSweep clips.", upgradeGlimmer);
        }
    }

    private static bool CannotPossiblyAnimate(Animator an)
    {
        if (an.runtimeAnimatorController == null) return false;
        if (an.avatar != null) return false;

        var clips = an.runtimeAnimatorController.animationClips;
        if (clips == null || clips.Length == 0) return false;
        foreach (var c in clips)
            if (c != null && !c.isHumanMotion) return false;   // something here could bind
        return true;
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