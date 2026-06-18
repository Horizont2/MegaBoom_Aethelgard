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

    private void Awake()
    {
        if (realModel != null)
        {
            originalModelPos = realModel.transform.localPosition;
            originalModelScale = realModel.transform.localScale;
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
            ghostModel.SetActive(true);
            realModel.SetActive(false);
        }
        else
        {
            ghostModel.SetActive(false);
            realModel.SetActive(true);
            realModel.transform.localPosition = originalModelPos;
        }
    }

    private void Update()
    {
        if (snowClumps != null && seasonManager != null)
        {
            bool isWinter = (seasonManager.currentSeason == Season.Winter);
            bool isBuilt = (currentLevel > 0);
            snowClumps.SetActive(isBuilt && isWinter && !isAnimating);
        }

        if (isAnimating) return;

        // --- 1. ПЕРЕВІРКА ЗАВЕРШЕННЯ АПГРЕЙДУ ---
        if (PlayerPrefs.GetInt("UpgradeFinished_" + buildingID, 0) == 1)
        {
            PlayerPrefs.SetInt("UpgradeFinished_" + buildingID, 0);
            StartCoroutine(CompleteUpgradeSequence(currentLevel + 1));
            return;
        }

        // --- 2. ДИНАМІЧНИЙ РУХ БУДІВЛІ ПІД ЧАС АПГРЕЙДУ ---
        if (PlayerPrefs.GetInt("IsUpgrading_" + buildingID, 0) == 1)
        {
            if (isPanelOpen) ClosePanel();

            if (buildDustVFX != null && !buildDustVFX.isPlaying) StartDustEffect();

            // Жорстко вимикаємо Glimmer під час будівництва
            if (upgradeGlimmer != null && upgradeGlimmer.activeSelf)
                upgradeGlimmer.SetActive(false);

            string startTimeStr = PlayerPrefs.GetString("UpgradeStart_" + buildingID, "");
            if (long.TryParse(startTimeStr, out long startTimeBin))
            {
                // Використовуємо UtcNow для ідеальної синхронізації з віджетом
                DateTime startTime = DateTime.FromBinary(startTimeBin);
                float elapsed = (float)(DateTime.UtcNow - startTime).TotalSeconds;

                if (currentLevel < levels.Length)
                {
                    float totalTime = levels[currentLevel].buildTime;

                    if (totalTime > 0)
                    {
                        float progress = Mathf.Clamp01(elapsed / totalTime);

                        // Для БУДЬ-ЯКОГО апгрейду показуємо Ghost, а Real виїжджає знизу
                        if (!ghostModel.activeSelf) ghostModel.SetActive(true);
                        if (!realModel.activeSelf) realModel.SetActive(true);

                        Vector3 startPos = originalModelPos - new Vector3(0, spawnDepth, 0);
                        realModel.transform.localPosition = Vector3.Lerp(startPos, originalModelPos, progress);
                    }
                }
            }
            return;
        }

        glimmerCheckTimer += Time.deltaTime;
        if (glimmerCheckTimer >= 1f)
        {
            glimmerCheckTimer = 0f;
            UpdateGlimmerState();
            if (isPanelOpen) UpdateUIData();
        }

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

        if (levels == null || levels.Length == 0 || currentLevel >= levels.Length)
        {
            if (isPanelOpen) ClosePanel();
            return;
        }

        if (Input.GetKeyDown(KeyCode.F))
        {
            if (isPanelOpen) ClosePanel();
            else OpenPanel();
        }

        if (isPanelOpen)
        {
            BuildingLevel nextLevelData = levels[currentLevel];
            bool canAfford = ResourceManager.Instance.CanAffordStash(nextLevelData.costWood, nextLevelData.costStone, nextLevelData.costFood);

            if (Input.GetKey(KeyCode.E) && canAfford)
            {
                currentHoldTime += Time.deltaTime;
                float fillRatio = currentHoldTime / holdTimeRequired;

                if (holdFillImage != null) holdFillImage.fillAmount = fillRatio;
                if (progressTMP != null) progressTMP.text = $"{(int)(fillRatio * 100)}%";

                if (currentHoldTime >= holdTimeRequired)
                {
                    currentHoldTime = 0f;
                    if (progressTMP != null) progressTMP.text = "100%";

                    ResourceManager.Instance.SpendStashResources(nextLevelData.costWood, nextLevelData.costStone, nextLevelData.costFood);

                    long startTimeBinary = DateTime.UtcNow.ToBinary();
                    PlayerPrefs.SetString("UpgradeStart_" + buildingID, startTimeBinary.ToString());
                    PlayerPrefs.SetInt("IsUpgrading_" + buildingID, 1);
                    PlayerPrefs.Save();

                    StartDustEffect();
                    if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Camp_BuildStart);

                    if (GlobalHUD.Instance != null)
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
                    float fillRatio = currentHoldTime / holdTimeRequired;

                    if (holdFillImage != null) holdFillImage.fillAmount = fillRatio;
                    if (progressTMP != null) progressTMP.text = $"{(int)(fillRatio * 100)}%";
                }
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player") && levels != null && currentLevel < levels.Length && !isAnimating)
        {
            playerInRange = true;
            if (GlobalHUD.Instance != null && !isPanelOpen && PlayerPrefs.GetInt("IsUpgrading_" + buildingID, 0) == 0)
            {
                GlobalHUD.Instance.ShowPrompt("[F] Inspect " + buildingName);
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

    private void OpenPanel()
    {
        isPanelOpen = true;
        if (AudioManager.Instance != null) AudioManager.Instance.PlayUI(AudioID.UI_Click);
        UpdateUIData();

        if (aaaPanel != null)
        {
            aaaPanel.SetActive(true);
            StartCoroutine(PopUpUI(aaaPanel.transform));
        }
        if (GlobalHUD.Instance != null) GlobalHUD.Instance.HidePrompt();
    }

    private void ClosePanel()
    {
        isPanelOpen = false;
        currentHoldTime = 0f;
        if (holdFillImage != null) holdFillImage.fillAmount = 0f;
        if (progressTMP != null) progressTMP.text = "0%";

        if (aaaPanel != null) aaaPanel.SetActive(false);

        if (playerInRange && GlobalHUD.Instance != null && currentLevel < levels.Length && !isAnimating && PlayerPrefs.GetInt("IsUpgrading_" + buildingID, 0) == 0)
        {
            GlobalHUD.Instance.ShowPrompt("[F] Inspect " + buildingName);
        }
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
        if (levels == null || levels.Length == 0 || currentLevel >= levels.Length) return;
        BuildingLevel nextLevelData = levels[currentLevel];

        if (titleTMP) titleTMP.text = buildingName.ToUpper();
        if (lvlTMP) lvlTMP.text = currentLevel == 0 ? "(Unbuilt)" : $"(Level {currentLevel})";
        if (descTMP) descTMP.text = description;

        if (buildingIconImage != null && buildingIconSprite != null)
            buildingIconImage.sprite = buildingIconSprite;

        string infoText = "";
        string prodLabel = (buildingID == "ScoutsLodge") ? "Feature" : "Production";

        if (currentLevel == 0)
        {
            infoText += $"{prodLabel}: <b><color=#FFFFFF>{nextLevelData.productionDescription}</color></b>\n";
            infoText += $"Build Time: <b><color=#FFFFFF>{nextLevelData.buildTime}s</color></b>";
            if (buildHintTMP) buildHintTMP.text = "HOLD [E] TO BUILD";
        }
        else
        {
            BuildingLevel currentData = levels[currentLevel - 1];
            infoText += $"{prodLabel}: <b><color=#AAAAAA>{currentData.productionDescription}</color></b> -> <b><color=#00FF00>{nextLevelData.productionDescription}</color></b>\n";
            infoText += $"Upgrade Time: <b><color=#FFFFFF>{nextLevelData.buildTime}s</color></b>";
            if (buildHintTMP) buildHintTMP.text = "HOLD [E] TO UPGRADE";
        }

        if (buildingName.ToUpper().Contains("FORGE"))
        {
            float currentMult = GetForgeMultiplier(currentLevel);
            float nextMult = GetForgeMultiplier(currentLevel + 1);
            int currentPower = PlayerPrefs.GetInt("PlayerTotalPower", 50);
            int basePower = Mathf.RoundToInt(currentPower / currentMult);
            int nextPower = Mathf.RoundToInt(basePower * nextMult);
            infoText += $"\n\nTotal Power: <b><color=#AAAAAA>{currentPower}</color></b> -> <b><color=#D4AF37>{nextPower} <size=70%>({nextMult * 100 - 100}%)</size></color></b>";
        }

        if (infoTMP) infoTMP.text = infoText;

        if (ResourceManager.Instance != null)
        {
            string woodColor = ResourceManager.Instance.stashWood >= nextLevelData.costWood ? "#FFFFFF" : "#FF4444";
            string stoneColor = ResourceManager.Instance.stashStone >= nextLevelData.costStone ? "#FFFFFF" : "#FF4444";
            string foodColor = ResourceManager.Instance.stashFood >= nextLevelData.costFood ? "#FFFFFF" : "#FF4444";

            if (costWoodTMP) costWoodTMP.text = $"<color=#CCCCCC>WOOD</color>\n<size=130%><color={woodColor}>{nextLevelData.costWood}</color></size>";
            if (costStoneTMP) costStoneTMP.text = $"<color=#CCCCCC>STONE</color>\n<size=130%><color={stoneColor}>{nextLevelData.costStone}</color></size>";
            if (costFoodTMP) costFoodTMP.text = $"<color=#CCCCCC>FOOD</color>\n<size=130%><color={foodColor}>{nextLevelData.costFood}</color></size>";
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

        ghostModel.SetActive(false);
        realModel.SetActive(true);
        realModel.transform.localPosition = originalModelPos;

        float popTimer = 0f;
        float popDuration = 0.5f;
        Vector3 bounceScale = originalModelScale * upgradeBounceAmount;

        while (popTimer < popDuration)
        {
            popTimer += Time.deltaTime;
            float progress = popTimer / popDuration;
            float scaleCurve = Mathf.PingPong(progress * 2f, 1f);
            realModel.transform.localScale = Vector3.Lerp(originalModelScale, bounceScale, Mathf.SmoothStep(0f, 1f, scaleCurve));
            yield return null;
        }
        realModel.transform.localScale = originalModelScale;

        StopDustEffect();
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Camp_BuildDone);

        currentLevel = targetLevel;
        PlayerPrefs.SetInt("SaveBld_" + buildingID, currentLevel);
        PlayerPrefs.Save();

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
            else
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
        CampBuilding[] all = FindObjectsByType<CampBuilding>(FindObjectsSortMode.None);
        foreach (var b in all) if (b.isStorageVault) return b;
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

    private void StartDustEffect()
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
            var main = buildDustVFX.main;
            main.loop = false;
            buildDustVFX.Stop();
        }
    }
}