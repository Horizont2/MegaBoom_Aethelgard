using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

[RequireComponent(typeof(CanvasGroup))]
public class MapPanelUI : MonoBehaviour
{
    [Header("UI Elements - Text")]
    public TextMeshProUGUI regionNameText;
    public TextMeshProUGUI descriptionText;
    public TextMeshProUGUI recommendedPowerText;
    public TextMeshProUGUI playerPowerText;

    [Header("UI Elements - One-Time Rewards (Conquer)")]
    public GameObject conquerTitleObj;
    public GameObject conquerContainerObj;
    public TextMeshProUGUI conquerWoodText;
    public TextMeshProUGUI conquerStoneText;
    public TextMeshProUGUI conquerFoodText;
    public TextMeshProUGUI conquerDiamondText;

    [Header("UI Elements - Passive Rewards")]
    public TextMeshProUGUI passiveWoodText;
    public TextMeshProUGUI passiveStoneText;
    public TextMeshProUGUI passiveFoodText;
    public TextMeshProUGUI passiveDiamondText;

    [Header("Reward Containers & Icons (NEW)")]
    // Контейнери (WoodItem, StoneItem...) - ПЕРЕТЯГНИ СЮДИ БАТЬКІВСЬКІ БЛОКИ!
    public GameObject passiveWoodItem;
    public GameObject passiveStoneItem;
    public GameObject passiveFoodItem;
    public GameObject passiveDiamondItem;

    public GameObject conquerWoodItem;
    public GameObject conquerStoneItem;
    public GameObject conquerFoodItem;
    public GameObject conquerDiamondItem;

    public GameObject upgWoodItem;
    public GameObject upgStoneItem;
    public GameObject upgFoodItem;

    // Самі картинки (ResourceSprite) - ПЕРЕТЯГНИ СЮДИ КАРТИНКИ!
    public Image passiveWoodIcon;
    public Image passiveStoneIcon;
    public Image passiveFoodIcon;
    public Image passiveDiamondIcon;

    public Image conquerWoodIcon;
    public Image conquerStoneIcon;
    public Image conquerFoodIcon;
    public Image conquerDiamondIcon;

    public Image upgWoodIcon;
    public Image upgStoneIcon;
    public Image upgFoodIcon;

    [Header("UI Elements - Graphics & Buttons")]
    public Image illustrationImage;
    public Button actionButton;
    public TextMeshProUGUI actionButtonText;
    public Button closeButton;

    [Header("Region Upgrade UI")]
    public Button upgradeButton;
    public TextMeshProUGUI upgradeButtonText;
    public GameObject upgradeTitleObj;
    public GameObject upgradeCostRoot;
    public TextMeshProUGUI upgWoodCostText;
    public TextMeshProUGUI upgStoneCostText;
    public TextMeshProUGUI upgFoodCostText;
    public TextMeshProUGUI upgMaxLevelText;

    [Header("Dynamic UI Colors")]
    public Color affordableColor = new Color(0.298f, 0.686f, 0.314f, 1f);
    public Color unaffordableColor = new Color(0.957f, 0.263f, 0.212f, 1f);
    public Color btnTextAffordableColor = Color.white;
    public Color btnTextUnaffordableColor = new Color(0.6f, 0.6f, 0.6f, 1f);

    [Header("AAA Juice Effects")]
    public ParticleSystem upgradeSuccessVFX;
    public float shakeIntensity = 8f;
    public float shakeDuration = 0.2f;

    [Header("Animation Settings")]
    public RectTransform panelRect;
    public float animationDuration = 0.3f;
    public float startOffset = 200f;
    public float edgePadding = 15f;

    [Header("Zoom Transition Effects")]
    public Image fullScreenCloudOverlay;
    public MapInteractiveViewer mapViewer;

    private CanvasGroup canvasGroup;
    private RegionData currentRegion;
    private Coroutine animationCoroutine;
    private bool isConfirmingUpgrade = false;
    private Vector3 currentRegionPos;

    public RegionData GetCurrentRegion() { return currentRegion; }
    public bool IsPanelOpen() { return canvasGroup.interactable; }

    void Awake()
    {
        canvasGroup = GetComponent<CanvasGroup>();
        if (panelRect == null) panelRect = GetComponent<RectTransform>();

        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;

        if (actionButton != null) actionButton.onClick.AddListener(OnActionButtonClicked);
        if (closeButton != null) closeButton.onClick.AddListener(ClosePanel);
        if (upgradeButton != null) upgradeButton.onClick.AddListener(OnUpgradeButtonClicked);
    }

    public void OpenPanel(RegionData region, Vector3 regionWorldPosition)
    {
        isConfirmingUpgrade = false;
        ToggleUpgradeFocus(false);
        currentRegion = region;
        currentRegionPos = regionWorldPosition;
        PopulateData();

        Vector3 viewportPos = Camera.main.WorldToViewportPoint(regionWorldPosition);
        bool putPanelOnRight = viewportPos.x < 0.5f;

        Vector2 hiddenPos, visiblePos;
        Vector2 cachedSize = panelRect.rect.size;

        if (putPanelOnRight)
        {
            panelRect.anchorMin = new Vector2(1, 0.5f);
            panelRect.anchorMax = new Vector2(1, 0.5f);
            panelRect.pivot = new Vector2(1, 0.5f);
            visiblePos = new Vector2(-edgePadding, 0);
            hiddenPos = new Vector2(cachedSize.x + startOffset, 0);
        }
        else
        {
            panelRect.anchorMin = new Vector2(0, 0.5f);
            panelRect.anchorMax = new Vector2(0, 0.5f);
            panelRect.pivot = new Vector2(0, 0.5f);
            visiblePos = new Vector2(edgePadding, 0);
            hiddenPos = new Vector2(-cachedSize.x - startOffset, 0);
        }

        panelRect.sizeDelta = cachedSize;
        panelRect.anchoredPosition = hiddenPos;

        if (animationCoroutine != null) StopCoroutine(animationCoroutine);
        animationCoroutine = StartCoroutine(AnimatePanel(visiblePos, 1f, true));
    }

    public void ClosePanel()
    {
        if (currentRegion == null) return;
        ToggleUpgradeFocus(false);

        float width = panelRect.rect.width;
        Vector2 hiddenPos = panelRect.pivot.x > 0.5f
            ? new Vector2(width + startOffset, 0)
            : new Vector2(-width - startOffset, 0);

        if (animationCoroutine != null) StopCoroutine(animationCoroutine);
        animationCoroutine = StartCoroutine(AnimatePanel(hiddenPos, 0f, false));
    }

    private void PopulateData()
    {
        if (regionNameText != null) regionNameText.text = currentRegion.regionName.ToUpper();
        if (descriptionText != null) descriptionText.text = currentRegion.loreDescription;
        if (upgradeButtonText != null) upgradeButtonText.text = LocalizationManager.Tr("UPGRADE");

        if (illustrationImage != null)
        {
            if (currentRegion.regionIllustration != null)
            {
                illustrationImage.sprite = currentRegion.regionIllustration;
                illustrationImage.gameObject.SetActive(true);
            }
            else illustrationImage.gameObject.SetActive(false);
        }

        int currentLevel = PlayerPrefs.GetInt("RegionLevel_" + currentRegion.regionID, 1);

        // Примусово вмикаємо ВСІ картинки, щоб вони не зникали
        if (passiveWoodIcon) { passiveWoodIcon.gameObject.SetActive(true); passiveWoodIcon.enabled = true; }
        if (passiveStoneIcon) { passiveStoneIcon.gameObject.SetActive(true); passiveStoneIcon.enabled = true; }
        if (passiveFoodIcon) { passiveFoodIcon.gameObject.SetActive(true); passiveFoodIcon.enabled = true; }
        if (passiveDiamondIcon) { passiveDiamondIcon.gameObject.SetActive(true); passiveDiamondIcon.enabled = true; }

        if (conquerWoodIcon) { conquerWoodIcon.gameObject.SetActive(true); conquerWoodIcon.enabled = true; }
        if (conquerStoneIcon) { conquerStoneIcon.gameObject.SetActive(true); conquerStoneIcon.enabled = true; }
        if (conquerFoodIcon) { conquerFoodIcon.gameObject.SetActive(true); conquerFoodIcon.enabled = true; }
        if (conquerDiamondIcon) { conquerDiamondIcon.gameObject.SetActive(true); conquerDiamondIcon.enabled = true; }

        if (upgWoodIcon) { upgWoodIcon.gameObject.SetActive(true); upgWoodIcon.enabled = true; }
        if (upgStoneIcon) { upgStoneIcon.gameObject.SetActive(true); upgStoneIcon.enabled = true; }
        if (upgFoodIcon) { upgFoodIcon.gameObject.SetActive(true); upgFoodIcon.enabled = true; }

        if (currentRegion.upgradeLevels != null && currentRegion.upgradeLevels.Length > 0)
        {
            RegionLevelData levelData = currentRegion.upgradeLevels[currentLevel - 1];

            if (!isConfirmingUpgrade)
            {
                if (passiveWoodText != null) passiveWoodText.text = $"+{levelData.passiveWood}/hr";
                if (passiveStoneText != null) passiveStoneText.text = $"+{levelData.passiveStone}/hr";
                if (passiveFoodText != null) passiveFoodText.text = $"+{levelData.passiveFood}/hr";
                if (passiveDiamondText != null) passiveDiamondText.text = $"+{levelData.passiveDiamonds}/hr";

                // Вмикаємо/Вимикаємо ЦІЛІ БЛОКИ (зсув списку)
                if (passiveWoodItem != null) passiveWoodItem.SetActive(levelData.passiveWood > 0);
                if (passiveStoneItem != null) passiveStoneItem.SetActive(levelData.passiveStone > 0);
                if (passiveFoodItem != null) passiveFoodItem.SetActive(levelData.passiveFood > 0);
                if (passiveDiamondItem != null) passiveDiamondItem.SetActive(levelData.passiveDiamonds > 0);
            }
        }

        switch (currentRegion.currentState)
        {
            case RegionState.Locked:
            case RegionState.Available:
                if (conquerTitleObj) conquerTitleObj.SetActive(true);
                if (conquerContainerObj) conquerContainerObj.SetActive(true);

                if (upgradeTitleObj) upgradeTitleObj.SetActive(false);
                if (upgradeCostRoot) upgradeCostRoot.SetActive(false);
                if (upgMaxLevelText) upgMaxLevelText.gameObject.SetActive(false);
                if (upgradeButton) upgradeButton.gameObject.SetActive(false);

                if (actionButtonText != null) actionButtonText.text = currentRegion.currentState == RegionState.Locked ? LocalizationManager.Tr("AREA LOCKED") : LocalizationManager.Tr("START JOURNEY");
                if (actionButton != null) actionButton.interactable = currentRegion.currentState == RegionState.Available;
                break;

            case RegionState.Conquered:
                if (conquerTitleObj) conquerTitleObj.SetActive(false);
                if (conquerContainerObj) conquerContainerObj.SetActive(false);

                if (actionButtonText != null) actionButtonText.text = LocalizationManager.Tr("TRAVEL");
                if (actionButton != null) actionButton.interactable = !isConfirmingUpgrade;
                if (upgradeButton) upgradeButton.gameObject.SetActive(true);

                UpdateUpgradeUI(currentLevel);
                break;
        }

        if (conquerWoodText != null) conquerWoodText.text = $"+{currentRegion.woodReward}";
        if (conquerStoneText != null) conquerStoneText.text = $"+{currentRegion.stoneReward}";
        if (conquerFoodText != null) conquerFoodText.text = $"+{currentRegion.foodReward}";
        if (conquerDiamondText != null) conquerDiamondText.text = $"+{currentRegion.diamondReward}";

        // Вмикаємо/Вимикаємо ЦІЛІ БЛОКИ (зсув списку)
        if (conquerWoodItem != null) conquerWoodItem.SetActive(currentRegion.woodReward > 0);
        if (conquerStoneItem != null) conquerStoneItem.SetActive(currentRegion.stoneReward > 0);
        if (conquerFoodItem != null) conquerFoodItem.SetActive(currentRegion.foodReward > 0);
        if (conquerDiamondItem != null) conquerDiamondItem.SetActive(currentRegion.diamondReward > 0);

        int currentPlayerPower = PlayerPrefs.GetInt("PlayerTotalPower", 70);

        string powerColor = (currentPlayerPower >= currentRegion.recommendedPower) ? ColorToHex(affordableColor) : ColorToHex(unaffordableColor);
        if (recommendedPowerText != null) recommendedPowerText.text = $"<size=50%><color=#D4AF37>{LocalizationManager.Tr("RECOMMENDED")}</color></size>\n{currentRegion.recommendedPower}";
        if (playerPowerText != null) playerPowerText.text = $"<size=50%><color=#D4AF37>{LocalizationManager.Tr("YOUR POWER")}</color></size>\n<color={powerColor}>{currentPlayerPower}</color>";
    }

    private void UpdateUpgradeUI(int currentLevel)
    {
        if (currentLevel < 5)
        {
            if (upgradeTitleObj) upgradeTitleObj.SetActive(true);
            if (upgradeCostRoot) upgradeCostRoot.SetActive(true);
            if (upgMaxLevelText) upgMaxLevelText.gameObject.SetActive(false);

            RegionLevelData nextLevelData = currentRegion.upgradeLevels[currentLevel];

            bool canAfford = ResourceManager.Instance != null && ResourceManager.Instance.CanAffordStash(nextLevelData.costWood, nextLevelData.costStone, nextLevelData.costFood);
            if (upgradeButton) upgradeButton.interactable = canAfford;

            if (upgradeButtonText != null && !isConfirmingUpgrade)
            {
                upgradeButtonText.color = canAfford ? btnTextAffordableColor : btnTextUnaffordableColor;
            }

            if (ResourceManager.Instance != null)
            {
                string wCol = ResourceManager.Instance.stashWood >= nextLevelData.costWood ? ColorToHex(affordableColor) : ColorToHex(unaffordableColor);
                string sCol = ResourceManager.Instance.stashStone >= nextLevelData.costStone ? ColorToHex(affordableColor) : ColorToHex(unaffordableColor);
                string fCol = ResourceManager.Instance.stashFood >= nextLevelData.costFood ? ColorToHex(affordableColor) : ColorToHex(unaffordableColor);

                if (upgWoodCostText) upgWoodCostText.text = $"<color={wCol}>{nextLevelData.costWood}</color>";
                if (upgStoneCostText) upgStoneCostText.text = $"<color={sCol}>{nextLevelData.costStone}</color>";
                if (upgFoodCostText) upgFoodCostText.text = $"<color={fCol}>{nextLevelData.costFood}</color>";
            }

            // Вмикаємо/Вимикаємо блоки вартості апгрейду
            if (upgWoodItem != null) upgWoodItem.SetActive(nextLevelData.costWood > 0);
            if (upgStoneItem != null) upgStoneItem.SetActive(nextLevelData.costStone > 0);
            if (upgFoodItem != null) upgFoodItem.SetActive(nextLevelData.costFood > 0);
        }
        else
        {
            if (upgradeTitleObj) upgradeTitleObj.SetActive(false);
            if (upgradeCostRoot) upgradeCostRoot.SetActive(false);
            if (upgMaxLevelText)
            {
                upgMaxLevelText.gameObject.SetActive(true);
                upgMaxLevelText.text = LocalizationManager.Tr("MAX LEVEL REACHED");
            }
            if (upgradeButton) upgradeButton.interactable = false;
        }
    }

    private void OnUpgradeButtonClicked()
    {
        int currentLevel = PlayerPrefs.GetInt("RegionLevel_" + currentRegion.regionID, 1);

        if (currentLevel < 5 && ResourceManager.Instance != null)
        {
            RegionLevelData nextLevelData = currentRegion.upgradeLevels[currentLevel];

            if (ResourceManager.Instance.CanAffordStash(nextLevelData.costWood, nextLevelData.costStone, nextLevelData.costFood))
            {
                if (!isConfirmingUpgrade)
                {
                    isConfirmingUpgrade = true;
                    ToggleUpgradeFocus(true);

                    if (AudioManager.Instance != null) AudioManager.Instance.PlayUI(AudioID.UI_Click);

                    if (upgradeButtonText != null)
                    {
                        upgradeButtonText.text = "<color=#FFD700>CONFIRM</color>";
                        upgradeButtonText.color = btnTextAffordableColor;
                    }
                    if (actionButton != null) actionButton.interactable = false;

                    ShowUpgradePreview(currentLevel);
                }
                else
                {
                    if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Camp_BuildDone);
                    if (upgradeSuccessVFX != null) upgradeSuccessVFX.Play();
                    StartCoroutine(ShakePanel());

                    ResourceManager.Instance.SpendStashResources(nextLevelData.costWood, nextLevelData.costStone, nextLevelData.costFood);
                    PlayerPrefs.SetInt("RegionLevel_" + currentRegion.regionID, currentLevel + 1);
                    PlayerPrefs.Save();

                    isConfirmingUpgrade = false;
                    ToggleUpgradeFocus(false);
                    PopulateData();

                    if (MapProgressionManager.Instance != null) MapProgressionManager.Instance.RefreshMapState();
                }
            }
            else
            {
                if (AudioManager.Instance != null) AudioManager.Instance.PlayUI(AudioID.UI_Error);
            }
        }
    }

    private void ToggleUpgradeFocus(bool isFocused)
    {
        float targetAlpha = isFocused ? 0.3f : 1f;
        Color dimColor = new Color(1f, 1f, 1f, targetAlpha);

        if (illustrationImage != null) illustrationImage.color = dimColor;
        if (descriptionText != null) descriptionText.color = dimColor;
        if (recommendedPowerText != null) recommendedPowerText.alpha = targetAlpha;
        if (playerPowerText != null) playerPowerText.alpha = targetAlpha;
    }

    private void ShowUpgradePreview(int currentLevel)
    {
        RegionLevelData currentData = currentRegion.upgradeLevels[currentLevel - 1];
        RegionLevelData nextData = currentRegion.upgradeLevels[currentLevel];

        string arrowCol = ColorToHex(affordableColor);

        if (passiveWoodText) passiveWoodText.text = $"{currentData.passiveWood} <color={arrowCol}>→ {nextData.passiveWood}</color>/hr";
        if (passiveStoneText) passiveStoneText.text = $"{currentData.passiveStone} <color={arrowCol}>→ {nextData.passiveStone}</color>/hr";
        if (passiveFoodText) passiveFoodText.text = $"{currentData.passiveFood} <color={arrowCol}>→ {nextData.passiveFood}</color>/hr";
        if (passiveDiamondText) passiveDiamondText.text = $"{currentData.passiveDiamonds} <color={arrowCol}>→ {nextData.passiveDiamonds}</color>/hr";
    }

    private IEnumerator ShakePanel()
    {
        Vector3 originalPos = panelRect.anchoredPosition;
        float elapsed = 0f;
        while (elapsed < shakeDuration)
        {
            float x = Random.Range(-1f, 1f) * shakeIntensity;
            float y = Random.Range(-1f, 1f) * shakeIntensity;
            panelRect.anchoredPosition = new Vector2(originalPos.x + x, originalPos.y + y);
            elapsed += Time.deltaTime;
            yield return null;
        }
        panelRect.anchoredPosition = originalPos;
    }

    private void OnActionButtonClicked()
    {
        if (currentRegion.currentState == RegionState.Available || currentRegion.currentState == RegionState.Conquered)
        {
            if (AudioManager.Instance != null) AudioManager.Instance.PlayUI(AudioID.UI_Click);

            // --- ФІКС: Зберігаємо регіон у СТАТИЧНУ змінну, яка переживе зміну сцени! ---
            MissionInitializer.PendingMissionRegion = currentRegion;

            if (GameManager.Instance != null) GameManager.Instance.currentRegion = currentRegion;

            PlayerPrefs.SetInt("IsRegionMission", 1);
            PlayerPrefs.SetInt("RegionBiomeType", (int)currentRegion.regionBiome);
            PlayerPrefs.SetInt("IsRunActive", 1);
            PlayerPrefs.SetInt("IsContinuing", 0);
            PlayerPrefs.Save();

            ClosePanel();
            StartCoroutine(ZoomAndTravelTransition());
        }
    }

    private IEnumerator ZoomAndTravelTransition()
    {
        if (mapViewer != null)
        {
            mapViewer.enabled = false;
            RectTransform mapRect = mapViewer.GetComponent<RectTransform>();

            float duration = 1.2f;
            float elapsed = 0f;
            Vector3 startScale = mapRect.localScale;
            Vector3 targetScale = Vector3.one * 8f;

            Vector2 startPos = mapRect.anchoredPosition;
            Vector2 targetPos = -mapRect.InverseTransformPoint(currentRegionPos) * 8f;

            if (fullScreenCloudOverlay != null)
            {
                fullScreenCloudOverlay.gameObject.SetActive(true);
            }

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                float easeInCubic = t * t * t;

                mapRect.localScale = Vector3.Lerp(startScale, targetScale, easeInCubic);
                mapRect.anchoredPosition = Vector2.Lerp(startPos, targetPos, easeInCubic);

                if (fullScreenCloudOverlay != null)
                {
                    fullScreenCloudOverlay.color = new Color(1, 1, 1, Mathf.Lerp(0f, 1f, easeInCubic));
                }

                yield return null;
            }
        }

        if (GlobalHUD.Instance != null) GlobalHUD.Instance.FadeAndLoadScene("GameScene");
    }

    private IEnumerator AnimatePanel(Vector2 targetPos, float targetAlpha, bool state)
    {
        if (state) { canvasGroup.interactable = true; canvasGroup.blocksRaycasts = true; }

        float elapsed = 0f;
        Vector2 startPos = panelRect.anchoredPosition;
        float startAlpha = canvasGroup.alpha;

        while (elapsed < animationDuration)
        {
            elapsed += Time.deltaTime;
            float smoothT = 1f - Mathf.Pow(1f - (elapsed / animationDuration), 3f);
            panelRect.anchoredPosition = Vector2.Lerp(startPos, targetPos, smoothT);
            canvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, smoothT);
            yield return null;
        }

        panelRect.anchoredPosition = targetPos;
        canvasGroup.alpha = targetAlpha;
        if (!state) { canvasGroup.interactable = false; canvasGroup.blocksRaycasts = false; }
    }

    private string ColorToHex(Color color)
    {
        return "#" + ColorUtility.ToHtmlStringRGB(color);
    }
}